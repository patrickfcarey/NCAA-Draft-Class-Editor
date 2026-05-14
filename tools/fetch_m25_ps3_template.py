#!/usr/bin/env python3
"""
Snapshot M25 PS3 roster template from RPCS3's dev_hdd0 into out/templates/.

M25 stores its "Connected Franchise" save in an opaque FrTk container
(NOT TDB) that this toolchain doesn't write. Fortunately M25 moved the
per-player contract fields (PSA0..6, PSB0..6, PCSA) into the ROSTER
PLAY table (200 fields, up from 110 in PS2 rosters). That means writing
real-historical rosters with real contracts into the roster save alone
is sufficient — the user starts a Connected Franchise from the custom
roster in-game and the cap economy is era-correct.

So we only template the roster save, not the career save.

Procedure for the user:
  1. Install Madden NFL 25 (BLUS31178) in RPCS3.
  2. Boot the game. Save a default roster (any menu that lets you save).
  3. Close RPCS3 cleanly.
  4. Run this script.
"""
from __future__ import annotations

import argparse
import shutil
import sys
from pathlib import Path

DEFAULT_DEV_HDD0 = Path("/mnt/c/Emulators/RPCS3/dev_hdd0/home/00000001/savedata")
REPO_ROOT = Path(__file__).resolve().parent.parent
TEMPLATES_DIR = REPO_ROOT / "out" / "templates"

GAME_ID = "BLUS31178"  # Madden NFL 25 PS3 (US)


def latest_save(savedata_dir: Path, kind: str) -> Path | None:
    candidates = sorted(
        savedata_dir.glob(f"{GAME_ID}-{kind}-*"),
        key=lambda p: p.stat().st_mtime,
        reverse=True,
    )
    return candidates[0] if candidates else None


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument("--savedata-dir", type=Path, default=DEFAULT_DEV_HDD0)
    args = p.parse_args()

    if not args.savedata_dir.is_dir():
        print(f"savedata-dir does not exist: {args.savedata_dir}", file=sys.stderr)
        return 2

    save = latest_save(args.savedata_dir, "ROSTER")
    if save is None:
        print(f"no {GAME_ID}-ROSTER-* save found under {args.savedata_dir}", file=sys.stderr)
        return 1
    src = save / "USR-DATA"
    if not src.exists():
        print(f"{save} has no USR-DATA inside", file=sys.stderr)
        return 1
    TEMPLATES_DIR.mkdir(parents=True, exist_ok=True)
    dst = TEMPLATES_DIR / "madden-nfl-25-ps3-roster-template.bin"
    shutil.copyfile(src, dst)
    print(f"copied {src} -> {dst} ({dst.stat().st_size:,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
