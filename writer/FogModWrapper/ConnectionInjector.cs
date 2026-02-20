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
    /// <param name="finalNodeFlag">Zone-tracking flag for the final boss node, used to identify
    /// which connections lead to the final boss area and extract its DefeatFlag</param>
    /// <returns>InjectionResult with boss defeat flag for zone tracking</returns>
    public static InjectionResult InjectAndExtract(
        Graph graph, List<Connection> connections, int finishEvent, int finalNodeFlag)
    {
        Console.WriteLine($"Injecting {connections.Count} connections...");

        var result = new InjectionResult { FinishEvent = finishEvent };

        // Group connections by (entrance_area, entrance_gate) to detect shared entrances.
        // Shared entrance = multiple exits connecting to the same entrance fog gate.
        var entranceGroups = new Dictionary<string, List<Connection>>();
        foreach (var conn in connections)
        {
            var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
            if (!entranceGroups.ContainsKey(key))
                entranceGroups[key] = new List<Connection>();
            entranceGroups[key].Add(conn);
        }

        // Track which entrances have already been connected (for DuplicateEntrance)
        var connectedEntrances = new Dictionary<string, Graph.Edge>();

        foreach (var conn in connections)
        {
            try
            {
                var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
                bool isSharedEntrance = entranceGroups[key].Count > 1;
                bool isSecondaryConnection = connectedEntrances.ContainsKey(key);

                ConnectAndExtract(graph, conn, finalNodeFlag, result,
                    isSharedEntrance, isSecondaryConnection, connectedEntrances);
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
    /// For shared entrances, secondary connections use DuplicateEntrance().
    /// </summary>
    private static void ConnectAndExtract(
        Graph graph, Connection conn, int finalNodeFlag, InjectionResult result,
        bool isSharedEntrance, bool isSecondaryConnection,
        Dictionary<string, Graph.Edge> connectedEntrances)
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
        Graph.Edge? destExitEdge = null;

        if (isSecondaryConnection)
        {
            // Shared entrance: duplicate the original entrance for this connection
            var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
            var originalEntrance = connectedEntrances[key];
            entranceEdge = graph.DuplicateEntrance(originalEntrance);
            Console.WriteLine($"  Duplicated entrance for shared merge: {conn.EntranceGate}");
        }
        else
        {
            // Strategy 1: For bidirectional fogs, find via To + Pair
            // The entrance gate name refers to an exit edge on the destination side,
            // whose Pair is the entrance edge we want
            destExitEdge = entranceNode.To.Find(e => e.Name == conn.EntranceGate);
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
        }

        // Always disconnect the exit edge if pre-connected (each connection has its own exit)
        if (exitEdge.Link != null)
        {
            Console.WriteLine($"  Disconnecting pre-connected exit: {conn.ExitGate}");
            graph.Disconnect(exitEdge);
        }

        // Entrance-side disconnect only for primary connections (duplicates are fresh edges)
        if (!isSecondaryConnection)
        {
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
        }

        // Connect them
        graph.Connect(exitEdge, entranceEdge);

        // Track connected entrance for shared entrance detection
        if (isSharedEntrance && !isSecondaryConnection)
        {
            var key = $"{conn.EntranceArea}|{conn.EntranceGate}";
            connectedEntrances[key] = entranceEdge;
        }

        Console.WriteLine($"  Connected: {conn.ExitArea} --[{conn.ExitGate}]--> {conn.EntranceArea}" +
            (isSecondaryConnection ? " (shared entrance)" : ""));

        // For connections targeting the final boss node, extract boss defeat flag.
        // finalNodeFlag matches the first connection to the end node; subsequent
        // connections to the same node have different flag_ids (per-connection allocation)
        // but we only need one match to extract the DefeatFlag.
        if (conn.FlagId == finalNodeFlag && finalNodeFlag > 0)
        {
            var area = graph.Areas.GetValueOrDefault(conn.EntranceArea);
            if (area != null && area.DefeatFlag > 0)
            {
                result.BossDefeatFlag = area.DefeatFlag;
            }
            else
            {
                Console.WriteLine($"Note: No DefeatFlag in FogMod Graph for finish area {conn.EntranceArea} " +
                    "(will use graph.json finish_boss_defeat_flag if available)");
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
