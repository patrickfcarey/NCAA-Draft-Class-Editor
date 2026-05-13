using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class PositionMapperTests
{
    private static string PositionsPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "positions.json");

    [Fact]
    public void Loads_All_Canonical_Positions()
    {
        var mapper = PositionMapper.LoadFile(PositionsPath);
        // The 15 canonical position strings the schema accepts
        foreach (var pos in new[] { "QB", "RB", "FB", "WR", "TE", "OT", "OG", "C",
                                     "DE", "DT", "LB", "CB", "S", "K", "P" })
        {
            Assert.True(mapper.TryToPpos(pos, out var b), $"{pos} should be mapped");
            Assert.InRange(b, (byte)0, (byte)20);
        }
    }

    [Fact]
    public void Maps_Known_Examples_To_Expected_Ppos()
    {
        var mapper = PositionMapper.LoadFile(PositionsPath);
        // Sanity-check against PositionCatalog values in NcaaDraftEditor.Core
        Assert.Equal((byte)0, mapper.ToPpos("QB"));
        Assert.Equal((byte)1, mapper.ToPpos("RB"));
        Assert.Equal((byte)7, mapper.ToPpos("C"));
        Assert.Equal((byte)12, mapper.ToPpos("DT"));
        Assert.Equal((byte)19, mapper.ToPpos("K"));
        Assert.Equal((byte)20, mapper.ToPpos("P"));
    }

    [Fact]
    public void Is_Case_Insensitive()
    {
        var mapper = PositionMapper.LoadFile(PositionsPath);
        Assert.Equal(mapper.ToPpos("QB"), mapper.ToPpos("qb"));
        Assert.Equal(mapper.ToPpos("WR"), mapper.ToPpos("Wr"));
    }

    [Fact]
    public void Unknown_Position_Throws()
    {
        var mapper = PositionMapper.LoadFile(PositionsPath);
        Assert.Throws<ArgumentException>(() => mapper.ToPpos("FAKE"));
    }
}
