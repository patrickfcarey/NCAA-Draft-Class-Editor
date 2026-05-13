using System.Text;
namespace NcaaDraftEditor.Core;
public sealed class DraftClassFile
{
    public static readonly byte[] MagicHeader = { 0x46, 0x00, 0x40, 0x06 };
    public const int FileHeaderSize = 4;
    public const int RecordSize = 86;
    public const int MaxPlayers = 1600;
    public string? SourcePath { get; private set; }
    public List<PlayerRecord> Players { get; } = new();
    // Any bytes after the last 86-byte record (e.g. sector-alignment zero padding).
    // Real NCAA 08 files end with 636 zero bytes padding the file to a 512-byte boundary.
    public byte[] Trailer { get; set; } = Array.Empty<byte>();
    public static DraftClassFile Load(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < FileHeaderSize + RecordSize)
            throw new InvalidDataException("File too small for a draft class.");
        var dc = new DraftClassFile { SourcePath = filePath };
        int offset = FileHeaderSize;
        while (offset + RecordSize <= bytes.Length && dc.Players.Count < MaxPlayers)
        {
            var slice = new byte[RecordSize];
            Buffer.BlockCopy(bytes, offset, slice, 0, RecordSize);
            dc.Players.Add(new PlayerRecord(slice));
            offset += RecordSize;
        }
        int trailerLen = bytes.Length - offset;
        if (trailerLen > 0)
        {
            dc.Trailer = new byte[trailerLen];
            Buffer.BlockCopy(bytes, offset, dc.Trailer, 0, trailerLen);
        }
        return dc;
    }
    public void Save(string filePath)
    {
        using var ms = new MemoryStream();
        ms.Write(MagicHeader, 0, MagicHeader.Length);
        foreach (var p in Players) ms.Write(p.Raw, 0, p.Raw.Length);
        if (Trailer.Length > 0) ms.Write(Trailer, 0, Trailer.Length);
        File.WriteAllBytes(filePath, ms.ToArray());
        SourcePath = filePath;
    }
    public void ExportCsv(string filePath)
    {
        using var sw = new StreamWriter(filePath, false, Encoding.UTF8);
        for (int i = 0; i < RecordSize; i++) { if (i>0) sw.Write(','); sw.Write($"byte_{i:00}"); } sw.WriteLine();
        for (int i = 0; i < RecordSize; i++) { if (i>0) sw.Write(','); sw.Write(Escape(FieldMap.GetName(i))); } sw.WriteLine();
        foreach (var p in Players)
        {
            for (int i = 0; i < RecordSize; i++)
            {
                if (i>0) sw.Write(',');
                if (i == FieldMap.FirstNameOffset) sw.Write(Escape(p.FirstName));
                else if (i == FieldMap.LastNameOffset) sw.Write(Escape(p.LastName));
                else sw.Write(p.Raw[i].ToString());
            }
            sw.WriteLine();
        }
        static string Escape(string? s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            if (s.Contains(',') || s.Contains('\"') || s.Contains('\n') || s.Contains('\r'))
                return "\"" + s.Replace("\"", "\"\"") + "\"";
            return s;
        }
    }
}
