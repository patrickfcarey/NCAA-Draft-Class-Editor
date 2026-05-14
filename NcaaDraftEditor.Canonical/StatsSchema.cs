using System.Text.Json;

namespace NcaaDraftEditor.Canonical;

/// <summary>
/// Real historical NFL player stats per season, sourced from nflverse's
/// stats_player_reg_{year}.parquet releases. Each player entry has a
/// `career` block (cumulative through year-1) plus per-season blocks for
/// the most recent ~5 seasons. The compiler matches PLAY records to this
/// list by gsis_id and writes into the franchise TDB's PCOF/PCDE (career)
/// and PSOF/PSDE (per-season) tables.
/// </summary>
public sealed record CanonicalStats
{
    public string Schema { get; init; } = "ncaa-madden-stats/v1";
    public int NflSeason { get; init; }
    public string Source { get; init; } = "";
    public int CareerThroughInclusive { get; init; }
    public List<int> SeasonsCovered { get; init; } = new();
    public List<CanonicalPlayerStats> Players { get; init; } = new();

    public static CanonicalStats LoadFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var result = new CanonicalStats
        {
            Schema = root.TryGetProperty("schema", out var s) ? s.GetString() ?? "" : "",
            NflSeason = root.TryGetProperty("nflSeason", out var y) ? y.GetInt32() : 0,
            Source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
            CareerThroughInclusive = root.TryGetProperty("careerThroughInclusive", out var ct) ? ct.GetInt32() : 0,
        };
        if (root.TryGetProperty("seasonsCovered", out var sc))
            foreach (var e in sc.EnumerateArray()) result.SeasonsCovered.Add(e.GetInt32());
        if (root.TryGetProperty("players", out var players))
        {
            foreach (var p in players.EnumerateArray())
            {
                var entry = new CanonicalPlayerStats
                {
                    GsisId = p.TryGetProperty("gsisId", out var g) ? g.GetString() ?? "" : "",
                    Name = p.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                };
                if (p.TryGetProperty("career", out var career))
                    entry.Career = ReadStatBlock(career);
                if (p.TryGetProperty("seasons", out var seasons))
                {
                    foreach (var prop in seasons.EnumerateObject())
                    {
                        if (int.TryParse(prop.Name, out var yr))
                            entry.Seasons[yr] = ReadStatBlock(prop.Value);
                    }
                }
                result.Players.Add(entry);
            }
        }
        return result;
    }

    private static StatBlock ReadStatBlock(JsonElement el)
    {
        var sb = new StatBlock();
        foreach (var prop in el.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.Number)
                sb.Values[prop.Name] = prop.Value.GetInt32();
        }
        return sb;
    }
}

public sealed class CanonicalPlayerStats
{
    public string GsisId { get; set; } = "";
    public string Name { get; set; } = "";
    public StatBlock Career { get; set; } = new();
    public Dictionary<int, StatBlock> Seasons { get; init; } = new();
}

/// <summary>
/// A bag of named stat values (int). Keyed by canonical field name from
/// scrapers/nflverse/build_stats.py's STAT_COLUMNS dict: passComp,
/// passAtt, passYards, passTDs, passInts, sacksTaken, rushAtt, rushYards,
/// rushTDs, receptions, targets, recYards, recTDs, fumbles, tacklesSolo,
/// tacklesAst, sacks, defInts, forcedFum, fumRecov, defTDs, passDefended,
/// fgMade, fgAtt, patMade, patAtt, kickReturns, kickRetYds, puntReturns,
/// puntRetYds.
/// </summary>
public sealed class StatBlock
{
    public Dictionary<string, int> Values { get; init; } = new();
    public int Get(string key) => Values.TryGetValue(key, out var v) ? v : 0;
}
