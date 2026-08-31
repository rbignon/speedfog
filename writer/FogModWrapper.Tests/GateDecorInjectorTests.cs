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
