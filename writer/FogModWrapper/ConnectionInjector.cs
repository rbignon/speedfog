using FogMod;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Result of connection injection, carrying data needed for EMEVD post-processing.
/// </summary>
public class InjectionResult
{
    /// <summary>FogMod DefeatFlag for the final boss zone.</summary>
    public int BossDefeatFlag { get; set; }

    /// <summary>The finish_event flag ID from graph.json.</summary>
    public int FinishEvent { get; set; }
}

/// <summary>
/// Injects SpeedFog's graph connections into FogMod's Graph object.
/// </summary>
public static class ConnectionInjector
{
    /// <summary>
    /// Inject connections and extract boss defeat flag for EMEVD post-processing.
    /// </summary>
    /// <param name="graph">FogMod Graph with nodes/edges constructed but not connected</param>
    /// <param name="connections">List of connections from graph.json</param>
    /// <param name="finishEvent">The finish_event flag ID (0 if not using v4)</param>
    /// <returns>InjectionResult with boss defeat flag for zone tracking</returns>
    public static InjectionResult InjectAndExtract(
        Graph graph, List<Connection> connections, int finishEvent)
    {
        Console.WriteLine($"Injecting {connections.Count} connections...");

        var result = new InjectionResult { FinishEvent = finishEvent };

        foreach (var conn in connections)
        {
            try
            {
                ConnectAndExtract(graph, conn, finishEvent, result);
            }
            catch (Exception ex)
            {
                throw new Exception($"Failed to connect: {conn}\n{ex.Message}", ex);
            }
        }

        Console.WriteLine($"All connections injected successfully.");
        return result;
    }

    /// <summary>
    /// Connect a single exit edge to an entrance edge and extract warp data.
    /// </summary>
    private static void ConnectAndExtract(
        Graph graph, Connection conn, int finishEvent, InjectionResult result)
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

        // Pre-disconnect any edges that FogMod's Graph.Construct() auto-connected.
        // This happens for internal dungeon fog gates (e.g., enirilim_stairs ↔ enirilim_radahn)
        // that we want to redirect to our custom DAG connections.
        // Graph.Disconnect(exit) clears both directions and paired edges.
        if (exitEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected exit: {conn.ExitGate}");
            graph.Disconnect(exitEdge);
        }
        if (destExitEdge != null && destExitEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected entrance: {conn.EntranceGate}");
            graph.Disconnect(destExitEdge);
        }
        // Fallback: entrance edge still linked (e.g., one-way warp pre-connected independently)
        if (entranceEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected entrance link: {conn.EntranceGate}");
            graph.Disconnect(entranceEdge.Link);
        }

        // Connect them
        graph.Connect(exitEdge, entranceEdge);

        Console.WriteLine($"  Connected: {conn.ExitArea} --[{conn.ExitGate}]--> {conn.EntranceArea}");

        // For the finish event connection, extract boss defeat flag.
        // Multiple connections may target the same final boss node (diamond DAG merge),
        // but they all share the same EntranceArea so DefeatFlag is consistent.
        if (conn.FlagId == finishEvent && finishEvent > 0)
        {
            var area = graph.Areas.GetValueOrDefault(conn.EntranceArea);
            if (area != null && area.DefeatFlag > 0)
            {
                result.BossDefeatFlag = area.DefeatFlag;
            }
            else
            {
                Console.WriteLine($"Warning: No DefeatFlag found for finish area {conn.EntranceArea}");
            }
        }
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
