using NcaaDraftEditor.Canonical;
using NcaaDraftEditor.Compiler;
using NcaaDraftEditor.Core;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class DraftClassCompilerTests
{
    private static string PositionsPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "positions.json");
    private static string CollegesPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "colleges.json");
    private static string SampleCanonicalPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "draft-class-2018-sample.json");

    private static DraftClassCompiler MakeCompiler(MaddenRoster? madden = null) =>
        new(PositionMapper.LoadFile(PositionsPath),
            CollegeMapper.LoadFile(CollegesPath),
            madden);

    [Fact]
    public void Compile_Produces_1600_Records_And_Sector_Aligned_Trailer()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();

        var dc = compiler.Compile(canonical);

        Assert.Equal(DraftClassFile.MaxPlayers, dc.Players.Count);
        // 4 + 1600*86 = 137,604; aligned up to 512 -> 138,240; trailer = 636
        Assert.Equal(636, dc.Trailer.Length);
        Assert.All(dc.Trailer, b => Assert.Equal((byte)0, b));
    }

    [Fact]
    public void Compile_Sets_Names_Position_And_College_For_Each_Canonical_Player()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();

        var dc = compiler.Compile(canonical);

        // Sample fixture: Mayfield #1, Barkley #2, Chubb (Bradley) #3, Nelson #4, Edmunds (Tremaine) #5
        // (sample order is by file, not draft pick - reflects what's in the JSON)
        Assert.Equal("Baker", dc.Players[0].FirstName);
        Assert.Equal("Mayfield", dc.Players[0].LastName);
        Assert.Equal((byte)0, dc.Players[0].PPOS);   // QB
        Assert.Equal((byte)71, dc.Players[0].TGID);  // Oklahoma

        Assert.Equal("Saquon", dc.Players[1].FirstName);
        Assert.Equal((byte)1, dc.Players[1].PPOS);    // RB
        Assert.Equal((byte)76, dc.Players[1].TGID);   // Penn State

        Assert.Equal("Quenton", dc.Players[3].FirstName);
        Assert.Equal("Nelson", dc.Players[3].LastName);
        Assert.Equal((byte)6, dc.Players[3].PPOS);    // OG
        Assert.Equal((byte)68, dc.Players[3].TGID);   // Notre Dame
    }

    [Fact]
    public void Compile_Encodes_College_Year_And_Redshirt()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();

        var dc = compiler.Compile(canonical);

        // Mayfield: SR + redshirt true
        Assert.Equal((byte)3, dc.Players[0].PYER);
        Assert.Equal((byte)1, dc.Players[0].PRSD);
        // Barkley: JR + redshirt false
        Assert.Equal((byte)2, dc.Players[1].PYER);
        Assert.Equal((byte)0, dc.Players[1].PRSD);
    }

    [Fact]
    public void Compile_Sets_Height_And_Weight_From_Measurables()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();

        var dc = compiler.Compile(canonical);

        Assert.Equal((byte)73, dc.Players[0].PHGT);   // Mayfield 6'1" = 73"
        Assert.Equal((byte)215, dc.Players[0].PWGT);  // 215 lb
        Assert.Equal((byte)72, dc.Players[1].PHGT);   // Barkley 6'0"
        Assert.Equal((byte)233, dc.Players[1].PWGT);
    }

    [Fact]
    public void Compile_Without_Madden_Uses_Default_Ratings()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();  // no Madden data

        var dc = compiler.Compile(canonical);

        // All canonical players get default 50 ratings when no Madden data is provided
        Assert.Equal((byte)50, dc.Players[0].PSPD);
        Assert.Equal((byte)50, dc.Players[0].PAWR);
        Assert.Equal((byte)50, dc.Players[0].POVR);
    }

    [Fact]
    public void Compile_With_Madden_Roster_Applies_Real_Ratings()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var maddenPath = Path.Combine(AppContext.BaseDirectory, "fixtures", "madden19-2018.json");
        var madden = MaddenRoster.LoadFile(maddenPath);

        var compiler = MakeCompiler(madden);
        var dc = compiler.Compile(canonical);

        // Mayfield's Madden 19 launch ratings: OVR 81, THP 95, AWR 64
        Assert.Equal((byte)81, dc.Players[0].POVR);
        Assert.Equal((byte)95, dc.Players[0].PTHP);
        Assert.Equal((byte)64, dc.Players[0].PAWR);
    }

    [Fact]
    public void Compile_With_Filler_Replaces_Empty_Padding_Slots()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var filler = DraftClassFile.Load(
            Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.bin"));

        var compiler = new DraftClassCompiler(
            PositionMapper.LoadFile(PositionsPath),
            CollegeMapper.LoadFile(CollegesPath),
            madden: null,
            filler: filler);
        var dc = compiler.Compile(canonical);

        // First 5 slots are our compiled players (sample canonical has 5)
        Assert.Equal("Baker", dc.Players[0].FirstName);
        Assert.Equal("Saquon", dc.Players[1].FirstName);

        // Slots 5..1599 must come from the filler with their original names.
        // sample.bin's record 5 onward should be populated, not empty.
        Assert.Equal(filler.Players[5].FirstName, dc.Players[5].FirstName);
        Assert.NotEmpty(dc.Players[5].FirstName);
        Assert.Equal(filler.Players[1599].FirstName, dc.Players[1599].FirstName);
    }

    [Fact]
    public void Compiled_File_Roundtrips_Through_Save_And_Load()
    {
        var canonical = CanonicalJson.Load(SampleCanonicalPath);
        var compiler = MakeCompiler();
        var dc = compiler.Compile(canonical);

        var tempPath = Path.GetTempFileName();
        try
        {
            dc.Save(tempPath);
            var bytes = File.ReadAllBytes(tempPath);
            Assert.Equal(138_240, bytes.Length);
            Assert.Equal(DraftClassFile.MagicHeader, bytes[..4]);

            var reloaded = DraftClassFile.Load(tempPath);
            Assert.Equal(DraftClassFile.MaxPlayers, reloaded.Players.Count);
            Assert.Equal("Baker", reloaded.Players[0].FirstName);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
