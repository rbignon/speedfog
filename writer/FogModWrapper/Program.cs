using FogModWrapper;
using FogModWrapper.Pipeline;

class Program
{
    static int Main(string[] args)
    {
        try
        {
            var config = ParseArgs(args);
            Run(config);
            return 0;
        }
        catch (AggregateException ex)
        {
            // Parallel.ForEach wraps worker exceptions; report each inner
            // one directly or field failures become undiagnosable.
            foreach (var inner in ex.Flatten().InnerExceptions)
            {
                Console.Error.WriteLine($"Error: {inner.Message}");
                if (Environment.GetEnvironmentVariable("DEBUG") != null)
                {
                    Console.Error.WriteLine(inner.StackTrace);
                }
            }
            return 1;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("DEBUG") != null)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }

    static Config ParseArgs(string[] args)
    {
        var config = new Config();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--game-dir":
                    config.GameDir = args[++i];
                    break;
                case "--data-dir":
                    config.DataDir = args[++i];
                    break;
                case "-o":
                case "--output":
                    config.OutputDir = args[++i];
                    break;
                case "--merge-dir":
                    config.MergeDir = args[++i];
                    break;
                case "-h":
                case "--help":
                    PrintUsage();
                    Environment.Exit(0);
                    break;
                default:
                    if (arg.StartsWith("-"))
                    {
                        throw new ArgumentException($"Unknown option: {arg}");
                    }
                    config.SeedDir = arg;
                    break;
            }
        }

        // Validate required args
        if (string.IsNullOrEmpty(config.SeedDir))
            throw new ArgumentException("Missing required argument: seed directory");
        if (!File.Exists(config.GraphPath))
            throw new ArgumentException($"graph.json not found in seed directory: {config.GraphPath}");
        if (string.IsNullOrEmpty(config.GameDir))
            throw new ArgumentException("Missing required argument: --game-dir");
        if (string.IsNullOrEmpty(config.DataDir))
            throw new ArgumentException("Missing required argument: --data-dir");
        if (string.IsNullOrEmpty(config.OutputDir))
            throw new ArgumentException("Missing required argument: -o/--output");

        return config;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"FogModWrapper - SpeedFog mod generator using FogMod.dll

Usage: FogModWrapper <seed_dir> --game-dir <path> --data-dir <path> -o <output> [--merge-dir <path>]

Arguments:
  <seed_dir>         Path to seed directory (contains graph.json, spoiler.txt)
  --game-dir <path>  Path to Elden Ring Game directory
  --data-dir <path>  Path to data directory (fog.txt, fogevents.txt, er-common.emedf.json)
  -o, --output       Output directory for generated mod files
  --merge-dir <path> Optional: Path to Item Randomizer output to merge

Example:
  FogModWrapper seeds/123456 --game-dir ""C:/Games/ELDEN RING/Game"" --data-dir data -o output
  FogModWrapper seeds/123456 --game-dir ""C:/Games/ELDEN RING/Game"" --data-dir data -o output --merge-dir temp/item-randomizer
");
    }

    static void Run(Config config)
    {
        PrintBanner(config);

        var ctx = new Context(config);
        Directory.CreateDirectory(ctx.ModDir);

        var timer = new PhaseTimer();
        try
        {
            timer.Phase("Load inputs");
            LoadInputsPhase.Run(ctx);
            timer.Phase("Construct graph");
            ConstructGraphPhase.Run(ctx);
            timer.Phase("Inject connections");
            InjectConnectionsPhase.Run(ctx);
            timer.Phase("Write fogmod");
            WriteFogModPhase.Run(ctx);
            timer.Phase("Patch EMEVD");
            PatchEmevdPhase.Run(ctx);
            timer.Phase("Common injectors");
            CommonEmevdPhase.Run(ctx);
            timer.Phase("Regulation");
            RegulationPhase.Run(ctx);
            timer.Phase("Mod-dir injectors");
            ModDirPhase.Run(ctx);
        }
        finally
        {
            // Printed even on failure: a phase that hangs or dies slowly is
            // exactly the one worth seeing in the breakdown.
            var total = timer.Stop();
            Console.WriteLine();
            Console.WriteLine($"Timing breakdown ({total.ToString("F2", System.Globalization.CultureInfo.InvariantCulture)}s total):");
            Console.WriteLine(timer.FormatSummary());
        }

        // Final ME3 packaging is handled by the Python speedfog pipeline.
    }

    static void PrintBanner(Config config)
    {
        Console.WriteLine("=== FogModWrapper ===");
        Console.WriteLine($"Seed dir: {config.SeedDir}");
        Console.WriteLine($"Game dir: {config.GameDir}");
        Console.WriteLine($"Data dir: {config.DataDir}");
        Console.WriteLine($"Output: {config.OutputDir}");
        if (!string.IsNullOrEmpty(config.MergeDir))
        {
            Console.WriteLine($"Merge dir: {config.MergeDir}");
        }
        Console.WriteLine();
    }
}
