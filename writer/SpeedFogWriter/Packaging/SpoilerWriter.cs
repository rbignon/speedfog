// writer/SpeedFogWriter/Packaging/SpoilerWriter.cs
using System.Text;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Packaging;

/// <summary>
/// Generates human-readable spoiler logs for SpeedFog runs.
/// </summary>
public static class SpoilerWriter
{
    /// <summary>
    /// Writes a spoiler.txt file describing the generated paths and nodes.
    /// </summary>
    /// <param name="outputDir">The output directory.</param>
    /// <param name="graph">The SpeedFog graph.</param>
    public static void WriteSpoiler(string outputDir, SpeedFogGraph graph)
    {
        var spoilerPath = Path.Combine(outputDir, "spoiler.txt");
        var sb = new StringBuilder();

        sb.AppendLine("=================================================");
        sb.AppendLine("  SpeedFog - Generated Path Spoiler");
        sb.AppendLine($"  Seed: {graph.Seed}");
        sb.AppendLine($"  Generated: {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine("=================================================");
        sb.AppendLine();

        // Build adjacency for path tracing
        var adjacency = BuildAdjacency(graph);

        // Find and print all paths from start to end
        sb.AppendLine("PATH OVERVIEW:");
        sb.AppendLine("--------------");

        var startNode = graph.StartNode;
        if (startNode != null)
        {
            var paths = FindAllPaths(graph, adjacency, startNode.Id);

            for (int i = 0; i < paths.Count; i++)
            {
                sb.AppendLine($"\nBranch {i + 1}:");
                foreach (var nodeId in paths[i])
                {
                    var node = graph.GetNode(nodeId);
                    if (node != null)
                    {
                        var scaling = node.Tier > 0 ? $" [Tier {node.Tier}]" : "";
                        sb.AppendLine($"  -> {node.PrimaryZone}{scaling}");
                    }
                }
            }
        }

        sb.AppendLine();
        sb.AppendLine("=================================================");
        sb.AppendLine("NODE DETAILS:");
        sb.AppendLine("=================================================");

        foreach (var node in graph.AllNodes().OrderBy(n => n.Tier))
        {
            sb.AppendLine();
            sb.AppendLine($"[{node.PrimaryZone}]");
            sb.AppendLine($"  Type: {node.Type}");
            sb.AppendLine($"  Zones: {string.Join(", ", node.Zones)}");
            sb.AppendLine($"  Scaling Tier: {node.Tier}");
            sb.AppendLine($"  Layer: {node.Layer}");
            sb.AppendLine($"  Weight: {node.Weight}");

            var outEdges = graph.GetOutgoingEdges(node.Id).ToList();
            if (outEdges.Any())
            {
                sb.AppendLine($"  Exits:");
                foreach (var edge in outEdges)
                {
                    var target = graph.GetNode(edge.Target);
                    if (target != null)
                    {
                        sb.AppendLine($"    -> {target.PrimaryZone} via {edge.FogId}");
                    }
                }
            }
        }

        File.WriteAllText(spoilerPath, sb.ToString());
    }

    /// <summary>
    /// Builds an adjacency list from the graph edges.
    /// </summary>
    private static Dictionary<string, List<string>> BuildAdjacency(SpeedFogGraph graph)
    {
        var adj = new Dictionary<string, List<string>>();

        foreach (var edge in graph.Edges)
        {
            if (!adj.ContainsKey(edge.Source))
                adj[edge.Source] = new List<string>();
            adj[edge.Source].Add(edge.Target);
        }

        return adj;
    }

    /// <summary>
    /// Finds all paths from start to end using DFS.
    /// </summary>
    private static List<List<string>> FindAllPaths(
        SpeedFogGraph graph,
        Dictionary<string, List<string>> adjacency,
        string startId)
    {
        var endId = graph.EndId;
        var paths = new List<List<string>>();
        var currentPath = new List<string>();

        void Dfs(string nodeId)
        {
            currentPath.Add(nodeId);

            if (nodeId == endId)
            {
                paths.Add(new List<string>(currentPath));
            }
            else if (adjacency.TryGetValue(nodeId, out var neighbors))
            {
                foreach (var neighbor in neighbors)
                {
                    Dfs(neighbor);
                }
            }

            currentPath.RemoveAt(currentPath.Count - 1);
        }

        Dfs(startId);
        return paths;
    }
}
