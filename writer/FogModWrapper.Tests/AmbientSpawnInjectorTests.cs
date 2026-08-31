using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for AmbientSpawnInjector: spec expansion from the shared exit-gate
/// anchors (ambush pack sizing) and the in-memory MSB placement (passive
/// greeter clone, radius band and facing around the anchored gate). Anchor
/// collection itself is covered by HalloweenGateAnchorsTests.
/// </summary>
public class AmbientSpawnInjectorTests
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
        ["legacy1"] = new GraphNode { Type = "legacy_dungeon", Zones = new() { "castle_zone" } },
        ["arena1"] = new GraphNode { Type = "boss_arena", Zones = new() { "arena_zone" } },
    };

    [Fact]
    public void CollectSpecs_AnchorsOnSourceClusterExitGates()
    {
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("arena_zone", "m12_00_00_00_AEG099_004_9000", 2),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false));

        Assert.True(specs.ContainsKey("m31_00_00_00"));   // mini_dungeon exit
        Assert.False(specs.ContainsKey("m12_00_00_00"));  // boss arena stays bare
        Assert.Equal(SpawnKind.Greeter, Assert.Single(specs["m31_00_00_00"]).Kind);
    }

    [Fact]
    public void CollectSpecs_AmbushesAddSkeletonPacks()
    {
        var connections = new List<Connection>
        {
            Conn("castle_zone", "m31_00_00_00_AEG099_002_9000", 1),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: true));

        var kinds = specs["m31_00_00_00"].Select(s => s.Kind).ToList();
        Assert.Contains(SpawnKind.Greeter, kinds);
        Assert.Contains(SpawnKind.Ambusher, kinds);
        int ambushers = kinds.Count(k => k == SpawnKind.Ambusher);
        Assert.InRange(ambushers, 2, 3);
    }

    [Fact]
    public void CollectSpecs_DedupesSameGateAcrossConnections()
    {
        // Two connections leaving through the same exit gate must not double
        // up the greeter (or, with ambushes on, double the pack).
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 3),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false));

        Assert.Single(specs["m31_00_00_00"]);
    }

    [Fact]
    public void ApplyToMsb_PlacesGreeterWithPassiveThink()
    {
        var msb = MakeMsbWithGateAndEnemy();
        var sourceEnemy = msb.Parts.Enemies.Single(e => e.ModelName == "c9990");
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            new List<Connection>
            {
                Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            },
            Nodes, new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false))["m31_00_00_00"];

        var (greeters, ambushers) = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(1, greeters);
        Assert.Equal(0, ambushers);
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
        // The greeter stands watch facing AWAY from the gate, toward the
        // player approaching the exit fog (not toward the gate, which would
        // show its back to everyone walking up).
        float expectedYaw = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
        Assert.Equal(expectedYaw, greeter.Rotation.Y, 3);
        // Visibility groups inherit the clone source's values (a chr-rendered
        // spawn must not go all-zero like an SFX-visible bloodstain marker
        // would), but through fresh, un-aliased arrays.
        Assert.Equal(0x8u, greeter.Unk1.DrawGroups[0]);
        Assert.Equal(0x10u, greeter.Unk1.DisplayGroups[0]);
        Assert.NotSame(sourceEnemy.Unk1.DrawGroups, greeter.Unk1.DrawGroups);
        Assert.NotSame(sourceEnemy.Unk1.DisplayGroups, greeter.Unk1.DisplayGroups);
    }

    [Fact]
    public void ApplyToMsb_LaterGateDoesNotCloneAnEarlierGatesPlacedSpawn()
    {
        // Two gates in the same map, each with its own genuine vanilla enemy
        // nearby, distinguished by CollisionPartName. Gate B's own vanilla
        // enemy sits 1m away from gate B; gate A's greeter lands EXACTLY at
        // gate B's position by construction. A clone-source search that
        // rescans the live (mutated) enemy list would find gate A's
        // already-placed greeter (distance 0, EntityID 0 passes the
        // "vanilla" filter) before ever preferring gate B's own neighbor.
        var msb = new MSBE();

        var gateA = new MSBE.Part.Asset
        {
            Name = "AEG099_002_9000", ModelName = "AEG099_002",
            Position = new Vector3(0f, 0f, 0f), EntityID = 755890001,
        };
        msb.Parts.Assets.Add(gateA);

        var vanillaA = new MSBE.Part.Enemy
        {
            Name = "c9990_9000", ModelName = "c9990",
            Position = new Vector3(0f, 0f, 0f), EntityID = 0,
            NPCParamID = 99900000, ThinkParamID = 99900000,
            CollisionPartName = "h_zone_a",
        };
        msb.Parts.Enemies.Add(vanillaA);

        // Exactly where gate A's greeter will land: same seed math ApplyToMsb
        // uses for a Greeter spec with GateSideIsASide=false (arc center 0
        // degrees, radius 4-6m, pack size 1).
        var greeterAOffset = GateGeometry.GenerateArcOffsets(
            gateA.EntityID, gateA.Rotation.Y, 0f, 1, 4.0f, 6.0f, 0f, 120f)[0];
        var greeterAPosition = gateA.Position + greeterAOffset;

        var gateB = new MSBE.Part.Asset
        {
            Name = "AEG099_003_9000", ModelName = "AEG099_003",
            Position = greeterAPosition, EntityID = 755890002,
        };
        msb.Parts.Assets.Add(gateB);

        // Gate B's own genuine neighbor: closer to gate B than vanillaA, but
        // deliberately 1m off (not distance 0) so the buggy live-list scan
        // still prefers gate A's already-placed greeter (distance 0) over it.
        var vanillaB = new MSBE.Part.Enemy
        {
            Name = "c9991_9000", ModelName = "c9991",
            Position = greeterAPosition + new Vector3(1f, 0f, 0f), EntityID = 0,
            NPCParamID = 99910000, ThinkParamID = 99910000,
            CollisionPartName = "h_zone_b",
        };
        msb.Parts.Enemies.Add(vanillaB);

        var specs = new List<SpawnSpec>
        {
            new("AEG099_002_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false),
            new("AEG099_003_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false),
        };

        var (greeters, _) = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(2, greeters);
        var placedGreeters = msb.Parts.Enemies.Where(e => e.ModelName == "c5280").ToList();
        Assert.Equal(2, placedGreeters.Count);
        // GroupBy preserves first-occurrence key order, matching the specs
        // list order (gate A's group processed before gate B's).
        var greeterForGateB = placedGreeters[1];
        Assert.Equal("h_zone_b", greeterForGateB.CollisionPartName);
    }

    private static MSBE MakeMsbWithGateAndEnemy()
    {
        var msb = new MSBE();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_002_9000", ModelName = "AEG099_002",
            Position = new Vector3(10f, 0f, 10f), EntityID = 755890001,
        });
        var enemy = new MSBE.Part.Enemy
        {
            Name = "c9990_9000", ModelName = "c9990",
            Position = new Vector3(0f, 0f, 0f), EntityID = 0,
            NPCParamID = 99900000, ThinkParamID = 99900000,
        };
        enemy.Unk1.DrawGroups[0] = 0x8;
        enemy.Unk1.DisplayGroups[0] = 0x10;
        msb.Parts.Enemies.Add(enemy);
        return msb;
    }

    // --- ApplyPassiveThinkRow: PARAM-level coverage ---
    //
    // This is the codepath that shipped without its paramdef in a real
    // publish (see e5a3212): ApplyPassiveThinkRow.GetParam("NpcThinkParam")
    // fails silently by design (Console.WriteLine + return) when the
    // .csproj does not ship eldendata/Defs/NpcThinkParam.xml. Loading the
    // real paramdef here regression-guards the .csproj entry: if it goes
    // missing again, PARAMDEF.XmlDeserialize below throws (file not found)
    // rather than the row-writing assertions silently not running.

    [Fact]
    public void ApplyPassiveThinkRow_ClonesAgingUntouchableRowWithPerceptionZeroed()
    {
        var think = BuildParamFromDef(Path.Combine(DefsDir(), "NpcThinkParam.xml"), 52800000);

        AmbientSpawnInjector.Apply(think);

        var row = think[SpeedFogIds.PassiveGreeterThinkRow]!;
        Assert.Equal(0f, row["ear_dist"].Value);
        Assert.Equal((ushort)0, row["eye_dist"].Value);
        Assert.Equal((ushort)0, row["nose_dist"].Value);
        Assert.Equal((ushort)0, row["searchEye_dist"].Value);
        Assert.Equal((ushort)0, row["BattleStartDist"].Value);
    }

    // Same idiom as PhantomCatalogInjectorTests.BuildParamFromDef: build an
    // in-memory PARAM from the real paramdef XML, with a template row to
    // clone from.
    private static PARAM BuildParamFromDef(string defXmlPath, params int[] templateRowIds)
    {
        var def = PARAMDEF.XmlDeserialize(defXmlPath);
        var param = new PARAM { ParamType = def.ParamType, Rows = new List<PARAM.Row>() };
        param.ApplyParamdef(def);
        foreach (var id in templateRowIds)
        {
            param.Rows.Add(new PARAM.Row(id, "", def));
        }
        return param;
    }

    private static string DefsDir() =>
        Path.Combine(AppContext.BaseDirectory, "eldendata", "Defs");
}
