using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for GateDecorInjector: entrance-gate spec collection (destination
/// cluster type filter, same as Task 4), MSB asset placement, and the
/// entity-id/event pre-partition math (mirrors DeathMarkerTests).
/// </summary>
public class GateDecorInjectorTests
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
    public void CollectGates_FiltersOnDestinationClusterType()
    {
        var connections = new List<Connection>
        {
            Conn("m10_00_00_00_AEG099_001_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 1),
            Conn("m10_00_00_00_AEG099_003_9000", "m12_00_00_00_AEG099_004_9000", "arena_zone", 2),
        };
        var eventMap = new Dictionary<string, string> { ["1"] = "mini1", ["2"] = "arena1" };
        var gates = GateDecorInjector.CollectGatesByMap(
            connections, eventMap, Nodes, new Dictionary<string, (string, string)>());

        Assert.True(gates.ContainsKey("m31_00_00_00"));   // mini_dungeon entrance
        Assert.False(gates.ContainsKey("m12_00_00_00"));  // boss arena filtered out
        Assert.Equal("AEG099_002_9000", Assert.Single(gates["m31_00_00_00"]).PartName);
    }

    [Fact]
    public void CollectGates_DedupesSameGateAcrossConnections()
    {
        var connections = new List<Connection>
        {
            Conn("m10_00_00_00_AEG099_001_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 1),
            Conn("m10_00_00_00_AEG099_005_9000", "m31_00_00_00_AEG099_002_9000", "cave_zone", 3),
        };
        var eventMap = new Dictionary<string, string> { ["1"] = "mini1", ["3"] = "mini1" };
        var gates = GateDecorInjector.CollectGatesByMap(
            connections, eventMap, Nodes, new Dictionary<string, (string, string)>());

        Assert.Single(gates["m31_00_00_00"]);
    }

    [Fact]
    public void ApplyToMsb_PlacesEntryCountAssetsAtCorrectRadiusAndEntityIds()
    {
        var msb = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG099_090", 2, 2.0f, 4.0f, 0f, 100, 0),
        });

        var (placed, sfxWork) = GateDecorInjector.ApplyToMsb(
            msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        Assert.Equal(2, placed);
        Assert.Empty(sfxWork); // SfxId = 0 means geometry only, no EMEVD work items

        var decorAssets = msb.Parts.Assets.Where(a => a.ModelName == "AEG099_090").ToList();
        Assert.Equal(2, decorAssets.Count);
        Assert.Equal(new uint[] { 755910000, 755910001 }, decorAssets.Select(a => a.EntityID).OrderBy(x => x));

        var gate = msb.Parts.Assets.Single(a => a.Name == "AEG099_002_9000");
        foreach (var asset in decorAssets)
        {
            var d = asset.Position - gate.Position;
            var horizontal = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            Assert.InRange(horizontal, 2.0f, 4.0f);
        }
        Assert.Contains(msb.Models.Assets, m => m.Name == "AEG099_090");
    }

    [Fact]
    public void ApplyToMsb_SfxEntryProducesSfxWorkItems()
    {
        var msb = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG099_090", 1, 1.0f, 2.0f, 0f, 100, 42),
        });

        var (placed, sfxWork) = GateDecorInjector.ApplyToMsb(
            msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        Assert.Equal(1, placed);
        var item = Assert.Single(sfxWork);
        Assert.Equal(755910000u, item.EntityId);
        Assert.Equal(100, item.SfxDummy);
        Assert.Equal(42, item.SfxId);
    }

    [Fact]
    public void ApplyToMsb_DistinctEntriesWithEqualParamsGetDifferentOffsets()
    {
        // Two catalogue entries at the same gate with identical count and
        // radius bands must not land on coincident positions: each entry
        // needs its own PRNG sequence, not a shared one seeded off the raw
        // gate EntityID.
        var msb = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG099_090", 2, 2.0f, 4.0f, 0f, 100, 0),
            new("AEG099_091", 2, 2.0f, 4.0f, 0f, 100, 0),
        });

        GateDecorInjector.ApplyToMsb(msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var gate = msb.Parts.Assets.Single(a => a.Name == "AEG099_002_9000");
        var entry1Offsets = msb.Parts.Assets
            .Where(a => a.ModelName == "AEG099_090")
            .Select(a => a.Position - gate.Position)
            .OrderBy(v => v.X).ToList();
        var entry2Offsets = msb.Parts.Assets
            .Where(a => a.ModelName == "AEG099_091")
            .Select(a => a.Position - gate.Position)
            .OrderBy(v => v.X).ToList();

        Assert.NotEqual(entry1Offsets, entry2Offsets);
    }

    [Fact]
    public void ApplyToMsb_FirstEntryOffsetsDifferFromRawEntityIdSeed()
    {
        // The decor sequence must not coincide with what AmbientSpawnInjector
        // would draw for a greeter at the same gate/arc center, which seeds
        // GenerateArcOffsets directly off the raw gate EntityID.
        var msb = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var entry = new DecorEntry("AEG099_090", 2, 2.0f, 4.0f, 0f, 100, 0);
        var catalog = new DecorCatalog(new List<DecorEntry> { entry });

        var gateAsset = msb.Parts.Assets.Single(a => a.Name == "AEG099_002_9000");
        var rawSeedOffsets = GateGeometry.GenerateArcOffsets(
            gateAsset.EntityID, gateAsset.Rotation.Y, 0f,
            entry.Count, entry.MinRadius, entry.MaxRadius, entry.YOffset)
            .OrderBy(v => v.X).ToList();

        GateDecorInjector.ApplyToMsb(msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var decorOffsets = msb.Parts.Assets
            .Where(a => a.ModelName == "AEG099_090")
            .Select(a => a.Position - gateAsset.Position)
            .OrderBy(v => v.X).ToList();

        Assert.NotEqual(rawSeedOffsets, decorOffsets);
    }

    [Fact]
    public void ApplyToMsb_MissingGateAssetIsSkippedNotThrown()
    {
        var msb = new MSBE();
        var gates = new List<GateDecorInjector.GateSpec> { new("no_such_gate", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG099_090", 1, 1.0f, 2.0f, 0f, 100, 0),
        });

        var (placed, sfxWork) = GateDecorInjector.ApplyToMsb(
            msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        Assert.Equal(0, placed);
        Assert.Empty(sfxWork);
    }

    [Fact]
    public void ApplyToMsb_RegistersDecorModelWithSibPath()
    {
        // FogRando parity (GameDataWriterE addAssetModel): without a SibPath
        // the game may not resolve real geometry models registered by name.
        var msb = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG023_920", 1, 1.0f, 2.0f, 0f, 100, 0),
        });

        GateDecorInjector.ApplyToMsb(msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var model = msb.Models.Assets.Single(m => m.Name == "AEG023_920");
        Assert.Equal(
            @"N:\GR\data\Asset\Environment\geometry\AEG023\AEG023_920\sib\AEG023_920.sib",
            model.SibPath);
    }

    [Fact]
    public void ApplyToMsb_SnapsDecorToNearbyVanillaAssetGroundY()
    {
        // Gate origin sits 1.3m above the floor; two hand-placed vanilla
        // assets nearby define the ground. Decor must sit at their median Y,
        // not at the gate's own Y.
        var msb = MakeMsbWithGateAndVanillaAsset();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG020_100_1000", ModelName = "AEG020_100",
            Position = new Vector3(8f, -1.4f, 10f), EntityID = 0,
        });
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG020_100_1001", ModelName = "AEG020_100",
            Position = new Vector3(12f, -1.2f, 11f), EntityID = 0,
        });
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG023_920", 2, 2.0f, 4.0f, 0f, 100, 0),
        });

        GateDecorInjector.ApplyToMsb(msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var decorAssets = msb.Parts.Assets.Where(a => a.ModelName == "AEG023_920").ToList();
        Assert.Equal(2, decorAssets.Count);
        Assert.All(decorAssets, a => Assert.Equal(-1.3f, a.Position.Y, 3));
    }

    [Fact]
    public void ApplyToMsb_GroundEstimateIgnoresEnemiesAndFogGateAssets()
    {
        // Enemy parts are excluded on purpose: AmbientSpawnInjector's
        // greeters (EntityID = 0) are already in the MSB when this injector
        // runs, and their Y comes from the same unreliable gate origin.
        // AEG099_* assets (other fog gates, warp doors) are no floor
        // evidence either. With only those nearby, decor keeps the gate Y.
        var msb = MakeMsbWithGateAndVanillaAsset();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9000", ModelName = "c5280",
            Position = new Vector3(9f, -1.5f, 10f), EntityID = 0,
        });
        // Two AEG099 assets with vanilla entity IDs: were the model-prefix
        // filter dropped, they would form a 2-sample median at -1.5 and
        // shift the decor off the gate Y.
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_001_9500", ModelName = "AEG099_001",
            Position = new Vector3(11f, -1.5f, 10f), EntityID = 30051801,
        });
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_065_9501", ModelName = "AEG099_065",
            Position = new Vector3(10f, -1.5f, 12f), EntityID = 30051950,
        });
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG023_920", 1, 2.0f, 4.0f, 0f, 100, 0),
        });

        GateDecorInjector.ApplyToMsb(msb, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var decor = msb.Parts.Assets.Single(a => a.ModelName == "AEG023_920");
        Assert.Equal(0f, decor.Position.Y, 3);
    }

    [Fact]
    public void ApplyToMsb_DecorGetsDeterministicNonZeroYaw()
    {
        var msb1 = MakeMsbWithGateAndVanillaAsset();
        var msb2 = MakeMsbWithGateAndVanillaAsset();
        var gates = new List<GateDecorInjector.GateSpec> { new("AEG099_002_9000", IsASide: false) };
        var catalog = new DecorCatalog(new List<DecorEntry>
        {
            new("AEG023_920", 3, 2.0f, 4.0f, 0f, 100, 0),
        });

        GateDecorInjector.ApplyToMsb(msb1, gates, catalog, entityIdBase: 755910000, log: _ => { });
        GateDecorInjector.ApplyToMsb(msb2, gates, catalog, entityIdBase: 755910000, log: _ => { });

        var yaws1 = msb1.Parts.Assets.Where(a => a.ModelName == "AEG023_920")
            .Select(a => a.Rotation.Y).ToList();
        var yaws2 = msb2.Parts.Assets.Where(a => a.ModelName == "AEG023_920")
            .Select(a => a.Rotation.Y).ToList();
        Assert.Equal(yaws1, yaws2);
        Assert.All(yaws1, y => Assert.InRange(y, 0f, 360f));
        Assert.Contains(yaws1, y => y != 0f);
    }

    private static MSBE MakeMsbWithGateAndVanillaAsset()
    {
        var msb = new MSBE();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_002_9000", ModelName = "AEG099_002",
            Position = new Vector3(10f, 0f, 10f), EntityID = 755890001,
        });
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_500_0000", ModelName = "AEG099_500",
            Position = new Vector3(0f, 0f, 0f), EntityID = 12345,
        });
        return msb;
    }

    [Fact]
    public void PlanAllocations_AssignsContiguousEntityBlocksAndOneEventPerMapWhenSfx()
    {
        var plans = GateDecorInjector.PlanAllocations(new[]
        {
            ("m10_00_00_00", 3),
            ("m11_00_00_00", 5),
        }, hasSfxEntries: true);

        Assert.Equal(2, plans.Count);
        Assert.Equal(SpeedFogIds.HalloweenDecorEntityBase, plans[0].EntityIdBase);
        Assert.Equal(SpeedFogIds.HalloweenDecorEntityBase + 3, plans[1].EntityIdBase);
        Assert.Equal(0, plans[0].EventOffsetBase);
        Assert.Equal(1, plans[1].EventOffsetBase);
    }

    [Fact]
    public void PlanAllocations_NoEventSlotsWhenCatalogueHasNoSfx()
    {
        var plans = GateDecorInjector.PlanAllocations(new[]
        {
            ("m10_00_00_00", 2),
        }, hasSfxEntries: false);

        Assert.Equal(-1, plans[0].EventOffsetBase);
    }

    [Fact]
    public void PlanAllocations_ThrowsWhenEventBudgetExceeded()
    {
        var maps = Enumerable.Range(0, SpeedFogIds.HalloweenDecorEvents.Capacity + 1)
            .Select(i => ($"m{i:D2}_00_00_00", 1));

        var ex = Assert.Throws<InvalidOperationException>(
            () => GateDecorInjector.PlanAllocations(maps, hasSfxEntries: true));
        Assert.Contains("budget", ex.Message);
    }
}
