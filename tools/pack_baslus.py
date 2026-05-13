#!/usr/bin/env python3
"""
Pack a compiled NCAA draft class binary into a PS2 save file ready to import
into PCSX2 (or copy to a real PS2 memory card via Action Replay MAX / SharkPort).

A PS2 save isn't just the raw binary - it's a folder named with a product code
plus a slot suffix (e.g. BASLUS-21620LClass07 for Madden NFL 08 USA's draft class
slot) containing three files:
  - icon.sys   (964 bytes)   save title and icon metadata
  - view.ico   (33 KB)       3D icon model the PS2 dashboard renders
  - BASLUS-21620LClass07     the actual draft class data (138,240 bytes)

We reuse icon.sys and view.ico from the template .max checked into this repo
(madden-nfl-08.26380.max, which was extracted from the user's original PS2 save).
The data file gets replaced with the compiled binary.

Output format is chosen by file extension:
  .max -> MaxDrive (Action Replay)
  .psu -> EMS / SharkPort       [default]

Requires mymcplus (pip install mymcplus).

Usage:
    python tools/pack_baslus.py <compiled.bin> <output.max|psu>
"""
from __future__ import annotations

import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEMPLATE_MAX = REPO_ROOT / "madden-nfl-08.26380.max"
SAVE_FOLDER = "BASLUS-21620LClass07"   # Madden NFL 08 USA, draft class slot
INNER_FILENAME = SAVE_FOLDER            # same name as the folder by PS2 convention


def run(cmd: list[str | Path]) -> None:
    """Run a command, surfacing stderr but not stdout (mymcplus prints noise)."""
    print(f"  $ {' '.join(str(c) for c in cmd)}", file=sys.stderr)
    subprocess.run([str(c) for c in cmd], check=True, capture_output=True, text=True)


def pack(compiled_bin: Path, output_path: Path) -> None:
    if not compiled_bin.exists():
        raise FileNotFoundError(f"Compiled binary not found: {compiled_bin}")
    if not TEMPLATE_MAX.exists():
        raise FileNotFoundError(f"Template .max not found at {TEMPLATE_MAX}")

    expected_size = 138_240
    actual_size = compiled_bin.stat().st_size
    if actual_size != expected_size:
        print(f"  WARNING: compiled binary is {actual_size} bytes; "
              f"NCAA 08 saves are normally {expected_size}", file=sys.stderr)

    fmt_flag = "-m" if output_path.suffix.lower() == ".max" else "-p"

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        card = tmp_dir / "card.ps2"

        # Stage the .bin under the exact inner filename so mymcplus adds it correctly.
        staged_bin = tmp_dir / INNER_FILENAME
        shutil.copy2(compiled_bin, staged_bin)

        # 1. Empty card.
        run(["mymcplus", card, "format"])
        # 2. Import template to get the BASLUS-21620LClass07 folder with icon.sys + view.ico.
        run(["mymcplus", card, "import", TEMPLATE_MAX])
        # 3. Drop the template's data file.
        run(["mymcplus", card, "remove", f"{SAVE_FOLDER}/{INNER_FILENAME}"])
        # 4. Add the freshly compiled data file.
        run(["mymcplus", card, "add", "-d", SAVE_FOLDER, staged_bin])
        # 5. Export the save as .max or .psu.
        output_path.parent.mkdir(parents=True, exist_ok=True)
        run(["mymcplus", card, "export", fmt_flag,
             "-o", output_path, "-f", SAVE_FOLDER])


def main() -> int:
    if len(sys.argv) != 3:
        print(f"Usage: {sys.argv[0]} <compiled.bin> <output.max|psu>", file=sys.stderr)
        return 2

    compiled_bin = Path(sys.argv[1]).resolve()
    output_path = Path(sys.argv[2]).resolve()
    if output_path.suffix.lower() not in (".max", ".psu"):
        print(f"Output filename must end in .max or .psu (got {output_path.suffix})",
              file=sys.stderr)
        return 2

    pack(compiled_bin, output_path)
    print(f"Wrote {output_path} ({output_path.stat().st_size} bytes)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
