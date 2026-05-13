# Project context for AI agents

This file is the load-bearing handoff doc. Read it first, then look at the code.

## What this is

A toolchain to keep **Madden NFL 08 (PS2)** playable indefinitely by producing
historically accurate game artifacts for every NFL season 2008–2026:

1. **NCAA-format draft class files** — one per year. Holds the real NFL Draft
   of year N (e.g., 2018 = Baker Mayfield, Saquon Barkley, …). Imports into
   Madden 08 franchise mode after the Pro Bowl to seed the upcoming draft.
2. **Madden 08 PS2 opening-day roster files** — one per year. Holds the
   real opening-day NFL roster of season N. Lets you start a fresh franchise
   at any historical year instead of simming forward from 2007.

The vehicle game (Madden 08 PS2) never changes. We produce the *content*
that gets imported into it. End user plays Madden 08 on PCSX2, imports our
files via a PS2 memcard, and gets a franchise that mirrors NFL history.

## Status snapshot (branch `dev`)

| Tier | What | State |
|---|---|---|
| 1 | Headless NCAA file Core + JSON converter + CLI | ✅ Done |
| 2 | Canonical real-world JSON schema | ✅ Done |
| 3 | Scrapers (nflverse drafts, wikipedia 2026, weebly Madden) | ✅ Done (16 of 19 Madden years; gaps documented) |
| 4 | Compiler canonical→binary + position/college mapping | ✅ MVP done; 2018 verified end-to-end in PCSX2 |
| 5 | College mapping table (folded into Tier 4) | ✅ Done |
| 6 | Madden 08 PS2 roster builder | ✅ TDB read/write (Python + C#, byte-exact roundtrip); CanonicalRoster schema; MaddenRosterCompiler; nflverse roster scraper; CLI compile-roster; 18 canonical rosters generated; pack_baslus.py extended to --type roster; build_all_rosters.py loop ready |
| 7 | Pipeline + releases (one-shot build all 38 artifacts) | ⬜ Not started |
| 8 | PCSX2 verification | ✅ 2018 verified; other years pending Tier 7 |
| 9 | Madden 09 PS2 (Deluxe-compatible) roster builder | ✅ M09 TDB schema confirmed byte-identical to M08; pack_baslus.py m09-roster preset (BASLUS-21770); fetch_m09_template.py auto-downloads Deluxe .psu; build_all_rosters.py --target m09; 2018 compile+pack verified end-to-end |
| 10 | Madden 12 PS2 (Deluxe-compatible) roster builder | ✅ M12 PS2 is bare TDB (MC02 wrapper is PS3/360/PC-only); PLAY field bit-layout shifted vs M08 (~74 of 110 fields) but metadata-driven compiler handles transparently; pack_baslus.py m12-roster preset (BASLUS-21946); fetch_m12_template.py auto-downloads Deluxe .psu; build_all_rosters.py --target m12; 2018 compile+pack verified end-to-end |
| 11 | NCAA draft class import for M09 / M12 (vanilla + Deluxe) | ✅ Format identical to M08 NCAA binary (138,240 bytes). pack_baslus.py m09-draft-class (BASLUS-21769LClass08, for NCAA 09→M09) and m12-draft-class (BASLUS-21932LClass10, for NCAA 11→M12; NCAA 12 has no PS2 release). build_all_draft_classes.py --target m08\|m09\|m12\|all. PCSX2 verification of BASLUS suffix convention pending. |
| 12 | Madden 08 franchise compiler (Phase 1: calendar + cap economy) | ✅ MaddenTdb preamble support (franchise saves prepend 02 00 00 00 before DB magic). MaddenFranchiseCompiler writes SEAI.SEYR (calendar year offset from base 2007) and SLRI.SCAD/SMAD/RFA1..4 (real-dollar cap economy). data/raw/salary-caps/nfl-salary-caps.json carries real NFL cap + RFA tenders 2007–2026. CLI compile-franchise. pack_baslus.py m08-franchise preset. fetch_m08_franchise_template.py extracts template from user memcard. 2018 smoke test verified (SEYR=11, SCAD=$177.2M) end-to-end through pack/unpack roundtrip. PCSX2 verification pending. |
| 13 | Phase 2: per-player contract synthesizer | ⬜ Not started. Franchise PLAY has 21 extra fields vs roster PLAY: PSA0..6 (annual salary, 14-bit), PSB0..6 (signing bonus, 13-bit), PCSA (cap hit), PCTS, PSBO, PSBS. Need rating+age+position model to populate. Roster save has only PCON (4-bit contract length) and PYRP (years pro); game auto-generates contracts on franchise start from those. |
| 14 | Phase 3: real contract data (Spotrac/OvertheCap import) | ⬜ Not started. |
| 15 | M09 / M12 franchise compiler | ⬜ Not started. Templates not fetched; field schema not yet diffed against M08 franchise. Expected to share most table shape. |

End-to-end works for one year (2018). User has loaded the compiled file in
Madden 08 on PCSX2 and seen Mayfield/Barkley/etc. in the draft pool.

## Repo layout

```
.
├── NcaaDraftEditor.Core/        C# .NET 8. Binary format I/O (DraftClassFile,
│                                PlayerRecord, FieldMap), catalogs, JSON
│                                converter. Cross-platform.
├── NcaaDraftEditor.Canonical/   C# .NET 8. Real-world JSON schema (DTOs +
│                                CanonicalJson load/save). No deps on Core.
├── NcaaDraftEditor.Compiler/    C# .NET 8. Canonical -> binary glue.
│                                PositionMapper, CollegeMapper, MaddenRoster,
│                                MaddenToNcaa, DraftClassCompiler.
├── NcaaDraftEditor.Cli/         C# .NET 8. ncaa-draft executable:
│                                dump | build | roundtrip | new | compile
├── NcaaDraftEditor.WinForms/    C# .NET 8 Windows. Interactive editor;
│                                debug viewer for binary files.
├── NcaaDraftEditor.Tests/       xUnit. ~25 tests across format roundtrip,
│                                canonical, mappers, and compiler.
├── data/
│   ├── canonical/               draft-class-{year}.json, 2008-2026
│   ├── mappings/                positions.json, colleges.json
│   └── raw/madden-ratings/      madden{NN}-{YYYY}.json per game year
├── scrapers/
│   ├── nflverse/                Python: build_draft_class.py (uses
│   │                            players.csv + combine.csv)
│   ├── wikipedia/               Python: build_draft_class.py (fallback when
│   │                            nflverse hasn't backfilled, e.g. 2026)
│   └── maddenratings/           Python: build_madden_year.py (weebly XLSX)
├── tools/
│   ├── build_college_mapping.py  Auto-derives colleges.json
│   └── pack_baslus.py            Wraps mymcplus to repack compiled binary
│                                 into a .max or .psu for PCSX2 import
├── tests/fixtures/
│   ├── sample.bin                Real NCAA draft class file
│   │                             (BASLUS-21620LClass07, 138,240 bytes)
│   └── madden08-roster-sample.bin Real Madden 08 roster file
│                                  (BASLUS-21638DRost5, 246,784 bytes)
├── madden-nfl-08.26380.max       Original .max for the NCAA draft class
├── madden-nfl-08.16516.max       Original .max for the Madden roster
├── out/                          gitignored — compiled binaries land here
└── ../madden-db-editor/          Sibling repo. Electron/Vue app, the existing
                                  reader/writer for the EA TDB format. Reference
                                  for Tier 6.
```

## File formats

### MaxDrive container (.max / "Ps2PowerSave")

PS2 memory card save export format, from Action Replay MAX / SharkPort era.
Layout:

| Offset | Size | Field |
|---|---|---|
| 0x00 | 12 | Magic: `"Ps2PowerSave"` |
| 0x0C | 4 | CRC32 of the file with this area zeroed |
| 0x10 | 32 | Directory name (e.g. `"BASLUS-21620LClass07"`) |
| 0x30 | 32 | Display title (e.g. `"NCAA Draft Class"`) |
| 0x50 | 4 | Compressed data length |
| 0x54 | 4 | Number of files in the save |
| 0x58 | 4 | Uncompressed data length |
| 0x5C | … | LZSS-Ari compressed stream containing the file table + contents |

Don't reinvent the codec — `mymcplus` (`pip install mymcplus`) handles it.

### NCAA draft class binary (BASLUS-21620LClass07)

Produced by NCAA Football 08 PS2 via "Send to Madden". The user game is
NCAA but the file is read by Madden. Exactly **138,240 bytes** = 270 ×
512-byte sectors. Layout:

| Offset | Size | Contents |
|---|---|---|
| 0x00 | 4 | Magic header: `46 00 40 06` |
| 0x04 | 1600 × 86 = 137,600 | Player records |
| 0x21984 | 636 | Zero trailer (pads file to 270-sector boundary) |

**Always 1600 records.** Madden 08 hangs on "initializing roster management"
if any of the 1600 slots is empty/zeroed. Most of them must be valid college
players (the eligible senior + early-entry pool, not just the ~250 that
actually get drafted). Real picks go in the first N slots; the rest is
filled from a template file (`tests/fixtures/sample.bin`) with ratings
capped to OVR 49 so Madden's auto-draft picks our real players first.

Per-record layout: see `NcaaDraftEditor.Core/PlayerRecord.cs` and
`FieldMap.cs`. Highlights:

| Bytes | Field | Notes |
|---|---|---|
| 0 | PFMP | Face appearance |
| 4 | TGID | College (NCAA 06 catalog ID, 1–254; 255 = N/A) |
| 6–16 | FirstName | 11-byte ASCII, zero-padded |
| 17–30 | LastName | 14-byte ASCII, zero-padded |
| 31 | PYER | College year (we encode FR=0/SO=1/JR=2/SR=3/GS=4) |
| 32 | PRSD | Redshirt flag (we encode 0/1) |
| 33 | POVR | Overall rating |
| 34 | PJEN | Jersey number |
| 35 | PPOS | Position (0=QB, 1=HB … 20=P) |
| 36 | PWGT | Weight (lb) |
| 37 | PHGT | Height (inches) |
| 38–58 | 21 rating bytes | PSTR, PAGI, PSPD, PACC, PAWR, … |
| 60–85 | Appearance | Hair, face, skin, gear |

**7 bytes still unmapped:** 1, 5, 59, 69, 70, 73, 84. None of them carry
a 0–99 rating-scale value (verified by profiling `sample.bin` across all
1600 records). Probably reserved or used by NCAA-side scouting we don't
care about.

**No player-potential byte.** Madden 08 derives potential at import time
from age + OVR + position. The NCAA file doesn't carry it.

### Madden 08 PS2 roster binary (BASLUS-21638DRost5)

Produced by Madden NFL 08 PS2 itself. EA's **TDB** (tabular database)
format. Sample is 246,784 bytes.

**ENDIANNESS: PS2 is little-endian.** The bep713 madden-db-editor parser
(at `../madden-db-editor/src/worker/TableReader.js`) reads big-endian
and would target a different EA platform variant (PS3/PC). For PS2 the
fields must be read LE. Reverse-engineered and verified empirically by
`tools/parse_madden_tdb.py`.

#### File header (24 bytes)

| Offset | Size | Field | Notes |
|---|---|---|---|
| 0x00 | 2 | magic | ASCII `"DB"` (= 0x4244 LE) |
| 0x02 | 2 | version | 0x0008 LE → 8 |
| 0x04 | 4 | unknown1 | |
| 0x08 | 4 | dbSize | 245,856 in sample |
| 0x0C | 4 | zero | |
| 0x10 | 4 | tableCount | 4 for a roster |
| 0x14 | 4 | checksum | |

#### Table directory (24 bytes onward, 8 bytes per entry)

| Offset | Size | Field |
|---|---|---|
| +0 | 4 | ASCII table name, forward direction (e.g. `"DCHT"`) |
| +4 | 4 | LE offset, **relative to end of table directory** (= file offset 24 + tableCount×8) |

#### Per-table header (40 bytes at directory-end + offset)

Bytes 0–3 priorcrc; 4–7 unknown; 8–11 lenBytes (record stride in bytes);
12–15 lenBits (record stride in bits — bit-packed); 16–19 zero; 20–21
maxRecords (LE word); 22–23 curRecords (LE word); 24–27 unknown;
byte 28 numFields; byte 29 indexCount; 30–31 zero2; 32–35 zero3;
36–39 headercrc.

#### Field directory (16 bytes per field, immediately after table header)

| Offset | Size | Field |
|---|---|---|
| +0 | 4 | type (0=STRING, 1=BINARY, 2=SINT, 3=UINT, 4=FLOAT) |
| +4 | 4 | bit offset within record |
| +8 | 4 | ASCII field name, forward (e.g. `"PFNA"`, `"PSPD"`) |
| +12 | 4 | bit width |

#### Records (immediately after field directory)

Records are tightly **bit-packed** — fields don't align to byte boundaries.
Each record is `lenBytes` bytes (with `lenBits` significant bits). UINT
fields use MSB-first bit reading within the record byte sequence. STRING
fields ARE byte-aligned (offset is always a multiple of 8) and read as
ASCII with null termination.

#### The 4 roster tables (from the sample)

| Name | curRecords/max | lenBytes | numFields | Purpose |
|---|---|---|---|---|
| DCHT | 1745 / 2912 | 8 (63 bits) | 4 | Depth chart (PGID, TGID, PPOS, ddep) |
| INJY | 115 / 320 | 8 (63 bits) | 5 | Injuries (PGID, TGID, INJL, INIR, INJT) |
| PLAY | 1995 / 2048 | 104 (831 bits) | 110 | Players (all attributes - 50+ rating + bio fields) |
| TEAM | 33 / 33 | 116 (927 bits) | 66 | Teams (32 NFL + 1 free agents bucket) |

PLAY field codes share many with NCAA's 86-byte record (`PSPD`, `PACC`,
`PSTR`, `PAGI`, `PAWR`, `PCTH`, `PCAR`, `PTHP`, `PTHA`, `PKAC`, `PJMP`,
`PINJ`, `PSTA`, etc.), plus name (`PFNA`, `PLNA`), age (`PAGE`), and
position (`PPOS`). The 21 NCAA-side rating fields almost certainly map
to identically named PLAY fields here — that makes the Tier 6 compiler's
field-mapping work mostly trivial. Confirmed by spot-checking record 0
of PLAY against the 2007 Chicago Bears roster: Olin Kreutz, Brian
Urlacher, Brian Griese, Muhsin Muhammad, all present with correct
TGID, PPOS, names.

Use `tools/parse_madden_tdb.py <file> [out.json]` to dump the full
parse for any TDB file in the working directory.

### Madden 08 PS2 franchise binary (BASLUS-21638BFran1)

Same TDB family as the roster, but **prepended with a 4-byte `02 00 00 00`
preamble** before the `"DB"` magic. Our `MaddenTdb.Load` auto-detects and
strips the preamble; `Save()` re-prepends it. Don't fight the preamble — pass
the whole file through `MaddenTdb` and it's handled.

The franchise TDB has **183 tables** (vs 4 in a roster). The PLAY table here
has **131 fields** (vs 110 in the roster); the 21 extras are per-player
franchise state, including:

- `PSA0..PSA6` (14 bits each) — annual base salary, year 0–6 of contract.
  Units appear to be **$10,000** (14-bit ceiling = $163.83M, room for top
  contracts).
- `PSB0..PSB6` (13 bits each) — prorated signing bonus, year 0–6.
- `PCSA` (14 bits) — current-year cap hit (PSA + bonus allocation).
- `PCTS` (2 bits), `PSBO` (13 bits), `PSBS` (7 bits) — contract status,
  original bonus total, bonus state.
- Other franchise state: morale, role, progression, fatigue.

The two singletons our Phase-1 compiler mutates:

- **`SEAI`** (Season Info, 19 fields). `SEAI.SEYR` is a **6-bit SINT at
  offset 51** holding the year offset from the disc's base year
  (M08 = 2007). Fresh save has SEYR=0; for a 2018 target on M08 write
  SEYR=11. Range -32..+31 covers 1975..2038 from a 2007 base.
- **`SLRI`** (Salary Info, 9 fields, all 32-bit UINT except 3 small flags):
  - `SCAD` — league salary cap, real dollars.
  - `SMAD` — franchise tag amount, real dollars.
  - `RFA1..RFA4` — RFA tender amounts at the 4 ascending tiers, real dollars.
  - `SAIP` / `SIIP` / `SAMU` — cap-policy constants (min salary, etc.).

M08's engine routinely produces caps in the $200M+ range during long sims
(~6% YoY inflation, community-documented). Writing real-historical caps
($109M..$300M+ across 2007–2026) is well within proven engine range — see
[footballidiot.com cap inflation thread](https://www.footballidiot.com/forum/viewtopic.php?t=20989).

Phase-1 compiler **does not** populate per-player contracts. The engine
generates plausible contracts at franchise start from rating + age + PCON +
position. Phase 2 adds a rating-driven contract synthesizer; Phase 3 adds
real Spotrac/OvertheCap contract import.

## Pipelines

### NCAA draft class (Tier 4, working)

```
data/canonical/draft-class-{year}.json  (real NFL Draft, from nflverse)
                ↓ DraftClassCompiler
out/draft-class-{year}.bin              (138,240-byte BASLUS-21620LClass07)
                ↓ tools/pack_baslus.py
out/draft-class-{year}.max              (MaxDrive container)
                ↓ mymcplus import
PCSX2 memcard                            (Boot Madden 08 → sim to Pro Bowl
                                          → Import Draft Class)
```

Full one-line CLI command (verified working for 2018):
```
dotnet run --project NcaaDraftEditor.Cli -- compile \
    data/canonical/draft-class-2018.json \
    data/mappings/positions.json \
    data/mappings/colleges.json \
    out/draft-class-2018.bin \
    --madden data/raw/madden-ratings/madden19-2018.json \
    --filler tests/fixtures/sample.bin \
    --lock-draft-order
```

Flag meanings:
- `--madden <path>`: enrich rookie ratings from the Madden-of-the-year file
- `--filler <path>`: fill slots beyond real picks (REQUIRED — Madden hangs otherwise)
- `--lock-draft-order`: linear OVR floor by pick number (pick 1 → 90, pick 256 → 50) so the in-game auto-draft mirrors real history

Then on Linux/WSL:
```
python tools/pack_baslus.py out/draft-class-2018.bin out/draft-class-2018.max
mymcplus /mnt/c/PCSX2/memcards/madden08_test.ps2 import out/draft-class-2018.max
```

If the memcard is uninitialized (all 0xFF), `mymcplus` rejects with "Not
a PS2 memory card image". Delete the file and run `mymcplus … format`
first to write a Sony PS2 memcard signature.

### Madden 08 roster (Tier 6, built)

```
nflverse rosters CSV + weebly Madden ratings
                ↓ scrapers/nflverse/build_roster.py + normalization
data/canonical/roster-{year}.json
                ↓ MaddenRosterCompiler (CLI: compile-roster)
out/roster-{year}.bin                     (BASLUS-21638DRost5, TDB format, 246,784 bytes)
                ↓ tools/pack_baslus.py --type roster
out/roster-{year}.max
                ↓ mymcplus import
PCSX2 memcard
```

### Madden 08 franchise (Tier 12, Phase 1 built)

```
data/raw/salary-caps/nfl-salary-caps.json   real NFL cap + RFA tenders 2007-2026
                ↓ MaddenFranchiseCompiler.Compile(year, template)
out/templates/madden-nfl-08-franchise-template.bin  (user-extracted via fetch_m08_franchise_template.py)
                ↓ mutates SEAI.SEYR + SLRI.SCAD/SMAD/RFA1..4 in place
out/franchise-{year}.bin                    (1,474,560-byte BASLUS-21638BFran1 with 4-byte preamble)
                ↓ tools/pack_baslus.py --type m08-franchise
out/franchise-{year}.psu                    PS2 save container
                ↓ mymcplus import
PCSX2 memcard                               Boot Madden 08 → Load Franchise → date should be {year}
```

CLI:

```
dotnet run --project NcaaDraftEditor.Cli -- compile-franchise \
    out/templates/madden-nfl-08-franchise-template.bin \
    out/franchise-2018.bin \
    --year 2018 \
    --caps data/raw/salary-caps/nfl-salary-caps.json
python3 tools/pack_baslus.py out/franchise-2018.bin out/franchise-2018.psu --type m08-franchise
```

The template is user-specific (a fresh-Week-1 franchise the user exported
from their own PS2 memcard). `tools/fetch_m08_franchise_template.py` reads
from `/mnt/c/PCSX2/memcards/Mcd001.ps2` (configurable via `--memcard`) and
caches both `.psu` and inner `.bin` at the standard path.

PCSX2 verification of "did the in-game calendar actually flip to 2018"
pending. Smoke-test verification of file contents (SEYR=11, SCAD=$177.2M,
all other SEAI/SLRI fields preserved byte-exact, 4-byte preamble survives
pack/unpack roundtrip) is ✅.

### Madden 09 / Madden 12 rosters (Tier 9–10, built — Deluxe-compatible)

Both M09 and M12 PS2 use **the same bare TDB format as M08** — no MC02
wrapper (that's the PS3/360/PC variant). Table structure is identical to
M08 across all three games:
- M09: PLAY field bit-layout matches M08 exactly. Drop-in compatible.
- M12: same 110 PLAY fields but ~74 of them shifted in bit position
  (e.g. PCMT widened from 10 to 11 bits). Our compiler is
  metadata-driven — it reads each field's offset from the file's own
  field directory — so this drift is transparent.

```
data/canonical/roster-{year}.json
                ↓ MaddenRosterCompiler (same code path for all targets)
out/{m09,m12}/roster-{year}-{target}.bin     (BASLUS-21770 or 21946, 246,784 / 246,783 bytes)
                ↓ tools/pack_baslus.py --type {m09-roster,m12-roster}
out/{m09,m12}/roster-{year}.psu
                ↓ mymcplus import (with corresponding Deluxe ISO patched)
PCSX2 memcard
```

Bulk build:
```
python tools/build_all_rosters.py --target m09      # M09 only
python tools/build_all_rosters.py --target m12      # M12 only
python tools/build_all_rosters.py --target all      # M08 + M09 + M12
```

On first run for m09/m12 the script auto-invokes
`tools/fetch_{m09,m12}_template.py`, which downloads the corresponding
Deluxe community .psu from GitHub and caches it at
`out/templates/madden-nfl-{09,12}-template.{psu,bin}`. The template
provides (a) icon.sys + view.ico for the PS2 dashboard, and (b) the
inner TDB the compiler mutates — Deluxe-side TEAM/DCHT data (extra
uniform slots, depth chart) passes through unchanged, which is what
users running the Deluxe ISO patch expect.

## Data sources

| Source | What it gives | Years | Quirks |
|---|---|---|---|
| `github.com/nflverse/nflverse-data` `players.csv` | NFL drafts + bio (college, height, weight, birth date, jersey, draft slot) | 2007–2025 | Use **`common_first_name`** not `first_name`; the latter is the legal name ("Fe'Zahn Edmunds" instead of "Tremaine"). |
| `github.com/nflverse/nflverse-data` `combine.csv` | combine measurables (40, bench, vert, broad, cone, shuttle) | 1987–2025 | Joined on `pfr_id`. Height stored as `"6-2"` style. |
| `en.wikipedia.org/wiki/{year}_NFL_draft` | NFL draft picks, basic | any year | Used for 2026 (nflverse hasn't backfilled). Round/pick cells can contain `*` for comp picks — strip non-digits. Wikipedia uses `Mozilla/5.0`-friendly UA; no Cloudflare. |
| `maddenratings.weebly.com/madden-nfl-{NN}.html` | Per-team XLSX with ~50 attributes per player | Madden 09–25 anniversary (=2008–2023 NFL seasons), + Madden 06–08 (older eras, not used). | The team page embeds team logos as anchor tags pointing to `/uploads/1/4/0/9/14097292/{team}_madden_nfl_{NN}.xlsx`. Madden 11 uses .xls (legacy BIFF, needs xlrd==1.2.0). Madden 22 is a single consolidated file. Schema varies wildly year-to-year (FIRSTNAME vs Name, single ThrowAccuracy vs short/mid/deep, etc.). Convert XLSX dates to ISO strings on serialize. |
| `sportsgamingrosters.com/madden-nfl-25/` | Madden 25 modern launch OVR | 2024 NFL Draft | OVR + position + team only, all 257 picks |
| `web.archive.org` of `maddenratings.com/lists/{year}-nfl-draft` | Top 100 rookies per year with full attributes | recent | Rate-limited; archive.org disconnects after ~20 requests at 1s delay. JS pagination caps live page at top 100 too. **Not viable for bulk scraping**; documented as a gap. |

### Madden launch-ratings naming convention

Files in `data/raw/madden-ratings/` use `madden{version}-{NFL season year}.json`:

| File | Madden | Released | NFL season | Draft rookies |
|---|---|---|---|---|
| `madden09-2008.json` | Madden NFL 09 | Aug 2008 | 2008 | 2008 NFL Draft |
| `madden25-2013.json` | Madden NFL 25 *anniversary* | Aug 2013 | 2013 | 2013 NFL Draft |
| `madden24-2023.json` | Madden NFL 24 | Aug 2023 | 2023 | 2023 NFL Draft |

The Madden 25 anniversary (2013) vs Madden 25 modern (2024) naming collision
is the reason for the `{version}-{year}` scheme — they live in
`madden25-2013.json` and `madden25-2024.json` (the latter is a gap; not on
weebly).

## Mappings

`data/mappings/positions.json` (15 entries): canonical NFL position string
→ NCAA 06 PPOS byte. Default to left-side variant when NCAA distinguishes
L/R sides (OL, DE, OLB).

`data/mappings/colleges.json` (288 entries): canonical college name →
TGID byte. Built by `tools/build_college_mapping.py` from
`CollegeCatalog.cs` + canonical drafts. Match sources tracked in the
`audit` block of the JSON: direct (165), alias (31), explicit-na (36),
fuzzy (3), normalized (1), no-match (52, → 255).

## Important gotchas (sorted by likelihood of biting you)

### Madden 08's "Import Draft Class" option is hidden behind the Pro Bowl
The menu doesn't appear until after the Pro Bowl in franchise mode.
Sim 17 regular-season weeks + playoffs + Pro Bowl, then the offseason
menu has it. Not on the main menu, not at franchise start, not in roster
management.

### Empty player records hang Madden 08
Madden 08 hangs on "initializing roster management" if any of the 1600
slots in the draft class has an empty/zeroed record. You **must** fill
all 1600. Use `--filler <real-draft-class>` to copy slots from a known-
good template.

### High-OVR filler outranks your real picks
Madden 08 sorts the imported pool by OVR. Real-life Madden ratings for
real prospects (e.g. Forsett OVR 91, Foster OVR 90 in the 2008 sample)
can outrank our 2018 picks (Saquon Barkley topped out at 84). Solution:
the compiler caps filler at OVR 49, one below the default real-pick OVR
of 50, so all filler is strictly below all real picks.

### nflverse's `first_name` is the legal name
`first_name` = "Joshua" / "Fe'Zahn" / "Denniston". You want
`common_first_name` = "Josh" / "Tremaine" / "DJ". The scraper falls back
to `first_name` only if `common_first_name` is empty.

### Madden 25 has two games sharing the name
The 2013 anniversary edition AND a 2024 modern release are both named
"Madden NFL 25". Avoid file naming that uses only the version number.

### PCSX2 v2.0+ has no memcard manager UI
The Qt rewrite dropped the standalone manager. Use `mymcplus` directly
on the `.ps2` memcard file. If the file is fresh and all 0xFF, format
it with `mymcplus` first.

### Empty .ps2 memcard files
PCSX2 sometimes allocates a memcard but doesn't format it (all 0xFF).
`mymcplus import` rejects with "Not a PS2 memory card image". Delete
the file and `mymcplus … format` it before importing.

### Pro Football Reference is Cloudflare-protected
Cannot scrape with `requests` / `curl`. Use nflverse's pre-scraped
datasets instead. nflverse has draft, combine, rosters, etc.

### archive.org rate-limits aggressively
~20 requests at 1s delay before "Connection refused". 4s delay still
triggers it. Not viable for bulk scraping per-player pages. Use it for
one-off lookups only.

### NCAA file size must be exactly 138,240 bytes
The natural alignment of `4 + 1600*86 = 137,604` rounds up to 269
sectors (137,728 bytes), but real NCAA 08 files are 270 sectors
(138,240). Pin the canonical size; don't compute it dynamically.

### JsonDocument disposal in MaddenRoster
`System.Text.Json.JsonDocument` invalidates its `JsonElement` references
when disposed. The `MaddenPlayer.Raw` dictionary stores elements that
must outlive the document — call `.Clone()` on each.

### Wikipedia round/pick cells have asterisks
Compensatory picks display as `3*` instead of `3`. Strip non-digits
before `int()`.

### Madden 11 uses .xls not .xlsx
Single year on weebly that uses the legacy BIFF format. Needs
`xlrd==1.2.0` (newer xlrd dropped BIFF support).

### Filler picks must use NCAA-side ratings cap, not Madden-side
The compiler caps filler ratings at the byte level, not at the source
data level. Don't try to filter or downsample the filler file itself.

### Auto-draft mismatch with real NFL draft order
Madden 19 launch ratings don't always track NFL Draft pick order
(e.g. Quenton Nelson M19 OVR 83 was higher than Mayfield's 81 despite
Mayfield going #1). Use `--lock-draft-order` to apply a linear OVR
floor by real pick number.

### Franchise saves have a 4-byte preamble
Madden 08 franchise saves (`BASLUS-21638BFran1`) prepend `02 00 00 00`
before the TDB's `"DB"` magic. Both the Python `parse_madden_tdb.py`
parser and the C# `MaddenTdb` class auto-detect and strip the preamble on
read; `MaddenTdb.Save()` re-prepends it. Don't manually slice; pass the
whole file in.

### Roster save's contract data is minimal
Roster save's PLAY table has only `PCON` (4-bit contract length) and
`PYRP` (5-bit years pro). No salary/bonus/cap-hit fields. Full per-player
contracts live only in franchise saves (`PSA0..6`, `PSB0..6`, `PCSA`, etc.,
21 extra fields in the franchise PLAY table). When Madden 08 boots a fresh
franchise from a roster, it generates contracts on the fly from a
`f(rating, age, position, PCON)` model.

### Madden uses ~6% YoY cap inflation
Engine-driven franchise simulation grows the league cap ~6% per offseason.
A long sim out to year 25 reportedly produces caps above $400M with no
display/overflow problems — modders have hex-edited `SLRI.SCAD` to large
values for over a decade without issue. So writing real-historical NFL caps
(up to ~$300M for 2026) into a fresh M08 save is **safe by a wide margin**.

### Salary cap field is in real dollars, not $10K units
`SLRI.SCAD` is 32-bit UINT, **raw dollar amount**. (M08's 2007 default
holds the literal value 109,000,000.) Don't divide by 10,000 — that's the
PLAY contract fields' convention, not the league cap.

## What's known to NOT exist

- Bulk launch-ratings data for Madden 25 modern (2024), Madden 26
  (2025), Madden 27 (2026). Documented in `memory/project_madden_data_gap.md`.
  Filled in by the rating-model fallback (which we haven't built yet —
  Tier 4 polish).
- `collegeYear` and `redshirt` real values. We default to `SR` / `false`
  for everyone. PFR's bio pages have it but they're Cloudflare-protected.
- Player potential rating in the NCAA file (game derives it at import).
- A clean 1:1 NFL position → NCAA position mapping. We pick a default
  for ambiguous positions (OL→LT, DE→LE, etc.).

## Cross-cutting conventions

- **Commits never have a `Co-Authored-By:` trailer.** User has explicitly
  forbidden Anthropic/Claude credit on commits. See
  `memory/feedback_no_coauthor.md`.
- **Branch `dev`** is where active work lives; `main` tracks the original
  upstream (`antdroidx/NCAA-Draft-Class-Editor`). Origin is the fork
  (`patrickfcarey/NCAA-Draft-Class-Editor`).
- Python is **stdlib-only** in scrapers except for `openpyxl`/`xlrd` in
  `scrapers/maddenratings/` (Madden XLSX parsing). Requirements are in
  per-scraper `requirements.txt`.
- C# uses **`net8.0`** for cross-platform projects, **`net8.0-windows`**
  for WinForms. Nullable and ImplicitUsings enabled everywhere.
- Test fixtures are linked into test project output via
  `<None Include="..\..\path"><CopyToOutputDirectory>...</None>`. Tests
  read from `AppContext.BaseDirectory + "fixtures/"`.

## Where to look first when picking up Tier 6

Read+write of the TDB format is **done**:

- `tools/parse_madden_tdb.py` — Python parser, byte-exact validated
- `tools/write_madden_tdb.py` — Python writer, byte-exact roundtrip
- `tools/edit_madden_tdb.py` — proves mutation works (changed Urlacher's
  PSPD from 88 → 99 in the sample, all neighboring fields preserved)
- `tools/dump_tdb_schema.py` + `docs/madden08-tdb-schema.md` — full
  field reference for all 4 tables (DCHT/INJY/PLAY 110 fields/TEAM 66)
- `NcaaDraftEditor.Compiler/MaddenTdb.cs` — C# port; same API surface
  as `DraftClassFile`. Test coverage in `MaddenTdbTests.cs`.

**Tier 6 pipeline is now complete in code. On Windows run:**

```
python tools/build_all_draft_classes.py       # 2008-2026 NCAA draft .max files
python tools/build_all_rosters.py             # 2008-2025 Madden roster .max files
```

Each script loops the year-by-year:
```
dotnet run --project NcaaDraftEditor.Cli -- compile [-roster] ...
python tools/pack_baslus.py <bin> <max> [--type roster]
```

Outputs land in `out/draft-class-{year}.max` and `out/roster-{year}.max`.
Import via PCSX2 memory card manager (or `mymcplus <memcard.ps2>
import out/...max` directly).

**What's left for Tier 6:**
1. **PCSX2 verification** — boot Madden 08, load a roster save, start
   franchise, sanity check rosters match the canonical year.
2. ~~TEAM table updates~~ — ✅ Done (d29b48f). Compiler authoritatively
   rewrites `TDNA` / `TLNA` / `TSNA` / `TMNC` per canonical year. Relocated
   teams now show correct city/abbr on every target including Deluxe.
3. **Per-team roster cap** — canonical has 55-100 players per team but
   Madden's PLAY table has only ~62 slots per TGID. The compiler
   currently sorts by OVR descending and silently drops the rest;
   could be smarter (preserve role distribution, etc.).

**Where to look first when picking up Tier 12 (franchise) Phase 2:**

The franchise calendar + cap economy work is complete. Phase 2 is the
per-player contract synthesizer:

- Schema: 21 extra PLAY fields exist only in franchise saves
  (`PSA0..6` 14-bit annual salary in $10K units, `PSB0..6` 13-bit
  signing bonus, `PCSA` cap hit, plus `PCTS`, `PSBO`, `PSBS`).
- Model: `cap_hit = f(POVR, PPOS, PAGE, PCON)` calibrated against the
  league cap (`SLRI.SCAD`).
- Caveat: the franchise PLAY table has 1,963 records by default. Roster
  PLAY had `PCON=0 PSA0=0` for record 0 (empty slot) but most franchise
  PLAY records carry real contracts — we need to **overwrite all 1,963**
  with synthesized values, not just the populated ones, or the engine
  will produce inconsistent cap math.

Test fixtures: `out/franchise-2018.bin` is the Phase-1 smoke-test output
(1,474,560 bytes with 4-byte preamble). Re-parse via
`python3 tools/parse_madden_tdb.py out/franchise-2018.bin /tmp/parsed.json`
— both the Python parser and the C# `MaddenTdb` class auto-detect and strip
the preamble.
