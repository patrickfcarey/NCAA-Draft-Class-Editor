using System.Text.Json;
using System.Text.Json.Serialization;

namespace NcaaDraftEditor.Core;

// Tier 1 JSON shape: byte-exact mirror of the binary file as hex.
// Per-player parsed fields (FirstName, ratings, etc.) live in the editor UI
// and the higher-level canonical schema (Tier 2+), not here.
public static class DraftClassJson
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault,
    };

    public static string ToJson(DraftClassFile dc)
    {
        var dto = new DraftClassDto
        {
            Magic = Convert.ToHexString(DraftClassFile.MagicHeader),
            Trailer = Convert.ToHexString(dc.Trailer),
            Players = dc.Players.Select(p => new PlayerDto { Raw = Convert.ToHexString(p.Raw) }).ToList(),
        };
        return JsonSerializer.Serialize(dto, Options);
    }

    public static DraftClassFile FromJson(string json)
    {
        var dto = JsonSerializer.Deserialize<DraftClassDto>(json, Options)
            ?? throw new InvalidDataException("Draft class JSON was null or invalid.");

        if (!string.IsNullOrEmpty(dto.Magic))
        {
            var magic = Convert.FromHexString(dto.Magic);
            if (!magic.AsSpan().SequenceEqual(DraftClassFile.MagicHeader))
                throw new InvalidDataException($"Magic header mismatch: got {dto.Magic}, expected {Convert.ToHexString(DraftClassFile.MagicHeader)}.");
        }

        var dc = new DraftClassFile();
        foreach (var p in dto.Players)
        {
            var raw = Convert.FromHexString(p.Raw);
            if (raw.Length != DraftClassFile.RecordSize)
                throw new InvalidDataException($"Player record must be {DraftClassFile.RecordSize} bytes, got {raw.Length}.");
            dc.Players.Add(new PlayerRecord(raw));
        }
        if (!string.IsNullOrEmpty(dto.Trailer))
            dc.Trailer = Convert.FromHexString(dto.Trailer);
        return dc;
    }

    private sealed class DraftClassDto
    {
        public string Magic { get; set; } = "";
        public string Trailer { get; set; } = "";
        public List<PlayerDto> Players { get; set; } = new();
    }

    private sealed class PlayerDto
    {
        public string Raw { get; set; } = "";
    }
}
