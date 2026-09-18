#!/usr/bin/env python3
"""
Build a canonical *college draft-pool* file for an NFL draft year.

The NCAA draft-class binary always carries 1600 player records: the ~256 real
draftees plus ~1344 "filler" slots representing the rest of the eligible
college pool. Historically those filler slots were block-copied from the 2007
`sample.bin` fixture, so they showed 2006-era college names at the wrong
schools. This scraper produces real, era-correct college players to stamp onto
those slots instead.

Source: sportsdataverse/cfbfastR-data (GitHub-hosted, no API key, nflverse
pattern). For NFL draft year N the relevant college season is N-1 (the draft
in spring N follows the fall N-1 season), so draft-class-{N} pulls
cfb_rosters_{N-1}.csv. Coverage: CFB rosters 2004-2025 -> draft years
2008-2026, all available.

We keep only draft-eligible upperclassmen (class year JR/SR/5th/6th) at real
NCAA-mappable colleges and positions, then round-robin across teams so the
emitted pool is spread evenly (looks like a real draft board, not the first
few alphabetical schools). Output shape matches a canonical draft class so the
compiler's existing CanonicalJson.Load reads it directly.

Usage:
    python build_college_pool.py --year 2018
    python build_college_pool.py --all
"""
import argparse
import csv
import io
import json
import os
import sys
import unicodedata
import urllib.request
from collections import defaultdict

REPO_ROOT = os.path.abspath(os.path.join(os.path.dirname(__file__), "..", ".."))
COLLEGES_JSON = os.path.join(REPO_ROOT, "data", "mappings", "colleges.json")
OUT_DIR = os.path.join(REPO_ROOT, "data", "canonical")

CSV_URL = ("https://raw.githubusercontent.com/sportsdataverse/cfbfastR-data/"
           "main/rosters/csv/cfb_rosters_{season}.csv")

# NFL draft year N <- college season N-1. CFB rosters exist 2004-2025.
DRAFT_YEARS = range(2008, 2027)
MIN_CFB_SEASON, MAX_CFB_SEASON = 2004, 2025

# How many pool players to emit per year. The compiler consumes ~1344; the
# extra headroom covers real-draftee dedup drops and any residual skips.
POOL_TARGET = 1800

# CFB roster position codes -> canonical positions.json keys. NCAA 06
# distinguishes L/R sides for OL/DE/OLB; the canonical schema uses a single
# label (OT/OG/DE/LB) and the compiler defaults to one variant.
POS_MAP = {
    "QB": "QB",
    "RB": "RB", "FB": "FB",
    "WR": "WR", "TE": "TE",
    "OL": "OT", "OT": "OT", "T": "OT",
    "G": "OG", "OG": "OG",
    "C": "C", "LS": "C",            # long snapper -> center (no NCAA06 LS)
    "DL": "DE", "DE": "DE", "EDGE": "DE",
    "DT": "DT", "NT": "DT",
    "LB": "LB", "ILB": "LB", "OLB": "LB", "MLB": "LB",
    "DB": "CB", "CB": "CB",
    "S": "S", "FS": "S", "SS": "S",
    "PK": "K", "K": "K",
    "P": "P",
    # intentionally dropped: NA, "?", ATH, PR (ambiguous / non-mappable)
}

# CFB "year" column (class) -> canonical CollegeYear. Class is used only to
# PRIORITIZE (seniors first), never to exclude: CFB rosters before ~2015 have
# an all-NA class column (e.g. cfb_rosters_2007 is 4695/4698 NA), so a hard
# upperclassman filter would empty the early-year pools. Unknown class defaults
# to SR and sorts last, after every known upperclassman.
YEAR_MAP = {"1": "FR", "2": "SO", "3": "JR", "4": "SR", "5": "GS", "6": "GS"}
YEAR_RANK = {"4": 0, "3": 1, "6": 2, "5": 3, "2": 4, "1": 5}
DEFAULT_RANK = 6              # NA / unknown class -> sorted last
DEFAULT_COLLEGE_YEAR = "SR"


def asciify(s):
    """NFKD-strip accents so names encode cleanly into the ASCII record
    fields (José -> Jose) instead of becoming '?' at compile time."""
    if not s:
        return ""
    return unicodedata.normalize("NFKD", s).encode("ascii", "ignore").decode("ascii").strip()


def load_college_keys():
    """College names that resolve to a REAL NCAA 06 TGID. colleges.json maps
    ~88 names to 255 (explicit N/A / FCS schools absent from the NCAA 06
    catalog); excluding those keeps every pool player at a college that
    actually renders in-game (we have far more upperclassmen than the ~1344
    filler slots need, so dropping N/A colleges costs no coverage)."""
    with open(COLLEGES_JSON, encoding="utf-8") as f:
        cj = json.load(f)
    return {name for name, tgid in cj["map"].items() if tgid != 255}


def fetch_csv(season):
    url = CSV_URL.format(season=season)
    req = urllib.request.Request(url, headers={"User-Agent": "Mozilla/5.0"})
    with urllib.request.urlopen(req, timeout=90) as resp:
        data = resp.read().decode("utf-8", errors="replace")
    return list(csv.DictReader(io.StringIO(data)))


def to_int(v):
    try:
        n = int(float(v))
        return n if n > 0 else None
    except (TypeError, ValueError):
        return None


def build_year(draft_year, college_keys):
    season = draft_year - 1
    if not (MIN_CFB_SEASON <= season <= MAX_CFB_SEASON):
        raise ValueError(f"draft {draft_year} needs CFB season {season}, outside {MIN_CFB_SEASON}-{MAX_CFB_SEASON}")

    rows = fetch_csv(season)

    buckets = defaultdict(list)
    for r in rows:
        team = (r.get("team") or "").strip()
        if team not in college_keys:
            continue
        pos = POS_MAP.get((r.get("position") or "").strip().upper())
        if pos is None:
            continue
        raw_year = (r.get("year") or "").strip()
        cy = YEAR_MAP.get(raw_year, DEFAULT_COLLEGE_YEAR)
        first = asciify(r.get("first_name"))
        last = asciify(r.get("last_name"))
        if not first or not last:
            continue

        meas = {}
        h = to_int(r.get("height"))
        w = to_int(r.get("weight"))
        if h:
            meas["heightIn"] = h
        if w:
            meas["weightLb"] = w

        player = {
            "name": {"first": first, "last": last},
            "position": pos,
            "college": team,
            "collegeYear": cy,
            "redshirt": raw_year in ("5", "6"),
        }
        if meas:
            player["measurables"] = meas
        buckets[team].append((raw_year, last.lower(), first.lower(), player))

    for team in buckets:
        buckets[team].sort(key=lambda t: (YEAR_RANK.get(t[0], DEFAULT_RANK), t[1], t[2]))

    teams = sorted(buckets)
    pool = []
    depth = max((len(v) for v in buckets.values()), default=0)
    for i in range(depth):
        if len(pool) >= POOL_TARGET:
            break
        for team in teams:
            if i < len(buckets[team]):
                pool.append(buckets[team][i][3])
                if len(pool) >= POOL_TARGET:
                    break

    return {
        "schema": "ncaa-college-pool/v1",
        "year": draft_year,
        "sourceSeason": season,
        "source": "sportsdataverse/cfbfastR-data",
        "players": pool,
    }


def main():
    ap = argparse.ArgumentParser(description="Build canonical college draft-pool JSON for filler slots.")
    g = ap.add_mutually_exclusive_group(required=True)
    g.add_argument("--year", type=int, help="NFL draft year (2008-2026)")
    g.add_argument("--all", action="store_true", help="Build every draft year 2008-2026")
    ap.add_argument("--out-dir", default=OUT_DIR)
    args = ap.parse_args()

    college_keys = load_college_keys()
    os.makedirs(args.out_dir, exist_ok=True)
    years = list(DRAFT_YEARS) if args.all else [args.year]

    for y in years:
        try:
            doc = build_year(y, college_keys)
        except Exception as e:  # noqa: BLE001 - report and continue in --all
            print(f"  {y}: ERROR {e}", file=sys.stderr)
            if not args.all:
                return 1
            continue
        out = os.path.join(args.out_dir, f"college-pool-{y}.json")
        with open(out, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2)
        teams = len({p["college"] for p in doc["players"]})
        print(f"  {y}: {len(doc['players'])} players across {teams} colleges (from CFB {doc['sourceSeason']}) -> {out}")

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
