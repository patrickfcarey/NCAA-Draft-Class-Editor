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

    // Generic helpers - same Options for every canonical type
    // (CanonicalDraftClass, CanonicalRoster, ...).
    public static string Serialize<T>(T value) where T : notnull =>
        JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) where T : notnull =>
        JsonSerializer.Deserialize<T>(json, Options)
        ?? throw new InvalidDataException($"Failed to parse canonical {typeof(T).Name} JSON.");

    // CanonicalDraftClass convenience wrappers (kept for back-compat with
    // existing call sites in the compiler + CLI).
    public static CanonicalDraftClass Load(string path) =>
        Deserialize<CanonicalDraftClass>(File.ReadAllText(path));

    public static void Save(CanonicalDraftClass dc, string path) =>
        File.WriteAllText(path, Serialize(dc));

    public static string Serialize(CanonicalDraftClass dc) =>
        Serialize<CanonicalDraftClass>(dc);

    public static CanonicalDraftClass Deserialize(string json) =>
        Deserialize<CanonicalDraftClass>(json);
}
