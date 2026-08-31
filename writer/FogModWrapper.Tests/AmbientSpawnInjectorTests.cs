using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for AmbientSpawnInjector: spec collection (destination cluster type
/// filter, ambush pack sizing) and the in-memory MSB placement (passive
/// greeter clone, radius band around the entrance gate).
/// </summary>
public class AmbientSpawnInjectorTests
{
    private static Connection Conn(string exitGate, string entranceGate, string entranceArea, int flag)
        => new()
        {
            ExitGate = exitGate, EntranceGate = entranceGate,
            ExitArea = "src_zone", EntranceArea = entranceArea, FlagId = flag,
        };

    private static readonly Dictionary<string, GraphNode> Nodes = new()
    {
        ["mini1"] = new GraphNode { Type = "mini_dungeon" },
        ["legacy1"] = new GraphNode { Type = "legacy_dungeon" },
        ["arena1"] = new GraphNode { Type = "boss_arena" },
    };

    [Fact]
    public void CollectSpecs_FiltersOnDestinationClusterType()
    {
        var connections = new List<Connection>
        {
            Conn("m10_00_00_00_AEG099_001_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 1),
            Conn("m10_00_00_00_AEG099_003_9000", "m12_00_00_00_AEG099_004_9000", "arena_zone", 2),
        };
        var eventMap = new Dictionary<string, string> { ["1"] = "mini1", ["2"] = "arena1" };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, eventMap, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false));

        Assert.True(specs.ContainsKey("m31_00_00_00"));   // mini_dungeon entrance
        Assert.False(specs.ContainsKey("m12_00_00_00"));  // boss arena filtered out
        Assert.All(specs["m31_00_00_00"], s => Assert.Equal(SpawnKind.Greeter, s.Kind));
    }

    [Fact]
    public void CollectSpecs_AmbushesAddSkeletonPacks()
    {
        var connections = new List<Connection>
        {
            Conn("m10_00_00_00_AEG099_001_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 1),
        };
        var eventMap = new Dictionary<string, string> { ["1"] = "legacy1" };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, eventMap, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: true));

        var kinds = specs["m31_00_00_00"].Select(s => s.Kind).ToList();
        Assert.Contains(SpawnKind.Greeter, kinds);
        Assert.Contains(SpawnKind.Ambusher, kinds);
        int ambushers = kinds.Count(k => k == SpawnKind.Ambusher);
        Assert.InRange(ambushers, 2, 3);
    }

    [Fact]
    public void ApplyToMsb_PlacesGreeterWithPassiveThink()
    {
        var msb = MakeMsbWithGateAndEnemy();
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            new List<Connection>
            {
                Conn("m99_00_00_00_AEG099_001_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 1),
            },
            new Dictionary<string, string> { ["1"] = "mini1" },
            Nodes, new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false))["m31_00_00_00"];

        int placed = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(1, placed);
        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Equal(52800086, greeter.NPCParamID);
        Assert.Equal(SpeedFogIds.PassiveGreeterThinkRow, greeter.ThinkParamID);
        Assert.Equal(0u, greeter.EntityID);
        Assert.Contains(msb.Models.Enemies, m => m.Name == "c5280");
        // Placement lands within the greeter radius band around the gate.
        var gate = msb.Parts.Assets.Single(a => a.Name == "AEG099_002_9000");
        var d = greeter.Position - gate.Position;
        var horizontal = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
        Assert.InRange(horizontal, 4.0f, 6.0f);
    }

    private static MSBE MakeMsbWithGateAndEnemy()
    {
        var msb = new MSBE();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_002_9000", ModelName = "AEG099_002",
            Position = new Vector3(10f, 0f, 10f), EntityID = 755890001,
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c9990_9000", ModelName = "c9990",
            Position = new Vector3(0f, 0f, 0f), EntityID = 0,
            NPCParamID = 99900000, ThinkParamID = 99900000,
        });
        return msb;
    }
}
