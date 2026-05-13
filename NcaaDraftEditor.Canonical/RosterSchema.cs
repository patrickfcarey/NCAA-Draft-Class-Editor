namespace NcaaDraftEditor.Canonical;

// Canonical real-world NFL opening-day roster for a single season. The Madden
// 08 PS2 roster compiler consumes this shape; the same compiler can also be
// used to inject historical rosters into a starter franchise.
//
// Shape choice: grouped by team (mirrors how rosters are reasoned about IRL,
// and matches the Madden TDB's TEAM table + per-team PLAY records cluster).
// The flat per-player view can be derived by flattening Teams[*].Players.
public sealed record CanonicalRoster
{
    public string Schema { get; init; } = "ncaa-madden-roster/v1";
    public int NflSeason { get; init; }
    public List<CanonicalTeam> Teams { get; init; } = new();
}

public sealed record CanonicalTeam
{
    /// <summary>Team ID in the TDB TEAM table (Bears=1, Bengals=2, alphabetical by name).</summary>
    public int TgId { get; init; }
    /// <summary>NFL abbreviation, e.g. "CHI".</summary>
    public string Abbreviation { get; init; } = "";
    /// <summary>City/location, e.g. "Chicago".</summary>
    public string City { get; init; } = "";
    /// <summary>Mascot/name, e.g. "Bears".</summary>
    public string Name { get; init; } = "";
    public List<CanonicalRosterPlayer> Players { get; init; } = new();
}

public sealed record CanonicalRosterPlayer
{
    public PlayerName Name { get; init; } = new();
    /// <summary>NFL position label (QB, RB, WR, TE, OT, OG, C, DE, DT, LB, CB, S, K, P).</summary>
    public string Position { get; init; } = "";
    public int? JerseyNumber { get; init; }
    public int? Age { get; init; }
    /// <summary>NFL years of experience. 0 = rookie.</summary>
    public int? YearsPro { get; init; }
    public string College { get; init; } = "";
    public Measurables? Measurables { get; init; }
    /// <summary>
    /// Madden-style ratings for this player at the start of NflSeason.
    /// Reuses RookieMaddenRatings (defined in Schema.cs) - same 20-attribute
    /// shape works for both rookies and veterans.
    /// </summary>
    public RookieMaddenRatings? Ratings { get; init; }
}
