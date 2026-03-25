using System.Numerics;
using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class DeathMarkerTests
{
    [Fact]
    public void GenerateOffsets_BSide_PlacesInFrontOfGate()
    {
        // BSide (isASide=false): arc centered at 0 degrees (gate's facing direction).
        // The ASide warp region is in the facing direction, but the player stands
        // on the opposite side to trigger it. So BSide placement = facing direction.
        // Gate facing +Z (rotY=0), so BSide offsets should have positive Z.
        var offsets = DeathMarkerInjector.GenerateOffsets(100, 0f, isASide: false);

        Assert.Equal(3, offsets.Length);
        foreach (var offset in offsets)
        {
            Assert.True(offset.Z > 0, $"BSide offset Z should be positive (facing direction), got {offset.Z}");
        }
    }

    [Fact]
    public void GenerateOffsets_ASide_PlacesBehindGate()
    {
        // ASide (isASide=true): arc centered at 180 degrees (opposite gate facing).
        // Gate facing +Z (rotY=0), so ASide offsets should have negative Z.
        var offsets = DeathMarkerInjector.GenerateOffsets(100, 0f, isASide: true);

        Assert.Equal(3, offsets.Length);
        foreach (var offset in offsets)
        {
            Assert.True(offset.Z < 0, $"ASide offset Z should be negative (opposite facing), got {offset.Z}");
        }
    }

    [Fact]
    public void GenerateOffsets_ASide_And_BSide_AreOnOppositeSides()
    {
        uint entityId = 42;
        float rotY = 45f; // arbitrary rotation

        var aSideOffsets = DeathMarkerInjector.GenerateOffsets(entityId, rotY, isASide: true);
        var bSideOffsets = DeathMarkerInjector.GenerateOffsets(entityId, rotY, isASide: false);

        // Compute centroid of each set
        var aCentroid = Average(aSideOffsets);
        var bCentroid = Average(bSideOffsets);

        // The centroids should be on opposite sides of the gate (dot product of
        // their XZ directions should be negative).
        float dot = aCentroid.X * bCentroid.X + aCentroid.Z * bCentroid.Z;
        Assert.True(dot < 0, $"ASide and BSide centroids should be on opposite sides, dot={dot}");
    }

    [Fact]
    public void ParseGateFullName_SplitsCorrectly()
    {
        var (mapId, partName) = DeathMarkerInjector.ParseGateFullName("m10_00_00_00_AEG099_002_9000");
        Assert.Equal("m10_00_00_00", mapId);
        Assert.Equal("AEG099_002_9000", partName);
    }

    private static Vector3 Average(Vector3[] vectors)
    {
        var sum = Vector3.Zero;
        foreach (var v in vectors)
            sum += v;
        return sum / vectors.Length;
    }
}
