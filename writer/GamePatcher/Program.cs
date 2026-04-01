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

        Console.WriteLine("=== GamePatcher ===");
        Console.WriteLine($"Game dir: {gameDir}");
        Console.WriteLine($"Mod dir:  {outputDir}");

        int total = 0;

        // Grace animation speedup
        total += GraceAnimationPatcher.Patch(gameDir, outputDir);

        Console.WriteLine($"GamePatcher: {total} patch(es) applied");
        return 0;
    }

    static void PrintUsage()
    {
        Console.WriteLine(@"GamePatcher - Pre-process game files for SpeedFog

Usage: GamePatcher <game-dir> <output-dir>

Arguments:
  <game-dir>    Path to Elden Ring Game directory
  <output-dir>  Output directory for patched files (e.g. data/overlay/)

Patches applied:
  - Grace animation speedup (chr/c0000.anibnd.dcx)
");
    }
}
