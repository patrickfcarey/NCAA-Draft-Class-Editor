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

# Two save-type presets. Pick by file extension or --type flag.
PRESETS = {
    "draft-class": {
        "template": REPO_ROOT / "madden-nfl-08.26380.max",
        "save_folder": "BASLUS-21620LClass07",   # NCAA Football 08 USA, draft class export
    },
    "roster": {
        "template": REPO_ROOT / "madden-nfl-08.16516.max",
        "save_folder": "BASLUS-21638DRost5",     # Madden NFL 08 USA, roster save
    },
}
DEFAULT_PRESET = "draft-class"


def run(cmd: list[str | Path]) -> None:
    """Run a command, surfacing stderr but not stdout (mymcplus prints noise)."""
    print(f"  $ {' '.join(str(c) for c in cmd)}", file=sys.stderr)
    subprocess.run([str(c) for c in cmd], check=True, capture_output=True, text=True)


def pack(compiled_bin: Path, output_path: Path, save_type: str = DEFAULT_PRESET) -> None:
    if save_type not in PRESETS:
        raise ValueError(f"Unknown save type '{save_type}'. Valid: {list(PRESETS)}")
    preset = PRESETS[save_type]
    template_max: Path = preset["template"]
    save_folder: str = preset["save_folder"]
    inner_filename: str = save_folder

    if not compiled_bin.exists():
        raise FileNotFoundError(f"Compiled binary not found: {compiled_bin}")
    if not template_max.exists():
        raise FileNotFoundError(f"Template .max not found at {template_max}")

    fmt_flag = "-m" if output_path.suffix.lower() == ".max" else "-p"

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        card = tmp_dir / "card.ps2"

        # Stage the .bin under the exact inner filename so mymcplus adds it correctly.
        staged_bin = tmp_dir / inner_filename
        shutil.copy2(compiled_bin, staged_bin)

        run(["mymcplus", card, "format"])
        run(["mymcplus", card, "import", template_max])
        run(["mymcplus", card, "remove", f"{save_folder}/{inner_filename}"])
        run(["mymcplus", card, "add", "-d", save_folder, staged_bin])
        output_path.parent.mkdir(parents=True, exist_ok=True)
        run(["mymcplus", card, "export", fmt_flag,
             "-o", output_path, "-f", save_folder])


def main() -> int:
    import argparse
    p = argparse.ArgumentParser()
    p.add_argument("input", help="compiled .bin to wrap")
    p.add_argument("output", help="output file (.max or .psu by extension)")
    p.add_argument("--type", choices=list(PRESETS), default=DEFAULT_PRESET,
                   help=f"save type (default: {DEFAULT_PRESET}). draft-class -> "
                        f"BASLUS-21620 NCAA Football 08 draft class slot. roster -> "
                        f"BASLUS-21638 Madden NFL 08 roster slot.")
    args = p.parse_args()

    compiled_bin = Path(args.input).resolve()
    output_path = Path(args.output).resolve()
    if output_path.suffix.lower() not in (".max", ".psu"):
        print(f"Output filename must end in .max or .psu (got {output_path.suffix})",
              file=sys.stderr)
        return 2

    pack(compiled_bin, output_path, args.type)
    print(f"Wrote {output_path} ({output_path.stat().st_size} bytes) "
          f"as {args.type}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
