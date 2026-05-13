# NCAA Draft Class Editor / Madden PS2 Historical Roster Toolchain

A toolchain that keeps **Madden NFL 08 / 09 / 12 (PlayStation 2)** playable
indefinitely by generating historically accurate game artifacts for every NFL
season from **2008 through 2026**:

1. **NCAA-format draft class files** — one per year, holding the real NFL Draft
   of year N. Imports into Madden 08 / 09 / 12 PS2 franchise mode and seeds the
   upcoming draft pool with real prospects.
2. **Madden PS2 opening-day roster files** — one per year, holding the real
   opening-day NFL roster of season N. Lets you start a fresh franchise at any
   historical year instead of having to sim forward from the disc's launch year.

The vehicle games (Madden 08 / 09 / 12 PS2) never change. We produce the
*content* that gets imported into them. The end user plays the original game
on PCSX2, imports our files via a PS2 memcard, and gets a franchise that
mirrors NFL history. The pipeline also supports the community **"Deluxe"** ISO
patches for Madden 09 and Madden 12.

This repository is forked from
[antdroidx/NCAA-Draft-Class-Editor](https://github.com/antdroidx/NCAA-Draft-Class-Editor),
which contributed the original Windows Forms editor for reading and writing the
NCAA draft class binary format. Everything beyond that editor — the canonical
data model, the scrapers, the compiler, the Madden TDB read/write, the bulk
build pipeline, and the M09/M12 + Deluxe targets — was added in this fork.

---

## Status

| Target | What it produces | State |
|---|---|---|
| Madden 08 PS2 (vanilla) | NCAA draft class + roster, 2008–2026 | End-to-end verified for 2018 in PCSX2; bulk build implemented |
| Madden 09 PS2 (Deluxe-compatible) | Roster `.psu`, 2008–2025 | Compile + pack verified for 2018; bulk build implemented |
| Madden 12 PS2 (Deluxe-compatible) | Roster `.psu`, 2008–2025 | Compile + pack verified for 2018; bulk build implemented |
| Draft class import into M09 / M12 | NCAA-format `.max` retargeted to BASLUS-21769 / BASLUS-21932 | Not yet wired; format compatibility confirmed in research |
| Tier 7 release pipeline | One-shot build of all 38 artifacts | Not started |

Open work items are tracked in `CLAUDE.md` (the "Where to look first" section).

---

## Quick start

### Prerequisites

- **.NET 8 SDK** (`dotnet --version` ≥ `8.0`) for the C# core, compiler, CLI,
  and tests.
- **Python 3.10+** for the scrapers, the TDB parser/writer reference
  implementation, and the MaxDrive packer. Stdlib only for most scripts;
  `openpyxl` / `xlrd==1.2.0` for the Madden ratings scraper; `mymcplus` for the
  PS2 memory-card packer.
- **PCSX2 2.0+** to verify output on actual Madden discs.

```bash
# C# build (run from repo root)
dotnet build NcaaDraftEditor.sln

# Python deps for scrapers / packing
pip install mymcplus openpyxl 'xlrd==1.2.0'
```

### Build the 2018 NCAA draft class for Madden 08

The end-to-end commands that produce a working `.max` for PCSX2 import:

```bash
# 1. Compile the canonical 2018 draft-class JSON to a 138,240-byte NCAA binary
dotnet run --project NcaaDraftEditor.Cli -- compile \
    data/canonical/draft-class-2018.json \
    data/mappings/positions.json \
    data/mappings/colleges.json \
    out/draft-class-2018.bin \
    --madden data/raw/madden-ratings/madden19-2018.json \
    --filler tests/fixtures/sample.bin \
    --lock-draft-order

# 2. Wrap the binary in a MaxDrive container (.max) for PCSX2 memcard import
python3 tools/pack_baslus.py out/draft-class-2018.bin out/draft-class-2018.max

# 3. Import the .max into a PS2 memcard
mymcplus /path/to/memcard.ps2 import out/draft-class-2018.max
```

In Madden 08 franchise mode, sim through Week 17 + playoffs + Pro Bowl. The
"Import Draft Class" option appears in the offseason menu after the Pro Bowl.

### Build the 2018 roster for Madden 09 PS2 Deluxe

```bash
# Compile canonical -> TDB binary
dotnet run --project NcaaDraftEditor.Cli -- compile-roster \
    data/canonical/roster-2018.json \
    data/mappings/positions.json \
    out/templates/madden-nfl-09-template.bin \
    out/m09/roster-2018-m09.bin

# Wrap in PS2 save container (.psu) for the Madden 09 BASLUS dir name
python3 tools/pack_baslus.py out/m09/roster-2018-m09.bin out/m09/roster-2018.psu --type m09-roster
```

The template is auto-downloaded on first use by
`tools/fetch_m09_template.py`. Same flow for M12, swapping `m09` for `m12`.

### Bulk build all years

```bash
python3 tools/build_all_draft_classes.py            # NCAA classes 2008–2026
python3 tools/build_all_rosters.py --target m08     # M08 rosters
python3 tools/build_all_rosters.py --target m09     # M09 Deluxe rosters
python3 tools/build_all_rosters.py --target m12     # M12 Deluxe rosters
python3 tools/build_all_rosters.py --target all     # all three
```

---

## Repository layout

```
.
├── NcaaDraftEditor.Core/           Binary format I/O: DraftClassFile, PlayerRecord,
│                                   FieldMap. Position/team/college catalogs. JSON
│                                   converter for the on-disk NCAA binary.
├── NcaaDraftEditor.Canonical/      Real-world schema DTOs + CanonicalJson load/save.
│                                   Two schemas: draft-class and full-roster. No
│                                   dependencies on Core.
├── NcaaDraftEditor.Compiler/       Canonical → binary glue:
│                                   • PositionMapper, CollegeMapper
│                                   • DraftClassCompiler (NCAA file builder)
│                                   • MaddenTdb (TDB read/write, byte-exact roundtrip)
│                                   • MaddenRosterCompiler (canonical roster → TDB)
├── NcaaDraftEditor.Cli/            `ncaa-draft` CLI:
│                                   dump | build | roundtrip | new | compile | compile-roster
├── NcaaDraftEditor.WinForms/       Windows-only interactive editor (the original
│                                   antdroidx tool, preserved). Useful as a debug
│                                   viewer for binary files.
├── NcaaDraftEditor.Tests/          xUnit. Format roundtrip, canonical, mappers,
│                                   compiler, TDB.
├── data/
│   ├── canonical/                  draft-class-{year}.json (2008–2026)
│   │                               roster-{year}.json     (2008–2025)
│   ├── mappings/                   positions.json, colleges.json
│   └── raw/madden-ratings/         madden{NN}-{YYYY}.json per game year
├── scrapers/
│   ├── nflverse/                   Python: build_draft_class.py, build_roster.py
│   ├── wikipedia/                  Python: build_draft_class.py (2026 fallback)
│   └── maddenratings/              Python: build_madden_year.py (weebly XLSX)
├── tools/
│   ├── parse_madden_tdb.py         Python reference parser (byte-exact)
│   ├── write_madden_tdb.py         Python reference writer (byte-exact)
│   ├── dump_tdb_schema.py          Dump field directory for any TDB
│   ├── edit_madden_tdb.py          Single-field mutation demo
│   ├── build_college_mapping.py    Build colleges.json from sources
│   ├── pack_baslus.py              Wrap a compiled binary in .max / .psu via mymcplus
│   ├── fetch_m09_template.py       Download Deluxe M09 template on first use
│   ├── fetch_m12_template.py       Download Deluxe M12 template on first use
│   ├── build_all_draft_classes.py  Bulk loop 2008–2026
│   └── build_all_rosters.py        Bulk loop 2008–2025, --target m08|m09|m12|all
├── tests/fixtures/
│   ├── sample.bin                  Real NCAA draft class (BASLUS-21620LClass07,
│   │                               138,240 bytes). Used as filler-pool source.
│   └── madden08-roster-sample.bin  Real M08 roster (BASLUS-21638DRost5, 246,784 B)
├── madden-nfl-08.26380.max         Original .max for the NCAA draft class
├── madden-nfl-08.16516.max         Original .max for the Madden 08 roster
├── docs/
│   ├── madden08-tdb-schema.md      Full field reference: DCHT/INJY/PLAY/TEAM
│   └── madden25-ps3-plan.md        Forward-looking design for M25 PS3 support
└── out/                            (gitignored) compiled artifacts land here
```

---

## File formats

### MaxDrive container (`.max`, magic `"Ps2PowerSave"`)

PS2 memory-card save export format from the Action Replay MAX / SharkPort era.
Layout:

| Offset | Size | Field |
|---|---|---|
| `0x00` | 12 | Magic: `"Ps2PowerSave"` |
| `0x0C` | 4 | CRC32 (file with this area zeroed) |
| `0x10` | 32 | Directory name, e.g. `"BASLUS-21620LClass07"` |
| `0x30` | 32 | Display title, e.g. `"NCAA Draft Class"` |
| `0x50` | 4 | Compressed data length |
| `0x54` | 4 | File count inside the save |
| `0x58` | 4 | Uncompressed data length |
| `0x5C` | … | LZSS-Ari compressed stream (file table + contents) |

We do not reinvent the codec; `mymcplus` handles it. The `.psu` container used
for M09/M12 is a related but distinct PS2 save format also handled by
`mymcplus`.

### NCAA draft class binary (`BASLUS-21620LClass07`)

Produced by NCAA Football 08 PS2's "Send to Madden". Always **exactly 138,240
bytes** (270 × 512-byte sectors). Always **1,600 player records**; Madden 08
hangs on "initializing roster management" if any of the 1,600 slots is empty,
so unused slots are filled from a known-good template with OVR capped to 49.

| Offset | Size | Contents |
|---|---|---|
| `0x00` | 4 | Magic: `46 00 40 06` |
| `0x04` | 1600 × 86 = 137,600 | Player records (86 bytes each) |
| `0x21984` | 636 | Zero trailer to 270-sector boundary |

Per-record layout is in
[`NcaaDraftEditor.Core/PlayerRecord.cs`](NcaaDraftEditor.Core/PlayerRecord.cs)
and [`FieldMap.cs`](NcaaDraftEditor.Core/FieldMap.cs). Highlights:

| Bytes | Field | Notes |
|---|---|---|
| 0 | `PFMP` | Face appearance |
| 4 | `TGID` | College (NCAA 06 catalog ID; 255 = N/A) |
| 6–16 | First name | 11 bytes ASCII, zero-padded |
| 17–30 | Last name | 14 bytes ASCII, zero-padded |
| 31 | `PYER` | College year (FR=0 / SO=1 / JR=2 / SR=3 / GS=4) |
| 32 | `PRSD` | Redshirt flag |
| 33 | `POVR` | Overall rating |
| 34 | `PJEN` | Jersey number |
| 35 | `PPOS` | Position (0=QB, 1=HB, … 20=P) |
| 36 | `PWGT` | Weight (lb) |
| 37 | `PHGT` | Height (inches) |
| 38–58 | 21 ratings | `PSTR`, `PAGI`, `PSPD`, `PACC`, `PAWR`, … |
| 60–85 | Appearance | Hair, face, skin, gear |

7 bytes remain empirically unmapped (1, 5, 59, 69, 70, 73, 84). They do not
carry rating-scale values — verified by profiling `sample.bin` across all
1,600 records. There is **no player-potential byte**; Madden 08 derives
potential at import time from age + OVR + position.

### Madden 08 / 09 / 12 PS2 roster binary (TDB)

Produced by Madden itself. EA's **TDB** (tabular database) format. Sample
`madden08-roster-sample.bin` is 246,784 bytes. PS2 endianness is **little-endian**
(distinct from PS3/Xbox 360/PC variants, which the bep713 madden-db-editor
parser targets).

#### File header (24 bytes)

| Offset | Size | Field |
|---|---|---|
| `0x00` | 2 | Magic: ASCII `"DB"` |
| `0x02` | 2 | Version: `0x0008` |
| `0x04` | 4 | (unknown) |
| `0x08` | 4 | dbSize |
| `0x0C` | 4 | zero |
| `0x10` | 4 | tableCount (4 for a roster) |
| `0x14` | 4 | checksum |

#### Table directory (8 bytes per entry)

| Offset | Size | Field |
|---|---|---|
| +0 | 4 | ASCII table name |
| +4 | 4 | LE offset relative to end of table directory |

#### Per-table header (40 bytes)

Bytes 0–3 priorcrc; 4–7 unknown; 8–11 lenBytes (record stride); 12–15 lenBits;
20–21 maxRecords; 22–23 curRecords; 28 numFields; 29 indexCount; 36–39
headercrc.

#### Field directory (16 bytes per field)

| Offset | Size | Field |
|---|---|---|
| +0 | 4 | type (0=STRING, 1=BINARY, 2=SINT, 3=UINT, 4=FLOAT) |
| +4 | 4 | bit offset within record |
| +8 | 4 | ASCII field name |
| +12 | 4 | bit width |

#### Records

Tightly **bit-packed**. UINT fields use LSB-first bit reading within
record byte sequence. STRING fields are byte-aligned (offset is always a
multiple of 8) and read as ASCII with null termination.

#### Roster tables

| Name | Records | Stride | Fields | Purpose |
|---|---|---|---|---|
| `DCHT` | up to 2,912 | 8 bytes (63 bits) | 4 | Depth chart |
| `INJY` | up to 320 | 8 bytes (63 bits) | 5 | Injuries |
| `PLAY` | up to 2,048 | 104 bytes (831 bits) | 110 | Players (50+ ratings + bio) |
| `TEAM` | 33 | 116 bytes (927 bits) | 66 | 32 NFL + 1 free-agent bucket |

A complete field reference is in
[`docs/madden08-tdb-schema.md`](docs/madden08-tdb-schema.md). M09 PS2's PLAY
layout matches M08 exactly. M12 PS2 keeps the same 110 PLAY fields but ~74
of them shift bit position; our compiler is metadata-driven (reads each
field's offset from the file's own field directory) so the drift is handled
transparently.

The PLAY `TGID` column links each player to its team in the TEAM table.
The TEAM table holds only 4 STRING fields per team — `TDNA` (display name),
`TLNA` (city), `TSNA` (abbreviation), `TMNC` (mirror of display name) — plus
a `SGID` byte that indexes into a stadium-name catalog **inside the ISO
executable**. Stadium name is therefore not editable from save data alone.

---

## Pipelines

### NCAA draft class pipeline (Madden 08)

```
data/canonical/draft-class-{year}.json    real NFL draft, from nflverse + wikipedia
                ↓ DraftClassCompiler (C#)
out/draft-class-{year}.bin                138,240-byte BASLUS-21620LClass07
                ↓ tools/pack_baslus.py
out/draft-class-{year}.max                MaxDrive container
                ↓ mymcplus import
PCSX2 memcard                             Boot Madden 08 → sim to Pro Bowl → Import
```

`compile` flags:

- `--madden <path>` — enrich rookie ratings from the Madden-of-the-year file.
- `--filler <path>` — required; fills slots 250–1599 with capped-OVR players.
  Madden 08 hangs if any of the 1,600 slots is empty.
- `--lock-draft-order` — apply a linear OVR floor by pick number (pick 1 → 90,
  pick 256 → 50). Mirrors real history when the in-game auto-draft picks.

### Madden roster pipeline (M08 / M09 Deluxe / M12 Deluxe)

```
nflverse rosters CSV + weebly Madden ratings XLSX
                ↓ scrapers/nflverse/build_roster.py + normalization
data/canonical/roster-{year}.json
                ↓ MaddenRosterCompiler (C#)
out/{target}/roster-{year}-{target}.bin
                ↓ tools/pack_baslus.py --type {target}-roster
out/{target}/roster-{year}.psu | .max
                ↓ mymcplus import
PCSX2 memcard
```

`MaddenRosterCompiler` does three things authoritatively:

1. **PLAY** — for each canonical team, overwrites the first N PLAY records
   carrying that TGID with canonical players (name, position, jersey, age,
   height, weight, years pro, OVR, 20 attribute ratings). Sorted by OVR
   descending so the best players take the available slots when canonical
   supplies more players than the template has slots.
2. **TEAM** — overwrites `TDNA` / `TLNA` / `TSNA` / `TMNC` for every team
   from canonical city/name/abbr. This is what makes 2018 rosters correct
   on a Deluxe template (which ships with LAR / LAC / LV Raiders / Commanders
   baked in) without breaking 2026 rosters either.
3. **Format preservation** — the TDB's overall shape (table layout, record
   counts, indices, anything we don't model) is preserved. Bits between
   declared fields (TDB has packing slack) are passed through from the
   template byte-for-byte.

Stadium names are *not* mutable from save data; they live in the ISO
executable's stadium catalog keyed by the SGID byte. On Deluxe ISOs whose
catalog has been replaced with modern stadiums, a 2018 build will still
show modern stadium names on screen. Accepted scope limitation.

---

## Data sources

| Source | Provides | Years | Notes |
|---|---|---|---|
| nflverse `players.csv` | NFL drafts + bio (college, height, weight, jersey, draft slot) | 2007–2025 | Use `common_first_name`, **not** `first_name` (legal-name column). |
| nflverse `combine.csv` | Combine measurables (40, bench, vert, broad, cone, shuttle) | 1987–2025 | Joined on `pfr_id`. Height format `"6-2"`. |
| nflverse rosters | Opening-day team rosters per season | 2008–2025 | Source for the roster builder. |
| Wikipedia `{year}_NFL_draft` | Draft picks, basic | any | Used for 2026 (nflverse hasn't backfilled). Strip non-digits from round/pick cells; compensatory picks display as `3*`. |
| `maddenratings.weebly.com/madden-nfl-{NN}.html` | Per-team XLSX with ~50 attributes per player | Madden 09–25 anniversary + Madden 06–08 | Madden 11 is `.xls` (BIFF; needs `xlrd==1.2.0`). Schema varies year-to-year. |
| `sportsgamingrosters.com/madden-nfl-25/` | Madden 25 modern launch OVR | 2024 draft | OVR + position + team for all 257 picks. |
| `web.archive.org/.../maddenratings.com` | Top-100 rookies per year, full attributes | recent | Rate-limited (~20 req/s of disconnect); not viable for bulk. |

Madden launch-ratings files are named `madden{version}-{NFL season year}.json`,
e.g. `madden09-2008.json`. The Madden 25 anniversary (2013) vs Madden 25
modern (2024) collision is why the year suffix exists.

---

## Mappings

- [`data/mappings/positions.json`](data/mappings/positions.json) — 15 entries
  mapping a canonical NFL position string to the NCAA 06 `PPOS` byte. Default
  to the left-side variant where NCAA distinguishes L/R (OL → LT, DE → LE,
  OLB → LOLB).
- [`data/mappings/colleges.json`](data/mappings/colleges.json) — 288 entries
  mapping canonical college name to NCAA 06 `TGID`. Built by
  `tools/build_college_mapping.py` from `CollegeCatalog.cs` and the canonical
  draft files. The JSON's `audit` block tracks each entry's match source:
  165 direct, 31 alias, 36 explicit-na, 3 fuzzy, 1 normalized, 52 no-match
  (encoded as `255`).

---

## CLI reference

`ncaa-draft` is the .NET 8 CLI under `NcaaDraftEditor.Cli`. All subcommands:

```
ncaa-draft dump            <in.bin>                                   # decode NCAA binary -> JSON
ncaa-draft build           <in.json>     <out.bin>                    # encode JSON -> NCAA binary
ncaa-draft roundtrip       <in.bin>      <out.bin>                    # decode + encode (byte-exact check)
ncaa-draft new             <out.bin>                                  # synthesize empty 138,240-byte file
ncaa-draft compile         <canonical.json> <positions.json>
                            <colleges.json> <out.bin>
                            [--madden <ratings.json>]
                            [--filler <template.bin>]
                            [--lock-draft-order]                      # canonical -> NCAA binary
ncaa-draft compile-roster  <canonical-roster.json> <positions.json>
                            <template.bin> <out.bin>                  # canonical roster -> TDB
```

---

## Building and testing

```bash
# Build everything
dotnet build NcaaDraftEditor.sln

# Run the full test suite (~25 tests across format, canonical, mappers, compiler, TDB)
dotnet test NcaaDraftEditor.sln

# Run one test class
dotnet test --filter FullyQualifiedName~MaddenRosterCompilerTests
```

From WSL, all `dotnet` commands run via Windows interop:

```bash
cmd.exe /c "dotnet test NcaaDraftEditor.sln"
```

C# projects target `net8.0` (`net8.0-windows` for WinForms). Nullable and
ImplicitUsings enabled.

Python scrapers are stdlib-only except for `openpyxl` / `xlrd` in
`scrapers/maddenratings/` (Madden XLSX parsing) and `mymcplus` in
`tools/pack_baslus.py`. Per-scraper `requirements.txt` files where needed.

---

## Known gaps and explicit non-goals

- **Bulk launch-ratings data is unavailable for Madden 25 modern (2024),
  Madden 26 (2025), and Madden 27 (2026).** Documented in the project
  memory. Will be backfilled by a rating model rather than further scraping.
- **`collegeYear` and `redshirt` use defaults** (`SR` / `false`) for every
  player. The real per-player values live on PFR's bio pages, which are
  Cloudflare-protected and not scrapable with `requests` / `curl`.
- **Stadium accuracy on Deluxe ISOs** is not fixable from the save. Stadium
  name is keyed by `SGID` into the ISO's catalog. A 2018 build on a Deluxe
  ISO will show modern stadiums for relocated teams.
- **Per-team roster cap.** Canonical rosters have 55–100 players per team but
  Madden's PLAY table allocates only ~62 slots per TGID. The compiler sorts
  by OVR descending and silently drops the remainder. Smarter selection
  (preserve role distribution) is future work.
- **No NCAA draft class import target for M09 / M12 yet.** Research confirms
  the inner format is shared with M08 and that Deluxe doesn't patch the
  import code path. The remaining work is two new BASLUS dir-name presets in
  `pack_baslus.py` and PCSX2 verification.
- **NCAA draft class import in Madden 12 PS2 pairs with NCAA Football *11*,
  not 12.** NCAA 12 has no PS2 release; NCAA 11 was EA's last PS2 NCAA.

---

## The original WinForms editor

The `NcaaDraftEditor.WinForms` project preserves the original interactive
editor from [antdroidx/NCAA-Draft-Class-Editor](https://github.com/antdroidx/NCAA-Draft-Class-Editor),
which reads and writes the NCAA draft class binary format on Windows. It
remains useful as a debug viewer when validating compiler output: open the
binary, scroll the player list, confirm names / positions / ratings parse
correctly.

To open a save:

1. Extract the raw `BASLUS-21620LClass07` file from your PS2 memory card
   (e.g. via PCSX2's memory card folder feature, MyMC, or PS2 Save Builder).
2. Run the WinForms editor and open the file. The filename should start with
   `BASLUS-`.

<img width="1378" alt="Editor main view" src="https://github.com/user-attachments/assets/f45f57fb-f461-4826-b6d4-45b37a488286" />
<img width="1379" alt="Editor player view" src="https://github.com/user-attachments/assets/2d573769-218f-4f1a-a641-2ab8b283633e" />

---

## Important gotchas (sorted by likelihood of biting you)

- **Madden 08's "Import Draft Class" is hidden behind the Pro Bowl.** It
  only appears in the offseason menu after Week 17 + playoffs + Pro Bowl.
- **Empty player records hang Madden 08.** All 1,600 NCAA slots must be
  valid. Use `--filler tests/fixtures/sample.bin` to fill unused slots.
- **High-OVR filler outranks real picks.** The compiler caps filler OVR at
  49 (one below the default real-pick floor) so all real picks rank higher.
- **nflverse's `first_name` is the legal name.** Use `common_first_name`.
- **PCSX2 2.0+ has no memcard manager UI.** Use `mymcplus` directly on the
  `.ps2` memcard file. Fresh memcards filled with `0xFF` must be formatted
  with `mymcplus … format` before `mymcplus … import` will accept them.
- **NCAA file size must be exactly 138,240 bytes.** `4 + 1600*86 = 137,604`
  rounds up to 269 sectors naturally, but real NCAA files are 270 sectors.
  Pin the canonical size; don't compute it dynamically.
- **`JsonDocument` disposal in `MaddenRoster`.** `JsonElement` references
  become invalid when the parent document disposes. The `MaddenPlayer.Raw`
  dictionary calls `.Clone()` on each element to outlive the document.
- **Wikipedia round/pick cells contain `*`** for compensatory picks. Strip
  non-digits before parsing.
- **Madden 11 ratings ship as `.xls`** (legacy BIFF). Requires
  `xlrd==1.2.0`; newer xlrd dropped BIFF support.
- **Pro Football Reference is Cloudflare-protected.** Cannot scrape with
  `requests` / `curl`. Use nflverse's pre-scraped datasets.
- **archive.org rate-limits aggressively.** ~20 requests at 1 s delay
  before "Connection refused". Use for one-off lookups only.

---

## Credits

- Original NCAA draft class binary format reverse engineering and the
  Windows Forms editor: [antdroidx](https://github.com/antdroidx).
- Madden TDB format reference for non-PS2 platforms (PS3 / Xbox 360 / PC):
  [bep713/madden-db-editor](https://github.com/bep713/madden-db-editor).
  The PS2 endianness and bit-packing direction differ; the
  `MaddenTdb` implementation in this repo is independent.
- Data: [nflverse](https://github.com/nflverse/nflverse-data),
  [Madden Ratings](https://maddenratings.weebly.com/),
  [Sports Gaming Rosters](https://sportsgamingrosters.com/),
  Wikipedia, archive.org.
- Madden Deluxe project (M09 / M12 PS2 ISO patches): community work
  hosted on the Operation Sports forums and adjacent communities.

---

## License

Inherited from upstream. See the upstream repository for licensing terms.
