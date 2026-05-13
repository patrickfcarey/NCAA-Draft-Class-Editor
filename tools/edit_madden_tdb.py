#!/usr/bin/env python3
"""
Edit one field of one player in a Madden 08 PS2 TDB roster file.

Locates a player by first/last name in the PLAY table, sets a field
value, writes the modified file back. Proves the read-write loop works
end-to-end on real player data.

Examples:
    python tools/edit_madden_tdb.py in.bin out.bin --player "Brian Urlacher" --field PSPD --value 99
    python tools/edit_madden_tdb.py in.bin out.bin --player "Olin Kreutz" --field PFNA --value Tom
"""
from __future__ import annotations

import argparse
import sys
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parent))
from parse_madden_tdb import parse_tdb  # type: ignore
from write_madden_tdb import write_tdb  # type: ignore


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.splitlines()[1])
    p.add_argument("input")
    p.add_argument("output")
    p.add_argument("--player", required=True, help="\"First Last\" name to match in PLAY")
    p.add_argument("--field", required=True, help="4-letter field code (PSPD, PFNA, etc.)")
    p.add_argument("--value", required=True, help="New value (numeric or string)")
    args = p.parse_args()

    parts = args.player.split(maxsplit=1)
    if len(parts) != 2:
        print('--player must be "First Last"', file=sys.stderr)
        return 2
    first_match, last_match = parts

    original = Path(args.input).read_bytes()
    parsed = parse_tdb(original)
    play = next(t for t in parsed["tables"] if t["name"] == "PLAY")

    matched = []
    for rec in play["records"]:
        if (rec.get("PFNA", "").strip().lower() == first_match.lower()
                and rec.get("PLNA", "").strip().lower() == last_match.lower()):
            matched.append(rec)

    if not matched:
        print(f"No match for '{args.player}' in PLAY", file=sys.stderr)
        return 1
    if len(matched) > 1:
        print(f"WARNING: {len(matched)} players match '{args.player}'; updating all", file=sys.stderr)

    field_def = next((f for f in play["fields"] if f["name"] == args.field), None)
    if field_def is None:
        print(f"Unknown field '{args.field}'; valid fields: "
              + ", ".join(sorted(f["name"] for f in play["fields"])),
              file=sys.stderr)
        return 2

    # Coerce new value
    if field_def["type"] == 0:  # STRING
        new_value = args.value
    else:
        try:
            new_value = int(args.value)
        except ValueError:
            print(f"--value must be numeric for non-string field {args.field}", file=sys.stderr)
            return 2

    for rec in matched:
        old = rec.get(args.field)
        rec[args.field] = new_value
        print(f"  {rec['PFNA'].strip()} {rec['PLNA'].strip()}: {args.field} {old!r} -> {new_value!r}",
              file=sys.stderr)

    out_bytes = write_tdb(parsed, original)
    Path(args.output).write_bytes(out_bytes)
    print(f"Wrote {args.output} ({len(out_bytes)} bytes)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
