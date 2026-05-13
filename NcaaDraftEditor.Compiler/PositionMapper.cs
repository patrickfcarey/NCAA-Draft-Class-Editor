using System.Text.Json;

namespace NcaaDraftEditor.Compiler;

// Loads positions.json (canonical NFL position string -> NCAA 06 PPOS byte).
// Owns no IO; callers feed JSON in. Keeps the compiler library portable.
public sealed class PositionMapper
{
    private readonly Dictionary<string, byte> _map;

    public PositionMapper(Dictionary<string, byte> map)
    {
        _map = new Dictionary<string, byte>(map, StringComparer.OrdinalIgnoreCase);
    }

    public static PositionMapper FromJson(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var dict = new Dictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var prop in doc.RootElement.GetProperty("map").EnumerateObject())
        {
            dict[prop.Name] = (byte)prop.Value.GetInt32();
        }
        return new PositionMapper(dict);
    }

    public static PositionMapper LoadFile(string path) => FromJson(File.ReadAllText(path));

    public byte ToPpos(string canonicalPosition)
    {
        if (_map.TryGetValue(canonicalPosition, out var p)) return p;
        throw new ArgumentException($"Unknown canonical position: '{canonicalPosition}'. Add it to positions.json.");
    }

    public bool TryToPpos(string canonicalPosition, out byte ppos) =>
        _map.TryGetValue(canonicalPosition, out ppos);

    public IReadOnlyDictionary<string, byte> AsDictionary => _map;
}
