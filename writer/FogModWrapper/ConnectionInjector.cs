using FogMod;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Injects SpeedFog's graph connections into FogMod's Graph object.
/// </summary>
public static class ConnectionInjector
{
    /// <summary>
    /// Inject connections from SpeedFog's graph.json into FogMod's Graph.
    /// </summary>
    /// <param name="graph">FogMod Graph with nodes/edges constructed but not connected</param>
    /// <param name="connections">List of connections from graph.json</param>
    public static void Inject(Graph graph, List<Connection> connections)
    {
        Console.WriteLine($"Injecting {connections.Count} connections...");

        foreach (var conn in connections)
        {
            try
            {
                ConnectEdges(graph, conn);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect: {conn}\n{ex.Message}", ex);
            }
        }

        Console.WriteLine("All connections injected successfully.");
    }

    /// <summary>
    /// Connect a single exit edge to an entrance edge.
    /// </summary>
    private static void ConnectEdges(Graph graph, Connection conn)
    {
        // Find the exit edge in the source area's To list
        if (!graph.Nodes.TryGetValue(conn.ExitArea, out var exitNode))
        {
            throw new Exception($"Exit area not found: {conn.ExitArea}");
        }

        var exitEdge = exitNode.To.Find(e => e.Name == conn.ExitGate);
        if (exitEdge == null)
        {
            var available = string.Join(", ", exitNode.To.Select(e => e.Name));
            throw new Exception($"Exit edge not found: {conn.ExitGate} in {conn.ExitArea}\nAvailable: {available}");
        }

        // Find the entrance edge in the destination area
        if (!graph.Nodes.TryGetValue(conn.EntranceArea, out var entranceNode))
        {
            throw new Exception($"Entrance area not found: {conn.EntranceArea}");
        }

        Graph.Edge? entranceEdge = null;

        // Strategy 1: For bidirectional fogs, find via To + Pair
        // The entrance gate name refers to an exit edge on the destination side,
        // whose Pair is the entrance edge we want
        var destExitEdge = entranceNode.To.Find(e => e.Name == conn.EntranceGate);
        if (destExitEdge != null)
        {
            entranceEdge = destExitEdge.Pair;
        }

        // Strategy 2: For one-way warps, the entrance edge is directly in From
        // (one-way warps only have entrance edge on destination, no exit edge)
        if (entranceEdge == null)
        {
            entranceEdge = entranceNode.From.Find(e => e.Name == conn.EntranceGate);
        }

        if (entranceEdge == null)
        {
            var availableTo = string.Join(", ", entranceNode.To.Select(e => e.Name));
            var availableFrom = string.Join(", ", entranceNode.From.Select(e => e.Name));
            throw new Exception(
                $"Entrance edge not found: {conn.EntranceGate} in {conn.EntranceArea}\n" +
                $"Available in To: {availableTo}\n" +
                $"Available in From: {availableFrom}");
        }

        // Connect them
        graph.Connect(exitEdge, entranceEdge);

        Console.WriteLine($"  Connected: {conn.ExitArea} --[{conn.ExitGate}]--> {conn.EntranceArea}");
    }

    /// <summary>
    /// Apply area tiers for enemy scaling.
    /// </summary>
    /// <param name="graph">FogMod Graph</param>
    /// <param name="areaTiers">Dictionary of area -> tier</param>
    public static void ApplyAreaTiers(Graph graph, Dictionary<string, int> areaTiers)
    {
        // FogMod's Graph has an AreaTiers property
        if (graph.AreaTiers == null)
        {
            graph.AreaTiers = new Dictionary<string, int>();
        }

        foreach (var (area, tier) in areaTiers)
        {
            graph.AreaTiers[area] = tier;
        }

        Console.WriteLine($"Applied {areaTiers.Count} area tiers for scaling.");
    }
}
