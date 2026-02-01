// writer/SpeedFogWriter/Program.cs
using SpeedFogWriter.Models;

namespace SpeedFogWriter;

class Program
{
    static int Main(string[] args)
    {
        Console.WriteLine("SpeedFogWriter v0.1");

        if (args.Length < 1)
        {
            Console.WriteLine("Usage: SpeedFogWriter <graph.json> [game_dir] [output_dir]");
            return 1;
        }

        var graphPath = args[0];

        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"Error: Graph file not found: {graphPath}");
            return 1;
        }

        try
        {
            // Test graph parsing
            Console.WriteLine($"\nLoading graph: {graphPath}");
            var graph = SpeedFogGraph.Load(graphPath);

            Console.WriteLine($"  Seed: {graph.Seed}");
            Console.WriteLine($"  Total nodes: {graph.TotalNodes}");
            Console.WriteLine($"  Total edges: {graph.Edges.Count}");
            Console.WriteLine($"  Start node: {graph.StartId} ({graph.StartNode?.Type})");
            Console.WriteLine($"  End node: {graph.EndId} ({graph.EndNode?.Type})");

            Console.WriteLine("\nNodes:");
            foreach (var node in graph.AllNodes())
            {
                Console.WriteLine($"  {node.Id}: {node.Type}, zones=[{string.Join(", ", node.Zones)}], tier={node.Tier}");
            }

            Console.WriteLine("\nEdges:");
            foreach (var edge in graph.Edges)
            {
                Console.WriteLine($"  {edge.Source} -> {edge.Target} via {edge.FogId}");
            }

            // Test fog_data parsing if available
            var fogDataPath = Path.Combine(Path.GetDirectoryName(graphPath) ?? ".", "..", "data", "fog_data.json");
            if (File.Exists(fogDataPath))
            {
                Console.WriteLine($"\nLoading fog data: {fogDataPath}");
                var fogData = FogDataFile.Load(fogDataPath);
                Console.WriteLine($"  Fog entries: {fogData.Fogs.Count}");
            }

            // Test clusters parsing if available
            var clustersPath = Path.Combine(Path.GetDirectoryName(graphPath) ?? ".", "..", "data", "clusters.json");
            if (File.Exists(clustersPath))
            {
                Console.WriteLine($"\nLoading clusters: {clustersPath}");
                var clusters = ClusterFile.Load(clustersPath);
                Console.WriteLine($"  Zone maps: {clusters.ZoneMaps.Count}");
                Console.WriteLine($"  Clusters: {clusters.Clusters.Count}");
            }

            Console.WriteLine("\nParsing tests passed!");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            return 1;
        }
    }
}
