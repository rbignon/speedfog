using System.Numerics;
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
    public void ApplyVanillaOverrides_RepointsMiniMidraToBossArea()
    {
        // Vanilla foglocations2 files "Mini Midra" (the phase-1 Midra inside
        // the boss arena) under the surrounding manse area; when only
        // midramanse_boss is in the DAG the part has no tier and FogMod
        // silently skips its rescale (AllowUnlinked).
        var locations = new FogLocations
        {
            EnemyAreas = new List<EnemyLocArea>
            {
                new EnemyLocArea { Name = "midramanse", ScalingTier = 31 },
                new EnemyLocArea { Name = "midramanse_boss", ScalingTier = 32 },
            },
            Enemies = new List<EnemyLoc>
            {
                new EnemyLoc { Map = "m28_00_00_00", ID = "c5050_9000", AArea = "midramanse" },
                new EnemyLoc { Map = "m28_00_00_00", ID = "c5051_9000", AArea = "midramanse_boss" },
            },
        };

        var changed = HelperAreaResolver.ApplyVanillaOverrides(locations, _ => { });

        Assert.Equal(1, changed);
        Assert.Equal("midramanse_boss",
            locations.Enemies.Single(l => l.ID == "c5050_9000").ActualArea);
        Assert.Null(locations.Enemies.Single(l => l.ID == "c5051_9000").Area);
    }

    [Fact]
    public void ApplyVanillaOverrides_TargetAreaMissingFromEnemyAreas_LeavesEntryAlone()
    {
        // FogMod indexes EnemyAreas by name and would throw on an EnemyLoc
        // pointing at an area with no EnemyLocArea entry; never re-point to one.
        var locations = new FogLocations
        {
            EnemyAreas = new List<EnemyLocArea>
            {
                new EnemyLocArea { Name = "midramanse", ScalingTier = 31 },
            },
            Enemies = new List<EnemyLoc>
            {
                new EnemyLoc { Map = "m28_00_00_00", ID = "c5050_9000", AArea = "midramanse" },
            },
        };

        var changed = HelperAreaResolver.ApplyVanillaOverrides(locations, _ => { });

        Assert.Equal(0, changed);
        Assert.Equal("midramanse", locations.Enemies.Single().ActualArea);
    }

    [Fact]
    public void ApplyVanillaOverrides_NoMatchingEntries_IsNoOp()
    {
        var locations = MakeLocations();

        var changed = HelperAreaResolver.ApplyVanillaOverrides(locations, _ => { });

        Assert.Equal(0, changed);
        Assert.All(locations.Enemies, l => Assert.Null(l.Area));
    }

    [Fact]
    public void ApplyVanillaOverrides_AlreadyPointingAtTarget_CountsNothing()
    {
        // Idempotence, and resilience to a future FogRando version fixing the
        // assignment upstream.
        var locations = new FogLocations
        {
            EnemyAreas = new List<EnemyLocArea>
            {
                new EnemyLocArea { Name = "midramanse_boss", ScalingTier = 32 },
            },
            Enemies = new List<EnemyLoc>
            {
                new EnemyLoc { Map = "m28_00_00_00", ID = "c5050_9000", AArea = "midramanse_boss" },
            },
        };

        var changed = HelperAreaResolver.ApplyVanillaOverrides(locations, _ => { });

        Assert.Equal(0, changed);
        Assert.Null(locations.Enemies.Single().Area);
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

    // --- Model pass (graph.json helper_models) ---------------------------
    // Rennala's students in Rykard's arena: the source's declared Groups sit
    // on the students only, so the slot never carries the randomizer group
    // and the group pass cannot link them. graph.json says arena 16000801
    // received a source whose helpers are c2040 (and a c0100 dummy).

    private static readonly Dictionary<uint, IReadOnlyList<string>> RykardHelperModels = new()
    {
        [16000801] = new[] { "c2040", "c0100" },
    };

    private static bool TwoBossAreas(string area) => area is "volcano_rykard" or "volcano_other";

    private static HelperAreaResolver.EnemyPart Slot(string name, uint entity, Vector3 pos)
        => new(name, new uint[] { 16005802 }, null, entity, pos);

    private static HelperAreaResolver.EnemyPart Clone(string name, uint entity, Vector3 pos, uint group = 19005007)
        => new(name, new uint[] { group }, null, entity, pos);

    [Fact]
    public void ClonedPartMatchingSourceHelperModel_GetsBossArea()
    {
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Clone("c2040_0138", 4000038, new Vector3(3, 0, 2)),
            Clone("c2040_0139", 4000039, new Vector3(-2, 0, 4)),
            // Vanilla part of the same model elsewhere in the map: never touched.
            new("c2040_9000", Array.Empty<uint>(), "h003100", 16000450, new Vector3(120, 0, 5)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), EligibleBossArea, EligibleBossArea, RykardHelperModels);

        Assert.Equal(new[] { "c2040_0138", "c2040_0139" }, added.Select(l => l.ID).Order().ToArray());
        Assert.All(added, loc => Assert.Equal("volcano_rykard", loc.ActualArea));
    }

    [Fact]
    public void ClonedPartOfAnotherModel_IsNotAdded()
    {
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Clone("c3560_0180", 4000180, new Vector3(3, 0, 2)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), EligibleBossArea, EligibleBossArea, RykardHelperModels);

        Assert.Empty(added);
    }

    [Fact]
    public void PartAlreadyResolvable_IsNotAdded()
    {
        // Resolvable by name (already in Enemies) or by a group declared on
        // some area: FogMod handles it, and a duplicate name entry would
        // make FogMod's ToDictionary throw.
        var locations = MakeLocations();
        locations.Enemies.Add(new EnemyLoc { Map = Map, ID = "c2040_0138", AArea = "volcano_rykard" });
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Clone("c2040_0138", 4000038, new Vector3(3, 0, 2)),
            Clone("c2040_0139", 4000039, new Vector3(3, 0, 2), group: 16005100),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, locations, EligibleBossArea, EligibleBossArea, RykardHelperModels);

        Assert.Empty(added);
    }

    [Fact]
    public void TwoArenasSharingHelperModel_NearestSlotWins()
    {
        var locations = MakeLocations();
        locations.EnemyAreas.Add(new EnemyLocArea { Name = "volcano_other", ScalingTier = 12 });
        locations.Enemies.Add(new EnemyLoc { Map = Map, ID = "c5000_9000", AArea = "volcano_other" });
        var models = new Dictionary<uint, IReadOnlyList<string>>
        {
            [16000801] = new[] { "c2040" },
            [16000850] = new[] { "c2040" },
        };
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Slot("c5000_9000", 16000850, new Vector3(100, 0, 0)),
            Clone("c2040_0138", 4000038, new Vector3(98, 0, 1)),
            Clone("c2040_0139", 4000039, new Vector3(-1, 0, 3)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, locations, TwoBossAreas, TwoBossAreas, models);

        Assert.Equal(2, added.Count);
        Assert.Equal("volcano_other", added.Single(l => l.ID == "c2040_0138").ActualArea);
        Assert.Equal("volcano_rykard", added.Single(l => l.ID == "c2040_0139").ActualArea);
    }

    [Fact]
    public void SlotAreaNotEligible_ClonesNotAdded()
    {
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Clone("c2040_0138", 4000038, new Vector3(3, 0, 2)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), _ => false, EligibleBossArea, RykardHelperModels);

        Assert.Empty(added);
    }

    [Fact]
    public void VanillaSuffixedPartOfHelperModel_IsNotAdded()
    {
        // Only randomizer clones (part index 100-8999, CloneEnemy's
        // helperModelBase) qualify; vanilla parts use 9xxx (four use 0000-0003).
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            new("c2040_9001", Array.Empty<uint>(), null, 16000451, new Vector3(1, 0, 1)),
            new("c2040_0002", Array.Empty<uint>(), null, 16000452, new Vector3(1, 0, 1)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), EligibleBossArea, EligibleBossArea, RykardHelperModels);

        Assert.Empty(added);
    }

    [Fact]
    public void PrefixedCloneName_MatchesBareModel()
    {
        // Open-world tiles name parts "m60_52_38_00-c0000_0109".
        var models = new Dictionary<uint, IReadOnlyList<string>> { [16000801] = new[] { "c0000" } };
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Clone("m60_52_38_00-c0000_0109", 4000009, new Vector3(1, 0, 1)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), EligibleBossArea, EligibleBossArea, models);

        Assert.Equal("m60_52_38_00-c0000_0109", Assert.Single(added).ID);
    }

    [Fact]
    public void CloneNearestToNonDagBossSlot_IsNotAdded()
    {
        // The randomizer also randomizes boss slots outside the DAG; their
        // clones share the map and may share a helper model. A clone whose
        // nearest boss slot belongs to another area is not ours to tag.
        var locations = MakeLocations();
        locations.EnemyAreas.Add(new EnemyLocArea { Name = "volcano_abductors", ScalingTier = 12 });
        locations.Enemies.Add(new EnemyLoc { Map = Map, ID = "c3800_9000", AArea = "volcano_abductors" });
        var models = new Dictionary<uint, IReadOnlyList<string>> { [16000801] = new[] { "c0000" } };
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9001", 16000801, new Vector3(0, 0, 0)),
            Slot("c3800_9000", 16000850, new Vector3(100, 0, 0)),
            Clone("c0000_0200", 4000200, new Vector3(98, 0, 2)),
            Clone("c0000_0201", 4000201, new Vector3(2, 0, 1)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, locations, EligibleBossArea,
            area => area is "volcano_rykard" or "volcano_abductors", models);

        Assert.Equal("c0000_0201", Assert.Single(added).ID);
    }

    [Fact]
    public void CloneNearerToOtherPhaseSlotOfSameArena_IsStillAdded()
    {
        // Two-phase arena: each slot lists its own source's helpers; a clone
        // sitting closer to the other phase's slot still belongs to the arena.
        var models = new Dictionary<uint, IReadOnlyList<string>>
        {
            [16000800] = new[] { "c0000" },
            [16000801] = new[] { "c2040" },
        };
        var parts = new List<HelperAreaResolver.EnemyPart>
        {
            Slot("c4710_9000", 16000800, new Vector3(0, 0, 0)),
            Slot("c4710_9001", 16000801, new Vector3(1, 0, 0)),
            Clone("c2040_0138", 4000038, new Vector3(-0.5f, 0, 0)),
        };

        var added = HelperAreaResolver.ComputeModelAdditions(
            Map, parts, MakeLocations(), EligibleBossArea, EligibleBossArea, models);

        Assert.Equal("volcano_rykard", Assert.Single(added).ActualArea);
    }
}
