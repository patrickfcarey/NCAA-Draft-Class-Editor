# Madden 08 TDB schema reference

Generated from `madden08-roster-sample.bin` (246784 bytes).

- Magic: `DB` (`0x4244` LE)
- Version: 2048
- DB size: 245856
- Table count: 4
- Checksum: `05e5caf4`

## Tables

| Name | Records (cur/max) | Bytes/record | Bits/record | Fields |
|---|---|---|---|---|
| `DCHT` | 1745 / 2912 | 8 | 63 | 4 |
| `INJY` | 115 / 320 | 8 | 63 | 5 |
| `PLAY` | 1995 / 2048 | 104 | 831 | 110 |
| `TEAM` | 33 / 33 | 116 | 927 | 66 |

## Table `DCHT` (1745 records × 8 bytes)

| Code | Type | Bit offset | Bits | Sample values | Likely meaning |
|---|---|---|---|---|---|
| `PGID` | UINT | 0 | 15 | 218, 218, 224, 365, 399 | Player global ID |
| `TGID` | UINT | 15 | 10 | 1, 1, 1, 1, 1 | Team ID (FK to TEAM) |
| `PPOS` | UINT | 25 | 5 | 7, 24, 14, 0, 4 | Position code |
| `ddep` | UINT | 30 | 5 | 0, 0, 0, 1, 0 | Unknown |

## Table `INJY` (115 records × 8 bytes)

| Code | Type | Bit offset | Bits | Sample values | Likely meaning |
|---|---|---|---|---|---|
| `INJL` | UINT | 0 | 8 | 254, 254, 254, 254, 254 | Unknown |
| `INJT` | UINT | 8 | 8 | 203, 220, 203, 178, 220 | Unknown |
| `PGID` | UINT | 16 | 15 | 261, 322, 27491, 28003, 28018 | Player global ID |
| `TGID` | UINT | 31 | 10 | 1, 1, 1, 1, 1 | Team ID (FK to TEAM) |
| `INIR` | UINT | 41 | 1 | 1, 1, 1, 1, 1 | Unknown |

## Table `PLAY` (1995 records × 104 bytes)

| Code | Type | Bit offset | Bits | Sample values | Likely meaning |
|---|---|---|---|---|---|
| `PTSA` | UINT | 0 | 16 | 1800, 3480, 1700, 200, 1310 | Throw Accuracy Short (?) |
| `POID` | UINT | 16 | 16 | 218, 224, 261, 322, 365 | Pro Bowl / overall ID |
| `PVTS` | UINT | 32 | 16 | 1800, 3480, 1700, 200, 1310 | Player numeric attribute (16-bit) |
| `PFNA` | STRING | 48 | 88 | "Olin", "Brian", "Mike" | First Name (string, 11 chars) |
| `PLNA` | STRING | 136 | 104 | "Kreutz", "Urlacher", "Brown" | Last Name (string, 13 chars) |
| `PLPL` | UINT | 240 | 8 | 100, 100, 100, 100, 100 | Player numeric attribute (8-bit) |
| `PEGO` | SINT | 248 | 8 | 62, 95, 68, 94, 82 | Player numeric attribute (8-bit) |
| `PWGT` | UINT | 256 | 8 | 133, 94, 47, 144, 55 | Weight (lb, offset-encoded; +160 to get real lbs) |
| `PRL2` | UINT | 264 | 6 | 45, 13, 45, 45, 45 | Player numeric attribute (6-bit) |
| `PLHA` | UINT | 270 | 3 | 3, 2, 2, 2, 0 | Left hand (?) |
| `TLHA` | UINT | 273 | 3 | 3, 3, 2, 2, 0 | Tape/glove left hand (?) |
| `PRHA` | UINT | 276 | 3 | 3, 2, 2, 2, 0 | Right hand (?) |
| `TRHA` | UINT | 279 | 3 | 3, 3, 2, 2, 0 | Tape/glove right hand (?) |
| `PTHA` | UINT | 282 | 7 | 25, 17, 17, 35, 88 | Throw Accuracy (rating) |
| `PSTA` | UINT | 289 | 7 | 65, 95, 90, 69, 90 | Stamina (rating) |
| `PFPB` | UINT | 296 | 1 | 1, 1, 1, 0, 0 | Pro Bowl flag (1 = Pro Bowl appearance) |
| `PVSB` | UINT | 297 | 14 | 600, 1400, 500, 20, 500 | Visor + various flags (composite) |
| `PKAC` | UINT | 311 | 7 | 24, 18, 26, 17, 36 | Kick Accuracy (rating) |
| `PACC` | UINT | 318 | 7 | 85, 93, 88, 66, 55 | Acceleration (rating) |
| `PMPC` | UINT | 325 | 2 | 0, 0, 0, 0, 0 | Player flag / enum (2-bit) |
| `PHED` | UINT | 327 | 4 | 7, 4, 0, 0, 7 | Head model / hair style |
| `PGID` | UINT | 331 | 15 | 218, 224, 261, 322, 365 | Player global ID |
| `TGID` | UINT | 346 | 10 | 1, 1, 1, 1, 1 | Team ID (FK to TEAM) |
| `PSPD` | UINT | 356 | 7 | 62, 88, 87, 46, 54 | Speed (rating) |
| `PAGE` | UINT | 363 | 6 | 30, 29, 29, 35, 32 | Age (years) |
| `PBRE` | UINT | 369 | 2 | 0, 0, 0, 0, 0 | Player flag / enum (2-bit) |
| `PEYE` | UINT | 371 | 1 | 0, 1, 1, 0, 0 | Eye black |
| `PTGH` | UINT | 372 | 7 | 95, 97, 90, 94, 80 | Toughness |
| `PCPH` | UINT | 379 | 3 | 2, 1, 1, 2, 2 | Hand pads (?) |
| `PLSH` | UINT | 382 | 3 | 0, 0, 0, 0, 0 | Left arm pad (?) |
| `PRSH` | UINT | 385 | 3 | 0, 0, 0, 0, 0 | Right arm pad (?) |
| `PCTH` | UINT | 388 | 7 | 24, 62, 75, 18, 37 | Catch (rating) |
| `PLTH` | UINT | 395 | 1 | 0, 0, 0, 0, 0 | Player flag / enum (1-bit) |
| `PRTH` | UINT | 396 | 1 | 0, 0, 0, 0, 0 | Player flag / enum (1-bit) |
| `PAGI` | UINT | 397 | 7 | 63, 88, 88, 43, 56 | Agility (rating) |
| `PSKI` | UINT | 404 | 2 | 0, 0, 1, 2, 0 | Skin tone |
| `PDPI` | UINT | 406 | 6 | 3, 9, 8, 14, 30 | Player numeric attribute (6-bit) |
| `PPTI` | UINT | 412 | 10 | 1015, 1015, 1015, 1009, 1009 | Player numeric attribute (10-bit) |
| `PINJ` | UINT | 422 | 7 | 85, 92, 60, 61, 65 | Injury (rating) |
| `PTAK` | UINT | 429 | 7 | 29, 94, 71, 13, 19 | Tackle (rating) |
| `PPBK` | UINT | 436 | 7 | 90, 21, 15, 86, 36 | Pass Block (rating) |
| `PRBK` | UINT | 443 | 7 | 93, 53, 28, 89, 35 | Run Block (rating) |
| `PNEK` | UINT | 450 | 2 | 2, 0, 0, 2, 0 | Neck pad |
| `PFMK` | UINT | 452 | 4 | 2, 3, 13, 2, 1 | Facemask |
| `PBTK` | UINT | 456 | 7 | 10, 10, 47, 10, 47 | Break Tackle (rating) |
| `PTAL` | UINT | 463 | 6 | 0, 0, 0, 0, 0 | Player numeric attribute (6-bit) |
| `PHCL` | UINT | 469 | 3 | 2, 1, 0, 0, 2 | Hair color |
| `PUCL` | UINT | 472 | 5 | 0, 0, 0, 0, 0 | Player numeric attribute (5-bit) |
| `PLEL` | UINT | 477 | 4 | 2, 0, 7, 2, 0 | Player flag / enum (4-bit) |
| `TLEL` | UINT | 481 | 4 | 2, 0, 8, 2, 0 | Team attribute (4-bit) |
| `PREL` | UINT | 485 | 4 | 2, 0, 7, 2, 0 | Player flag / enum (4-bit) |
| `TREL` | UINT | 489 | 4 | 2, 0, 8, 2, 0 | Team attribute (4-bit) |
| `PCOL` | UINT | 493 | 9 | 252, 145, 142, 166, 117 | College ID (NFL team's player came from) |
| `PROL` | UINT | 502 | 6 | 26, 41, 45, 24, 10 | Player numeric attribute (6-bit) |
| `PGSL` | UINT | 508 | 3 | 0, 0, 0, 0, 0 | Player flag / enum (3-bit) |
| `PTSL` | UINT | 511 | 3 | 0, 0, 0, 0, 0 | Player flag / enum (3-bit) |
| `PCYL` | UINT | 514 | 4 | 3, 3, 1, 1, 4 | Player flag / enum (4-bit) |
| `PHLM` | UINT | 518 | 3 | 0, 0, 4, 0, 0 | Player flag / enum (3-bit) |
| `PSTM` | UINT | 521 | 7 | 0, 0, 0, 0, 0 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PHAN` | UINT | 528 | 1 | 0, 0, 0, 0, 0 | Player flag / enum (1-bit) |
| `PICN` | UINT | 529 | 1 | 0, 1, 0, 0, 0 | Player flag / enum (1-bit) |
| `PJEN` | UINT | 530 | 7 | 57, 54, 30, 74, 14 | Jersey number |
| `PTEN` | UINT | 537 | 2 | 0, 0, 0, 0, 0 | Tenacity (rating) |
| `PCON` | UINT | 539 | 4 | 3, 7, 5, 1, 5 | Player flag / enum (4-bit) |
| `PSBO` | UINT | 543 | 13 | 600, 1400, 500, 20, 500 | Player numeric attribute (13-bit) |
| `PVCO` | UINT | 556 | 4 | 3, 7, 5, 1, 5 | Player flag / enum (4-bit) |
| `PFHO` | UINT | 560 | 1 | 0, 0, 0, 0, 0 | Player flag / enum (1-bit) |
| `PDRO` | UINT | 561 | 4 | 3, 1, 2, 1, 3 | Player flag / enum (4-bit) |
| `PTHP` | UINT | 565 | 7 | 31, 31, 20, 20, 87 | Throw Power (rating) |
| `PIMP` | UINT | 572 | 7 | 80, 86, 70, 64, 63 | Impact / Hit Power (rating) |
| `PJMP` | UINT | 579 | 7 | 18, 81, 77, 10, 50 | Jumping (rating) |
| `PYRP` | UINT | 586 | 5 | 9, 7, 7, 12, 9 | Years pro (NFL experience) |
| `PSXP` | UINT | 591 | 13 | 25, 43, 9, 10, 16 | Player numeric attribute (13-bit) |
| `PCAR` | UINT | 604 | 7 | 21, 47, 41, 15, 37 | Carry/Carrying (rating) |
| `PTAR` | UINT | 611 | 6 | 0, 9, 0, 0, 0 | Player numeric attribute (6-bit) |
| `PJER` | UINT | 617 | 1 | 0, 1, 1, 0, 1 | Player flag / enum (1-bit) |
| `PMOR` | UINT | 618 | 7 | 95, 90, 90, 84, 75 | Morale (rating) |
| `PKPR` | UINT | 625 | 7 | 20, 17, 17, 14, 21 | Kick Power (rating) |
| `PSTR` | UINT | 632 | 7 | 92, 77, 56, 90, 54 | Strength (rating) |
| `POVR` | UINT | 639 | 7 | 98, 98, 90, 87, 80 | Overall (rating) |
| `PAWR` | UINT | 646 | 7 | 92, 93, 84, 92, 78 | Awareness (rating) |
| `PLWR` | UINT | 653 | 4 | 2, 3, 0, 0, 2 | Left wrist (?) |
| `TLWR` | UINT | 657 | 4 | 2, 3, 0, 0, 2 | Tape left wrist (?) |
| `PRWR` | UINT | 661 | 4 | 2, 3, 0, 0, 2 | Right wrist (?) |
| `TRWR` | UINT | 665 | 4 | 2, 3, 0, 0, 2 | Tape right wrist (?) |
| `PFAS` | UINT | 669 | 7 | 17, 17, 1, 17, 5 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PMAS` | UINT | 676 | 7 | 10, 7, 10, 10, 7 | Medium throw accuracy (?) |
| `PSBS` | UINT | 683 | 7 | 24, 13, 2, 24, 0 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PFCS` | UINT | 690 | 7 | 10, 10, 10, 10, 10 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PMCS` | UINT | 697 | 7 | 25, 7, 0, 25, 5 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PFGS` | UINT | 704 | 7 | 5, 10, 10, 5, 10 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PCHS` | UINT | 711 | 7 | 17, 12, 4, 17, 5 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PFHS` | UINT | 718 | 7 | 0, 10, 10, 0, 10 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PMHS` | UINT | 725 | 7 | 0, 13, 7, 0, 5 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PVIS` | UINT | 732 | 2 | 0, 0, 0, 0, 0 | Player flag / enum (2-bit) |
| `PPOS` | UINT | 734 | 5 | 7, 14, 17, 6, 0 | Position code |
| `POPS` | UINT | 739 | 5 | 7, 14, 17, 6, 0 | Player numeric attribute (5-bit) |
| `PLSS` | UINT | 744 | 7 | 25, 10, 5, 25, 6 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PFTS` | UINT | 751 | 7 | 0, 10, 10, 0, 10 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PMTS` | UINT | 758 | 7 | 3, 2, 6, 3, 7 | Throw on the run (?) |
| `PUTS` | UINT | 765 | 7 | 0, 12, 4, 0, 5 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PMUS` | UINT | 772 | 2 | 0, 0, 0, 0, 0 | Player flag / enum (2-bit) |
| `PHGT` | UINT | 774 | 7 | 74, 76, 70, 75, 75 | Height (inches) |
| `PCMT` | UINT | 781 | 10 | 999, 457, 54, 999, 196 | Player numeric attribute (10-bit) |
| `PKRT` | UINT | 791 | 7 | 0, 0, 12, 0, 0 | Player rating (guess - 7-bit field, fits 0-127 / typical Madden 0-99 range) |
| `PYWT` | UINT | 798 | 5 | 9, 7, 7, 3, 1 | Player numeric attribute (5-bit) |
| `PLHY` | SINT | 803 | 6 | 33, 33, 33, 33, 33 | Player numeric attribute (6-bit) |
| `PJTY` | UINT | 809 | 3 | 0, 0, 1, 1, 0 | Player flag / enum (3-bit) |
| `PSTY` | UINT | 812 | 2 | 0, 0, 0, 0, 0 | Player flag / enum (2-bit) |
| `PFEx` | UINT | 814 | 10 | 150, 150, 58, 207, 239 | Player numeric attribute (10-bit) |

## Table `TEAM` (33 records × 116 bytes)

| Code | Type | Bit offset | Bits | Sample values | Likely meaning |
|---|---|---|---|---|---|
| `TDNA` | STRING | 0 | 136 | "Bears", "Bengals", "Bills" | Team Display Name (e.g., 'Bears') |
| `TLNA` | STRING | 136 | 144 | "Chicago", "Cincinnati", "Buffalo" | Team Location/City Name (e.g., 'Chicago') |
| `TSNA` | STRING | 280 | 56 | "CHI", "CIN", "BUF" | Team Short Name / Abbreviation (e.g., 'CHI') |
| `TB2B` | UINT | 336 | 8 | 57, 0, 33, 57, 57 | Team attribute (8-bit) |
| `TBCB` | UINT | 344 | 8 | 63, 27, 70, 76, 13 | Team attribute (8-bit) |
| `TMNC` | STRING | 352 | 136 | "Bears", "Bengals", "Bills" | Team string (17 chars) |
| `CYID` | UINT | 488 | 8 | 0, 1, 2, 3, 4 | Unknown |
| `TB2G` | UINT | 496 | 8 | 104, 0, 42, 104, 104 | Team attribute (8-bit) |
| `TBCG` | UINT | 504 | 8 | 39, 74, 49, 34, 35 | Team attribute (8-bit) |
| `TCDO` | UINT | 512 | 8 | 1, 2, 3, 4, 5 | Team attribute (8-bit) |
| `TCRP` | UINT | 520 | 8 | 0, 1, 2, 3, 4 | Team attribute (8-bit) |
| `TB2R` | UINT | 528 | 8 | 199, 0, 218, 199, 199 | Team attribute (8-bit) |
| `TBCR` | UINT | 536 | 8 | 8, 193, 13, 0, 59 | Team attribute (8-bit) |
| `TGPT` | UINT | 544 | 8 | 1, 2, 3, 4, 5 | Team attribute (8-bit) |
| `TCTX` | UINT | 552 | 8 | 1, 2, 3, 4, 5 | Team attribute (8-bit) |
| `TRV1` | UINT | 560 | 10 | 20, 5, 12, 23, 29 | Team Rivalry 1 (?) |
| `TEZ1` | UINT | 570 | 10 | 0, 1, 2, 3, 4 | Team Endzone 1 (?) |
| `TRV2` | UINT | 580 | 10 | 31, 29, 22, 9, 25 | Team Rivalry 2 (?) |
| `TEZ2` | UINT | 590 | 10 | 1, 0, 0, 0, 2 | Team Endzone 2 (?) |
| `TRV3` | UINT | 600 | 10 | 19, 30, 18, 8, 2 | Team Rivalry 3 (?) |
| `TLSA` | UINT | 610 | 9 | 0, 0, 0, 0, 0 | Team attribute (9-bit) |
| `TMSA` | UINT | 619 | 19 | 9492, 7628, 6478, 6657, 7704 | Team attribute (19-bit) |
| `TRDB` | UINT | 638 | 7 | 79, 74, 91, 92, 74 | Team attribute (7-bit) |
| `TRLB` | UINT | 645 | 7 | 88, 85, 94, 92, 72 | Team attribute (7-bit) |
| `TDPB` | UINT | 652 | 6 | 0, 1, 2, 3, 4 | Team attribute (6-bit) |
| `TOPB` | UINT | 658 | 6 | 0, 1, 2, 3, 4 | Team attribute (6-bit) |
| `TRQB` | UINT | 664 | 7 | 70, 78, 65, 82, 73 | Team attribute (7-bit) |
| `TRRB` | UINT | 671 | 7 | 79, 86, 90, 78, 68 | Team attribute (7-bit) |
| `CGID` | UINT | 678 | 2 | 1, 0, 0, 0, 0 | Unknown |
| `DGID` | UINT | 680 | 5 | 4, 0, 2, 3, 0 | Unknown |
| `LGID` | UINT | 685 | 2 | 0, 0, 0, 0, 0 | Unknown |
| `SGID` | UINT | 687 | 7 | 0, 1, 2, 3, 4 | Unknown |
| `TGID` | UINT | 694 | 10 | 1, 2, 3, 4, 5 | Team ID (FK to TEAM) |
| `TOID` | UINT | 704 | 10 | 1, 2, 3, 4, 5 | Team attribute (10-bit) |
| `FRID` | UINT | 714 | 7 | 0, 1, 2, 3, 4 | Unknown |
| `TSID` | UINT | 721 | 10 | 1, 2, 3, 4, 5 | Team attribute (10-bit) |
| `TORD` | UINT | 731 | 10 | 1, 2, 3, 4, 5 | Team attribute (10-bit) |
| `TRDE` | UINT | 741 | 7 | 97, 74, 70, 89, 74 | Team attribute (7-bit) |
| `TCHE` | UINT | 748 | 1 | 0, 0, 0, 0, 0 | Team attribute (1-bit) |
| `TROF` | UINT | 749 | 7 | 83, 92, 65, 89, 67 | Team attribute (7-bit) |
| `TDRI` | UINT | 756 | 9 | 0, 1, 2, 3, 4 | Team attribute (9-bit) |
| `TRDL` | UINT | 765 | 7 | 75, 76, 75, 76, 74 | Team attribute (7-bit) |
| `TMFL` | UINT | 772 | 10 | 0, 1, 2, 3, 4 | Team attribute (10-bit) |
| `TLGL` | UINT | 782 | 9 | 0, 1, 2, 3, 4 | Team attribute (9-bit) |
| `TROL` | UINT | 791 | 7 | 83, 88, 78, 93, 69 | Team attribute (7-bit) |
| `TPSL` | UINT | 798 | 6 | 3, 8, 9, 7, 12 | Team attribute (6-bit) |
| `TFTL` | UINT | 804 | 7 | 0, 1, 2, 3, 4 | Team attribute (7-bit) |
| `DISN` | UINT | 811 | 3 | 0, 1, 0, 0, 2 | Unknown |
| `TUHO` | UINT | 814 | 4 | 0, 0, 0, 0, 0 | Team attribute (4-bit) |
| `TFLO` | UINT | 818 | 1 | 0, 0, 0, 0, 0 | Team attribute (1-bit) |
| `TAUO` | UINT | 819 | 5 | 0, 0, 0, 0, 0 | Team attribute (5-bit) |
| `TREP` | UINT | 824 | 10 | 750, 600, 150, 550, 300 | Team attribute (10-bit) |
| `TGRP` | UINT | 834 | 10 | 1, 2, 3, 4, 5 | Team attribute (10-bit) |
| `TTYP` | UINT | 844 | 5 | 0, 0, 0, 0, 0 | Team attribute (5-bit) |
| `TWRR` | UINT | 849 | 7 | 82, 90, 86, 84, 76 | Team attribute (7-bit) |
| `TLGS` | UINT | 856 | 9 | 0, 1, 2, 3, 4 | Team attribute (9-bit) |
| `TVIS` | UINT | 865 | 1 | 1, 1, 1, 1, 1 | Team attribute (1-bit) |
| `TVQS` | UINT | 866 | 1 | 1, 1, 1, 1, 1 | Team attribute (1-bit) |
| `TPST` | UINT | 867 | 6 | 0, 0, 0, 0, 0 | Team attribute (6-bit) |
| `TRST` | UINT | 873 | 7 | 92, 86, 90, 95, 74 | Team attribute (7-bit) |
| `TROV` | UINT | 880 | 7 | 91, 87, 69, 89, 67 | Team attribute (7-bit) |
| `TUAW` | UINT | 887 | 4 | 1, 1, 1, 1, 1 | Team attribute (4-bit) |
| `TSOW` | UINT | 891 | 1 | 0, 0, 0, 0, 0 | Team attribute (1-bit) |
| `TPSW` | UINT | 892 | 6 | 13, 8, 7, 9, 4 | Team attribute (6-bit) |
| `TAth` | UINT | 898 | 1 | 0, 0, 0, 0, 0 | Team attribute (1-bit) |
| `TAss` | UINT | 899 | 1 | 0, 0, 0, 0, 0 | Team attribute (1-bit) |

