#!/usr/bin/env python3
"""
Build a canonical draft class JSON from a Wikipedia NFL draft page.

Use case: years that nflverse hasn't backfilled yet (typically the most
recent one or two drafts). For 2008-2025 we use nflverse; for 2026+ this
scraper fills the gap until nflverse catches up.

Wikipedia hosts a single big sortable picks table per draft year at
https://en.wikipedia.org/wiki/{year}_NFL_draft with columns:
    icon | Round | Pick | Team | Player | Pos | College | Notes

Output: data/canonical/draft-class-{year}.json with the same canonical
schema the nflverse scraper produces. Measurables / combine / birth-date
are not present in the Wikipedia table - those stay null.

Usage:
    python build_draft_class.py 2026

Dependencies: stdlib only.
"""
from __future__ import annotations

import json
import re
import sys
import time
import urllib.request
from html.parser import HTMLParser
from pathlib import Path
from typing import Any

REPO_ROOT = Path(__file__).resolve().parent.parent.parent
CACHE_DIR = Path(__file__).resolve().parent / "cache"
CANONICAL_DIR = REPO_ROOT / "data" / "canonical"
UA = "Mozilla/5.0 (X11; Linux x86_64) NCAA-Draft-Class-Editor-research/1.0"

TEAM_TO_ABBR = {
    "Arizona Cardinals": "ARI", "Atlanta Falcons": "ATL", "Baltimore Ravens": "BAL",
    "Buffalo Bills": "BUF", "Carolina Panthers": "CAR", "Chicago Bears": "CHI",
    "Cincinnati Bengals": "CIN", "Cleveland Browns": "CLE", "Dallas Cowboys": "DAL",
    "Denver Broncos": "DEN", "Detroit Lions": "DET", "Green Bay Packers": "GB",
    "Houston Texans": "HOU", "Indianapolis Colts": "IND", "Jacksonville Jaguars": "JAX",
    "Kansas City Chiefs": "KC", "Las Vegas Raiders": "LV", "Los Angeles Chargers": "LAC",
    "Los Angeles Rams": "LA", "Miami Dolphins": "MIA", "Minnesota Vikings": "MIN",
    "New England Patriots": "NE", "New Orleans Saints": "NO", "New York Giants": "NYG",
    "New York Jets": "NYJ", "Philadelphia Eagles": "PHI", "Pittsburgh Steelers": "PIT",
    "San Francisco 49ers": "SF", "Seattle Seahawks": "SEA", "Tampa Bay Buccaneers": "TB",
    "Tennessee Titans": "TEN", "Washington Commanders": "WAS",
}

POS_MAP = {
    "QB": "QB", "RB": "RB", "FB": "FB", "HB": "RB",
    "WR": "WR", "TE": "TE",
    "OT": "OT", "T": "OT", "OL": "OT", "OG": "OG", "G": "OG", "C": "C", "LS": "C",
    "DE": "DE", "EDGE": "DE", "DT": "DT", "NT": "DT", "DL": "DT",
    "LB": "LB", "OLB": "LB", "MLB": "LB", "ILB": "LB",
    "CB": "CB", "DB": "CB", "S": "S", "SS": "S", "FS": "S", "SAF": "S",
    "K": "K", "PK": "K", "P": "P",
}


def fetch(url: str, cache_path: Path) -> str:
    if cache_path.exists():
        return cache_path.read_text(encoding="utf-8", errors="replace")
    cache_path.parent.mkdir(parents=True, exist_ok=True)
    print(f"GET {url}", file=sys.stderr)
    req = urllib.request.Request(url, headers={"User-Agent": UA})
    with urllib.request.urlopen(req, timeout=60) as r:
        data = r.read()
    cache_path.write_bytes(data)
    time.sleep(1.0)
    return data.decode("utf-8", errors="replace")


class _TableParser(HTMLParser):
    """Pulls plain-text cells out of <tr><td>...</td></tr> rows."""
    def __init__(self) -> None:
        super().__init__()
        self.rows: list[list[str]] = []
        self.cells: list[str] = []
        self.buf: list[str] = []
        self.in_cell = False
        self.in_row = False

    def handle_starttag(self, tag: str, attrs: list[tuple[str, str | None]]) -> None:
        if tag == "tr":
            self.in_row = True
            self.cells = []
        elif tag in ("td", "th") and self.in_row:
            self.in_cell = True
            self.buf = []

    def handle_endtag(self, tag: str) -> None:
        if tag == "tr" and self.in_row:
            self.rows.append(self.cells)
            self.in_row = False
        elif tag in ("td", "th") and self.in_cell:
            self.cells.append(" ".join("".join(self.buf).split()).strip())
            self.in_cell = False

    def handle_data(self, data: str) -> None:
        if self.in_cell:
            self.buf.append(data)


def extract_picks_table(html: str) -> str:
    """Find the big sortable picks table (typically the largest wikitable)."""
    start = html.find('<table class="wikitable sortable plainrowheaders"')
    if start < 0:
        raise RuntimeError("Could not find picks table in Wikipedia page")
    depth = 1
    pos = start + 1
    while depth > 0 and pos < len(html):
        n = html.find("<table", pos)
        e = html.find("</table>", pos)
        if e < 0:
            break
        if n != -1 and n < e:
            depth += 1
            pos = n + 6
        else:
            depth -= 1
            pos = e + 8
    return html[start:pos]


def split_name(full: str) -> tuple[str, str]:
    """'Baker Mayfield Jr.' -> ('Baker', 'Mayfield Jr.')."""
    parts = full.split()
    if not parts:
        return "", ""
    suffix = ""
    if len(parts) >= 2 and parts[-1] in ("Jr.", "Sr.", "II", "III", "IV", "V"):
        suffix = parts.pop()
    last = (parts[-1] + (" " + suffix if suffix else "")) if parts else ""
    first = " ".join(parts[:-1]) if len(parts) > 1 else parts[0]
    return first, last


def build(year: int) -> dict[str, Any]:
    url = f"https://en.wikipedia.org/wiki/{year}_NFL_draft"
    html_path = CACHE_DIR / f"{year}.html"
    html = fetch(url, html_path)

    table_html = extract_picks_table(html)
    p = _TableParser()
    p.feed(table_html)
    rows = p.rows[1:]  # drop header

    players: list[dict[str, Any]] = []
    for row in rows:
        if len(row) < 7:
            continue
        _icon, rnd, pick, team, name, pos, college, *_ = row
        if not name or not pos:
            continue
        round_n = int(re.sub(r"\D", "", rnd) or 0)
        pick_n = int(re.sub(r"\D", "", pick) or 0)
        if not round_n:
            continue
        first, last = split_name(name)
        players.append({
            "name": {"first": first, "last": last},
            "position": POS_MAP.get(pos, pos),
            "college": college,
            "collegeYear": "SR",
            "redshirt": False,
            "draft": {
                "round": round_n,
                "pick": pick_n,
                "team": TEAM_TO_ABBR.get(team, team),
            },
        })

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
