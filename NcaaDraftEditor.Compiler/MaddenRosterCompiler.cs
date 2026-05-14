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

    /// <summary>
    /// PositionMapper produces a single PPOS code per canonical position
    /// (OT→LT=5, OG→LG=6, DE→LE=10, LB→MLB=14, S→FS=17). But Madden's depth
    /// chart maintains L/R variants as separate PPOS values. Without this
    /// map, DCHT entries at the R-side codes (RT=9, RG=8, RE=11, LOLB=13,
    /// ROLB=15, SS=18) would never find a canonical player and end up with
    /// PGID=0, leaving half the OL/DL/LB/S depth chart blank in-game.
    /// We distribute canonical players across the variant codes round-robin
    /// so the depth chart has plausible players on both sides.
    /// </summary>
    private static readonly Dictionary<uint, uint[]> PositionVariants = new()
    {
        [5]  = new uint[] { 5, 9 },        // OT  -> LT, RT
        [6]  = new uint[] { 6, 8 },        // OG  -> LG, RG
        [10] = new uint[] { 10, 11 },      // DE  -> LE, RE
        [14] = new uint[] { 13, 14, 15 },  // LB  -> LOLB, MLB, ROLB
        [17] = new uint[] { 17, 18 },      // S   -> FS, SS
    };

    public MaddenRosterCompiler(PositionMapper positions)
    {
        _positions = positions;
    }

    /// <summary>
    /// Mutates <paramref name="template"/> in place and returns it. Pass a
    /// MaddenTdb you loaded from a template fixture; after Compile the same
    /// instance is ready to .Save() with the canonical roster baked in.
    /// </summary>
    public MaddenTdb Compile(
        CanonicalRoster canonical,
        MaddenTdb template,
        CanonicalStats? stats = null,
        int baseYear = 2007)
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

        // Pass 4: clear every other table that references PGIDs by
        // template-era values. After our 16384+ PGID shift, every one of
        // these holds stale references to template-era players who no longer
        // exist - dangling pointers that could show wrong player names in
        // stats screens or crash the league menu when Madden looks up the
        // missing PGID. None of these tables make sense for a fresh-Week-1
        // franchise anyway (no games played, no career stats accumulated),
        // so clearing is the right state.
        //
        // Categories:
        //   INJY                                — injuries (cleared above)
        //   PSDE/PSKI/PSKP/PSNG/PSOF/PSOL       — per-season stats history
        //   PCDE/PCKI/PCKP/PCNG/PCOF            — per-career cumulative stats
        //   FBPL                                — Fan Box / playoff history
        //   PMCV/pPYR/TCIN                      — small singleton/sentinel tables
        //                                          that reference PGIDs
        //   AWPL/AYPL/BDEF/BKIC/BKPR/BOFF       — empty in fresh templates but
        //   BQTR/BSCS/BTES/STTI/etc.              listed for completeness; cleared
        //                                          defensively so nothing future
        //                                          can introduce orphans.
        foreach (var name in PgidReferencingTablesToClear)
        {
            var t = template.FindTable(name);
            if (t is null) continue;
            t.Records.Clear();
            t.Header.CurRecords = 0;
        }

        // Pass 5: populate career stats tables (PCOF/PCDE) from real nflverse
        // data. Per-season tables (PSOF/PSDE) stay empty for now - they need
        // base-year context for the SEYR offset, which is a franchise-level
        // concept the roster compiler doesn't have. Career totals cover the
        // most-visible 'Player Profile -> Career Stats' UI; per-season game-
        // log views stay blank.
        if (stats is not null)
        {
            WriteCareerStats(template, canonical, pgids, stats);
            WriteSeasonStats(template, canonical, pgids, stats, baseYear);
        }

        return template;
    }

    /// <summary>
    /// Walk the canonical roster, join each player to nflverse career stats
    /// by gsis_id, and write PCOF (offense) + PCDE (defense) records.
    /// Skips players without a gsis_id (very early historical or scraper
    /// misses) and players whose stats fall below significance thresholds
    /// in both categories (no point in writing a row of zeros).
    /// </summary>
    private void WriteCareerStats(
        MaddenTdb template,
        CanonicalRoster canonical,
        Dictionary<CanonicalRosterPlayer, uint> pgids,
        CanonicalStats stats)
    {
        var byGsis = new Dictionary<string, CanonicalPlayerStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stats.Players)
        {
            if (!string.IsNullOrEmpty(s.GsisId)) byGsis[s.GsisId] = s;
        }

        var pcof = template.FindTable("PCOF");
        var pcde = template.FindTable("PCDE");
        var pcki = template.FindTable("PCKI");
        var pckp = template.FindTable("PCKP");
        var pcng = template.FindTable("PCNG");

        foreach (var team in canonical.Teams)
        {
            foreach (var player in team.Players)
            {
                if (string.IsNullOrEmpty(player.GsisId)) continue;
                if (!byGsis.TryGetValue(player.GsisId, out var ps)) continue;
                if (!pgids.TryGetValue(player, out var pgid)) continue;

                var c = ps.Career;
                if (pcof is not null && HasOffensiveStats(c) && pcof.Records.Count < pcof.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pcof);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("caya", ClampUInt(c.Get("passYards"),     18));
                    rec.SetUInt("catd", ClampUInt(c.Get("passTDs"),       11));
                    rec.SetUInt("cacm", ClampUInt(c.Get("passComp"),      14));
                    rec.SetUInt("caat", ClampUInt(c.Get("passAtt"),       14));
                    rec.SetUInt("cain", ClampUInt(c.Get("passInts"),      11));
                    rec.SetUInt("cufu", ClampUInt(c.Get("fumbles"),        9));
                    rec.SetUInt("cuya", ClampUInt(c.Get("rushYards"),     17));
                    rec.SetUInt("cutd", ClampUInt(c.Get("rushTDs"),       10));
                    rec.SetUInt("cuat", ClampUInt(c.Get("rushAtt"),       14));
                    rec.SetUInt("ccca", ClampUInt(c.Get("receptions"),    12));
                    rec.SetUInt("ccya", ClampUInt(Math.Max(0, c.Get("recYards")), 17));
                    rec.SetUInt("cctd", ClampUInt(c.Get("recTDs"),        10));
                    pcof.Records.Add(rec);
                }

                if (pcde is not null && HasDefensiveStats(c) && pcde.Records.Count < pcde.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pcde);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("csca", ClampUInt(c.Get("tacklesSolo"),   12));
                    rec.SetUInt("cdta", ClampUInt(c.Get("tacklesAst"),    12));
                    rec.SetUInt("clff", ClampUInt(c.Get("forcedFum"),      9));
                    rec.SetUInt("cdbh", ClampUInt(c.Get("passDefended"),  12));
                    rec.SetUInt("clsk", ClampUInt(c.Get("sacks"),         10));
                    rec.SetUInt("csin", ClampUInt(c.Get("defInts"),        9));
                    rec.SetUInt("clfr", ClampUInt(c.Get("fumRecov"),       9));
                    pcde.Records.Add(rec);
                }

                // PCKI carries BOTH kicker and punter career stats per row.
                // Kicker fields: ckfa/ckfm = FG att/made, ckea/ckem = XP att/made.
                // Punter fields: cpat = punts, cpya = punt yards (gross).
                // Decoded against Stover/Vinatieri (kickers) and Lechler (punter).
                // Punter-specific stats (cptb, cpbl, cppt) aren't in
                // nflverse stats_player_reg, so left blank.
                if (pcki is not null && HasKickingStats(c) && pcki.Records.Count < pcki.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pcki);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("ckfa", ClampUInt(c.Get("fgAtt"),   13));
                    rec.SetUInt("ckfm", ClampUInt(c.Get("fgMade"),  13));
                    rec.SetUInt("ckea", ClampUInt(c.Get("patAtt"),  11));
                    rec.SetUInt("ckem", ClampUInt(c.Get("patMade"), 11));
                    pcki.Records.Add(rec);
                }

                if (pckp is not null && HasReturnStats(c) && pckp.Records.Count < pckp.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pckp);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("crka", ClampUInt(c.Get("kickReturns"),                  11));
                    rec.SetUInt("crpa", ClampUInt(c.Get("puntReturns"),                  11));
                    rec.SetUInt("crky", ClampUInt(Math.Max(0, c.Get("kickRetYds")),      17));
                    rec.SetUInt("crpy", ClampUInt(Math.Max(0, c.Get("puntRetYds")),      17));
                    // crkt / crpt (return TDs) aren't in nflverse stats_player_reg.
                    pckp.Records.Add(rec);
                }

                // PCNG.cgmp = career games played. Other PCNG fields (cgdp,
                // cgms) appear to be sim-populated; safe to leave 0.
                if (pcng is not null && c.Get("games") > 0 && pcng.Records.Count < pcng.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pcng);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("cgmp", ClampUInt(c.Get("games"), 9));
                    pcng.Records.Add(rec);
                }
            }
        }

        if (pcof is not null) pcof.Header.CurRecords = (ushort)pcof.Records.Count;
        if (pcde is not null) pcde.Header.CurRecords = (ushort)pcde.Records.Count;
        if (pcki is not null) pcki.Header.CurRecords = (ushort)pcki.Records.Count;
        if (pckp is not null) pckp.Header.CurRecords = (ushort)pckp.Records.Count;
        if (pcng is not null) pcng.Header.CurRecords = (ushort)pcng.Records.Count;
    }

    private static bool HasKickingStats(StatBlock c) =>
        c.Get("fgAtt") + c.Get("patAtt") > 0;

    private static bool HasReturnStats(StatBlock c) =>
        c.Get("kickReturns") + c.Get("puntReturns") > 0;

    /// <summary>
    /// Per-season stat tables (PSOF/PSDE/PSKI/PSKP/PSNG) mirror their PC*
    /// counterparts but with an `s` prefix (saya/satd/... vs caya/catd/...).
    /// Each row carries a SEYR field encoding the season year as offset from
    /// the disc's base year (M08=2007, M09=2008, M12=2011), 6-bit signed
    /// (range -32..+31).
    ///
    /// Capacity-aware: each table has a max-records ceiling. We write each
    /// player's last-3 seasons worth of stats (the seasons the scraper
    /// emits per player), in OVR-descending order across the league, until
    /// the table fills.
    /// </summary>
    private void WriteSeasonStats(
        MaddenTdb template,
        CanonicalRoster canonical,
        Dictionary<CanonicalRosterPlayer, uint> pgids,
        CanonicalStats stats,
        int baseYear)
    {
        var byGsis = new Dictionary<string, CanonicalPlayerStats>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in stats.Players)
        {
            if (!string.IsNullOrEmpty(s.GsisId)) byGsis[s.GsisId] = s;
        }

        var psof = template.FindTable("PSOF");
        var psde = template.FindTable("PSDE");
        var pski = template.FindTable("PSKI");
        var pskp = template.FindTable("PSKP");
        var psng = template.FindTable("PSNG");

        // Enumerate canonical players in canonical-roster order (already
        // sorted by team, but within team we want OVR descending so the
        // most-visible players' stats land first if any table fills up).
        var playerOrder = new List<CanonicalRosterPlayer>();
        foreach (var team in canonical.Teams)
        {
            playerOrder.AddRange(team.Players.OrderByDescending(p => p.Ratings?.Ovr ?? 0));
        }

        foreach (var player in playerOrder)
        {
            if (string.IsNullOrEmpty(player.GsisId)) continue;
            if (!byGsis.TryGetValue(player.GsisId, out var ps)) continue;
            if (!pgids.TryGetValue(player, out var pgid)) continue;

            // Walk most-recent season first so if a table fills mid-player
            // they at least have their latest line.
            foreach (var (season, sb) in ps.Seasons.OrderByDescending(kv => kv.Key))
            {
                int seyrSigned = season - baseYear;
                if (seyrSigned < -32 || seyrSigned > 31) continue;
                uint seyr = (uint)(seyrSigned & 0x3F);  // mask to 6 bits

                if (psof is not null && HasOffensiveStats(sb) && psof.Records.Count < psof.Header.MaxRecords)
                {
                    var rec = NewRecordFor(psof);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("SEYR", seyr);
                    rec.SetUInt("saya", ClampUInt(sb.Get("passYards"),  15));
                    rec.SetUInt("satd", ClampUInt(sb.Get("passTDs"),     7));
                    rec.SetUInt("sacm", ClampUInt(sb.Get("passComp"),   10));
                    rec.SetUInt("saat", ClampUInt(sb.Get("passAtt"),    11));
                    rec.SetUInt("sain", ClampUInt(sb.Get("passInts"),    6));
                    rec.SetUInt("sufu", ClampUInt(sb.Get("fumbles"),     5));
                    rec.SetUInt("suya", ClampUInt(sb.Get("rushYards"),  14));
                    rec.SetUInt("sutd", ClampUInt(sb.Get("rushTDs"),     7));
                    rec.SetUInt("suat", ClampUInt(sb.Get("rushAtt"),    10));
                    rec.SetUInt("scca", ClampUInt(sb.Get("receptions"),  8));
                    rec.SetUInt("scya", ClampUInt(Math.Max(0, sb.Get("recYards")), 14));
                    rec.SetUInt("sctd", ClampUInt(sb.Get("recTDs"),      7));
                    psof.Records.Add(rec);
                }

                if (psde is not null && HasDefensiveStats(sb) && psde.Records.Count < psde.Header.MaxRecords)
                {
                    var rec = NewRecordFor(psde);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("SEYR", seyr);
                    rec.SetUInt("ssca", ClampUInt(sb.Get("tacklesSolo"), 8));
                    rec.SetUInt("sdta", ClampUInt(sb.Get("tacklesAst"),  9));
                    rec.SetUInt("slff", ClampUInt(sb.Get("forcedFum"),   6));
                    rec.SetUInt("sdbh", ClampUInt(sb.Get("passDefended"),9));
                    rec.SetUInt("slsk", ClampUInt(sb.Get("sacks"),       6));
                    rec.SetUInt("ssin", ClampUInt(sb.Get("defInts"),     6));
                    rec.SetUInt("slfr", ClampUInt(sb.Get("fumRecov"),    5));
                    psde.Records.Add(rec);
                }

                if (pski is not null && HasKickingStats(sb) && pski.Records.Count < pski.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pski);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("SEYR", seyr);
                    rec.SetUInt("skfa", ClampUInt(sb.Get("fgAtt"),   8));
                    rec.SetUInt("skfm", ClampUInt(sb.Get("fgMade"),  8));
                    rec.SetUInt("skea", ClampUInt(sb.Get("patAtt"),  8));
                    rec.SetUInt("skem", ClampUInt(sb.Get("patMade"), 8));
                    pski.Records.Add(rec);
                }

                if (pskp is not null && HasReturnStats(sb) && pskp.Records.Count < pskp.Header.MaxRecords)
                {
                    var rec = NewRecordFor(pskp);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("SEYR", seyr);
                    rec.SetUInt("srka", ClampUInt(sb.Get("kickReturns"),                  8));
                    rec.SetUInt("srpa", ClampUInt(sb.Get("puntReturns"),                  8));
                    rec.SetUInt("srky", ClampUInt(Math.Max(0, sb.Get("kickRetYds")),     13));
                    rec.SetUInt("srpy", ClampUInt(Math.Max(0, sb.Get("puntRetYds")),     13));
                    pskp.Records.Add(rec);
                }

                if (psng is not null && sb.Get("games") > 0 && psng.Records.Count < psng.Header.MaxRecords)
                {
                    var rec = NewRecordFor(psng);
                    rec.SetUInt("PGID", pgid);
                    rec.SetUInt("SEYR", seyr);
                    rec.SetUInt("sgmp", ClampUInt(sb.Get("games"), 5));
                    psng.Records.Add(rec);
                }
            }
        }

        if (psof is not null) psof.Header.CurRecords = (ushort)psof.Records.Count;
        if (psde is not null) psde.Header.CurRecords = (ushort)psde.Records.Count;
        if (pski is not null) pski.Header.CurRecords = (ushort)pski.Records.Count;
        if (pskp is not null) pskp.Header.CurRecords = (ushort)pskp.Records.Count;
        if (psng is not null) psng.Header.CurRecords = (ushort)psng.Records.Count;
    }

    private static bool HasOffensiveStats(StatBlock c) =>
        c.Get("passYards") + c.Get("rushYards") + Math.Max(0, c.Get("recYards")) > 0
        || c.Get("passAtt") + c.Get("rushAtt") + c.Get("receptions") > 0;

    private static bool HasDefensiveStats(StatBlock c) =>
        c.Get("tacklesSolo") + c.Get("tacklesAst") + c.Get("sacks") + c.Get("defInts") > 0;

    private static TdbRecord NewRecordFor(TdbTable table)
    {
        var rec = new TdbRecord();
        foreach (var f in table.Fields)
        {
            rec[f.Name] = f.Type == MaddenTdb.TypeString ? "" : (object)0u;
        }
        return rec;
    }

    private static uint ClampUInt(int value, int bits)
    {
        long max = (1L << bits) - 1;
        if (value < 0) value = 0;
        if (value > max) value = (int)max;
        return (uint)value;
    }

    /// <summary>
    /// Tables in the franchise TDB that have a PGID field and could end up
    /// with dangling references after we shift all PLAY-record PGIDs into
    /// the 16384+ canonical range. PLAY itself + DCHT are handled separately
    /// (we rewrite them with the new PGIDs). Anything else we clear to avoid
    /// orphan-pointer crashes / wrong-name displays in stat history screens.
    /// </summary>
    private static readonly string[] PgidReferencingTablesToClear =
    {
        "INJY",  // injuries
        // Per-season stats history (highest-record-count tables in a fresh
        // template; account for stats accumulated by template's launch-year
        // players over their careers prior to franchise start).
        "PSDE", "PSKI", "PSKP", "PSNG", "PSOF", "PSOL",
        // Per-career cumulative stats history.
        "PCDE", "PCKI", "PCKP", "PCNG", "PCOF", "PCOL",
        // Misc PGID-keyed tables that may have non-zero records.
        "FBPL", "PMCV", "pPYR", "TCIN", "TCPA",
        // Box-score / game-event tables (empty in fresh Week-1 but clear
        // defensively so any future template variant doesn't sneak in
        // stale-PGID rows).
        "AWPL", "AYPL", "BDEF", "BKIC", "BKPR", "BOFF", "BQTR", "BSCS", "BTES",
        "STTI", "PAGR", "PRGD", "PRGK", "PRGR", "PROF", "PGSO", "PHOF",
        "DROS", "DRPL", "DRRS", "FDPL", "FDRS", "IRST", "MLAS", "STCF",
        "PRPB", "pTAT", "PFTA", "PLGR", "PLIA", "PGDS", "PPBS", "PROR",
        "PLRL", "PLRS", "PLRT", "RFPL", "RFST", "SPLA", "SSPL", "FAPL",
        "PSTA", "SIOF", "SCON", "DCGA", "IGAM", "TMFN", "TPMN", "PLGA",
        "PLSU", "PGDE", "PGKI", "PGKP", "PGNG", "PGOF", "PGOL", "PCOL",
    };

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
        // For position groups that have multiple Madden codes (OT -> LT+RT etc.),
        // distribute canonical players across the variants in round-robin order
        // so both sides of the depth chart get plausible starters and backups.
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
                var variants = PositionVariants.GetValueOrDefault(ppos, new uint[] { ppos });
                if (variants.Length == 1)
                {
                    depthLists[(realTgid, ppos)] = list.Select(t => t.pgid).ToList();
                }
                else
                {
                    // Round-robin distribute by OVR ranking so each variant
                    // gets a comparable mix: top OT -> LT[0], 2nd -> RT[0],
                    // 3rd -> LT[1], 4th -> RT[1], etc.
                    for (int i = 0; i < variants.Length; i++)
                        depthLists[(realTgid, variants[i])] = new List<uint>();
                    for (int i = 0; i < list.Count; i++)
                    {
                        var variant = variants[i % variants.Length];
                        depthLists[(realTgid, variant)].Add(list[i].pgid);
                    }
                }
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
