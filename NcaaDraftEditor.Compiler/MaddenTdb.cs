using System.Buffers.Binary;
using System.Text;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Read/write for EA's TDB (tabular database) format as used by Madden 08 PS2
/// roster files (BASLUS-21638DRost5). Mirrors tools/parse_madden_tdb.py and
/// tools/write_madden_tdb.py, which produce byte-exact roundtrip on the sample
/// fixture. PS2 layout: little-endian multi-byte values, ASCII strings forward,
/// numeric fields bit-packed LSB-first within each byte AND LSB-first within
/// the field. See docs/madden08-tdb-schema.md for table/field reference.
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

    public TdbHeader Header { get; private set; } = new();
    public List<TdbTable> Tables { get; } = new();

    /// <summary>
    /// Original bytes from Load; used by Save to seed each record before
    /// overwriting schema-known fields so any bits BETWEEN declared fields
    /// (the TDB has packing slack) survive intact. Null when constructed
    /// from scratch.
    /// </summary>
    private byte[]? _originalBytes;

    public static MaddenTdb Load(byte[] data)
    {
        var tdb = new MaddenTdb { _originalBytes = data };
        tdb.Header = TdbHeader.Read(data.AsSpan(0, FileHeaderSize));

        int dataOrigin = FileHeaderSize + (int)tdb.Header.TableCount * TableDefinitionSize;

        for (int i = 0; i < tdb.Header.TableCount; i++)
        {
            int defStart = FileHeaderSize + i * TableDefinitionSize;
            var name = Encoding.ASCII.GetString(data, defStart, 4);
            var offset = BinaryPrimitives.ReadUInt32LittleEndian(data.AsSpan(defStart + 4, 4));
            tdb.Tables.Add(new TdbTable { Name = name, Offset = offset });
        }

        foreach (var table in tdb.Tables)
        {
            int tableStart = dataOrigin + (int)table.Offset;
            table.Header = TdbTableHeader.Read(data.AsSpan(tableStart, TableHeaderSize));

            int fieldsStart = tableStart + TableHeaderSize;
            for (int i = 0; i < table.Header.NumFields; i++)
            {
                int fStart = fieldsStart + i * TableFieldSize;
                table.Fields.Add(TdbField.Read(data.AsSpan(fStart, TableFieldSize)));
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
            table.Header.Write(body.AsSpan(0, TableHeaderSize));
            for (int i = 0; i < table.Fields.Count; i++)
            {
                table.Fields[i].Write(body.AsSpan(TableHeaderSize + i * TableFieldSize, TableFieldSize));
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

        Header.Write(outBuf.AsSpan(0, FileHeaderSize));
        for (int i = 0; i < Tables.Count; i++)
        {
            int defStart = FileHeaderSize + i * TableDefinitionSize;
            Encoding.ASCII.GetBytes(Tables[i].Name.PadRight(4)[..4], outBuf.AsSpan(defStart, 4));
            BinaryPrimitives.WriteUInt32LittleEndian(outBuf.AsSpan(defStart + 4, 4), Tables[i].Offset);
        }
        foreach (var (start, body) in tableBlocks)
            body.CopyTo(outBuf, start);
        return outBuf;
    }

    public void SaveFile(string path) => File.WriteAllBytes(path, Save());

    public TdbTable? FindTable(string name) =>
        Tables.FirstOrDefault(t => string.Equals(t.Name, name, StringComparison.Ordinal));
}

public sealed class TdbHeader
{
    public ushort Version { get; set; }
    public uint Unknown1 { get; set; }
    public uint DbSize { get; set; }
    public uint Zero { get; set; }
    public uint TableCount { get; set; }
    public byte[] Checksum { get; set; } = new byte[4];

    public static TdbHeader Read(ReadOnlySpan<byte> span)
    {
        if (span[0] != (byte)'D' || span[1] != (byte)'B')
            throw new InvalidDataException($"Not a TDB file (magic {span[0]:X2} {span[1]:X2})");
        return new TdbHeader
        {
            Version = BinaryPrimitives.ReadUInt16LittleEndian(span[2..]),
            Unknown1 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
            DbSize = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]),
            Zero = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]),
            TableCount = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]),
            Checksum = span.Slice(20, 4).ToArray(),
        };
    }

    public void Write(Span<byte> span)
    {
        span[0] = (byte)'D';
        span[1] = (byte)'B';
        BinaryPrimitives.WriteUInt16LittleEndian(span[2..], Version);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Unknown1);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], DbSize);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], Zero);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], TableCount);
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

    public static TdbTableHeader Read(ReadOnlySpan<byte> span) => new()
    {
        PriorCrc = span.Slice(0, 4).ToArray(),
        Unknown2 = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
        LenBytes = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]),
        LenBits = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]),
        Zero = BinaryPrimitives.ReadUInt32LittleEndian(span[16..]),
        MaxRecords = BinaryPrimitives.ReadUInt16LittleEndian(span[20..]),
        CurRecords = BinaryPrimitives.ReadUInt16LittleEndian(span[22..]),
        Unknown3 = BinaryPrimitives.ReadUInt32LittleEndian(span[24..]),
        NumFields = span[28],
        IndexCount = span[29],
        Zero2 = BinaryPrimitives.ReadUInt16LittleEndian(span[30..]),
        Zero3 = BinaryPrimitives.ReadUInt32LittleEndian(span[32..]),
        HeaderCrc = span.Slice(36, 4).ToArray(),
    };

    public void Write(Span<byte> span)
    {
        PriorCrc.CopyTo(span[..4]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], Unknown2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[8..], LenBytes);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], LenBits);
        BinaryPrimitives.WriteUInt32LittleEndian(span[16..], Zero);
        BinaryPrimitives.WriteUInt16LittleEndian(span[20..], MaxRecords);
        BinaryPrimitives.WriteUInt16LittleEndian(span[22..], CurRecords);
        BinaryPrimitives.WriteUInt32LittleEndian(span[24..], Unknown3);
        span[28] = NumFields;
        span[29] = IndexCount;
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], Zero2);
        BinaryPrimitives.WriteUInt32LittleEndian(span[32..], Zero3);
        HeaderCrc.CopyTo(span[36..40]);
    }
}

public sealed class TdbField
{
    public uint Type { get; set; }
    public uint OffsetBits { get; set; }
    public string Name { get; set; } = "";
    public uint Bits { get; set; }

    public static TdbField Read(ReadOnlySpan<byte> span) => new()
    {
        Type = BinaryPrimitives.ReadUInt32LittleEndian(span[..4]),
        OffsetBits = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]),
        Name = Encoding.ASCII.GetString(span.Slice(8, 4)),
        Bits = BinaryPrimitives.ReadUInt32LittleEndian(span[12..]),
    };

    public void Write(Span<byte> span)
    {
        BinaryPrimitives.WriteUInt32LittleEndian(span[..4], Type);
        BinaryPrimitives.WriteUInt32LittleEndian(span[4..], OffsetBits);
        var nameBytes = Encoding.ASCII.GetBytes(Name.PadRight(4)[..4]);
        nameBytes.CopyTo(span[8..]);
        BinaryPrimitives.WriteUInt32LittleEndian(span[12..], Bits);
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
        return Encoding.ASCII.GetString(raw);
    }

    private void WriteString(Span<byte> record, string value)
    {
        int byteOffset = (int)(OffsetBits / 8);
        int width = (int)(Bits / 8);
        var dest = record.Slice(byteOffset, width);
        dest.Clear();
        var encoded = Encoding.ASCII.GetBytes(value);
        var copyLen = Math.Min(encoded.Length, width);
        encoded.AsSpan(0, copyLen).CopyTo(dest);
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
        return BinaryPrimitives.ReadSingleLittleEndian(record.Slice(byteOffset, 4));
    }

    private void WriteFloat(Span<byte> record, object? value)
    {
        int byteOffset = (int)(OffsetBits / 8);
        float f = value switch { float fv => fv, double dv => (float)dv, int iv => iv, _ => 0f };
        BinaryPrimitives.WriteSingleLittleEndian(record.Slice(byteOffset, 4), f);
    }

    // LSB-first within byte, LSB-first within field. See parse_madden_tdb.py for derivation.
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
