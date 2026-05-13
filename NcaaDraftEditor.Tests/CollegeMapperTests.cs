using NcaaDraftEditor.Compiler;
using Xunit;

namespace NcaaDraftEditor.Tests;

public class CollegeMapperTests
{
    private static string CollegesPath =>
        Path.Combine(AppContext.BaseDirectory, "fixtures", "colleges.json");

    [Fact]
    public void Top_Programs_Map_To_Correct_Tgid()
    {
        var m = CollegeMapper.LoadFile(CollegesPath);
        // TGID values from NcaaDraftEditor.Core/CollegeCatalog.cs
        Assert.Equal((byte)3, m.ToTgid("Alabama"));
        Assert.Equal((byte)70, m.ToTgid("Ohio State"));
        Assert.Equal((byte)71, m.ToTgid("Oklahoma"));
        Assert.Equal((byte)68, m.ToTgid("Notre Dame"));
    }

    [Fact]
    public void Aliases_Resolve_To_Catalog_Form()
    {
        var m = CollegeMapper.LoadFile(CollegesPath);
        // canonical: "Miami (FL)" -> catalog: "Miami" (TGID 49)
        Assert.Equal((byte)49, m.ToTgid("Miami (FL)"));
        // canonical: "Miami (OH)" -> catalog: "Miami University" (TGID 50)
        Assert.Equal((byte)50, m.ToTgid("Miami (OH)"));
        // canonical: "UConn" -> catalog: "Connecticut" (TGID 100)
        Assert.Equal((byte)100, m.ToTgid("UConn"));
        // canonical: "Southern Mississippi" -> catalog: "Southern Miss" (TGID 85)
        Assert.Equal((byte)85, m.ToTgid("Southern Mississippi"));
    }

    [Fact]
    public void Unmappable_Small_Schools_Are_NotApplicable()
    {
        var m = CollegeMapper.LoadFile(CollegesPath);
        // Schools not in the NCAA 06 catalog default to 255 (N/A)
        Assert.Equal(CollegeMapper.NotApplicable, m.ToTgid("Bucknell"));
        Assert.Equal(CollegeMapper.NotApplicable, m.ToTgid("North Dakota State"));
    }

    [Fact]
    public void Try_Returns_False_For_Unknown()
    {
        var m = CollegeMapper.LoadFile(CollegesPath);
        Assert.False(m.TryToTgid("Fictional University", out _));
        Assert.False(m.TryToTgid("", out _));
        Assert.False(m.TryToTgid(null, out _));
    }

    [Fact]
    public void Is_Case_Insensitive()
    {
        var m = CollegeMapper.LoadFile(CollegesPath);
        Assert.Equal(m.ToTgid("Alabama"), m.ToTgid("alabama"));
        Assert.Equal(m.ToTgid("Notre Dame"), m.ToTgid("NOTRE DAME"));
    }
}
