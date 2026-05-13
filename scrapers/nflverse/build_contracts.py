#!/usr/bin/env python3
"""
Build canonical NFL contract data per season by flattening the daily-refreshed
nflverse `historical_contracts.parquet` release (which mirrors OvertheCap.com,
bypassing the Cloudflare protection on otc directly).

Each row of the parquet is a full multi-year contract (player + signing year +
length + per-year breakdown). We expand that into one row per (player, season)
holding the current-year cap economy:

  {
    "schema": "ncaa-madden-contracts/v1",
    "nflSeason": 2018,
    "players": [
      {
        "name": "Aaron Rodgers", "team": "GB", "position": "QB",
        "otcId": 1085, "gsisId": "00-0023459",
        "capHitMillions": 20.4, "baseSalaryMillions": 1.1,
        "proratedBonusMillions": 7.55, "rosterBonusMillions": 11.0,
        "cashPaidMillions": 25.0,
        "yearsRemaining": 2, "totalContractYears": 5, "yearSigned": 2013
      },
      ...
    ]
  }

`capHitMillions` is the field most likely to drive in-game cap display when
written to PCSA (after $M -> $10K unit conversion). Per-year breakdowns
(base/bonus) flow into PSA0/PSB0 of the current year; future-year breakdowns
flow into PSA1..PSA{N-1} / PSB1..PSB{N-1} via the compiler.

Stdlib + pyarrow only. Source data refreshes daily; cached under
scrapers/nflverse/cache/ for repeatability across years built in one batch.

Usage:
    python scrapers/nflverse/build_contracts.py             # 2008-2026 inclusive
    python scrapers/nflverse/build_contracts.py 2018        # one year only
    python scrapers/nflverse/build_contracts.py --refresh   # force re-download
"""
from __future__ import annotations

import argparse
import json
import sys
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache"
CANONICAL_DIR = REPO_ROOT / "data" / "canonical"

PARQUET_URL = "https://github.com/nflverse/nflverse-data/releases/download/contracts/historical_contracts.parquet"
PARQUET_CACHE = CACHE_DIR / "historical_contracts.parquet"

DEFAULT_YEARS = list(range(2008, 2027))

# Map nflverse team mascot -> our 3-letter abbreviation (matches the M08 TGID
# ordering used by build_roster.py and the canonical roster JSONs).
TEAM_ABBREV = {
    "Bears": "CHI", "Bengals": "CIN", "Bills": "BUF", "Broncos": "DEN",
    "Browns": "CLE", "Buccaneers": "TB", "Cardinals": "ARI",
    "Chargers": "LAC", "Chiefs": "KC", "Colts": "IND", "Commanders": "WAS",
    "Cowboys": "DAL", "Dolphins": "MIA", "Eagles": "PHI", "Falcons": "ATL",
    "49ers": "SF", "Giants": "NYG", "Jaguars": "JAX", "Jets": "NYJ",
    "Lions": "DET", "Packers": "GB", "Panthers": "CAR", "Patriots": "NE",
    "Raiders": "LV", "Rams": "LAR", "Ravens": "BAL", "Redskins": "WAS",
    "Saints": "NO", "Seahawks": "SEA", "Steelers": "PIT", "Texans": "HOU",
    "Titans": "TEN", "Vikings": "MIN",
}


def fetch_parquet(refresh: bool = False) -> Path:
    CACHE_DIR.mkdir(parents=True, exist_ok=True)
    if PARQUET_CACHE.exists() and not refresh:
        return PARQUET_CACHE
    print(f"  $ curl {PARQUET_URL} -> {PARQUET_CACHE}", file=sys.stderr)
    urllib.request.urlretrieve(PARQUET_URL, PARQUET_CACHE)
    return PARQUET_CACHE


def load_contracts(parquet_path: Path) -> list[dict]:
    """Read the parquet and return list of contract dicts (top-level + cols)."""
    import pyarrow.parquet as pq  # imported lazily so other scrapers don't need it
    table = pq.read_table(parquet_path)
    return table.to_pylist()


def normalize_team(team_str: str | None) -> str:
    """nflverse stores team as mascot ('Bears') or sometimes joined like 'GB/NYJ'
    for mid-season trades. We take the first segment."""
    if not team_str:
        return ""
    first = team_str.split("/")[0].strip()
    return TEAM_ABBREV.get(first, first)


def expand_to_year_with_active(player_row: dict, active: dict, year: int) -> dict | None:
    """Find the cap-history row for `year` in the player's `cols`. `player_row`
    is any contract row for this player (cols is identical across them).
    `active` is the contract whose terms describe the deal in effect (used for
    year_signed / years / total value)."""
    cols = player_row.get("cols") or []
    for col in cols:
        try:
            col_year = int(col.get("year"))
        except (TypeError, ValueError):
            continue
        if col_year != year:
            continue
        signed = active.get("year_signed") or year
        total_years = active.get("years") or 0
        # Count actual future years with a cap obligation in the career
        # history — covers restructures / extensions that the parquet doesn't
        # represent as separate contract rows (e.g. Brady's 2018 cap exists
        # but his "current" contract record technically ended in 2017).
        years_remaining = 0
        for col2 in cols:
            try:
                cy = int(col2.get("year"))
            except (TypeError, ValueError):
                continue
            if cy >= year and col2.get("cap_number") not in (None, 0):
                years_remaining += 1
        team_col = col.get("team") or player_row.get("team")
        return {
            "name": player_row.get("player"),
            "team": normalize_team(team_col),
            "position": player_row.get("position"),
            "otcId": player_row.get("otc_id"),
            "gsisId": player_row.get("gsis_id"),
            "capHitMillions": _round(col.get("cap_number")),
            "baseSalaryMillions": _round(col.get("base_salary")),
            "proratedBonusMillions": _round(col.get("prorated_bonus")),
            "rosterBonusMillions": _round(col.get("roster_bonus")),
            "cashPaidMillions": _round(col.get("cash_paid")),
            "yearsRemaining": years_remaining,
            "totalContractYears": total_years,
            "yearSigned": signed,
        }
    return None


def _round(x: float | None) -> float | None:
    if x is None:
        return None
    try:
        return round(float(x), 3)
    except (TypeError, ValueError):
        return None


def build_year(contracts: list[dict], year: int) -> dict:
    # The parquet has multiple contract rows per player (one per signed deal
    # over their career), but each row's `cols` is the SAME career-long cap
    # history per player. So we just dedup by otc_id (keep any one row - all
    # carry the same career cap history) and read cap_number for the target
    # year from cols. Brady etc. may have career caps for 2018 even though no
    # signed-contract entry technically spans that year (restructures aren't
    # always represented as separate contract rows).
    by_player: dict[int, dict] = {}
    # For "active contract" metadata (year_signed, years, value), pick the
    # most-recent contract whose year_signed <= target year. That's the deal
    # actually in effect during the season.
    active_contract: dict[int, dict] = {}
    for c in contracts:
        oid = c.get("otc_id")
        if oid is None:
            continue
        by_player.setdefault(oid, c)
        ys = c.get("year_signed")
        if ys is not None and ys <= year:
            cur = active_contract.get(oid)
            if cur is None or (cur.get("year_signed") or 0) < ys:
                active_contract[oid] = c

    players = []
    for oid, any_row in by_player.items():
        active = active_contract.get(oid, any_row)
        # Use any_row for the cols (career cap history); use active for
        # year_signed / years / total value metadata.
        row = expand_to_year_with_active(any_row, active, year)
        if row is not None and row["name"] and row["capHitMillions"]:
            players.append(row)
    # Sort for stable diffs: cap_hit descending so star contracts appear first.
    players.sort(key=lambda p: (-(p["capHitMillions"] or 0), p["name"]))
    return {
        "schema": "ncaa-madden-contracts/v1",
        "nflSeason": year,
        "source": "nflverse/historical_contracts (mirrors overthecap.com)",
        "players": players,
    }


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("year", nargs="?", type=int, help="Build one season; default: all 2008-2026")
    p.add_argument("--refresh", action="store_true", help="Force re-download of the parquet")
    args = p.parse_args()

    parquet = fetch_parquet(refresh=args.refresh)
    print(f"  loading {parquet} ...", file=sys.stderr)
    contracts = load_contracts(parquet)
    print(f"  {len(contracts)} contracts loaded", file=sys.stderr)

    years = [args.year] if args.year else DEFAULT_YEARS
    CANONICAL_DIR.mkdir(parents=True, exist_ok=True)
    summary = []
    for y in years:
        doc = build_year(contracts, y)
        out = CANONICAL_DIR / f"contracts-{y}.json"
        with open(out, "w", encoding="utf-8") as f:
            json.dump(doc, f, indent=2, ensure_ascii=False)
        n = len(doc["players"])
        top = doc["players"][0] if n else None
        top_str = f"top: {top['name']} {top.get('capHitMillions')}M" if top else "no players"
        summary.append((y, n, top_str, out.name))

    print("\n=== Summary ===")
    print(f"{'Year':<6}{'#players':<10}{'top':<35}{'file'}")
    for y, n, top, fname in summary:
        print(f"{y:<6}{n:<10}{top:<35}{fname}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
