#!/usr/bin/env python3
"""
Decrypt a PS3 PARAM.PFD-protected save file (USB-export variant).

Port of the algorithm from bucanero/apollo-ps3 (source/pfd_util.c, pfd.c),
itself a port of flatz' original pfdtool. The PS3 keys are public for the
"export-to-USB" save format (all-zero ACCOUNT_ID in PARAM.SFO marks it).
Real-PS3-HDD saves use a per-console-private key and are NOT decryptable
by this script.

Requires: pycryptodome (`pip install --user pycryptodome`).

Algorithm overview:

  Per file (USR-DATA, HED-DATA, PARAM.SFO, ...) listed in PARAM.PFD:
    1. Read the per-entry 64-byte `entry.key` blob and `entry.file_size`.
    2. Build a 16-byte IV from the per-(game,file) `secure_file_id`:
         iv[0,3,4,6,7,9..15] = secure_file_id[0..11]
         iv[1]=11, iv[2]=15, iv[5]=14, iv[8]=10  (magic constants)
    3. AES-128-CBC decrypt entry.key using syscon_manager_key + that IV.
       The resulting 64 bytes contain 4 × 16-byte per-hash-class AES keys;
       the first 16 (PFD_ENTRY_HASH_FILE) are the AES key for the file.
    4. Read the file's data, pad up to a 16-byte multiple (aligned size).
    5. Decrypt block-by-block in a CTR-like mode:
         counter_key = u64_le(block_index) || u64_le(0)
         counter_key_enc = AES-ECB-Encrypt(counter_key, file_key)
         block_dec       = AES-ECB-Decrypt(block, file_key)
         block_plain     = block_dec XOR counter_key_enc
    6. Truncate to the original file_size (drops PFD padding).

The catch: step 2 needs the per-(game,file) `secure_file_id`, a 16-byte
secret embedded in the game's EBOOT.BIN. It is NOT published in any
open-source DB for BLUS30770 (Madden NFL 12 PS3). Apollo Save Tool ships
keys for many games but not for the Madden series. Without it, we can
only get blocks 2-4 of the 64-byte entry.key correctly (CBC propagation);
block 1 (the actual file AES key) requires the right IV and therefore
the secure_file_id.

Usage:
  python tools/ps3/decrypt_pfd.py <save_dir> <file_name> [--secure-id HEX] [--out PATH]

Examples:
  # Try with zero secure_file_id (some games genuinely use it):
  python tools/ps3/decrypt_pfd.py "ALL 32 Franchise Week 1/PS3/SAVEDATA/BLUS30770-FRANCHISE-M25ALL32" USR-DATA

  # Once the M12 secure_file_id is known:
  python tools/ps3/decrypt_pfd.py <save_dir> USR-DATA --secure-id 00112233445566778899AABBCCDDEEFF

The script also dumps blocks 2-4 of the decrypted entry.key (which DON'T
need the IV) — useful for verifying the syscon_manager_key path works
end-to-end before we have the right secure_file_id.
"""
from __future__ import annotations

import argparse
import struct
import sys
from pathlib import Path

try:
    from Crypto.Cipher import AES
except ImportError:
    print("error: pycryptodome not installed. Run: pip install --user pycryptodome",
          file=sys.stderr)
    sys.exit(2)


# --- Constants ported from apollo-ps3 pfd_util.c ---

# The "raw" obfuscated keys from apollo-ps3 are XOR'd at init with this 8-byte
# repeating XOR key. We pre-apply the XOR here so consumers see the real keys.
XOR_KEY = bytes.fromhex("D4D16B0C5DB08791")

def _deobfuscate(raw: bytes) -> bytes:
    return bytes(b ^ XOR_KEY[i % 8] for i, b in enumerate(raw))

# 16-byte AES-128 key used as CBC key for entry-key decryption AND as ECB
# key for the file-data per-block keystream. Stored XOR'd in apollo source.
SYSCON_MANAGER_KEY = _deobfuscate(bytes.fromhex(
    "00C2D39A3E51790E"
    "A1C55637E9E6D5E5"
))

# 20-byte HMAC-SHA1 key used during PFD top/bottom signature derivation.
# Not needed for plain decryption but kept for completeness.
KEYGEN_KEY = _deobfuscate(bytes.fromhex(
    "BFCBA5AE1B07C26C"
    "5B421D37CFB5135C"
    "8799508E"
))

# 20-byte HMAC-SHA1 key for PARAM.SFO entries' signature.
SAVEGAME_PARAM_SFO_KEY = _deobfuscate(bytes.fromhex(
    "D8D96B0254B58395"
    "D9D0640C59B68593"
    "DDD7660F"
))

# --- PFD entry layout (from apollo-ps3 pfd_internal.h, #pragma pack(push,1)) ---
PFD_ENTRY_SIZE = 272
PFD_ENTRY_NAME_SIZE = 65
PFD_ENTRY_KEY_SIZE = 64
PFD_HASH_SIZE = 20
PFD_KEY_SIZE = 16
PFD_FILE_SIZE_ALIGNMENT = 16
PFD_AES_BLOCK_LEN = 16
ENTRY_TABLE_OFFSET = 0x240  # entry_table starts here in a typical PFD


def parse_pfd(pfd_bytes: bytes) -> list[dict]:
    """Return one dict per non-empty PFD entry: file_name, key, file_size."""
    entries = []
    n = (len(pfd_bytes) - ENTRY_TABLE_OFFSET) // PFD_ENTRY_SIZE
    for i in range(n):
        base = ENTRY_TABLE_OFFSET + i * PFD_ENTRY_SIZE
        if base + PFD_ENTRY_SIZE > len(pfd_bytes):
            break
        name_bytes = pfd_bytes[base + 8 : base + 8 + PFD_ENTRY_NAME_SIZE]
        name = name_bytes.split(b"\x00", 1)[0].decode("latin1", errors="replace")
        if not name:
            continue
        key = pfd_bytes[base + 80 : base + 80 + PFD_ENTRY_KEY_SIZE]
        # file_size is u64 at the end of the entry (BE: PFD is BE throughout)
        file_size = struct.unpack_from(">Q", pfd_bytes, base + PFD_ENTRY_SIZE - 8)[0]
        entries.append({"name": name, "key": key, "file_size": file_size, "offset": base})
    return entries


def build_iv_hash_key(secure_file_id: bytes) -> bytes:
    """Build the 16-byte IV used to AES-CBC-decrypt an entry's key field."""
    if len(secure_file_id) != 16:
        raise ValueError(f"secure_file_id must be 16 bytes, got {len(secure_file_id)}")
    iv = bytearray(PFD_KEY_SIZE)
    j = 0
    for i in range(PFD_KEY_SIZE):
        if i == 1:
            iv[i] = 11
        elif i == 2:
            iv[i] = 15
        elif i == 5:
            iv[i] = 14
        elif i == 8:
            iv[i] = 10
        else:
            iv[i] = secure_file_id[j]
            j += 1
    return bytes(iv)


def decrypt_entry_key(encrypted_entry_key: bytes, secure_file_id: bytes) -> bytes:
    """AES-128-CBC decrypt the 64-byte stored entry.key blob. The 16-byte IV
    is built from secure_file_id. Returns 64 bytes; bytes 0..16 are the
    AES-128 file-data key (PFD_ENTRY_HASH_FILE)."""
    if len(encrypted_entry_key) != PFD_ENTRY_KEY_SIZE:
        raise ValueError("encrypted entry key must be 64 bytes")
    iv = build_iv_hash_key(secure_file_id)
    cipher = AES.new(SYSCON_MANAGER_KEY, AES.MODE_CBC, iv)
    return cipher.decrypt(encrypted_entry_key)


def decrypt_file(file_bytes: bytes, file_key: bytes, file_size: int) -> bytes:
    """Decrypt a PFD-encrypted file using the 16-byte per-file AES key.
    Returns plaintext truncated to file_size."""
    if len(file_key) != PFD_KEY_SIZE:
        raise ValueError("file_key must be 16 bytes")

    # Pad up to 16-byte alignment
    aligned = (file_size + PFD_FILE_SIZE_ALIGNMENT - 1) & ~(PFD_FILE_SIZE_ALIGNMENT - 1)
    if len(file_bytes) < aligned:
        # Some saves are stored at exactly aligned length already; if shorter, zero-pad
        file_bytes = file_bytes + b"\x00" * (aligned - len(file_bytes))
    elif len(file_bytes) > aligned:
        file_bytes = file_bytes[:aligned]

    aes_ecb = AES.new(file_key, AES.MODE_ECB)
    out = bytearray(aligned)
    num_blocks = aligned // PFD_AES_BLOCK_LEN
    for i in range(num_blocks):
        # counter_key = u64_LE(i) || u64_LE(0)
        counter = struct.pack("<QQ", i, 0)
        counter_enc = aes_ecb.encrypt(counter)
        block_dec = aes_ecb.decrypt(file_bytes[i * 16 : (i + 1) * 16])
        out[i * 16 : (i + 1) * 16] = bytes(a ^ b for a, b in zip(block_dec, counter_enc))

    return bytes(out[:file_size])


def hex_or_zero(s: str | None) -> bytes:
    if s is None:
        return b"\x00" * 16
    s = s.replace(" ", "").replace(":", "").replace("-", "").strip()
    if len(s) != 32:
        raise ValueError(f"--secure-id must be 32 hex chars (16 bytes), got {len(s)}")
    return bytes.fromhex(s)


def main() -> int:
    p = argparse.ArgumentParser(description=__doc__.split("\n\n")[0])
    p.add_argument("save_dir", type=Path)
    p.add_argument("file_name", help="e.g. USR-DATA, HED-DATA, PARAM.SFO")
    p.add_argument("--secure-id", help="16-byte secure_file_id as 32 hex chars; defaults to all zeros")
    p.add_argument("--out", type=Path, help="output path; defaults to <save_dir>/<file>.decrypted")
    args = p.parse_args()

    save_dir = args.save_dir
    if not save_dir.is_dir():
        print(f"error: {save_dir} is not a directory", file=sys.stderr)
        return 2

    pfd_path = save_dir / "PARAM.PFD"
    if not pfd_path.exists():
        print(f"error: {pfd_path} not found", file=sys.stderr)
        return 2

    pfd = pfd_path.read_bytes()
    entries = parse_pfd(pfd)
    print(f"# PFD has {len(entries)} entries:")
    for e in entries:
        print(f"  {e['name']:20s} file_size={e['file_size']:>10d} offset=0x{e['offset']:x}")

    entry = next((e for e in entries if e["name"].lower() == args.file_name.lower()), None)
    if entry is None:
        print(f"error: {args.file_name} not in PFD entries", file=sys.stderr)
        return 2

    target_path = save_dir / entry["name"]
    if not target_path.exists():
        print(f"error: {target_path} not found", file=sys.stderr)
        return 2

    secure_id = hex_or_zero(args.secure_id)
    print(f"\n# Using secure_file_id: {secure_id.hex()}")
    print(f"# syscon_manager_key:   {SYSCON_MANAGER_KEY.hex()}")
    print(f"# Built IV (for CBC):   {build_iv_hash_key(secure_id).hex()}")

    decrypted_entry_key = decrypt_entry_key(entry["key"], secure_id)
    print(f"\n# Decrypted entry.key (64 bytes):")
    for i in range(0, 64, 16):
        label = ("FILE", "FILE_CID", "FILE_DHK_CID2", "FILE_AID_UID")[i // 16]
        print(f"  block {i//16} ({label:13s}): {decrypted_entry_key[i:i+16].hex()}")
    print("  ^^ Block 0 (FILE) depends on secure_file_id; blocks 1-3 don't.")

    file_key = decrypted_entry_key[:16]
    encrypted_blob = target_path.read_bytes()
    print(f"\n# {entry['name']}: {len(encrypted_blob)} bytes on disk, "
          f"declared {entry['file_size']} bytes in PFD")

    plaintext = decrypt_file(encrypted_blob, file_key, entry["file_size"])

    out_path = args.out or save_dir.parent / f"{entry['name']}.decrypted"
    out_path.write_bytes(plaintext)
    print(f"\n# Wrote decrypted plaintext to {out_path}")
    print(f"# First 32 bytes: {plaintext[:32].hex(' ')}")
    print(f"# Looking for known magic in first 256 bytes...")
    candidates = [
        (b"DB", "TDB magic (bare)"),
        (b"MC02", "MC02 wrapper magic"),
        (b"\x02\x00\x00\x00DB", "TDB w/ 4-byte preamble"),
        (b"PSF", "PARAM.SFO content"),
        (b"\x00PSF", "PARAM.SFO content (with leading null)"),
    ]
    for magic, label in candidates:
        pos = plaintext[:256].find(magic)
        if pos >= 0:
            print(f"  ✓ found {label} ({magic!r}) at offset {pos}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
