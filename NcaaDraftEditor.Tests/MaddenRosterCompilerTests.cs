using NcaaDraftEditor.Canonical;
using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class MaddenRosterCompilerTests
{
    private static string SamplePath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "madden08-roster-sample.bin");
    private static string PositionsPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "positions.json");

    [Fact]
    public void Compile_Writes_Canonical_Player_Into_First_Team_Slot()
    {
        var template = MaddenTdb.LoadFile(SamplePath);
        var canonical = new CanonicalRoster
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
                            Measurables = new Measurables { HeightIn = 74, WeightLb = 220 },
                            Ratings = new RookieMaddenRatings
                            {
                                Ovr = 85, Spd = 87, Awr = 60, Thp = 91, Tha = 75,
                            },
                        },
                    },
                },
            },
        };

        var compiler = new MaddenRosterCompiler(PositionMapper.LoadFile(PositionsPath));
        compiler.Compile(canonical, template);

        var play = template.FindTable("PLAY")!;
        var firstBearsSlot = play.Records.First(r => r.GetUInt("TGID") == 1);
        Assert.Equal("Mitchell", firstBearsSlot.GetString("PFNA"));
        Assert.Equal("Trubisky", firstBearsSlot.GetString("PLNA"));
        Assert.Equal((uint)85, firstBearsSlot.GetUInt("POVR"));
        Assert.Equal((uint)87, firstBearsSlot.GetUInt("PSPD"));
        Assert.Equal((uint)91, firstBearsSlot.GetUInt("PTHP"));
        Assert.Equal((uint)10, firstBearsSlot.GetUInt("PJEN"));
        Assert.Equal((uint)23, firstBearsSlot.GetUInt("PAGE"));
        Assert.Equal((uint)74, firstBearsSlot.GetUInt("PHGT"));
        // Weight is offset-encoded: 220 lb -> 60 stored (220 - 160)
        Assert.Equal((uint)60, firstBearsSlot.GetUInt("PWGT"));
        // Position 0 = QB
        Assert.Equal((uint)0, firstBearsSlot.GetUInt("PPOS"));
    }

    [Fact]
    public void Compile_Sorts_Canonical_Players_By_Ovr_Descending()
    {
        var template = MaddenTdb.LoadFile(SamplePath);
        var canonical = new CanonicalRoster
        {
            NflSeason = 2017,
            Teams =
            {
                new CanonicalTeam
                {
                    TgId = 1, Abbreviation = "CHI", Name = "Bears",
                    Players =
                    {
                        new CanonicalRosterPlayer
                        {
                            Name = new PlayerName { First = "Low", Last = "Rated" },
                            Position = "QB",
                            Ratings = new RookieMaddenRatings { Ovr = 50 },
                        },
                        new CanonicalRosterPlayer
                        {
                            Name = new PlayerName { First = "High", Last = "Rated" },
                            Position = "QB",
                            Ratings = new RookieMaddenRatings { Ovr = 95 },
                        },
                    },
                },
            },
        };

        var compiler = new MaddenRosterCompiler(PositionMapper.LoadFile(PositionsPath));
        compiler.Compile(canonical, template);

        var play = template.FindTable("PLAY")!;
        var bearsSlots = play.Records.Where(r => r.GetUInt("TGID") == 1).Take(2).ToList();
        // High OVR (95) should take slot 0, low OVR (50) slot 1
        Assert.Equal("High", bearsSlots[0].GetString("PFNA"));
        Assert.Equal((uint)95, bearsSlots[0].GetUInt("POVR"));
        Assert.Equal("Low", bearsSlots[1].GetString("PFNA"));
        Assert.Equal((uint)50, bearsSlots[1].GetUInt("POVR"));
    }

    [Fact]
    public void Compile_Rewrites_Team_Strings_For_Relocations()
    {
        // Sample is M08 vanilla -> Rams are in St. Louis (TGID=24, TSNA=STL).
        // Compile a canonical for an LA-Rams-era year and confirm the TEAM
        // record now says Los Angeles / LAR. This is the load-bearing fix for
        // historical-year rosters on Deluxe templates whose baseline is 2026.
        var template = MaddenTdb.LoadFile(SamplePath);
        var beforeRams = template.FindTable("TEAM")!
            .Records.First(r => r.GetUInt("TGID") == 24);
        Assert.Equal("St. Louis", beforeRams.GetString("TLNA"));
        Assert.Equal("STL", beforeRams.GetString("TSNA"));

        var canonical = new CanonicalRoster
        {
            NflSeason = 2018,
            Teams =
            {
                new CanonicalTeam
                {
                    TgId = 24, Abbreviation = "LAR", City = "Los Angeles", Name = "Rams",
                },
            },
        };

        var compiler = new MaddenRosterCompiler(PositionMapper.LoadFile(PositionsPath));
        compiler.Compile(canonical, template);

        var afterRams = template.FindTable("TEAM")!
            .Records.First(r => r.GetUInt("TGID") == 24);
        Assert.Equal("Los Angeles", afterRams.GetString("TLNA"));
        Assert.Equal("LAR", afterRams.GetString("TSNA"));
        Assert.Equal("Rams", afterRams.GetString("TDNA"));
        Assert.Equal("Rams", afterRams.GetString("TMNC"));
    }

    [Fact]
    public void Compiled_Tdb_Saves_And_Reloads_Cleanly()
    {
        var template = MaddenTdb.LoadFile(SamplePath);
        var canonical = new CanonicalRoster
        {
            NflSeason = 2017,
            Teams =
            {
                new CanonicalTeam
                {
                    TgId = 1, Abbreviation = "CHI", Name = "Bears",
                    Players =
                    {
                        new CanonicalRosterPlayer
                        {
                            Name = new PlayerName { First = "Bobby", Last = "TestQB" },
                            Position = "QB",
                            Ratings = new RookieMaddenRatings { Ovr = 75 },
                        },
                    },
                },
            },
        };

        var compiler = new MaddenRosterCompiler(PositionMapper.LoadFile(PositionsPath));
        compiler.Compile(canonical, template);

        var tempPath = Path.GetTempFileName();
        try
        {
            template.SaveFile(tempPath);
            var reloaded = MaddenTdb.LoadFile(tempPath);
            Assert.Equal((uint)4, reloaded.Header.TableCount);
            var slot = reloaded.FindTable("PLAY")!
                .Records.First(r => r.GetUInt("TGID") == 1);
            Assert.Equal("Bobby", slot.GetString("PFNA"));
            Assert.Equal("TestQB", slot.GetString("PLNA"));
        }
        finally
        {
            File.Delete(tempPath);
        }
    }
}
