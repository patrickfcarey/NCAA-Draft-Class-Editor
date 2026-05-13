#!/usr/bin/env python3
"""
Extract a Madden NFL 08 PS2 franchise save from a PCSX2 memory card and
package it as a template .psu at the path the pack_baslus.py 'm08-franchise'
preset expects.

Unlike the M09/M12 Deluxe templates (which are community-distributed and
checked into a known download), the M08 franchise template is user-specific:
it's whatever fresh-Week-1 franchise the user has set up on their own memcard.
This script automates fetching it from a known memcard location into the
toolchain's standard cache so the rest of the pipeline can find it.

Usage:
    python tools/fetch_m08_franchise_template.py [--memcard <path>]

Default memcard path: /mnt/c/PCSX2/memcards/Mcd001.ps2
Output: out/templates/madden-nfl-08-franchise-template.psu
"""
from __future__ import annotations

import argparse
import subprocess
import sys
import tempfile
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
DEFAULT_MEMCARD = Path("/mnt/c/PCSX2/memcards/Mcd001.ps2")
SAVE_FOLDER = "BASLUS-21638BFran1"
OUTPUT_PATH = REPO_ROOT / "out" / "templates" / "madden-nfl-08-franchise-template.psu"
INNER_BIN_PATH = REPO_ROOT / "out" / "templates" / "madden-nfl-08-franchise-template.bin"


def run(cmd: list[str | Path]) -> None:
    print(f"  $ {' '.join(str(c) for c in cmd)}", file=sys.stderr)
    subprocess.run([str(c) for c in cmd], check=True, capture_output=True, text=True)


def fetch(memcard: Path) -> Path:
    if not memcard.exists():
        raise FileNotFoundError(f"Memcard not found: {memcard}")

    OUTPUT_PATH.parent.mkdir(parents=True, exist_ok=True)
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        for fname in ("icon.sys", "view.ico", SAVE_FOLDER):
            run(["mymcplus", memcard, "extract",
                 "-d", SAVE_FOLDER, "-o", tmp_dir / fname, fname])

        # Stash the inner TDB at the canonical .bin path so the compile-franchise
        # CLI can consume it directly without the user having to re-extract.
        import shutil
        shutil.copy2(tmp_dir / SAVE_FOLDER, INNER_BIN_PATH)

        # Rebuild on a fresh card to package as standalone .psu
        card = tmp_dir / "card.ps2"
        run(["mymcplus", card, "format"])
        run(["mymcplus", card, "mkdir", SAVE_FOLDER])
        run(["mymcplus", card, "add", "-d", SAVE_FOLDER,
             tmp_dir / "icon.sys", tmp_dir / "view.ico", tmp_dir / SAVE_FOLDER])
        run(["mymcplus", card, "export", "-p", "-o", OUTPUT_PATH, "-f", SAVE_FOLDER])

    return OUTPUT_PATH


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--memcard", type=Path, default=DEFAULT_MEMCARD,
                   help=f"Source memcard (default: {DEFAULT_MEMCARD})")
    args = p.parse_args()
    out = fetch(args.memcard)
    print(f"Wrote {out} ({out.stat().st_size} bytes)", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
