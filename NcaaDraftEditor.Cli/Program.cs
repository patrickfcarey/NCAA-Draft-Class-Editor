using NcaaDraftEditor.Canonical;
using NcaaDraftEditor.Compiler;
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
                "compile" => Compile(args[1..]),
                "compile-roster" => CompileRoster(args[1..]),
                "compile-franchise" => CompileFranchise(args[1..]),
                "-h" or "--help" or "help" => Help(),
                _ => Help($"Unknown command: {args[0]}"),
            };
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("NCAA_DRAFT_VERBOSE") == "1")
                Console.Error.WriteLine(ex.ToString());
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
              dump <in.bin> <out.json>             Read a draft class binary, write its JSON
              build <in.json> <out.bin>            Read JSON, write a draft class binary
              roundtrip <in.bin>                   Load -> JSON -> Save; exit 0 iff byte-exact
              new --players N <out.json>           Write a blank JSON template with N empty records
              compile <canonical.json> <positions.json> <colleges.json> <out.bin>
                      [--madden <path>] [--filler <path>] [--lock-draft-order]
                                                   Compile canonical real-world draft class to binary.
                                                   --filler fills slots beyond your real picks with
                                                   valid records from an existing draft class file;
                                                   without it Madden 08 hangs on empty padding.
                                                   --lock-draft-order applies a pick-number-based
                                                   OVR floor (pick 1 -> 90, pick 256 -> 50) so the
                                                   in-game draft order matches the real one.
              compile-roster <canonical-roster.json> <positions.json> <template.bin> <out.bin>
                                                   Apply a canonical NFL roster onto a Madden 08
                                                   roster template. Mutates PLAY records by TGID;
                                                   leaves TEAM/DCHT/INJY tables untouched. Use
                                                   tests/fixtures/madden08-roster-sample.bin as the
                                                   template.
              compile-franchise <template.bin> <out.bin> --year YYYY --caps <salary-caps.json>
                                                   [--base-year YYYY]
                                                   Mutate a Madden franchise save to target NFL
                                                   season YYYY. Writes SEAI.SEYR (calendar) and
                                                   SLRI.SCAD/SMAD/RFA1..4 (cap economy). Default
                                                   base year is 2007 (M08); pass --base-year 2008
                                                   for M09 or 2011 for M12.

            Set NCAA_DRAFT_VERBOSE=1 to print full stack traces on errors.
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

    static int Compile(string[] args)
    {
        if (args.Length < 4)
            return Help("compile requires <canonical.json> <positions.json> <colleges.json> <out.bin> [--madden <path>] [--filler <path>]");

        var canonical = CanonicalJson.Load(args[0]);
        var positions = PositionMapper.LoadFile(args[1]);
        var colleges = CollegeMapper.LoadFile(args[2]);
        var outPath = args[3];

        MaddenRoster? madden = null;
        DraftClassFile? filler = null;
        bool lockDraftOrder = false;
        for (int i = 4; i < args.Length; i++)
        {
            if (args[i] == "--madden" && i + 1 < args.Length)
                madden = MaddenRoster.LoadFile(args[++i]);
            else if (args[i] == "--filler" && i + 1 < args.Length)
                filler = DraftClassFile.Load(args[++i]);
            else if (args[i] == "--lock-draft-order")
                lockDraftOrder = true;
            else
                return Help($"Unexpected argument: {args[i]}");
        }

        if (filler is null)
            Console.Error.WriteLine(
                "WARNING: --filler not provided. Madden 08 hangs on empty player records; " +
                "pass --filler tests/fixtures/sample.bin (or any real NCAA draft class file) " +
                "to fill slots beyond your canonical picks with valid records.");

        var compiler = new DraftClassCompiler(positions, colleges, madden, filler, lockDraftOrder);
        var dc = compiler.Compile(canonical);
        dc.Save(outPath);

        var realPlayers = canonical.Players.Count;
        Console.Error.WriteLine(
            $"Wrote {outPath}: {realPlayers} real players from canonical, " +
            $"padded to {dc.Players.Count} records, {dc.Trailer.Length}-byte trailer " +
            $"(madden={(madden is null ? "none" : "loaded")}, " +
            $"filler={(filler is null ? "none" : $"loaded {filler.Players.Count} records")}, " +
            $"lock-draft-order={lockDraftOrder})");
        return 0;
    }

    static int CompileRoster(string[] args)
    {
        if (args.Length != 4)
            return Help("compile-roster requires <canonical-roster.json> <positions.json> <template.bin> <out.bin>");

        var rosterJson = File.ReadAllText(args[0]);
        var canonical = CanonicalJson.Deserialize<CanonicalRoster>(rosterJson);
        var positions = PositionMapper.LoadFile(args[1]);
        var template = MaddenTdb.LoadFile(args[2]);
        var outPath = args[3];

        var compiler = new MaddenRosterCompiler(positions);
        compiler.Compile(canonical, template);
        template.SaveFile(outPath);

        var play = template.FindTable("PLAY");
        int totalCanonicalPlayers = canonical.Teams.Sum(t => t.Players.Count);
        int totalTemplateSlots = play?.Records.Count ?? 0;
        Console.Error.WriteLine(
            $"Wrote {outPath}: {canonical.Teams.Count} teams, " +
            $"{totalCanonicalPlayers} canonical players supplied, " +
            $"{totalTemplateSlots} template PLAY slots available");
        return 0;
    }

    static int CompileFranchise(string[] args)
    {
        if (args.Length < 2)
            return Help("compile-franchise requires <template.bin> <out.bin> --year YYYY --caps <salary-caps.json> [--base-year YYYY] [--no-contracts] [--contracts <contracts.json>]");
        var templatePath = args[0];
        var outPath = args[1];
        int year = -1;
        string? capsPath = null;
        string? contractsPath = null;
        int baseYear = MaddenFranchiseCompiler.M08BaseYear;
        bool synthesizeContracts = true;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--year" && i + 1 < args.Length) year = int.Parse(args[++i]);
            else if (args[i] == "--caps" && i + 1 < args.Length) capsPath = args[++i];
            else if (args[i] == "--contracts" && i + 1 < args.Length) contractsPath = args[++i];
            else if (args[i] == "--base-year" && i + 1 < args.Length) baseYear = int.Parse(args[++i]);
            else if (args[i] == "--no-contracts") synthesizeContracts = false;
            else return Help($"Unexpected argument: {args[i]}");
        }
        if (year < 0) return Help("compile-franchise requires --year YYYY");
        if (capsPath is null) return Help("compile-franchise requires --caps <salary-caps.json>");

        var caps = SalaryCapTable.LoadFile(capsPath);
        var template = MaddenTdb.LoadFile(templatePath);
        var compiler = new MaddenFranchiseCompiler(baseYear, caps);
        CanonicalContracts? contracts = contractsPath is not null
            ? CanonicalContracts.LoadFile(contractsPath)
            : null;
        compiler.Compile(year, template, synthesizeContracts, contracts);
        template.SaveFile(outPath);

        var seai = template.FindTable("SEAI")!.Records[0];
        var slri = template.FindTable("SLRI")!.Records[0];
        var play = template.FindTable("PLAY");
        long totalCapHit = 0;
        int contracted = 0;
        if (synthesizeContracts && play is not null && play.FindField("PCSA") is not null)
        {
            foreach (var r in play.Records)
            {
                long cap10k = r.GetUInt("PCSA");
                if (cap10k > 0) { totalCapHit += cap10k * 10_000L; contracted++; }
            }
        }
        string contractMsg;
        if (!synthesizeContracts)
        {
            contractMsg = " | contracts: not synthesized";
        }
        else
        {
            var s = compiler.ContractStats;
            int matched = s?.MatchedReal ?? 0;
            int synth = s?.SynthesizedFallback ?? 0;
            contractMsg = $" | contracts: {matched} real / {synth} synthesized" +
                          $" ({contracted} non-zero, total ${totalCapHit:N0}," +
                          $" vs 32 teams × ${slri.GetUInt("SCAD"):N0} = ${32L * slri.GetUInt("SCAD"):N0})";
        }
        Console.Error.WriteLine(
            $"Wrote {outPath}: target NFL season {year}, base year {baseYear}, " +
            $"SEYR={seai.GetUInt("SEYR")} SCAD=${slri.GetUInt("SCAD"):N0} " +
            $"SMAD=${slri.GetUInt("SMAD"):N0}" + contractMsg);
        return 0;
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
