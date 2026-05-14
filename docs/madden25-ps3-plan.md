# Madden NFL 25 (2013 anniversary) PS3 support — plan

Status: **gated on byte-diff validation** (need an M25 PS3 save).

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

## Sourcing (current blocker)

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
