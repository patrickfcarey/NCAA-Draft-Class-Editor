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

    public DraftClassCompiler(PositionMapper positions, CollegeMapper colleges, MaddenRoster? madden = null)
    {
        _positions = positions;
        _colleges = colleges;
        _madden = madden;
    }

    public DraftClassFile Compile(CanonicalDraftClass canonical)
    {
        var dc = new DraftClassFile();
        foreach (var player in canonical.Players)
        {
            if (dc.Players.Count >= DraftClassFile.MaxPlayers) break;
            dc.Players.Add(CompilePlayer(player));
        }
        PadToMaxPlayers(dc);
        dc.Trailer = BuildSectorPadTrailer(dc.Players.Count);
        return dc;
    }

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

    private static void PadToMaxPlayers(DraftClassFile dc)
    {
        while (dc.Players.Count < DraftClassFile.MaxPlayers)
            dc.Players.Add(new PlayerRecord(new byte[DraftClassFile.RecordSize]));
    }

    /// <summary>
    /// Real NCAA 08 files end on a 512-byte sector boundary - 4 magic + 1600*86 +
    /// 636 zero bytes = 138,240 = 270 sectors. We emit the same trailer so the
    /// produced file matches the format the game expects byte-for-byte.
    /// </summary>
    private static byte[] BuildSectorPadTrailer(int playerCount)
    {
        const int sectorSize = 512;
        int dataSize = DraftClassFile.FileHeaderSize + playerCount * DraftClassFile.RecordSize;
        int aligned = (dataSize + sectorSize - 1) / sectorSize * sectorSize;
        return new byte[aligned - dataSize];
    }
}
