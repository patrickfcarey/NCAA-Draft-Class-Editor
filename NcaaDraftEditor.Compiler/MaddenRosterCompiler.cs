using NcaaDraftEditor.Canonical;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Apply a canonical NFL roster onto a Madden PS2 TDB template (works against
/// M08 roster, M09/M12 Deluxe roster, AND franchise saves on all three games).
///
/// Per team, the first N PLAY records carrying that TGID get overwritten with
/// canonical players' data (name, position, jersey, age, height, weight, years
/// pro, OVR, 20 attribute ratings). Compile also:
///
///   1. Writes TEAM string fields (city/name/abbr/mascot) per canonical team
///      so Deluxe templates whose baseline is the 2026 NFL (LAR/LAC/LV
///      Raiders/Commanders) get rolled back to the target year correctly.
///
///   2. Assigns each canonical player a stable unique PGID in the 16384-32767
///      range so they don't collide with Madden's built-in photo library
///      (which covers low PGIDs). Combined with appearance fields (PHED/PSKI/
///      PHCL/PNEK/PEYE), this gives each overwritten slot a distinct rendered
///      head instead of inheriting the template slot's original-2007/2008
///      face. Without this, a 2018 Brady in slot X would render with whatever
///      face the 2007 player originally in slot X had.
///
///   3. Rebuilds DCHT (depth chart) entries so they reference the new PGIDs
///      in OVR-descending order per (team, position). DCHT inherits the
///      template's record structure (one entry per slot at each depth level
///      per team), but each entry's PGID is repointed to the canonical
///      player who now occupies that depth slot.
///
/// Players beyond the template's per-team slot count are silently dropped;
/// the compiler does not add records. Future polish: smarter selection
/// (preserve role distribution rather than pure OVR ranking).
/// </summary>
public sealed class MaddenRosterCompiler
{
    private readonly PositionMapper _positions;

    /// <summary>
    /// Base of the PGID range we assign to canonical players. Forced to a
    /// region above Madden's built-in photo/face library so the game falls
    /// back to a generic head rather than misattributing an existing photo.
    /// 15-bit field (0-32767); we use 16384-32767 = 16k unique IDs.
    /// </summary>
    public const uint CanonicalPgidBase = 16384;

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
        var dcht = template.FindTable("DCHT");

        // Build canonical-team -> template-TGID map by mascot name. EA's
        // templates use different TGID schemes across files:
        //   M08 roster:    1..32, Texans last (alphabetical + expansion)
        //   M08 franchise: 0..31, Titans last
        //   M09 franchise: 0..31, Titans last
        //   M12 franchise: 0..31, Titans last
        // Canonical roster JSON assigns tgId=1..32 alphabetically, which only
        // matches M08 roster. Without name-based remapping, franchise builds
        // shift every team into the next slot (Bears canonical=1 -> template
        // Bengals=1) and drop the 32nd team entirely. Result: stale players
        // from the template's launch year survive in the "left behind" slot.
        var templateTgidByMascot = new Dictionary<string, uint>(StringComparer.OrdinalIgnoreCase);
        foreach (var rec in teamTable.Records)
        {
            var mascot = rec.GetString("TDNA").Trim();
            if (!string.IsNullOrEmpty(mascot))
                templateTgidByMascot[mascot] = rec.GetUInt("TGID");
        }

        // Resolve each canonical team to its template TGID. Skip teams whose
        // mascot doesn't appear in the template (e.g. Commanders on a
        // pre-Deluxe template that still says Redskins).
        var resolvedTgid = new Dictionary<CanonicalTeam, uint>();
        foreach (var team in canonical.Teams)
        {
            if (TryResolveTgid(templateTgidByMascot, team, out var realTgid))
                resolvedTgid[team] = realTgid;
        }

        // Pass 1: assign deterministic PGIDs per canonical player. Sequential
        // within range so collisions are impossible across the league.
        var pgids = new Dictionary<CanonicalRosterPlayer, uint>(ReferenceEqualityComparer.Instance);
        uint nextPgid = CanonicalPgidBase;
        foreach (var team in canonical.Teams)
        {
            foreach (var player in team.Players)
            {
                pgids[player] = nextPgid++;
            }
        }

        // Pass 2: mutate TEAM strings + PLAY records using resolved TGIDs.
        foreach (var team in canonical.Teams)
        {
            if (!resolvedTgid.TryGetValue(team, out var realTgid)) continue;
            ApplyTeamStrings(teamTable, team, realTgid);
            var ordered = team.Players
                .OrderByDescending(p => p.Ratings?.Ovr ?? 0)
                .ToList();
            var slots = play.Records
                .Where(r => r.GetUInt("TGID") == realTgid)
                .ToList();
            int limit = Math.Min(slots.Count, ordered.Count);
            for (int i = 0; i < limit; i++)
            {
                ApplyPlayer(slots[i], ordered[i], pgids[ordered[i]]);
            }
        }

        // Pass 3: rebuild DCHT if present.
        if (dcht is not null)
        {
            RebuildDcht(canonical, dcht, pgids, resolvedTgid);
        }

        return template;
    }

    /// <summary>
    /// Resolve a canonical team to the template's TGID by mascot name.
    /// Falls back to historical aliases for renamed teams (e.g. canonical
    /// 'Redskins' against a Deluxe template that has 'Commanders').
    /// </summary>
    private static bool TryResolveTgid(
        Dictionary<string, uint> templateMascots,
        CanonicalTeam team,
        out uint tgid)
    {
        if (templateMascots.TryGetValue(team.Name, out tgid)) return true;
        foreach (var alias in MascotAliases(team.Name))
        {
            if (templateMascots.TryGetValue(alias, out tgid)) return true;
        }
        tgid = 0;
        return false;
    }

    /// <summary>Historical mascot aliases for teams that were renamed.</summary>
    private static IEnumerable<string> MascotAliases(string mascot) =>
        mascot.ToLowerInvariant() switch
        {
            "redskins"   => new[] { "Commanders", "Football Team" },
            "commanders" => new[] { "Redskins", "Football Team" },
            _ => Array.Empty<string>(),
        };

    private static void ApplyTeamStrings(TdbTable teamTable, CanonicalTeam team, uint realTgid)
    {
        var rec = teamTable.Records.FirstOrDefault(r => r.GetUInt("TGID") == realTgid);
        if (rec is null) return;
        if (!string.IsNullOrEmpty(team.Name))
        {
            rec.SetString("TDNA", team.Name);
            rec.SetString("TMNC", team.Name);
        }
        if (!string.IsNullOrEmpty(team.City)) rec.SetString("TLNA", team.City);
        if (!string.IsNullOrEmpty(team.Abbreviation)) rec.SetString("TSNA", team.Abbreviation);
    }

    private void ApplyPlayer(TdbRecord slot, CanonicalRosterPlayer player, uint pgid)
    {
        slot.SetString("PFNA", player.Name.First ?? "");
        slot.SetString("PLNA", player.Name.Last ?? "");
        if (!string.IsNullOrEmpty(player.Position) && _positions.TryToPpos(player.Position, out var ppos))
            slot.SetUInt("PPOS", ppos);

        // Identity: PGID is the league-unique player ID. Writing a new one
        // (in the non-collision range) drops the inherited photo/face from
        // the template's original 2007/2008 player at this slot.
        slot.SetUInt("PGID", pgid);

        // Appearance: hash from name so each player gets a distinct but
        // deterministic 3D head. Won't match real faces but keeps each player
        // visually distinguishable and stable across rebuilds.
        int h = StableHash(player.Name.First, player.Name.Last);
        unchecked
        {
            slot.SetUInt("PHED", (uint)(h & 0xF));           // 4-bit head shape
            slot.SetUInt("PSKI", (uint)((h >> 4) & 0x3));    // 2-bit skin tone
            slot.SetUInt("PHCL", (uint)((h >> 6) & 0x7));    // 3-bit hair color
            slot.SetUInt("PNEK", (uint)((h >> 9) & 0x3));    // 2-bit neck/tattoo
            slot.SetUInt("PEYE", (uint)((h >> 11) & 0x1));   // 1-bit eye type
        }

        if (player.JerseyNumber is int jn) slot.SetUInt("PJEN", ClampByte(jn));
        if (player.Age is int age) slot.SetUInt("PAGE", ClampByte(age));
        if (player.YearsPro is int yp) slot.SetUInt("PYRP", ClampByte(yp));

        if (player.Measurables?.HeightIn is double ht)
            slot.SetUInt("PHGT", ClampByte((int)Math.Round(ht)));
        if (player.Measurables?.WeightLb is int w)
        {
            // Madden uses offset-encoded weight: stored = real - 160.
            // Verified empirically: Urlacher 258 lb shows as PWGT 94 (94 + 160 = 254 ≈ 258).
            int encoded = Math.Clamp(w - 160, 0, 255);
            slot.SetUInt("PWGT", (uint)encoded);
        }

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

    /// <summary>
    /// Rebuild DCHT (depth chart) entries so they reference the canonical
    /// roster's new PGIDs. Walks the template's DCHT records in their original
    /// order; for each (TGID, PPOS) we encounter, the next canonical player at
    /// that team/position (in OVR descending order) gets stamped in. ddep is
    /// rewritten 0..N-1 within each (TGID, PPOS) group. Slots with no canonical
    /// match (e.g. team position the canonical doesn't fill) get PGID=0.
    /// </summary>
    private void RebuildDcht(
        CanonicalRoster canonical,
        TdbTable dcht,
        Dictionary<CanonicalRosterPlayer, uint> pgids,
        Dictionary<CanonicalTeam, uint> resolvedTgid)
    {
        // Precompute (template_TGID, PPOS) -> ordered list of PGIDs by OVR.
        var depthLists = new Dictionary<(uint tgid, uint ppos), List<uint>>();
        foreach (var team in canonical.Teams)
        {
            if (!resolvedTgid.TryGetValue(team, out var realTgid)) continue;
            var byPpos = new Dictionary<uint, List<(int ovr, uint pgid)>>();
            foreach (var player in team.Players)
            {
                if (string.IsNullOrEmpty(player.Position)) continue;
                if (!_positions.TryToPpos(player.Position, out var ppos)) continue;
                if (!pgids.TryGetValue(player, out var pgid)) continue;
                int ovr = player.Ratings?.Ovr ?? 0;
                if (!byPpos.TryGetValue(ppos, out var list))
                    byPpos[ppos] = list = new List<(int, uint)>();
                list.Add((ovr, pgid));
            }
            foreach (var (ppos, list) in byPpos)
            {
                list.Sort((a, b) => b.ovr.CompareTo(a.ovr));
                depthLists[(realTgid, ppos)] = list.Select(t => t.pgid).ToList();
            }
        }

        // Walk DCHT in document order, assigning the next-best canonical
        // player to each (TGID, PPOS) slot encountered.
        var ddepCounter = new Dictionary<(uint tgid, uint ppos), int>();
        foreach (var rec in dcht.Records)
        {
            uint tgid = rec.GetUInt("TGID");
            uint ppos = rec.GetUInt("PPOS");
            var key = (tgid, ppos);
            int idx = ddepCounter.GetValueOrDefault(key, 0);
            if (depthLists.TryGetValue(key, out var list) && idx < list.Count)
            {
                rec.SetUInt("PGID", list[idx]);
                rec.SetUInt("ddep", (uint)idx);
            }
            else
            {
                rec.SetUInt("PGID", 0);
            }
            ddepCounter[key] = idx + 1;
        }
    }

    /// <summary>
    /// Deterministic name hash, independent of .NET's String.GetHashCode
    /// (which is randomized across processes). Java-style.
    /// </summary>
    private static int StableHash(string? first, string? last)
    {
        int h = 0;
        foreach (var c in (first ?? "")) h = unchecked(h * 31 + c);
        foreach (var c in (last ?? "")) h = unchecked(h * 31 + c);
        return h;
    }

    private static uint ClampByte(int v) => (uint)Math.Clamp(v, 0, 255);
    private static uint ClampRating(int v) => (uint)Math.Clamp(v, 0, 99);
}
