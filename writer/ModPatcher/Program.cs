namespace ModPatcher;

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
        var modDir = args[1];

        if (!Directory.Exists(gameDir))
        {
            Console.Error.WriteLine($"Error: game directory not found: {gameDir}");
            return 1;
        }

        if (!Directory.Exists(modDir))
        {
            Console.Error.WriteLine($"Error: mod directory not found: {modDir}");
            return 1;
        }

        Console.WriteLine("=== ModPatcher ===");
        Console.WriteLine($"Game dir: {gameDir}");
        Console.WriteLine($"Mod dir:  {modDir}");

        int total = 0;

        // Grace animation speedup
        total += GraceAnimationPatcher.Patch(gameDir, modDir);

        Console.WriteLine($"ModPatcher: {total} patch(es) applied");
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"ModPatcher - Post-processing patches for SpeedFog

Usage: ModPatcher <game-dir> <mod-dir>

Arguments:
  <game-dir>   Path to Elden Ring Game directory
  <mod-dir>    Path to mod output directory (mods/fogmod/)

Patches applied:
  - Grace animation speedup (c0000.anibnd.dcx)
");
    }
}
