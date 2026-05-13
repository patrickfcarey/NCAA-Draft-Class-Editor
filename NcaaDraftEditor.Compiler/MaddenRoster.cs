using System.Text.Json;

namespace NcaaDraftEditor.Compiler;

// Loads data/raw/madden-ratings/maddenNN-YYYY.json into a flat per-player lookup
// keyed by (lowercased) name. Each year's XLSX has different column names; we
// normalize at lookup time via column-name aliasing.
public sealed class MaddenRoster
{
    public int? NflSeason { get; init; }
    public string MaddenVersion { get; init; } = "";
    public string Source { get; init; } = "";
    public IReadOnlyList<MaddenPlayer> Players { get; init; } = Array.Empty<MaddenPlayer>();

    // Map first+last name -> all matching Madden records. Names collide (multiple
    // "Mike Williams" in the NFL); callers disambiguate via position/team/jersey.
    private readonly Dictionary<string, List<MaddenPlayer>> _byName;

    public MaddenRoster(int? nflSeason, string maddenVersion, string source, IReadOnlyList<MaddenPlayer> players)
    {
        NflSeason = nflSeason;
        MaddenVersion = maddenVersion;
        Source = source;
        Players = players;
        _byName = new(StringComparer.OrdinalIgnoreCase);
        foreach (var p in players)
        {
            var key = p.NormalizedName();
            if (string.IsNullOrEmpty(key)) continue;
            if (!_byName.TryGetValue(key, out var list))
                _byName[key] = list = new List<MaddenPlayer>();
            list.Add(p);
        }
    }

    public IReadOnlyList<MaddenPlayer> FindByName(string firstName, string lastName)
    {
        var key = MaddenPlayer.MakeKey(firstName, lastName);
        return _byName.TryGetValue(key, out var list)
            ? list
            : Array.Empty<MaddenPlayer>();
    }

    public bool TryFind(string firstName, string lastName, string? position, out MaddenPlayer player)
    {
        var candidates = FindByName(firstName, lastName);
        if (candidates.Count == 0) { player = null!; return false; }
        if (candidates.Count == 1 || string.IsNullOrEmpty(position))
        {
            player = candidates[0];
            return true;
        }
        // Disambiguate by position - use the candidate whose position matches.
        foreach (var c in candidates)
        {
            if (string.Equals(c.Position, position, StringComparison.OrdinalIgnoreCase))
            {
                player = c;
                return true;
            }
        }
        // Position didn't match any; fall back to the first record.
        player = candidates[0];
        return true;
    }

    public static MaddenRoster LoadFile(string path) => FromJson(File.ReadAllText(path));

    public static MaddenRoster FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var season = root.TryGetProperty("nflSeason", out var s) && s.ValueKind == JsonValueKind.Number
            ? s.GetInt32() : (int?)null;
        var ver = root.TryGetProperty("maddenVersion", out var v) ? v.GetString() ?? "" : "";
        var src = root.TryGetProperty("source", out var sr) ? sr.GetString() ?? "" : "";

        var players = new List<MaddenPlayer>();
        if (root.TryGetProperty("teams", out var teams))
        {
            foreach (var team in teams.EnumerateObject())
            foreach (var rec in team.Value.EnumerateArray())
                players.Add(MaddenPlayer.FromJson(rec, team.Name));
        }
        return new MaddenRoster(season, ver, src, players);
    }
}

public sealed class MaddenPlayer
{
    public string TeamSlug { get; init; } = "";
    public string FirstName { get; init; } = "";
    public string LastName { get; init; } = "";
    public string Position { get; init; } = "";
    public int? JerseyNumber { get; init; }
    public int? YearsPro { get; init; }
    public string College { get; init; } = "";

    // The raw key-value pairs (so MaddenToNcaa can pull whatever fields it needs)
    public IReadOnlyDictionary<string, JsonElement> Raw { get; init; } = new Dictionary<string, JsonElement>();

    public string NormalizedName() => MakeKey(FirstName, LastName);

    public static string MakeKey(string first, string last)
    {
        first = (first ?? "").Trim().ToLowerInvariant();
        last = (last ?? "").Trim().ToLowerInvariant();
        // Strip suffix like "jr.", "iii"
        var lastTokens = last.Split(' ', 2);
        if (lastTokens.Length == 2 && (lastTokens[1] == "jr." || lastTokens[1] == "sr."
                                       || lastTokens[1] == "ii" || lastTokens[1] == "iii"
                                       || lastTokens[1] == "iv" || lastTokens[1] == "v"))
            last = lastTokens[0];
        return $"{first}|{last}";
    }

    public static MaddenPlayer FromJson(JsonElement rec, string teamSlug)
    {
        // Clone each JsonElement so it survives disposal of the parent JsonDocument.
        var raw = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in rec.EnumerateObject())
            raw[prop.Name] = prop.Value.Clone();

        // The schema varies by year. Try the common variants for each field.
        string firstName, lastName;
        if (raw.TryGetValue("FIRSTNAME", out var fn) || raw.TryGetValue("First Name", out fn))
        {
            firstName = fn.GetString() ?? "";
            lastName = raw.TryGetValue("LASTNAME", out var ln) ? ln.GetString() ?? ""
                     : raw.TryGetValue("Last Name", out ln) ? ln.GetString() ?? "" : "";
        }
        else if (raw.TryGetValue("Name", out var nm))
        {
            var name = nm.GetString() ?? "";
            var parts = name.Split(' ', 2);
            firstName = parts.Length > 0 ? parts[0] : "";
            lastName = parts.Length > 1 ? parts[1] : "";
        }
        else
        {
            firstName = "";
            lastName = "";
        }

        var pos = raw.TryGetValue("Position", out var p) ? p.GetString() ?? ""
                : raw.TryGetValue("POSITION", out p) ? p.GetString() ?? "" : "";

        int? jersey = null;
        if (raw.TryGetValue("JERSEYNUM", out var j) || raw.TryGetValue("Jersey #", out j)
            || raw.TryGetValue("Jersey", out j))
        {
            if (j.ValueKind == JsonValueKind.Number) jersey = j.GetInt32();
            else if (j.ValueKind == JsonValueKind.String && int.TryParse(j.GetString(), out var ji)) jersey = ji;
        }

        int? yearsPro = null;
        if (raw.TryGetValue("Years Pro", out var yp) || raw.TryGetValue("YEARSPRO", out yp))
        {
            if (yp.ValueKind == JsonValueKind.Number) yearsPro = yp.GetInt32();
            else if (yp.ValueKind == JsonValueKind.String && int.TryParse(yp.GetString(), out var ypi)) yearsPro = ypi;
        }

        var college = raw.TryGetValue("College", out var c) ? c.GetString() ?? "" : "";

        return new MaddenPlayer
        {
            TeamSlug = teamSlug,
            FirstName = firstName,
            LastName = lastName,
            Position = pos,
            JerseyNumber = jersey,
            YearsPro = yearsPro,
            College = college,
            Raw = raw,
        };
    }
}
