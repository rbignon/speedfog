using System.Collections.Generic;
using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class GateGeometryTests
{
    [Fact]
    public void ParseGateFullName_SplitsMapAndPart()
    {
        var (map, part) = GateGeometry.ParseGateFullName("m10_01_00_00_AEG099_001_9000");
        Assert.Equal("m10_01_00_00", map);
        Assert.Equal("AEG099_001_9000", part);
    }

    [Fact]
    public void GenerateArcOffsets_IsDeterministicPerSeed()
    {
        var a = GateGeometry.GenerateArcOffsets(755900001u, 90f, 180f, 3, 1.5f, 3.0f, 0.13f);
        var b = GateGeometry.GenerateArcOffsets(755900001u, 90f, 180f, 3, 1.5f, 3.0f, 0.13f);
        Assert.Equal(a, b);
        Assert.All(a, v => Assert.Equal(0.13f, v.Y));
        Assert.Equal(3, a.Length);
    }

    [Fact]
    public void GenerateArcOffsets_MatchesDeathMarkerWrapper()
    {
        // The death-marker wrapper must keep producing the exact same
        // offsets as before the refactor (same PRNG seed and draw order).
        var direct = GateGeometry.GenerateArcOffsets(42u, 30f, 180f, 3, 1.5f, 3.0f, 0.13f);
        var viaWrapper = DeathMarkerInjector.GenerateOffsets(42u, 30f, isASide: true);
        Assert.Equal(direct, viaWrapper);
    }

    [Fact]
    public void ResolveIsASide_MatchesAreaSides()
    {
        var sides = new Dictionary<string, (string, string)>
        {
            ["m10_00_00_00_AEG099_001_9000"] = ("zoneA", "zoneB"),
        };
        Assert.True(GateGeometry.ResolveIsASide("m10_00_00_00_AEG099_001_9000", "zoneA", sides));
        Assert.False(GateGeometry.ResolveIsASide("m10_00_00_00_AEG099_001_9000", "zoneB", sides));
        Assert.False(GateGeometry.ResolveIsASide("unknown_gate", "zoneA", sides));
    }
}
