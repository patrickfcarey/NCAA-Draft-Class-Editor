#!/usr/bin/env python3
"""
Dump the TDB field schema as a human-readable markdown reference.

For each table, lists every field with its 4-letter code, type, bit
position, width, and a best-guess meaning derived from:
  * The shared codes with NCAA's 86-byte record (P[A-Z]{3} ratings)
  * Common EA conventions (P prefix = player attribute, T prefix = team)
  * Statistical inspection of sample values when no other hint

Run:
    python tools/dump_tdb_schema.py <tdb_file>

Outputs the schema to stdout as markdown.
"""
from __future__ import annotations

import sys
from collections import Counter
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from parse_madden_tdb import parse_tdb, TYPE_NAMES, read_field_value  # type: ignore


# Field meanings inferred from NCAA file's FieldMap.cs + common EA conventions
# + this codebase's existing rating mappers. Fill in confidently when known.
FIELD_MEANINGS: dict[str, str] = {
    # NCAA-side rating codes (PSPD, PSTR, etc.) carry directly
    "PSPD": "Speed (rating)",
    "PACC": "Acceleration (rating)",
    "PAGI": "Agility (rating)",
    "PSTR": "Strength (rating)",
    "PAWR": "Awareness (rating)",
    "PCTH": "Catch (rating)",
    "PCAR": "Carry/Carrying (rating)",
    "PTHP": "Throw Power (rating)",
    "PTHA": "Throw Accuracy (rating)",
    "PKPR": "Kick Power (rating)",
    "PKAC": "Kick Accuracy (rating)",
    "PBTK": "Break Tackle (rating)",
    "PTAK": "Tackle (rating)",
    "PIMP": "Impact / Hit Power (rating)",
    "PPBK": "Pass Block (rating)",
    "PRBK": "Run Block (rating)",
    "PPOE": "Point of Emphasis (rating)",
    "PTEN": "Tenacity (rating)",
    "PJMP": "Jumping (rating)",
    "PINJ": "Injury (rating)",
    "PSTA": "Stamina (rating)",
    "POVR": "Overall (rating)",
    # Identity / bio
    "PFNA": "First Name (string, 11 chars)",
    "PLNA": "Last Name (string, 13 chars)",
    "PPOS": "Position code",
    "PJEN": "Jersey number",
    "PAGE": "Age (years)",
    "PHGT": "Height (inches)",
    "PWGT": "Weight (lb, offset-encoded; +160 to get real lbs)",
    "PHED": "Head model / hair style",
    "PHCL": "Hair color",
    "PSKI": "Skin tone",
    "PMOR": "Morale (rating)",
    "PYRP": "Years pro (NFL experience)",
    "PCOL": "College ID (NFL team's player came from)",
    # IDs / linkage
    "PGID": "Player global ID",
    "TGID": "Team ID (FK to TEAM)",
    "POID": "Pro Bowl / overall ID",
    # Throw accuracy sub-ratings (Madden has short/mid/deep)
    "PTSA": "Throw Accuracy Short (?)",
    "PMAS": "Medium throw accuracy (?)",
    "PMTS": "Throw on the run (?)",
    "PTGH": "Toughness",
    "PFPB": "Pro Bowl flag (1 = Pro Bowl appearance)",
    # Equipment / cosmetic
    "PFMK": "Facemask",
    "PNEK": "Neck pad",
    "PEYE": "Eye black",
    "PVSB": "Visor + various flags (composite)",
    "PCPH": "Hand pads (?)",
    "PLSH": "Left arm pad (?)",
    "PRSH": "Right arm pad (?)",
    "PLHA": "Left hand (?)",
    "PRHA": "Right hand (?)",
    "PLWR": "Left wrist (?)",
    "PRWR": "Right wrist (?)",
    "TLHA": "Tape/glove left hand (?)",
    "TRHA": "Tape/glove right hand (?)",
    "TLWR": "Tape left wrist (?)",
    "TRWR": "Tape right wrist (?)",
    # Team fields
    "TDNA": "Team Display Name (e.g., 'Bears')",
    "TLNA": "Team Location/City Name (e.g., 'Chicago')",
    "TSNA": "Team Short Name / Abbreviation (e.g., 'CHI')",
    "TRV1": "Team Rivalry 1 (?)",
    "TRV2": "Team Rivalry 2 (?)",
    "TRV3": "Team Rivalry 3 (?)",
    "TEZ1": "Team Endzone 1 (?)",
    "TEZ2": "Team Endzone 2 (?)",
    "TEZ3": "Team Endzone 3 (?)",
}


def guess_meaning(code: str, ftype: int, bits: int, value_stats: dict) -> str:
    if code in FIELD_MEANINGS:
        return FIELD_MEANINGS[code]
    # Generic guesses
    if code.startswith("P"):
        if ftype == 0:  # STRING
            return f"Player string (likely text field, {bits//8} chars)"
        if bits == 7:
            return "Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range)"
        if bits <= 4:
            return f"Player flag / enum ({bits}-bit)"
        return f"Player numeric attribute ({bits}-bit)"
    if code.startswith("T"):
        if ftype == 0:
            return f"Team string ({bits//8} chars)"
        return f"Team attribute ({bits}-bit)"
    return "Unknown"


def dump(path: Path) -> None:
    data = path.read_bytes()
    parsed = parse_tdb(data)
    header = parsed["fileHeader"]

    print(f"# Madden 08 TDB schema reference")
    print()
    print(f"Generated from `{path.name}` ({len(data)} bytes).")
    print()
    print(f"- Magic: `{header['magic']}` (`0x{int.from_bytes(b'DB', 'little'):04x}` LE)")
    print(f"- Version: {header['version']}")
    print(f"- DB size: {header['dbSize']}")
    print(f"- Table count: {header['tableCount']}")
    print(f"- Checksum: `{header['checksum']}`")
    print()
    print(f"## Tables")
    print()
    print(f"| Name | Records (cur/max) | Bytes/record | Bits/record | Fields |")
    print(f"|---|---|---|---|---|")
    for t in parsed["tables"]:
        h = t["header"]
        print(f"| `{t['name']}` | {h['curRecords']} / {h['maxRecords']} | {h['lenBytes']} | {h['lenBits']} | {h['numFields']} |")
    print()

    for t in parsed["tables"]:
        print(f"## Table `{t['name']}` ({t['header']['curRecords']} records × {t['header']['lenBytes']} bytes)")
        print()
        # Sample value stats: collect for hint generation
        stats_per_field: dict[str, list] = {f["name"]: [] for f in t["fields"]}
        for r in t["records"][:200]:
            for f in t["fields"]:
                v = r.get(f["name"])
                stats_per_field[f["name"]].append(v)
        print(f"| Code | Type | Bit offset | Bits | Sample values | Likely meaning |")
        print(f"|---|---|---|---|---|---|")
        for f in sorted(t["fields"], key=lambda x: x["offsetBits"]):
            sample = stats_per_field[f["name"]][:5]
            if f["type"] == 0:  # string
                sample_str = ", ".join(f'"{s}"' if isinstance(s, str) else str(s) for s in sample[:3])
            elif f["type"] in (2, 3):
                sample_str = ", ".join(str(s) for s in sample[:6])
            else:
                sample_str = "..."
            meaning = guess_meaning(f["name"], f["type"], f["bits"], {})
            print(f"| `{f['name']}` | {TYPE_NAMES.get(f['type'], '?')} | {f['offsetBits']} | {f['bits']} | {sample_str} | {meaning} |")
        print()


def main() -> int:
    if len(sys.argv) != 2:
        print(f"Usage: {sys.argv[0]} <tdb_file>", file=sys.stderr)
        return 2
    dump(Path(sys.argv[1]))
    return 0


if __name__ == "__main__":
    sys.exit(main())
