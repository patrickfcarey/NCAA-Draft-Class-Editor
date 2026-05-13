using System.Text.Json;

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
    /// Phase 2 work: per-player contract synthesis via ContractSynthesizer
    /// (gated by <paramref name="synthesizeContracts"/>; default on).
    /// </summary>
    public MaddenTdb Compile(int nflSeason, MaddenTdb template, bool synthesizeContracts = true)
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
            SynthesizeContracts(template, year.Cap);

        return template;
    }

    /// <summary>
    /// Iterate PLAY records and write era-appropriate contract terms
    /// (PCON / PSA0..6 / PSB0..6 / PCSA) based on each player's POVR,
    /// PPOS, PAGE. Only runs if the PLAY table has the contract fields
    /// (i.e. it's a franchise PLAY table, not a roster PLAY).
    /// </summary>
    private static void SynthesizeContracts(MaddenTdb template, long leagueCap)
    {
        var play = template.FindTable("PLAY");
        if (play is null) return;
        if (play.FindField("PSA0") is null || play.FindField("PCSA") is null)
            return;  // not a franchise PLAY table

        var synth = new ContractSynthesizer(leagueCap);
        foreach (var rec in play.Records)
        {
            uint ovr = rec.GetUInt("POVR");
            uint pos = rec.GetUInt("PPOS");
            uint age = rec.GetUInt("PAGE");
            var terms = synth.Synthesize(ovr, pos, age);
            ContractSynthesizer.Apply(rec, terms);
        }
    }
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
