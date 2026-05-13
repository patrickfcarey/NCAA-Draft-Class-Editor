#!/usr/bin/env python3
"""
Build a canonical draft class JSON for a given NFL Draft year using nflverse data.

Sources (CSV from github.com/nflverse):
- players.csv  : primary source. Has draft_year/round/pick/team, names, height,
                 weight, college_name, birth_date, jersey_number. Coverage 2007-2025.
- combine.csv  : joined on pfr_id. Has 40, bench, vertical, broad_jump, cone, shuttle.

draft_picks.csv was considered as a primary source but its 2023 coverage is
incomplete (102 of ~258 picks present), so we use players.csv which has the
full draft for every year through 2025.

Output: data/canonical/draft-class-{year}.json (canonical schema, no Madden ratings yet)

Stdlib only. No external deps.

Usage:
    python build_draft_class.py 2018
"""
from __future__ import annotations

import csv
import json
import sys
import urllib.request
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache"
CANONICAL_DIR = REPO_ROOT / "data" / "canonical"

SOURCES = {
    "combine.csv": "https://github.com/nflverse/nflverse-data/releases/download/combine/combine.csv",
    "players.csv": "https://github.com/nflverse/nflverse-data/releases/download/players/players.csv",
}

# NFL position label -> our canonical schema label (the subset NCAA 06's PPOS knows about)
POSITION_MAP = {
    "QB": "QB",
    "RB": "RB", "HB": "RB", "FB": "FB",
    "WR": "WR", "TE": "TE",
    "OT": "OT", "T": "OT", "OL": "OT",
    "OG": "OG", "G": "OG",
    "C": "C", "LS": "C",
    "DE": "DE", "EDGE": "DE",
    "DT": "DT", "NT": "DT", "DL": "DT",
    "LB": "LB", "OLB": "LB", "MLB": "LB", "ILB": "LB",
    "CB": "CB", "DB": "CB",
    "S": "S", "SS": "S", "FS": "S", "SAF": "S",
    "K": "K", "PK": "K",
    "P": "P",
}


def download_if_missing(filename: str, url: str) -> Path:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    path = CACHE_DIR / filename
    if not path.exists():
        print(f"Downloading {url} -> {path.name}", file=sys.stderr)
        urllib.request.urlretrieve(url, path)
    return path


def parse_height_inches(s: str) -> int | None:
    """combine.csv uses '6-1' format; players.csv already has inches as int."""
    if not s:
        return None
    if "-" in s:
        feet, inches = s.split("-")
        return int(feet) * 12 + int(inches)
    return int(s) if s.isdigit() else None


def safe_int(s: str) -> int | None:
    s = (s or "").strip()
    return int(s) if s else None


def safe_float(s: str) -> float | None:
    s = (s or "").strip()
    return float(s) if s else None


def primary_college(college_name: str) -> str:
    """'Oklahoma; Texas Tech' -> 'Oklahoma' (first listed is the school they finished at)."""
    if not college_name:
        return ""
    return college_name.split(";")[0].strip()


def build(year: int) -> dict[str, Any]:
    combine_path = download_if_missing("combine.csv", SOURCES["combine.csv"])
    players_path = download_if_missing("players.csv", SOURCES["players.csv"])

    combine_by_pfr: dict[str, dict[str, str]] = {}
    with combine_path.open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row.get("season") == str(year) and row.get("pfr_id"):
                combine_by_pfr[row["pfr_id"]] = row

    # Iterate players.csv directly; pick those drafted in the target year.
    # Sort by draft pick number (some rows may not have a pick, e.g. supplemental).
    rows_for_year: list[dict[str, str]] = []
    with players_path.open(encoding="utf-8") as f:
        for row in csv.DictReader(f):
            if row.get("draft_year") == str(year) and row.get("draft_round"):
                rows_for_year.append(row)
    rows_for_year.sort(key=lambda r: (safe_int(r.get("draft_round", "")) or 999,
                                       safe_int(r.get("draft_pick", "")) or 999))

    players: list[dict[str, Any]] = []
    for bio in rows_for_year:
        pfr_id = bio.get("pfr_id", "")
        combine = combine_by_pfr.get(pfr_id, {})

        # Prefer common_first_name (player's playing name: "Tremaine", "Josh", "DJ")
        # over first_name (legal name: "Fe'Zahn", "Joshua", "Denniston"). Game UIs and
        # commentary use the common name; legal names confuse anyone looking at the roster.
        first = (bio.get("common_first_name") or "").strip() or bio.get("first_name", "")
        last = bio.get("last_name", "")
        raw_pos = bio.get("position", "")
        position = POSITION_MAP.get(raw_pos, raw_pos)

        height_in = safe_int(bio.get("height", "")) or parse_height_inches(combine.get("ht", ""))
        weight_lb = safe_int(bio.get("weight", "")) or safe_int(combine.get("wt", ""))
        college = primary_college(bio.get("college_name", "")) or combine.get("school", "")

        player: dict[str, Any] = {
            "name": {"first": first, "last": last},
            "position": position,
            "college": college,
            "collegeYear": "SR",   # nflverse doesn't track; default
            "redshirt": False,     # nflverse doesn't track; default
            "draft": {
                "round": safe_int(bio.get("draft_round", "")),
                "pick": safe_int(bio.get("draft_pick", "")),
                "team": bio.get("draft_team", ""),
            },
        }

        measurables: dict[str, Any] = {}
        if height_in is not None:
            measurables["heightIn"] = height_in
        if weight_lb is not None:
            measurables["weightLb"] = weight_lb
        if measurables:
            player["measurables"] = measurables

        combine_block: dict[str, Any] = {}
        for src_key, canon_key, parser in [
            ("forty", "fortyYd", safe_float),
            ("bench", "bench", safe_int),
            ("vertical", "verticalIn", safe_float),
            ("broad_jump", "broadIn", safe_int),
            ("shuttle", "shuttle", safe_float),
            ("cone", "threeCone", safe_float),
        ]:
            val = parser(combine.get(src_key, ""))
            if val is not None:
                combine_block[canon_key] = val
        if combine_block:
            player["combine"] = combine_block

        if bio.get("birth_date"):
            player["birthDate"] = bio["birth_date"]

        players.append(player)

    return {
        "schema": "ncaa-draft-class/v1",
        "year": year,
        "players": players,
    }


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <year>", file=sys.stderr)
        return 2
    year = int(sys.argv[1])

    result = build(year)
    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    out_path = CANONICAL_DIR / f"draft-class-{year}.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False)
    print(f"Wrote {out_path}: {len(result['players'])} players", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
