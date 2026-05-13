using System.Text;
namespace NcaaDraftEditor.Core;
public sealed class DraftClassFile
{
    public static readonly byte[] MagicHeader = { 0x46, 0x00, 0x40, 0x06 };
    public const int FileHeaderSize = 4;
    public const int RecordSize = 86;
    public string? SourcePath { get; private set; }
    public List<PlayerRecord> Players { get; } = new();
    public static DraftClassFile Load(string filePath)
    {
        var bytes = File.ReadAllBytes(filePath);
        if (bytes.Length < FileHeaderSize + RecordSize)
            throw new InvalidDataException("File too small for a draft class.");
        var dc = new DraftClassFile { SourcePath = filePath };
        int offset = FileHeaderSize;
        int playerCount = 0;
        while (offset + RecordSize <= bytes.Length)
        {
            var slice = new byte[RecordSize];
            Buffer.BlockCopy(bytes, offset, slice, 0, RecordSize);
            dc.Players.Add(new PlayerRecord(slice));
            offset += RecordSize;
            playerCount++;
            if (playerCount >= 1600) break; // Safety limit to prevent excessive memory usage
        }
        return dc;
    }
    public void Save(string filePath)
    {
        using var ms = new MemoryStream();
        ms.Write(MagicHeader, 0, MagicHeader.Length);
        foreach (var p in Players) ms.Write(p.Raw, 0, p.Raw.Length);
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
