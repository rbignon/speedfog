using Xunit;

namespace FogModWrapper.Tests;

public class SpeedFogIdsTests
{
    // FogMod allocates its own entities/regions from this base upward
    // (SpeedFogIds.FogModEntityMin); SpeedFog event IDs must stay below.
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

    [Fact]
    public void MsbEntityBands_AreDisjointAndOrdered()
    {
        // Three consecutive MSB entity id bands, each owned by a different
        // injector: FogMod itself, DeathMarkerInjector's bloodstains, and
        // GateDecorInjector's Halloween decorations. Each base must be
        // strictly below the next so none of the three ever hands out an id
        // another one already claims.
        Assert.True(
            SpeedFogIds.FogModEntityMin < SpeedFogIds.DeathMarkerEntityBase,
            $"FogModEntityMin ({SpeedFogIds.FogModEntityMin}) must be below " +
            $"DeathMarkerEntityBase ({SpeedFogIds.DeathMarkerEntityBase})");
        Assert.True(
            SpeedFogIds.DeathMarkerEntityBase < SpeedFogIds.HalloweenDecorEntityBase,
            $"DeathMarkerEntityBase ({SpeedFogIds.DeathMarkerEntityBase}) must be below " +
            $"HalloweenDecorEntityBase ({SpeedFogIds.HalloweenDecorEntityBase})");
    }

    [Fact]
    public void BehaviorRowId_MatchesTheGamesCompositeKey()
    {
        // Vanilla c5280: variation 52800, judge 100 is row 252800100.
        Assert.Equal(252800100, SpeedFogIds.BehaviorRowId(52800, 100));
        // The boss's beam row lands in its own 275589xxx band.
        Assert.Equal(275589150, SpeedFogIds.BehaviorRowId(
            SpeedFogIds.UntouchableBossBehaviorVariation, SpeedFogIds.UntouchableBeamJudge));
    }

    [Fact]
    public void NpcThinkParamRows_DoNotCollide()
    {
        // Both are NpcThinkParam rows (one namespace): the greeter and the
        // boss must not share an id.
        Assert.NotEqual(SpeedFogIds.PassiveGreeterThinkRow, SpeedFogIds.UntouchableBossThinkRow);
    }
}
