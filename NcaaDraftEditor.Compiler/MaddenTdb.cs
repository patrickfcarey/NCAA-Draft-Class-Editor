using System.Buffers.Binary;
using System.Text;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Endianness of a TDB file's multi-byte fields. PS2 Madden saves are
/// LittleEndian; PS3 Madden saves are BigEndian (and additionally store
/// 4-char table/field names byte-reversed, because EA's code stored them as
/// CPU-native u32s). Bit-packed UINT/SINT record fields are endian-neutral
/// (LSB-first within byte and within field on both platforms).
/// </summary>
public enum TdbEndian
{
    LittleEndian,
    BigEndian,
}

/// <summary>
/// Read/write for EA's TDB (tabular database) format. Originally targeted
/// Madden 08 PS2 roster files (BASLUS-21638DRost5); now also handles M12 PS3
/// (BLUS30770) and M25 PS3 (BLUS31178) roster + franchise saves via the
/// <see cref="Endian"/> flag. PS2 layout: little-endian multi-byte values,
/// ASCII strings forward, numeric fields bit-packed LSB-first within each
/// byte AND LSB-first within the field. PS3 layout: same bit-packing, but
/// multi-byte values are big-endian and 4-char names are byte-reversed.
/// </summary>
public sealed class MaddenTdb
{
    public const int FileHeaderSize = 24;
    public const int TableDefinitionSize = 8;
    public const int TableHeaderSize = 40;
    public const int TableFieldSize = 16;

    public const uint TypeString = 0;
    public const uint TypeBinary = 1;
    public const uint TypeSint = 2;
    public const uint TypeUint = 3;
    public const uint TypeFloat = 4;

    public TdbEndian Endian { get; set; } = TdbEndian.LittleEndian;
    public TdbHeader Header { get; private set; } = new();
    public List<TdbTable> Tables { get; } = new();

    /// <summary>
    /// Original bytes from Load; used by Save to seed each record before
    /// overwriting schema-known fields so any bits BETWEEN declared fields
    /// (the TDB has packing slack) survive intact. Null when constructed
    /// from scratch.
    /// </summary>
    private byte[]? _originalBytes;

    /// <summary>
    /// Bytes that appear before the TDB "DB" magic in the source file. Madden
    /// franchise saves prepend a 4-byte 02 00 00 00 wrapper (purpose unknown,
    /// likely a save-format version); roster saves and draft-class saves have
    /// no preamble. Preserved through Save() so byte-exact roundtrip works for
    /// both shapes.
    /// </summary>
    public byte[] Preamble { get; set; } = Array.Empty<byte>();

    public static MaddenTdb Load(byte[] data)
    {
        // Detect a pre-TDB preamble (franchise saves have 02 00 00 00 before
        // the DB magic). Scan the first 16 bytes for "DB"; anything before is
        // preamble. Roster/draft saves start with DB directly -> empty preamble.
        int tdbStart = 0;
        for (int i = 0; i < Math.Min(16, data.Length - 1); i++)
        {
            if (data[i] == (byte)'D' && data[i + 1] == (byte)'B')
            {
                tdbStart = i;
                break;
            }
        }
        var preamble = data.AsSpan(0, tdbStart).ToArray();
        data = tdbStart == 0 ? data : data[tdbStart..];

        // Auto-detect endianness from the tableCount field at bytes 16..20.
        // The "version" bytes at 2..4 are identical in both PS2 and PS3 saves
        // (both are 00 08), so they don't distinguish — but tableCount makes
        // it trivial: real Madden TDBs have 4..~250 tables. A reading that
        // overflows or doesn't fit the file is the wrong endian.
        uint tableCountLe = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(16, 4));
        long needed = (long)FileHeaderSize + (long)tableCountLe * TableDefinitionSize;
        TdbEndian endian = (tableCountLe > 10_000 || needed > data.Length)
            ? TdbEndian.BigEndian
            : TdbEndian.LittleEndian;

        var tdb = new MaddenTdb { _originalBytes = data, Preamble = preamble, Endian = endian };
        tdb.Header = TdbHeader.Read(data.AsSpan(0, FileHeaderSize), endian);

        int dataOrigin = FileHeaderSize + (int)tdb.Header.TableCount * TableDefinitionSize;

        for (int i = 0; i < tdb.Header.TableCount; i++)
        {
            int defStart = FileHeaderSize + i * TableDefinitionSize;
            var name = ReadName4(data.AsSpan(defStart, 4), endian);
            var offset = ReadU32(data.AsSpan(defStart + 4, 4), endian);
            tdb.Tables.Add(new TdbTable { Name = name, Offset = offset });
        }

        foreach (var table in tdb.Tables)
        {
            int tableStart = dataOrigin + (int)table.Offset;
            table.Header = TdbTableHeader.Read(data.AsSpan(tableStart, TableHeaderSize), endian);

            int fieldsStart = tableStart + TableHeaderSize;
            for (int i = 0; i < table.Header.NumFields; i++)
            {
                int fStart = fieldsStart + i * TableFieldSize;
                table.Fields.Add(TdbField.Read(data.AsSpan(fStart, TableFieldSize), endian));
            }

            int recordsStart = fieldsStart + table.Header.NumFields * TableFieldSize;
            table.RecordsStart = recordsStart;
            int lenBytes = (int)table.Header.LenBytes;

            for (int r = 0; r < table.Header.CurRecords; r++)
            {
                int recStart = recordsStart + r * lenBytes;
                var recSpan = data.AsSpan(recStart, lenBytes);
                var rec = new TdbRecord();
                foreach (var f in table.Fields)
                {
                    rec[f.Name] = f.ReadValue(recSpan);
                }
                table.Records.Add(rec);
            }
        }
        return tdb;
    }

    public static MaddenTdb LoadFile(string path) => Load(File.ReadAllBytes(path));

    public byte[] Save()
    {
        int dataOrigin = FileHeaderSize + Tables.Count * TableDefinitionSize;
        int maxExtent = dataOrigin;
        var tableBlocks = new List<(int start, byte[] body)>();

        foreach (var table in Tables)
        {
            int tableStart = dataOrigin + (int)table.Offset;
            int headerBytes = TableHeaderSize;
            int fieldsBytes = TableFieldSize * table.Header.NumFields;
            int recBytes = (int)table.Header.LenBytes * table.Header.CurRecords;
            int totalBytes = headerBytes + fieldsBytes + recBytes;

            var body = new byte[totalBytes];
            table.Header.Write(body.AsSpan(0, TableHeaderSize), Endian);
            for (int i = 0; i < table.Fields.Count; i++)
            {
                table.Fields[i].Write(body.AsSpan(TableHeaderSize + i * TableFieldSize, TableFieldSize), Endian);
            }

            int recordsStart = tableStart + TableHeaderSize + fieldsBytes;
            int lenBytes = (int)table.Header.LenBytes;
            for (int r = 0; r < table.Records.Count; r++)
            {
                Span<byte> recSpan = body.AsSpan(TableHeaderSize + fieldsBytes + r * lenBytes, lenBytes);
                // Seed from original if we have it
                if (_originalBytes is not null
                    && recordsStart + r * lenBytes + lenBytes <= _originalBytes.Length)
                {
                    _originalBytes.AsSpan(recordsStart + r * lenBytes, lenBytes).CopyTo(recSpan);
                }
                foreach (var f in table.Fields)
                {
                    if (table.Records[r].TryGetValue(f.Name, out var v))
                        f.Write(recSpan, v);
                }
            }
            tableBlocks.Add((tableStart, body));
            maxExtent = Math.Max(maxExtent, tableStart + totalBytes);
        }

        int finalSize = Math.Max(_originalBytes?.Length ?? 0, maxExtent);
        var outBuf = new byte[finalSize];
        // Seed with original bytes so untouched regions (between tables, file
        // tail, anything we don't model) survive intact.
        if (_originalBytes is not null)
            _originalBytes.AsSpan(0, Math.Min(_originalBytes.Length, finalSize)).CopyTo(outBuf);

        Header.Write(outBuf.AsSpan(0, FileHeaderSize), Endian);
        for (int i = 0; i < Tables.Count; i++)
        {
            int defStart = FileHeaderSize + i * TableDefinitionSize;
            WriteName4(outBuf.AsSpan(defStart, 4), Tables[i].Name, Endian);
            WriteU32(outBuf.AsSpan(defStart + 4, 4), Tables[i].Offset, Endian);
        }
        foreach (var (start, body) in tableBlocks)
            body.CopyTo(outBuf, start);

        // Recompute and write CRCs. EA TDB protects integrity via four kinds
        // of CRCs (all CRC-32/MPEG-2: poly 0x04C11DB7, init 0xFFFFFFFF, no
        // reflection, no xorout). PS2 stores them in little-endian; PS3
        // stores them in big-endian. Madden 08 franchise loading enforces
        // these; without them the save is rejected with "error loading
        // franchise".
        WriteCrcs(outBuf);

        if (Preamble.Length == 0) return outBuf;
        // Prepend preamble for franchise saves (and any future format that
        // wraps the TDB with leading metadata bytes).
        var withPreamble = new byte[Preamble.Length + outBuf.Length];
        Preamble.CopyTo(withPreamble, 0);
        outBuf.CopyTo(withPreamble, Preamble.Length);
        return withPreamble;
    }

    /// <summary>
    /// CRC-32/MPEG-2: poly 0x04C11DB7, init 0xFFFFFFFF, no reflection, no
    /// xorout. Compatible with bep713's crc32_be implementation.
    /// </summary>
    public static uint Crc32Mpeg2(ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        for (int i = 0; i < data.Length; i++)
        {
            crc ^= (uint)data[i] << 24;
            for (int b = 0; b < 8; b++)
            {
                if ((crc & 0x80000000u) != 0)
                    crc = (crc << 1) ^ 0x04C11DB7;
                else
                    crc <<= 1;
            }
        }
        return crc;
    }

    /// <summary>
    /// Compute and write all four kinds of CRCs into <paramref name="buf"/>
    /// (which is the TDB bytes, no preamble). Buf is mutated in place. CRC
    /// storage endian matches <see cref="Endian"/> — LE for PS2, BE for PS3.
    ///   - File header CRC: bytes [0..20), written at offset 20.
    ///   - Per-table priorCRC: CRC of previous table's data (or of the table
    ///     directory for the first table), written at bytes 0..3 of each
    ///     table header.
    ///   - Per-table headerCRC: CRC of bytes 4..36 of the table header,
    ///     written at bytes 36..39.
    ///   - EOF CRC: CRC of the last table's data block, written at
    ///     dbSize - 4.
    /// </summary>
    private void WriteCrcs(byte[] buf)
    {
        int dataOrigin = FileHeaderSize + Tables.Count * TableDefinitionSize;

        // File header CRC = CRC of first 20 bytes.
        uint fileCrc = Crc32Mpeg2(buf.AsSpan(0, 20));
        WriteU32(buf.AsSpan(20, 4), fileCrc, Endian);

        // priorCRC starts at the CRC of the table directory.
        uint priorCrc = Crc32Mpeg2(buf.AsSpan(FileHeaderSize, Tables.Count * TableDefinitionSize));

        for (int i = 0; i < Tables.Count; i++)
        {
            int tableStart = dataOrigin + (int)Tables[i].Offset;

            // Header CRC covers bytes [4..36) of this table's header.
            uint headerCrc = Crc32Mpeg2(buf.AsSpan(tableStart + 4, TableHeaderSize - 8));

            // Write priorCRC (CRC of the previous table's data, or table
            // directory for i==0) into this table's header at bytes 0..3.
            WriteU32(buf.AsSpan(tableStart, 4), priorCrc, Endian);
            // Write this table's headerCRC at bytes 36..39.
            WriteU32(buf.AsSpan(tableStart + TableHeaderSize - 4, 4), headerCrc, Endian);

            // Update priorCRC to the CRC of this table's data block.
            int dataStart = tableStart + TableHeaderSize;
            int dataEnd = i + 1 < Tables.Count
                ? dataOrigin + (int)Tables[i + 1].Offset
                : (int)Header.DbSize - 4;
            priorCrc = Crc32Mpeg2(buf.AsSpan(dataStart, dataEnd - dataStart));
        }

        // EOF CRC = CRC of the last table's data block (i.e. the same value
        // we just computed as priorCrc after the loop). Written at dbSize-4.
        if (Header.DbSize >= 4 && Header.DbSize <= buf.Length)
            WriteU32(buf.AsSpan((int)Header.DbSize - 4, 4), priorCrc, Endian);
    }

    public void SaveFile(string path) => File.WriteAllBytes(path, Save());

    public TdbTable? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));

    // ---------- Endian-aware byte helpers ----------

    internal static ushort ReadU16(ReadOnlySpan<byte> s, TdbEndian e) =>
        e == TdbEndian.LittleEndian
            ? BinaryPrimitives.ReadUInt16LittleEndian(s)
            : BinaryPrimitives.ReadUInt16BigEndian(s);

    internal static uint ReadU32(ReadOnlySpan<byte> s, TdbEndian e) =>
        e == TdbEndian.LittleEndian
            ? BinaryPrimitives.ReadUInt32LittleEndian(s)
            : BinaryPrimitives.ReadUInt32BigEndian(s);

    internal static float ReadF32(ReadOnlySpan<byte> s, TdbEndian e) =>
        e == TdbEndian.LittleEndian
            ? BinaryPrimitives.ReadSingleLittleEndian(s)
            : BinaryPrimitives.ReadSingleBigEndian(s);

    internal static void WriteU16(Span<byte> s, ushort v, TdbEndian e)
    {
        if (e == TdbEndian.LittleEndian)
            BinaryPrimitives.WriteUInt16LittleEndian(s, v);
        else
            BinaryPrimitives.WriteUInt16BigEndian(s, v);
    }

    internal static void WriteU32(Span<byte> s, uint v, TdbEndian e)
    {
        if (e == TdbEndian.LittleEndian)
            BinaryPrimitives.WriteUInt32LittleEndian(s, v);
        else
            BinaryPrimitives.WriteUInt32BigEndian(s, v);
    }

    internal static void WriteF32(Span<byte> s, float v, TdbEndian e)
    {
        if (e == TdbEndian.LittleEndian)
            BinaryPrimitives.WriteSingleLittleEndian(s, v);
        else
            BinaryPrimitives.WriteSingleBigEndian(s, v);
    }

    /// <summary>
    /// Read a 4-char ASCII name from the table directory or field directory.
    /// On PS3 (BE) these are stored byte-reversed because EA's code did
    /// effectively `*(u32*)name_field = *(u32*)"PLAY"`, which on a BE CPU
    /// produces bytes `59 41 4C 50` = "YALP" instead of "PLAY". We reverse
    /// so callers always see the logical name.
    /// </summary>
    internal static string ReadName4(ReadOnlySpan<byte> s, TdbEndian e)
    {
        Span<byte> buf = stackalloc byte[4];
        s.Slice(0, 4).CopyTo(buf);
        if (e == TdbEndian.BigEndian) buf.Reverse();
        return Encoding.ASCII.GetString(buf);
    }

    internal static void WriteName4(Span<byte> s, string name, TdbEndian e)
    {
        var padded = name.PadRight(4)[..4];
        Encoding.ASCII.GetBytes(padded, s);
        if (e == TdbEndian.BigEndian) s.Slice(0, 4).Reverse();
    }
}

public sealed class TdbHeader
{
    public ushort Version { get; set; }
    public uint Unknown1 { get; set; }
    public uint DbSize { get; set; }
    public uint Zero { get; set; }
    public uint TableCount { get; set; }
    public byte[] Checksum { get; set; } = new byte[4];

    public static TdbHeader Read(ReadOnlySpan<byte> span, TdbEndian e)
    {
        if (span[0] != (byte)'D' || span[1] != (byte)'B')
            throw new InvalidDataException($"Not a TDB file (magic {span[0]:X2} {span[1]:X2})");
        return new TdbHeader
        {
            Version = MaddenTdb.ReadU16(span[2..], e),
            Unknown1 = MaddenTdb.ReadU32(span[4..], e),
            DbSize = MaddenTdb.ReadU32(span[8..], e),
            Zero = MaddenTdb.ReadU32(span[12..], e),
            TableCount = MaddenTdb.ReadU32(span[16..], e),
            Checksum = span.Slice(20, 4).ToArray(),
        };
    }

    public void Write(Span<byte> span, TdbEndian e)
    {
        span[0] = (byte)'D';
        span[1] = (byte)'B';
        MaddenTdb.WriteU16(span[2..], Version, e);
        MaddenTdb.WriteU32(span[4..], Unknown1, e);
        MaddenTdb.WriteU32(span[8..], DbSize, e);
        MaddenTdb.WriteU32(span[12..], Zero, e);
        MaddenTdb.WriteU32(span[16..], TableCount, e);
        Checksum.CopyTo(span[20..]);
    }
}

public sealed class TdbTableHeader
{
    public byte[] PriorCrc { get; set; } = new byte[4];
    public uint Unknown2 { get; set; }
    public uint LenBytes { get; set; }
    public uint LenBits { get; set; }
    public uint Zero { get; set; }
    public ushort MaxRecords { get; set; }
    public ushort CurRecords { get; set; }
    public uint Unknown3 { get; set; }
    public byte NumFields { get; set; }
    public byte IndexCount { get; set; }
    public ushort Zero2 { get; set; }
    public uint Zero3 { get; set; }
    public byte[] HeaderCrc { get; set; } = new byte[4];

    public static TdbTableHeader Read(ReadOnlySpan<byte> span, TdbEndian e) => new()
    {
        PriorCrc = span.Slice(0, 4).ToArray(),
        Unknown2 = MaddenTdb.ReadU32(span[4..], e),
        LenBytes = MaddenTdb.ReadU32(span[8..], e),
        LenBits = MaddenTdb.ReadU32(span[12..], e),
        Zero = MaddenTdb.ReadU32(span[16..], e),
        MaxRecords = MaddenTdb.ReadU16(span[20..], e),
        CurRecords = MaddenTdb.ReadU16(span[22..], e),
        Unknown3 = MaddenTdb.ReadU32(span[24..], e),
        NumFields = span[28],
        IndexCount = span[29],
        Zero2 = MaddenTdb.ReadU16(span[30..], e),
        Zero3 = MaddenTdb.ReadU32(span[32..], e),
        HeaderCrc = span.Slice(36, 4).ToArray(),
    };

    public void Write(Span<byte> span, TdbEndian e)
    {
        PriorCrc.CopyTo(span[..4]);
        MaddenTdb.WriteU32(span[4..], Unknown2, e);
        MaddenTdb.WriteU32(span[8..], LenBytes, e);
        MaddenTdb.WriteU32(span[12..], LenBits, e);
        MaddenTdb.WriteU32(span[16..], Zero, e);
        MaddenTdb.WriteU16(span[20..], MaxRecords, e);
        MaddenTdb.WriteU16(span[22..], CurRecords, e);
        MaddenTdb.WriteU32(span[24..], Unknown3, e);
        span[28] = NumFields;
        span[29] = IndexCount;
        MaddenTdb.WriteU16(span[30..], Zero2, e);
        MaddenTdb.WriteU32(span[32..], Zero3, e);
        HeaderCrc.CopyTo(span[36..40]);
    }
}

public sealed class TdbField
{
    public uint Type { get; set; }
    public uint OffsetBits { get; set; }
    public string Name { get; set; } = "";
    public uint Bits { get; set; }

    /// <summary>
    /// Endianness inherited from the containing TDB. Used only by FLOAT
    /// fields (ReadFloat/WriteFloat); bit-packed UINT/SINT fields are
    /// endian-neutral, and STRING/BINARY fields store raw bytes.
    /// </summary>
    internal TdbEndian Endian { get; set; } = TdbEndian.LittleEndian;

    public static TdbField Read(ReadOnlySpan<byte> span, TdbEndian e) => new()
    {
        Type = MaddenTdb.ReadU32(span[..4], e),
        OffsetBits = MaddenTdb.ReadU32(span[4..], e),
        Name = MaddenTdb.ReadName4(span.Slice(8, 4), e),
        Bits = MaddenTdb.ReadU32(span[12..], e),
        Endian = e,
    };

    public void Write(Span<byte> span, TdbEndian e)
    {
        MaddenTdb.WriteU32(span[..4], Type, e);
        MaddenTdb.WriteU32(span[4..], OffsetBits, e);
        MaddenTdb.WriteName4(span[8..], Name, e);
        MaddenTdb.WriteU32(span[12..], Bits, e);
    }

    public object? ReadValue(ReadOnlySpan<byte> record) => Type switch
    {
        MaddenTdb.TypeString => ReadString(record),
        MaddenTdb.TypeBinary => ReadBinary(record),
        MaddenTdb.TypeSint or MaddenTdb.TypeUint => ReadBits(record, (int)OffsetBits, (int)Bits),
        MaddenTdb.TypeFloat => ReadFloat(record),
        _ => null,
    };

    public void Write(Span<byte> record, object? value)
    {
        switch (Type)
        {
            case MaddenTdb.TypeString:
                WriteString(record, value as string ?? "");
                break;
            case MaddenTdb.TypeBinary:
                WriteBinary(record, value);
                break;
            case MaddenTdb.TypeSint:
            case MaddenTdb.TypeUint:
                WriteBits(record, (int)OffsetBits, (int)Bits, ToUInt32(value));
                break;
            case MaddenTdb.TypeFloat:
                WriteFloat(record, value);
                break;
        }
    }

    private string ReadString(ReadOnlySpan<byte> record)
    {
        int byteOffset = (int)(OffsetBits / 8);
        int width = (int)(Bits / 8);
        var raw = record.Slice(byteOffset, width);
        int end = raw.IndexOf((byte)0);
        if (end >= 0) raw = raw[..end];
        // ISO-8859-1 (Latin-1) preserves every byte 0..255 1:1 to the same
        // Unicode codepoint, so STRING fields with high bytes (e.g. STAD.SNAM
        // stadium names with accented characters) round-trip byte-exact.
        // ASCII would corrupt anything ≥ 0x80 into the '?' replacement char.
        return Encoding.Latin1.GetString(raw);
    }

    private void WriteString(Span<byte> record, string value)
    {
        int byteOffset = (int)(OffsetBits / 8);
        int width = (int)(Bits / 8);
        var dest = record.Slice(byteOffset, width);
        // Don't pre-clear the field. Save() already seeded recSpan from the
        // original bytes; we only want to overwrite the value + one null
        // terminator, leaving whatever bytes followed the original null
        // intact. (Some TDBs carry non-zero trailing bytes after STRING
        // terminators; clearing them breaks byte-exact roundtrip.)
        var encoded = Encoding.Latin1.GetBytes(value);
        var copyLen = Math.Min(encoded.Length, width);
        encoded.AsSpan(0, copyLen).CopyTo(dest);
        if (copyLen < width) dest[copyLen] = 0;  // null terminator
    }

    private byte[] ReadBinary(ReadOnlySpan<byte> record)
    {
        int byteOffset = (int)(OffsetBits / 8);
        int width = (int)(Bits / 8);
        return record.Slice(byteOffset, width).ToArray();
    }

    private void WriteBinary(Span<byte> record, object? value)
    {
        int byteOffset = (int)(OffsetBits / 8);
        int width = (int)(Bits / 8);
        var dest = record.Slice(byteOffset, width);
        dest.Clear();
        if (value is byte[] bs)
            bs.AsSpan(0, Math.Min(bs.Length, width)).CopyTo(dest);
    }

    private object? ReadFloat(ReadOnlySpan<byte> record)
    {
        int byteOffset = (int)(OffsetBits / 8);
        return MaddenTdb.ReadF32(record.Slice(byteOffset, 4), Endian);
    }

    private void WriteFloat(Span<byte> record, object? value)
    {
        int byteOffset = (int)(OffsetBits / 8);
        float f = value switch { float fv => fv, double dv => (float)dv, int iv => iv, _ => 0f };
        MaddenTdb.WriteF32(record.Slice(byteOffset, 4), f, Endian);
    }

    // LSB-first within byte, LSB-first within field. See parse_madden_tdb.py
    // for derivation. Endian-neutral: the same algorithm works for both PS2
    // (LE) and PS3 (BE) records.
    public static uint ReadBits(ReadOnlySpan<byte> record, int offsetBits, int numBits)
    {
        uint value = 0;
        for (int i = 0; i < numBits; i++)
        {
            int bitPos = offsetBits + i;
            int byteIndex = bitPos / 8;
            int bitInByte = bitPos % 8;
            if (byteIndex < record.Length)
            {
                uint bit = (uint)((record[byteIndex] >> bitInByte) & 1);
                value |= bit << i;
            }
        }
        return value;
    }

    public static void WriteBits(Span<byte> record, int offsetBits, int numBits, uint value)
    {
        for (int i = 0; i < numBits; i++)
        {
            int bit = (int)((value >> i) & 1);
            int bitPos = offsetBits + i;
            int byteIndex = bitPos / 8;
            int bitInByte = bitPos % 8;
            if (byteIndex < record.Length)
            {
                byte mask = (byte)(1 << bitInByte);
                if (bit != 0) record[byteIndex] |= mask;
                else record[byteIndex] &= (byte)~mask;
            }
        }
    }

    private static uint ToUInt32(object? value) => value switch
    {
        null => 0,
        int i => unchecked((uint)i),
        uint u => u,
        long l => unchecked((uint)l),
        ulong ul => (uint)ul,
        string s when uint.TryParse(s, out var p) => p,
        _ => Convert.ToUInt32(value),
    };
}

public sealed class TdbTable
{
    public string Name { get; set; } = "";
    public uint Offset { get; set; }
    public TdbTableHeader Header { get; set; } = new();
    public List<TdbField> Fields { get; } = new();
    public List<TdbRecord> Records { get; } = new();
    public int RecordsStart { get; set; }

    public TdbField? FindField(string name) =>
        Fields.FirstOrDefault(f => f.Name == name);
}

public sealed class TdbRecord : Dictionary<string, object?>
{
    public uint GetUInt(string field) => TryGetValue(field, out var v) ? Convert.ToUInt32(v) : 0;
    public string GetString(string field) => TryGetValue(field, out var v) ? v as string ?? "" : "";
    public void SetUInt(string field, uint value) => this[field] = value;
    public void SetString(string field, string value) => this[field] = value;
}
