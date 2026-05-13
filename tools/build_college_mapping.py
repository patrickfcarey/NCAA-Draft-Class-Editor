#!/usr/bin/env python3
"""
Auto-derive data/mappings/colleges.json from CollegeCatalog.cs + canonical
draft data, for later user review.

Strategy (in order, first match wins):
  1. Direct exact match against catalog names
  2. Normalized match (lowercase, strip punctuation/common suffixes)
  3. Hand-curated alias table (CANONICAL_TO_CATALOG)
  4. Fuzzy match (difflib close-match, threshold 0.85)
  5. Fallback to 255 (N/A) for schools not in NCAA 06's catalog

Output JSON has both the bare {college: tgid} map (consumed by the compiler)
and an "audit" block recording how each entry was matched (for review).

Usage:
    python tools/build_college_mapping.py
"""
from __future__ import annotations

import difflib
import glob
import json
import re
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
CATALOG_CS = REPO_ROOT / "NcaaDraftEditor.Core" / "CollegeCatalog.cs"
CANONICAL_GLOB = str(REPO_ROOT / "data" / "canonical" / "draft-class-*.json")
OUT_PATH = REPO_ROOT / "data" / "mappings" / "colleges.json"

# Hand-curated aliases: canonical name (as seen in nflverse/wiki) -> catalog name
# Add to this when fuzzy matching gets one wrong on review.
CANONICAL_TO_CATALOG = {
    "California": "Cal",
    "East Carolina": "ECU",
    "Eastern Washington": "E Washington",
    "Mississippi": "Ole Miss",
    "Brigham Young": "BYU",
    "Southern California": "USC",
    "Central Florida": "UCF",
    "South Florida": "USF",
    "Texas-San Antonio": "UTSA",
    "Texas-El Paso": "UTEP",
    "Massachusetts": "UMass",
    "Connecticut": "Connecticut",
    "Louisiana": "UL Lafayette",           # rebrand of Louisiana-Lafayette
    "Louisiana-Lafayette": "UL Lafayette",
    "Louisiana-Monroe": "UL Monroe",
    "Middle Tennessee State": "Mid Tenn State",
    "Middle Tennessee": "Mid Tenn State",
    "N.C. State": "NC State",
    "NC State": "NC State",
    "North Carolina State": "NC State",
    "N.C. A&T State": "NC A&T State",
    "North Carolina A&T": "NC A&T State",
    "Southeast Missouri State": "SE Missouri St",
    "Southeastern Louisiana": "Southeastern",
    "Northwestern State": "Northwestern St",
    "Northwestern State (LA)": "Northwestern St",
    "Sam Houston State": "Sam Houston St",
    "Southwest Missouri State": "SMS",
    "Missouri State": "SMS",
    "Northern Iowa": "UNI",
    "Charleston Southern": "Charleston",
    "Tennessee State": "Tennessee State",
    "Tennessee-Martin": "Tennessee-Martin",
    "Florida International": "FIU",
    "Miami (FL)": "Miami",
    "Miami (OH)": "Miami University",
    "Miami (Ohio)": "Miami University",
    "Southern Mississippi": "Southern Miss",
    "Tennessee-Chattanooga": "Chattanooga",
    "Texas State-San Marcos": "Texas State",
    "Towson State": "Towson",
    "UConn": "Connecticut",
    "UMass Amherst": "UMass",
    "University of South Florida": "USF",
    "University of Arkansas at Pine Bluff": "Ark - Pine Bluff",
}

# Schools known to NOT be in NCAA 06's catalog -> 255 (N/A).
# Pre-populating these avoids noisy fuzzy guesses.
EXPLICIT_NA = {
    "Albany State (GA)", "Albion College", "Ashland", "Bentley College",
    "Bloomsburg", "Cal Poly (San Luis Obispo)", "California (PA)",
    "Central Arkansas", "Central Missouri", "Chadron State", "Concordia College St. Paul",
    "Dayton", "Drake", "East Central", "East Texas A&M",
    "Fayetteville State University", "Ferris State", "Gardner-Webb",
    "Hillsdale", "Indiana PA,  University of", "Lehigh University",
    "Lehigh", "Lindenwood", "Long Island University",
    "Mars Hill", "Morehouse College", "Monmouth", "Nebraska-Omaha",
    "Norfolk State", "Pittsburg State", "Saint Augustine's College",
    "Shaw University", "Shepherd", "St. Paul's College", "Stetson",
    "Stillman College", "Tarleton State", "Tiffin", "Tusculum",
    "Tuskegee", "Valdosta State", "Virginia Union", "West Alabama",
    "West Georgia", "Western Ontario", "Wheaton College Illinois",
    "Winston Salem State University",
    # Wofford removed - it IS in the catalog as TGID 196
}


def parse_catalog() -> dict[str, int]:
    """Read CollegeCatalog.cs and return {name: tgid_byte}."""
    cs = CATALOG_CS.read_text()
    return {m.group(2): int(m.group(1))
            for m in re.finditer(r'\[\s*\(byte\)(\d+)\s*\]\s*=\s*"([^"]+)"', cs)}


def normalize(s: str) -> str:
    """Lowercase, strip punctuation, collapse whitespace."""
    s = s.lower()
    s = re.sub(r"\b(university|college|the)\b", "", s)
    s = re.sub(r"[^\w&]+", " ", s)
    return " ".join(s.split())


def collect_canonical_colleges() -> set[str]:
    out: set[str] = set()
    for path in sorted(glob.glob(CANONICAL_GLOB)):
        if "sample" in path:
            continue
        with open(path) as f:
            data = json.load(f)
        for p in data.get("players", []):
            c = (p.get("college") or "").strip()
            if c:
                out.add(c)
    return out


def main() -> int:
    catalog = parse_catalog()
    by_name = {name: tgid for name, tgid in catalog.items()}
    by_norm = {normalize(name): tgid for name, tgid in catalog.items()}
    canonical_names = sorted(catalog.values())  # for fuzzy candidates use catalog names
    catalog_names = list(catalog.keys())

    canonicals = collect_canonical_colleges()
    print(f"Canonical colleges: {len(canonicals)}", file=sys.stderr)
    print(f"Catalog entries:    {len(catalog)}", file=sys.stderr)

    mapping: dict[str, int] = {}
    audit: dict[str, str] = {}

    for c in sorted(canonicals):
        # 1. Direct match
        if c in by_name:
            mapping[c] = by_name[c]; audit[c] = "direct"
            continue
        # 2. Explicit N/A
        if c in EXPLICIT_NA:
            mapping[c] = 255; audit[c] = "explicit-na"
            continue
        # 3. Hand-curated alias
        if c in CANONICAL_TO_CATALOG:
            target = CANONICAL_TO_CATALOG[c]
            if target in by_name:
                mapping[c] = by_name[target]; audit[c] = f"alias:{target}"
                continue
        # 4. Normalized match
        nc = normalize(c)
        if nc in by_norm:
            mapping[c] = by_norm[nc]; audit[c] = "normalized"
            continue
        # 5. Fuzzy
        close = difflib.get_close_matches(c, catalog_names, n=1, cutoff=0.85)
        if close:
            mapping[c] = by_name[close[0]]; audit[c] = f"fuzzy:{close[0]}"
            continue
        # 6. Default to N/A
        mapping[c] = 255; audit[c] = "no-match"

    # Tally
    by_source: dict[str, int] = {}
    for src in audit.values():
        key = src.split(":")[0]
        by_source[key] = by_source.get(key, 0) + 1
    print("\nMatch sources:", file=sys.stderr)
    for k, v in sorted(by_source.items(), key=lambda x: -x[1]):
        print(f"  {k:15} {v}", file=sys.stderr)

    out = {
        "schema": "ncaa-college-mapping/v1",
        "comment": ("Maps canonical college names (as found in nflverse + Wikipedia "
                    "draft sources) to NCAA 06 TGID byte values (0-254 = catalog "
                    "entry, 255 = N/A). Built by tools/build_college_mapping.py; "
                    "audit block records how each entry was matched. "
                    "Review fuzzy: and no-match entries before relying on them."),
        "map": mapping,
        "audit": audit,
    }
    OUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with OUT_PATH.open("w", encoding="utf-8") as f:
        json.dump(out, f, indent=2, ensure_ascii=False, sort_keys=False)
    print(f"\nWrote {OUT_PATH}: {len(mapping)} entries", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
