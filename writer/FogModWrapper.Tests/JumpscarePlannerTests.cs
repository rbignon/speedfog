using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for JumpscarePlanner: which layers are eligible, how many are
/// drawn and how deterministically, and the per-actor flags, maps, anchors
/// and ids. The fixture DAG:
///   L0 start -> L1 {mini_a (m31_00), mini_b (m31_01)} -> L2 arena
///   -> L3 legacy_l (m10_00) -> L4 {legacy_ow (m60 only), legacy_x (m11_00 + m60 tile)}
///   -> L5 mini_c (m31_02 + m12_07) -> L6 final
/// Eligible: L1, L3, L5 (L2 is a boss arena, L4 holds an open-world-only cluster).
/// </summary>
public class JumpscarePlannerTests
{
    private static Connection Conn(string exitArea, string entranceArea, string entranceGate, int flag)
        => new()
        {
            ExitArea = exitArea, ExitGate = "m99_00_00_00_AEG099_000_9000",
            EntranceArea = entranceArea, EntranceGate = entranceGate, FlagId = flag,
        };

    private static GraphData Fixture(int seed = 1)
    {
        var graph = new GraphData
        {
            Seed = seed,
            Nodes = new()
            {
                ["start"] = new GraphNode { Type = "start", Layer = 0, Zones = new() { "chapel" } },
                ["mini_a"] = new GraphNode { Type = "mini_dungeon", Layer = 1, Zones = new() { "cave_a" } },
                ["mini_b"] = new GraphNode { Type = "mini_dungeon", Layer = 1, Zones = new() { "cave_b" } },
                ["arena"] = new GraphNode { Type = "boss_arena", Layer = 2, Zones = new() { "arena_z" } },
                ["legacy_l"] = new GraphNode { Type = "legacy_dungeon", Layer = 3, Zones = new() { "castle" } },
                ["legacy_ow"] = new GraphNode { Type = "legacy_dungeon", Layer = 4, Zones = new() { "morne" } },
                ["legacy_x"] = new GraphNode { Type = "legacy_dungeon", Layer = 4, Zones = new() { "leyndell" } },
                ["mini_c"] = new GraphNode { Type = "mini_dungeon", Layer = 5, Zones = new() { "cave_c", "cave_c2" } },
                ["final"] = new GraphNode { Type = "final_boss", Layer = 6, Zones = new() { "final_z" } },
            },
            Connections = new()
            {
                Conn("chapel", "cave_a", "m31_00_00_00_AEG099_001_9000", 100),
                Conn("chapel", "cave_b", "m31_01_00_00_AEG099_001_9000", 101),
                Conn("cave_a", "arena_z", "m30_10_00_00_AEG099_001_9000", 102),
                Conn("cave_b", "arena_z", "m30_10_00_00_AEG099_002_9000", 103),
                Conn("arena_z", "castle", "m10_00_00_00_AEG099_001_9000", 104),
                Conn("castle", "morne", "m60_43_31_00_AEG099_001_9000", 105),
                // Leyndell entered through its m60 tile: no anchor in m11.
                Conn("castle", "leyndell", "m60_45_52_00_AEG099_001_9000", 106),
                Conn("morne", "cave_c", "m31_02_00_00_AEG099_001_9000", 107),
                // Backportal: named by entity id alone, no map prefix.
                Conn("leyndell", "cave_c", "31022840", 108),
                Conn("cave_c", "final_z", "m13_00_00_00_AEG099_001_9000", 109),
            },
        };
        foreach (var conn in graph.Connections)
        {
            var target = graph.Nodes.First(kv => kv.Value.Zones.Contains(conn.EntranceArea)).Key;
            graph.EventMap[conn.FlagId.ToString()] = target;
        }
        return graph;
    }

    private static Dictionary<string, string[]> ZoneMaps() => new()
    {
        ["chapel"] = new[] { "m10_01_00_00" },
        ["cave_a"] = new[] { "m31_00_00_00" },
        ["cave_b"] = new[] { "m31_01_00_00" },
        ["arena_z"] = new[] { "m30_10_00_00" },
        ["castle"] = new[] { "m10_00_00_00" },
        ["morne"] = new[] { "m60_43_31_00" },
        ["leyndell"] = new[] { "m11_00_00_00", "m60_45_52_00" },
        ["cave_c"] = new[] { "m31_02_00_00" },
        ["cave_c2"] = new[] { "m12_07_00_00" },
        ["final_z"] = new[] { "m13_00_00_00" },
    };

    private static JumpscarePlan Plan(int count, GraphData? graph = null,
        Dictionary<string, string[]>? zoneMaps = null, List<string>? logs = null)
        => JumpscarePlanner.Plan(graph ?? Fixture(), zoneMaps ?? ZoneMaps(), count, (logs ?? new()).Add);

    [Fact]
    public void Plan_DrawsOnlyEligibleLayersAndWarnsWhenShort()
    {
        var logs = new List<string>();

        var plan = Plan(10, logs: logs);

        // L2 (boss arena) and L4 (an open-world-only cluster) never qualify.
        Assert.Equal(new[] { 1, 3, 5 }, plan.Layers);
        Assert.Contains(logs, l => l.Contains("warning") && l.Contains("only 3"));
    }

    [Fact]
    public void Plan_ZeroJumpscaresIsEmpty()
        => Assert.Empty(Plan(0).Actors);

    [Fact]
    public void Plan_SameSeedSamePlan()
    {
        static string Describe(JumpscarePlan p) => string.Join(";", p.Actors.Select(a =>
            $"{a.ClusterId}@{a.MapId}:{a.LayerFlag}:{a.EntityId}:{a.EventId}"));

        Assert.Equal(Describe(Plan(2, Fixture(seed: 42))), Describe(Plan(2, Fixture(seed: 42))));
    }

    [Fact]
    public void Plan_DrawDependsOnTheSeed()
    {
        // Deterministic per seed, but not the same layer for every seed: a
        // draw that ignored the seed would always arm the same layer.
        var drawn = Enumerable.Range(0, 50)
            .Select(seed => Plan(1, Fixture(seed)).Layers.Single())
            .ToHashSet();
        Assert.True(drawn.Count > 1, $"every seed drew layer {string.Join(", ", drawn)}");
    }

    [Fact]
    public void Plan_ArmsEveryClusterOfAChosenLayerWithTheLayerFlag()
    {
        var plan = Plan(3);

        // Flags follow ascending layer order; both L1 clusters share theirs.
        int b = SpeedFogIds.HalloweenJumpscareLayerFlagBase;
        Assert.Equal(b, plan.Actors.Single(a => a.ClusterId == "mini_a").LayerFlag);
        Assert.Equal(b, plan.Actors.Single(a => a.ClusterId == "mini_b").LayerFlag);
        Assert.Equal(b + 1, plan.Actors.Single(a => a.ClusterId == "legacy_l").LayerFlag);
        Assert.All(plan.Actors.Where(a => a.ClusterId == "mini_c"), a => Assert.Equal(b + 2, a.LayerFlag));
    }

    [Fact]
    public void Plan_CollectsEntryAndExitFlagsAcrossCrossLinks()
    {
        var plan = Plan(3);

        var miniA = plan.Actors.Single(a => a.ClusterId == "mini_a");
        Assert.Equal(new[] { 100 }, miniA.EntryFlags);
        Assert.Equal(new[] { 102 }, miniA.ExitFlags);
        var legacyL = plan.Actors.Single(a => a.ClusterId == "legacy_l");
        Assert.Equal(new[] { 105, 106 }, legacyL.ExitFlags);
        Assert.All(plan.Actors.Where(a => a.ClusterId == "mini_c"),
            a => Assert.Equal(new[] { 107, 108 }, a.EntryFlags));
    }

    [Fact]
    public void Plan_PlacesOneActorPerDungeonMapOfACluster()
    {
        var zoneMaps = ZoneMaps();
        zoneMaps["morne"] = new[] { "m10_00_00_00" };  // L4 eligible now

        var plan = Plan(10, zoneMaps: zoneMaps);

        // mini_c spans two dungeon maps: one actor in each.
        Assert.Equal(new[] { "m12_07_00_00", "m31_02_00_00" },
            plan.Actors.Where(a => a.ClusterId == "mini_c").Select(a => a.MapId));
        // legacy_x keeps m11 only: the m60 tile hosts no actor.
        Assert.Equal("m11_00_00_00", plan.Actors.Single(a => a.ClusterId == "legacy_x").MapId);
    }

    [Fact]
    public void Plan_ClusterWithoutEntryFlagMakesItsLayerIneligible()
    {
        var graph = Fixture();
        graph.EventMap.Remove("101");  // mini_b loses its only entry flag

        var plan = Plan(10, graph);

        // Arming mini_b would leave an event waiting on nothing, and a path
        // through it would see no jumpscare on that layer.
        Assert.DoesNotContain(1, plan.Layers);
    }

    [Fact]
    public void Plan_AnchorsOnAnEntranceGateOfTheActorsMapOnly()
    {
        var zoneMaps = ZoneMaps();
        zoneMaps["morne"] = new[] { "m10_00_00_00" };

        var plan = Plan(10, zoneMaps: zoneMaps);

        Assert.Equal("AEG099_001_9000", plan.Actors.Single(a => a.ClusterId == "mini_a").AnchorGatePartName);
        // Entered through the m60 tile only.
        Assert.Null(plan.Actors.Single(a => a.ClusterId == "legacy_x").AnchorGatePartName);
        // m31_02 has a named entrance (107); m12_07 has none, and the
        // backportal entrance (108) is skipped rather than throwing.
        Assert.Equal("AEG099_001_9000",
            plan.Actors.Single(a => a.ClusterId == "mini_c" && a.MapId == "m31_02_00_00").AnchorGatePartName);
        Assert.Null(plan.Actors.Single(a => a.ClusterId == "mini_c" && a.MapId == "m12_07_00_00").AnchorGatePartName);
    }

    [Fact]
    public void Plan_ClustersSharingAMapGetDistinctActors()
    {
        var zoneMaps = ZoneMaps();
        zoneMaps["cave_c"] = new[] { "m31_00_00_00" };  // mini_c (L5) shares mini_a's map (L1)

        var plan = Plan(3, zoneMaps: zoneMaps);

        var shared = plan.Actors.Where(a => a.MapId == "m31_00_00_00").ToList();
        Assert.Equal(2, shared.Count);
        Assert.Equal(2, shared.Select(a => a.EntityId).Distinct().Count());
        Assert.Equal(2, shared.Select(a => a.EventId).Distinct().Count());
        Assert.Equal(2, shared.Select(a => a.LayerFlag).Distinct().Count());
        Assert.Equal(new[] { "mini_a", "mini_c" }, plan.ActorsByMap()["m31_00_00_00"].Select(a => a.ClusterId));
    }

    [Fact]
    public void Plan_IdsAreSequentialInsideTheirRanges()
    {
        var plan = Plan(3);

        for (int i = 0; i < plan.Actors.Count; i++)
        {
            Assert.Equal(SpeedFogIds.HalloweenJumpscareEntityBase + (uint)i, plan.Actors[i].EntityId);
            Assert.Equal(SpeedFogIds.HalloweenJumpscareEvents.Base + i, plan.Actors[i].EventId);
        }
    }
}
