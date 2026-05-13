#!/usr/bin/env python3
"""
Fetch the Madden NFL 09 PS2 roster template used by the build pipeline.

We need a real M09 PS2 save to source two things:
  1. The PS2 dashboard metadata (icon.sys + view.ico) that pack_baslus.py
     copies into our generated .psu so PCSX2 displays it correctly.
  2. The inner TDB binary (a real M09 roster) that the compiler mutates -
     it preserves TEAM/DCHT/INJY and replaces PLAY records by TGID.

The Madden Deluxe community publishes a 2026-2027 M09 roster as a .psu on
GitHub releases. We use it as our template because:
  - M09 TDB schema is byte-identical to M08, so the file format works.
  - Deluxe-side modifications to TEAM/DCHT (extra uniform slots, depth
    chart) carry through, which is what users running the Deluxe ISO
    patch expect their roster to contain.

Outputs (idempotent - won't redownload if files already exist):
  out/templates/madden-nfl-09-template.psu    full PS2 save (for pack_baslus.py)
  out/templates/madden-nfl-09-template.bin    inner TDB (for compile-roster)

Usage:
    python tools/fetch_m09_template.py
    python tools/fetch_m09_template.py --force    # redownload even if cached
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
PSU_PATH = TEMPLATES_DIR / "madden-nfl-09-template.psu"
BIN_PATH = TEMPLATES_DIR / "madden-nfl-09-template.bin"

DELUXE_PSU_URL = (
    "https://github.com/maddendeluxe/madden09deluxe/releases/download/"
    "v0.2-beta/SLUS-21770.Madden.NFL.Rost.Rost1.D887FA61.psu"
)
SAVE_FOLDER = "BASLUS-21770DRost1"
EXPECTED_INNER_SIZE = 246784  # bytes; M09 TDB is identical size to M08


def download(url: str, dest: Path) -> None:
    print(f"  downloading {url}", file=sys.stderr)
    with urllib.request.urlopen(url, timeout=60) as resp:
        dest.write_bytes(resp.read())
    print(f"  wrote {dest} ({dest.stat().st_size} bytes)", file=sys.stderr)


def extract_inner(psu: Path, out_bin: Path) -> None:
    """Use mymcplus to pull the inner BASLUS-21770DRost1 file out of the .psu."""
    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        card = tmp_dir / "card.ps2"
        subprocess.run(["mymcplus", str(card), "format"],
                       check=True, capture_output=True)
        subprocess.run(["mymcplus", str(card), "import", str(psu)],
                       check=True, capture_output=True)
        # Extract writes to CWD; do it from a clean tempdir.
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
                f"M09 roster format may have changed.")
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
