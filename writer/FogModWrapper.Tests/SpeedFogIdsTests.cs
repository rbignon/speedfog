using Xunit;

namespace FogModWrapper.Tests;

public class SpeedFogIdsTests
{
    // FogMod allocates its own entities/regions from this base upward
    // (DeathMarkerInjector.FOGMOD_ENTITY_MIN); SpeedFog event IDs must stay below.
    private const int FogModEntityMin = 755890000;

    [Fact]
    public void EventRanges_AreDisjoint()
    {
        var ordered = SpeedFogIds.EventRanges.OrderBy(r => r.Base).ToList();
        for (int i = 1; i < ordered.Count; i++)
        {
            var prev = ordered[i - 1];
            var curr = ordered[i];
            Assert.True(
                prev.End <= curr.Base,
                $"Range overlap: {prev.Owner} [{prev.Base}, {prev.End}) collides " +
                $"with {curr.Owner} [{curr.Base}, {curr.End})");
        }
    }

    [Fact]
    public void EventRanges_StayBelowFogModEntityBase()
    {
        foreach (var range in SpeedFogIds.EventRanges)
        {
            Assert.True(
                range.End <= FogModEntityMin,
                $"{range.Owner} range ends at {range.End}, past FogMod base {FogModEntityMin}");
        }
    }

    [Fact]
    public void EventRanges_HavePositiveCapacityAndUniqueOwners()
    {
        Assert.All(SpeedFogIds.EventRanges, r => Assert.True(r.Capacity > 0));
        Assert.Equal(
            SpeedFogIds.EventRanges.Count,
            SpeedFogIds.EventRanges.Select(r => r.Owner).Distinct().Count());
    }

    [Fact]
    public void AuxiliaryFlags_AreUniqueAndOutsideFogModAllocations()
    {
        Assert.Equal(
            SpeedFogIds.AuxiliaryFlags.Count,
            SpeedFogIds.AuxiliaryFlags.Distinct().Count());
        // The shared finger-pickup flag is FogMod's, not one of ours
        Assert.DoesNotContain(SpeedFogIds.FingerPickupFlag, SpeedFogIds.AuxiliaryFlags);
    }
}
