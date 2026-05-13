#!/usr/bin/env python3
"""
Scrape per-team rosters from maddenratings.weebly.com for a given NFL season.

CLI takes the NFL SEASON YEAR (e.g. 2013, 2018, 2023) rather than the Madden
version. That avoids the Madden 25 collision: there's a 2013 anniversary
edition AND a 2024 modern release, both literally named "Madden NFL 25".
By indexing on NFL season, the mapping draft-year -> rookie-ratings file is
trivial: the 2013 NFL Draft rookies live in `madden-2013.json` (sourced from
the 2013 anniversary game), and the eventual 2024 NFL Draft rookies will be
in `madden-2024.json` (sourced from the modern 2024 game from a different
URL once we add that scraper).

The site hosts a per-team XLSX file for each Madden year on its team page.
URL pattern observed (Madden 09):
    https://maddenratings.weebly.com/uploads/1/4/0/9/14097292/
        pittsburgh_steelers_madden_nfl_09.xlsx

The year page (e.g. madden-nfl-09.html) embeds each team logo as an
<a href="...xlsx"> link. We scrape the page, collect all XLSX URLs, download
each, and parse with openpyxl. Madden 11 uses .xls (BIFF) which needs xlrd.

Output: data/raw/madden-ratings/madden{version}-{nflSeason}.json
        { nflSeason, maddenVersion, source, teams: { team_slug: [...], ... } }
Examples:
    madden09-2008.json   (M09, released Aug 2008, 2008 rookies)
    madden25-2013.json   (M25 anniversary, released Aug 2013, 2013 rookies)
    madden25-2024.json   (M25 modern, released Aug 2024, 2024 rookies; future)

Usage:
    python build_madden_year.py 2008    # NFL 2008 season = Madden NFL 09
    python build_madden_year.py 2013    # 2013 season    = Madden NFL 25 (anniversary)
    python build_madden_year.py 2018    # 2018 season    = Madden NFL 19

Dependencies: openpyxl>=3.0, xlrd==1.2.0 (see requirements.txt).
"""
from __future__ import annotations

import json
import re
import sys
import time
import urllib.request
from datetime import date, datetime
from pathlib import Path
from typing import Any

from openpyxl import load_workbook

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache"
RAW_DIR = REPO_ROOT / "data" / "raw" / "madden-ratings"
BASE = "https://maddenratings.weebly.com"
UA = "Mozilla/5.0 (X11; Linux x86_64) NCAA-Draft-Class-Editor-research/1.0"
DELAY = 0.3  # seconds between XLSX downloads (static file host, but be polite)

# NFL season year -> Madden version string used in URLs on weebly.
# Note the M25 anniversary case: the 2013 season game is "Madden NFL 25",
# distinct from the modern 2024-released M25 which is NOT on weebly.
SEASON_TO_MADDEN: dict[int, str] = {
    2008: "09",
    2009: "10",
    2010: "11",
    2011: "12",
    2012: "13",
    2013: "25",   # anniversary edition
    2014: "15",
    2015: "16",
    2016: "17",
    2017: "18",
    2018: "19",
    2019: "20",
    2020: "21",
    2021: "22",
    2022: "23",
    2023: "24",
}


def fetch(url: str, cache_path: Path) -> bytes:
    if cache_path.exists():
        return cache_path.read_bytes()
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    print(f"  GET {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=30) as r:
        data = r.read()
    cache_path.write_bytes(data)
    time.sleep(DELAY)
    return data


def parse_xls(path: Path) -> list[dict[str, Any]]:
    """Read legacy .xls (BIFF) via xlrd 1.2.0."""
    import xlrd
    wb = xlrd.open_workbook(str(path))
    ws = wb.sheet_by_index(0)
    if ws.nrows == 0:
        return []
    headers = [str(ws.cell_value(0, c)).strip() or f"col_{c}" for c in range(ws.ncols)]
    out: list[dict[str, Any]] = []
    for r in range(1, ws.nrows):
        row = [ws.cell_value(r, c) for c in range(ws.ncols)]
        if all(v == "" or v is None for v in row):
            continue
        out.append({h: v for h, v in zip(headers, row) if v not in (None, "")})
    return out


def parse_xlsx(path: Path) -> list[dict[str, Any]]:
    wb = load_workbook(path, read_only=True, data_only=True)
    ws = wb.active
    rows = list(ws.iter_rows(values_only=True))
    if not rows:
        return []
    headers = [str(h).strip() if h is not None else f"col_{i}"
               for i, h in enumerate(rows[0])]
    out: list[dict[str, Any]] = []
    for row in rows[1:]:
        if all(v is None or (isinstance(v, str) and not v.strip()) for v in row):
            continue
        out.append({h: v for h, v in zip(headers, row) if v is not None and v != ""})
    return out


def parse_workbook(path: Path) -> list[dict[str, Any]]:
    return parse_xls(path) if path.suffix.lower() == ".xls" else parse_xlsx(path)


def json_default(o: Any) -> str:
    if isinstance(o, (datetime, date)):
        return o.isoformat()
    raise TypeError(f"Object of type {type(o).__name__} is not JSON serializable")


def scrape_season(season: int) -> dict[str, Any]:
    if season not in SEASON_TO_MADDEN:
        raise ValueError(f"NFL season {season} not in SEASON_TO_MADDEN map. "
                         f"Available: {sorted(SEASON_TO_MADDEN)}")
    yy = SEASON_TO_MADDEN[season]

    year_url = f"{BASE}/madden-nfl-{yy}.html"
    html_path = CACHE_DIR / "pages" / f"madden-nfl-{yy}.html"
    html = fetch(year_url, html_path).decode("utf-8", errors="replace")

    pattern = rf"/uploads/[^'\"]+madden_nfl_{yy}[^'\"]*\.xlsx?"
    urls = sorted(set(re.findall(pattern, html)))
    if not urls:
        urls = sorted(set(re.findall(r"/uploads/[^'\"]+\.xlsx?", html)))
    print(f"Season {season} (Madden {yy}): {len(urls)} XLSX URLs", file=sys.stderr)

    teams_data: dict[str, list[dict[str, Any]]] = {}
    for rel in urls:
        full_url = BASE + rel
        filename = rel.rsplit("/", 1)[-1]
        cache_path = CACHE_DIR / "xlsx" / yy / filename
        try:
            fetch(full_url, cache_path)
            players = parse_workbook(cache_path)
        except Exception as e:
            print(f"  ERROR {filename}: {e}", file=sys.stderr)
            continue
        team_key = re.sub(rf"_madden_nfl_{yy}[^.]*\.xls(x)?$", "", filename)
        teams_data[team_key] = players
        print(f"  {team_key}: {len(players)} players", file=sys.stderr)

    return {
        "nflSeason": season,
        "maddenVersion": yy,
        "source": "maddenratings.weebly.com",
        "teams": teams_data,
    }


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <nfl_season_year>", file=sys.stderr)
        print(f"Example: {sys.argv[0]} 2013   (rookies = 2013 NFL Draft class)", file=sys.stderr)
        return 2
    season = int(sys.argv[1])

    result = scrape_season(season)
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    out_path = RAW_DIR / f"madden{result['maddenVersion']}-{season}.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False, default=json_default)

    total = sum(len(t) for t in result["teams"].values())
    print(f"Wrote {out_path}: {len(result['teams'])} files, {total} players", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
