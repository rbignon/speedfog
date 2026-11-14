using Xunit;

namespace FogModWrapper.Tests.Core;

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
    public void EventRanges_ListEveryDeclaredRange()
    {
        // A range missing from EventRanges escapes the disjointness checks
        // above. Every IdRange is an event range unless listed here as
        // living in another id namespace, so a forgotten one fails.
        var otherNamespaces = new[] { nameof(SpeedFogIds.BossNameFmgIds) };
        var declared = typeof(SpeedFogIds)
            .GetFields(System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Static)
            .Where(f => f.FieldType == typeof(SpeedFogIds.IdRange) && !otherNamespaces.Contains(f.Name))
            .Select(f => (SpeedFogIds.IdRange)f.GetValue(null)!)
            .ToList();
        Assert.NotEmpty(declared);
        Assert.All(declared, r => Assert.Contains(r, SpeedFogIds.EventRanges));
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
    public void JumpscareLayerFlags_AreSavedModStateFlagsClearOfTheOthers()
    {
        var flags = Enumerable.Range(
            SpeedFogIds.HalloweenJumpscareLayerFlagBase,
            SpeedFogIds.HalloweenJumpscareLayerFlagCount).ToList();
        // Saved category 1050290, offsets 0-99 (docs/event-flags.md): a
        // played layer must survive deaths and quit-outs.
        Assert.All(flags, f => Assert.InRange(f, 1050290000, 1050290099));
        // Offset 0 is items_spawned_flag (allocated Python-side), offset 1
        // the RUN COMPLETE banner guard.
        Assert.DoesNotContain(1050290000, flags);
        Assert.DoesNotContain(SpeedFogIds.BannerShownFlag, flags);
    }

    [Fact]
    public void RaceItems_AreRegisteredAndTheirFlagsClearOfHalloweenAndTheBase()
    {
        Assert.Contains(SpeedFogIds.RaceItemEvents, SpeedFogIds.EventRanges);

        var use = Enumerable.Range(SpeedFogIds.RaceItemUseFlagBase, 100).ToHashSet();
        var remove = Enumerable.Range(SpeedFogIds.RaceItemRemoveFlagBase, 100).ToHashSet();
        Assert.Empty(use.Intersect(remove));

        var others = new HashSet<int> { SpeedFogIds.BannerShownFlag, 1050290000 };
        others.UnionWith(Enumerable.Range(SpeedFogIds.HalloweenJumpscareLayerFlagBase, SpeedFogIds.HalloweenJumpscareLayerFlagCount));
        others.UnionWith(Enumerable.Range(SpeedFogIds.HalloweenJumpscareLeftFlagBase, SpeedFogIds.HalloweenJumpscareLeftFlagCount));
        others.UnionWith(Enumerable.Range(SpeedFogIds.HalloweenJumpscareEnteredFlagBase, SpeedFogIds.HalloweenJumpscareEnteredFlagCount));
        Assert.Empty(use.Intersect(others));
        Assert.Empty(remove.Intersect(others));

        // Saved category 1050290 (flags 1050290000-1050290999).
        Assert.All(use.Concat(remove), f => Assert.Equal(1050290, f / 1000));

        // Param ids: clear of the Phantom skin range right below.
        Assert.True(SpeedFogIds.RaceItemParamIdMin > PhantomCatalogLoader.IdRangeEnd);
    }

    [Fact]
    public void RaceItemEffectRange_SitsInsideTheParamRange()
    {
        Assert.True(SpeedFogIds.RaceItemEffectIdMin <= SpeedFogIds.RaceItemEffectIdMax);
        Assert.True(SpeedFogIds.RaceItemEffectIdMin >= SpeedFogIds.RaceItemParamIdMin);
        Assert.True(SpeedFogIds.RaceItemEffectIdMax <= SpeedFogIds.RaceItemParamIdMax);
    }

    [Fact]
    public void JumpscareMirrorFlags_AreSavedModStateFlagsDisjointFromTheLayerFlags()
    {
        // The mirror flags replace the zone-tracking flags the racing mod
        // clears: they must sit in the saved mod-state category the mod
        // never polls (1050290xxx, offsets 0-99), clear of every other
        // flag of that category.
        var left = Enumerable.Range(
            SpeedFogIds.HalloweenJumpscareLeftFlagBase, SpeedFogIds.HalloweenJumpscareLeftFlagCount);
        var entered = Enumerable.Range(
            SpeedFogIds.HalloweenJumpscareEnteredFlagBase, SpeedFogIds.HalloweenJumpscareEnteredFlagCount);
        var layer = Enumerable.Range(
            SpeedFogIds.HalloweenJumpscareLayerFlagBase, SpeedFogIds.HalloweenJumpscareLayerFlagCount);
        var all = left.Concat(entered).Concat(layer).Append(1050290000).Append(SpeedFogIds.BannerShownFlag).ToList();

        Assert.Equal(all.Count, all.Distinct().Count());
        Assert.All(all, f => Assert.InRange(f, 1050290000, 1050290099));
        // One left flag per possible chosen layer.
        Assert.Equal(SpeedFogIds.HalloweenJumpscareLayerFlagCount, SpeedFogIds.HalloweenJumpscareLeftFlagCount);
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
        // The vanilla-part filters (SpeedFogIds.IsModManagedEntity) only
        // exclude the managed band: a base outside it would let SpeedFog's
        // own parts pass as vanilla clone sources or ground evidence.
        Assert.True(SpeedFogIds.IsModManagedEntity(SpeedFogIds.DeathMarkerEntityBase));
        Assert.True(SpeedFogIds.IsModManagedEntity(SpeedFogIds.HalloweenDecorEntityBase));
        Assert.True(
            SpeedFogIds.HalloweenDecorEntityBase < SpeedFogIds.HalloweenJumpscareEntityBase,
            $"HalloweenDecorEntityBase ({SpeedFogIds.HalloweenDecorEntityBase}) must be below " +
            $"HalloweenJumpscareEntityBase ({SpeedFogIds.HalloweenJumpscareEntityBase})");
        // The whole jumpscare block (one id per event slot) stays managed.
        Assert.True(SpeedFogIds.IsModManagedEntity(SpeedFogIds.HalloweenJumpscareEntityBase));
        Assert.True(SpeedFogIds.IsModManagedEntity(
            SpeedFogIds.HalloweenJumpscareEntityBase + (uint)SpeedFogIds.HalloweenJumpscareEvents.Capacity - 1));
    }

    [Theory]
    [InlineData(0u, false)]          // unset
    [InlineData(30040800u, false)]   // dungeon map (8 digits)
    [InlineData(755889999u, false)]  // just below the band
    [InlineData(755890000u, true)]   // FogModEntityMin, inclusive
    [InlineData(755999999u, true)]   // last managed id
    [InlineData(756000000u, false)]  // ModEntityMaxExclusive, exclusive
    [InlineData(1043300200u, false)] // m60 open-world tile (10 digits)
    [InlineData(2052430800u, false)] // m61 open-world tile (10 digits)
    public void IsModManagedEntity_CoversOnlyTheManagedBand(uint entityId, bool managed)
    {
        Assert.Equal(managed, SpeedFogIds.IsModManagedEntity(entityId));
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
    public void NpcParamRows_DoNotCollide()
    {
        // All NpcParam rows (one namespace), written into the same
        // regulation when the boss, the greeters and the ambushers coexist.
        var rows = new[]
        {
            SpeedFogIds.UntouchableBossNpcRow,
            SpeedFogIds.DecorativeAmbusherNpcRow,
            SpeedFogIds.HostileGreeterNpcRow,
            SpeedFogIds.HalloweenJumpscareNpcRow,
        };
        Assert.Equal(rows.Length, rows.Distinct().Count());
    }

    [Fact]
    public void NpcThinkParamRows_DoNotCollide()
    {
        // Both are NpcThinkParam rows (one namespace): the greeters and the
        // boss must not share an id.
        Assert.NotEqual(SpeedFogIds.HostileGreeterThinkRow, SpeedFogIds.UntouchableBossThinkRow);
    }

    [Fact]
    public void SpEffectParamRows_DoNotCollide()
    {
        // All SpEffectParam rows (one namespace), written into the same
        // regulation when the boss and the Halloween ambushers coexist.
        var rows = new[]
        {
            SpeedFogIds.UntouchableBossSpEffectRow,
            SpeedFogIds.DecorativeAmbusherSpEffectRow,
            SpeedFogIds.UntouchableBrokenSpEffectRow,
            SpeedFogIds.UntouchableNearWarpMarkerSpEffect,
        };
        Assert.Equal(rows.Length, rows.Distinct().Count());
    }
}
