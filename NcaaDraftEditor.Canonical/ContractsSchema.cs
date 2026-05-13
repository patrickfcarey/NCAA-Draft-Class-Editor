using System.Text.Json;

namespace NcaaDraftEditor.Canonical;

/// <summary>
/// Real historical NFL contract data per season, sourced from nflverse's
/// daily-refreshed mirror of OverTheCap.com. Each <see cref="CanonicalContract"/>
/// is a (player, year) pair carrying the current-year cap economy for one
/// player. The compiler matches PLAY records to this list by name and writes
/// the real PSA/PSB/PCSA values when a match is found, falling back to
/// ContractSynthesizer otherwise.
/// </summary>
public sealed record CanonicalContracts
{
    public string Schema { get; init; } = "ncaa-madden-contracts/v1";
    public int NflSeason { get; init; }
    public string Source { get; init; } = "";
    public List<CanonicalContract> Players { get; init; } = new();

    public static CanonicalContracts LoadFile(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var result = new CanonicalContracts
        {
            Schema = root.TryGetProperty("schema", out var s) ? s.GetString() ?? "" : "",
            NflSeason = root.TryGetProperty("nflSeason", out var y) ? y.GetInt32() : 0,
            Source = root.TryGetProperty("source", out var src) ? src.GetString() ?? "" : "",
        };
        if (root.TryGetProperty("players", out var players))
        {
            foreach (var p in players.EnumerateArray())
            {
                result.Players.Add(new CanonicalContract
                {
                    Name = GetStr(p, "name"),
                    Team = GetStr(p, "team"),
                    Position = GetStr(p, "position"),
                    OtcId = p.TryGetProperty("otcId", out var oid) && oid.ValueKind != JsonValueKind.Null ? oid.GetInt32() : 0,
                    GsisId = GetStr(p, "gsisId"),
                    CapHitMillions = GetDouble(p, "capHitMillions"),
                    BaseSalaryMillions = GetDouble(p, "baseSalaryMillions"),
                    ProratedBonusMillions = GetDouble(p, "proratedBonusMillions"),
                    RosterBonusMillions = GetDouble(p, "rosterBonusMillions"),
                    CashPaidMillions = GetDouble(p, "cashPaidMillions"),
                    YearsRemaining = p.TryGetProperty("yearsRemaining", out var yr) ? yr.GetInt32() : 0,
                    TotalContractYears = p.TryGetProperty("totalContractYears", out var ty) ? ty.GetInt32() : 0,
                    YearSigned = p.TryGetProperty("yearSigned", out var ys) ? ys.GetInt32() : 0,
                });
            }
        }
        return result;
    }

    private static string GetStr(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";

    private static double GetDouble(JsonElement e, string name) =>
        e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
}

public sealed record CanonicalContract
{
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string Position { get; init; } = "";
    public int OtcId { get; init; }
    public string GsisId { get; init; } = "";
    public double CapHitMillions { get; init; }
    public double BaseSalaryMillions { get; init; }
    public double ProratedBonusMillions { get; init; }
    public double RosterBonusMillions { get; init; }
    public double CashPaidMillions { get; init; }
    public int YearsRemaining { get; init; }
    public int TotalContractYears { get; init; }
    public int YearSigned { get; init; }
}
