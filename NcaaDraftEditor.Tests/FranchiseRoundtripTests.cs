using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class FranchiseRoundtripTests
{
    /// <summary>
    /// The franchise template is user-specific (1.4 MB, not checked into the
    /// repo). When present, asserts MaddenTdb load/save is byte-exact across
    /// the full file, including the 4-byte preamble and all 183 tables.
    /// Skipped when the template isn't present (CI / fresh clone).
    /// </summary>
    [Fact]
    public void Franchise_Load_Save_Is_Byte_Exact_If_Template_Present()
    {
        string[] candidates = {
            @"C:\GitHub\NCAA-Draft-Class-Editor\out\templates\madden-nfl-08-franchise-template.bin",
            "/mnt/c/GitHub/NCAA-Draft-Class-Editor/out/templates/madden-nfl-08-franchise-template.bin",
        };
        var path = candidates.FirstOrDefault(File.Exists);
        if (path is null) return;  // template absent, skip silently

        var tdb = MaddenTdb.LoadFile(path);
        var saved = tdb.Save();
        var original = File.ReadAllBytes(path);
        Assert.Equal(original.Length, saved.Length);
        Assert.True(original.AsSpan().SequenceEqual(saved),
            "Franchise Load/Save should be byte-exact; STRING fields must use Latin-1 and preserve bytes after the null terminator.");
    }

    /// <summary>
    /// Regression: STRING fields used to silently corrupt bytes ≥ 0x80 by
    /// reading them as ASCII '?'. This test exercises the Latin-1 fix on
    /// a synthetic byte sequence so it runs even without the franchise
    /// template available.
    /// </summary>
    [Fact]
    public void String_Field_Roundtrips_High_Bytes()
    {
        // Build a 16-byte buffer holding "ab\xB0c\0extra-junk"
        var raw = new byte[16] { (byte)'a', (byte)'b', 0xB0, (byte)'c', 0x00,
                                 (byte)'e', (byte)'x', (byte)'t', (byte)'r',
                                 (byte)'a', 0x00, 0xFF, 0xFE, 0x55, 0xAA, 0x42 };
        var field = new TdbField { Type = MaddenTdb.TypeString, OffsetBits = 0, Bits = 16 * 8, Name = "TEST" };
        var read = field.ReadValue(raw) as string;
        Assert.NotNull(read);
        Assert.Equal(4, read!.Length);  // stops at first null
        Assert.Equal((char)0xB0, read[2]);  // high byte preserved

        // Write back into a fresh buffer seeded with the original bytes.
        var rt = (byte[])raw.Clone();
        field.Write(rt, read);
        // Bytes 0..3 unchanged; byte 4 is the null terminator (was 0);
        // bytes 5..15 (post-null trailing bytes) must NOT have been zeroed.
        Assert.Equal(raw, rt);
    }
}
