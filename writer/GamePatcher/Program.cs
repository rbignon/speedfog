namespace GamePatcher;

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

        Console.WriteLine("=== GamePatcher ===");
        Console.WriteLine($"Game dir: {gameDir}");
        Console.WriteLine($"Mod dir:  {outputDir}");

        int total = 0;

        // Grace animation speedup
        total += GraceAnimationPatcher.Patch(gameDir, outputDir);

        // SpeedFog title screen artwork
        if (dataDir != null)
        {
            total += TitleScreenPatcher.Patch(gameDir, outputDir, dataDir);
        }
        else
        {
            Console.WriteLine("Note: --data-dir not provided, skipping title screen patch");
        }

        Console.WriteLine($"GamePatcher: {total} patch(es) applied");
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"GamePatcher - Pre-process game files for SpeedFog

Usage: GamePatcher <game-dir> <output-dir> [--data-dir <dir>]

Arguments:
  <game-dir>    Path to Elden Ring Game directory
  <output-dir>  Output directory for patched files (e.g. data/overlay/)
  --data-dir    SpeedFog data directory (for title_screen_overlay.png)

Patches applied:
  - Grace animation speedup (chr/c0000.anibnd.dcx)
  - Title screen artwork (menu/{hi,low}/01_common.tpf.dcx, needs --data-dir)
");
    }
}
