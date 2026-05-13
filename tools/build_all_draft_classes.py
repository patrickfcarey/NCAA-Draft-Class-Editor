#!/usr/bin/env python3
"""
Build every NCAA draft class .max file we have canonical data for.

Loops 2008..2026:
  1. dotnet run --project NcaaDraftEditor.Cli -- compile <canonical>
     <positions> <colleges> <out.bin> [--madden <madden>] --filler <sample>
     --lock-draft-order
  2. python tools/pack_baslus.py <out.bin> <out.max>

Madden launch-ratings file is paired by NFL season year:
  2008 -> madden09-2008.json   (Madden NFL 09 has the 2008 rookies)
  2009 -> madden10-2009.json
  ...
  2013 -> madden25-2013.json   (anniversary edition)
  ...
  2023 -> madden24-2023.json

2024, 2025, 2026 don't have Madden launch ratings on weebly yet, so the
compile is run without --madden (rookies get default ratings, anchored
via --lock-draft-order so the auto-draft order still matches reality).

Run on Windows where the .NET SDK is installed:
    python tools/build_all_draft_classes.py                # M08 only (default)
    python tools/build_all_draft_classes.py --target m09   # M09 only
    python tools/build_all_draft_classes.py --target m12   # M12 only
    python tools/build_all_draft_classes.py --target all   # all three

All outputs land under out/ (M08 at out/, M09 at out/m09/, M12 at out/m12/).
Pass --year YYYY to build just one year.
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = REPO_ROOT / "out"

CANONICAL = REPO_ROOT / "data" / "canonical"
POSITIONS = REPO_ROOT / "data" / "mappings" / "positions.json"
COLLEGES = REPO_ROOT / "data" / "mappings" / "colleges.json"
FILLER = REPO_ROOT / "tests" / "fixtures" / "sample.bin"
MADDEN_DIR = REPO_ROOT / "data" / "raw" / "madden-ratings"

SEASON_TO_MADDEN_VERSION = {
    2008: "09", 2009: "10", 2010: "11", 2011: "12", 2012: "13",
    2013: "25",   # anniversary edition
    2014: "15", 2015: "16", 2016: "17", 2017: "18", 2018: "19",
    2019: "20", 2020: "21", 2021: "22", 2022: "23", 2023: "24",
}

YEARS = list(range(2008, 2027))   # 2008..2026 inclusive

# Targets the bulk builder can emit. The .bin produced by the compiler is the
# same NCAA-format binary in all cases; only the BASLUS save-folder name (set
# by pack_baslus.py's --type) and output directory differ.
TARGETS = {
    "m08": {"pack_type": "draft-class",    "out_subdir": "."},
    "m09": {"pack_type": "m09-draft-class", "out_subdir": "m09"},
    "m12": {"pack_type": "m12-draft-class", "out_subdir": "m12"},
}


def find_dotnet() -> str:
    """Locate the dotnet executable, preferring PATH but falling back to the
    common Windows install path so this script works in a vanilla shell."""
    candidate = shutil.which("dotnet")
    if candidate:
        return candidate
    win = Path("C:/Program Files/dotnet/dotnet.exe")
    if win.exists():
        return str(win)
    raise RuntimeError("dotnet not found; install the .NET 8 SDK or add it to PATH")


def compile_year(year: int, dotnet: str) -> Path:
    canonical = CANONICAL / f"draft-class-{year}.json"
    if not canonical.exists():
        raise FileNotFoundError(f"Missing canonical draft class for {year}: {canonical}")
    out_bin = OUT_DIR / f"draft-class-{year}.bin"
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    cmd = [
        dotnet, "run", "--project", str(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile", str(canonical), str(POSITIONS), str(COLLEGES), str(out_bin),
        "--filler", str(FILLER), "--lock-draft-order",
    ]
    madden_version = SEASON_TO_MADDEN_VERSION.get(year)
    if madden_version is not None:
        madden_path = MADDEN_DIR / f"madden{madden_version}-{year}.json"
        if madden_path.exists():
            cmd.extend(["--madden", str(madden_path)])
        else:
            print(f"  WARNING: expected Madden file {madden_path.name} not found; "
                  f"using defaults for {year}", file=sys.stderr)
    else:
        print(f"  (no Madden launch ratings available for {year}; using defaults + "
              "draft-order anchoring)", file=sys.stderr)

    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_bin


def pack_year(year: int, bin_path: Path, target: str) -> Path:
    cfg = TARGETS[target]
    out_dir = OUT_DIR / cfg["out_subdir"] if cfg["out_subdir"] != "." else OUT_DIR
    out_dir.mkdir(parents=True, exist_ok=True)
    suffix = "" if target == "m08" else f"-{target}"
    # Use .psu for the M09/M12 case to match the roster pipeline's container
    # choice; M08 keeps the historical .max convention.
    ext = ".max" if target == "m08" else ".psu"
    out_path = out_dir / f"draft-class-{year}{suffix}{ext}"
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(out_path), "--type", cfg["pack_type"]]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_path


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--target", choices=list(TARGETS) + ["all"], default="m08",
                   help="Pack target. m08 -> Madden 08 PS2 (.max via BASLUS-21620). "
                        "m09 -> Madden 09 PS2 (.psu via BASLUS-21769). "
                        "m12 -> Madden 12 PS2 (.psu via BASLUS-21932). "
                        "all -> emit all three.")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip the .max/.psu packaging step")
    args = p.parse_args()

    years = [args.year] if args.year else YEARS
    targets = list(TARGETS) if args.target == "all" else [args.target]
    dotnet = find_dotnet()

    summary = []
    for year in years:
        print(f"\n=== {year} ===", file=sys.stderr)
        try:
            bin_path = compile_year(year, dotnet)
        except Exception as e:
            summary.append((year, "-", f"FAILED compile: {e}", "-"))
            print(f"  ERROR: {e}", file=sys.stderr)
            continue
        if args.skip_pack:
            summary.append((year, "all", "compiled", bin_path.name))
            continue
        for target in targets:
            try:
                out_path = pack_year(year, bin_path, target)
                summary.append((year, target, "compiled+packed", out_path.name))
            except Exception as e:
                summary.append((year, target, f"FAILED pack: {e}", "-"))
                print(f"  ERROR ({target}): {e}", file=sys.stderr)

    print("\n=== Summary ===")
    print(f"{'Year':<6}{'Target':<8}{'Status':<22}Output")
    for y, t, s, o in summary:
        print(f"{y:<6}{t:<8}{s:<22}{o}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
