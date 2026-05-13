using NcaaDraftEditor.Canonical;
using NcaaDraftEditor.Core;

namespace NcaaDraftEditor.Compiler;

/// <summary>
/// Compiles a canonical real-world draft class into NCAA 06's 86-byte-per-player
/// binary format. Optionally enriches each player from a same-year Madden roster
/// (e.g. 2018 canonical + Madden 19 launch ratings -> rookie ratings for the 2018
/// NFL Draft class as they would appear in Madden 19).
/// </summary>
public sealed class DraftClassCompiler
{
    private readonly PositionMapper _positions;
    private readonly CollegeMapper _colleges;
    private readonly MaddenRoster? _madden;
    private readonly DraftClassFile? _filler;

    public DraftClassCompiler(
        PositionMapper positions,
        CollegeMapper colleges,
        MaddenRoster? madden = null,
        DraftClassFile? filler = null)
    {
        _positions = positions;
        _colleges = colleges;
        _madden = madden;
        _filler = filler;
    }

    public DraftClassFile Compile(CanonicalDraftClass canonical)
    {
        var dc = new DraftClassFile();
        foreach (var player in canonical.Players)
        {
            if (dc.Players.Count >= DraftClassFile.MaxPlayers) break;
            dc.Players.Add(CompilePlayer(player));
        }
        FillRemainingSlots(dc);
        dc.Trailer = BuildSectorPadTrailer(dc.Players.Count);
        return dc;
    }

    /// <summary>
    /// Hard cap applied to filler players' OVR and key attributes (speed,
    /// strength, agility, acceleration, awareness, catch, throw power, tackle).
    /// Without this cap, high-rated college players from the template file
    /// (e.g. 91-OVR Forsett, 90-OVR Foster from the 2008 sample) outrank our
    /// real NFL Draft picks in Madden 08's OVR-sorted draft pool, and the
    /// game's simulated draft picks them ahead of the actual rookies.
    /// Default real-pick OVR is 50 when no Madden data matches, so filler is
    /// capped one below that to keep all filler strictly below all real picks.
    /// </summary>
    public const byte FillerRatingCap = 49;

    /// <summary>
    /// Real NCAA draft-class files always carry 1600 valid college player
    /// records (the full eligible senior + early-entry pool, not just the
    /// ~250 actually drafted). Madden 08 hangs on "initializing roster
    /// management" if any of the 1600 slots contains an empty/zeroed record.
    /// If a filler file is provided we use its records for slots beyond our
    /// real picks; otherwise we fall back to zero padding and warn that
    /// Madden may reject the file.
    /// </summary>
    private void FillRemainingSlots(DraftClassFile dc)
    {
        int realCount = dc.Players.Count;
        if (_filler is not null)
        {
            for (int i = realCount; i < DraftClassFile.MaxPlayers && i < _filler.Players.Count; i++)
            {
                var copy = new byte[DraftClassFile.RecordSize];
                Buffer.BlockCopy(_filler.Players[i].Raw, 0, copy, 0, DraftClassFile.RecordSize);
                var rec = new PlayerRecord(copy);
                CapFillerAttributes(rec);
                dc.Players.Add(rec);
            }
        }
        while (dc.Players.Count < DraftClassFile.MaxPlayers)
            dc.Players.Add(new PlayerRecord(new byte[DraftClassFile.RecordSize]));
    }

    private static void CapFillerAttributes(PlayerRecord rec)
    {
        rec.POVR = Min(rec.POVR, FillerRatingCap);
        rec.PSPD = Min(rec.PSPD, FillerRatingCap);
        rec.PACC = Min(rec.PACC, FillerRatingCap);
        rec.PAGI = Min(rec.PAGI, FillerRatingCap);
        rec.PSTR = Min(rec.PSTR, FillerRatingCap);
        rec.PAWR = Min(rec.PAWR, FillerRatingCap);
        rec.PCTH = Min(rec.PCTH, FillerRatingCap);
        rec.PCAR = Min(rec.PCAR, FillerRatingCap);
        rec.PTHP = Min(rec.PTHP, FillerRatingCap);
        rec.PTHA = Min(rec.PTHA, FillerRatingCap);
        rec.PTAK = Min(rec.PTAK, FillerRatingCap);
        rec.PBTK = Min(rec.PBTK, FillerRatingCap);
        rec.PJMP = Min(rec.PJMP, FillerRatingCap);
    }

    private static byte Min(byte a, byte b) => a < b ? a : b;

    private PlayerRecord CompilePlayer(CanonicalPlayer cp)
    {
        var rec = new PlayerRecord(new byte[DraftClassFile.RecordSize])
        {
            FirstName = cp.Name.First,
            LastName = cp.Name.Last,
            PPOS = _positions.ToPpos(cp.Position),
            TGID = _colleges.TryToTgid(cp.College, out var tgid) ? tgid : CollegeMapper.NotApplicable,
            PYER = EncodeCollegeYear(cp.CollegeYear),
            PRSD = (byte)(cp.Redshirt ? 1 : 0),
        };

        if (cp.Measurables?.HeightIn is double h)
            rec.PHGT = ClampToByte(h);
        if (cp.Measurables?.WeightLb is int w)
            rec.PWGT = ClampToByte(w);

        ApplyRatings(rec, cp);
        return rec;
    }

    private void ApplyRatings(PlayerRecord rec, CanonicalPlayer cp)
    {
        if (_madden is not null
            && _madden.TryFind(cp.Name.First, cp.Name.Last, cp.Position, out var maddenPlayer))
        {
            MaddenToNcaa.ApplyTo(rec, maddenPlayer);
            return;
        }
        ApplyDefaultRatings(rec);
    }

    private static void ApplyDefaultRatings(PlayerRecord rec)
    {
        const byte d = 50;
        rec.PSPD = d; rec.PACC = d; rec.PAGI = d; rec.PSTR = d; rec.PAWR = d;
        rec.PCTH = d; rec.PCAR = d;
        rec.PTHP = d; rec.PTHA = d;
        rec.PKPR = d; rec.PKAC = d;
        rec.PBTK = d; rec.PTAK = d; rec.PIMP = d;
        rec.PPBK = d; rec.PRBK = d; rec.PPOE = d; rec.PTEN = d;
        rec.PJMP = d; rec.PINJ = d; rec.PSTA = d;
        rec.POVR = d;
    }

    private static byte EncodeCollegeYear(string collegeYear) => collegeYear?.ToUpperInvariant() switch
    {
        "FR" => 0,
        "SO" => 1,
        "JR" => 2,
        "SR" => 3,
        "GS" => 4,
        _    => 3,
    };

    private static byte ClampToByte(double value) =>
        (byte)Math.Clamp((int)Math.Round(value), 0, 255);

    private static byte ClampToByte(int value) =>
        (byte)Math.Clamp(value, 0, 255);

    /// <summary>
    /// Real NCAA 08 draft-class files are exactly 138,240 bytes = 270 sectors
    /// of 512 bytes. That's one sector beyond the natural alignment of
    /// 4 (magic) + 1600*86 (records) = 137,604 bytes, which would otherwise
    /// align to 269 sectors. EA's writer always pads to 270; we match that
    /// so produced files are byte-shape-identical to what the game emits.
    /// </summary>
    public const int CanonicalFileSize = 138_240;

    private static byte[] BuildSectorPadTrailer(int playerCount)
    {
        int dataSize = DraftClassFile.FileHeaderSize + playerCount * DraftClassFile.RecordSize;
        int padding = CanonicalFileSize - dataSize;
        return padding > 0 ? new byte[padding] : Array.Empty<byte>();
    }
}
