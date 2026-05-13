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

# Save-type presets. Pick by --type flag.
PRESETS = {
    "draft-class": {
        "template": REPO_ROOT / "madden-nfl-08.26380.max",
        "save_folder": "BASLUS-21620LClass07",   # NCAA Football 08 USA, draft class export
    },
    "roster": {
        "template": REPO_ROOT / "madden-nfl-08.16516.max",
        "save_folder": "BASLUS-21638DRost5",     # Madden NFL 08 USA, roster save
    },
    "m09-roster": {
        # Madden NFL 09 PS2 uses an identical TDB schema to M08. Template is
        # the Deluxe community .psu (fetched by tools/fetch_m09_template.py)
        # because there's no vanilla M09 save checked into the repo.
        "template": REPO_ROOT / "out" / "templates" / "madden-nfl-09-template.psu",
        "save_folder": "BASLUS-21770DRost1",     # Madden NFL 09 USA, roster save
    },
    "m12-roster": {
        # Madden NFL 12 PS2. Same bare-TDB format as M08/M09 (the MC02 wrapper
        # only applies to PS3/360/PC). PLAY field bit-layout reshuffled vs M08
        # but the compiler is metadata-driven so it handles this transparently.
        # Template fetched by tools/fetch_m12_template.py.
        "template": REPO_ROOT / "out" / "templates" / "madden-nfl-12-template.psu",
        "save_folder": "BASLUS-21946DRost1",     # Madden NFL 12 USA, roster save
    },
    "m09-draft-class": {
        # NCAA Football 09 PS2's "Send to Madden" export, consumed by Madden 09.
        # Inner-file format is the same 138,240-byte / 1,600-record / 86-byte
        # NCAA binary as M08 (confirmed by community: NCAA 09->M09 import works
        # without binary changes). We reuse the M08 NCAA template's icon.sys
        # and view.ico (cosmetic only - the PS2 dashboard will show the M08
        # title); what matters is the BASLUS folder name Madden 09 looks for.
        #
        # WARNING: Suffix "LClass08" is unverified. M08 uses "LClass07" where
        # "07" is the in-game NCAA season year (NCAA 08 covers 2007 college
        # season). Extrapolating: NCAA 09 covers 2008 -> "LClass08". Will need
        # PCSX2 verification; trivial to change if a real NCAA 09 save uses a
        # different convention.
        "template": REPO_ROOT / "madden-nfl-08.26380.max",
        "template_folder": "BASLUS-21620LClass07",
        "save_folder": "BASLUS-21769LClass08",   # NCAA Football 09 USA, draft class export
    },
    "m08-franchise": {
        # Madden NFL 08 PS2 franchise save. Has a 4-byte 02 00 00 00 preamble
        # before the TDB magic, then 183 TDB tables (vs the roster's 4). The
        # MaddenFranchiseCompiler writes calendar (SEAI.SEYR) and cap economy
        # (SLRI.SCAD/SMAD/RFA1..4). Template is user-specific (a fresh franchise
        # exported from the user's own memcard); fetch via
        # tools/fetch_m08_franchise_template.py.
        "template": REPO_ROOT / "out" / "templates" / "madden-nfl-08-franchise-template.psu",
        "save_folder": "BASLUS-21638BFran1",
    },
    "m12-draft-class": {
        # Madden NFL 12 PS2 reads draft classes written by NCAA Football *11*,
        # not NCAA 12 - NCAA 12 has no PS2 release; NCAA 11 (BASLUS-21932) was
        # EA's last PS2 NCAA. Community confirms the NCAA 11 -> M12 import works
        # on the binary level. Same 138,240-byte NCAA binary format.
        #
        # WARNING: Suffix "LClass10" is unverified (NCAA 11 covers 2010 college
        # season per the M08 convention). Needs PCSX2 verification.
        "template": REPO_ROOT / "madden-nfl-08.26380.max",
        "template_folder": "BASLUS-21620LClass07",
        "save_folder": "BASLUS-21932LClass10",   # NCAA Football 11 USA, draft class export
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
    # Most presets reuse a template whose save folder name already matches the
    # target. The M09/M12 draft-class presets reuse the M08 NCAA template under
    # a different folder name, so they declare template_folder explicitly.
    template_folder: str = preset.get("template_folder", save_folder)
    inner_filename: str = save_folder

    if not compiled_bin.exists():
        raise FileNotFoundError(f"Compiled binary not found: {compiled_bin}")
    if not template_max.exists():
        hint = ""
        if save_type == "m09-roster":
            hint = " Run: python tools/fetch_m09_template.py"
        elif save_type == "m12-roster":
            hint = " Run: python tools/fetch_m12_template.py"
        elif save_type == "m08-franchise":
            hint = " Run: python tools/fetch_m08_franchise_template.py"
        elif save_type in ("m09-draft-class", "m12-draft-class"):
            hint = " (Template madden-nfl-08.26380.max should be at repo root)"
        raise FileNotFoundError(
            f"Template not found at {template_max}.{hint}")

    fmt_flag = "-m" if output_path.suffix.lower() == ".max" else "-p"

    with tempfile.TemporaryDirectory() as tmp:
        tmp_dir = Path(tmp)
        card = tmp_dir / "card.ps2"

        # Stage the .bin under the exact inner filename so mymcplus adds it correctly.
        staged_bin = tmp_dir / inner_filename
        shutil.copy2(compiled_bin, staged_bin)

        if template_folder == save_folder:
            # Fast path: template lands at the target folder name. Just swap the
            # inner data file in place.
            run(["mymcplus", card, "format"])
            run(["mymcplus", card, "import", template_max])
            run(["mymcplus", card, "remove", f"{save_folder}/{inner_filename}"])
            run(["mymcplus", card, "add", "-d", save_folder, staged_bin])
        else:
            # Rename path: template lives under template_folder. Pull icon.sys
            # and view.ico out of the imported template, wipe the card, and
            # rebuild under save_folder with the new data file. mkdir + add
            # creates a valid PS2 save folder (verified empirically with this
            # exact sequence).
            run(["mymcplus", card, "format"])
            run(["mymcplus", card, "import", template_max])
            icon_path = tmp_dir / "icon.sys"
            view_path = tmp_dir / "view.ico"
            run(["mymcplus", card, "extract", "-d", template_folder,
                 "-o", icon_path, "icon.sys"])
            run(["mymcplus", card, "extract", "-d", template_folder,
                 "-o", view_path, "view.ico"])
            # Recursively delete the template's folder so we can reuse the
            # card with the target folder name. mymcplus won't reformat an
            # existing card, so `delete` is the cleanup step.
            run(["mymcplus", card, "delete", template_folder])
            run(["mymcplus", card, "mkdir", save_folder])
            run(["mymcplus", card, "add", "-d", save_folder,
                 icon_path, view_path, staged_bin])

        output_path.parent.mkdir(parents=True, exist_ok=True)
        run(["mymcplus", card, "export", fmt_flag,
             "-o", output_path, "-f", save_folder])


def main() -> int:
    import argparse
    p = argparse.ArgumentParser()
    p.add_argument("input", help="compiled .bin to wrap")
    p.add_argument("output", help="output file (.max or .psu by extension)")
    p.add_argument("--type", choices=list(PRESETS), default=DEFAULT_PRESET,
                   help=f"save type (default: {DEFAULT_PRESET}). "
                        f"draft-class -> BASLUS-21620 NCAA 08 draft class (for M08). "
                        f"roster -> BASLUS-21638 Madden NFL 08 roster. "
                        f"m09-roster -> BASLUS-21770 Madden NFL 09 roster. "
                        f"m12-roster -> BASLUS-21946 Madden NFL 12 roster. "
                        f"m09-draft-class -> BASLUS-21769 NCAA 09 draft class (for M09). "
                        f"m12-draft-class -> BASLUS-21932 NCAA 11 draft class (for M12; "
                        f"NCAA 12 has no PS2 release). "
                        f"m08-franchise -> BASLUS-21638BFran1 Madden 08 franchise save.")
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
