using NcaaDraftEditor.Core;

namespace NcaaDraftEditor.Cli;

internal static class Program
{
    static int Main(string[] args)
    {
        if (args.Length == 0)
            return Help();

        try
        {
            return args[0] switch
            {
                "dump" => Dump(args[1..]),
                "build" => Build(args[1..]),
                "roundtrip" => Roundtrip(args[1..]),
                "new" => NewTemplate(args[1..]),
                "-h" or "--help" or "help" => Help(),
                _ => Help($"Unknown command: {args[0]}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }

    static int Help(string? error = null)
    {
        if (error is not null) Console.Error.WriteLine(error);
        var writer = error is null ? Console.Out : Console.Error;
        writer.WriteLine("""
            Usage: ncaa-draft <command> [args...]

            Commands:
              dump <in.bin> <out.json>       Read a draft class binary, write its JSON
              build <in.json> <out.bin>      Read JSON, write a draft class binary
              roundtrip <in.bin>             Load -> JSON -> Save; exit 0 iff byte-exact
              new --players N <out.json>     Write a blank JSON template with N empty records
            """);
        return error is null ? 0 : 2;
    }

    static int Dump(string[] args)
    {
        if (args.Length != 2) return Help("dump requires <in.bin> <out.json>");
        var dc = DraftClassFile.Load(args[0]);
        File.WriteAllText(args[1], DraftClassJson.ToJson(dc));
        Console.Error.WriteLine($"Wrote {args[1]}: {dc.Players.Count} players, {dc.Trailer.Length}-byte trailer");
        return 0;
    }

    static int Build(string[] args)
    {
        if (args.Length != 2) return Help("build requires <in.json> <out.bin>");
        var dc = DraftClassJson.FromJson(File.ReadAllText(args[0]));
        dc.Save(args[1]);
        Console.Error.WriteLine($"Wrote {args[1]}: {dc.Players.Count} players, {dc.Trailer.Length}-byte trailer");
        return 0;
    }

    static int Roundtrip(string[] args)
    {
        if (args.Length != 1) return Help("roundtrip requires <in.bin>");
        var original = File.ReadAllBytes(args[0]);
        var dc = DraftClassFile.Load(args[0]);
        var json = DraftClassJson.ToJson(dc);
        var dc2 = DraftClassJson.FromJson(json);
        var temp = Path.GetTempFileName();
        try
        {
            dc2.Save(temp);
            var roundtripped = File.ReadAllBytes(temp);
            if (!original.AsSpan().SequenceEqual(roundtripped))
            {
                Console.Error.WriteLine($"Roundtrip FAILED: {original.Length} vs {roundtripped.Length} bytes");
                return 1;
            }
            Console.Error.WriteLine($"Roundtrip OK: {original.Length} bytes preserved");
            return 0;
        }
        finally
        {
            File.Delete(temp);
        }
    }

    static int NewTemplate(string[] args)
    {
        int players = DraftClassFile.MaxPlayers;
        string? outPath = null;
        for (int i = 0; i < args.Length; i++)
        {
            if (args[i] == "--players" && i + 1 < args.Length)
                players = int.Parse(args[++i]);
            else
                outPath = args[i];
        }
        if (outPath is null) return Help("new requires <out.json>");

        var dc = new DraftClassFile();
        for (int i = 0; i < players; i++)
            dc.Players.Add(new PlayerRecord(new byte[DraftClassFile.RecordSize]));
        File.WriteAllText(outPath, DraftClassJson.ToJson(dc));
        Console.Error.WriteLine($"Wrote {outPath}: {players} empty records, no trailer");
        return 0;
    }
}
