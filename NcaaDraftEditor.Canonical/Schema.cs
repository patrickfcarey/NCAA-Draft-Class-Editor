namespace NcaaDraftEditor.Canonical;

public sealed record CanonicalDraftClass
{
    public string Schema { get; init; } = "ncaa-draft-class/v1";
    public int Year { get; init; }
    public List<CanonicalPlayer> Players { get; init; } = new();
}

public sealed record CanonicalPlayer
{
    public PlayerName Name { get; init; } = new();
    public string Position { get; init; } = "";          // QB, RB, WR, TE, OT, OG, C, DE, DT, LB, CB, S, K, P
    public string College { get; init; } = "";
    public string CollegeYear { get; init; } = "SR";     // FR | SO | JR | SR | GS
    public bool Redshirt { get; init; }
    public DraftSlot? Draft { get; init; }
    public Measurables? Measurables { get; init; }
    public RookieMaddenRatings? RookieMaddenRatings { get; init; }
    public CombineResults? Combine { get; init; }
    public string? BirthDate { get; init; }              // ISO 8601 date, e.g. "1995-04-14"
}

public sealed record PlayerName
{
    public string First { get; init; } = "";
    public string Last { get; init; } = "";
}

public sealed record DraftSlot
{
    public int Round { get; init; }
    public int Pick { get; init; }
    public string Team { get; init; } = "";              // 3-letter NFL code, e.g. "CLE"
}

public sealed record Measurables
{
    public double? HeightIn { get; init; }
    public int? WeightLb { get; init; }
    public double? ArmIn { get; init; }
    public double? HandIn { get; init; }
}

// Subset of Madden's rating set that maps to NCAA 06's 21 rating bytes.
// PPOE and PTEN have no clean Madden equivalent and are defaulted at compile time.
public sealed record RookieMaddenRatings
{
    public string Source { get; init; } = "";           // e.g. "madden-19-launch"
    public int? Ovr { get; init; }
    public int? Spd { get; init; }
    public int? Acc { get; init; }
    public int? Agi { get; init; }
    public int? Str { get; init; }
    public int? Awr { get; init; }
    public int? Cth { get; init; }
    public int? Car { get; init; }
    public int? Thp { get; init; }
    public int? Tha { get; init; }
    public int? Kpw { get; init; }
    public int? Kac { get; init; }
    public int? Btk { get; init; }
    public int? Tak { get; init; }
    public int? Pow { get; init; }                       // hit power -> PIMP
    public int? Pbk { get; init; }
    public int? Rbk { get; init; }
    public int? Jmp { get; init; }
    public int? Inj { get; init; }
    public int? Sta { get; init; }
}

public sealed record CombineResults
{
    public double? FortyYd { get; init; }
    public int? Bench { get; init; }
    public double? VerticalIn { get; init; }
    public int? BroadIn { get; init; }
    public double? Shuttle { get; init; }
    public double? ThreeCone { get; init; }
}
