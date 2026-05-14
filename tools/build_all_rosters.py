#!/usr/bin/env python3
"""
Build every PS2 opening-day roster .max/.psu file we have canonical data for.

For each year, the pipeline runs:
  1. dotnet run --project NcaaDraftEditor.Cli -- compile-roster
     <canonical-roster.json> <positions.json> <template.bin> <out.bin>
  2. python tools/pack_baslus.py <out.bin> <out.<ext>> --type <preset>

Supported targets:
  m08      Madden NFL 08 PS2 (BASLUS-21638, .max output)
           Template: tests/fixtures/madden08-roster-sample.bin (vanilla 2007 roster)
  m09      Madden NFL 09 PS2 (BASLUS-21770, .psu output)
           Template: out/templates/madden-nfl-09-template.bin
           (auto-fetched from the Madden 09 Deluxe release on first use)
  m12      Madden NFL 12 PS2 (BASLUS-21946, .psu output)
           Template: out/templates/madden-nfl-12-template.bin
           (auto-fetched from the Madden 12 Deluxe release on first use)
  m12-ps3  Madden NFL 12 PS3 (BLUS30770, USR-DATA output - no PFD wrapper)
           Template: out/templates/madden-nfl-12-ps3-roster-template.bin
           (snapshot from RPCS3 dev_hdd0 via fetch_m12_ps3_template.py)
  m25-ps3  Madden NFL 25 PS3 (BLUS31178, USR-DATA output - no PFD wrapper)
           Template: out/templates/madden-nfl-25-ps3-roster-template.bin
           (snapshot from RPCS3 dev_hdd0 via fetch_m25_ps3_template.py)
           Note: M25 roster PLAY has 200 fields including contracts
           (PSA0..6, PSB0..6, PCSA) — no separate franchise build needed.

The compiler is target-agnostic - all Madden TDBs share the same table
structure. Endian (LE for PS2, BE for PS3) is auto-detected at MaddenTdb
load time, so the same compile-roster CLI command works for every target.
PLAY field bit-layouts vary between versions (74-out-of-110 fields moved
between M08 and M12; M25 has 200 fields including contracts) but the
metadata-driven compiler reads offsets from each file's own field
directory, so the layout drift is transparent.

PS3 targets produce a bare USR-DATA file (the TDB blob); the user copies
it into RPCS3's dev_hdd0 save folder manually. RPCS3 doesn't enforce the
PARAM.PFD integrity layer on dev_hdd0 saves, so no resigning is needed.

Run on Windows where the .NET SDK is installed:
    python tools/build_all_rosters.py                       # M08 (default), all years
    python tools/build_all_rosters.py --target m12          # M12 PS2, all years
    python tools/build_all_rosters.py --target m12-ps3      # M12 PS3, all years
    python tools/build_all_rosters.py --target m25-ps3      # M25 PS3, all years
    python tools/build_all_rosters.py --target all          # all PS2 + all PS3
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
    "m12-ps3": {
        "template_bin": TEMPLATES_DIR / "madden-nfl-12-ps3-roster-template.bin",
        "pack_type": None,                      # PS3 dev_hdd0 has no PFD; USR-DATA is the final form
        "out_subdir": "m12-ps3",                # out/m12-ps3/roster-{year}.bin
        "out_ext": ".bin",
        "label": "Madden NFL 12 PS3 (RPCS3 dev_hdd0)",
        "fetch_script": "fetch_m12_ps3_template.py",
    },
    "m25-ps3": {
        "template_bin": TEMPLATES_DIR / "madden-nfl-25-ps3-roster-template.bin",
        "pack_type": None,                      # PS3 dev_hdd0 has no PFD; USR-DATA is the final form
        "out_subdir": "m25-ps3",                # out/m25-ps3/roster-{year}.bin
        "out_ext": ".bin",
        "label": "Madden NFL 25 PS3 (RPCS3 dev_hdd0; contracts in roster PLAY)",
        "fetch_script": "fetch_m25_ps3_template.py",
    },
}


def find_dotnet() -> tuple[str, bool]:
    """Returns (dotnet path, translate_paths_to_windows_form)."""
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


def ensure_template(target: str) -> None:
    """For m09/m12/m12-ps3/m25-ps3, auto-fetch the template if it isn't cached.
    PS3 fetch scripts pull from the user's RPCS3 dev_hdd0; PS2 fetch scripts
    download Deluxe community releases from GitHub."""
    fetch_scripts = {
        "m09":     "fetch_m09_template.py",
        "m12":     "fetch_m12_template.py",
        "m12-ps3": TARGETS["m12-ps3"].get("fetch_script", ""),
        "m25-ps3": TARGETS["m25-ps3"].get("fetch_script", ""),
    }
    if target not in fetch_scripts or not fetch_scripts[target]:
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
    # For PS3 targets with pack_type=None, the .bin IS the deliverable.
    # Use the same path for both out_bin and out_pack so callers don't break.
    if cfg.get("pack_type") is None:
        return out_bin, out_bin
    out_pack = subdir / f"roster-{year}{cfg['out_ext']}"
    return out_bin, out_pack


def compile_year(target: str, year: int, dotnet: str, translate: bool) -> Path:
    cfg = TARGETS[target]
    canonical = CANONICAL / f"roster-{year}.json"
    if not canonical.exists():
        raise FileNotFoundError(f"Missing canonical roster for {year}: {canonical}")
    out_bin, _ = out_paths(target, year)
    def w(p): return winpath(p, translate)

    cmd = [
        dotnet, "run", "--project", w(REPO_ROOT / "NcaaDraftEditor.Cli"),
        "--", "compile-roster",
        w(canonical), w(POSITIONS), w(cfg["template_bin"]), w(out_bin),
    ]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_bin


def pack_year(target: str, year: int, bin_path: Path) -> Path:
    cfg = TARGETS[target]
    # PS3 targets (pack_type=None) need no pack step — the compiled USR-DATA
    # is the final deliverable. User copies it into dev_hdd0 manually.
    if cfg.get("pack_type") is None:
        return bin_path
    _, out_pack = out_paths(target, year)
    cmd = [sys.executable, str(REPO_ROOT / "tools" / "pack_baslus.py"),
           str(bin_path), str(out_pack), "--type", cfg["pack_type"]]
    print(f"  $ {' '.join(cmd)}", file=sys.stderr)
    subprocess.run(cmd, check=True)
    return out_pack


def build_target(target: str, years: list[int], dotnet: str, translate: bool,
                 skip_pack: bool) -> list[tuple]:
    print(f"\n### {TARGETS[target]['label']} ###", file=sys.stderr)
    ensure_template(target)
    summary = []
    for year in years:
        print(f"\n=== {target} {year} ===", file=sys.stderr)
        try:
            bin_path = compile_year(target, year, dotnet, translate)
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
    dotnet, translate = find_dotnet()

    summary = []
    for target in targets:
        summary.extend(build_target(target, years, dotnet, translate, args.skip_pack))

    print("\n=== Summary ===")
    print(f"{'Target':<8}{'Year':<6}{'Status':<22}{'.bin':<32}.pack")
    for tgt, y, s, b, m in summary:
        print(f"{tgt:<8}{y:<6}{s:<22}{b:<32}{m}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
