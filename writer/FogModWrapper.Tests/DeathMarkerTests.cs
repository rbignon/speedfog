using System.Numerics;
using FogModWrapper;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class DeathMarkerTests
{
    [Fact]
    public void DetachVisibilityGroups_ZeroesGroupsAndPreservesScalars()
    {
        // MSBE's UnkStruct1.DeepCopy only clones CollisionMask: a cloned
        // bloodstain SHARES the base asset's DisplayGroups/DrawGroups arrays,
        // so a restrictive inherited DisplayGroups (e.g. an interior prop's
        // display cell, found on Fort of Reprimand's chapel props) culls the
        // marker and its SFX. Detaching must zero both group arrays (the
        // profile every working map's bloodstains ship with) while keeping
        // the scalar display-condition fields.
        var baseAsset = new MSBE.Part.Asset();
        baseAsset.Unk1.DisplayGroups[0] = 0x10;
        baseAsset.Unk1.DrawGroups[0] = 0x8;
        baseAsset.Unk1.CollisionMask[0] = 0x4;
        baseAsset.Unk1.UnkC4 = -1;
        baseAsset.Unk1.Condition2 = 1;

        var clone = (MSBE.Part.Asset)baseAsset.DeepCopy();
        DeathMarkerInjector.DetachVisibilityGroups(clone);

        Assert.All(clone.Unk1.DisplayGroups, g => Assert.Equal(0u, g));
        Assert.All(clone.Unk1.DrawGroups, g => Assert.Equal(0u, g));
        Assert.Equal(0x4u, clone.Unk1.CollisionMask[0]);
        Assert.Equal(-1, clone.Unk1.UnkC4);
        Assert.Equal(1, clone.Unk1.Condition2);

        // The base asset keeps its own values: the clone no longer aliases
        // its arrays.
        Assert.Equal(0x10u, baseAsset.Unk1.DisplayGroups[0]);
        Assert.Equal(0x8u, baseAsset.Unk1.DrawGroups[0]);
    }

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

    [Fact]
    public void PlanAllocations_AssignsContiguousDisjointBlocks()
    {
        var plans = DeathMarkerInjector.PlanAllocations(new[]
        {
            (MapId: "m10_00_00_00", SpecCount: 3, DistinctFlagCount: 2),
            (MapId: "m11_00_00_00", SpecCount: 5, DistinctFlagCount: 1),
            (MapId: "m12_00_00_00", SpecCount: 1, DistinctFlagCount: 1),
        });

        Assert.Equal(3, plans.Count);
        // Entity IDs start above FogMod's range and advance by SpecCount.
        Assert.Equal(755900000u, plans[0].EntityIdBase);
        Assert.Equal(755900003u, plans[1].EntityIdBase);
        Assert.Equal(755900008u, plans[2].EntityIdBase);
        // Event offsets advance by DistinctFlagCount (one event per flag per map).
        Assert.Equal(0, plans[0].EventOffsetBase);
        Assert.Equal(2, plans[1].EventOffsetBase);
        Assert.Equal(3, plans[2].EventOffsetBase);
        Assert.Equal("m11_00_00_00", plans[1].MapId);
    }

    [Fact]
    public void PlanAllocations_ThrowsWhenEventBudgetExceeded()
    {
        var maps = Enumerable.Range(0, SpeedFogIds.DeathMarkerEvents.Capacity + 1)
            .Select(i => (MapId: $"m{i:D2}_00_00_00", SpecCount: 1, DistinctFlagCount: 1));

        var ex = Assert.Throws<InvalidOperationException>(
            () => DeathMarkerInjector.PlanAllocations(maps));
        Assert.Contains("budget", ex.Message);
    }

    [Fact]
    public void PlanAllocations_AcceptsExactlyFullEventBudget()
    {
        var maps = Enumerable.Range(0, SpeedFogIds.DeathMarkerEvents.Capacity)
            .Select(i => (MapId: $"m{i:D2}_00_00_00", SpecCount: 1, DistinctFlagCount: 1));

        var plans = DeathMarkerInjector.PlanAllocations(maps);

        Assert.Equal(SpeedFogIds.DeathMarkerEvents.Capacity, plans.Count);
    }

    [Fact]
    public void PlanAllocations_EmptyInputYieldsEmptyPlan()
    {
        Assert.Empty(DeathMarkerInjector.PlanAllocations(
            Array.Empty<(string, int, int)>()));
    }
}
