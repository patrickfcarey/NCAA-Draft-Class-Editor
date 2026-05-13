using System.Text.Json;
using NcaaDraftEditor.Core;

namespace NcaaDraftEditor.Compiler;

// Pulls 21 NCAA 06 rating bytes plus OVR and jersey from a Madden record.
// The XLSX column names vary by Madden year (M09 uses SPEED/STRENGTH/...,
// M19+ uses Speed/Strength/... with spaces and granular throw accuracy).
// Each NCAA field has an ordered list of column aliases - first hit wins.
// Missing columns fall back to the DefaultRating value.
public static class MaddenToNcaa
{
    public const byte DefaultRating = 50;

    public static void ApplyTo(PlayerRecord rec, MaddenPlayer madden)
    {
        var raw = madden.Raw;

        rec.PSPD = ReadRating(raw, "Speed", "SPEED");
        rec.PACC = ReadRating(raw, "Acceleration", "ACCELERATION");
        rec.PAGI = ReadRating(raw, "Agility", "AGILITY");
        rec.PSTR = ReadRating(raw, "Strength", "STRENGTH");
        rec.PAWR = ReadRating(raw, "Awareness", "AWARENESS");
        rec.PCTH = ReadRating(raw, "Catch", "Catching", "CATCHING");
        rec.PCAR = ReadRating(raw, "Carrying", "CARRYING");
        rec.PTHP = ReadRating(raw, "Throw Power", "THROWPOWER");
        rec.PTHA = ReadThrowAccuracy(raw);
        rec.PKPR = ReadRating(raw, "Kick Power", "KICKPOWER");
        rec.PKAC = ReadRating(raw, "Kick Accuracy", "KICKACCURACY");
        rec.PBTK = ReadRating(raw, "Break Tackle", "BREAKTACKLE", "Elusiveness", "ELUSIVENESS");
        rec.PTAK = ReadRating(raw, "Tackle", "TACKLE");
        rec.PIMP = ReadRating(raw, "Hit Power", "HITPOWER", "POW");
        rec.PPBK = ReadRating(raw, "Pass Block", "PASSBLOCK");
        rec.PRBK = ReadRating(raw, "Run Block", "RUNBLOCK");
        rec.PPOE = ReadRating(raw, "Impact Blocking", "IMPACTBLOCKING", "Hit Power", "HITPOWER");
        rec.PTEN = ReadRating(raw, "Toughness", "TOUGHNESS");
        rec.PJMP = ReadRating(raw, "Jumping", "JUMPING");
        rec.PINJ = ReadRating(raw, "Injury", "INJURY");
        rec.PSTA = ReadRating(raw, "Stamina", "STAMINA");

        var ovr = ReadInt(raw, "Overall", "OVERALL");
        rec.POVR = (byte)Math.Clamp(ovr ?? DefaultRating, 0, 99);

        if (madden.JerseyNumber is int jn)
            rec.PJEN = (byte)Math.Clamp(jn, 0, 99);
    }

    /// <summary>
    /// Aggregates Madden's three throw accuracy ratings (short/medium/deep) into
    /// NCAA's single PTHA. Older Maddens (M09-M18) have a single Throw Accuracy
    /// column - that's used directly when present.
    /// </summary>
    private static byte ReadThrowAccuracy(IReadOnlyDictionary<string, JsonElement> raw)
    {
        var single = ReadInt(raw, "Throw Accuracy", "THROWACCURACY");
        if (single is int s) return (byte)Math.Clamp(s, 0, 99);

        var sShort = ReadInt(raw, "Throw Accuracy Short", "SHORTTHROWACCURACY", "Short Throw Accuracy");
        var sMid   = ReadInt(raw, "Throw Accuracy Mid",   "MEDIUMTHROWACCURACY", "Medium Throw Accuracy");
        var sDeep  = ReadInt(raw, "Throw Accuracy Deep",  "DEEPTHROWACCURACY",  "Deep Throw Accuracy");
        var parts = new int?[] { sShort, sMid, sDeep }.Where(x => x.HasValue).Select(x => x!.Value).ToList();
        return parts.Count > 0
            ? (byte)Math.Clamp((int)Math.Round(parts.Average()), 0, 99)
            : DefaultRating;
    }

    private static byte ReadRating(IReadOnlyDictionary<string, JsonElement> raw, params string[] aliases)
    {
        var v = ReadInt(raw, aliases);
        return v is int i ? (byte)Math.Clamp(i, 0, 99) : DefaultRating;
    }

    private static int? ReadInt(IReadOnlyDictionary<string, JsonElement> raw, params string[] aliases)
    {
        foreach (var key in aliases)
        {
            if (!raw.TryGetValue(key, out var e)) continue;
            switch (e.ValueKind)
            {
                case JsonValueKind.Number:
                    if (e.TryGetInt32(out var i)) return i;
                    if (e.TryGetDouble(out var d)) return (int)Math.Round(d);
                    break;
                case JsonValueKind.String:
                    if (int.TryParse(e.GetString(), out var si)) return si;
                    break;
            }
        }
        return null;
    }
}
