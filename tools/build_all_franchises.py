#!/usr/bin/env python3
"""
Build every Madden 08 PS2 franchise save we have data for.

For each NFL season 2008-2026 the pipeline runs:

  1. dotnet run --project NcaaDraftEditor.Cli -- compile-roster
       data/canonical/roster-{year}.json
       data/mappings/positions.json
       out/templates/madden-nfl-08-franchise-template.bin   (Week-1 fresh)
       out/franchise/franchise-{year}-roster.bin             (intermediate)

  2. dotnet run --project NcaaDraftEditor.Cli -- compile-franchise
       <intermediate>
       out/franchise/franchise-{year}.bin
       --year {year}
       --caps data/raw/salary-caps/nfl-salary-caps.json
       --contracts data/canonical/contracts-{year}.json

  3. python tools/pack_baslus.py
       out/franchise/franchise-{year}.bin
       out/franchise/franchise-{year}.psu
       --type m08-franchise

Inputs:
  - data/canonical/roster-{year}.json     (real NFL opening-day roster)
  - data/canonical/contracts-{year}.json  (real OvertheCap contracts)
  - data/raw/salary-caps/nfl-salary-caps.json (real cap + RFA tenders)
  - out/templates/madden-nfl-08-franchise-template.bin (user-provided Week 1
      fresh franchise; run tools/fetch_m08_franchise_template.py once)

Output: out/franchise/franchise-{year}.psu - mount as a PS2 memcard and
        load in Madden NFL 08 PS2.

Skips years where any required canonical file is missing (e.g. 2026
contracts may not exist on first refresh).

Run on Windows where the .NET SDK is installed:
    python tools/build_all_franchises.py                # 2008..2026 inclusive
    python tools/build_all_franchises.py --year 2018    # one year only
    python tools/build_all_franchises.py --no-contracts # skip real-contracts step
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = REPO_ROOT / "out"
OUT_FRANCHISE_DIR = OUT_DIR / "franchise"
CANONICAL = REPO_ROOT / "data" / "canonical"
POSITIONS = REPO_ROOT / "data" / "mappings" / "positions.json"
CAPS = REPO_ROOT / "data" / "raw" / "salary-caps" / "nfl-salary-caps.json"
TEMPLATE_BIN = OUT_DIR / "templates" / "madden-nfl-08-franchise-template.bin"

YEARS = list(range(2008, 2027))  # 2008..2026 inclusive


def find_dotnet() -> tuple[str, bool]:
    """Locate dotnet. Returns (path, is_windows_exe_from_wsl). When the
    second is True, callers must translate path arguments to Windows form
    via wslpath before invoking, since the .NET CLI runs in Windows-land
    and won't resolve /mnt/c/... paths."""
    found = shutil.which("dotnet")
    if found:
        return (found, False)
    for candidate in [
        Path("/mnt/c/Program Files/dotnet/dotnet.exe"),
        Path("C:/Program Files/dotnet/dotnet.exe"),
    ]:
        if candidate.exists():
            return (str(candidate), True)
    raise RuntimeError("dotnet not found; install the .NET 8 SDK or add it to PATH")


def winpath(p: Path | str, translate: bool) -> str:
    """Convert a WSL path to a Windows path when invoking the Windows .NET CLI."""
    if not translate:
        return str(p)
    s = str(p)
    if s.startswith("/mnt/c/"):
        # /mnt/c/Foo/Bar -> C:\Foo\Bar
        return "C:\\" + s[len("/mnt/c/"):].replace("/", "\\")
    if s.startswith("/mnt/"):
        drive = s[5].upper()
        return f"{drive}:\\" + s[7:].replace("/", "\\")
    return s


def ensure_template() -> None:
    if TEMPLATE_BIN.exists():
        return
    print(f"  franchise template missing at {TEMPLATE_BIN}; running fetch_m08_franchise_template.py",
          file=sys.stderr)
    fetch = REPO_ROOT / "tools" / "fetch_m08_franchise_template.py"
    subprocess.run([sys.executable, str(fetch)], check=True)


def compile_year(year: int, use_contracts: bool, dotnet: str, translate_paths: bool) -> tuple[Path, bool]:
    """Run compile-roster + compile-franchise for one year. Returns (final .bin, used_real_contracts)."""
    roster_json = CANONICAL / f"roster-{year}.json"
    contracts_json = CANONICAL / f"contracts-{year}.json"
    if not roster_json.exists():
        raise FileNotFoundError(f"Missing canonical roster: {roster_json}")

    OUT_FRANCHISE_DIR.mkdir(parents=True, exist_ok=True)
    intermediate = OUT_FRANCHISE_DIR / f"franchise-{year}-roster.bin"
    final_bin = OUT_FRANCHISE_DIR / f"franchise-{year}.bin"

    def w(p): return winpath(p, translate_paths)

    # Step 1: compile-roster (install year's players into template's PLAY)
    cmd1 = [
        dotnet, "run", "--project", w(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-roster",
        w(roster_json), w(POSITIONS), w(TEMPLATE_BIN), w(intermediate),
    ]
    print(f"  $ compile-roster {year}", file=sys.stderr)
    subprocess.run(cmd1, check=True)

    # Step 2: compile-franchise (calendar + cap + contracts)
    cmd2 = [
        dotnet, "run", "--project", w(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-franchise",
        w(intermediate), w(final_bin),
        "--year", str(year), "--caps", w(CAPS),
    ]
    has_real = use_contracts and contracts_json.exists()
    if has_real:
        cmd2.extend(["--contracts", w(contracts_json)])
    print(f"  $ compile-franchise {year}" + (f" (with real contracts)" if has_real else " (synthesized only)"),
          file=sys.stderr)
    subprocess.run(cmd2, check=True)

    return final_bin, has_real


def pack_year(year: int, bin_path: Path) -> Path:
    psu = OUT_FRANCHISE_DIR / f"franchise-{year}.psu"
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(psu), "--type", "m08-franchise"]
    print(f"  $ pack {year}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return psu


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--no-contracts", action="store_true",
                   help="Skip --contracts (rely on synthesizer for all players)")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip the .psu packaging step")
    args = p.parse_args()

    years = [args.year] if args.year else YEARS
    dotnet, translate = find_dotnet()
    ensure_template()

    summary = []
    for year in years:
        print(f"\n=== franchise {year} ===", file=sys.stderr)
        try:
            bin_path, used_real = compile_year(year, not args.no_contracts, dotnet, translate)
            if args.skip_pack:
                summary.append((year, "compiled", "real" if used_real else "synth", bin_path.name, "-"))
            else:
                psu = pack_year(year, bin_path)
                summary.append((year, "compiled+packed", "real" if used_real else "synth",
                                bin_path.name, psu.name))
        except Exception as e:
            summary.append((year, f"FAILED: {e}", "-", "-", "-"))
            print(f"  ERROR: {e}", file=sys.stderr)

    print("\n=== Summary ===")
    print(f"{'Year':<6}{'Status':<22}{'Contracts':<11}{'.bin':<32}.psu")
    for y, s, c, b, m in summary:
        print(f"{y:<6}{s:<22}{c:<11}{b:<32}{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
