#!/usr/bin/env python3
"""
TDB writer for Madden 08 PS2 roster files. Symmetric to parse_madden_tdb.py.

The acid test: parse a TDB file, write it back without modification, and
verify the output bytes are identical to the input. If that roundtrips,
the format is fully understood and we can build a Madden roster compiler.

Run:
    python tools/write_madden_tdb.py <input.bin> <output.bin> [--roundtrip-only]
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

# Reuse the parser's constants and structures
sys.path.insert(0, str(Path(__file__).resolve().parent))
from parse_madden_tdb import (  # type: ignore
    FILE_HEADER_SIZE, TABLE_DEFINITION_SIZE, TABLE_HEADER_SIZE, TABLE_FIELD_SIZE,
    TYPE_STRING, TYPE_BINARY, TYPE_SINT, TYPE_UINT, TYPE_FLOAT,
    parse_tdb, read_field_value,
)


def write_bits(record: bytearray, offset_bits: int, num_bits: int, value: int) -> None:
    """Inverse of parse_madden_tdb.read_bits: LSB-first within byte, LSB-first within field."""
    for i in range(num_bits):
        bit = (value >> i) & 1
        bit_pos = offset_bits + i
        byte_index = bit_pos // 8
        bit_in_byte = bit_pos % 8
        if byte_index < len(record):
            mask = 1 << bit_in_byte
            if bit:
                record[byte_index] |= mask
            else:
                record[byte_index] &= ~mask & 0xFF


def write_string(record: bytearray, offset_bits: int, num_bits: int, value: str) -> None:
    """Strings are byte-aligned ASCII with null padding."""
    byte_offset = offset_bits // 8
    field_width = num_bits // 8
    encoded = value.encode("ascii", errors="replace")[:field_width]
    for i in range(field_width):
        record[byte_offset + i] = encoded[i] if i < len(encoded) else 0


def write_field(record: bytearray, field: dict, value) -> None:
    t = field["type"]
    if t == TYPE_STRING:
        write_string(record, field["offsetBits"], field["bits"], value or "")
    elif t == TYPE_BINARY:
        bo = field["offsetBits"] // 8
        if isinstance(value, str):
            raw = bytes.fromhex(value) if value else b""
        else:
            raw = bytes(value or [])
        for i in range(field["bits"] // 8):
            record[bo + i] = raw[i] if i < len(raw) else 0
    elif t in (TYPE_SINT, TYPE_UINT):
        write_bits(record, field["offsetBits"], field["bits"], int(value or 0))
    elif t == TYPE_FLOAT:
        # 32-bit float, byte-aligned
        bo = field["offsetBits"] // 8
        f = 0.0
        if isinstance(value, (int, float)):
            f = float(value)
        packed = struct.pack("<f", f)
        for i in range(4):
            record[bo + i] = packed[i]


def write_file_header(parsed_header: dict) -> bytes:
    """Reconstruct the 24-byte file header."""
    out = bytearray(FILE_HEADER_SIZE)
    out[0:2] = b"DB"
    out[2:4] = parsed_header["version"].to_bytes(2, "little")
    out[4:8] = parsed_header["unknown1"].to_bytes(4, "little")
    out[8:12] = parsed_header["dbSize"].to_bytes(4, "little")
    out[12:16] = parsed_header["zero"].to_bytes(4, "little")
    out[16:20] = parsed_header["tableCount"].to_bytes(4, "little")
    out[20:24] = bytes.fromhex(parsed_header["checksum"])
    return bytes(out)


def write_table_directory(tables: list[dict]) -> bytes:
    out = bytearray(len(tables) * TABLE_DEFINITION_SIZE)
    for i, t in enumerate(tables):
        base = i * TABLE_DEFINITION_SIZE
        out[base : base + 4] = t["name"].encode("ascii")
        out[base + 4 : base + 8] = t["offset"].to_bytes(4, "little")
    return bytes(out)


def write_table_header(header: dict) -> bytes:
    out = bytearray(TABLE_HEADER_SIZE)
    out[0:4] = bytes.fromhex(header["priorcrc"])
    out[4:8] = header["unknown2"].to_bytes(4, "little")
    out[8:12] = header["lenBytes"].to_bytes(4, "little")
    out[12:16] = header["lenBits"].to_bytes(4, "little")
    out[16:20] = header["zero"].to_bytes(4, "little")
    out[20:22] = header["maxRecords"].to_bytes(2, "little")
    out[22:24] = header["curRecords"].to_bytes(2, "little")
    out[24:28] = header["unknown3"].to_bytes(4, "little")
    out[28] = header["numFields"]
    out[29] = header["indexCount"]
    out[30:32] = header["zero2"].to_bytes(2, "little")
    out[32:36] = header["zero3"].to_bytes(4, "little")
    out[36:40] = bytes.fromhex(header["headercrc"])
    return bytes(out)


def write_field_directory(fields: list[dict]) -> bytes:
    out = bytearray(len(fields) * TABLE_FIELD_SIZE)
    for i, f in enumerate(fields):
        base = i * TABLE_FIELD_SIZE
        out[base : base + 4] = f["type"].to_bytes(4, "little")
        out[base + 4 : base + 8] = f["offsetBits"].to_bytes(4, "little")
        out[base + 8 : base + 12] = f["name"].encode("ascii", errors="replace")[:4].ljust(4, b"\x00")
        out[base + 12 : base + 16] = f["bits"].to_bytes(4, "little")
    return bytes(out)


def write_table_records(header: dict, fields: list[dict], records: list[dict], original_bytes: bytes, records_start: int) -> bytes:
    """Build the records block by writing each field of each record into a
    fresh bytearray. To preserve any bits NOT covered by the schema (TDB
    sometimes leaves zero-or-other bits between fields), we start from the
    original record bytes when available, then overwrite the known fields.
    """
    len_bytes = header["lenBytes"]
    out = bytearray(len_bytes * header["curRecords"])
    for r, rec in enumerate(records):
        # Seed with original bytes to keep any unrecognized bits intact.
        original_rec = original_bytes[records_start + r * len_bytes : records_start + (r + 1) * len_bytes]
        record = bytearray(original_rec) if len(original_rec) == len_bytes else bytearray(len_bytes)
        for f in fields:
            write_field(record, f, rec.get(f["name"]))
        out[r * len_bytes : (r + 1) * len_bytes] = record
    return bytes(out)


def write_tdb(parsed: dict, original_bytes: bytes) -> bytes:
    """Serialize a parsed TDB back to bytes."""
    header = parsed["fileHeader"]
    tables = parsed["tables"]

    out = bytearray(write_file_header(header))
    out += write_table_directory(tables)

    data_origin = len(out)  # 24 + tableCount*8

    # Tables are positioned at data_origin + table.offset. We trust the
    # original offsets; we don't re-pack.
    # First, figure out the maximum extent we need.
    max_extent = data_origin
    table_bytes: list[bytes] = []
    for t in tables:
        table_start = data_origin + t["offset"]
        header_bytes = write_table_header(t["header"])
        field_dir_bytes = write_field_directory(t["fields"])
        records_start_in_file = table_start + TABLE_HEADER_SIZE + len(field_dir_bytes)
        record_bytes = write_table_records(
            t["header"], t["fields"], t["records"], original_bytes, records_start_in_file
        )
        full_table = header_bytes + field_dir_bytes + record_bytes
        table_bytes.append(full_table)
        max_extent = max(max_extent, table_start + len(full_table))

    # Allocate the output buffer to the original file size; preserves any
    # padding between/after tables that we don't otherwise account for.
    out_buf = bytearray(max(len(original_bytes), max_extent))
    out_buf[: len(out)] = out
    # Copy original bytes for anything we don't overwrite (gaps between tables, file tail)
    for i in range(len(out), len(out_buf)):
        if i < len(original_bytes):
            out_buf[i] = original_bytes[i]

    for t, body in zip(tables, table_bytes):
        ts = data_origin + t["offset"]
        out_buf[ts : ts + len(body)] = body

    return bytes(out_buf)


def main() -> int:
    if len(sys.argv) < 3:
        print(f"Usage: {sys.argv[0]} <input.bin> <output.bin> [--roundtrip-only]", file=sys.stderr)
        return 2
    in_path = Path(sys.argv[1])
    out_path = Path(sys.argv[2])
    roundtrip_only = "--roundtrip-only" in sys.argv

    original = in_path.read_bytes()
    parsed = parse_tdb(original)
    written = write_tdb(parsed, original)

    if roundtrip_only:
        if written == original:
            print(f"OK: byte-exact roundtrip ({len(original)} bytes)", file=sys.stderr)
            return 0
        # Find first differing byte
        n = min(len(written), len(original))
        diff = next((i for i in range(n) if written[i] != original[i]), n)
        print(f"DIFFER at byte {diff}: orig=0x{original[diff]:02x} new=0x{written[diff]:02x}", file=sys.stderr)
        print(f"  Sizes: orig={len(original)} written={len(written)}", file=sys.stderr)
        return 1

    out_path.parent.mkdir(parents=True, exist_ok=True)
    out_path.write_bytes(written)
    print(f"Wrote {out_path} ({len(written)} bytes)", file=sys.stderr)
    if written == original:
        print("Roundtrip: byte-exact", file=sys.stderr)
    else:
        n = min(len(written), len(original))
        diff = next((i for i in range(n) if written[i] != original[i]), n)
        print(f"Roundtrip diff at byte {diff}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
