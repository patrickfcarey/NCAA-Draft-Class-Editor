#!/usr/bin/env python3
"""
Build every PS2 opening-day roster .max/.psu file we have canonical data for.

For each year, the pipeline runs:
  1. dotnet run --project NcaaDraftEditor.Cli -- compile-roster
     <canonical-roster.json> <positions.json> <template.bin> <out.bin>
  2. python tools/pack_baslus.py <out.bin> <out.<ext>> --type <preset>

Supported targets:
  m08   Madden NFL 08 PS2 (BASLUS-21638, .max output)
        Template: tests/fixtures/madden08-roster-sample.bin (vanilla 2007 roster)
  m09   Madden NFL 09 PS2 (BASLUS-21770, .psu output)
        Template: out/templates/madden-nfl-09-template.bin
        (auto-fetched from the Madden 09 Deluxe release on first use)
  m12   Madden NFL 12 PS2 (BASLUS-21946, .psu output)
        Template: out/templates/madden-nfl-12-template.bin
        (auto-fetched from the Madden 12 Deluxe release on first use)

The compiler is target-agnostic - all PS2 Madden TDBs share the same table
structure. PLAY field bit-layout shifts between versions (~74 of 110 fields
moved between M08 and M12) but the metadata-driven compiler reads offsets
from each file's own field directory, so the layout drift is transparent.

Run on Windows where the .NET SDK is installed:
    python tools/build_all_rosters.py                       # M08 (default), all years
    python tools/build_all_rosters.py --target m12          # M12, all years
    python tools/build_all_rosters.py --target all          # M08 + M09 + M12
    python tools/build_all_rosters.py --year 2017           # one year
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
TEMPLATES_DIR = OUT_DIR / "templates"

YEARS = list(range(2008, 2026))   # 2008..2025 inclusive (2026 not yet rostered)

TARGETS = {
    "m08": {
        "template_bin": REPO_ROOT / "tests" / "fixtures" / "madden08-roster-sample.bin",
        "pack_type": "roster",
        "out_subdir": "",                       # out/roster-{year}.max (back-compat)
        "out_ext": ".max",
        "label": "Madden NFL 08 PS2",
    },
    "m09": {
        "template_bin": TEMPLATES_DIR / "madden-nfl-09-template.bin",
        "pack_type": "m09-roster",
        "out_subdir": "m09",                    # out/m09/roster-{year}.psu
        "out_ext": ".psu",
        "label": "Madden NFL 09 PS2 (Deluxe-compatible)",
    },
    "m12": {
        "template_bin": TEMPLATES_DIR / "madden-nfl-12-template.bin",
        "pack_type": "m12-roster",
        "out_subdir": "m12",                    # out/m12/roster-{year}.psu
        "out_ext": ".psu",
        "label": "Madden NFL 12 PS2 (Deluxe-compatible)",
    },
}


def find_dotnet() -> str:
    found = shutil.which("dotnet")
    if found:
        return found
    win = Path("C:/Program Files/dotnet/dotnet.exe")
    if win.exists():
        return str(win)
    raise RuntimeError("dotnet not found; install the .NET 8 SDK or add it to PATH")


def ensure_template(target: str) -> None:
    """For m09/m12, auto-fetch the Deluxe template if it isn't cached yet."""
    fetch_scripts = {"m09": "fetch_m09_template.py", "m12": "fetch_m12_template.py"}
    if target not in fetch_scripts:
        return
    bin_path = TARGETS[target]["template_bin"]
    if bin_path.exists():
        return
    print(f"  {target.upper()} template missing at {bin_path}; fetching...",
          file=sys.stderr)
    fetch = REPO_ROOT / "tools" / fetch_scripts[target]
    subprocess.run([sys.executable, str(fetch)], check=True)


def out_paths(target: str, year: int) -> tuple[Path, Path]:
    cfg = TARGETS[target]
    subdir = OUT_DIR / cfg["out_subdir"] if cfg["out_subdir"] else OUT_DIR
    subdir.mkdir(parents=True, exist_ok=True)
    suffix = "" if target == "m08" else f"-{target}"
    out_bin = subdir / f"roster-{year}{suffix}.bin"
    out_pack = subdir / f"roster-{year}{cfg['out_ext']}"
    return out_bin, out_pack


def compile_year(target: str, year: int, dotnet: str) -> Path:
    cfg = TARGETS[target]
    canonical = CANONICAL / f"roster-{year}.json"
    if not canonical.exists():
        raise FileNotFoundError(f"Missing canonical roster for {year}: {canonical}")
    out_bin, _ = out_paths(target, year)

    cmd = [
        dotnet, "run", "--project", str(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-roster",
        str(canonical), str(POSITIONS), str(cfg["template_bin"]), str(out_bin),
    ]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_bin


def pack_year(target: str, year: int, bin_path: Path) -> Path:
    cfg = TARGETS[target]
    _, out_pack = out_paths(target, year)
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(out_pack), "--type", cfg["pack_type"]]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_pack


def build_target(target: str, years: list[int], dotnet: str,
                 skip_pack: bool) -> list[tuple]:
    print(f"\n### {TARGETS[target]['label']} ###", file=sys.stderr)
    ensure_template(target)
    summary = []
    for year in years:
        print(f"\n=== {target} {year} ===", file=sys.stderr)
        try:
            bin_path = compile_year(target, year, dotnet)
            if skip_pack:
                summary.append((target, year, "compiled", bin_path.name, "-"))
            else:
                pack_path = pack_year(target, year, bin_path)
                summary.append((target, year, "compiled+packed",
                                bin_path.name, pack_path.name))
        except Exception as e:
            summary.append((target, year, f"FAILED: {e}", "-", "-"))
            print(f"  ERROR: {e}", file=sys.stderr)
    return summary


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--target", choices=list(TARGETS) + ["all"], default="m08",
                   help="Which game target to build for (default: m08; 'all' builds m08+m09+m12)")
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip the .max/.psu packaging step")
    args = p.parse_args()

    years = [args.year] if args.year else YEARS
    targets = list(TARGETS) if args.target == "all" else [args.target]
    dotnet = find_dotnet()

    summary = []
    for target in targets:
        summary.extend(build_target(target, years, dotnet, args.skip_pack))

    print("\n=== Summary ===")
    print(f"{'Target':<8}{'Year':<6}{'Status':<22}{'.bin':<32}.pack")
    for tgt, y, s, b, m in summary:
        print(f"{tgt:<8}{y:<6}{s:<22}{b:<32}{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
