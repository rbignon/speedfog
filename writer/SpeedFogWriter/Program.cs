// writer/SpeedFogWriter/Program.cs
using SpeedFogWriter.Models;
using SpeedFogWriter.Writers;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("SpeedFogWriter v0.1");

        if (args.Length < 3)
        {
            Console.WriteLine("Usage: SpeedFogWriter <graph.json> <game_dir> <output_dir> [--data-dir <path>]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  graph.json  - Path to graph from speedfog-core");
            Console.WriteLine("  game_dir    - Path to Elden Ring Game folder");
            Console.WriteLine("  output_dir  - Output directory for mod files");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --data-dir  - Path to data directory (default: ../data)");
            return 1;
        }

        var graphPath = args[0];
        var gameDir = args[1];
        var outputDir = args[2];

        var dataDir = PathHelper.GetDataDir();
        for (int i = 3; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir")
                dataDir = args[i + 1];
        }

        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"Error: Graph not found: {graphPath}");
            return 1;
        }

        if (!Directory.Exists(gameDir))
        {
            Console.Error.WriteLine($"Error: Game directory not found: {gameDir}");
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
            var writer = new ModWriter(gameDir, outputDir, dataDir, graph);
            writer.Generate();

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
}
