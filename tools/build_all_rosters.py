#!/usr/bin/env python3
"""
Build every Madden 08 PS2 opening-day roster .max file we have canonical data for.

Loops over data/canonical/roster-{year}.json:
  1. dotnet run --project NcaaDraftEditor.Cli -- compile-roster
     <canonical-roster.json> <positions.json> <template.bin> <out.bin>
  2. python tools/pack_baslus.py <out.bin> <out.max> --type roster

Template fixture is tests/fixtures/madden08-roster-sample.bin (Madden 08
PS2's 2007 default roster). The compiler mutates PLAY records by TGID.

Run on Windows where the .NET SDK is installed:
    python tools/build_all_rosters.py
    python tools/build_all_rosters.py --year 2017     # just one year
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
TEMPLATE_BIN = REPO_ROOT / "tests" / "fixtures" / "madden08-roster-sample.bin"

YEARS = list(range(2008, 2026))   # 2008..2025 inclusive (2026 not yet rostered)


def find_dotnet() -> str:
    found = shutil.which("dotnet")
    if found:
        return found
    win = Path("C:/Program Files/dotnet/dotnet.exe")
    if win.exists():
        return str(win)
    raise RuntimeError("dotnet not found; install the .NET 8 SDK or add it to PATH")


def compile_year(year: int, dotnet: str) -> Path:
    canonical = CANONICAL / f"roster-{year}.json"
    if not canonical.exists():
        raise FileNotFoundError(f"Missing canonical roster for {year}: {canonical}")
    out_bin = OUT_DIR / f"roster-{year}.bin"
    OUT_DIR.mkdir(parents=True, exist_ok=True)

    cmd = [
        dotnet, "run", "--project", str(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-roster", str(canonical), str(POSITIONS), str(TEMPLATE_BIN), str(out_bin),
    ]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_bin


def pack_year(year: int, bin_path: Path) -> Path:
    out_max = OUT_DIR / f"roster-{year}.max"
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(out_max), "--type", "roster"]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_max


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip the .max packaging step")
    args = p.parse_args()

    years = [args.year] if args.year else YEARS
    dotnet = find_dotnet()

    summary = []
    for year in years:
        print(f"\n=== {year} ===", file=sys.stderr)
        try:
            bin_path = compile_year(year, dotnet)
            if args.skip_pack:
                summary.append((year, "compiled", bin_path.name, "-"))
            else:
                max_path = pack_year(year, bin_path)
                summary.append((year, "compiled+packed", bin_path.name, max_path.name))
        except Exception as e:
            summary.append((year, f"FAILED: {e}", "-", "-"))
            print(f"  ERROR: {e}", file=sys.stderr)

    print("\n=== Summary ===")
    print(f"{'Year':<6}{'Status':<22}{'.bin':<28}.max")
    for y, s, b, m in summary:
        print(f"{y:<6}{s:<22}{b:<28}{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
