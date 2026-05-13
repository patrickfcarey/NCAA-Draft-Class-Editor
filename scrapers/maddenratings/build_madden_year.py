#!/usr/bin/env python3
"""
Scrape per-team rosters from maddenratings.weebly.com for a given Madden NFL year.

The site hosts a per-team XLSX file for each Madden year on its team page.
URL pattern observed (Madden 09):
    https://maddenratings.weebly.com/uploads/1/4/0/9/14097292/
        pittsburgh_steelers_madden_nfl_09.xlsx

The year page (e.g. madden-nfl-09.html) embeds each team logo as an <a href="...xlsx">
link. We scrape the page, collect all XLSX URLs, download each, and parse with openpyxl.

Output: data/raw/madden-ratings/madden-{yy}.json
        { maddenVersion, source, teams: { team_slug: [player_dicts...], ... } }

Usage:
    python build_madden_year.py 09     # Madden NFL 09  (2008 draftees as rookies)
    python build_madden_year.py 27     # Madden NFL 27  (2026 draftees as rookies)

Dependencies: openpyxl (pip install openpyxl).
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


def scrape_year(yy: str) -> dict[str, Any]:
    year_url = f"{BASE}/madden-nfl-{yy}.html"
    html_path = CACHE_DIR / "pages" / f"madden-nfl-{yy}.html"
    html = fetch(year_url, html_path).decode("utf-8", errors="replace")

    pattern = rf"/uploads/[^'\"]+madden_nfl_{yy}[^'\"]*\.xlsx?"
    urls = sorted(set(re.findall(pattern, html)))
    if not urls:
        urls = sorted(set(re.findall(r"/uploads/[^'\"]+\.xlsx?", html)))
    print(f"Madden {yy}: found {len(urls)} XLSX URLs", file=sys.stderr)

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
        "maddenVersion": yy,
        "source": "maddenratings.weebly.com",
        "teams": teams_data,
    }


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <madden_year_yy>", file=sys.stderr)
        print(f"Example: {sys.argv[0]} 09   (for Madden NFL 09 = 2008 rookies)", file=sys.stderr)
        return 2
    yy = sys.argv[1].zfill(2)

    result = scrape_year(yy)
    RAW_DIR.mkdir(parents=True, exist_ok=True)
    out_path = RAW_DIR / f"madden-{yy}.json"
    with out_path.open("w", encoding="utf-8") as f:
        json.dump(result, f, indent=2, ensure_ascii=False, default=json_default)

    total = sum(len(t) for t in result["teams"].values())
    print(f"Wrote {out_path}: {len(result['teams'])} teams, {total} players", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
