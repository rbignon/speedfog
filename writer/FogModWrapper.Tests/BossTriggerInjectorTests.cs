using FogMod;
using Xunit;

namespace FogModWrapper.Tests;

public class BossTriggerInjectorTests
{
    // --- BuildRegionToTrapFlag tests ---

    [Fact]
    public void BuildRegionToTrapFlag_BossArea_MapsRegion()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["stormveil_margit"] = new AnnotationData.Area
            {
                Name = "stormveil_margit",
                DefeatFlag = 10000850,
                BossTrigger = 10002855,
                TrapFlag = 10000851
            }
        };

        var side = new AnnotationData.Side { Area = "stormveil_margit" };
        side.Warp = new Graph.WarpPoint { Region = 755890042 };

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "test connection"));

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Single(mapping);
        Assert.Equal(10000851, mapping[755890042]);
    }

    [Fact]
    public void BuildRegionToTrapFlag_NonBossArea_ReturnsEmpty()
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

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Empty(mapping);
    }

    [Fact]
    public void BuildRegionToTrapFlag_NoWarpData_ReturnsEmpty()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805,
                TrapFlag = 10000801
            }
        };

        var side = new AnnotationData.Side { Area = "boss_zone" };
        // Warp is null (not populated by Write)

        var edge = new Graph.Edge { Side = side };
        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge, "no warp data"));

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Empty(mapping);
    }

    [Fact]
    public void BuildRegionToTrapFlag_DuplicateRegion_FirstWins()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805,
                TrapFlag = 10000801
            }
        };

        var side1 = new AnnotationData.Side { Area = "boss_zone" };
        side1.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge1 = new Graph.Edge { Side = side1 };

        var side2 = new AnnotationData.Side { Area = "boss_zone" };
        side2.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge2 = new Graph.Edge { Side = side2 };

        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge1, "conn 1"));
        result.DeferredEdges.Add((1050292001, edge2, "conn 2"));

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Single(mapping);
    }

    [Fact]
    public void BuildRegionToTrapFlag_DifferentRegions_BothMapped()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805,
                TrapFlag = 10000801
            }
        };

        var side1 = new AnnotationData.Side { Area = "boss_zone" };
        side1.Warp = new Graph.WarpPoint { Region = 755890042 };
        var edge1 = new Graph.Edge { Side = side1 };

        var side2 = new AnnotationData.Side { Area = "boss_zone" };
        side2.Warp = new Graph.WarpPoint { Region = 755890043 };
        var edge2 = new Graph.Edge { Side = side2 };

        var result = new InjectionResult();
        result.DeferredEdges.Add((1050292000, edge1, "front entrance"));
        result.DeferredEdges.Add((1050292001, edge2, "side entrance"));

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Equal(2, mapping.Count);
        Assert.Equal(10000801, mapping[755890042]);
        Assert.Equal(10000801, mapping[755890043]);
    }

    [Fact]
    public void BuildRegionToTrapFlag_AlternateSide_BothRegionsMapped()
    {
        var areas = new Dictionary<string, AnnotationData.Area>
        {
            ["boss_zone"] = new AnnotationData.Area
            {
                Name = "boss_zone",
                DefeatFlag = 10000800,
                BossTrigger = 10002805,
                TrapFlag = 10000801
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

        var mapping = BossTriggerInjector.BuildRegionToTrapFlag(result, areas);

        Assert.Equal(2, mapping.Count);
        Assert.Equal(10000801, mapping[755890042]);
        Assert.Equal(10000801, mapping[755890099]);
    }
}
