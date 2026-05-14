#!/usr/bin/env python3
"""
Build every Madden PS2 franchise save we have data for, across M08 / M09 / M12.

For each (target, year), the pipeline runs:

  1. dotnet run --project NcaaDraftEditor.Cli -- compile-roster
       data/canonical/roster-{year}.json
       data/mappings/positions.json
       out/templates/madden-nfl-{NN}-franchise-template.bin
       out/franchise/{target}/franchise-{year}-roster.bin

  2. dotnet run --project NcaaDraftEditor.Cli -- compile-franchise
       <intermediate>
       out/franchise/{target}/franchise-{year}.bin
       --year {year}
       --base-year {target_base_year}
       --caps data/raw/salary-caps/nfl-salary-caps.json
       --contracts data/canonical/contracts-{year}.json

  3. python tools/pack_baslus.py
       out/franchise/{target}/franchise-{year}.bin
       out/franchise/{target}/franchise-{year}.psu
       --type {target}-franchise

Targets:
  m08   Madden NFL 08 PS2 (base year 2007, BASLUS-21638BFran1)
  m09   Madden NFL 09 PS2 (base year 2008, BASLUS-21770BFran1)
  m12   Madden NFL 12 PS2 (base year 2011, BASLUS-21946BFran1)

Each target needs a user-provided Week-1-fresh franchise template at
out/templates/madden-nfl-{NN}-franchise-template.bin. M09 / M12 templates
do NOT exist in the repo and must be created by you - see the per-target
fetch_m{NN}_franchise_template.py scripts for the procedure.

Run on Windows or WSL where the .NET 8 SDK is installed:
    python tools/build_all_franchises.py                       # M08 only (default)
    python tools/build_all_franchises.py --target m09          # M09 only
    python tools/build_all_franchises.py --target m12          # M12 only
    python tools/build_all_franchises.py --target all          # M08 + M09 + M12
    python tools/build_all_franchises.py --year 2018           # one year
    python tools/build_all_franchises.py --no-contracts        # skip real-contracts
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
TEMPLATES_DIR = OUT_DIR / "templates"

YEARS = list(range(2008, 2027))  # 2008..2026 inclusive

TARGETS = {
    "m08": {
        "template_bin":  TEMPLATES_DIR / "madden-nfl-08-franchise-template.bin",
        "pack_type":     "m08-franchise",
        "base_year":     2007,
        "out_subdir":    "",                  # back-compat: out/franchise/franchise-{year}.psu
        "fetch_script":  "fetch_m08_franchise_template.py",
        "label":         "Madden NFL 08 PS2",
    },
    "m09": {
        "template_bin":  TEMPLATES_DIR / "madden-nfl-09-franchise-template.bin",
        "pack_type":     "m09-franchise",
        "base_year":     2008,
        "out_subdir":    "m09",               # out/franchise/m09/franchise-{year}.psu
        "fetch_script":  "fetch_m09_franchise_template.py",
        "label":         "Madden NFL 09 PS2 (Deluxe-compatible)",
    },
    "m12": {
        "template_bin":  TEMPLATES_DIR / "madden-nfl-12-franchise-template.bin",
        "pack_type":     "m12-franchise",
        "base_year":     2011,
        "out_subdir":    "m12",               # out/franchise/m12/franchise-{year}.psu
        "fetch_script":  "fetch_m12_franchise_template.py",
        "label":         "Madden NFL 12 PS2 (Deluxe-compatible)",
    },
}


def find_dotnet() -> tuple[str, bool]:
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


def winpath(p, translate: bool) -> str:
    if not translate:
        return str(p)
    s = str(p)
    if s.startswith("/mnt/"):
        drive = s[5].upper()
        return f"{drive}:\\" + s[7:].replace("/", "\\")
    return s


def ensure_template(target: str) -> bool:
    """Returns True if template exists (or was successfully fetched), False
    otherwise. For M09/M12 the fetch script can't succeed without a
    user-prepared memcard, so we surface a clear error if missing."""
    cfg = TARGETS[target]
    if cfg["template_bin"].exists():
        return True
    fetch = REPO_ROOT / "tools" / cfg["fetch_script"]
    print(f"  {target.upper()} franchise template missing at {cfg['template_bin']};", file=sys.stderr)
    print(f"  attempting {fetch.name} ...", file=sys.stderr)
    try:
        subprocess.run([sys.executable, str(fetch)], check=True)
        return cfg["template_bin"].exists()
    except subprocess.CalledProcessError:
        print(f"  ERROR: cannot fetch {target} franchise template.", file=sys.stderr)
        print(f"  See {fetch} for instructions on how to create one.", file=sys.stderr)
        return False


def target_out_dir(target: str) -> Path:
    sub = TARGETS[target]["out_subdir"]
    return OUT_FRANCHISE_DIR / sub if sub else OUT_FRANCHISE_DIR


def compile_year(target: str, year: int, use_contracts: bool,
                 dotnet: str, translate: bool) -> tuple[Path, bool]:
    cfg = TARGETS[target]
    roster_json = CANONICAL / f"roster-{year}.json"
    contracts_json = CANONICAL / f"contracts-{year}.json"
    stats_json = CANONICAL / f"stats-{year}.json"
    if not roster_json.exists():
        raise FileNotFoundError(f"Missing canonical roster: {roster_json}")

    out_dir = target_out_dir(target)
    out_dir.mkdir(parents=True, exist_ok=True)
    suffix = "" if target == "m08" else f"-{target}"
    intermediate = out_dir / f"franchise-{year}{suffix}-roster.bin"
    final_bin = out_dir / f"franchise-{year}{suffix}.bin"

    def w(p): return winpath(p, translate)

    cmd1 = [
        dotnet, "run", "--project", w(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-roster",
        w(roster_json), w(POSITIONS), w(cfg["template_bin"]), w(intermediate),
        "--base-year", str(cfg["base_year"]),
    ]
    if stats_json.exists():
        cmd1.extend(["--stats", w(stats_json)])
    print(f"  $ compile-roster {target} {year}" + (" (with stats)" if stats_json.exists() else ""),
          file=sys.stderr)
    subprocess.run(cmd1, check=True)

    cmd2 = [
        dotnet, "run", "--project", w(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-franchise",
        w(intermediate), w(final_bin),
        "--year", str(year),
        "--base-year", str(cfg["base_year"]),
        "--caps", w(CAPS),
    ]
    has_real = use_contracts and contracts_json.exists()
    if has_real:
        cmd2.extend(["--contracts", w(contracts_json)])
    print(f"  $ compile-franchise {target} {year}" + (f" (with real contracts)" if has_real else " (synthesized only)"),
          file=sys.stderr)
    subprocess.run(cmd2, check=True)

    return final_bin, has_real


def pack_year(target: str, year: int, bin_path: Path) -> Path:
    cfg = TARGETS[target]
    out_dir = target_out_dir(target)
    suffix = "" if target == "m08" else f"-{target}"
    psu = out_dir / f"franchise-{year}{suffix}.psu"
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(psu), "--type", cfg["pack_type"]]
    print(f"  $ pack {target} {year}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return psu


def build_target(target: str, years: list[int], use_contracts: bool,
                 dotnet: str, translate: bool, skip_pack: bool) -> list[tuple]:
    print(f"\n### {TARGETS[target]['label']} ###", file=sys.stderr)
    if not ensure_template(target):
        return [(target, y, "FAILED: template missing", "-", "-", "-") for y in years]

    summary = []
    for year in years:
        print(f"\n=== {target} {year} ===", file=sys.stderr)
        try:
            bin_path, used_real = compile_year(target, year, use_contracts, dotnet, translate)
            if skip_pack:
                summary.append((target, year, "compiled",
                                "real" if used_real else "synth",
                                bin_path.name, "-"))
            else:
                psu = pack_year(target, year, bin_path)
                summary.append((target, year, "compiled+packed",
                                "real" if used_real else "synth",
                                bin_path.name, psu.name))
        except Exception as e:
            summary.append((target, year, f"FAILED: {e}", "-", "-", "-"))
            print(f"  ERROR: {e}", file=sys.stderr)
    return summary


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--target", choices=list(TARGETS) + ["all"], default="m08",
                   help="Which game to build franchises for (default: m08; 'all' = m08+m09+m12)")
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--no-contracts", action="store_true",
                   help="Skip --contracts (rely on synthesizer for all players)")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip the .psu packaging step")
    args = p.parse_args()

    years = [args.year] if args.year else YEARS
    targets = list(TARGETS) if args.target == "all" else [args.target]
    dotnet, translate = find_dotnet()

    summary = []
    for target in targets:
        summary.extend(build_target(
            target, years, not args.no_contracts, dotnet, translate, args.skip_pack))

    print("\n=== Summary ===")
    print(f"{'Target':<8}{'Year':<6}{'Status':<22}{'Contracts':<11}{'.bin':<32}.psu")
    for tgt, y, s, c, b, m in summary:
        print(f"{tgt:<8}{y:<6}{s:<22}{c:<11}{b:<32}{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
