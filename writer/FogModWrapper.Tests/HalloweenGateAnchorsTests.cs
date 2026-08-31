using System.Collections.Generic;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for HalloweenGateAnchors: the exit-gate anchor collection shared by
/// AmbientSpawnInjector and GateDecorInjector (source cluster resolved
/// through zone lists, type filter, dedup, side resolution).
/// </summary>
public class HalloweenGateAnchorsTests
{
    private static Connection Conn(string exitArea, string exitGate, int flag)
        => new()
        {
            ExitArea = exitArea, ExitGate = exitGate,
            EntranceArea = "dst_zone", EntranceGate = "m99_00_00_00_AEG099_099_9000",
            FlagId = flag,
        };

    private static readonly Dictionary<string, GraphNode> Nodes = new()
    {
        ["mini1"] = new GraphNode { Type = "mini_dungeon", Zones = new() { "cave_zone" } },
        ["legacy1"] = new GraphNode { Type = "legacy_dungeon", Zones = new() { "castle_zone", "castle_annex" } },
        ["arena1"] = new GraphNode { Type = "boss_arena", Zones = new() { "arena_zone" } },
        ["start1"] = new GraphNode { Type = "start", Zones = new() { "chapel_start", "roundtable" } },
    };

    [Fact]
    public void Collect_KeepsExitsOfMiniLegacyAndStartClusters()
    {
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("castle_annex", "m10_00_00_00_AEG099_003_9000", 2),  // secondary zone of legacy1
            Conn("chapel_start", "m10_01_00_00_AEG099_001_9000", 3),  // the run's very first gate
            Conn("arena_zone", "m12_00_00_00_AEG099_004_9000", 4),    // boss arena: stays bare
            Conn("nowhere_zone", "m13_00_00_00_AEG099_005_9000", 5),  // zone in no cluster
            Conn("roundtable", "m11_10_00_00_AEG099_231_9000", 6),    // safe hub: excluded
        };

        var anchors = HalloweenGateAnchors.Collect(
            connections, Nodes, new Dictionary<string, (string, string)>());

        Assert.Equal("AEG099_002_9000", Assert.Single(anchors["m31_00_00_00"]).PartName);
        Assert.Equal("AEG099_003_9000", Assert.Single(anchors["m10_00_00_00"]).PartName);
        Assert.Equal("AEG099_001_9000", Assert.Single(anchors["m10_01_00_00"]).PartName);
        Assert.False(anchors.ContainsKey("m12_00_00_00"));
        Assert.False(anchors.ContainsKey("m13_00_00_00"));
        // The roundtable zone belongs to the start cluster but is the safe
        // hub; its fog gate must stay bare (no greeter, no ambush pack).
        Assert.False(anchors.ContainsKey("m11_10_00_00"));
    }

    [Fact]
    public void Collect_DedupesSameGateAcrossConnections()
    {
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 3),
        };

        var anchors = HalloweenGateAnchors.Collect(
            connections, Nodes, new Dictionary<string, (string, string)>());

        Assert.Single(anchors["m31_00_00_00"]);
    }

    [Fact]
    public void Collect_ResolvesSideAgainstExitArea()
    {
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("castle_zone", "m10_00_00_00_AEG099_003_9000", 2),
        };
        var gateSides = new Dictionary<string, (string, string)>
        {
            ["m31_00_00_00_AEG099_002_9000"] = ("cave_zone", "other_zone"),
            ["m10_00_00_00_AEG099_003_9000"] = ("other_zone", "castle_zone"),
        };

        var anchors = HalloweenGateAnchors.Collect(connections, Nodes, gateSides);

        Assert.True(Assert.Single(anchors["m31_00_00_00"]).IsASide);
        Assert.False(Assert.Single(anchors["m10_00_00_00"]).IsASide);
    }
}
