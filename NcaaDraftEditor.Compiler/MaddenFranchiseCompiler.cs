using System.Text.Json;
using NcaaDraftEditor.Canonical;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Phase-1 franchise-save mutator: given a Madden franchise TDB template and a
/// target NFL season year, rewrites two singletons so the game boots in the
/// right era:
///
///   SEAI.SEYR  Season year offset from the disc's base year. e.g. for an M08
///              template (base 2007), target 2018 -> SEYR = 11. 6-bit SINT,
///              effective range 1975..2038 from a 2007 base.
///   SLRI.SCAD  League salary cap, raw dollars. Written verbatim from
///              data/raw/salary-caps/nfl-salary-caps.json. M08's engine
///              routinely produces 9-figure cap values during long sims, so
///              writing a real 2018 cap ($177.2M) is well within proven range.
///   SLRI.SMAD  Franchise tag amount (QB tier, dollars).
///   SLRI.RFA1..RFA4  Restricted free agent tender amounts at the 4 ascending
///                    tiers, dollars each. 32-bit UINT fields.
///
/// Per-player contracts (PSA0..6 / PSB0..6 / PCSA in the franchise PLAY table)
/// are NOT touched here; the engine generates plausible contracts from
/// rating + PCON + age when the franchise launches. Phase 2 will add a
/// rating-driven contract synthesizer.
/// </summary>
public sealed class MaddenFranchiseCompiler
{
    public const int M08BaseYear = 2007;
    public const int M09BaseYear = 2008;
    public const int M12BaseYear = 2011;

    private readonly int _baseYear;
    private readonly SalaryCapTable _caps;

    public MaddenFranchiseCompiler(int baseYear, SalaryCapTable caps)
    {
        _baseYear = baseYear;
        _caps = caps;
    }

    public static MaddenFranchiseCompiler ForM08(SalaryCapTable caps) =>
        new(M08BaseYear, caps);
    public static MaddenFranchiseCompiler ForM09(SalaryCapTable caps) =>
        new(M09BaseYear, caps);
    public static MaddenFranchiseCompiler ForM12(SalaryCapTable caps) =>
        new(M12BaseYear, caps);

    /// <summary>
    /// Mutate <paramref name="template"/> in place to target the given NFL
    /// season year. Returns the same instance, ready to .Save().
    ///
    /// Phase 1 work: SEAI.SEYR (calendar) + SLRI.SCAD/SMAD/RFA1..4 (cap).
    /// Phase 2 work: per-player contract synthesis (rating-based model).
    /// Phase 3 work: real contract import from <paramref name="contracts"/>;
    ///     PLAY records are matched by name and get real PSA/PSB/PCSA. Players
    ///     with no contract match fall through to Phase 2 synthesis.
    /// </summary>
    public MaddenTdb Compile(
        int nflSeason,
        MaddenTdb template,
        bool synthesizeContracts = true,
        CanonicalContracts? contracts = null)
    {
        if (!_caps.Years.TryGetValue(nflSeason.ToString(), out var year))
            throw new InvalidOperationException(
                $"No salary cap data for {nflSeason}. Add an entry under 'years' in " +
                "data/raw/salary-caps/nfl-salary-caps.json.");

        var seai = template.FindTable("SEAI")
            ?? throw new InvalidOperationException("Template missing SEAI (season info) table");
        var slri = template.FindTable("SLRI")
            ?? throw new InvalidOperationException("Template missing SLRI (salary info) table");

        if (seai.Records.Count < 1)
            throw new InvalidOperationException("SEAI is empty; expected exactly 1 league-state record");
        if (slri.Records.Count < 1)
            throw new InvalidOperationException("SLRI is empty; expected exactly 1 salary-state record");

        int yearOffset = nflSeason - _baseYear;
        if (yearOffset < -32 || yearOffset > 31)
            throw new InvalidOperationException(
                $"Year offset {yearOffset} (= {nflSeason} - {_baseYear}) doesn't fit in SEYR's 6-bit SINT range.");

        seai.Records[0].SetUInt("SEYR", (uint)(yearOffset & 0x3F));  // mask to 6 bits

        slri.Records[0].SetUInt("SCAD", (uint)year.Cap);
        slri.Records[0].SetUInt("SMAD", (uint)year.FranchiseTag);
        if (year.Rfa.Length >= 4)
        {
            slri.Records[0].SetUInt("RFA1", (uint)year.Rfa[0]);
            slri.Records[0].SetUInt("RFA2", (uint)year.Rfa[1]);
            slri.Records[0].SetUInt("RFA3", (uint)year.Rfa[2]);
            slri.Records[0].SetUInt("RFA4", (uint)year.Rfa[3]);
        }

        if (synthesizeContracts)
            ContractStats = ApplyContracts(template, year.Cap, contracts);

        return template;
    }

    /// <summary>
    /// Stats from the last Compile() call's contract pass: how many PLAY
    /// records got real contracts (matched against the CanonicalContracts
    /// list) vs. synthesized contracts. Available for the CLI to print.
    /// </summary>
    public ContractApplyStats? ContractStats { get; private set; }

    /// <summary>
    /// Iterate PLAY records and write era-appropriate contract terms.
    /// If <paramref name="contracts"/> is provided, match each record by
    /// "FirstName LastName" against the contract list and write real values
    /// (PCSA from cap_number, PSA0/PSB0 from base_salary/prorated_bonus).
    /// Records with no contract match fall back to ContractSynthesizer.
    /// </summary>
    private static ContractApplyStats ApplyContracts(
        MaddenTdb template, long leagueCap, CanonicalContracts? contracts)
    {
        var play = template.FindTable("PLAY");
        var teamTable = template.FindTable("TEAM");
        var stats = new ContractApplyStats();
        if (play is null) return stats;
        if (play.FindField("PSA0") is null || play.FindField("PCSA") is null)
            return stats;  // not a franchise PLAY table

        // Build template TGID -> team-abbreviation map for disambiguating
        // duplicate-name contracts (multiple "Chris Jones", "Mike Williams"
        // etc. exist across seasons). Without team-aware lookup, the last
        // contract written to byName wins and the wrong player gets the
        // wrong cap hit.
        var tgidToAbbrev = new Dictionary<uint, string>();
        if (teamTable is not null)
        {
            foreach (var rec in teamTable.Records)
            {
                var abbr = NormalizeTeamAbbrev(rec.GetString("TSNA"));
                if (!string.IsNullOrEmpty(abbr))
                    tgidToAbbrev[rec.GetUInt("TGID")] = abbr;
            }
        }

        // Build name -> list-of-contracts (preserves duplicates).
        var byName = new Dictionary<string, List<CanonicalContract>>(StringComparer.OrdinalIgnoreCase);
        if (contracts is not null)
        {
            foreach (var c in contracts.Players)
            {
                var key = NormalizeName(c.Name);
                if (string.IsNullOrEmpty(key)) continue;
                if (!byName.TryGetValue(key, out var list))
                    byName[key] = list = new List<CanonicalContract>();
                list.Add(c);
            }
        }

        var synth = new ContractSynthesizer(leagueCap);
        foreach (var rec in play.Records)
        {
            uint ovr = rec.GetUInt("POVR");
            uint pos = rec.GetUInt("PPOS");
            uint age = rec.GetUInt("PAGE");

            string first = rec.GetString("PFNA");
            string last = rec.GetString("PLNA");
            string nameKey = NormalizeName($"{first} {last}");

            CanonicalContract? real = null;
            if (!string.IsNullOrEmpty(nameKey) && byName.TryGetValue(nameKey, out var matches))
            {
                if (matches.Count == 1)
                {
                    real = matches[0];
                }
                else
                {
                    // Multiple players share this normalized name. Disambiguate
                    // by team abbreviation. The contract's team may use a modern
                    // code (LAR/LAC/LV) while the template has the historical
                    // code (STL/SD/OAK); normalize both.
                    var playerTeam = tgidToAbbrev.GetValueOrDefault(rec.GetUInt("TGID"), "");
                    foreach (var candidate in matches)
                    {
                        if (NormalizeTeamAbbrev(candidate.Team) == playerTeam)
                        {
                            real = candidate;
                            break;
                        }
                    }
                    // Last-resort: take the first match (preserves old behavior).
                    real ??= matches[0];
                }
            }

            if (real is not null)
            {
                ApplyRealContract(rec, real);
                stats.MatchedReal++;
            }
            else
            {
                var terms = synth.Synthesize(ovr, pos, age);
                ContractSynthesizer.Apply(rec, terms);
                stats.SynthesizedFallback++;
            }
        }
        return stats;
    }

    /// <summary>
    /// Canonicalize a team abbreviation. Maps modern codes back to their
    /// historical Madden-era forms (LA/LAR -> STL, LAC -> SD, LV -> OAK).
    /// Mirrors scrapers/nflverse/build_roster.py's ABBREV_ALIASES.
    /// </summary>
    public static string NormalizeTeamAbbrev(string abbr)
    {
        if (string.IsNullOrWhiteSpace(abbr)) return "";
        return abbr.Trim().ToUpperInvariant() switch
        {
            "LA" or "LAR" => "STL",
            "LAC" => "SD",
            "LV" => "OAK",
            "WSH" => "WAS",
            "ARZ" => "ARI",
            "BLT" => "BAL",
            "CLV" => "CLE",
            "HST" => "HOU",
            "SL" => "STL",
            var s => s,
        };
    }

    /// <summary>
    /// Map a CanonicalContract onto a PLAY record's contract fields.
    /// Real-world dollars in $M -> $10K units = millions × 100.
    /// </summary>
    private static void ApplyRealContract(TdbRecord rec, CanonicalContract c)
    {
        // Cap hit (PCSA): 14-bit ceiling = $163.83M in $10K units
        uint pcsa10k = (uint)Math.Min(ContractSynthesizer.Psa10kCeiling,
            Math.Max(0, (long)Math.Round(c.CapHitMillions * 100)));

        // Years remaining (PCON): 4-bit field, clamp 1..15
        uint pcon = (uint)Math.Clamp(c.YearsRemaining, 1, 15);

        // Base salary + prorated bonus for current year. If OTC didn't break
        // out base/bonus separately, split the cap hit 60/40.
        double baseM = c.BaseSalaryMillions > 0 ? c.BaseSalaryMillions : c.CapHitMillions * 0.6;
        double bonusM = c.ProratedBonusMillions > 0 ? c.ProratedBonusMillions : c.CapHitMillions * 0.4;
        uint psa0 = (uint)Math.Min(ContractSynthesizer.Psa10kCeiling,
            Math.Max(0, (long)Math.Round(baseM * 100)));
        uint psb0 = (uint)Math.Min(ContractSynthesizer.Psb10kCeiling,
            Math.Max(0, (long)Math.Round(bonusM * 100)));

        rec.SetUInt("PCON", pcon);
        rec.SetUInt("PCSA", pcsa10k);
        rec.SetUInt("PSA0", psa0);
        rec.SetUInt("PSB0", psb0);
        rec.SetUInt("PSBO", (uint)Math.Min(ContractSynthesizer.Psb10kCeiling, psb0 * pcon));

        // Future years aren't broken out in our canonical contracts (we'd
        // need per-year columns from the parquet's `cols`, which we currently
        // collapse to a single year). Use a simple model: PSA grows 5%/yr,
        // PSB stays flat. Beyond contract length: zero.
        for (int y = 1; y < ContractSynthesizer.MaxContractYears; y++)
        {
            if (y < pcon)
            {
                rec.SetUInt($"PSA{y}", (uint)Math.Min(ContractSynthesizer.Psa10kCeiling, psa0 * (100 + y * 5) / 100));
                rec.SetUInt($"PSB{y}", psb0);
            }
            else
            {
                rec.SetUInt($"PSA{y}", 0);
                rec.SetUInt($"PSB{y}", 0);
            }
        }
    }

    /// <summary>
    /// Canonicalize a player name for matching. Handles case, whitespace,
    /// trailing suffixes (Jr/Sr/II/III/IV), and punctuation.
    /// </summary>
    public static string NormalizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return "";
        var s = name.Trim().ToLowerInvariant();
        // Strip suffixes that PLAY records may omit but contracts include.
        foreach (var suffix in new[] { " jr.", " jr", " sr.", " sr", " ii", " iii", " iv", " v" })
        {
            if (s.EndsWith(suffix)) s = s[..^suffix.Length];
        }
        // Remove periods and apostrophes for matching ("T.J." vs "TJ").
        s = s.Replace(".", "").Replace("'", "").Replace("-", " ");
        // Collapse multiple spaces.
        while (s.Contains("  ")) s = s.Replace("  ", " ");
        return s.Trim();
    }
}

public sealed class ContractApplyStats
{
    public int MatchedReal { get; set; }
    public int SynthesizedFallback { get; set; }
    public int Total => MatchedReal + SynthesizedFallback;
}

/// <summary>
/// In-memory shape of data/raw/salary-caps/nfl-salary-caps.json.
/// </summary>
public sealed class SalaryCapTable
{
    public Dictionary<string, SalaryCapYear> Years { get; set; } = new();

    public static SalaryCapTable LoadFile(string path)
    {
        var doc = JsonDocument.Parse(File.ReadAllText(path));
        var table = new SalaryCapTable();
        if (!doc.RootElement.TryGetProperty("years", out var years))
            return table;
        foreach (var prop in years.EnumerateObject())
        {
            var y = new SalaryCapYear
            {
                Cap = prop.Value.GetProperty("cap").GetInt64(),
                FranchiseTag = prop.Value.TryGetProperty("franchiseTag", out var ft) ? ft.GetInt64() : 0,
            };
            if (prop.Value.TryGetProperty("rfa", out var rfa))
            {
                var arr = rfa.EnumerateArray().Select(e => e.GetInt64()).ToArray();
                y.Rfa = arr;
            }
            table.Years[prop.Name] = y;
        }
        return table;
    }
}

public sealed class SalaryCapYear
{
    public long Cap { get; set; }
    public long FranchiseTag { get; set; }
    public long[] Rfa { get; set; } = Array.Empty<long>();
}
