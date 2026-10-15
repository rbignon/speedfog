using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// One hidden Aging Untouchable of the Halloween jumpscare layer, placed in
/// MapId for the armed cluster ClusterId. It plays at most once per chosen
/// layer: LayerFlag is shared by every armed cluster of the layer.
/// EntryFlags are the zone-tracking flags of the connections into the
/// cluster, ExitFlags those of the connections out of it.
/// AnchorGatePartName is an entrance gate of the cluster in this map, or
/// null when none parses to it; the placement clones the vanilla enemy
/// nearest to it.
/// </summary>
public sealed record JumpscareActor(
    string ClusterId, int Layer, string MapId, int LayerFlag,
    IReadOnlyList<int> EntryFlags, IReadOnlyList<int> ExitFlags,
    string? AnchorGatePartName, uint EntityId, int EventId);

/// <summary>The layers drawn for a seed and their actors, in id order.</summary>
public sealed record JumpscarePlan(IReadOnlyList<int> Layers, IReadOnlyList<JumpscareActor> Actors)
{
    public static readonly JumpscarePlan Empty = new(Array.Empty<int>(), Array.Empty<JumpscareActor>());

    public Dictionary<string, List<JumpscareActor>> ActorsByMap() =>
        Actors.GroupBy(a => a.MapId).ToDictionary(g => g.Key, g => g.ToList());
}

/// <summary>
/// Pure selection logic of the Halloween jumpscare layer ([plugin.halloween]
/// jumpscares), no MSB or EMEVD access. Draws `count` layers among the
/// eligible ones with a PRNG seeded from the graph seed, and arms every
/// cluster of each drawn layer. The DAG only links consecutive layers, so
/// every path crosses each drawn layer exactly once and meets exactly
/// `count` armed clusters.
///
/// A layer is eligible when every one of its clusters is a mini or legacy
/// dungeon (HalloweenGateAnchors.SpawnClusterTypes), is entered through at
/// least one zone-tracking flag (graph.json event_map), and has at least
/// one dungeon map: an open-world tile (m60/m61) streams in and out as the
/// player moves, so an actor placed on one may not be loaded when its
/// delay expires.
/// </summary>
internal static class JumpscarePlanner
{
    // Arbitrary fixed tag XORed into the graph seed, so the layer draw does
    // not replay another seed-derived sequence. Must never be 0.
    private const int SeedTag = 0x4A4D5053;

    internal static JumpscarePlan Plan(
        GraphData graph, IReadOnlyDictionary<string, string[]> zoneMaps, int count, Action<string> log)
    {
        if (count <= 0)
            return JumpscarePlan.Empty;

        var entryFlags = graph.EventMap
            .GroupBy(kv => kv.Value, kv => int.Parse(kv.Key))
            .ToDictionary(g => g.Key, g => g.OrderBy(f => f).ToList());

        var candidates = graph.Nodes
            .GroupBy(kv => kv.Value.Layer)
            .Where(layer => layer.All(kv =>
                HalloweenGateAnchors.SpawnClusterTypes.Contains(kv.Value.Type)
                && entryFlags.ContainsKey(kv.Key)
                && DungeonMaps(kv.Value, zoneMaps).Count > 0))
            .Select(layer => layer.Key)
            .OrderBy(l => l)
            .ToList();

        // Fisher-Yates over the ascending list: new Random(int) is
        // deterministic, so the same seed always draws the same layers.
        var rng = new Random(graph.Seed ^ SeedTag);
        for (int i = candidates.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (candidates[i], candidates[j]) = (candidates[j], candidates[i]);
        }
        var chosen = candidates.Take(count).OrderBy(l => l).ToList();
        if (chosen.Count < count)
            log($"Halloween jumpscares: warning: only {chosen.Count} eligible dungeon layers for {count} jumpscares");

        var actors = new List<JumpscareActor>();
        for (int k = 0; k < chosen.Count; k++)
        {
            int layerFlag = SpeedFogIds.HalloweenJumpscareLayerFlagBase + k;
            var clusters = graph.Nodes
                .Where(kv => kv.Value.Layer == chosen[k])
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();
            foreach (var (clusterId, node) in clusters)
            {
                var zones = node.Zones.ToHashSet();
                var exitFlags = graph.Connections
                    .Where(c => zones.Contains(c.ExitArea))
                    .Select(c => c.FlagId)
                    .OrderBy(f => f)
                    .ToList();
                foreach (var mapId in DungeonMaps(node, zoneMaps))
                {
                    int index = actors.Count;
                    actors.Add(new JumpscareActor(
                        clusterId, chosen[k], mapId, layerFlag, entryFlags[clusterId], exitFlags,
                        AnchorGate(graph.Connections, zones, mapId),
                        SpeedFogIds.HalloweenJumpscareEntityBase + (uint)index,
                        SpeedFogIds.HalloweenJumpscareEvents.Base + index));
                }
            }
            log($"Halloween jumpscares: layer {chosen[k]} (flag {layerFlag}): " +
                string.Join(", ", clusters.Select(kv => kv.Key)));
        }

        if (actors.Count > SpeedFogIds.HalloweenJumpscareEvents.Capacity)
        {
            throw new InvalidOperationException(
                $"Halloween jumpscare event budget exceeded: {actors.Count} actors " +
                $"(max {SpeedFogIds.HalloweenJumpscareEvents.Capacity})");
        }
        return new JumpscarePlan(chosen, actors);
    }

    /// <summary>The cluster's maps that are not open-world tiles, sorted.</summary>
    private static List<string> DungeonMaps(GraphNode node, IReadOnlyDictionary<string, string[]> zoneMaps) =>
        node.Zones
            .SelectMany(z => zoneMaps.TryGetValue(z, out var maps) ? maps : Array.Empty<string>())
            .Where(m => !m.StartsWith("m60_", StringComparison.Ordinal)
                && !m.StartsWith("m61_", StringComparison.Ordinal))
            .Distinct()
            .OrderBy(m => m, StringComparer.Ordinal)
            .ToList();

    /// <summary>The part name of the first entrance gate of the cluster
    /// that lives in mapId, in connection order; null when there is none.
    /// Gates named by entity id alone (backportals) carry no map and are
    /// skipped.</summary>
    private static string? AnchorGate(List<Connection> connections, HashSet<string> zones, string mapId)
    {
        foreach (var conn in connections)
        {
            if (!zones.Contains(conn.EntranceArea))
                continue;
            (string MapId, string PartName) gate;
            try
            {
                gate = GateGeometry.ParseGateFullName(conn.EntranceGate);
            }
            catch (ArgumentException)
            {
                continue;
            }
            if (gate.MapId == mapId)
                return gate.PartName;
        }
        return null;
    }
}
