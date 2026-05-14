#!/usr/bin/env python3
"""
Extract a Madden NFL 12 PS2 franchise save from a PCSX2 memory card and
package it as a template .psu at the path the pack_baslus.py 'm12-franchise'
preset expects.

The M12 franchise template is user-specific - you must create it yourself:
  1. Boot Madden NFL 12 PS2 in PCSX2 (Deluxe ISO or vanilla; the franchise
     save layout is the same).
  2. Start a new franchise.
  3. Save it at Week 1 preseason (before any games or FA signings).
     SEAI.SEWN should equal 0 in the resulting save; SEYR=0; SEWT=200.
  4. Export the memcard or use the PCSX2-mounted .ps2 directly.

Then run this script. Defaults assume the save was written to slot 1
(BASLUS-21946BFran1) on /mnt/c/PCSX2/memcards/Madden12Deluxe.ps2 (the
user's existing M12 Deluxe memcard).

Usage:
    python tools/fetch_m12_franchise_template.py [--memcard <path>] [--slot BASLUS-21946BFranN]

Output: out/templates/madden-nfl-12-franchise-template.{bin,psu}
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MEMCARD = Path("/mnt/c/PCSX2/memcards/Madden12Deluxe.ps2")
SAVE_FOLDER = "BASLUS-21946BFran1"
OUTPUT_PATH = REPO_ROOT / "out" / "templates" / "madden-nfl-12-franchise-template.psu"
INNER_BIN_PATH = REPO_ROOT / "out" / "templates" / "madden-nfl-12-franchise-template.bin"


def run(cmd: list[str | Path]) -> None:
    print(f"  $ {' '.join(str(c) for c in cmd)}", file=sys.stderr)
    subprocess.run([str(c) for c in cmd], check=True, capture_output=True, text=True)


def fetch(memcard: Path, save_folder: str) -> Path:
    if not memcard.exists():
        raise FileNotFoundError(
            f"Memcard not found: {memcard}.\n"
            f"  You need to create a fresh Madden 12 franchise save first - see\n"
            f"  this script's docstring for the procedure.")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        for fname in ("icon.sys", "view.ico", save_folder):
            run(["mymcplus", memcard, "extract",
                 "-d", save_folder, "-o", tmp_dir / fname, fname])

        shutil.copy2(tmp_dir / save_folder, INNER_BIN_PATH)

        card = tmp_dir / "card.ps2"
        run(["mymcplus", card, "format"])
        run(["mymcplus", card, "mkdir", save_folder])
        run(["mymcplus", card, "add", "-d", save_folder,
             tmp_dir / "icon.sys", tmp_dir / "view.ico", tmp_dir / save_folder])
        run(["mymcplus", card, "export", "-p", "-o", OUTPUT_PATH, "-f", save_folder])

    return OUTPUT_PATH


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--memcard", type=Path, default=DEFAULT_MEMCARD,
                   help=f"Source memcard (default: {DEFAULT_MEMCARD})")
    p.add_argument("--slot", default=SAVE_FOLDER,
                   help=f"Save folder name (default: {SAVE_FOLDER}). Use BASLUS-21946BFran[1-5].")
    args = p.parse_args()
    out = fetch(args.memcard, args.slot)
    print(f"Wrote {out} ({out.stat().st_size} bytes)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
