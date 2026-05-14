#!/usr/bin/env python3
"""
Build per-season + career NFL stats per player, sourced from nflverse's
`stats_player_reg_{year}.parquet` releases (1999-2025), and emit canonical
per-target-year JSON for the Madden franchise compiler.

These populate the franchise TDB's career stats tables (PCOF/PCDE/PCKI/...)
and most-recent-season tables (PSOF/PSDE/PSKI/...) so Madden's player
profile screens show real historical stats instead of the blank state
we land in after Pass 4 of MaddenRosterCompiler clears them.

Output: data/canonical/stats-{year}.json — one file per target NFL season.
Each player entry has a `career` block (cumulative through year-1) plus a
`seasons` block keyed by season year (covering year-5 through year-1 by
default; older seasons are aggregated into career-only).

Stdlib + pyarrow. Source data refreshes weekly during the NFL season.

Usage:
    python scrapers/nflverse/build_stats.py             # 2008-2026 inclusive
    python scrapers/nflverse/build_stats.py 2018        # one year only
    python scrapers/nflverse/build_stats.py --refresh   # force re-download
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from collections import defaultdict
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache" / "stats"
CANONICAL_DIR = REPO_ROOT / "data" / "canonical"

URL_TEMPLATE = "https://github.com/nflverse/nflverse-data/releases/download/stats_player/stats_player_reg_{year}.parquet"

# Earliest season we pull. Career totals beyond this date will be zero in
# our data even if the player kept playing - but for any player whose career
# starts ≥1999 we get the full career.
CAREER_FLOOR = 1999

DEFAULT_YEARS = list(range(2008, 2027))

# Fields we extract from the parquet and aggregate. Each maps a Madden
# canonical key -> nflverse column name. Only stats Madden's PCOF/PCDE/etc.
# can hold; we skip EPA / fantasy_points / situational variants.
STAT_COLUMNS = {
    # Passing
    "passComp":   "completions",
    "passAtt":    "attempts",
    "passYards":  "passing_yards",
    "passTDs":    "passing_tds",
    "passInts":   "passing_interceptions",
    "sacksTaken": "sacks_suffered",
    # Rushing
    "rushAtt":    "carries",
    "rushYards":  "rushing_yards",
    "rushTDs":    "rushing_tds",
    # Receiving
    "receptions": "receptions",
    "targets":    "targets",
    "recYards":   "receiving_yards",
    "recTDs":     "receiving_tds",
    # Fumbles (combine sack/rush/rec for total)
    "fumbles":    None,  # synthesized below
    # Defense
    "tacklesSolo":  "def_tackles_solo",
    "tacklesAst":   "def_tackle_assists",
    "sacks":        "def_sacks",
    "defInts":      "def_interceptions",
    "forcedFum":    "def_fumbles_forced",
    "fumRecov":     "fumble_recovery_own",
    "defTDs":       "def_tds",
    "passDefended": "def_pass_defended",
    # Kicking
    "fgMade":  "fg_made",
    "fgAtt":   "fg_att",
    "patMade": "pat_made",
    "patAtt":  "pat_att",
    # Returns
    "kickReturns":  "kickoff_returns",
    "kickRetYds":   "kickoff_return_yards",
    "puntReturns":  "punt_returns",
    "puntRetYds":   "punt_return_yards",
    # Generic
    "games":   "games",
}

FUMBLE_SOURCES = ("sack_fumbles", "rushing_fumbles", "receiving_fumbles")


def fetch_parquet(year: int, refresh: bool = False) -> Path:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    cache = CACHE_DIR / f"stats_player_reg_{year}.parquet"
    if cache.exists() and not refresh:
        return cache
    url = URL_TEMPLATE.format(year=year)
    print(f"  $ curl {url}", file=sys.stderr)
    urllib.request.urlretrieve(url, cache)
    return cache


def load_year(year: int) -> list[dict]:
    import pyarrow.parquet as pq
    cache = fetch_parquet(year)
    table = pq.read_table(cache).to_pylist()
    return table


def aggregate_player_row(rows_for_player: list[dict]) -> dict:
    """Multiple rows per player can exist within a single season if they
    changed teams mid-year. Sum the stats and keep the last-known team."""
    out: dict = {}
    fumbles = 0
    for r in rows_for_player:
        for k, src in STAT_COLUMNS.items():
            if src is None:
                continue
            v = r.get(src)
            if v is None:
                continue
            try:
                out[k] = out.get(k, 0) + int(round(float(v)))
            except (TypeError, ValueError):
                continue
        for fs in FUMBLE_SOURCES:
            v = r.get(fs)
            if v is None:
                continue
            try:
                fumbles += int(round(float(v)))
            except (TypeError, ValueError):
                continue
    if fumbles:
        out["fumbles"] = fumbles
    return {k: v for k, v in out.items() if v}


def build(target_year: int, all_year_rows: dict[int, list[dict]]) -> dict:
    """Build canonical stats for `target_year`. Players' careers = sum of all
    seasons strictly before target_year. Seasons block holds last 5 seasons."""
    # Group rows by (player_id, season) -> list of rows
    by_player_season: dict[tuple[str, int], list[dict]] = defaultdict(list)
    by_player_name: dict[str, str] = {}
    for season, rows in all_year_rows.items():
        if season >= target_year:
            continue
        for r in rows:
            pid = r.get("player_id")
            if not pid:
                continue
            by_player_season[(pid, season)].append(r)
            disp = r.get("player_display_name") or r.get("player_name")
            if disp:
                by_player_name[pid] = disp

    # Aggregate per (player, season) then roll up career.
    season_stats: dict[tuple[str, int], dict] = {}
    career: dict[str, dict] = {}
    for (pid, season), rows in by_player_season.items():
        agg = aggregate_player_row(rows)
        if not agg:
            continue
        season_stats[(pid, season)] = agg
        career_agg = career.setdefault(pid, {})
        for k, v in agg.items():
            career_agg[k] = career_agg.get(k, 0) + v

    # Emit one entry per player with non-zero career stats.
    players_out = []
    seasons_window = range(max(CAREER_FLOOR, target_year - 5), target_year)
    for pid, career_agg in career.items():
        seasons_block = {}
        for season in seasons_window:
            s = season_stats.get((pid, season))
            if s:
                seasons_block[str(season)] = s
        players_out.append({
            "gsisId": pid,
            "name": by_player_name.get(pid, ""),
            "career": career_agg,
            "seasons": seasons_block,
        })

    # Sort by career passing yards desc as a deterministic, useful order for diffing.
    players_out.sort(key=lambda p: -(p["career"].get("passYards", 0) + p["career"].get("rushYards", 0) + p["career"].get("recYards", 0)))

    return {
        "schema": "ncaa-madden-stats/v1",
        "nflSeason": target_year,
        "source": "nflverse stats_player_reg (regular season aggregates from nflfastR pbp)",
        "careerThroughInclusive": target_year - 1,
        "seasonsCovered": [s for s in seasons_window],
        "players": players_out,
    }


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("year", nargs="?", type=int, help="Build one target season; default: all 2008-2026")
    p.add_argument("--refresh", action="store_true", help="Force re-download of every year's parquet")
    args = p.parse_args()

    years_to_build = [args.year] if args.year else DEFAULT_YEARS

    # Load every season needed: CAREER_FLOOR..(max target year - 1)
    max_target = max(years_to_build)
    load_years = range(CAREER_FLOOR, max_target)
    print(f"Loading {len(list(load_years))} season parquets ({CAREER_FLOOR}..{max_target-1})", file=sys.stderr)
    all_rows: dict[int, list[dict]] = {}
    for y in load_years:
        try:
            all_rows[y] = load_year(y)
        except Exception as e:
            print(f"  WARNING: failed to load {y}: {e}", file=sys.stderr)

    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    summary = []
    for y in years_to_build:
        doc = build(y, all_rows)
        out = CANONICAL_DIR / f"stats-{y}.json"
        with open(out, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
        summary.append((y, len(doc["players"]), out.name))

    print("\n=== Summary ===")
    print(f"{'Year':<6}{'#players':<10}file")
    for y, n, fn in summary:
        print(f"{y:<6}{n:<10}{fn}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
