#!/usr/bin/env python3
"""
Build a canonical NFL roster JSON for a single season year by combining:

  * nflverse `rosters` release  - one row per (player, season, team)
                                   with name, jersey, age, height, weight,
                                   college, years_exp, position
  * our existing per-year Madden launch-ratings JSONs in
    data/raw/madden-ratings/madden{version}-{season}.json

The output mirrors NcaaDraftEditor.Canonical.CanonicalRoster:

  {
    "schema": "ncaa-madden-roster/v1",
    "nflSeason": 2017,
    "teams": [
      {
        "tgId": 1, "abbreviation": "CHI", "city": "Chicago", "name": "Bears",
        "players": [
          {
            "name": { "first": "Mitchell", "last": "Trubisky" },
            "position": "QB", "jerseyNumber": 10, "age": 23, "yearsPro": 0,
            "college": "North Carolina",
            "measurables": { "heightIn": 74, "weightLb": 222 },
            "ratings": { "ovr": 75, "spd": 78, ... }
          },
          ...
        ]
      },
      ...
    ]
  }

The TEAM table in Madden 08 PS2's roster file is alphabetical by mascot,
so we use that order to assign TGID:
  Bears=1, Bengals=2, Bills=3, Broncos=4, Browns=5, ...

Stdlib only. Madden ratings are optional - rows that don't have a match
get `ratings: null` (the compiler falls back to defaults or a model).

Usage:
    python scrapers/nflverse/build_roster.py 2017
"""
from __future__ import annotations

import csv
import json
import re
import sys
import urllib.request
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache"
CANONICAL_DIR = REPO_ROOT / "data" / "canonical"
MADDEN_DIR = REPO_ROOT / "data" / "raw" / "madden-ratings"

ROSTERS_URL_TEMPLATE = "https://github.com/nflverse/nflverse-data/releases/download/rosters/roster_{year}.csv"

UA = "Mozilla/5.0 (X11; Linux x86_64) NCAA-Draft-Class-Editor-research/1.0"

# Bears=1, Bengals=2, alphabetical by mascot in Madden 08's TEAM table.
# Modern nflverse rosters use the CURRENT 3-letter code (LA for Rams since
# 2016, LAC for Chargers since 2017, LV for Raiders since 2020); the value
# stored is whichever code matches Madden 08's 2007 baseline.
TEAM_TABLE = [
    (1, "CHI", "Chicago", "Bears"),
    (2, "CIN", "Cincinnati", "Bengals"),
    (3, "BUF", "Buffalo", "Bills"),
    (4, "DEN", "Denver", "Broncos"),
    (5, "CLE", "Cleveland", "Browns"),
    (6, "TB",  "Tampa Bay", "Buccaneers"),
    (7, "ARI", "Arizona", "Cardinals"),
    (8, "SD",  "San Diego", "Chargers"),
    (9, "KC",  "Kansas City", "Chiefs"),
    (10, "IND", "Indianapolis", "Colts"),
    (11, "DAL", "Dallas", "Cowboys"),
    (12, "MIA", "Miami", "Dolphins"),
    (13, "PHI", "Philadelphia", "Eagles"),
    (14, "ATL", "Atlanta", "Falcons"),
    (15, "SF",  "San Francisco", "49ers"),
    (16, "NYG", "New York", "Giants"),
    (17, "JAX", "Jacksonville", "Jaguars"),
    (18, "NYJ", "New York", "Jets"),
    (19, "DET", "Detroit", "Lions"),
    (20, "GB",  "Green Bay", "Packers"),
    (21, "CAR", "Carolina", "Panthers"),
    (22, "NE",  "New England", "Patriots"),
    (23, "OAK", "Oakland", "Raiders"),
    (24, "STL", "St. Louis", "Rams"),
    (25, "BAL", "Baltimore", "Ravens"),
    (26, "WAS", "Washington", "Redskins"),
    (27, "NO",  "New Orleans", "Saints"),
    (28, "SEA", "Seattle", "Seahawks"),
    (29, "PIT", "Pittsburgh", "Steelers"),
    (30, "HOU", "Houston", "Texans"),
    (31, "TEN", "Tennessee", "Titans"),
    (32, "MIN", "Minnesota", "Vikings"),
]

# Team-abbreviation aliases that nflverse uses across eras. Map back to
# Madden 08's 2007 codes so the join works.
ABBREV_ALIASES = {
    # Relocations (post-2015)
    "LA": "STL",       # LA Rams (2016+) -> St. Louis Rams in M08
    "LAR": "STL",
    "LAC": "SD",       # LA Chargers (2017+) -> San Diego in M08
    "LV": "OAK",       # LV Raiders (2020+) -> Oakland in M08
    "WSH": "WAS",      # nflverse variant
    # Older-era nflverse codes (2008-2015 used different abbreviations)
    "ARZ": "ARI",
    "BLT": "BAL",
    "CLV": "CLE",
    "HST": "HOU",
    "SL":  "STL",
}

# NFL position label -> canonical position (matches positions.json + Madden's PPOS)
POSITION_MAP = {
    "QB": "QB", "RB": "RB", "HB": "RB", "FB": "FB",
    "WR": "WR", "TE": "TE",
    "OT": "OT", "T": "OT", "OL": "OT", "LT": "OT", "RT": "OT",
    "OG": "OG", "G": "OG", "LG": "OG", "RG": "OG",
    "C": "C", "LS": "C",
    "DE": "DE", "EDGE": "DE", "DT": "DT", "NT": "DT", "DL": "DT",
    "LB": "LB", "OLB": "LB", "MLB": "LB", "ILB": "LB",
    "CB": "CB", "DB": "CB",
    "S": "S", "SS": "S", "FS": "S", "SAF": "S",
    "K": "K", "PK": "K", "P": "P",
}


def fetch(url: str, cache_path: Path) -> bytes:
    if cache_path.exists():
        return cache_path.read_bytes()
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    print(f"  GET {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
    cache_path.write_bytes(data)
    return data


def to_int(v: str) -> int | None:
    s = (v or "").strip()
    if not s:
        return None
    try:
        return int(float(s))
    except ValueError:
        return None


def parse_height(s: str) -> int | None:
    """nflverse rosters height: integer inches OR 'feet-inches'."""
    if not s:
        return None
    s = s.strip()
    if "-" in s:
        feet, inches = s.split("-")
        try:
            return int(feet) * 12 + int(inches)
        except ValueError:
            return None
    return to_int(s)


def normalize_team(code: str) -> str:
    return ABBREV_ALIASES.get(code.upper(), code.upper())


_SUFFIX_TOKENS = ("jr", "sr", "ii", "iii", "iv", "v")


def name_key(first: str, last: str) -> tuple[str, str]:
    """Normalized lookup key for joining nflverse rosters to Madden ratings.

    Madden ratings often store the name with a suffix ('Patrick Mahomes II'),
    while nflverse stores the bare name ('Patrick Mahomes'). Strip trailing
    suffix tokens from `last` so both sides hash to the same key. Also
    lowercase and trim trailing periods so 'Jr.' == 'Jr' == 'JR'."""
    f = (first or "").strip().lower()
    l = (last or "").strip().lower()
    # Strip a trailing suffix token (and its preceding space) if present.
    for suffix in _SUFFIX_TOKENS:
        for sep in (" " + suffix + ".", " " + suffix):
            if l.endswith(sep):
                l = l[: -len(sep)].strip()
                break
    return (f, l)


def split_name(full: str, first: str, last: str) -> tuple[str, str]:
    """Prefer first_name/last_name columns when present, fall back to splitting full_name."""
    if first.strip() and last.strip():
        return first.strip(), last.strip()
    parts = full.split()
    if len(parts) >= 2 and parts[-1] in ("Jr.", "Sr.", "II", "III", "IV", "V"):
        return " ".join(parts[:-2]), parts[-2] + " " + parts[-1]
    if len(parts) == 0:
        return "", ""
    return parts[0], " ".join(parts[1:])


FIRST_NAME_ALIASES = (
    "FIRSTNAME", "FIRST NAME", "First Name", "First", "FirstName", "first_name")
LAST_NAME_ALIASES = (
    "LASTNAME", "LAST NAME", "Last Name", "Last", "LastName", "last_name")
FULL_NAME_ALIASES = ("Name", "Full Name", "FullName", "Player Name")


def _clean_str(value) -> str:
    """Strip whitespace + the non-breaking-space chars that appear in some
    Madden XLSX exports (e.g. M10 'Sendlein\\xa0\\xa0')."""
    if value is None:
        return ""
    s = str(value).strip()
    return s.replace("\xa0", "").strip()


def load_madden_ratings_index(season: int) -> dict[tuple[str, str], dict]:
    """Build a name-key -> player-record index from the Madden file for the
    season (if we have one). Key is (first_lower, last_lower). Madden XLSX
    schemas vary wildly by year so we try a long list of column-name aliases."""
    candidates = list(MADDEN_DIR.glob(f"madden*-{season}.json"))
    if not candidates:
        return {}
    path = candidates[0]
    print(f"  Joining with {path.name}", file=sys.stderr)
    data = json.loads(path.read_text(encoding="utf-8"))
    index: dict[tuple[str, str], dict] = {}
    for team_slug, players in data.get("teams", {}).items():
        for p in players:
            if not isinstance(p, dict):
                continue
            first = ""
            for k in FIRST_NAME_ALIASES:
                if k in p:
                    first = _clean_str(p[k])
                    if first:
                        break
            last = ""
            for k in LAST_NAME_ALIASES:
                if k in p:
                    last = _clean_str(p[k])
                    if last:
                        break
            if not (first and last):
                for k in FULL_NAME_ALIASES:
                    if k in p:
                        full = _clean_str(p[k])
                        if not full:
                            continue
                        parts = full.split(" ", 1)
                        first = first or parts[0]
                        last = last or (parts[1] if len(parts) > 1 else "")
                        if first and last:
                            break
            if not (first and last):
                continue
            # Skip header-row leakage (e.g. M21's Pro Bowl table has a row where
            # the first/last fields contain "Position"/"Name" rather than real names)
            if first in ("Position", "Name") and last in ("Name", "Overall Rating"):
                continue
            key = name_key(first, last)
            if key not in index:
                index[key] = p
    return index


def extract_ratings(madden_rec: dict) -> dict[str, int]:
    """Pull a normalized rating block out of a Madden player record. Each
    rating is named differently across Madden years - long alias lists."""
    out: dict[str, int] = {}
    field_aliases = [
        ("ovr", ["Overall", "OVERALL", "OVR", "OverallRating", "Overall Rating"]),
        ("spd", ["Speed", "SPEED", "SPD", "SpeedRating"]),
        ("acc", ["Acceleration", "ACCELERATION", "ACC", "AccelerationRating"]),
        ("agi", ["Agility", "AGILITY", "AGI", "AgilityRating"]),
        ("str", ["Strength", "STRENGTH", "STR", "StrengthRating"]),
        ("awr", ["Awareness", "AWARENESS", "AWR", "AwarenessRating"]),
        ("cth", ["Catch", "Catching", "CATCHING", "CTH", "CatchingRating"]),
        ("car", ["Carrying", "CARRYING", "CAR", "CarryingRating"]),
        ("thp", ["Throw Power", "THROWPOWER", "THP", "ThrowPowerRating"]),
        ("tha", ["Throw Accuracy", "THROWACCURACY", "THA", "ThrowAccuracyRating",
                 "Throw Accuracy Short", "ThrowAccuracyShortRating"]),
        ("kpw", ["Kick Power", "KICKPOWER", "KPW", "KickPowerRating"]),
        ("kac", ["Kick Accuracy", "KICKACCURACY", "KAC", "KickAccuracyRating"]),
        ("btk", ["Break Tackle", "BREAKTACKLE", "BTK", "BreakTackleRating",
                 "Elusiveness", "ELUSIVENESS"]),
        ("tak", ["Tackle", "TACKLE", "TAK", "TackleRating"]),
        ("pow", ["Hit Power", "HITPOWER", "POW", "HitPowerRating"]),
        ("pbk", ["Pass Block", "PASSBLOCK", "PBK", "PassBlockRating"]),
        ("rbk", ["Run Block", "RUNBLOCK", "RBK", "RunBlockRating"]),
        ("jmp", ["Jumping", "JUMPING", "JMP", "JumpingRating"]),
        ("inj", ["Injury", "INJURY", "INJ", "InjuryRating"]),
        ("sta", ["Stamina", "STAMINA", "STA", "StaminaRating"]),
    ]
    for out_key, src_keys in field_aliases:
        for k in src_keys:
            if k in madden_rec:
                v = madden_rec[k]
                try:
                    val = int(float(v))
                except (TypeError, ValueError):
                    continue
                # Clamp to valid 0-99 range (M22 etc. occasionally has 100 for
                # superstars; cap at 99 since that's NCAA-side max too).
                out[out_key] = max(0, min(99, val))
                break
    return out


def build(season: int) -> dict[str, Any]:
    url = ROSTERS_URL_TEMPLATE.format(year=season)
    rosters_path = CACHE_DIR / f"roster_{season}.csv"
    fetch(url, rosters_path)

    madden_index = load_madden_ratings_index(season)
    print(f"  {len(madden_index)} Madden players indexed", file=sys.stderr)

    by_team: dict[str, list[dict[str, Any]]] = {}
    with rosters_path.open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if str(row.get("season", "")) != str(season):
                continue
            team_code = normalize_team(row.get("team", ""))
            if not team_code:
                continue
            first, last = split_name(
                row.get("full_name", ""), row.get("first_name", ""), row.get("last_name", ""))
            if not first or not last:
                continue

            raw_pos = (row.get("position") or "").strip().upper()
            canonical_pos = POSITION_MAP.get(raw_pos, raw_pos)

            player: dict[str, Any] = {
                "name": {"first": first, "last": last},
                "position": canonical_pos,
                "college": row.get("college", "").strip(),
            }
            jn = to_int(row.get("jersey_number", ""))
            if jn is not None and 0 <= jn <= 99:
                player["jerseyNumber"] = jn
            age = to_int(row.get("age", ""))
            if age is None:
                # nflverse rosters carry birth_date (YYYY-MM-DD), not age.
                # Compute age as of NFL season opening week (~Sep 1 of season).
                birth = row.get("birth_date", "").strip()
                if birth and len(birth) >= 4:
                    try:
                        by, bm, bd = (int(x) for x in birth.split("-")[:3])
                        # Player's age at NFL season start.
                        age = season - by - (1 if (bm, bd) > (9, 1) else 0)
                    except (ValueError, AttributeError):
                        age = None
            if age is not None and 18 <= age <= 50:
                player["age"] = age
            yp = to_int(row.get("years_exp", "")) or to_int(row.get("years_pro", ""))
            # NFL careers cap around 25 yrs (Tom Brady's full career was 23).
            # Anything higher is bad source data.
            if yp is not None and 0 <= yp <= 25:
                player["yearsPro"] = yp

            measurables = {}
            h = parse_height(row.get("height", ""))
            w = to_int(row.get("weight", ""))
            if h is not None and 60 <= h <= 90:
                measurables["heightIn"] = h
            if w is not None and 140 <= w <= 400:
                measurables["weightLb"] = w
            if measurables:
                player["measurables"] = measurables

            # Join with Madden ratings if we have them
            madden_rec = madden_index.get(name_key(first, last))
            if madden_rec:
                ratings = extract_ratings(madden_rec)
                if ratings:
                    player["ratings"] = ratings

            # Deduplicate: nflverse sometimes lists a player twice on the same
            # team (e.g. Correll Buckhalter on PHI 2008 has 2 identical rows).
            # Skip when an identical (first, last, jersey, position) already
            # exists for this team. Different real players with the same name
            # (e.g. two Roy Williams on DAL 2008 - WR + S) survive because
            # their positions differ.
            team_list = by_team.setdefault(team_code, [])
            dupe_key = (first.lower(), last.lower(),
                        player.get("jerseyNumber"), canonical_pos)
            if any((p["name"]["first"].lower(), p["name"]["last"].lower(),
                    p.get("jerseyNumber"), p["position"]) == dupe_key
                   for p in team_list):
                continue
            team_list.append(player)

    # Assemble teams in TGID order
    teams_out: list[dict[str, Any]] = []
    for tgid, abbr, city, name in TEAM_TABLE:
        roster = by_team.get(abbr, [])
        teams_out.append({
            "tgId": tgid,
            "abbreviation": abbr,
            "city": city,
            "name": name,
            "players": roster,
        })

    return {
        "schema": "ncaa-madden-roster/v1",
        "nflSeason": season,
        "teams": teams_out,
    }


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <nfl_season>", file=sys.stderr)
        return 2
    season = int(sys.argv[1])
    result = build(season)

    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    out_path = CANONICAL_DIR / f"roster-{season}.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)

    total = sum(len(t["players"]) for t in result["teams"])
    per_team_avg = total / max(1, len(result["teams"]))
    print(f"Wrote {out_path}: {total} players across {len(result['teams'])} teams "
          f"({per_team_avg:.1f} avg)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
