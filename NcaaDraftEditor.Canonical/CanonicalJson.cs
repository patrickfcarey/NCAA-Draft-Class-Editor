using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcaaDraftEditor.Canonical;

public static class CanonicalJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public static CanonicalDraftClass Load(string path) =>
        Deserialize(File.ReadAllText(path));

    public static void Save(CanonicalDraftClass dc, string path) =>
        File.WriteAllText(path, Serialize(dc));

    public static string Serialize(CanonicalDraftClass dc) =>
        JsonSerializer.Serialize(dc, Options);

    public static CanonicalDraftClass Deserialize(string json) =>
        JsonSerializer.Deserialize<CanonicalDraftClass>(json, Options)
        ?? throw new InvalidDataException("Failed to parse canonical draft class JSON.");
}
