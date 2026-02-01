// writer/SpeedFogWriter/Program.cs
using SpeedFogWriter.Models;
using SpeedFogWriter.Writers;
using SpeedFogWriter.Helpers;
using SpeedFogWriter.Packaging;

namespace SpeedFogWriter;

class Program
{
    static async Task<int> Main(string[] args)
    {
        Console.WriteLine("SpeedFogWriter v0.1");

        if (args.Length < 3)
        {
            PrintUsage();
            return 1;
        }

        var options = ParseOptions(args);

        if (!Directory.Exists(options.SeedDir))
        {
            Console.Error.WriteLine($"Error: Seed directory not found: {options.SeedDir}");
            return 1;
        }

        var graphPath = Path.Combine(options.SeedDir, "graph.json");
        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"Error: graph.json not found in: {options.SeedDir}");
            return 1;
        }

        if (!Directory.Exists(options.GameDir))
        {
            Console.Error.WriteLine($"Error: Game directory not found: {options.GameDir}");
            return 1;
        }

        try
        {
            Console.WriteLine($"Loading graph: {graphPath}");
            var graph = SpeedFogGraph.Load(graphPath);
            Console.WriteLine($"  Seed: {graph.Seed}");
            Console.WriteLine($"  Nodes: {graph.TotalNodes}");
            Console.WriteLine($"  Edges: {graph.Edges.Count}");

            if (graph.StartNode == null || graph.EndNode == null)
            {
                Console.Error.WriteLine("Error: Graph missing start or end node");
                return 1;
            }

            Console.WriteLine();
            var writer = new ModWriter(options.GameDir, options.OutputDir, options.DataDir, graph);
            writer.Generate();

            // Copy spoiler.txt from seed directory if it exists
            var spoilerSource = Path.Combine(options.SeedDir, "spoiler.txt");
            if (File.Exists(spoilerSource))
            {
                var spoilerDest = Path.Combine(options.OutputDir, "spoiler.txt");
                File.Copy(spoilerSource, spoilerDest, overwrite: true);
                Console.WriteLine("Copied spoiler.txt from seed directory");
            }
            else
            {
                Console.WriteLine("Note: spoiler.txt not found (use --spoiler when generating DAG)");
            }

            // Phase 4: Packaging
            if (options.Package)
            {
                var packager = new PackagingWriter(options.OutputDir);
                await packager.WritePackageAsync(options.UpdateModEngine);
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("SPEEDFOG_DEBUG") != null)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage: SpeedFogWriter <seed_dir> <game_dir> <output_dir> [options]");
        Console.WriteLine();
        Console.WriteLine("Arguments:");
        Console.WriteLine("  seed_dir    - Path to seed directory (contains graph.json, spoiler.txt)");
        Console.WriteLine("  game_dir    - Path to Elden Ring Game folder");
        Console.WriteLine("  output_dir  - Output directory for mod files");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --data-dir <path>   - Path to data directory (default: ../data)");
        Console.WriteLine("  --no-package        - Skip ModEngine packaging (output mod files only)");
        Console.WriteLine("  --update-modengine  - Force re-download of ModEngine 2");
    }

    private static WriterOptions ParseOptions(string[] args)
    {
        var options = new WriterOptions
        {
            SeedDir = args[0],
            GameDir = args[1],
            OutputDir = args[2],
            DataDir = PathHelper.GetDataDir()
        };

        for (int i = 3; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--data-dir" when i + 1 < args.Length:
                    options.DataDir = args[++i];
                    break;
                case "--no-package":
                    options.Package = false;
                    break;
                case "--update-modengine":
                    options.UpdateModEngine = true;
                    break;
            }
        }

        return options;
    }
}

/// <summary>
/// CLI options for SpeedFogWriter.
/// </summary>
internal class WriterOptions
{
    public string SeedDir { get; set; } = "";
    public string GameDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string DataDir { get; set; } = "";
    public bool Package { get; set; } = true;
    public bool UpdateModEngine { get; set; } = false;
}
