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
| 6 | Madden 08 PS2 roster builder | ⬜ Not started; have sample fixture |
| 7 | Pipeline + releases (one-shot build all 38 artifacts) | ⬜ Not started |
| 8 | PCSX2 verification | ✅ 2018 verified; other years pending Tier 7 |

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

### Madden 08 roster (Tier 6, not built)

```
nflverse rosters CSV + weebly Madden ratings
                ↓ [normalizer, TBD]
data/canonical/roster-{year}.json
                ↓ [MaddenRosterCompiler, TBD]
out/roster-{year}.bin                     (BASLUS-21638DRost5, TDB format)
                ↓ tools/pack_baslus.py    (will need to extend for BASLUS-21638)
out/roster-{year}.max
                ↓ mymcplus import
PCSX2 memcard
```

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

1. `/mnt/c/GitHub/madden-db-editor/src/renderer/` — Vue components + Vuex
   store that parse the TDB format. Particularly:
   - `components/MaddenDatabase.vue` (entry point)
   - `components/MaddenHeader.vue` (24-byte header)
   - `components/MaddenTable.vue`, `MaddenTableData.vue` (per-table parsing)
   - `utils/HexReader.js` (byte reading helpers)
   - `store/` directory — the `READ_TABLES` Vuex action does the work
2. `tests/fixtures/madden08-roster-sample.bin` — our test target (Madden
   08 PS2 default roster).
3. `nflverse-data` `rosters` release — historical NFL rosters by year.
   `https://github.com/nflverse/nflverse-data/releases/tag/rosters`

The fastest path is probably: write a Python TDB parser that dumps the
PLAY table from our sample, get the schema right, then port to C# as
`NcaaDraftEditor.Compiler.MaddenRosterFile`. Don't try to drive the
Electron app headlessly — too painful.
