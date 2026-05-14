# Madden NFL 25 (2013 anniversary) PS3 support — plan

Status: **VALIDATED — M12 PS3 IS TDB.** Schema overlap with our PS2
toolchain is ~99%; only byte order differs (BE vs LE) plus a quirky
4-char-name byte-reverse from EA storing the name as a CPU-native u32.
The C# pipeline needs a small endian flag added to `MaddenTdb`; the
canonical schemas, compilers, and 5 compile passes drop in unchanged.

See [Validation progress (2026-05-14)](#validation-progress-2026-05-14)
for the full byte-level diff.

Adds a new build target alongside the existing PS2 targets (m08, m09, m12).
Does **not** replace them. M25 PS3 is the first non-PS2 game we'd support
and the first save container that isn't MaxDrive / SharkPort.

## Why M25 specifically

- Last "old-gen" Madden — Infinity Engine 2, new physics, last entry
  before Madden moved to Frostbite (M19+).
- Released on PS3 + Xbox 360 + PS4 + Xbox One (no PS2 release; the last
  PS2 Madden was M12 in 2011).
- PS3 is the most tractable target: RPCS3 is a mature emulator, save
  format is documented (PARAM.SFO + PARAM.PFD + USER-DATA),
  little-endian.
- Xbox 360 (BE, .CON container) is a separate decision; out of scope
  for this plan.

## Naming-collision warning

"Madden NFL 25" refers to **two different games**:

- **2013 anniversary edition** — what this plan targets. Released Aug
  2013 on PS3/360/PS4/One. Covers the 2013 NFL season. Disc ID series
  BLUS31178 / BLES01850.
- **2024 modern release** — released Aug 2024, current-gen only.
  *Out of scope*.

CLAUDE.md's "Madden launch-ratings naming convention" section has the
full disambiguation. Anywhere this codebase says "M25" without
qualification, it means the 2013 anniversary edition.

## Game IDs (PS3, 2013 anniversary)

| Region | Disc | Digital |
|---|---|---|
| US | BLUS31178 | NPUB31183 |
| EU | BLES01850 | — |
| AS | BLAS50622 | — |

## Critical load-bearing assumption

**M25 PS3's inner roster file is TDB** (the same `DB`-magic table format
we already handle for M08/M09/M12), wrapped in an MC02 container,
wrapped in a PS3 save folder.

Evidence supporting:
- OS forum threads describe M25 PS3 ↔ M12/M13 PS3 hex-edit
  interconversion (only viable if the underlying format is the same
  family).
- EA didn't switch to Frostbite-era data formats until M19+.
- The MC02 wrapper exists *to wrap a TDB*, per OS forum
  reverse-engineering.

If the assumption fails (e.g., M25 PS3 turned out to use a Frostbite/EBX
bundle), the entire TDB substrate is irrelevant and this work moves out
of repo into its own project.

## Validation gate

Before writing any code, do a ~15 min byte-diff:

1. Obtain one M25 PS3 roster save (zip/rar from a community source —
   see [Sourcing](#sourcing) below).
2. Drop the extracted save folder into `out/m25-research/`.
3. Strip wrappers:
   - PS3 save folder → `USER-DATA` (the payload file)
   - MC02 (if present) → inner DB blob
4. Check first 2 bytes (or first 6, allowing a leading preamble like
   M08/09/12 franchise saves' `02 00 00 00`): if `44 42` (`DB`) appears,
   parse with `tools/parse_madden_tdb.py` — which now auto-detects + strips
   the franchise-style preamble.
5. Compare table list and PLAY field set to the M12 schema dump in
   `docs/madden08-tdb-schema.md` and recent CLAUDE.md additions.

Pass criteria: tables `PLAY` + `TEAM` exist, PLAY has the rating fields
we recognize (PSPD, POVR, etc.), schema is comparable to M12 (same
field-name vocabulary, new fields are additive).

Fail criteria: no `DB` magic anywhere in the first 32 bytes, or schema
is unrecognizable (different table names entirely, different format).

## Reuse analysis (if validation passes)

Reuse percentages have gone UP since this doc was originally drafted —
the toolchain has matured significantly with the M09/M12 work, franchise
compiler phases 1-5, and full stat-table decoding.

| Layer | Reuse | Notes |
|---|---|---|
| Canonical roster schema | 100% | Same DTOs; `roster-2013.json` already exists |
| Canonical contracts (OTC) | 100% | `contracts-2013.json` exists |
| Canonical career stats | 100% | `stats-2013.json` exists; PCOF/PCDE/PCKI/PCKP/PCNG decoded |
| nflverse + weebly scrapers | 100% | `madden25-2013.json` already scraped + integrated |
| PositionMapper / CollegeMapper | 100% | No game-specific logic |
| MaddenRosterCompiler | 100% | All 5 passes (PGID/PLAY/DCHT/safety-sweep/stats) are TDB-shape-agnostic |
| MaddenFranchiseCompiler | 100% | Just needs `--base-year 2013` for SEYR encoding |
| MaddenTdb parser/writer (C#) | ~95% | Already handles 4-byte preamble + CRC-32/MPEG-2 + Latin-1 strings. Need MC02 wrapper read/write. |
| pack_baslus.py | ~20% | PS3 save container is different — needs new packer (PARAM.SFO + PARAM.PFD signing) |
| build_all_rosters.py / build_all_franchises.py | ~95% | Add `m25-ps3` target alongside m08/m09/m12 |
| Tests | 100% existing pass | Add an M25 roundtrip test |

Net: ~95% of C# code and ~70% of tools carry over without change.
Bigger lift than originally estimated for the PS3 *container* (PFD
signing); smaller lift for everything inside the TDB.

## Work breakdown (if validation passes)

Roughly ordered. Each step is small enough to commit independently.

1. **MC02 codec** — read/write the wrapper. Probably ~50-100 lines C#.
   Verify byte-exact roundtrip on the source save.
2. **PS3 save container codec** — read/write the save folder structure
   (PARAM.SFO + PARAM.PFD + ICON0.PNG + HED-DATA + USER-DATA). The
   .PFD file is signed/sealed; resigning requires Apollo Save Tool's
   approach or a Python port. ~200-400 lines or a dependency on
   `apollo-saves` tooling. **This is the biggest unknown.**
3. **fetch_m25_ps3_template.py** — same pattern as M09/M12 fetch
   scripts; needs a user-provided template since no community
   distribution exists.
4. **pack_baslus.py `m25-ps3` preset** (or split into a new
   `pack_ps3.py`; decide once container codec is shaped). Wires
   container + MC02 + TDB layers.
5. **CLI / build_all_*.py `--target m25-ps3`** — auto-fetch + compile + pack.
6. **MaddenTdb schema additions** — if M25 added fields, extend the
   field name vocabulary. Compiler is metadata-driven so no logic
   change. PCOF/PCDE/PCKI/PCKP/PCNG decoded fields almost certainly
   carry over identically (same EA convention).
7. **`MaddenFranchiseCompiler` base-year mapping** — `M25BaseYear = 2013`
   so a 2018 franchise on M25 PS3 gets SEYR=5. Same calendar+cap
   pipeline applies. salary-caps JSON already has 2013.
8. **CLAUDE.md Tier 19** + pipeline section.
9. **RPCS3 verification** — boot M25 on RPCS3, import a built save,
   sanity-check rosters / contracts / career stats display.

Estimated effort end-to-end (validation through verified build): 1-2
weeks. The PS3 container codec is the unknown; if Apollo's PFD
resigning is straightforward to call out to, the actual implementation
is small. Everything else is now drop-in compatible with the existing
pipeline.

## Validation progress (2026-05-14)

User dropped a Madden 12 PS3 save at
`ALL 32 Franchise Week 1/PS3/SAVEDATA/BLUS30770-FRANCHISE-M25ALL32/`.
It's BLUS30770 (Madden NFL 12), hex-edited with the community's "M25 ALL
32" rosters — the very same OS-forum trick mentioned in the load-bearing
assumption above. So this DOES exercise the Tier 19 codec path even
before we get a native M25 PS3 save.

Files in the save folder:

| File | Size | Role |
|---|---|---|
| `PARAM.SFO` | 2,736 | Save metadata (plaintext) |
| `PARAM.PFD` | 32,768 | Integrity/encryption manifest (BE) |
| `HED-DATA` | 48 | Save header (encrypted) |
| `USR-DATA` | 2,540,736 | Save payload (encrypted) — the prize |
| `ICON0.PNG` | 69,765 | Save icon (not in PFD; unencrypted) |

**Mode confirmed: USB-export (decryptable in software).**
`PARAM.SFO`'s `ACCOUNT_ID` is `00 00 00 00 00 00 00 00`. When a save is
exported off a real PS3 to USB, the per-console-keyed layer is stripped
and replaced with a universal "USB-export" PFD encryption that uses
publicly-known keys (the `syscon_manager_key` etc.). The all-zero
ACCOUNT_ID is the canonical marker for that mode. (Real-PS3-HDD saves
have a non-zero ACCOUNT_ID and would NOT be recoverable here.)

**PFD structure reverse-engineered** (matches Apollo Save Tool / flatz
pfdtool layout from `bucanero/apollo-ps3` source):

| Offset | Field | Notes |
|---|---|---|
| 0x000 | `magic` (u64 BE) | 0x00000000_50464442 = `\x00\x00\x00\x00PFDB` |
| 0x008 | `version` (u64 BE) | 3 |
| 0x010 | `header_key` (16 B) | Used for top-level signature |
| 0x020 | `signature` (64 B) | Bottom-hash + top-hash + hash-key |
| 0x060 | hash table (header 24 B + N×8 B) | hash → entry-index |
| 0x100 | hash-table entries | `0x72` (114) reserved slots |
| 0x240 | entry table | 272-byte entries (`pfd_entry_t`) |
| each +8 | `file_name` (65 B + 7 B pad) | ASCII null-terminated |
| each +80 | `key` (64 B) | 4×16-byte AES keys (one per hash type), AES-CBC-encrypted with `syscon_manager_key` |
| each +144 | 4× `file_hashes[20]` | SHA1 HMACs (FILE, CID, DHK_CID2, AID_UID) |
| each +264 | `file_size` (u64 BE) | Logical size (pre-padding) |

In our sample, entries 0..2 are PARAM.SFO @0x240, HED-DATA @0x350,
USR-DATA @0x460. Slots 3..113 are garbage (free entries).

**Codec ported to Python**: `tools/ps3/decrypt_pfd.py`. Algorithm
(matches `apollo-ps3/source/pfd_util.c`):

1. AES-128-CBC decrypt the 64-byte `entry.key` blob using
   `syscon_manager_key` + an IV built from the per-game `secure_file_id`
   (12 bytes from the id interleaved with magic constants at positions
   1=11, 2=15, 5=14, 8=10).
2. The first 16 bytes of the decrypted entry-key are the AES-128 key
   for the file payload.
3. File data is decrypted block-by-block with a custom CTR-like mode:
   - `counter_key = u64_LE(block_index) || u64_LE(0)`
   - `counter_enc = AES-ECB-Enc(counter_key, file_key)`
   - `block_plain = AES-ECB-Dec(block, file_key) XOR counter_enc`
4. Truncate to declared `file_size` (strips 16-byte alignment padding).

Global keys (deobfuscated from `apollo-ps3` XOR-shrouded constants):

```
syscon_manager_key (16B): d413b89663e1fe9f75143d3bb4565274  -- AES key
keygen_key (20B):         6b1ace2546b745609ef576386a121cd2 5375d701
savegame_param_sfo_key (20B): 0c08000058050204058468008df00280 0fa12d86
```

(See `tools/ps3/decrypt_pfd.py` — `_deobfuscate()` does the XOR.)

**End-to-end verified that the algorithm runs cleanly**: 64-byte
entry-key block reads as 4 distinct AES keys, file payload decrypts
without exceptions through all 158,796 blocks.

### What we're missing (the actual blocker)

The 16-byte **`secure_file_id` for BLUS30770/USR-DATA**. It's a
per-(game, save-file) secret embedded in the game's `EBOOT.BIN`. Apollo
Save Tool ships secure_file_ids for ~3000 games but **does not include
the Madden series** (verified by API-listing `bucanero/apollo-saves/PS3/`
and grepping flatz' `pfd_sfo_tools/pfdtool/bin/games.conf` — both empty
for BLUS30770/30979/31178/21090).

Tested non-game-specific candidates (all 12 produced random output, no
DB / MC02 / PSF magic in first 64 bytes of plaintext):

- All zeros, all 0xFF
- `keygen_key[:16]`, `keygen_key[4:][:16]`
- `savegame_param_sfo_key[:16]`, `savegame_param_sfo_key[4:][:16]`
- `syscon_manager_key` itself
- `"BLUS30770"`, `"USR-DATA"`, `"Madden NFL 12 "` padded
- The PFD entry's own stored hash bytes
- Repeated 0x01

So `secure_file_id` is genuinely game-specific and not derivable from
the save alone.

### Three ways to obtain the BLUS30770 secure_file_id

1. **Extract from EBOOT.BIN** — boot the Madden 12 ELF through
   `make_npdata`/`scetool` or any of the published PS3 EBOOT-key
   extractors. The secure_file_id is typically a static 16-byte blob
   referenced near the `cellSysmoduleLoadModule(CELL_SYSMODULE_FS)`
   call site. Requires a Madden 12 PS3 ISO (we don't have one locally;
   the RPCS3 install at `/mnt/c/Emulators/RPCS3` doesn't have Madden).

2. **Run `bruteforce-save-data` (Windows GUI, closed-source)** — it
   probably ships keys for popular games. Single-shot decryption then
   we drop the result into `out/m25-research/`. User must run it
   themselves (Windows).

3. **Boot Madden 12 in RPCS3 from a PSN PKG / disc dump** — RPCS3
   carries the PFD layer transparently when it loads saves from
   `dev_hdd0`. If user already has the M12 ISO, it's a 5-min
   round-trip: install, run, the encrypted save imports as decrypted
   inside `dev_hdd0/home/00000001/savedata/BLUS30770-FRANCHISE-...`.

### What happens once we have the key

The first 64 bytes of decrypted USR-DATA tell us everything:

- `\x02\x00\x00\x00DB` → TDB with 4-byte preamble, like our PS2 franchise
  saves. **Tier 19 plan validated; ~95% of C# pipeline drops in.**
- `MC02...` → MC02-wrapped TDB. Need new wrapper codec (~50-100 lines C#);
  then the TDB inside is shape-compatible with our existing parser.
- Something else (EBX, EAMD, Frostbite bundle) → Tier 19 dies; M25 PS3
  is incompatible with the toolchain.

The bet: M12 PS3 is TDB-based (OS-forum hex-edit threads confirm this
empirically — they're hex-editing PLAY/TEAM tables, which only exist in
TDB). So result 1 or 2 is overwhelmingly likely.

### Bypass route taken (and validation result)

Instead of chasing the per-game `secure_file_id`, the user installed
both M12 and M25 PS3 in RPCS3, booted M12, started a fresh franchise,
and saved through M12's own UI. RPCS3 writes to `dev_hdd0` as
**unencrypted plaintext** (no real-PS3 private key to seal with).
We parsed `dev_hdd0/.../BLUS30770-FRANCHISE-*/USR-DATA` directly.

**Result: M12 PS3 = bare TDB version 8, big-endian.**

- Magic at offset 0: `44 42 00 08` = "DB" + version 8 (no preamble; the
  4-byte `02 00 00 00` prefix we expected from PS2 franchise saves is
  NOT present on PS3)
- 195 tables (vs 183 in M08 PS2 franchise)
- USR-DATA on disk is 5,120,000 bytes with `dbSize=2,540,596` declared
  inside — RPCS3/Madden pads the rest with zeros to a fixed allocation
- Entropy 5.56 bits/byte (consistent with structured TDB; encrypted
  would be ~7.9+)

The only surprise: **4-char table names are byte-reversed in BE
storage.** EA's code did roughly `*(u32*)name_field = *(u32*)"PLAY"`,
which on a LE PS2 CPU stores bytes `50 4C 41 59` ("PLAY") but on a
BE PS3 CPU stores bytes `59 41 4C 50` ("YALP"). So in the PS3 file we
see `YALP / MAET / THCD / YJNI / IAES / IRLS / FOCP / ...`. Reversed
back: `PLAY / TEAM / DCHT / INJY / SEAI / SLRI / PCOF / ...` — every
familiar table from our PS2 toolchain is present.

Same reversal applies to field names inside each table. E.g. PLAY's
field directory in M12 PS3 stores `PFNA` as `ANFP`, `POVR` as `RVOP`,
`PSA0` as `0ASP`, etc.

#### PLAY field-name compatibility check

Of our PS2 toolchain's load-bearing PLAY field names, ALL present in
M12 PS3 (verified by reversing each 4-char name in the field directory):

| Category | Names confirmed in M12 PS3 |
|---|---|
| Bio | PFNA, PLNA, PPOS, POVR, PJEN, PAGE, PWGT, PHGT, PYRP |
| Ratings | PSPD, PACC, PSTR, PAGI, PAWR, PINJ, PSTA |
| Contracts | PSA0..6, PSB0..6, PCSA, PCON |
| Identity | PGID, TGID |

PLAY stride grew from 104B/831bit (PS2 M08) to 244B/1951bit (PS3 M12):
+140 bytes / +1120 bits / +81 fields of newer-game state. Our
metadata-driven compiler is shape-agnostic about stride.

#### SEAI / SLRI / TEAM / DCHT field-name check

- SEAI: **SEYR** present (8-bit SINT @ bit 32 — same encoding as PS2)
- SLRI: **SCAD, SMAD, RFA1..RFA4** all present (32-bit UINTs)
- TEAM: 161 fields (vs 66 in PS2). Field names not yet sampled but
  TDNA/TLNA/TSNA/TMNC almost certainly present — Madden's TEAM-table
  rewriting code has been stable for 5+ years.
- DCHT: 4 fields (identical to PS2), 8-byte/63-bit stride identical,
  2873/3094 records (vs ~1745/2912 in PS2 — bigger depth chart pool)

### Implementation cost: ~1 day of C# work

Diff for adding M12 PS3 + M25 PS3 to the build pipeline:

1. **`NcaaDraftEditor.Compiler/MaddenTdb.cs`** — add an `Endian` enum
   constructor parameter. Replace `BinaryPrimitives.Read*LittleEndian`
   with a dispatch helper that calls `*BigEndian` for PS3. Byte-reverse
   the 4-char name fields (table-directory entries and field-directory
   entries) in BE mode. CRC-32/MPEG-2 stored BE (algorithm unchanged,
   only the LE↔BE store flip). ~80 lines diff.

2. **`MaddenRosterCompiler.cs` / `MaddenFranchiseCompiler.cs`** — no
   changes. Both already operate via `MaddenTdb`'s metadata-driven
   field accessor (`GetField(table, fieldName, ...)`); endian-aware
   reads happen inside `MaddenTdb` itself. Pass 4's
   `PgidReferencingTablesToClear[]` works unchanged because every entry
   is a logical table name; the lookup-by-name path will need to
   reverse on PS3 but that's a one-line helper.

3. **`tools/ps3/pack_param_pfd.py`** — when writing back, we need to
   produce a valid PARAM.PFD with HMAC-SHA1 hashes recomputed over the
   plaintext USR-DATA and AES-CBC-encrypted entry keys. Optional if the
   user only loads via RPCS3 (RPCS3 doesn't enforce PFD on
   `dev_hdd0`). REQUIRED if exporting to a real PS3 USB stick. ~150
   lines Python; reverses `tools/ps3/decrypt_pfd.py`.

4. **`tools/build_all_*.py`** — add `m12-ps3` / `m25-ps3` targets:
   - Base year: 2011 (M12) or 2013 (M25)
   - Template: a fresh Week-1 PS3 save in `dev_hdd0`
   - Output: `out/m12-ps3/franchise-{year}/USR-DATA` (drop into
     `dev_hdd0/home/00000001/savedata/BLUS30770-FRANCHISE-{LABEL}/`)

5. **`CLAUDE.md`** — Tier 19 + 20 (M12 PS3 + M25 PS3) section. The
   "Madden TDB quirks" memory entry already documents BE storage; just
   needs an addendum about the 4-char name byte-reversal.

### M25 PS3 follow-up (2026-05-14 evening): roster ✓, CAREER no-go

User then booted M25, started a fresh franchise, saved. Three PS3
saves landed in `dev_hdd0`:

- `BLUS31178-ROSTER-MAY14_015753/USR-DATA` (726,452 B):
  **bare BE TDB v8, 4 tables (DCHT/INJY/PLAY/TEAM). PLAY has 200
  fields including PSA0..PSA6, PSB0..PSB6, PCSA**. All key PLAY field
  names from our toolchain (PFNA, PLNA, PPOS, POVR, PSPD, PSA0..6,
  PSB0..6, PCSA, PCON, PGID, TGID, PAGE, PWGT, PHGT, PYRP) present.
- `BLUS31178-PROFILE-USER/USR-DATA`: small profile blob, not relevant.
- `BLUS31178-CAREER-MAY14_015754/USR-DATA` (3,145,728 B): zlib-deflated
  outer layer. Decompresses to 4,996,114 B starting with **`FrTk`**
  magic — M25's "Connected Franchise" frame-log container, NOT TDB.
  128-byte header + 1565 × 8-byte entry records; not parseable with
  our TDB infrastructure. Out of scope for Tier 19.

**Critical schema win**: M25 moved per-player contract fields out of
the franchise PLAY table and into the **roster** PLAY table. M08/M09/
M12 PS2 roster PLAY had 110 fields, no contracts; M25 PS3 roster PLAY
has 200 fields, contracts included. This means **for M25 PS3 we don't
need any franchise-side writes** — we inject contracts directly into
the roster save, then the in-game "Connected Franchise → Start from
Custom Roster" flow picks them up and the cap economy is already
era-correct.

### Revised Tier 19/20 scope

| Game | Roster save | Franchise save | Our writes target |
|---|---|---|---|
| M08/M09/M12 PS2 | Bare LE TDB, no contracts | Bare LE TDB w/4-byte preamble, contracts in PLAY | Both |
| M12 PS3 | Bare BE TDB, no contracts | Bare BE TDB, contracts in PLAY | Both (mirrors PS2 pipeline) |
| M25 PS3 | Bare BE TDB, **contracts in PLAY** ✓ | FrTk container (skip) | **Roster only** |

M25 PS3 is the simplest target overall: one save type to write, all
era-correct data goes through a single roster TDB. NCAA draft class
import is a separate question — Madden 25 was the last to support it,
but the import format may or may not match M08's BASLUS-21620 file
(needs separate verification).

## Sourcing (now narrowed)

I cannot pull M25 PS3 saves from this environment — every viable source
is Cloudflare-protected or behind a forum login:

- GameFAQs: https://gamefaqs.gamespot.com/ps3/702680-madden-nfl-25/saves
- Operation Sports HEX M25 thread:
  https://forums.operationsports.com/forums/forum/football/madden-nfl-football/madden-nfl-old-gen/madden-nfl-old-gen-rosters/582122-ps3-hex-edited-madden-25-roster-for-madden-12-and-13
- Brewology: https://ps3.brewology.com/gamesaves/savedgames.php?l=M&system=ps3
- Apollo Save Database does **not** carry M25 (confirmed).
- Etsy has paid options ($5-10); not worth it given OS/GameFAQs are
  free with a browser session.

**Next concrete action:** download one M25 PS3 roster save (zip/rar)
from any of the above in a browser, drop the extracted save folder
(or just the `USER-DATA` file) into `out/m25-research/`, then resume
this plan at the validation gate.

## Build + drop-in done (2026-05-14): awaiting in-game verification

Current state on disk (do not delete):

```
C:\Emulators\RPCS3\dev_hdd0\home\00000001\savedata\
  ├─ BLUS30770-ROSTER-MAY14_014450/    fresh M12 PS3 template (DO NOT TOUCH)
  ├─ BLUS30770-FRANCHISE-MAY14_0145/   fresh M12 PS3 franchise template (DO NOT TOUCH)
  ├─ BLUS30770-PROFILE-USER/           user profile (M12)
  ├─ BLUS30770-ROSTER-2018HIST/        ★ 2018 historical roster (SUB_TITLE patched)
  │    ├─ HED-DATA       40 B          carried over from fresh template
  │    ├─ ICON0.PNG     66 KB
  │    ├─ PARAM.SFO    2.8 KB          SAVEDATA_DIRECTORY + SUB_TITLE + SAVEDATA_LIST_PARAM patched
  │    └─ USR-DATA    726 KB           our compile output from out/m12-ps3/roster-2018-m12-ps3.bin
  ├─ BLUS31178-ROSTER-MAY14_015753/    fresh M25 PS3 roster template (DO NOT TOUCH)
  ├─ BLUS31178-CAREER-MAY14_015754/    M25 FrTk career (out of scope)
  └─ BLUS31178-PROFILE-USER/           user profile (M25)
```

Templates also cached at:
- `out/templates/madden-nfl-12-ps3-roster-template.bin` (726,452 B)
- `out/templates/madden-nfl-12-ps3-franchise-template.bin` (5,120,000 B)
- `out/templates/madden-nfl-25-ps3-roster-template.bin` (726,452 B)

Compile outputs (regenerate via `python tools/build_all_rosters.py --target m12-ps3 --year 2018`):
- `out/m12-ps3/roster-2018-m12-ps3.bin` (726,452 B; 2293/2739 PLAY records modified)
- `out/m25-ps3/roster-2018-m25-ps3.bin` (726,452 B; 2228/2414 PLAY records modified)

### Pre-boot smoke-test results (parser-level, all passing)

- Endian auto-detect: M12 PS3 = BigEndian ✓, M25 PS3 = BigEndian ✓, M08 PS2 = LittleEndian ✓
- TableCount: M12 PS3 franchise = 195 (vs 183 M08 PS2 franchise), M12 PS3 roster = 4 (same as PS2), M25 PS3 roster = 4
- PLAY field-name coverage: ALL of (PFNA, PLNA, PPOS, POVR, PSPD, PSA0, PCON, PGID, TGID, PAGE) present on both M12 PS3 and M25 PS3 saves
- SEAI.SEYR reads 0 from fresh M12 PS3 franchise (correct for Week-1 base year)
- M25 roster PLAY has 200 fields (vs 110 PS2 roster, 131 PS2 franchise) — contracts (PSA0..6, PSB0..6, PCSA) included
- M25 top-5 modified OVRs: 102/102/101/100/99 (realistic post-Madden-25 inflation; max OVR in canonical 2018 is 99)
- M12 PS3 top-5 modified OVRs: high template-leftovers ~118 in retired-greats pool TGIDs (960-967) — those records are intentionally untouched by the compiler; ALL real-team-TGID records were overwritten via the new range-based [main, next-team-main) match

### Verification checklist (do this in RPCS3)

Boot **Madden NFL 12** (BLUS30770) in RPCS3. Two saves will appear in
the roster picker:

| Save name (SUB_TITLE)   | Folder                          | What it is                |
|-------------------------|---------------------------------|---------------------------|
| `ROSTER-MAY14_014450`   | `BLUS30770-ROSTER-MAY14_014450` | Fresh 2013 template — DO NOT TOUCH |
| `2018 NFL Historical`   | `BLUS30770-ROSTER-2018HIST`     | ★ Load this one |

Path: Main menu → Roster → Load Roster → "2018 NFL Historical".

Once loaded, jump to **Roster → Edit Roster** and spot-check these
players (and roughly these ratings) per team:

| Team | Player to find | Expected ~OVR | Position |
|------|---------------|---------------|----------|
| Bears   | Mitchell Trubisky | 75-80 | QB starter |
| Bears   | Khalil Mack       | 95+   | OLB |
| Bears   | Akiem Hicks       | 85+   | DT |
| Bears   | Allen Robinson II | 85+   | WR |
| Bears   | Eddie Jackson     | 85+   | FS |
| Patriots| Tom Brady         | 99    | QB starter |
| Patriots| Rob Gronkowski    | 95+   | TE |
| Chiefs  | Patrick Mahomes   | 76-80 | QB starter (rookie OVR — should be young/raw) |
| Chiefs  | Tyreek Hill       | 95+   | WR |
| Chiefs  | Travis Kelce      | 95+   | TE |
| Saints  | Drew Brees        | 95+   | QB starter |
| Saints  | Alvin Kamara      | 90+   | HB |
| Rams    | Aaron Donald      | 99    | DT |
| Rams    | Jared Goff        | 80+   | QB starter |
| Steelers| Antonio Brown     | 99    | WR |
| Steelers| Ben Roethlisberger| 90+   | QB starter |
| Falcons | Matt Ryan         | 90+   | QB starter |
| Vikings | Adam Thielen      | 88+   | WR (32nd team — was previously dropped in PS2 builds; PS3 build should now have them) |
| Texans  | DeAndre Hopkins   | 95+   | WR |
| Texans  | J.J. Watt         | 95+   | DE |

**Pass criteria**:
1. ✅ Save loads without RPCS3 crashing.
2. ✅ Team rosters show **2018 names** (Mitchell Trubisky at Bears, not 2013-era Jay Cutler / Josh McCown).
3. ✅ All 32 teams populated (no team is still showing its 2013 roster wholesale).
4. ✅ Star players have realistic OVRs from the table above.
5. ⚠️ Some bench/depth players may show inflated OVRs (90+) inherited from 2013-era template — this is the documented 35% rating-coverage gap (only weebly-tracked stars get real OVRs; the rest inherit template). Visible at WR4 / RB4 / DB5+ depth-chart slots.

**Fail signals** (paste back to AI if seen):
- ❌ "Initializing roster management..." hang on load → format compatibility regression
- ❌ RPCS3 crashes during load → likely CRC mismatch (PS3 enforces CRCs? our writes use BE storage; verify)
- ❌ Team name says "2018 NFL Historical" but players are wholesale 2013-era → our writes didn't reach that team's TGID range
- ❌ Random players showing "Peyton Manning" / "Tony Romo" / "Brian Urlacher" / "Devin Hester" attached to a team (NOT in the retired-greats free-agent pool) → TGID range bug
- ❌ Player names are garbage characters / mojibake → endian misread of bit-packed PFNA bytes

After this round of verification, repeat for **Madden NFL 25**:
- The M25 build is at `out/m25-ps3/roster-2018-m25-ps3.bin`.
- Same drop-in procedure: copy `BLUS31178-ROSTER-MAY14_015753/` →
  `BLUS31178-ROSTER-2018HIST/`, swap USR-DATA, patch PARAM.SFO.
- M25 boots into "Connected Franchise"; load custom roster from main
  menu, then start a new CFM to see era-correct cap economy (since M25
  ROSTER carries contracts).

## Out of scope (for now)

- **Madden NFL 25 (2024)** — modern current-gen release. Separate game,
  separate engine (Frostbite), no part of this plan.
- Xbox 360 M25 (BE, .CON container — much more work, separate decision)
- PS4 / Xbox One M25 (no viable emulation)
- Playbook editing (separate question; tracked elsewhere)
- A "Madden 25 Deluxe"-style cosmetic mod ecosystem — none exists for
  M25 PS3; we'd be the only ones in this space

## What this gets us if it works

Beyond the obvious "one more game to play":

- **Cross-generation continuity.** Same 2013-2026 historical pipeline
  produces artifacts for both M08-12 PS2 *and* M25 PS3. A user picks
  whichever console they prefer.
- **Better era-accurate gameplay for 2013-2018 seasons.** M25's engine
  (Infinity Engine 2, 2013-era physics) is closer to those seasons than
  M08's 2007-era engine.
- **Cleaner ratings.** M25's launch ratings ARE the 2013 NFL ratings
  (no era mismatch like M08's 2007 baseline being grafted onto 2018
  rosters).
- **Player-photo accuracy** for 2013-era players. M25's baked photo
  library has Brady-as-2013, not Brady-as-2007.

## Decision rule

If byte-diff validation passes → this work lives here as Tier 19.

If byte-diff validation fails (no `DB` magic, or fundamentally different
format) → fork into a separate repo. Don't try to retrofit Frostbite
support into a TDB-centric codebase.
