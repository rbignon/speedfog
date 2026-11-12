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
        string? iconsDir = null;
        string? winterDir = null;
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
            else if (args[i] == "--icons-dir")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --icons-dir requires a value");
                    return 1;
                }
                iconsDir = args[++i];
            }
            else if (args[i] == "--winter-dir")
            {
                if (i + 1 >= args.Length)
                {
                    Console.Error.WriteLine("Error: --winter-dir requires a value");
                    return 1;
                }
                winterDir = args[++i];
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

        if (iconsDir != null && !Directory.Exists(iconsDir))
        {
            Console.Error.WriteLine($"Error: icons directory not found: {iconsDir}");
            return 1;
        }

        if (winterDir != null && !Directory.Exists(winterDir))
        {
            Console.Error.WriteLine($"Error: winter directory not found: {winterDir}");
            return 1;
        }

        // The winter build deletes --winter-dir: refuse one that would take another folder with it.
        string? winterConflict = winterDir == null ? null : Winter.WinterModBuilder.CheckWinterDir(
            winterDir, ("game directory", gameDir), ("mod directory", outputDir),
            ("icons directory", iconsDir), ("data directory", dataDir));
        if (winterConflict != null)
        {
            Console.Error.WriteLine($"Error: {winterConflict}");
            return 1;
        }

        Console.WriteLine("=== StaticModBuilder ===");
        Console.WriteLine($"Game dir: {gameDir}");
        Console.WriteLine($"Mod dir:  {outputDir}");

        int total = 0;

        // Grace animation speedup
        total += GraceAnimationPatcher.Patch(gameDir, outputDir);

        // Aging Untouchable payload carriers (animation 3004 bullet events -> judges 150 and 151)
        // and the near-warp marker on the burst (animation 3001)
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

        // Item icons overlay (Halloween + race items)
        if (iconsDir != null && dataDir != null)
        {
            total += ItemIconPatcher.Patch(gameDir, iconsDir, dataDir);
        }
        else
        {
            Console.WriteLine("Note: --icons-dir or --data-dir not provided, skipping item icon overlay");
        }

        // Winter static mod (an input failure is reported on stderr and leaves no folder; exit code stays 0)
        if (winterDir != null && dataDir != null)
        {
            if (Winter.WinterModBuilder.Build(gameDir, winterDir, dataDir))
            {
                total += 1;
            }
        }
        else
        {
            Console.WriteLine("Note: --winter-dir or --data-dir not provided, skipping winter static mod");
        }

        Console.WriteLine($"StaticModBuilder: {total} patch(es) applied");
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"StaticModBuilder - Build SpeedFog's static mod files from vanilla game data

Usage: StaticModBuilder <game-dir> <output-dir> [--data-dir <dir>] [--icons-dir <dir>] [--winter-dir <dir>]

Arguments:
  <game-dir>       Path to Elden Ring Game directory
  <output-dir>     Output directory for patched files (e.g. data/mods/speedfog/)
  --data-dir       SpeedFog data directory (for title_screen_overlay.png and Halloween and race item icon PNGs)
  --icons-dir      Output directory for the item icon overlay (e.g. data/mods/speedfog-icons/), needs --data-dir
  --winter-dir     Output directory for the winter static mod (e.g. data/mods/speedfog-winter/), needs --data-dir; built in a sibling <dir>.building
                   folder and moved into place, removed on failure; must not be or contain another directory argument

Patches applied:
  - Grace animation speedup (chr/c0000.anibnd.dcx)
  - Untouchable payload carriers (chr/c5280.anibnd.dcx, animation 3004: 4 bullet events -> judge 150, 3 -> judge 151;
    animation 3001: near-warp marker SpEffect after the hit)
  - Title screen badge (menu/{hi,low}/01_common.sblytbnd.dcx + 02_title.tpf.dcx, needs --data-dir)
  - Item icon overlay (menu/{hi,low}/05_dummy.tpf.dcx superset, needs --data-dir and --icons-dir)
  - Winter static mod (material/ binders + asset/aet/ TPFs + the authored files under <data-dir>/mods-src/speedfog-winter/,
    report next to the folder, needs --data-dir and --winter-dir)
");
    }
}
