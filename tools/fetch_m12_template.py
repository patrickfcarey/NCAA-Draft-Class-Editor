#!/usr/bin/env python3
"""
Fetch the Madden NFL 12 PS2 roster template used by the build pipeline.

M12 PS2 uses the same bare TDB format as M08/M09 (no MC02 wrapper - that's
PS3/360/PC only). Schema is byte-identical to M08 at the table level; only
the PLAY record bit-layout shifted (~74 of 110 fields, e.g. PCMT widened
from 10 to 11 bits). Our compiler is metadata-driven so it handles M12
without code changes.

Outputs (idempotent - won't redownload if files already exist):
  out/templates/madden-nfl-12-template.psu    full PS2 save (for pack_baslus.py)
  out/templates/madden-nfl-12-template.bin    inner TDB (for compile-roster)

Usage:
    python tools/fetch_m12_template.py
    python tools/fetch_m12_template.py --force    # redownload even if cached
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
import tempfile
import urllib.request
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
TEMPLATES_DIR = REPO_ROOT / "out" / "templates"
PSU_PATH = TEMPLATES_DIR / "madden-nfl-12-template.psu"
BIN_PATH = TEMPLATES_DIR / "madden-nfl-12-template.bin"

DELUXE_PSU_URL = (
    "https://github.com/maddendeluxe/madden12deluxe/releases/download/"
    "v0.2-beta/SLUS-21946.Madden.NFL.Rost.Rost1.B949F683.psu"
)
SAVE_FOLDER = "BASLUS-21946DRost1"
EXPECTED_INNER_SIZE = 246783  # M12 is 1 byte smaller than M08/M09's 246,784


def download(url: str, dest: Path) -> None:
    print(f"  downloading {url}", file=sys.stderr)
    with urllib.request.urlopen(url, timeout=60) as resp:
        dest.write_bytes(resp.read())
    print(f"  wrote {dest} ({dest.stat().st_size} bytes)", file=sys.stderr)


def extract_inner(psu: Path, out_bin: Path) -> None:
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        card = tmp_dir / "card.ps2"
        subprocess.run(["mymcplus", str(card), "format"],
                       check=True, capture_output=True)
        subprocess.run(["mymcplus", str(card), "import", str(psu)],
                       check=True, capture_output=True)
        subprocess.run(["mymcplus", str(card), "extract", SAVE_FOLDER],
                       check=True, capture_output=True, cwd=tmp_dir)
        extracted = tmp_dir / SAVE_FOLDER
        if not extracted.exists():
            raise RuntimeError(
                f"mymcplus did not produce {extracted}; .psu structure unexpected")
        shutil.copy2(extracted, out_bin)
        size = out_bin.stat().st_size
        if size != EXPECTED_INNER_SIZE:
            raise RuntimeError(
                f"Inner TDB is {size} bytes, expected {EXPECTED_INNER_SIZE}. "
                f"M12 roster format may have changed.")
        print(f"  extracted {out_bin} ({size} bytes)", file=sys.stderr)


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--force", action="store_true",
                   help="Redownload and re-extract even if cached files exist")
    args = p.parse_args()

    TEMPLATES_DIR.mkdir(parents=True, exist_ok=True)

    if args.force or not PSU_PATH.exists():
        download(DELUXE_PSU_URL, PSU_PATH)
    else:
        print(f"  using cached {PSU_PATH}", file=sys.stderr)

    if args.force or not BIN_PATH.exists():
        extract_inner(PSU_PATH, BIN_PATH)
    else:
        print(f"  using cached {BIN_PATH}", file=sys.stderr)

    print(f"\nReady. Templates in {TEMPLATES_DIR}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
