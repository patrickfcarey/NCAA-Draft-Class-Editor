using NcaaDraftEditor.Core;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class DraftClassRoundtripTests
{
    private static string FixturePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "sample.bin");

    [Fact]
    public void Load_Then_Save_Is_Byte_Exact()
    {
        var original = File.ReadAllBytes(FixturePath);

        var dc = DraftClassFile.Load(FixturePath);
        var tempPath = Path.GetTempFileName();
        try
        {
            dc.Save(tempPath);
            var roundtripped = File.ReadAllBytes(tempPath);
            Assert.Equal(original.Length, roundtripped.Length);
            Assert.Equal(original, roundtripped);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Load_Then_Json_Then_Save_Is_Byte_Exact()
    {
        var original = File.ReadAllBytes(FixturePath);

        var dc = DraftClassFile.Load(FixturePath);
        var json = DraftClassJson.ToJson(dc);
        var dc2 = DraftClassJson.FromJson(json);

        var tempPath = Path.GetTempFileName();
        try
        {
            dc2.Save(tempPath);
            var roundtripped = File.ReadAllBytes(tempPath);
            Assert.Equal(original.Length, roundtripped.Length);
            Assert.Equal(original, roundtripped);
        }
        finally
        {
            File.Delete(tempPath);
        }
    }

    [Fact]
    public void Sample_Has_Expected_Shape()
    {
        var dc = DraftClassFile.Load(FixturePath);
        Assert.Equal(DraftClassFile.MaxPlayers, dc.Players.Count);
        Assert.Equal(636, dc.Trailer.Length);
        Assert.All(dc.Trailer, b => Assert.Equal(0, b));
        Assert.Equal("Vince", dc.Players[0].FirstName);
        Assert.Equal("Hall", dc.Players[0].LastName);
    }
}
