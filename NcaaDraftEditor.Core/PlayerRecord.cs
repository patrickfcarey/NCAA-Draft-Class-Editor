using System.Text;
namespace NcaaDraftEditor.Core;
public sealed class PlayerRecord
{
    public byte[] Raw { get; }
    public PlayerRecord(byte[] raw)
    {
        if (raw.Length != DraftClassFile.RecordSize)
            throw new ArgumentException("Record must be exactly 86 bytes.");
        Raw = raw;
    }
    public string FirstName { get => DecodeAscii(Raw.AsSpan(FieldMap.FirstNameOffset, FieldMap.FirstNameLength)); set => EncodeAscii(value, Raw, FieldMap.FirstNameOffset, FieldMap.FirstNameLength); }
    public string LastName  { get => DecodeAscii(Raw.AsSpan(FieldMap.LastNameOffset,  FieldMap.LastNameLength));  set => EncodeAscii(value, Raw, FieldMap.LastNameOffset,  FieldMap.LastNameLength);  }
    public byte PFMP { get => Raw[0]; set => Raw[0] = value; }
    public byte RandomSeed { get => Raw[2]; set => Raw[2] = value; }
    public byte TGIDBucket { get => Raw[3]; set => Raw[3] = value; }
    public byte TGID { get => Raw[4]; set => Raw[4] = value; }
    public byte PYER { get => Raw[31]; set => Raw[31] = value; }
    public byte PRSD { get => Raw[32]; set => Raw[32] = value; }
    public byte POVR { get => Raw[33]; set => Raw[33] = value; }
    public byte PJEN { get => Raw[34]; set => Raw[34] = value; }
    public byte PPOS { get => Raw[35]; set => Raw[35] = value; }
    public byte PWGT { get => Raw[36]; set => Raw[36] = value; }
    public byte PHGT { get => Raw[37]; set => Raw[37] = value; }
    public byte PSTR { get => Raw[38]; set => Raw[38] = value; }
    public byte PAGI { get => Raw[39]; set => Raw[39] = value; }
    public byte PSPD { get => Raw[40]; set => Raw[40] = value; }
    public byte PACC { get => Raw[41]; set => Raw[41] = value; }
    public byte PAWR { get => Raw[42]; set => Raw[42] = value; }
    public byte PCTH { get => Raw[43]; set => Raw[43] = value; }
    public byte PCAR { get => Raw[44]; set => Raw[44] = value; }
    public byte PTHP { get => Raw[45]; set => Raw[45] = value; }
    public byte PTHA { get => Raw[46]; set => Raw[46] = value; }
    public byte PKPR { get => Raw[47]; set => Raw[47] = value; }
    public byte PKAC { get => Raw[48]; set => Raw[48] = value; }
    public byte PBTK { get => Raw[49]; set => Raw[49] = value; }
    public byte PTAK { get => Raw[50]; set => Raw[50] = value; }
    public byte PIMP { get => Raw[51]; set => Raw[51] = value; }
    public byte PPBK { get => Raw[52]; set => Raw[52] = value; }
    public byte PRBK { get => Raw[53]; set => Raw[53] = value; }
    public byte PPOE { get => Raw[54]; set => Raw[54] = value; }
    public byte PTEN { get => Raw[55]; set => Raw[55] = value; }
    public byte PJMP { get => Raw[56]; set => Raw[56] = value; }
    public byte PINJ { get => Raw[57]; set => Raw[57] = value; }
    public byte PSTA { get => Raw[58]; set => Raw[58] = value; }
    public byte PHED { get => Raw[60]; set => Raw[60] = value; }
    public byte PHCL { get => Raw[61]; set => Raw[61] = value; }
    public byte PSKI { get => Raw[62]; set => Raw[62] = value; }
    public byte HELM { get => Raw[63]; set => Raw[63] = value; }
    public byte PFMK { get => Raw[64]; set => Raw[64] = value; }
    public byte PVIS { get => Raw[65]; set => Raw[65] = value; }
    public byte PEYE { get => Raw[66]; set => Raw[66] = value; }
    public byte PBRE { get => Raw[67]; set => Raw[67] = value; }
    public byte PNEK { get => Raw[68]; set => Raw[68] = value; }
    public byte PREB { get => Raw[71]; set => Raw[71] = value; }
    public byte PLEB { get => Raw[72]; set => Raw[72] = value; }
    public byte PSLT { get => Raw[74]; set => Raw[74] = value; }
    public byte PSLO { get => Raw[75]; set => Raw[75] = value; }
    public byte PRWS { get => Raw[76]; set => Raw[76] = value; }
    public byte PLWS { get => Raw[77]; set => Raw[77] = value; }
    public byte PRHN { get => Raw[78]; set => Raw[78] = value; }
    public byte PLHN { get => Raw[79]; set => Raw[79] = value; }
    public byte PRSH { get => Raw[80]; set => Raw[80] = value; }
    public byte PLSH { get => Raw[81]; set => Raw[81] = value; }
    public byte PMSH { get => Raw[82]; set => Raw[82] = value; }
    public byte PFSH { get => Raw[83]; set => Raw[83] = value; }
    //public byte Unused84 { get => Raw[84]; set => Raw[84] = value; }
    public byte PSSH { get => Raw[85]; set => Raw[85] = value; }

    static string DecodeAscii(ReadOnlySpan<byte> span)
    {
        int zero = span.IndexOf((byte)0);
        var s = zero >= 0 ? span.Slice(0, zero) : span;
        return Encoding.ASCII.GetString(s).Trim();
    }
    static void EncodeAscii(string? value, byte[] dest, int offset, int maxLen)
    {
        var s = (value ?? "").Trim();
        var bytes = Encoding.ASCII.GetBytes(s);
        int n = Math.Min(bytes.Length, maxLen);
        Array.Clear(dest, offset, maxLen);
        Buffer.BlockCopy(bytes, 0, dest, offset, n);
    }
}
