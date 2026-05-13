namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Synthesizes per-player NFL contract terms (PCON, PSA0..6, PSB0..6, PCSA)
/// for a Madden franchise save based on player rating, position, age, and
/// the league salary cap. Driven by a position-weight table calibrated to
/// NFL spending tiers so league total cap usage lands near (cap × 32 teams).
///
/// All cap-hit / salary / bonus values are stored in the TDB as **$10,000
/// units**. The 14-bit PSA/PCSA ceiling caps single-year salaries at
/// $163.83M and the 13-bit PSB ceiling caps signing-bonus proration at
/// $81.91M - far above any real NFL contract.
///
/// This is a *plausibility* model, not a real-contracts importer. A 2018
/// franchise produced by this synthesizer will have era-appropriate cap
/// usage but won't match Spotrac-reported individual contracts. Phase 3
/// (real contract data) is the next step up.
/// </summary>
public sealed class ContractSynthesizer
{
    public const int MaxContractYears = 7;   // PSA0..PSA6 / PSB0..PSB6
    public const int Psa10kCeiling = 16_383; // 14-bit UINT in $10K units
    public const int Psb10kCeiling = 8_191;  // 13-bit UINT in $10K units

    /// <summary>
    /// NFL spending tier by PPOS index. Matches the Madden position layout
    /// (0=QB, 1=HB, 2=FB, 3=WR, 4=TE, 5..9=OL L→R, 10/11=LE/RE, 12=DT,
    /// 13/14/15=LOLB/MLB/ROLB, 16=CB, 17=FS, 18=SS, 19=K, 20=P).
    /// </summary>
    private static readonly Dictionary<uint, double> PositionWeights = new()
    {
        [0]  = 1.80, // QB - premium
        [1]  = 0.80, // HB
        [2]  = 0.30, // FB
        [3]  = 1.05, // WR
        [4]  = 0.65, // TE
        [5]  = 1.30, // LT - blindside premium
        [6]  = 0.80, // LG
        [7]  = 0.85, // C
        [8]  = 0.80, // RG
        [9]  = 1.05, // RT
        [10] = 1.15, // LE - edge rusher premium
        [11] = 1.15, // RE
        [12] = 1.05, // DT
        [13] = 0.85, // LOLB
        [14] = 0.70, // MLB
        [15] = 0.85, // ROLB
        [16] = 1.10, // CB
        [17] = 0.75, // FS
        [18] = 0.75, // SS
        [19] = 0.15, // K
        [20] = 0.15, // P
    };

    private readonly long _leagueCap;

    public ContractSynthesizer(long leagueCap)
    {
        _leagueCap = leagueCap;
    }

    /// <summary>
    /// Compute contract terms for one player. <paramref name="ovr"/> 0-99,
    /// <paramref name="pos"/> PPOS index, <paramref name="age"/> 0-63.
    /// </summary>
    public ContractTerms Synthesize(uint ovr, uint pos, uint age)
    {
        // Out-of-roster / free-agent / sentinel records: write a minimum
        // contract so the field has a valid value but nothing extravagant.
        if (ovr == 0)
            return new ContractTerms { PCON = 1, PCSA10k = 70, PSA10k = new uint[1] { 70 }, PSB10k = new uint[1] };

        double posWeight = PositionWeights.GetValueOrDefault(pos, 0.50);

        // OVR factor: exponential ramp. OVR 50 → ~0.04, OVR 75 → ~0.25,
        // OVR 85 → ~0.50, OVR 95 → ~0.85, OVR 99 → ~1.0.
        double ovrNorm = Math.Max(0, ovr - 49) / 50.0;
        double ovrFactor = Math.Pow(ovrNorm, 2.2);

        // Age factor: peak earnings 26-30; rookies cheaper; vets decline.
        double ageFactor = age switch
        {
            < 23 => 0.55, // rookie deals
            < 26 => 0.85,
            < 31 => 1.00,
            < 33 => 0.80,
            < 36 => 0.55,
            _    => 0.35,
        };

        // Calibration: league total cap usage ≈ cap × 32 teams. Spread
        // across ~53 roster spots per team × 32 = 1696 active players.
        // Average target cap hit per player ≈ cap / 53. Scale by tier
        // factors so stars eat more than backups.
        double avgCapHit = _leagueCap / 53.0;
        double capHit = avgCapHit * posWeight * ovrFactor * ageFactor * 3.5;

        // Veteran minimum floor (~$700K for the 2018-era league).
        capHit = Math.Max(capHit, _leagueCap * 0.004);  // ~0.4% of cap

        // Contract length: tied to age + tier.
        uint pcon = ChooseContractLength(ovr, age);

        // Cap hit -> $10K units, clamp into 14-bit field.
        uint capHit10k = (uint)Math.Min(Psa10kCeiling, (long)(capHit / 10_000.0));

        // Split into base salary (~60%) + prorated signing bonus (~40%).
        // Each year's PSA grows slightly, PSB stays flat (it's the
        // proration of the original bonus across the contract).
        var psa = new uint[pcon];
        var psb = new uint[pcon];
        uint baseY0 = (uint)(capHit10k * 0.60);
        uint bonusProrated = (uint)Math.Min(Psb10kCeiling, capHit10k * 0.40);
        for (int y = 0; y < pcon; y++)
        {
            uint psaY = (uint)Math.Min(Psa10kCeiling, baseY0 * (10 + y) / 10);
            psa[y] = psaY;
            psb[y] = bonusProrated;
        }

        return new ContractTerms
        {
            PCON = pcon,
            PCSA10k = capHit10k,
            PSA10k = psa,
            PSB10k = psb,
            BonusOriginal10k = (uint)Math.Min(Psb10kCeiling, bonusProrated * pcon),
        };
    }

    /// <summary>
    /// Realistic contract-length distribution. Rookies typically on 4-year
    /// deals; mid-career stars 4-6 years; vets 1-3.
    /// </summary>
    private static uint ChooseContractLength(uint ovr, uint age) => age switch
    {
        < 24 when ovr >= 80 => 5,  // young star, second rookie-deal year
        < 24                 => 4,  // rookie deal
        < 30 when ovr >= 85 => 5,  // young/prime star extension (Mahomes/Wilson era)
        < 30                 => 4,  // young/prime starter
        < 33                 => 3,  // prime veteran
        < 35                 => 2,  // late-career
        _                    => 1,  // 1-year vet deal
    };

    /// <summary>
    /// Apply synthesized terms to a PLAY record's contract fields. Writes
    /// PCON, PCSA, PSA0..PSA{N-1}, PSB0..PSB{N-1}, PSBO. Years beyond the
    /// contract length stay as whatever the template carried (typically 0).
    /// </summary>
    public static void Apply(TdbRecord record, ContractTerms terms)
    {
        record.SetUInt("PCON", terms.PCON);
        record.SetUInt("PCSA", terms.PCSA10k);
        record.SetUInt("PSBO", terms.BonusOriginal10k);
        for (int y = 0; y < terms.PSA10k.Length && y < MaxContractYears; y++)
        {
            record.SetUInt($"PSA{y}", terms.PSA10k[y]);
            record.SetUInt($"PSB{y}", terms.PSB10k[y]);
        }
        // Zero out year slots beyond contract length (Madden treats those
        // as already-expired years; leaving template values can produce
        // ghost dead-money entries).
        for (int y = terms.PSA10k.Length; y < MaxContractYears; y++)
        {
            record.SetUInt($"PSA{y}", 0);
            record.SetUInt($"PSB{y}", 0);
        }
    }
}

public sealed class ContractTerms
{
    /// <summary>Contract length remaining, years (1-15). Stored in PCON.</summary>
    public uint PCON { get; set; }
    /// <summary>Current-year cap hit, $10K units. Stored in PCSA.</summary>
    public uint PCSA10k { get; set; }
    /// <summary>Annual base salary per contract year, $10K units. Stored in PSA0..PSA{N-1}.</summary>
    public uint[] PSA10k { get; set; } = Array.Empty<uint>();
    /// <summary>Prorated signing bonus per contract year, $10K units. Stored in PSB0..PSB{N-1}.</summary>
    public uint[] PSB10k { get; set; } = Array.Empty<uint>();
    /// <summary>Total original signing bonus (PSB × PCON), $10K units. Stored in PSBO.</summary>
    public uint BonusOriginal10k { get; set; }
}
