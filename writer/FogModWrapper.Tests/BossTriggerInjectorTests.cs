using FogMod;
using Xunit;

namespace FogModWrapper.Tests;

public class BossTriggerInjectorTests
{
    // --- CollectBossEntrances tests ---

    [Fact]
    public void CollectBossEntrances_BossArea_ReturnsEntrance()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["stormveil_margit"] = new AnnotationData.Area
            {
                Name = "stormveil_margit",
                DefeatFlag = 10000850,
                BossTrigger = 10002855
            }
        };

        var side = new AnnotationData.Side { Area = "stormveil_margit" };
        side.Warp = new Graph.WarpPoint { Region = 755890042 };

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "test connection"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Single(entrances);
        Assert.Equal(755890042, entrances[0].WarpRegion);
        Assert.Equal(10000850, entrances[0].DefeatFlag);
        Assert.Equal(10002855, entrances[0].BossTrigger);
    }

    [Fact]
    public void CollectBossEntrances_NonBossArea_ReturnsEmpty()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["stormveil"] = new AnnotationData.Area
            {
                Name = "stormveil",
                DefeatFlag = 0,
                BossTrigger = 0
            }
        };

        var side = new AnnotationData.Side { Area = "stormveil" };
        side.Warp = new Graph.WarpPoint { Region = 755890042 };

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "non-boss connection"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Empty(entrances);
    }

    [Fact]
    public void CollectBossEntrances_NoWarpData_ReturnsEmpty()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805
            }
        };

        var side = new AnnotationData.Side { Area = "boss_zone" };
        // Warp is null (not populated by Write)

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "no warp data"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Empty(entrances);
    }

    [Fact]
    public void CollectBossEntrances_DuplicateRegionAndTrigger_Deduplicated()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805
            }
        };

        // Two edges with same warp region entering same boss
        var side1 = new AnnotationData.Side { Area = "boss_zone" };
        side1.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge1 = new Graph.Edge { Side = side1 };

        var side2 = new AnnotationData.Side { Area = "boss_zone" };
        side2.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge2 = new Graph.Edge { Side = side2 };

        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge1, "conn 1"));
        result.DeferredEdges.Add((1050292001, edge2, "conn 2"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Single(entrances);
    }

    [Fact]
    public void CollectBossEntrances_DifferentRegions_BothCollected()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805
            }
        };

        // Two edges with different warp regions entering same boss
        var side1 = new AnnotationData.Side { Area = "boss_zone" };
        side1.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge1 = new Graph.Edge { Side = side1 };

        var side2 = new AnnotationData.Side { Area = "boss_zone" };
        side2.Warp = new Graph.WarpPoint { Region = 755890043 };
        var edge2 = new Graph.Edge { Side = side2 };

        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge1, "front entrance"));
        result.DeferredEdges.Add((1050292001, edge2, "side entrance"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Equal(2, entrances.Count);
    }

    [Fact]
    public void CollectBossEntrances_AlternateSide_BothRegionsCollected()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805
            }
        };

        var altSide = new AnnotationData.Side { Area = "boss_zone" };
        altSide.Warp = new Graph.WarpPoint { Region = 755890099 };

        var side = new AnnotationData.Side { Area = "boss_zone" };
        side.Warp = new Graph.WarpPoint { Region = 755890042 };
        side.AlternateSide = altSide;

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "alt connection"));

        var entrances = BossTriggerInjector.CollectBossEntrances(result, areas);

        Assert.Equal(2, entrances.Count);
        Assert.Equal(755890042, entrances[0].WarpRegion);
        Assert.Equal(755890099, entrances[1].WarpRegion);
    }
}
