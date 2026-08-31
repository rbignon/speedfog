using System.Collections.Generic;
using System.Numerics;
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
    public void EstimateGroundY_ReturnsMedianOfNearbyCandidates()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 8.6f, 0f),
            new Vector3(-3f, 8.8f, 1f),
            new Vector3(0f, 9.0f, -4f),
        };
        Assert.Equal(8.8f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_AveragesMiddlePairOnEvenCount()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 8.6f, 0f),
            new Vector3(-3f, 8.8f, 1f),
        };
        Assert.Equal(8.7f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_IgnoresCandidatesBeyondHorizontalRadiusOrDeltaY()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 9.4f, 0f),
            new Vector3(-1f, 9.6f, 2f),
            new Vector3(20f, 9.4f, 0f),   // beyond 6m horizontal (dY in range)
            new Vector3(1f, 14.0f, 0f),   // wall torch: above the +0.5m cap
        };
        Assert.Equal(9.5f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_WallPropsAboveGateAreNotFloorEvidence()
    {
        // The Shadow Keep regression (gate AEG099_230_9500): the gate origin
        // was AT floor level, but two wall props at +2.3m outvoted the
        // single floor asset under a symmetric vertical window and pulled
        // decor to mid-gate height. This exact mix is guarded twice (the
        // asymmetric cap rejects the wall props; even symmetric, the 2.3m
        // spread trips the consensus guard); the window alone is pinned by
        // EstimateGroundY_AgreeingWallPropsAloneCannotRaiseTheEstimate.
        var gate = new Vector3(0f, 263.0f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 263.0f, 2f),   // floor asset
            new Vector3(-3f, 265.3f, 1f),  // wall prop
            new Vector3(2f, 265.3f, -2f),  // wall prop
        };
        Assert.Equal(263.0f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_AgreeingWallPropsAloneCannotRaiseTheEstimate()
    {
        // Two matching wall props flanking a gate with no other evidence in
        // range: they agree within the consensus tolerance, so only the
        // asymmetric +0.5m cap stands between them and a +2m (clamped)
        // upward correction. A symmetric window would return 265.0 here.
        var gate = new Vector3(0f, 263.0f, 0f);
        var candidates = new[]
        {
            new Vector3(-3f, 265.3f, 1f),  // wall prop
            new Vector3(2f, 265.3f, -2f),  // wall prop
        };
        Assert.Equal(263.0f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_AcceptsFloorSlightlyAboveGateOrigin()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 10.4f, 0f),
            new Vector3(-3f, 10.4f, 1f),
        };
        Assert.Equal(10.4f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_MixedLevelNeighborhoodFallsBackToGateY()
    {
        // Stairs / ledges: candidates on different levels (spread beyond the
        // consensus tolerance) must not be blended into an arbitrary median
        // between them.
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 9.8f, 0f),
            new Vector3(-2f, 8.6f, 1f),
        };
        Assert.Equal(10f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void EstimateGroundY_FallsBackToGateYWithFewerThanTwoSamples()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[] { new Vector3(2f, 8.0f, 0f) };
        Assert.Equal(10f, GateGeometry.EstimateGroundY(gate, candidates), 3);
        Assert.Equal(10f, GateGeometry.EstimateGroundY(gate, new List<Vector3>()), 3);
    }

    [Fact]
    public void EstimateGroundY_ClampsCorrectionToMaxCorrection()
    {
        var gate = new Vector3(0f, 10f, 0f);
        var candidates = new[]
        {
            new Vector3(2f, 7.6f, 0f),
            new Vector3(-3f, 7.6f, 1f),
        };
        Assert.Equal(8.0f, GateGeometry.EstimateGroundY(gate, candidates), 3);
    }

    [Fact]
    public void GenerateYaws_IsDeterministicAndInRange()
    {
        var a = GateGeometry.GenerateYaws(755910001u, 4);
        var b = GateGeometry.GenerateYaws(755910001u, 4);
        Assert.Equal(a, b);
        Assert.Equal(4, a.Length);
        Assert.All(a, y => Assert.InRange(y, 0f, 360f));
        Assert.Contains(a, y => y != 0f);
    }

    [Fact]
    public void GenerateYaws_DrawsADistinctStreamFromArcOffsets()
    {
        // Same seed as a GenerateArcOffsets call: the yaw stream must not
        // replay the arc PRNG (which would correlate yaw with angle).
        var yaws = GateGeometry.GenerateYaws(42u, 2);
        var replayed = new Random(42u.GetHashCode());
        Assert.NotEqual(yaws[0], (float)(replayed.NextDouble() * 360.0));
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
