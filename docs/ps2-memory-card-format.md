# PS2 memory card format: pages, ECC, and writing in place

This pipeline currently gets saves onto a card by shelling out to `mymcplus`
(`tools/pack_baslus.py`). That works, but it means we treat the card itself as
a black box: we can only *add* a save to a freshly formatted card, we can't
edit a save already on one, and we depend on an external tool being installed.

This document records the card format as verified against a real 8 MB card, so
that constraint is a choice rather than a limitation. It came out of the
ESPN NFL 2K5 PS2 save work, but everything here is game-agnostic — it is the
Sony card format, not anything 2K- or EA-specific.

## Card geometry

A card image is a flat array of **528-byte pages**: 512 bytes of data followed
by a 16-byte spare area. An 8 MB card is 16,384 pages (8,650,752 bytes).

Pages are grouped into **clusters**; the cluster size is read from the
superblock rather than assumed (2 pages/cluster on a standard 8 MB card).

## The spare area is ECC, and you must recompute it

The 16 spare bytes hold **three ECC bytes for each of the page's four 128-byte
chunks** (12 bytes used, the last 4 are zero on every real page). A page
written without correct ECC is treated as damaged.

The algorithm is the long-documented public-domain one used by PS2 memory-card
tools. Two details are easy to get wrong and account for essentially all the
difficulty:

1. **The accumulators seed with `0x77`, `0x7F`, `0x7F`** — not zero.
2. **The two line-parity bytes are 7-bit fields**, masked with `0x7F`.

Getting the column masks right but seeding from zero produces a result that is
correct for roughly 80% of chunks — enough to look like it works. Both facts
matter.

The code is linear over GF(2): the column-parity contribution of a byte is the
XOR of a fixed mask per set bit, so the 256-entry mask table can be generated
from eight basis values rather than transcribed:

```
basis = (0x07, 0x16, 0x25, 0x34, 0x43, 0x52, 0x61, 0x70)

for each byte b at index i in the 128-byte chunk:
    column ^= XOR of basis[bit] for each set bit of b
    if popcount(b) is odd:
        line_a ^= (~i) & 0x7F
        line_b ^= i
ecc = (column & 0xFF, line_a & 0x7F, line_b & 0x7F)
```

**Verification**: this reproduces the ECC of **65,472 of 65,472** written
chunks on a real card, byte-exact. The remaining 64 chunks are erased pages
whose spare is `FF FF FF` with no ECC written — that is correct behaviour, not
a mismatch, and it cannot collide with a real value because the computed line
bytes are masked to `0x7F` and so can never be all-ones.

## Filesystem layout

Read these from page 0 (the superblock) rather than hardcoding them:

| Offset | Meaning |
|---|---|
| `0x00` | magic, begins `Sony PS2 Memory Card Format` |
| `0x2A` | `u16` pages per cluster |
| `0x34` | `u32` alloc offset (first data cluster) |
| `0x38` | `u32` alloc end |
| `0x3C` | `u32` root directory cluster |
| `0x50` | `u32[32]` indirect FAT cluster list |

The FAT is two levels: the indirect list points at clusters that themselves
hold the FAT cluster numbers, and those hold the entries. A chain terminates
when the entry is `0xFFFFFFFF` or its low 31 bits are `0x7FFFFFFF`; the next
cluster is `entry & 0x7FFFFFFF`. **Always bound the walk** — a corrupt card can
otherwise loop forever.

Directory entries are 512 bytes: `u32` mode at `0x00`, `u32` length at `0x04`,
`u32` first cluster at `0x10`, and a NUL-terminated name at `0x40`. Mode bit
`0x20` marks a directory and bit `0x8000` marks an entry as existing — deleted
saves keep their entry with that bit clear, which is why deleted saves are
often recoverable. For a directory, `length` is its entry count.

## Writing into a card without relinking it

The useful trick: **if no file changes length, the FAT and directory entries
never need to change.** Only the data pages are rewritten, each with its ECC
recomputed. That reduces a card write to a bounded, local operation and avoids
allocation logic entirely.

Rules worth keeping if you implement this here:

- **Refuse any size change.** If the edited file is a different length than the
  directory entry says, stop — do not attempt to relink.
- **Stage before committing.** Collect the pages for a file, and only apply
  them once the whole payload is placed. A card whose FAT chain is too short to
  hold the file it declares should be refused, not half-written.
- **Preserve the tail of the final page.** Files rarely end on a 512-byte
  boundary; read the existing page and overwrite only the bytes you are
  replacing.
- **Never write to the source card.** Compare `(st_dev, st_ino)`, not resolved
  paths — a hard link has a different path and the same inode, and comparing
  paths lets a write land on the user's card.

## `.psu` (what `pack_baslus.py` already produces)

A `.psu` is much simpler than a card: a 512-byte directory entry for the save
folder, then `.` and `..` entries, then for each file a 512-byte entry followed
by its data **padded to a 1024-byte boundary**. Entry modes are `0x8427` for
directories and `0x8417` for files. Preserving the original 512-byte entries on
a rewrite keeps timestamps and attributes byte-identical.

## What does *not* transfer from the 2K5 work

- **ESPN NFL 2K5 stores a CRC-32 of the save payload in a 4-byte `EXTRA`
  file** in the save directory. That is a Visual Concepts convention. Madden
  and NCAA saves carry their own integrity fields inside the TDB — do not
  assume `EXTRA` semantics here.
- The `ROST` container and its ten tables are 2K5's roster format and have
  nothing to do with the TDB schema in `docs/madden08-tdb-schema.md`.

The card layer below the save payload, though — pages, ECC, FAT, directory
entries, `.psu` — is shared by every PS2 game, including all of ours.

## Reference implementation

A working, tested implementation lives in the 2K5 mod-tools repo
(`tools/nfl2k5_ps2_save.py` for the reader/writer and page ECC,
`tools/nfl2k5_ps2_save_verify.py` for an independent verifier). Both are
standard-library Python 3.9+ and carry no dependency on that project's editor,
so the card-layer functions port here directly if we want to drop the external
packing dependency.

Worth copying alongside the code: the verifier there deliberately re-derives
the format from its own constants instead of importing the writer's, so a bug
in one is not invisible to the other, and it is mutation-tested — a flipped ECC
byte, a tampered neighbouring save, and an out-of-range edit are each proven to
fail verification rather than assumed to.
