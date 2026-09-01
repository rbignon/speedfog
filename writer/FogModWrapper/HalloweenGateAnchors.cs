using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Shared anchor collection for the Halloween ambient injectors
/// (AmbientSpawnInjector, GateDecorInjector): the EXIT gates of the clusters
/// the player walks through, i.e. the fogs they approach frontally while
/// hunting the next gate. Anchoring flipped from entrance gates to exit
/// gates after in-game review: on arrival the entrance gate is behind the
/// player and its dressing is never seen. Each consumer passes its own
/// cluster-type set: decorations include the start cluster (Chapel of
/// Anticipation, so the very first fog gate of a run sets the tone with
/// candles and bones), spawns do not (no mobs at the Chapel); boss arenas
/// and the final boss stay bare for both.
/// </summary>
internal static class HalloweenGateAnchors
{
    internal static readonly HashSet<string> DecorClusterTypes =
        new() { "mini_dungeon", "legacy_dungeon", "start" };

    internal static readonly HashSet<string> SpawnClusterTypes =
        new() { "mini_dungeon", "legacy_dungeon" };

    // The start cluster's zone list also contains the Roundtable Hold,
    // whose own exit fog would otherwise get decorations too (spawns
    // already cannot reach it: SpawnClusterTypes has no start). The start
    // inclusion is about the run's first gate (the Chapel exit), so the
    // hub zone is excluded outright.
    internal static readonly HashSet<string> ExcludedZones = new() { "roundtable" };

    internal readonly record struct GateAnchor(string PartName, bool IsASide);

    /// <summary>
    /// Collect exit-gate anchors per map id. The source cluster of a
    /// connection is resolved from its exit_area through the nodes' zone
    /// lists (GraphNode.Zones); connections whose source cluster is unknown
    /// or not in clusterTypes (DecorClusterTypes or SpawnClusterTypes) are
    /// skipped. Emits at most one anchor per (map, gate part name) pair.
    /// IsASide is resolved against the exit area, so the isASide?180:0
    /// arc-center mapping used by both consumers lands placements on the
    /// approach side of the gate (see docs/death-markers.md "Position
    /// Offsets (ASide/BSide)").
    /// </summary>
    internal static Dictionary<string, List<GateAnchor>> Collect(
        List<Connection> connections,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HashSet<string> clusterTypes)
    {
        var zoneTypes = new Dictionary<string, string>();
        foreach (var node in nodes.Values)
        {
            foreach (var zone in node.Zones)
                zoneTypes[zone] = node.Type;
        }

        var result = new Dictionary<string, List<GateAnchor>>();
        var seenGates = new HashSet<(string MapId, string PartName)>();

        foreach (var conn in connections)
        {
            if (ExcludedZones.Contains(conn.ExitArea))
                continue;
            if (!zoneTypes.TryGetValue(conn.ExitArea, out var type) || !clusterTypes.Contains(type))
                continue;

            var (mapId, partName) = GateGeometry.ParseGateFullName(conn.ExitGate);
            if (!seenGates.Add((mapId, partName)))
                continue;

            bool isASide = GateGeometry.ResolveIsASide(conn.ExitGate, conn.ExitArea, gateSides);

            if (!result.TryGetValue(mapId, out var anchors))
            {
                anchors = new List<GateAnchor>();
                result[mapId] = anchors;
            }
            anchors.Add(new GateAnchor(partName, isASide));
        }

        return result;
    }
}
