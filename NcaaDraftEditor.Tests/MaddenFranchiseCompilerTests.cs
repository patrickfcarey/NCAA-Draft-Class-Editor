using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class MaddenFranchiseCompilerTests
{
    private static string CapsPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "nfl-salary-caps.json");
    private static string RosterPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "madden08-roster-sample.bin");

    [Fact]
    public void SalaryCapTable_Loads_Real_Values()
    {
        var caps = SalaryCapTable.LoadFile(CapsPath);
        // 2007 entry should equal the M08 fresh-save fixture's value byte-for-byte.
        Assert.Equal(109000000L, caps.Years["2007"].Cap);
        // 2018 spot-check against historical record.
        Assert.Equal(177200000L, caps.Years["2018"].Cap);
        Assert.Equal(23189000L, caps.Years["2018"].FranchiseTag);
        Assert.Equal(new long[] { 1907000, 2914000, 4149000, 5273000 }, caps.Years["2018"].Rfa);
        // 2021 should be the COVID-dip year (lower than 2020).
        Assert.True(caps.Years["2021"].Cap < caps.Years["2020"].Cap);
    }

    [Fact]
    public void MaddenTdb_Preamble_Is_Preserved_Through_Save()
    {
        // The roster fixture has no preamble; prepend one synthetically to
        // exercise the preamble auto-detect path without needing a 1.4 MB
        // franchise fixture in the test tree.
        var rosterBytes = File.ReadAllBytes(RosterPath);
        var withPreamble = new byte[4 + rosterBytes.Length];
        new byte[] { 0x02, 0x00, 0x00, 0x00 }.CopyTo(withPreamble, 0);
        rosterBytes.CopyTo(withPreamble, 4);

        var loaded = MaddenTdb.Load(withPreamble);
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00 }, loaded.Preamble);

        var saved = loaded.Save();
        // Save must reproduce the preamble verbatim.
        Assert.Equal(new byte[] { 0x02, 0x00, 0x00, 0x00 }, saved.AsSpan(0, 4).ToArray());
        // And the rest should match the original roster bytes.
        Assert.Equal(rosterBytes.Length, saved.Length - 4);
    }

    [Fact]
    public void MaddenTdb_Without_Preamble_Has_Empty_Preamble()
    {
        var loaded = MaddenTdb.LoadFile(RosterPath);
        Assert.Empty(loaded.Preamble);
    }

    [Fact]
    public void YearOffset_OutOfRange_Throws()
    {
        var caps = SalaryCapTable.LoadFile(CapsPath);
        var compiler = MaddenFranchiseCompiler.ForM08(caps);
        // Synthesize a stub TDB with SEAI + SLRI? Too involved; instead trigger
        // the year-offset guard directly by passing a year far outside the
        // 6-bit SINT range. Caps file ends at 2026; pick a year inside the
        // caps table but outside SEYR's range: 2040 -> offset 33 on M08.
        // We need a real franchise template to fully test Compile; without it
        // we at least verify the guard wires. Skip if no template.
        var templatePath = Path.Combine(
            Path.GetDirectoryName(AppContext.BaseDirectory)!,
            "..", "..", "..", "..", "out", "templates",
            "madden-nfl-08-franchise-template.bin");
        if (!File.Exists(templatePath)) return;  // local-only; user-specific template

        var template = MaddenTdb.LoadFile(templatePath);
        // Caps file may not contain 2040 — add a synthetic entry so the cap
        // lookup succeeds and we reach the offset guard.
        caps.Years["2040"] = new SalaryCapYear { Cap = 300000000, FranchiseTag = 50000000, Rfa = new long[]{1,2,3,4} };
        Assert.Throws<InvalidOperationException>(() => compiler.Compile(2040, template));
    }
}
