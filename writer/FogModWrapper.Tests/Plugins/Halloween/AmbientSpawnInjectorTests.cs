using System.Numerics;
using FogModWrapper.Common;
using FogModWrapper.Models;
using FogModWrapper.Plugins.Halloween;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests.Plugins.Halloween;

/// <summary>
/// Tests for AmbientSpawnInjector: spec expansion from the shared exit-gate
/// anchors (ambush pack sizing), the in-memory MSB placement (hostile
/// greeter clone, radius band and facing around the anchored gate) and the
/// greeter/ambusher regulation rows. Anchor
/// collection itself is covered by HalloweenGateAnchorsTests.
/// </summary>
public class AmbientSpawnInjectorTests
{
    private static readonly SpawnProfile Untouchable =
        AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Untouchable);
    private static readonly SpawnProfile Skeleton =
        AmbientSpawnInjector.AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies.Skeleton);

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
        ["start1"] = new GraphNode { Type = "start", Zones = new() { "chapel_start" } },
    };

    [Fact]
    public void CollectSpecs_NoSpawnsAtTheStartCluster()
    {
        // The Chapel exit is dressed by GateDecorInjector only: props set
        // the tone, no greeter or ambush pack at the run's first gate.
        var connections = new List<Connection>
        {
            Conn("chapel_start", "m10_01_00_00_AEG099_001_9000", 1),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: true, Greeters: true));

        Assert.Empty(specs);
    }

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
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: true));

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
            new HalloweenPluginSettings.Settings(Ambushes: true, Greeters: true));

        var kinds = specs["m31_00_00_00"].Select(s => s.Kind).ToList();
        Assert.Contains(SpawnKind.Greeter, kinds);
        Assert.Contains(SpawnKind.Ambusher, kinds);
        int ambushers = kinds.Count(k => k == SpawnKind.Ambusher);
        Assert.InRange(ambushers, 2, 3);
    }

    [Fact]
    public void CollectSpecs_GreetersOffKeepsOnlyAmbushers()
    {
        var connections = new List<Connection>
        {
            Conn("castle_zone", "m31_00_00_00_AEG099_002_9000", 1),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: true, Greeters: false));

        var mapSpecs = specs["m31_00_00_00"];
        Assert.InRange(mapSpecs.Count, 2, 3);
        Assert.All(mapSpecs, s => Assert.Equal(SpawnKind.Ambusher, s.Kind));
    }

    [Fact]
    public void CollectSpecs_AllSpawnsOffYieldsNoMaps()
    {
        // Decorations-only mode: no map entry at all, so the ambient pass
        // does not read an MSB on the spawn layer's behalf.
        var connections = new List<Connection>
        {
            Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            Conn("castle_zone", "m32_00_00_00_AEG099_002_9000", 2),
        };
        var gateSides = new Dictionary<string, (string, string)>();

        // Control: the same gates are anchored when greeters are on.
        var withGreeters = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes, gateSides,
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: true));
        Assert.Equal(new[] { "m31_00_00_00", "m32_00_00_00" }, withGreeters.Keys.Order());

        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes, gateSides,
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: false));

        Assert.Empty(specs);
    }

    [Fact]
    public void CollectSpecs_CarryTheConfiguredSpecies()
    {
        var connections = new List<Connection>
        {
            Conn("castle_zone", "m31_00_00_00_AEG099_002_9000", 1),
        };
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, Nodes,
            new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(
                Ambushes: true, Greeters: true,
                GreeterSpecies: HalloweenPluginSettings.GreeterSpecies.Wormface,
                AmbusherSpecies: HalloweenPluginSettings.AmbusherSpecies.Basilisk))["m31_00_00_00"];

        Assert.Single(specs, s => s.Kind == SpawnKind.Greeter);
        Assert.InRange(specs.Count(s => s.Kind == SpawnKind.Ambusher), 2, 3);
        Assert.All(specs.Where(s => s.Kind == SpawnKind.Greeter), s => Assert.Equal(
            AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Wormface), s.Profile));
        Assert.All(specs.Where(s => s.Kind == SpawnKind.Ambusher), s => Assert.Equal(
            AmbientSpawnInjector.AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies.Basilisk), s.Profile));
    }

    [Fact]
    public void ApplyToMsb_PlacesTheGreeterProfilesModelOnTheGreeterRows()
    {
        // A wormface greeter keeps the greeter's own rows (the clone of the
        // wormface template), only the chr model follows the species.
        var msb = MakeMsbWithGateAndEnemy();
        var wormface = AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Wormface);
        var specs = new List<SpawnSpec>
        {
            new("AEG099_002_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false, wormface),
        };

        var (greeters, _) = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(1, greeters);
        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == wormface.Model);
        Assert.Equal(SpeedFogIds.HostileGreeterNpcRow, greeter.NPCParamID);
        Assert.Equal(SpeedFogIds.HostileGreeterThinkRow, greeter.ThinkParamID);
        Assert.Contains(msb.Models.Enemies, m => m.Name == wormface.Model);
        Assert.DoesNotContain(msb.Parts.Enemies, e => e.ModelName == Untouchable.Model);
    }

    [Fact]
    public void ApplyToMsb_AmbushersUseTheirSpeciesVanillaThinkRow()
    {
        // Ambushers keep vanilla perception: the part points at the species'
        // own think row, not the skeleton's.
        var msb = MakeMsbWithGateAndEnemy();
        var basilisk = AmbientSpawnInjector.AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies.Basilisk);
        var specs = new List<SpawnSpec>
        {
            new("AEG099_002_9000", SpawnKind.Ambusher, 0, 2, GateSideIsASide: false, basilisk),
            new("AEG099_002_9000", SpawnKind.Ambusher, 1, 2, GateSideIsASide: false, basilisk),
        };

        AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        var placed = msb.Parts.Enemies.Where(e => e.ModelName == basilisk.Model).ToList();
        Assert.Equal(2, placed.Count);
        Assert.All(placed, e => Assert.Equal(SpeedFogIds.DecorativeAmbusherNpcRow, e.NPCParamID));
        Assert.All(placed, e => Assert.Equal(basilisk.ThinkTemplate, e.ThinkParamID));
        Assert.NotEqual(Skeleton.ThinkTemplate, basilisk.ThinkTemplate);
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
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: true));

        Assert.Single(specs["m31_00_00_00"]);
    }

    [Fact]
    public void ApplyToMsb_PlacesHostileGreeter()
    {
        var msb = MakeMsbWithGateAndEnemy();
        var sourceEnemy = msb.Parts.Enemies.Single(e => e.ModelName == "c9990");
        var specs = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            new List<Connection>
            {
                Conn("cave_zone", "m31_00_00_00_AEG099_002_9000", 1),
            },
            Nodes, new Dictionary<string, (string, string)>(),
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: true))["m31_00_00_00"];

        var (greeters, ambushers) = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(1, greeters);
        Assert.Equal(0, ambushers);
        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        // Flat-tier clone, vanilla AI with a shorter aggro radius.
        Assert.Equal(SpeedFogIds.HostileGreeterNpcRow, greeter.NPCParamID);
        Assert.Equal(SpeedFogIds.HostileGreeterThinkRow, greeter.ThinkParamID);
        Assert.Equal(0u, greeter.EntityID);
        // The clone source patrols (WalkRouteName set above); the greeter
        // must not inherit that route.
        Assert.Null(greeter.WalkRouteName);
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
        // The spawn arc is tight around the gate axis (arc center 0 degrees
        // here): wide arcs clipped spawns into corridor walls in-game.
        Assert.InRange(expectedYaw,
            -AmbientSpawnInjector.GREETER_ARC_SPREAD / 2f,
            AmbientSpawnInjector.GREETER_ARC_SPREAD / 2f);
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
            gateA.EntityID, gateA.Rotation.Y, 0f, 1,
            AmbientSpawnInjector.GREETER_MIN_RADIUS, AmbientSpawnInjector.GREETER_MAX_RADIUS,
            0f, AmbientSpawnInjector.GREETER_ARC_SPREAD)[0];
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
            new("AEG099_002_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false, Untouchable),
            new("AEG099_003_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false, Untouchable),
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

    [Fact]
    public void ApplyToMsb_ClonesOverworldEnemiesWithTenDigitEntityIds()
    {
        // Open-world tiles number their enemies with 10-digit entity ids
        // (m61: 20xxxxxxxx), numerically ABOVE FogMod's band. Treating
        // "at or above FogModEntityMin" as FogMod-managed left the Darklight
        // Catacombs exit gate (m61_52_43, every enemy id-bearing) without
        // any clone source, hence without greeter or ambushers.
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c1000_9001", ModelName = "c1000",
            Position = new Vector3(0f, 0f, 0f), EntityID = 2052430100,
        });

        var (greeters, _) = AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), _ => { });

        Assert.Equal(1, greeters);
    }

    [Fact]
    public void ApplyToMsb_SkipsFogModManagedCloneSources()
    {
        // FogMod's own parts (755890000+) are not hand-placed vanilla
        // enemies: a nearer one must lose to a farther vanilla neighbor.
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c1000_9100", ModelName = "c1000",
            Position = new Vector3(10f, 0f, 11f), EntityID = 755890500,
            CollisionPartName = "h_fogmod",
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3000_9000", ModelName = "c3000",
            Position = new Vector3(0f, 0f, 0f), EntityID = 30040200,
            CollisionPartName = "h000300",
        });

        AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), _ => { });

        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Equal("h000300", greeter.CollisionPartName);
    }

    [Fact]
    public void ApplyToMsb_PrefersCloneSourcesWithACollisionWhereMostEnemiesHaveOne()
    {
        // Dungeon enemies stand on a named collision; c1000 dummies near
        // catacomb boss fogs often have none. A greeter cloned from such a
        // dummy inherits an empty CollisionPartName and never shows up
        // in-game (four catacomb/cave exit gates of seed f7280403ab5d).
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c1000_9001", ModelName = "c1000",
            Position = new Vector3(10f, 0f, 12f), EntityID = 30040100,
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3400_9000", ModelName = "c3970",
            Position = new Vector3(-10f, 0f, 10f), EntityID = 30040200,
            CollisionPartName = "h000300",
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3080_9011", ModelName = "c5240",
            Position = new Vector3(-40f, 5f, 10f), EntityID = 30040201,
            CollisionPartName = "h000200",
        });

        var (greeters, _) = AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), _ => { });

        Assert.Equal(1, greeters);
        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Equal("h000300", greeter.CollisionPartName);
    }

    [Fact]
    public void ApplyToMsb_KeepsCollisionlessSourcesWhereMostEnemiesHaveNone()
    {
        // Open-world tiles leave nearly every enemy collisionless (157 of
        // 185 tiles name none at all, the rest at most ~13%). A rare
        // collision-bearing enemy (cellar, interior) must not become the
        // source of every spawn in the tile: the nearest enemy stays the
        // source, collisionless like its neighbors.
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c4180_9003", ModelName = "c4313",
            Position = new Vector3(10f, 0f, 14f), EntityID = 1043300200,
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c4180_9004", ModelName = "c5380",
            Position = new Vector3(30f, 0f, 10f), EntityID = 1043300201,
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5020_0401", ModelName = "c5020",
            Position = new Vector3(-30f, -8f, 10f), EntityID = 1043300202,
            CollisionPartName = "h433000",
        });

        AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), _ => { });

        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Null(greeter.CollisionPartName);
    }

    [Fact]
    public void ApplyToMsb_HalfCollisionBearingCountsAsACollisionMap()
    {
        // "At least half": a 1-of-2 map still requires a collision.
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c1000_9001", ModelName = "c1000",
            Position = new Vector3(10f, 0f, 12f), EntityID = 30040100,
        });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3400_9000", ModelName = "c3970",
            Position = new Vector3(-10f, 0f, 10f), EntityID = 30040200,
            CollisionPartName = "h000300",
        });

        AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), _ => { });

        var greeter = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Equal("h000300", greeter.CollisionPartName);
    }

    [Fact]
    public void ApplyToMsb_LogsAFarCloneSource()
    {
        // A source far from the gate (Tombsward Cave: 96m, another part of
        // the cave) may carry a collision the spawn does not stand on; the
        // generation log names it so in-game checks know where to look.
        var msb = MakeMsbWithGate();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c4385_9000", ModelName = "c3450",
            Position = new Vector3(100f, -29f, 10f), EntityID = 31020200,
            CollisionPartName = "h000300",
        });
        var logs = new List<string>();

        AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), logs.Add);

        Assert.Contains(logs, l => l.Contains("c4385_9000") && l.Contains("h000300"));
    }

    [Fact]
    public void ApplyToMsb_NearCloneSourceIsNotLogged()
    {
        var msb = MakeMsbWithGateAndEnemy();
        var logs = new List<string>();

        AmbientSpawnInjector.ApplyToMsb(msb, GreeterAtGate(), logs.Add);

        Assert.Empty(logs);
    }

    private static MSBE MakeMsbWithGate()
    {
        var msb = new MSBE();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_002_9000", ModelName = "AEG099_002",
            Position = new Vector3(10f, 0f, 10f), EntityID = 755890001,
        });
        return msb;
    }

    private static List<SpawnSpec> GreeterAtGate() => new()
    {
        new("AEG099_002_9000", SpawnKind.Greeter, 0, 1, GateSideIsASide: false, Untouchable),
    };

    private static MSBE MakeMsbWithGateAndEnemy()
    {
        var msb = MakeMsbWithGate();
        var enemy = new MSBE.Part.Enemy
        {
            Name = "c9990_9000", ModelName = "c9990",
            Position = new Vector3(0f, 0f, 0f), EntityID = 0,
            NPCParamID = 99900000, ThinkParamID = 99900000,
            WalkRouteName = "h_patrol_route",
        };
        enemy.Unk1.DrawGroups[0] = 0x8;
        enemy.Unk1.DisplayGroups[0] = 0x10;
        msb.Parts.Enemies.Add(enemy);
        return msb;
    }

    [Fact]
    public void ApplyToMsb_AmbushersUseTheDecorativeNpcClone()
    {
        var msb = MakeMsbWithGateAndEnemy();
        var specs = new List<SpawnSpec>
        {
            new("AEG099_002_9000", SpawnKind.Ambusher, 0, 2, GateSideIsASide: false, Skeleton),
            new("AEG099_002_9000", SpawnKind.Ambusher, 1, 2, GateSideIsASide: false, Skeleton),
        };

        var (_, ambushers) = AmbientSpawnInjector.ApplyToMsb(msb, specs, _ => { });

        Assert.Equal(2, ambushers);
        var placed = msb.Parts.Enemies.Where(e => e.ModelName == "c3500").ToList();
        Assert.Equal(2, placed.Count);
        // Decorative clone (token HP, near-zero attack), vanilla aggro AI.
        Assert.All(placed, e => Assert.Equal(SpeedFogIds.DecorativeAmbusherNpcRow, e.NPCParamID));
        Assert.All(placed, e => Assert.Equal(35000000, e.ThinkParamID));
        // Pack stays inside the tight spawn arc around the gate axis (arc
        // center 0 degrees here; wide arcs clipped spawns into walls).
        var gate = msb.Parts.Assets.Single(a => a.Name == "AEG099_002_9000");
        Assert.All(placed, e =>
        {
            var d = e.Position - gate.Position;
            float angle = MathF.Atan2(d.X, d.Z) * 180f / MathF.PI;
            Assert.InRange(angle,
                -AmbientSpawnInjector.AMBUSH_ARC_SPREAD / 2f,
                AmbientSpawnInjector.AMBUSH_ARC_SPREAD / 2f);
        });
    }

    [Fact]
    public void ApplyAmbusher_ClonesSkeletonRowWithTokenHpAndNoRunes()
    {
        var npc = BuildParamFromDef("NpcParam", 35000030);
        var sp = BuildSpEffectParam();

        AmbientSpawnInjector.ApplyAmbusher(npc, sp, HalloweenPluginSettings.AmbusherSpecies.Skeleton);

        var row = npc[SpeedFogIds.DecorativeAmbusherNpcRow]!;
        Assert.Equal(AmbientSpawnInjector.AMBUSH_HP, (uint)row["hp"].Value);
        Assert.Equal(0u, (uint)row["getSoul"].Value);
        // Def defaults leave every slot at -1, so the scan picks slot 0.
        Assert.Equal(SpeedFogIds.DecorativeAmbusherSpEffectRow, (int)row["spEffectID0"].Value);
    }

    [Fact]
    public void ApplyAmbusher_AttachesToTheFirstFreeSpEffectSlot()
    {
        var npc = BuildParamFromDef("NpcParam", 35000030);
        // Occupy the template's first slots: the clone inherits them, and
        // the nerf SpEffect must land in the next free slot, not overwrite.
        var template = npc[35000030]!;
        template["spEffectID0"].Value = 350001;
        template["spEffectID1"].Value = 350002;
        var sp = BuildSpEffectParam();

        AmbientSpawnInjector.ApplyAmbusher(npc, sp, HalloweenPluginSettings.AmbusherSpecies.Skeleton);

        var row = npc[SpeedFogIds.DecorativeAmbusherNpcRow]!;
        Assert.Equal(350001, (int)row["spEffectID0"].Value);
        Assert.Equal(350002, (int)row["spEffectID1"].Value);
        Assert.Equal(SpeedFogIds.DecorativeAmbusherSpEffectRow, (int)row["spEffectID2"].Value);
    }

    [Fact]
    public void ApplyAmbusher_SpEffectNeutersAttackAndNeutralizesTemplateRates()
    {
        var npc = BuildParamFromDef("NpcParam", 35000030);
        var sp = BuildSpEffectParam();
        // Real vanilla 7010 values (def defaults are already 1, which would
        // make the neutralization assertions tautological).
        sp[7010]!["maxHpRate"].Value = 1.141f;
        sp[7010]!["staminaAttackRate"].Value = 1.021f;

        AmbientSpawnInjector.ApplyAmbusher(npc, sp, HalloweenPluginSettings.AmbusherSpecies.Skeleton);

        var row = sp[SpeedFogIds.DecorativeAmbusherSpEffectRow]!;
        foreach (var field in new[]
        {
            "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate",
            "thunderAttackPowerRate", "darkAttackPowerRate", "staminaAttackRate",
        })
        {
            Assert.Equal(AmbientSpawnInjector.AMBUSH_ATTACK_RATE, (float)row[field].Value);
        }
        Assert.Equal(1f, (float)row["maxHpRate"].Value);
        Assert.Equal(1f, (float)row["haveSoulRate"].Value);
    }

    [Fact]
    public void ApplyAmbusher_ClearsInheritedScalingSlotsOnly()
    {
        // Vanilla 35000030 carries the game's own area-scaling SpEffect
        // (7080) in a slot; the clone must drop scaling-band references
        // (they would stack ~2x hp/attack onto the nerf) while keeping
        // unrelated inherited SpEffects.
        var npc = BuildParamFromDef("NpcParam", 35000030);
        var template = npc[35000030]!;
        template["spEffectID0"].Value = 350001;    // unrelated: must survive
        template["spEffectID3"].Value = 7080;      // base-game scaling band
        template["spEffectID4"].Value = 20007000;  // DLC scaling band
        var sp = BuildSpEffectParam();

        AmbientSpawnInjector.ApplyAmbusher(npc, sp, HalloweenPluginSettings.AmbusherSpecies.Skeleton);

        var row = npc[SpeedFogIds.DecorativeAmbusherNpcRow]!;
        Assert.Equal(350001, (int)row["spEffectID0"].Value);
        Assert.Equal(-1, (int)row["spEffectID3"].Value);
        Assert.Equal(-1, (int)row["spEffectID4"].Value);
        // The nerf lands in the first slot freed after clearing.
        Assert.Equal(SpeedFogIds.DecorativeAmbusherSpEffectRow, (int)row["spEffectID1"].Value);
    }

    [Fact]
    public void ApplyAmbusher_BasiliskClonesTheBasiliskRow()
    {
        // Same decorative treatment, basilisk template: its own behavior
        // (the Deathblight cloud lives there, untouched by the attack-rate
        // nerf) and its own area scaling cleared.
        var basilisk = AmbientSpawnInjector.AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies.Basilisk);
        var npc = BuildParamFromDef("NpcParam", basilisk.NpcTemplate);
        var template = npc[basilisk.NpcTemplate]!;
        template["behaviorVariationId"].Value = 41500;
        template["hp"].Value = 338u;
        template["getSoul"].Value = 130u;
        template["spEffectID3"].Value = 7050;  // vanilla Ainsel tier 5
        var sp = BuildSpEffectParam();

        AmbientSpawnInjector.ApplyAmbusher(npc, sp, HalloweenPluginSettings.AmbusherSpecies.Basilisk);

        var row = npc[SpeedFogIds.DecorativeAmbusherNpcRow]!;
        Assert.Equal(41500, (int)row["behaviorVariationId"].Value);
        Assert.Equal(AmbientSpawnInjector.AMBUSH_HP, (uint)row["hp"].Value);
        Assert.Equal(0u, (uint)row["getSoul"].Value);
        var slots = Enumerable.Range(0, 32).Select(i => (int)row[$"spEffectID{i}"].Value).ToList();
        Assert.DoesNotContain(7050, slots);
        Assert.Contains(SpeedFogIds.DecorativeAmbusherSpEffectRow, slots);
    }

    // The scaling tier-1 row (7010) is the clone template in production;
    // its def-built stand-in just needs to exist with that id.
    private static PARAM BuildSpEffectParam()
        => BuildParamFromDef("SpEffect", 7010, "SpEffectParam");

    // --- ApplyHostileGreeter: PARAM-level coverage ---
    //
    // Built from the real NpcParam paramdef: a .csproj that stops shipping
    // eldendata/Defs/NpcParam.xml makes PARAMDEF.XmlDeserialize throw here
    // (file not found), whereas ApplyHostileGreeterRow itself only logs and
    // returns when the param is unavailable (see e5a3212 for the think-row
    // instance of that silent failure).

    [Fact]
    public void ApplyHostileGreeter_ReplacesDlcScalingWithTheFlatTierRow()
    {
        // Vanilla 52800086 carries the DLC area scaling of its own tier
        // (20007100, tier 31: hp x13.6, attack x3.8). Greeters are placed
        // after FogMod's scaling pass, so the clone swaps that slot for the
        // base-game row of GREETER_TIER while every unrelated slot survives.
        var npc = BuildParamFromDef("NpcParam", 52800086);
        var template = npc[52800086]!;
        template["spEffectID0"].Value = 5401;
        template["spEffectID1"].Value = 0;          // placeholders: occupied
        template["spEffectID2"].Value = 0;
        template["spEffectID3"].Value = 20007100;   // DLC scaling band
        template["spEffectID17"].Value = 20011450;  // vanilla teleport gate

        AmbientSpawnInjector.ApplyHostileGreeter(npc, HalloweenPluginSettings.GreeterSpecies.Untouchable);

        var row = npc[SpeedFogIds.HostileGreeterNpcRow]!;
        var slots = Enumerable.Range(0, 32).Select(i => (int)row[$"spEffectID{i}"].Value).ToList();
        Assert.Equal(5401, slots[0]);
        Assert.Equal(0, slots[1]);
        Assert.Equal(0, slots[2]);
        Assert.Equal(20011450, slots[17]);
        Assert.DoesNotContain(20007100, slots);
        // Vanilla's layout (slots 1-2 hold 0): the tier row takes the slot
        // the DLC row was cleared from, the first free one.
        Assert.Equal(7000 + 10 * AmbientSpawnInjector.GREETER_TIER, slots[3]);
        Assert.Single(slots, s => s == 7000 + 10 * AmbientSpawnInjector.GREETER_TIER);
    }

    [Fact]
    public void ApplyHostileGreeter_DropsNoRunesAndNoItem()
    {
        // One greeter per dungeon exit: vanilla's runes and enemy item lot
        // would turn every exit gate into a farm during a race.
        var npc = BuildParamFromDef("NpcParam", 52800086);
        var template = npc[52800086]!;
        template["getSoul"].Value = 4449u;
        template["itemLotId_enemy"].Value = 528000000;

        AmbientSpawnInjector.ApplyHostileGreeter(npc, HalloweenPluginSettings.GreeterSpecies.Untouchable);

        var row = npc[SpeedFogIds.HostileGreeterNpcRow]!;
        Assert.Equal(0u, (uint)row["getSoul"].Value);
        Assert.Equal(-1, (int)row["itemLotId_enemy"].Value);
    }

    [Fact]
    public void ApplyHostileGreeter_WormfaceClonesTheWormfaceRowAtTheFlatTier()
    {
        // Same greeter treatment on the wormface template: its behavior and
        // its own SpEffects (Deathblight included) survive, its area scaling
        // (tier 8 in Altus) gives way to GREETER_TIER, no runes, no drop.
        var wormface = AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Wormface);
        var npc = BuildParamFromDef("NpcParam", wormface.NpcTemplate);
        var template = npc[wormface.NpcTemplate]!;
        template["behaviorVariationId"].Value = 45700;
        template["spEffectID1"].Value = 16583;
        template["spEffectID3"].Value = 7080;
        template["getSoul"].Value = 541u;
        template["itemLotId_enemy"].Value = 457000000;

        AmbientSpawnInjector.ApplyHostileGreeter(npc, HalloweenPluginSettings.GreeterSpecies.Wormface);

        var row = npc[SpeedFogIds.HostileGreeterNpcRow]!;
        Assert.Equal(45700, (int)row["behaviorVariationId"].Value);
        var slots = Enumerable.Range(0, 32).Select(i => (int)row[$"spEffectID{i}"].Value).ToList();
        Assert.Equal(16583, slots[1]);
        Assert.DoesNotContain(7080, slots);
        Assert.Contains(7000 + 10 * AmbientSpawnInjector.GREETER_TIER, slots);
        Assert.Equal(0u, (uint)row["getSoul"].Value);
        Assert.Equal(-1, (int)row["itemLotId_enemy"].Value);
    }

    [Fact]
    public void ApplyGreeterThink_WormfaceClonesTheWormfaceThinkRow()
    {
        var wormface = AmbientSpawnInjector.GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Wormface);
        var think = BuildParamFromDef("NpcThinkParam", wormface.ThinkTemplate);
        var template = think[wormface.ThinkTemplate]!;
        template["battleGoalID"].Value = 457000;
        template["eye_dist"].Value = (ushort)25;

        AmbientSpawnInjector.ApplyGreeterThink(think, HalloweenPluginSettings.GreeterSpecies.Wormface);

        var row = think[SpeedFogIds.HostileGreeterThinkRow]!;
        Assert.Equal(457000, (int)row["battleGoalID"].Value);
        Assert.Equal((ushort)AmbientSpawnInjector.GREETER_AGGRO_DIST, (ushort)row["eye_dist"].Value);
    }

    [Fact]
    public void ApplyGreeterThink_CapsEveryPerceptionRadius()
    {
        // Vanilla 52800000 sees and hears the player from 30m, so a greeter
        // came down whole corridors to its gate. The clone caps every
        // perception radius at GREETER_AGGRO_DIST and never raises one
        // (smell stays at its vanilla 2m). Built from the real paramdef, like
        // the NpcParam tests above. searchEye_dist and BattleStartDist are
        // 10 in vanilla, the cap itself: raised here so each field proves
        // it is capped.
        var think = BuildParamFromDef("NpcThinkParam", 52800000);
        var template = think[52800000]!;
        template["eye_dist"].Value = (ushort)30;
        template["ear_dist"].Value = 30f;
        template["searchEye_dist"].Value = (ushort)25;
        template["BattleStartDist"].Value = (ushort)25;
        template["nose_dist"].Value = (ushort)2;
        template["battleGoalID"].Value = 528000;

        AmbientSpawnInjector.ApplyGreeterThink(think, HalloweenPluginSettings.GreeterSpecies.Untouchable);

        var row = think[SpeedFogIds.HostileGreeterThinkRow]!;
        Assert.Equal((ushort)AmbientSpawnInjector.GREETER_AGGRO_DIST, (ushort)row["eye_dist"].Value);
        Assert.Equal((float)AmbientSpawnInjector.GREETER_AGGRO_DIST, (float)row["ear_dist"].Value);
        Assert.Equal((ushort)AmbientSpawnInjector.GREETER_AGGRO_DIST, (ushort)row["searchEye_dist"].Value);
        Assert.Equal((ushort)AmbientSpawnInjector.GREETER_AGGRO_DIST, (ushort)row["BattleStartDist"].Value);
        Assert.Equal((ushort)2, (ushort)row["nose_dist"].Value);
        // Vanilla AI: same battle script, only the perception changes.
        Assert.Equal(528000, (int)row["battleGoalID"].Value);
    }
}
