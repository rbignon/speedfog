using System.Numerics;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests;

/// <summary>
/// Tests for HalloweenJumpscareInjector: the actor NpcParam row, the MSB
/// placement of the hidden actors and their EMEVD events. Layer selection
/// itself is covered by JumpscarePlannerTests.
/// </summary>
public class HalloweenJumpscareInjectorTests
{
    private static JumpscareActor Actor(uint entityId, string? anchor = "AEG099_001_9000", int eventId = 755865600)
        => new("mini_a", 1, "m31_02_00_00", 1050290010,
            new[] { 1050294100 }, new[] { 1050294102 }, anchor, entityId, eventId);

    private static MSBE MakeMsb()
    {
        var msb = new MSBE();
        msb.Parts.Assets.Add(new MSBE.Part.Asset
        {
            Name = "AEG099_001_9000", ModelName = "AEG099_001",
            Position = new Vector3(10f, 0f, 10f), EntityID = 755890001,
        });
        // First clone source in list order, far from the gate.
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c9990_9000", ModelName = "c9990", Position = new Vector3(100f, 5f, 100f),
            CollisionPartName = "h_far", WalkRouteName = "h_patrol_route",
        });
        // Nearest to the entrance gate.
        var near = new MSBE.Part.Enemy
        {
            Name = "c9990_9001", ModelName = "c9990", Position = new Vector3(12f, 0f, 10f),
            CollisionPartName = "h_near",
        };
        near.EntityGroupIDs[0] = 31025000;  // must not leak onto the actor
        msb.Parts.Enemies.Add(near);
        return msb;
    }

    [Fact]
    public void ApplyJumpscare_WritesItsOwnFlatTierRowWithoutTheGreeterRow()
    {
        var npc = BuildParamFromDef("NpcParam", 52800086);
        npc[52800086]!["spEffectID3"].Value = 20007100;  // vanilla DLC tier-31 scaling

        HalloweenJumpscareInjector.ApplyJumpscare(npc);

        var row = npc[SpeedFogIds.HalloweenJumpscareNpcRow]!;
        var slots = Enumerable.Range(0, 32).Select(i => (int)row[$"spEffectID{i}"].Value).ToList();
        Assert.DoesNotContain(20007100, slots);
        Assert.Contains(7000 + 10 * AmbientSpawnInjector.GREETER_TIER, slots);
        Assert.Equal(0u, (uint)row["getSoul"].Value);
        Assert.Equal(-1, (int)row["itemLotId_enemy"].Value);
        // Jumpscares must work with greeters = false: no dependency on the
        // greeter row.
        Assert.Null(npc[SpeedFogIds.HostileGreeterNpcRow]);
    }

    [Fact]
    public void ApplyToMsb_ClonesTheVanillaEnemyNearestToTheEntranceGate()
    {
        var msb = MakeMsb();

        var placed = HalloweenJumpscareInjector.ApplyToMsb(msb, new() { Actor(755920000) }, _ => { });

        Assert.Single(placed);
        var actor = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        // Stands on the nearest source's own (walkable) spot, on its collision.
        Assert.Equal(new Vector3(12f, 0f, 10f), actor.Position);
        Assert.Equal("h_near", actor.CollisionPartName);
        Assert.Equal(755920000u, actor.EntityID);
        Assert.Equal(SpeedFogIds.HalloweenJumpscareNpcRow, actor.NPCParamID);
        Assert.Equal(HalloweenJumpscareInjector.ACTOR_THINK_PARAM, actor.ThinkParamID);
        Assert.All(actor.EntityGroupIDs, g => Assert.Equal(0u, g));
        Assert.Contains(msb.Models.Enemies, m => m.Name == "c5280");
    }

    [Fact]
    public void ApplyToMsb_WithoutAnAnchorUsesTheFirstCloneSource()
    {
        var msb = MakeMsb();

        HalloweenJumpscareInjector.ApplyToMsb(msb, new() { Actor(755920000, anchor: null) }, _ => { });

        var actor = msb.Parts.Enemies.Single(e => e.ModelName == "c5280");
        Assert.Equal(new Vector3(100f, 5f, 100f), actor.Position);
        // The first source patrols; the actor must not inherit the route.
        Assert.Null(actor.WalkRouteName);
    }

    [Fact]
    public void ApplyToMsb_PlacesEveryActorOfTheMapWithItsOwnId()
    {
        var msb = MakeMsb();

        var placed = HalloweenJumpscareInjector.ApplyToMsb(msb,
            new() { Actor(755920000), Actor(755920001, eventId: 755865601) }, _ => { });

        Assert.Equal(2, placed.Count);
        var actors = msb.Parts.Enemies.Where(e => e.ModelName == "c5280").ToList();
        Assert.Equal(new[] { 755920000u, 755920001u }, actors.Select(a => a.EntityID));
        Assert.Equal(2, actors.Select(a => a.Name).Distinct().Count());
    }

    [Fact]
    public void ApplyToMsb_MapWithoutVanillaEnemyPlacesNothing()
    {
        var msb = MakeMsb();
        msb.Parts.Enemies.Clear();
        var logs = new List<string>();

        var placed = HalloweenJumpscareInjector.ApplyToMsb(msb, new() { Actor(755920000) }, logs.Add);

        // No part means no event either (the driver writes events for the
        // returned actors only).
        Assert.Empty(placed);
        Assert.Empty(msb.Parts.Enemies);
        Assert.Contains(logs, l => l.Contains("No vanilla enemy"));
    }
}
