namespace StaticModBuilder;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 2 || args.Contains("--help") || args.Contains("-h"))
        {
            PrintUsage();
            return args.Contains("--help") || args.Contains("-h") ? 0 : 1;
        }

        var gameDir = args[0];
        var outputDir = args[1];
        string? dataDir = null;
        string? halloweenDir = null;
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--data-dir")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --data-dir requires a value");
                    return 1;
                }
                dataDir = args[++i];
            }
            else if (args[i] == "--halloween-dir")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --halloween-dir requires a value");
                    return 1;
                }
                halloweenDir = args[++i];
            }
            else
            {
                Console.Error.WriteLine($"Error: unknown argument: {args[i]}");
                return 1;
            }
        }

        if (!Directory.Exists(gameDir))
        {
            Console.Error.WriteLine($"Error: game directory not found: {gameDir}");
            return 1;
        }

        if (!Directory.Exists(outputDir))
        {
            Console.Error.WriteLine($"Error: mod directory not found: {outputDir}");
            return 1;
        }

        if (dataDir != null && !Directory.Exists(dataDir))
        {
            Console.Error.WriteLine($"Error: data directory not found: {dataDir}");
            return 1;
        }

        if (halloweenDir != null && !Directory.Exists(halloweenDir))
        {
            Console.Error.WriteLine($"Error: Halloween directory not found: {halloweenDir}");
            return 1;
        }

        Console.WriteLine("=== StaticModBuilder ===");
        Console.WriteLine($"Game dir: {gameDir}");
        Console.WriteLine($"Mod dir:  {outputDir}");

        int total = 0;

        // Grace animation speedup
        total += GraceAnimationPatcher.Patch(gameDir, outputDir);

        // Aging Untouchable payload carriers (animation 3004 bullet events -> judges 150 and 151)
        total += UntouchableTaePatcher.Patch(gameDir, outputDir);

        // SpeedFog title screen artwork
        if (dataDir != null)
        {
            total += TitleScreenPatcher.Patch(gameDir, outputDir, dataDir);
        }
        else
        {
            Console.WriteLine("Note: --data-dir not provided, skipping title screen patch");
        }

        // Halloween item icons overlay
        if (halloweenDir != null && dataDir != null)
        {
            total += HalloweenIconPatcher.Patch(gameDir, halloweenDir, dataDir);
        }
        else
        {
            Console.WriteLine("Note: --halloween-dir or --data-dir not provided, skipping Halloween icon overlay");
        }

        Console.WriteLine($"StaticModBuilder: {total} patch(es) applied");
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"StaticModBuilder - Build SpeedFog's static mod files from vanilla game data

Usage: StaticModBuilder <game-dir> <output-dir> [--data-dir <dir>] [--halloween-dir <dir>]

Arguments:
  <game-dir>       Path to Elden Ring Game directory
  <output-dir>     Output directory for patched files (e.g. data/mods/speedfog/)
  --data-dir       SpeedFog data directory (for title_screen_overlay.png and Halloween icon PNGs)
  --halloween-dir  Output directory for the Halloween icon overlay (e.g. data/mods/speedfog-halloween/), needs --data-dir

Patches applied:
  - Grace animation speedup (chr/c0000.anibnd.dcx)
  - Untouchable payload carriers (chr/c5280.anibnd.dcx, animation 3004: 4 bullet events -> judge 150, 3 -> judge 151)
  - Title screen badge (menu/{hi,low}/01_common.sblytbnd.dcx + 02_title.tpf.dcx, needs --data-dir)
  - Halloween icon overlay (menu/{hi,low}/05_dummy.tpf.dcx superset, needs --data-dir and --halloween-dir)
");
    }
}
