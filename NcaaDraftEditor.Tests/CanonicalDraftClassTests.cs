using NcaaDraftEditor.Canonical;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class CanonicalDraftClassTests
{
    private static string SamplePath => Path.Combine(AppContext.BaseDirectory, "fixtures", "draft-class-2018-sample.json");

    [Fact]
    public void Sample_2018_Loads_With_Expected_Players()
    {
        var dc = CanonicalJson.Load(SamplePath);

        Assert.Equal("ncaa-draft-class/v1", dc.Schema);
        Assert.Equal(2018, dc.Year);
        Assert.Equal(5, dc.Players.Count);

        var mayfield = dc.Players[0];
        Assert.Equal("Baker", mayfield.Name.First);
        Assert.Equal("Mayfield", mayfield.Name.Last);
        Assert.Equal("QB", mayfield.Position);
        Assert.Equal("Oklahoma", mayfield.College);
        Assert.Equal("SR", mayfield.CollegeYear);
        Assert.True(mayfield.Redshirt);
        Assert.NotNull(mayfield.Draft);
        Assert.Equal(1, mayfield.Draft!.Pick);
        Assert.Equal("CLE", mayfield.Draft.Team);
        Assert.NotNull(mayfield.RookieMaddenRatings);
        Assert.Equal(76, mayfield.RookieMaddenRatings!.Ovr);
        Assert.Equal(89, mayfield.RookieMaddenRatings.Thp);

        var barkley = dc.Players[1];
        Assert.Equal("Saquon", barkley.Name.First);
        Assert.Equal("RB", barkley.Position);
        Assert.Equal("JR", barkley.CollegeYear);
        Assert.False(barkley.Redshirt);
        Assert.Equal(4.40, barkley.Combine!.FortyYd);
    }

    [Fact]
    public void Roundtrip_Through_Json_Preserves_Players()
    {
        var dc = CanonicalJson.Load(SamplePath);
        var json = CanonicalJson.Serialize(dc);
        var dc2 = CanonicalJson.Deserialize(json);

        Assert.Equal(dc.Year, dc2.Year);
        Assert.Equal(dc.Schema, dc2.Schema);
        Assert.Equal(dc.Players.Count, dc2.Players.Count);
        for (int i = 0; i < dc.Players.Count; i++)
        {
            Assert.Equal(dc.Players[i], dc2.Players[i]);
        }
    }

    [Fact]
    public void Null_Optional_Fields_Are_Omitted_On_Write()
    {
        var minimal = new CanonicalDraftClass
        {
            Year = 2020,
            Players = { new CanonicalPlayer { Name = new() { First = "Test", Last = "Player" }, Position = "QB", College = "Alabama" } }
        };
        var json = CanonicalJson.Serialize(minimal);
        Assert.DoesNotContain("\"draft\":", json);
        Assert.DoesNotContain("\"measurables\":", json);
        Assert.DoesNotContain("\"rookieMaddenRatings\":", json);
        Assert.DoesNotContain("\"combine\":", json);
        Assert.DoesNotContain("\"birthDate\":", json);
    }
}
