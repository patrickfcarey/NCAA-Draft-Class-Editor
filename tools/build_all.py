#!/usr/bin/env python3
"""
Master build wrapper. Produces every release artifact this toolchain can:

  - NCAA draft class .max/.psu files for M08, M09, M12 (2008-2026)
  - Madden PS2 opening-day roster .max/.psu files for M08, M09, M12
    (2008-2025; M09/M12 are Deluxe-compatible)
  - M08 franchise saves (calendar + cap + contracts) for 2008-2026 with real
    OvertheCap contract import

Total artifacts produced (assuming all canonical inputs are present):

  - 19 years × 3 targets = 57 draft classes (.max for m08, .psu for m09/m12)
  - 18 years × 3 targets = 54 rosters
  - 19 years × 1 target  = 19 franchise saves
  -                     = 130 artifacts

Run on Windows or WSL where the .NET 8 SDK is installed:

    python tools/build_all.py                       # build everything
    python tools/build_all.py --release             # also assemble out/release/
    python tools/build_all.py --skip-franchises     # rosters + draft classes only
    python tools/build_all.py --skip-pack           # compile .bin only, no .psu/.max

Output:
    out/draft-class-{year}.max                       (M08)
    out/m09/draft-class-{year}-m09.psu
    out/m12/draft-class-{year}-m12.psu
    out/roster-{year}.max                            (M08)
    out/m09/roster-{year}.psu
    out/m12/roster-{year}.psu
    out/franchise/franchise-{year}.psu               (M08 only - M09/M12 not yet)
    out/release/{game}/{year}/...                    (if --release passed)
"""
from __future__ import annotations

import argparse
import shutil
import subprocess
import sys
from pathlib import Path

REPO_ROOT = Path(__file__).resolve().parent.parent
OUT_DIR = REPO_ROOT / "out"
TOOLS = REPO_ROOT / "tools"


def run_sub(args: list[str], label: str) -> int:
    print(f"\n############### {label} ###############", file=sys.stderr)
    cmd = [sys.executable] + args
    print(f"$ {' '.join(cmd)}", file=sys.stderr)
    return subprocess.run(cmd).returncode


# ---------- release assembly ----------

# (target → release subdirectory, source artifact, glob)
# Each entry maps the source out/ artifact pattern to release/{game}/{year}/{file}.
RELEASE_LAYOUT = {
    "madden08": [
        # (kind, src_pattern, dst_name)
        ("draft-class", "draft-class-{year}.max",                "draft-class.max"),
        ("roster",      "roster-{year}.max",                     "roster.max"),
        ("franchise",   "franchise/franchise-{year}.psu",        "franchise.psu"),
    ],
    "madden09-deluxe": [
        ("draft-class", "m09/draft-class-{year}-m09.psu",        "draft-class.psu"),
        ("roster",      "m09/roster-{year}.psu",                 "roster.psu"),
    ],
    "madden12-deluxe": [
        ("draft-class", "m12/draft-class-{year}-m12.psu",        "draft-class.psu"),
        ("roster",      "m12/roster-{year}.psu",                 "roster.psu"),
    ],
}


def assemble_release(years: range) -> None:
    release_root = OUT_DIR / "release"
    if release_root.exists():
        shutil.rmtree(release_root)
    release_root.mkdir(parents=True)
    print(f"\n############### Assembling out/release/ ###############", file=sys.stderr)

    placed = 0
    missing = 0
    for game, entries in RELEASE_LAYOUT.items():
        for year in years:
            year_dir = release_root / game / str(year)
            for kind, src_pat, dst_name in entries:
                src = OUT_DIR / src_pat.format(year=year)
                if not src.exists():
                    missing += 1
                    continue
                year_dir.mkdir(parents=True, exist_ok=True)
                dst = year_dir / dst_name
                shutil.copy2(src, dst)
                placed += 1

    # Top-level README so the release archive is self-explanatory
    readme = release_root / "README.txt"
    readme.write_text(
        "NCAA Draft Class Editor / Madden PS2 Historical Roster Toolchain\n"
        "================================================================\n"
        "\n"
        "Each subdirectory is organized as {game}/{year}/{artifact}.\n"
        "\n"
        "Games:\n"
        "  madden08         Madden NFL 08 PS2 (vanilla)\n"
        "  madden09-deluxe  Madden NFL 09 PS2 with Deluxe ISO patch\n"
        "  madden12-deluxe  Madden NFL 12 PS2 with Deluxe ISO patch\n"
        "\n"
        "Artifacts per year:\n"
        "  draft-class.{max,psu}  Real NFL Draft of that year, imports into\n"
        "                         franchise mode after the Pro Bowl.\n"
        "  roster.{max,psu}       Real opening-day NFL roster of that year.\n"
        "  franchise.psu          (M08 only) Pre-baked franchise at Week 1 of\n"
        "                         that year, with era-correct calendar, salary\n"
        "                         cap, and per-player contracts.\n"
        "\n"
        "Importing on PCSX2:\n"
        "  mymcplus /path/to/memcard.ps2 import <artifact>\n"
        "\n"
        "See the project's README.md for full usage docs.\n"
    )
    print(f"  Placed {placed} artifacts under {release_root}", file=sys.stderr)
    if missing > 0:
        print(f"  ({missing} expected artifacts missing - probably skipped years)",
              file=sys.stderr)


def main() -> int:
    p = argparse.ArgumentParser()
    p.add_argument("--target", choices=["m08", "m09", "m12", "all"], default="all",
                   help="Which game(s) to build for (default: all)")
    p.add_argument("--year", type=int, help="Build only this year")
    p.add_argument("--skip-draft-classes", action="store_true")
    p.add_argument("--skip-rosters", action="store_true")
    p.add_argument("--skip-franchises", action="store_true")
    p.add_argument("--skip-pack", action="store_true",
                   help="Compile .bin only; skip .max/.psu packaging")
    p.add_argument("--release", action="store_true",
                   help="After builds, assemble out/release/{game}/{year}/ directory")
    args = p.parse_args()

    common = []
    if args.year:
        common += ["--year", str(args.year)]
    if args.skip_pack:
        common += ["--skip-pack"]

    failures = 0

    if not args.skip_draft_classes:
        rc = run_sub(
            [str(TOOLS / "build_all_draft_classes.py"), "--target", args.target] + common,
            f"Draft classes ({args.target})")
        if rc != 0:
            failures += 1

    if not args.skip_rosters:
        rc = run_sub(
            [str(TOOLS / "build_all_rosters.py"), "--target", args.target] + common,
            f"Rosters ({args.target})")
        if rc != 0:
            failures += 1

    if not args.skip_franchises:
        # M08 only for now (M09/M12 franchise builders not yet implemented).
        if args.target in ("m08", "all"):
            rc = run_sub(
                [str(TOOLS / "build_all_franchises.py")] + common,
                "Franchises (m08)")
            if rc != 0:
                failures += 1
        else:
            print(f"\n  (skipping franchises: --target {args.target} doesn't yet have a franchise builder)",
                  file=sys.stderr)

    if args.release:
        years_for_release = [args.year] if args.year else range(2008, 2027)
        assemble_release(years_for_release)

    print(f"\n############### build_all done ({failures} failures) ###############",
          file=sys.stderr)
    return 0 if failures == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
