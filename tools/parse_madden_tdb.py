#!/usr/bin/env python3
"""
Empirical Madden 08 PS2 TDB parser.

Reverse-engineered from bep713's madden-db-editor (its TableReader.js
worker shows the byte layout) but adjusted for PS2's little-endian
encoding. The JS tool targets a big-endian variant (PS3/PC?); our PS2
sample bytes are LE.

File layout (verified against tests/fixtures/madden08-roster-sample.bin):

  bytes 0-1     "DB" magic (0x4244 LE)
  bytes 2-3     version (0x0008 LE -> 8)
  bytes 4-7     unknown
  bytes 8-11    DB size (LE)
  bytes 12-15   zero
  bytes 16-19   table count (LE; 4 for a roster)
  bytes 20-23   checksum
  bytes 24-55   table directory: per table, 4 ASCII name chars + 4-byte LE
                offset (relative to end of directory, i.e. file offset 56)
  bytes 56+     table data (each table has its own header + fields + records)

Table data layout (per madden-db-editor with LE swap):

  bytes 0-3     priorcrc (LE dword)
  bytes 4-7     unknown
  bytes 8-11    lenBytes  - bytes per record
  bytes 12-15   lenBits   - bits per record
  bytes 16-19   zero
  bytes 20-21   maxRecords (LE word)
  bytes 22-23   curRecords (LE word)
  bytes 24-27   unknown
  byte  28      numFields
  byte  29      indexCount
  bytes 30-31   zero2
  bytes 32-35   zero3
  bytes 36-39   headercrc
  bytes 40+     field definitions, then record data

Each field definition is 16 bytes:
  bytes 0-3     type (0=STRING, 1=BINARY, 2=SINT, 3=UINT, 4=FLOAT)
  bytes 4-7     offset (in bits, relative to record start)
  bytes 8-11    name (4 ASCII chars)
  bytes 12-15   bits (width)

Run:
    python tools/parse_madden_tdb.py tests/fixtures/madden08-roster-sample.bin
"""
from __future__ import annotations

import json
import struct
import sys
from pathlib import Path

FILE_HEADER_SIZE = 24
TABLE_DEFINITION_SIZE = 8
TABLE_HEADER_SIZE = 40
TABLE_FIELD_SIZE = 16

TYPE_STRING = 0
TYPE_BINARY = 1
TYPE_SINT = 2
TYPE_UINT = 3
TYPE_FLOAT = 4
TYPE_NAMES = {0: "STRING", 1: "BINARY", 2: "SINT", 3: "UINT", 4: "FLOAT"}


def parse_file_header(data: bytes) -> dict:
    if data[0:2] != b"DB":
        raise ValueError(f"Not a TDB file (magic {data[0:2]!r})")
    return {
        "magic": data[0:2].decode("ascii"),
        "version": int.from_bytes(data[2:4], "little"),
        "unknown1": int.from_bytes(data[4:8], "little"),
        "dbSize": int.from_bytes(data[8:12], "little"),
        "zero": int.from_bytes(data[12:16], "little"),
        "tableCount": int.from_bytes(data[16:20], "little"),
        "checksum": data[20:24].hex(),
    }


def parse_table_directory(data: bytes, count: int) -> list[dict]:
    """Each entry: 4-byte ASCII name + 4-byte LE offset (relative to end of dir)."""
    tables = []
    for i in range(count):
        base = FILE_HEADER_SIZE + i * TABLE_DEFINITION_SIZE
        name = data[base : base + 4].decode("ascii")
        offset = int.from_bytes(data[base + 4 : base + 8], "little")
        tables.append({"name": name, "offset": offset})
    return tables


def parse_table_header(data: bytes, table_start: int) -> dict:
    h = data[table_start : table_start + TABLE_HEADER_SIZE]
    return {
        "priorcrc": h[0:4].hex(),
        "unknown2": int.from_bytes(h[4:8], "little"),
        "lenBytes": int.from_bytes(h[8:12], "little"),
        "lenBits": int.from_bytes(h[12:16], "little"),
        "zero": int.from_bytes(h[16:20], "little"),
        "maxRecords": int.from_bytes(h[20:22], "little"),
        "curRecords": int.from_bytes(h[22:24], "little"),
        "unknown3": int.from_bytes(h[24:28], "little"),
        "numFields": h[28],
        "indexCount": h[29],
        "zero2": int.from_bytes(h[30:32], "little"),
        "zero3": int.from_bytes(h[32:36], "little"),
        "headercrc": h[36:40].hex(),
    }


def parse_table_fields(data: bytes, fields_start: int, num_fields: int) -> list[dict]:
    fields = []
    for i in range(num_fields):
        base = fields_start + i * TABLE_FIELD_SIZE
        ftype = int.from_bytes(data[base : base + 4], "little")
        offset_bits = int.from_bytes(data[base + 4 : base + 8], "little")
        name = data[base + 8 : base + 12].decode("ascii", errors="replace")
        bits = int.from_bytes(data[base + 12 : base + 16], "little")
        fields.append({
            "type": ftype,
            "typeName": TYPE_NAMES.get(ftype, f"?{ftype}"),
            "offsetBits": offset_bits,
            "name": name,
            "bits": bits,
        })
    return fields


def read_bits(record: bytes, offset_bits: int, num_bits: int) -> int:
    """Read num_bits starting at offset_bits within record.

    PS2 Madden 08 TDB uses LSB-first within each byte AND LSB-first
    bit collection within the field. Verified empirically against
    Brian Urlacher's 2007 stats: POVR 98, PSPD 88, PAGE 29, PHGT 76,
    PJEN 54 all read correctly under this convention. (The JS reference
    parser uses MSB-first; that's a different platform variant.)
    """
    value = 0
    for i in range(num_bits):
        bit_pos = offset_bits + i
        byte_index = bit_pos // 8
        bit_in_byte = bit_pos % 8           # LSB-first within byte
        if byte_index < len(record):
            bit = (record[byte_index] >> bit_in_byte) & 1
            value |= bit << i               # LSB-first within field
    return value


def read_string(record: bytes, offset_bits: int, num_bits: int) -> str:
    """Strings are byte-aligned ASCII inside the record."""
    byte_offset = offset_bits // 8
    num_bytes = num_bits // 8
    raw = record[byte_offset : byte_offset + num_bytes]
    end = raw.find(b"\x00")
    if end >= 0:
        raw = raw[:end]
    return raw.decode("ascii", errors="replace")


def read_field_value(record: bytes, field: dict):
    t = field["type"]
    off = field["offsetBits"]
    bits = field["bits"]
    if t == TYPE_STRING:
        return read_string(record, off, bits)
    if t == TYPE_BINARY:
        bo = off // 8
        return record[bo : bo + (bits // 8)].hex()
    if t in (TYPE_SINT, TYPE_UINT):
        return read_bits(record, off, bits)
    if t == TYPE_FLOAT:
        return f"<float {bits}b>"
    return None


def parse_table_records(data: bytes, header: dict, fields: list[dict], data_start: int) -> list[dict]:
    records = []
    for r in range(header["curRecords"]):
        rec_start = data_start + r * header["lenBytes"]
        rec_bytes = data[rec_start : rec_start + header["lenBytes"]]
        row = {f["name"]: read_field_value(rec_bytes, f) for f in fields}
        records.append(row)
    return records


def parse_tdb(file_bytes: bytes) -> dict:
    header = parse_file_header(file_bytes)
    tables = parse_table_directory(file_bytes, header["tableCount"])
    data_origin = FILE_HEADER_SIZE + header["tableCount"] * TABLE_DEFINITION_SIZE
    for t in tables:
        table_start = data_origin + t["offset"]
        t["startInFile"] = table_start
        t["header"] = parse_table_header(file_bytes, table_start)
        fields_start = table_start + TABLE_HEADER_SIZE
        t["fields"] = parse_table_fields(file_bytes, fields_start, t["header"]["numFields"])
        records_start = fields_start + t["header"]["numFields"] * TABLE_FIELD_SIZE
        t["recordsStart"] = records_start
        t["records"] = parse_table_records(file_bytes, t["header"], t["fields"], records_start)
    return {"fileHeader": header, "tables": tables}


def main() -> int:
    if len(sys.argv) < 2:
        print(f"Usage: {sys.argv[0]} <tdb_file> [out.json]", file=sys.stderr)
        return 2
    in_path = Path(sys.argv[1])
    data = in_path.read_bytes()
    parsed = parse_tdb(data)

    # Concise summary on stderr
    print(f"File: {in_path.name} ({len(data)} bytes)", file=sys.stderr)
    print(f"  Header: {parsed['fileHeader']}", file=sys.stderr)
    for t in parsed["tables"]:
        print(f"  Table {t['name']} @ {t['startInFile']}: "
              f"{t['header']['curRecords']}/{t['header']['maxRecords']} records, "
              f"{t['header']['lenBytes']}-byte ({t['header']['lenBits']}-bit) per rec, "
              f"{t['header']['numFields']} fields", file=sys.stderr)
        for f in t["fields"][:8]:
            print(f"    field {f['name']:4} type={f['typeName']:6} "
                  f"offsetBits={f['offsetBits']:>5} bits={f['bits']}", file=sys.stderr)
        if len(t["fields"]) > 8:
            print(f"    ... ({len(t['fields']) - 8} more)", file=sys.stderr)

    if len(sys.argv) >= 3:
        out_path = Path(sys.argv[2])
        out_path.parent.mkdir(parents=True, exist_ok=True)
        with out_path.open("w", encoding="utf-8") as f:
            json.dump(parsed, f, indent=2, ensure_ascii=False)
        print(f"Wrote {out_path}", file=sys.stderr)
    return 0


if __name__ == "__main__":
    sys.exit(main())
