using FogMod;
using Xunit;
using static FogMod.AnnotationData;

namespace FogModWrapper.Tests;

public class HelperAreaResolverTests
{
    private const string Map = "m16_00_00_00";

    // Mirrors the volcano_rykard situation: the arena area is declared with
    // Groups only (vanilla boss group 16005800), the enemy randomizer clears
    // that group from the boss slots and puts its own (16005802) on both the
    // slots and the helpers it creates.
    private static FogLocations MakeLocations()
    {
        return new FogLocations
        {
            EnemyAreas = new List<EnemyLocArea>
            {
                new EnemyLocArea { Name = "volcano_rykard", Groups = "16005800", ScalingTier = 12 },
                new EnemyLocArea
                {
                    Name = "volcano_town",
                    Groups = "16005100 16005510",
                    Cols = "m16_00_00_00_h003100",
                    MainMap = Map,
                    ScalingTier = 11,
                },
            },
            Enemies = new List<EnemyLoc>
            {
                new EnemyLoc { Map = Map, ID = "c4710_9000", AArea = "volcano_rykard" },
                new EnemyLoc { Map = Map, ID = "c4710_9001", AArea = "volcano_rykard" },
            },
        };
    }

    private static bool EligibleBossArea(string area) => area == "volcano_rykard";

    [Fact]
    public void HelperSharingBossSlotGroup_GetsEnemyLocForBossArea()
    {
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c4710_9001", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
            new("c3570_0181", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        Assert.Equal(2, added.Count);
        Assert.All(added, loc =>
        {
            Assert.Equal(Map, loc.Map);
            Assert.Equal("volcano_rykard", loc.ActualArea);
        });
        Assert.Equal(new[] { "c3560_0180", "c3570_0181" }, added.Select(l => l.ID).Order().ToArray());
    }

    [Fact]
    public void PartResolvableByName_IsNotAdded()
    {
        // The boss slots themselves resolve by part name; they must not get
        // duplicate EnemyLoc entries even though they carry the shared group.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c4710_9001", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        Assert.Empty(added);
    }

    [Fact]
    public void PartWithKnownAreaGroup_IsNotAdded()
    {
        // A part whose group is already declared in some area's Groups is
        // resolvable by FogMod's group lookup; we must not shadow it.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c9999_0000", new uint[] { 16005100, 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        Assert.Empty(added);
    }

    [Fact]
    public void PartWithoutSharedBossGroup_IsNotAdded()
    {
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c9999_0000", Array.Empty<uint>(), null),
            new("c9999_0001", new uint[] { 77777777 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        Assert.Empty(added);
    }

    [Fact]
    public void HelperWithResolvableCollision_IsStillAdded()
    {
        // FogMod would resolve this helper via its collision to volcano_town,
        // but the boss-group signal is stronger: the part belongs to the boss
        // fight. Name-based EnemyLoc entries take priority over collisions in
        // FogMod's lookup, so adding one fixes the area.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, "h003100"),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        var loc = Assert.Single(added);
        Assert.Equal("c3560_0180", loc.ID);
        Assert.Equal("volcano_rykard", loc.ActualArea);
    }

    [Fact]
    public void BossAreaNotEligible_HelpersNotAdded()
    {
        // Area outside the DAG (no tier) or without a defeat flag: leave the
        // helpers alone, FogMod's existing fallbacks apply.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), _ => false);

        Assert.Empty(added);
    }

    [Fact]
    public void GroupSharedByTwoBossAreas_IsIgnored()
    {
        var locations = MakeLocations();
        locations.EnemyAreas.Add(new EnemyLocArea { Name = "volcano_other", ScalingTier = 12 });
        locations.Enemies.Add(new EnemyLoc { Map = Map, ID = "c5000_9000", AArea = "volcano_other" });

        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c5000_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(
            Map, parts, locations, area => area is "volcano_rykard" or "volcano_other");

        Assert.Empty(added);
    }

    [Fact]
    public void BossAreaMissingFromEnemyAreas_HelpersNotAdded()
    {
        // FogMod indexes EnemyAreas by name and would throw on an area with no
        // EnemyLocArea entry; never emit EnemyLocs pointing at one.
        var locations = MakeLocations();
        locations.EnemyAreas.RemoveAll(a => a.Name == "volcano_rykard");

        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, locations, EligibleBossArea);

        Assert.Empty(added);
    }

    [Fact]
    public void HelperAlreadyInEnemyLocs_IsNotDuplicated()
    {
        var locations = MakeLocations();
        locations.Enemies.Add(new EnemyLoc { Map = Map, ID = "c3560_0180", AArea = "volcano_rykard" });

        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, locations, EligibleBossArea);

        Assert.Empty(added);
    }

    [Fact]
    public void DuplicatePartNames_EmitOneEntry()
    {
        // FogMod builds its name dictionary with ToDictionary, which throws
        // on duplicate (Map, ID) pairs. Part names are unique in real MSBs;
        // this is defensive.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
            new("c3560_0180", new uint[] { 16005802 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        var loc = Assert.Single(added);
        Assert.Equal("c3560_0180", loc.ID);
    }

    [Fact]
    public void EligibleMaps_KeepsOnlyMapsWithEligibleBossSlots()
    {
        // The merge dir contains hundreds of MSBs (every open-world tile the
        // randomizer touched); only maps hosting an eligible boss slot can
        // yield additions, so the scan must be restricted to them.
        var locations = MakeLocations();
        locations.Enemies.Add(new EnemyLoc { Map = "m10_00_00_00", ID = "c3100_9000", AArea = "stormveil" });

        var maps = HelperAreaResolver.EligibleMaps(locations, EligibleBossArea);

        Assert.Equal(new[] { Map }, maps.Order().ToArray());
    }

    [Fact]
    public void ZeroGroupIds_AreIgnored()
    {
        // MSB EntityGroupIDs arrays are zero-padded; 0 must never act as a
        // shared boss group.
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            new("c4710_9000", new uint[] { 16005802, 0, 0 }, null),
            new("c9999_0000", new uint[] { 0, 0, 0 }, null),
        };

        var added = HelperAreaResolver.ComputeAdditions(Map, parts, MakeLocations(), EligibleBossArea);

        Assert.Empty(added);
    }
}
