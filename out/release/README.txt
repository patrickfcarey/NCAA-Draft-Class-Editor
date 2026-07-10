NCAA Draft Class Editor / Madden PS2 Historical Roster Toolchain
================================================================

Each subdirectory is organized as {game}/{year}/{artifact}.

Games:
  madden08         Madden NFL 08 PS2 (vanilla)
  madden09-deluxe  Madden NFL 09 PS2 with Deluxe ISO patch
  madden12-deluxe  Madden NFL 12 PS2 with Deluxe ISO patch

Artifacts per year:
  draft-class.{max,psu}  Real NFL Draft of that year, imports into
                         franchise mode after the Pro Bowl.
  roster.{max,psu}       Real opening-day NFL roster of that year.
  franchise.psu          (M08 only) Pre-baked franchise at Week 1 of
                         that year, with era-correct calendar, salary
                         cap, and per-player contracts.

Importing on PCSX2:
  mymcplus /path/to/memcard.ps2 import <artifact>

See the project's README.md for full usage docs.
