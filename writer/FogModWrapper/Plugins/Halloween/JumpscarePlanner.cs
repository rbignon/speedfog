using FogMod;
using FogModWrapper.Common;
using FogModWrapper.Models;

namespace FogModWrapper.Plugins.Halloween;

/// <summary>
/// One hidden Aging Untouchable of the Halloween jumpscare layer, placed in
/// MapId for the armed cluster ClusterId. It plays at most once per chosen
/// layer: LayerFlag is shared by every armed cluster of the layer.
/// EnteredFlag (set on every connection into the cluster) and LeftFlag (set
/// on every connection into the next layer) are private mirror flags of the
/// zone-tracking flags, which the racing mod clears right after capture.
/// AnchorGatePartName is an entrance gate of the cluster in this map, or
/// null when none parses to it; the placement clones the vanilla enemy
/// nearest to it.
/// </summary>
public sealed record JumpscareActor(
    string ClusterId, int Layer, string MapId, int LayerFlag,
    int EnteredFlag, int LeftFlag,
    string? AnchorGatePartName, uint EntityId, int EventId);

/// <summary>The layers drawn for a seed, their actors in id order, and the
/// mirror flags to set next to each zone-tracking flag (connection
/// flag_id) before its fog warp.</summary>
public sealed record JumpscarePlan(
    IReadOnlyList<int> Layers, IReadOnlyList<JumpscareActor> Actors,
    IReadOnlyDictionary<int, IReadOnlyList<int>> MirrorFlags)
{
    public static readonly JumpscarePlan Empty = new(
        Array.Empty<int>(), Array.Empty<JumpscareActor>(), new Dictionary<int, IReadOnlyList<int>>());

    public Dictionary<string, List<JumpscareActor>> ActorsByMap() =>
        Actors.GroupBy(a => a.MapId).ToDictionary(g => g.Key, g => g.ToList());
}

/// <summary>
/// Pure selection logic of the Halloween jumpscare layer ([plugin.halloween]
/// jumpscares), no MSB or EMEVD access. Draws `count` layers among the
/// eligible ones with a PRNG seeded from the graph seed, and arms every
/// cluster of each drawn layer. The DAG only links consecutive layers, so
/// every path crosses each drawn layer exactly once and meets exactly
/// `count` armed clusters, and entering layer L+1 is leaving layer L.
///
/// A layer is eligible when every one of its clusters is a mini or legacy
/// dungeon (HalloweenGateAnchors.SpawnClusterTypes), is entered through at
/// least one zone-tracking flag (graph.json event_map), contains no boss
/// zone (no fog separates a fight inside the cluster from the rest of it,
/// so the delay could land mid-fight), and has at least one dungeon map: an
/// open-world tile (m60/m61) streams in and out as the player moves, so an
/// actor placed on one may not be loaded when its delay expires.
/// </summary>
internal static class JumpscarePlanner
{
    // Arbitrary fixed tag XORed into the graph seed, so the layer draw does
    // not replay another seed-derived sequence. Must never be 0.
    private const int SeedTag = 0x4A4D5053;

    /// <summary>Zone name to its fog.txt maps (Area.Maps, space separated),
    /// for the planner's dungeon-map test.</summary>
    internal static Dictionary<string, string[]> BuildZoneMaps(AnnotationData ann)
    {
        var zoneMaps = new Dictionary<string, string[]>();
        foreach (var area in ann.Areas)
        {
            if (!string.IsNullOrWhiteSpace(area.Maps))
                zoneMaps[area.Name] = area.Maps.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        }
        return zoneMaps;
    }

    internal static JumpscarePlan Plan(
        GraphData graph, IReadOnlyDictionary<string, string[]> zoneMaps,
        IReadOnlySet<string> bossZones, int count, Action<string> log)
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
                && !kv.Value.Zones.Any(bossZones.Contains)
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
        var mirrors = new Dictionary<int, List<int>>();
        void Mirror(int zoneFlag, int mirrorFlag)
        {
            if (!mirrors.TryGetValue(zoneFlag, out var list))
                mirrors[zoneFlag] = list = new List<int>();
            if (!list.Contains(mirrorFlag))
                list.Add(mirrorFlag);
        }

        int armedClusters = 0;
        for (int k = 0; k < chosen.Count; k++)
        {
            int layerFlag = SpeedFogIds.HalloweenJumpscareLayerFlagBase + k;
            int leftFlag = SpeedFogIds.HalloweenJumpscareLeftFlagBase + k;
            foreach (var (flag, cluster) in graph.EventMap)
            {
                if (graph.Nodes.TryGetValue(cluster, out var target) && target.Layer == chosen[k] + 1)
                    Mirror(int.Parse(flag), leftFlag);
            }

            log($"Halloween jumpscares: layer {chosen[k]} (flag {layerFlag}, left flag {leftFlag}):");
            var clusters = graph.Nodes
                .Where(kv => kv.Value.Layer == chosen[k])
                .OrderBy(kv => kv.Key, StringComparer.Ordinal)
                .ToList();
            foreach (var (clusterId, node) in clusters)
            {
                if (armedClusters == SpeedFogIds.HalloweenJumpscareEnteredFlagCount)
                {
                    throw new InvalidOperationException(
                        $"Halloween jumpscare entered flag budget exceeded: more than " +
                        $"{SpeedFogIds.HalloweenJumpscareEnteredFlagCount} armed clusters");
                }
                int enteredFlag = SpeedFogIds.HalloweenJumpscareEnteredFlagBase + armedClusters++;
                foreach (var flag in entryFlags[clusterId])
                    Mirror(flag, enteredFlag);

                var zones = node.Zones.ToHashSet();
                var maps = DungeonMaps(node, zoneMaps);
                foreach (var mapId in maps)
                {
                    int index = actors.Count;
                    actors.Add(new JumpscareActor(
                        clusterId, chosen[k], mapId, layerFlag, enteredFlag, leftFlag,
                        AnchorGate(graph.Connections, zones, mapId),
                        SpeedFogIds.HalloweenJumpscareEntityBase + (uint)index,
                        SpeedFogIds.HalloweenJumpscareEvents.Base + index));
                }
                var name = node.DisplayName.Length > 0 ? $"{node.DisplayName} ({clusterId})" : clusterId;
                log($"  {name}: {string.Join(", ", maps)}");
            }
        }

        if (actors.Count > SpeedFogIds.HalloweenJumpscareEvents.Capacity)
        {
            throw new InvalidOperationException(
                $"Halloween jumpscare event budget exceeded: {actors.Count} actors " +
                $"(max {SpeedFogIds.HalloweenJumpscareEvents.Capacity})");
        }
        return new JumpscarePlan(
            chosen, actors, mirrors.ToDictionary(kv => kv.Key, kv => (IReadOnlyList<int>)kv.Value));
    }

    /// <summary>
    /// Append the plan's mirror flags to ZoneTrackingInjector's region table
    /// (region entity to flags set before a warp to it), after the zone
    /// flags they mirror and once per region, so the fog warp sets them
    /// along with the zone flags. Called from WriteFogModPhase, between
    /// BuildRegionToFlags and PatchEmevdPhase. An empty plan leaves the table untouched.
    /// </summary>
    internal static void AddMirrorFlags(Dictionary<int, List<int>> regionToFlags, JumpscarePlan plan)
    {
        foreach (var flags in regionToFlags.Values)
        {
            var extra = flags
                .Where(plan.MirrorFlags.ContainsKey)
                .SelectMany(f => plan.MirrorFlags[f])
                .Distinct()
                .Where(m => !flags.Contains(m))
                .ToList();
            flags.AddRange(extra);
        }
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
    /// Gate names ParseGateFullName rejects are skipped.</summary>
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
