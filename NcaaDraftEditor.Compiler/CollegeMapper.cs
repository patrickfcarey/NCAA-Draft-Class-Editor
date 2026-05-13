using System.Text.Json;

namespace NcaaDraftEditor.Compiler;

// Loads colleges.json (canonical college name -> NCAA 06 TGID byte).
// Unmapped names raise; use TryToTgid for fallback paths in the compiler.
public sealed class CollegeMapper
{
    private readonly Dictionary<string, byte> _map;
    public const byte NotApplicable = 255;

    public CollegeMapper(Dictionary<string, byte> map)
    {
        _map = new Dictionary<string, byte>(map, StringComparer.OrdinalIgnoreCase);
    }

    public static CollegeMapper FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.GetProperty("map").EnumerateObject())
        {
            dict[prop.Name] = (byte)prop.Value.GetInt32();
        }
        return new CollegeMapper(dict);
    }

    public static CollegeMapper LoadFile(string path) => FromJson(File.ReadAllText(path));

    public byte ToTgid(string college)
    {
        if (string.IsNullOrWhiteSpace(college)) return NotApplicable;
        if (_map.TryGetValue(college.Trim(), out var t)) return t;
        throw new ArgumentException($"College '{college}' not in mapping; add it to colleges.json or fall back via TryToTgid.");
    }

    public bool TryToTgid(string? college, out byte tgid)
    {
        tgid = NotApplicable;
        if (string.IsNullOrWhiteSpace(college)) return false;
        return _map.TryGetValue(college.Trim(), out tgid);
    }

    public IReadOnlyDictionary<string, byte> AsDictionary => _map;
}
