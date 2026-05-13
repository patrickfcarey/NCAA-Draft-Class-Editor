using NcaaDraftEditor.Canonical;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class CanonicalRosterTests
{
    [Fact]
    public void Roster_Roundtrips_Through_Json()
    {
        var roster = new CanonicalRoster
        {
            NflSeason = 2017,
            Teams =
            {
                new CanonicalTeam
                {
                    TgId = 1, Abbreviation = "CHI", City = "Chicago", Name = "Bears",
                    Players =
                    {
                        new CanonicalRosterPlayer
                        {
                            Name = new PlayerName { First = "Mitchell", Last = "Trubisky" },
                            Position = "QB", JerseyNumber = 10, Age = 23, YearsPro = 0,
                            College = "North Carolina",
                            Measurables = new Measurables { HeightIn = 74, WeightLb = 222 },
                            Ratings = new RookieMaddenRatings
                            {
                                Source = "madden-18-launch",
                                Ovr = 75, Spd = 78, Thp = 88, Tha = 70, Awr = 60,
                            },
                        },
                    },
                },
            },
        };

        var json = CanonicalJson.Serialize(roster);
        var reloaded = CanonicalJson.Deserialize<CanonicalRoster>(json);

        Assert.Equal(2017, reloaded.NflSeason);
        Assert.Single(reloaded.Teams);
        Assert.Equal("Bears", reloaded.Teams[0].Name);
        Assert.Equal("Mitchell", reloaded.Teams[0].Players[0].Name.First);
        Assert.Equal(75, reloaded.Teams[0].Players[0].Ratings!.Ovr);
        Assert.Equal(74.0, reloaded.Teams[0].Players[0].Measurables!.HeightIn);
    }

    [Fact]
    public void Schema_String_Defaults_To_Versioned_Form()
    {
        var r = new CanonicalRoster();
        Assert.Equal("ncaa-madden-roster/v1", r.Schema);
    }
}
