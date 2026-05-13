using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class MaddenTdbTests
{
    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "madden08-roster-sample.bin");

    [Fact]
    public void Load_Then_Save_Is_Byte_Exact()
    {
        var original = File.ReadAllBytes(SamplePath);
        var tdb = MaddenTdb.Load(original);
        var roundtripped = tdb.Save();

        Assert.Equal(original.Length, roundtripped.Length);
        Assert.Equal(original, roundtripped);
    }

    [Fact]
    public void Sample_Has_Four_Tables()
    {
        var tdb = MaddenTdb.LoadFile(SamplePath);
        Assert.Equal("DB", "DB");
        Assert.Equal((uint)4, tdb.Header.TableCount);
        Assert.Equal(new[] { "DCHT", "INJY", "PLAY", "TEAM" },
                     tdb.Tables.Select(t => t.Name).ToArray());
    }

    [Fact]
    public void Play_Table_Has_Real_Bears_Roster()
    {
        var tdb = MaddenTdb.LoadFile(SamplePath);
        var play = tdb.FindTable("PLAY");
        Assert.NotNull(play);
        Assert.Equal((ushort)1995, play!.Header.CurRecords);

        // Spot-check Urlacher (Bears MLB) is record index 1
        var urlacher = play.Records[1];
        Assert.Equal("Brian", urlacher.GetString("PFNA"));
        Assert.Equal("Urlacher", urlacher.GetString("PLNA"));
        Assert.Equal((uint)98, urlacher.GetUInt("POVR"));
        Assert.Equal((uint)88, urlacher.GetUInt("PSPD"));
        Assert.Equal((uint)29, urlacher.GetUInt("PAGE"));
        Assert.Equal((uint)54, urlacher.GetUInt("PJEN"));
    }

    [Fact]
    public void Team_Table_Has_Alphabetical_Teams()
    {
        var tdb = MaddenTdb.LoadFile(SamplePath);
        var team = tdb.FindTable("TEAM");
        Assert.NotNull(team);
        // TGID 1 -> Bears, TGID 2 -> Bengals, alphabetical by display name
        Assert.Equal("Bears", team!.Records[0].GetString("TDNA"));
        Assert.Equal("Chicago", team.Records[0].GetString("TLNA"));
        Assert.Equal("CHI", team.Records[0].GetString("TSNA"));
        Assert.Equal((uint)1, team.Records[0].GetUInt("TGID"));
    }

    [Fact]
    public void Edit_Field_Persists_Through_Save_And_Load()
    {
        var original = File.ReadAllBytes(SamplePath);
        var tdb = MaddenTdb.Load(original);
        var urlacher = tdb.FindTable("PLAY")!.Records[1];

        urlacher.SetUInt("PSPD", 99);

        var modified = tdb.Save();
        Assert.NotEqual(original, modified);   // bytes did change

        var tdb2 = MaddenTdb.Load(modified);
        var urlacher2 = tdb2.FindTable("PLAY")!.Records[1];
        Assert.Equal((uint)99, urlacher2.GetUInt("PSPD"));
        // Neighbors unchanged
        Assert.Equal((uint)98, urlacher2.GetUInt("POVR"));
        Assert.Equal((uint)93, urlacher2.GetUInt("PAWR"));
    }

    [Fact]
    public void ReadBits_And_WriteBits_Are_Symmetric()
    {
        // Round-trip every 7-bit value through the bit reader/writer
        for (int offset = 0; offset < 16; offset++)
        {
            for (uint v = 0; v < 128; v++)
            {
                var buf = new byte[8];
                TdbField.WriteBits(buf, offset, 7, v);
                Assert.Equal(v, TdbField.ReadBits(buf, offset, 7));
            }
        }
    }
}
