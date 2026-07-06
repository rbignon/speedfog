using FogMod;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class ConnectionInjectorTests
{
    // --- Test graph construction helpers ---
    //
    // Mirrors how FogMod's Graph.AddPairedEdges/AddNodeX build edges: a
    // bidirectional gate contributes an Exit edge (in node.To) paired with an
    // Entrance edge (in node.From) on the same area; a one-way warp only has
    // the Entrance edge on the destination.

    private static Graph MakeGraph(params string[] areas)
    {
        var graph = new Graph
        {
            Nodes = new Dictionary<string, Graph.Node>(),
            Areas = new Dictionary<string, AnnotationData.Area>(),
        };
        foreach (var area in areas)
        {
            graph.Nodes[area] = new Graph.Node { Area = area };
            graph.Areas[area] = new AnnotationData.Area { Name = area };
        }
        return graph;
    }

    private static (Graph.Edge Exit, Graph.Edge Entrance) AddGate(
        Graph graph, string area, string gateName)
    {
        var side = new AnnotationData.Side { Area = area };
        var exit = new Graph.Edge
        {
            Type = Graph.EdgeType.Exit,
            From = area,
            Name = gateName,
            Side = side,
        };
        var entrance = new Graph.Edge
        {
            Type = Graph.EdgeType.Entrance,
            To = area,
            Name = gateName,
            Side = side,
        };
        exit.Pair = entrance;
        entrance.Pair = exit;
        graph.Nodes[area].To.Add(exit);
        graph.Nodes[area].From.Add(entrance);
        return (exit, entrance);
    }

    private static Graph.Edge AddOneWayEntrance(Graph graph, string area, string gateName)
    {
        var side = new AnnotationData.Side { Area = area };
        var entrance = new Graph.Edge
        {
            Type = Graph.EdgeType.Entrance,
            To = area,
            Name = gateName,
            Side = side,
        };
        graph.Nodes[area].From.Add(entrance);
        return entrance;
    }

    private static Connection MakeConnection(
        string exitArea, string exitGate, string entranceArea, string entranceGate,
        int flagId = 1050294000)
    {
        return new Connection
        {
            ExitArea = exitArea,
            ExitGate = exitGate,
            EntranceArea = entranceArea,
            EntranceGate = entranceGate,
            FlagId = flagId,
        };
    }

    // --- InjectAndExtract: basic wiring ---

    [Fact]
    public void InjectAndExtract_BidirectionalGate_ConnectsExitToEntrance()
    {
        var graph = MakeGraph("chapel", "stormveil");
        var (exitEdge, _) = AddGate(graph, "chapel", "gate_a");
        var (destExit, destEntrance) = AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b");
        var result = ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        // Strategy 1: entrance resolved via the destination's To edge + Pair
        Assert.Same(destEntrance, exitEdge.Link);
        Assert.Equal("stormveil", exitEdge.To);
        Assert.Equal("chapel", destEntrance.From);
        // The paired return edges are linked too (bidirectional gate)
        Assert.Same(destExit.Link, exitEdge.Pair);
        Assert.Single(result.DeferredEdges);
        Assert.Equal(conn.FlagId, result.DeferredEdges[0].FlagId);
        Assert.Same(destEntrance, result.DeferredEdges[0].EntranceEdge);
    }

    [Fact]
    public void InjectAndExtract_OneWayWarp_FindsEntranceInFromList()
    {
        var graph = MakeGraph("chapel", "arena");
        var (exitEdge, _) = AddGate(graph, "chapel", "gate_a");
        // One-way warp: entrance edge only, no matching To edge on destination
        var entrance = AddOneWayEntrance(graph, "arena", "warp_b");

        var conn = MakeConnection("chapel", "gate_a", "arena", "warp_b");
        ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Same(entrance, exitEdge.Link);
        Assert.Equal("chapel", entrance.From);
    }

    [Fact]
    public void InjectAndExtract_FlagIdZero_NotAddedToDeferredEdges()
    {
        var graph = MakeGraph("chapel", "stormveil");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b", flagId: 0);
        var result = ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Empty(result.DeferredEdges);
    }

    // --- InjectAndExtract: error reporting ---

    [Fact]
    public void InjectAndExtract_UnknownExitArea_Throws()
    {
        var graph = MakeGraph("stormveil");
        AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("nowhere", "gate_a", "stormveil", "gate_b");
        var ex = Assert.Throws<Exception>(() => ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0));

        Assert.Contains("Exit area not found: nowhere", ex.Message);
    }

    [Fact]
    public void InjectAndExtract_UnknownExitGate_ThrowsListingAvailable()
    {
        var graph = MakeGraph("chapel", "stormveil");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("chapel", "wrong_gate", "stormveil", "gate_b");
        var ex = Assert.Throws<Exception>(() => ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0));

        Assert.Contains("Exit edge not found: wrong_gate", ex.Message);
        Assert.Contains("gate_a", ex.Message);
    }

    [Fact]
    public void InjectAndExtract_UnknownEntranceGate_Throws()
    {
        var graph = MakeGraph("chapel", "stormveil");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "wrong_gate");
        var ex = Assert.Throws<Exception>(() => ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0));

        Assert.Contains("Entrance edge not found: wrong_gate", ex.Message);
    }

    // --- InjectAndExtract: pre-connected edges (crawl-mode trivial edges) ---

    [Fact]
    public void InjectAndExtract_PreconnectedExit_IsDisconnectedFirst()
    {
        var graph = MakeGraph("chapel", "stormveil", "limgrave");
        var (exitEdge, _) = AddGate(graph, "chapel", "gate_a");
        var (_, oldEntrance) = AddGate(graph, "limgrave", "gate_old");
        var (_, newEntrance) = AddGate(graph, "stormveil", "gate_b");

        // Simulate FogMod's Graph.Construct pre-connecting a trivial edge
        graph.Connect(exitEdge, oldEntrance);
        Assert.Same(oldEntrance, exitEdge.Link);

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b");
        ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Same(newEntrance, exitEdge.Link);
        Assert.Null(oldEntrance.Link);
        Assert.Null(oldEntrance.From);
    }

    [Fact]
    public void InjectAndExtract_PreconnectedEntrance_IsDisconnectedFirst()
    {
        var graph = MakeGraph("chapel", "stormveil", "limgrave");
        var (exitEdge, _) = AddGate(graph, "chapel", "gate_a");
        var (oldExit, _) = AddGate(graph, "limgrave", "gate_old");
        var (destExit, destEntrance) = AddGate(graph, "stormveil", "gate_b");

        // Destination gate pre-connected from elsewhere (via its own exit edge pair)
        graph.Connect(oldExit, destEntrance);

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b");
        ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Same(destEntrance, exitEdge.Link);
        Assert.Equal("chapel", destEntrance.From);
        // destExit's return link was rewired from limgrave to chapel
        Assert.Same(exitEdge.Pair, destExit.Link);
    }

    [Fact]
    public void InjectAndExtract_PreconnectedOneWayEntrance_IsDisconnectedViaFallback()
    {
        var graph = MakeGraph("chapel", "arena", "limgrave");
        var (exitEdge, _) = AddGate(graph, "chapel", "gate_a");
        var (oldExit, _) = AddGate(graph, "limgrave", "gate_old");
        var entrance = AddOneWayEntrance(graph, "arena", "warp_b");

        // One-way entrance pre-connected from elsewhere: no destination To edge
        // exists, so only the entrance-link fallback disconnect can free it
        graph.Connect(oldExit, entrance, ignorePair: true);
        Assert.Same(oldExit, entrance.Link);

        var conn = MakeConnection("chapel", "gate_a", "arena", "warp_b");
        ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Same(entrance, exitEdge.Link);
        Assert.Equal("chapel", entrance.From);
        Assert.Null(oldExit.Link);
    }

    [Fact]
    public void InjectAndExtract_IgnorePair_LeavesPairedEdgesUntouched()
    {
        var graph = MakeGraph("chapel", "stormveil");
        var (exitEdge, sourceEntrance) = AddGate(graph, "chapel", "gate_a");
        var (destExit, destEntrance) = AddGate(graph, "stormveil", "gate_b");

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b");
        conn.IgnorePair = true;
        ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 0);

        Assert.Same(destEntrance, exitEdge.Link);
        // ignorePair: the paired return edges are NOT wired up
        Assert.Null(destExit.Link);
        Assert.Null(sourceEntrance.Link);
        Assert.Null(sourceEntrance.From);
    }

    // --- InjectAndExtract: shared entrances (merge nodes) ---

    [Fact]
    public void InjectAndExtract_SharedEntrance_SecondConnectionUsesDuplicate()
    {
        var graph = MakeGraph("path1", "path2", "boss");
        var (exit1, _) = AddGate(graph, "path1", "gate_1");
        var (exit2, _) = AddGate(graph, "path2", "gate_2");
        var (_, bossEntrance) = AddGate(graph, "boss", "gate_boss");
        int fromCountBefore = graph.Nodes["boss"].From.Count;

        var connections = new List<Connection>
        {
            MakeConnection("path1", "gate_1", "boss", "gate_boss", flagId: 1050294001),
            MakeConnection("path2", "gate_2", "boss", "gate_boss", flagId: 1050294002),
        };
        var result = ConnectionInjector.InjectAndExtract(
            graph, connections, finishEvent: 0, finalNodeFlag: 0);

        // Primary connection uses the original entrance, secondary a duplicate
        Assert.Same(bossEntrance, exit1.Link);
        Assert.NotNull(exit2.Link);
        Assert.NotSame(bossEntrance, exit2.Link);
        Assert.Equal(fromCountBefore + 1, graph.Nodes["boss"].From.Count);
        // Both connections deferred with their own flag
        Assert.Equal(2, result.DeferredEdges.Count);
        Assert.Equal(
            new[] { 1050294001, 1050294002 },
            result.DeferredEdges.Select(d => d.FlagId).OrderBy(f => f).ToArray());
    }

    // --- InjectAndExtract: boss defeat flag extraction ---

    [Fact]
    public void InjectAndExtract_FinalNodeFlag_ExtractsBossDefeatFlag()
    {
        var graph = MakeGraph("chapel", "erdtree");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "erdtree", "gate_boss");
        graph.Areas["erdtree"].DefeatFlag = 19000800;

        var conn = MakeConnection("chapel", "gate_a", "erdtree", "gate_boss", flagId: 1050294005);
        var result = ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 1050294006,
            finalNodeFlag: 1050294005);

        Assert.Equal(19000800, result.BossDefeatFlag);
        Assert.Equal(1050294006, result.FinishEvent);
    }

    [Fact]
    public void InjectAndExtract_FinalAreaWithoutDefeatFlag_LeavesZero()
    {
        var graph = MakeGraph("chapel", "erdtree");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "erdtree", "gate_boss");

        var conn = MakeConnection("chapel", "gate_a", "erdtree", "gate_boss", flagId: 1050294005);
        var result = ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 1050294005);

        Assert.Equal(0, result.BossDefeatFlag);
    }

    [Fact]
    public void InjectAndExtract_NonFinalConnection_DoesNotExtractDefeatFlag()
    {
        var graph = MakeGraph("chapel", "stormveil");
        AddGate(graph, "chapel", "gate_a");
        AddGate(graph, "stormveil", "gate_b");
        graph.Areas["stormveil"].DefeatFlag = 10000800;

        var conn = MakeConnection("chapel", "gate_a", "stormveil", "gate_b", flagId: 1050294001);
        var result = ConnectionInjector.InjectAndExtract(
            graph, new List<Connection> { conn }, finishEvent: 0, finalNodeFlag: 1050294099);

        Assert.Equal(0, result.BossDefeatFlag);
    }

    // --- ApplyAreaTiers ---

    [Fact]
    public void ApplyAreaTiers_NullDictionary_IsInitialized()
    {
        var graph = MakeGraph("chapel");
        Assert.Null(graph.AreaTiers);

        ConnectionInjector.ApplyAreaTiers(graph, new Dictionary<string, int>
        {
            ["chapel"] = 1,
            ["stormveil"] = 5,
        });

        Assert.Equal(1, graph.AreaTiers["chapel"]);
        Assert.Equal(5, graph.AreaTiers["stormveil"]);
    }

    [Fact]
    public void ApplyAreaTiers_ExistingValues_AreOverwritten()
    {
        var graph = MakeGraph("chapel");
        graph.AreaTiers = new Dictionary<string, int> { ["chapel"] = 3, ["other"] = 7 };

        ConnectionInjector.ApplyAreaTiers(graph, new Dictionary<string, int> { ["chapel"] = 10 });

        Assert.Equal(10, graph.AreaTiers["chapel"]);
        Assert.Equal(7, graph.AreaTiers["other"]);
    }

    // --- InjectionResult.BuildRegionToFlags ---

    private static Graph.Edge EdgeWithWarp(string area, int region)
    {
        var side = new AnnotationData.Side { Area = area };
        side.Warp = new Graph.WarpPoint { Region = region };
        return new Graph.Edge { Side = side };
    }

    [Fact]
    public void BuildRegionToFlags_MapsWarpRegionsToFlags()
    {
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050294001, EdgeWithWarp("stormveil", 755890001), "conn1"));
        result.DeferredEdges.Add((1050294002, EdgeWithWarp("caelid", 755890002), "conn2"));

        result.BuildRegionToFlags(new Dictionary<string, string>());

        Assert.Equal(2, result.RegionToFlags.Count);
        Assert.Equal(new List<int> { 1050294001 }, result.RegionToFlags[755890001]);
        Assert.Equal(new List<int> { 1050294002 }, result.RegionToFlags[755890002]);
    }

    [Fact]
    public void BuildRegionToFlags_EdgeWithoutWarp_IsSkipped()
    {
        var result = new InjectionResult();
        var side = new AnnotationData.Side { Area = "stormveil" };
        result.DeferredEdges.Add((1050294001, new Graph.Edge { Side = side }, "no-warp conn"));

        result.BuildRegionToFlags(new Dictionary<string, string>());

        Assert.Empty(result.RegionToFlags);
    }

    [Fact]
    public void BuildRegionToFlags_AlternateSide_AddsSecondRegion()
    {
        var result = new InjectionResult();
        var edge = EdgeWithWarp("leyndell", 755890001);
        var altSide = new AnnotationData.Side { Area = "leyndell" };
        altSide.Warp = new Graph.WarpPoint { Region = 755890002 };
        edge.Side.AlternateSide = altSide;
        result.DeferredEdges.Add((1050294001, edge, "alternate conn"));

        result.BuildRegionToFlags(new Dictionary<string, string>());

        Assert.Equal(new List<int> { 1050294001 }, result.RegionToFlags[755890001]);
        Assert.Equal(new List<int> { 1050294001 }, result.RegionToFlags[755890002]);
    }

    [Fact]
    public void BuildRegionToFlags_SharedEntranceSameCluster_Passes()
    {
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050294001, EdgeWithWarp("boss", 755890001), "conn1"));
        result.DeferredEdges.Add((1050294002, EdgeWithWarp("boss", 755890001), "conn2"));

        var eventMap = new Dictionary<string, string>
        {
            ["1050294001"] = "boss_cluster",
            ["1050294002"] = "boss_cluster",
        };
        result.BuildRegionToFlags(eventMap);

        Assert.Equal(new List<int> { 1050294001, 1050294002 }, result.RegionToFlags[755890001]);
    }

    [Fact]
    public void BuildRegionToFlags_SharedEntranceDifferentClusters_Throws()
    {
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050294001, EdgeWithWarp("boss", 755890001), "conn1"));
        result.DeferredEdges.Add((1050294002, EdgeWithWarp("boss", 755890001), "conn2"));

        var eventMap = new Dictionary<string, string>
        {
            ["1050294001"] = "cluster_a",
            ["1050294002"] = "cluster_b",
        };

        var ex = Assert.Throws<Exception>(() => result.BuildRegionToFlags(eventMap));
        Assert.Contains("same-cluster invariant", ex.Message);
    }

    [Fact]
    public void BuildRegionToFlags_IsIdempotent()
    {
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050294001, EdgeWithWarp("stormveil", 755890001), "conn1"));

        result.BuildRegionToFlags(new Dictionary<string, string>());
        result.BuildRegionToFlags(new Dictionary<string, string>());

        Assert.Equal(new List<int> { 1050294001 }, result.RegionToFlags[755890001]);
    }
}
