using NcaaDraftEditor.Canonical;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Apply a canonical NFL roster onto a Madden 08 PS2 TDB template. Per team,
/// the first N PLAY records carrying that TGID get overwritten with canonical
/// players' data (name, position, jersey, age, height, weight, years pro, OVR,
/// 20 attribute ratings). The TDB's overall shape is preserved (table layout,
/// record count, indices, anything we don't model), so the file remains valid
/// for Madden 08 to load.
///
/// Mutates PLAY records (rosters) and TEAM string fields (city/name/abbr) so
/// historical-year output is correct even on Deluxe templates whose baseline
/// is the 2026 NFL (LAR/LAC/LV Raiders/Commanders). DCHT/INJY untouched.
/// Players beyond the template's per-team slot count are silently dropped; the
/// compiler does NOT add records. Future work: sort canonical by OVR descending
/// so the best players take the available slots when canonical exceeds capacity.
/// </summary>
public sealed class MaddenRosterCompiler
{
    private readonly PositionMapper _positions;

    public MaddenRosterCompiler(PositionMapper positions)
    {
        _positions = positions;
    }

    /// <summary>
    /// Mutates <paramref name="template"/> in place and returns it. Pass a
    /// MaddenTdb you loaded from a template fixture; after Compile the same
    /// instance is ready to .Save() with the canonical roster baked in.
    /// </summary>
    public MaddenTdb Compile(CanonicalRoster canonical, MaddenTdb template)
    {
        var play = template.FindTable("PLAY")
            ?? throw new InvalidOperationException("Template TDB missing PLAY table");
        var teamTable = template.FindTable("TEAM")
            ?? throw new InvalidOperationException("Template TDB missing TEAM table");

        foreach (var team in canonical.Teams)
        {
            ApplyTeamStrings(teamTable, team);
            // Sort canonical players by ratings.ovr descending so the best
            // take the limited slot count when we over-supply.
            var ordered = team.Players
                .OrderByDescending(p => p.Ratings?.Ovr ?? 0)
                .ToList();

            // Find all PLAY slots currently assigned to this team.
            var slots = play.Records
                .Where(r => r.GetUInt("TGID") == (uint)team.TgId)
                .ToList();

            int limit = Math.Min(slots.Count, ordered.Count);
            for (int i = 0; i < limit; i++)
            {
                ApplyPlayer(slots[i], ordered[i]);
            }
        }

        return template;
    }

    /// <summary>
    /// Authoritatively write the 4 TEAM string fields for the given canonical
    /// team. Drives historical accuracy on Deluxe templates (which ship 2026
    /// cities) and on vanilla templates that predate later relocations.
    /// Stadium name isn't in the TDB - it's keyed off SGID into an ISO-level
    /// catalog - so we can't fix that from save data.
    /// </summary>
    private static void ApplyTeamStrings(TdbTable teamTable, CanonicalTeam team)
    {
        var rec = teamTable.Records.FirstOrDefault(r => r.GetUInt("TGID") == (uint)team.TgId);
        if (rec is null) return;
        if (!string.IsNullOrEmpty(team.Name))
        {
            rec.SetString("TDNA", team.Name);
            rec.SetString("TMNC", team.Name);
        }
        if (!string.IsNullOrEmpty(team.City)) rec.SetString("TLNA", team.City);
        if (!string.IsNullOrEmpty(team.Abbreviation)) rec.SetString("TSNA", team.Abbreviation);
    }

    private void ApplyPlayer(TdbRecord slot, CanonicalRosterPlayer player)
    {
        // Identity
        slot.SetString("PFNA", player.Name.First ?? "");
        slot.SetString("PLNA", player.Name.Last ?? "");
        if (!string.IsNullOrEmpty(player.Position) && _positions.TryToPpos(player.Position, out var ppos))
            slot.SetUInt("PPOS", ppos);

        if (player.JerseyNumber is int jn) slot.SetUInt("PJEN", ClampByte(jn));
        if (player.Age is int age) slot.SetUInt("PAGE", ClampByte(age));
        if (player.YearsPro is int yp) slot.SetUInt("PYRP", ClampByte(yp));

        if (player.Measurables?.HeightIn is double h)
            slot.SetUInt("PHGT", ClampByte((int)Math.Round(h)));
        if (player.Measurables?.WeightLb is int w)
        {
            // Madden uses offset-encoded weight: stored = real - 160.
            // Verified empirically: Urlacher 258 lb shows as PWGT 94 (94 + 160 = 254 ≈ 258).
            int encoded = Math.Clamp(w - 160, 0, 255);
            slot.SetUInt("PWGT", (uint)encoded);
        }

        // Ratings
        if (player.Ratings is not null)
        {
            var r = player.Ratings;
            if (r.Ovr is int ovr) slot.SetUInt("POVR", ClampRating(ovr));
            if (r.Spd is int spd) slot.SetUInt("PSPD", ClampRating(spd));
            if (r.Acc is int acc) slot.SetUInt("PACC", ClampRating(acc));
            if (r.Agi is int agi) slot.SetUInt("PAGI", ClampRating(agi));
            if (r.Str is int str) slot.SetUInt("PSTR", ClampRating(str));
            if (r.Awr is int awr) slot.SetUInt("PAWR", ClampRating(awr));
            if (r.Cth is int cth) slot.SetUInt("PCTH", ClampRating(cth));
            if (r.Car is int car) slot.SetUInt("PCAR", ClampRating(car));
            if (r.Thp is int thp) slot.SetUInt("PTHP", ClampRating(thp));
            if (r.Tha is int tha) slot.SetUInt("PTHA", ClampRating(tha));
            if (r.Kpw is int kpw) slot.SetUInt("PKPR", ClampRating(kpw));
            if (r.Kac is int kac) slot.SetUInt("PKAC", ClampRating(kac));
            if (r.Btk is int btk) slot.SetUInt("PBTK", ClampRating(btk));
            if (r.Tak is int tak) slot.SetUInt("PTAK", ClampRating(tak));
            if (r.Pow is int pow) slot.SetUInt("PIMP", ClampRating(pow));
            if (r.Pbk is int pbk) slot.SetUInt("PPBK", ClampRating(pbk));
            if (r.Rbk is int rbk) slot.SetUInt("PRBK", ClampRating(rbk));
            if (r.Jmp is int jmp) slot.SetUInt("PJMP", ClampRating(jmp));
            if (r.Inj is int inj) slot.SetUInt("PINJ", ClampRating(inj));
            if (r.Sta is int sta) slot.SetUInt("PSTA", ClampRating(sta));
        }
    }

    private static uint ClampByte(int v) => (uint)Math.Clamp(v, 0, 255);
    private static uint ClampRating(int v) => (uint)Math.Clamp(v, 0, 99);
}
