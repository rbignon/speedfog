using System.Globalization;
using System.Numerics;
using SoulsFormats;
using SoulsIds;
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
        => new("mini_a", 1, "m31_02_00_00", 1050290010, 1050290030, 1050290020,
            anchor, entityId, eventId);

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
        // The map is named, so the skip maps back to the planner's per-cluster line.
        Assert.Contains(logs, l => l.Contains("No vanilla enemy") && l.Contains("m31_02_00_00"));
    }

    // BuildEvent goes through FogMod's Events.ParseAdd, backed by the
    // gitignored data/er-common.emedf.json; tests return early without it,
    // like HalloweenDarknessInjectorTests.
    private static Events? BuildEvents()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        string? dataDir = null;
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "er-common.emedf.json")))
            dataDir = envDir;
        else
        {
            var candidate = Path.GetFullPath(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../..", "data"));
            if (File.Exists(Path.Combine(candidate, "er-common.emedf.json")))
                dataDir = candidate;
        }
        return dataDir == null
            ? null
            : new Events(Path.Combine(dataDir, "er-common.emedf.json"), darkScriptMode: true, paramAwareMode: true);
    }

    private static readonly HalloweenPluginSettings.Settings Scare =
        new(Ambushes: false, Greeters: true, JumpscareMode: HalloweenPluginSettings.JumpscareMode.Scare);
    private static readonly HalloweenPluginSettings.Settings Strike =
        new(Ambushes: false, Greeters: true, JumpscareMode: HalloweenPluginSettings.JumpscareMode.Strike);

    private static int IndexOf(List<EMEVD.Instruction> list, int bank, int id, Func<EMEVD.Instruction, bool>? match = null)
        => list.FindIndex(i => i.Bank == bank && i.ID == id && (match == null || match(i)));

    private static int U32(EMEVD.Instruction i, int offset) => (int)BitConverter.ToUInt32(i.ArgData, offset);

    [Fact]
    public void BuildEvent_DisablesTheActorBeforeAnyEarlyExit()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Scare).Instructions;

        // MSB parts load enabled: an event that ended before disabling would
        // leave the actor standing at its spot after every reload.
        Assert.Equal((2004, 5), (list[0].Bank, list[0].ID));
        Assert.Equal(755920000, U32(list[0], 0));
        Assert.Equal(0, list[0].ArgData[4]);  // Disabled
        Assert.All(list.Where(i => i.Bank is 1000 or 1003), end => Assert.True(list.IndexOf(end) > 0));
    }

    [Fact]
    public void BuildEvent_GuardsTheExitBeforeAndAfterTheWaitThenSetsTheLayerFlagBeforeTheWarp()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Scare).Instructions;

        // Mirror flags, not the zone-tracking flags the racing mod clears.
        int entry = IndexOf(list, 3, 0, i => U32(i, 4) == 1050290030);
        int wait = IndexOf(list, 1001, 2);
        int setFlag = IndexOf(list, 2003, 66, i => U32(i, 4) == 1050290010);
        int warp = IndexOf(list, 2004, 42);
        var exitGuards = list.Select((ins, idx) => (ins, idx))
            .Where(t => t.ins.Bank == 1003 && t.ins.ID == 2 && U32(t.ins, 4) == 1050290020)
            .Select(t => t.idx).ToList();

        Assert.True(entry >= 0 && wait > entry);
        Assert.Contains(exitGuards, i => i < entry);  // already left: never arm
        Assert.Contains(exitGuards, i => i > wait);   // left during the delay: lost
        Assert.True(setFlag > wait && setFlag < warp, "the layer flag must be set before the warp");
        // Warp onto the player (Character 10000, dummy 12, floor from 10000),
        // as vanilla event 2049422200 does with its helper.
        Assert.Equal(2, list[warp].ArgData[4]);
        Assert.Equal(10000, U32(list[warp], 8));
        Assert.Equal(12, BitConverter.ToInt32(list[warp].ArgData, 12));
        Assert.Equal(10000, U32(list[warp], 16));
    }

    [Fact]
    public void BuildEvent_RechecksTheLayerFlagAfterTheWait()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Scare).Instructions;

        // A cluster spanning two maps has one actor per map, both waiting at
        // once: the one whose delay expires second must see that the layer
        // already played in the other map.
        int wait = IndexOf(list, 1001, 2);
        int layerGuard = list.FindIndex(wait, i => i.Bank == 1003 && i.ID == 2 && U32(i, 4) == 1050290010);
        Assert.True(layerGuard > wait, "no layer-flag guard after the wait");
        Assert.True(layerGuard < IndexOf(list, 2003, 66));
    }

    [Fact]
    public void BuildEvent_RestartsWhileThePlayerIsOutsideTheActorsMap()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Scare).Instructions;

        var mapGuard = list[IndexOf(list, 1003, 8)];
        // Restart, player outside, m31_02_00_00.
        Assert.Equal(new byte[] { 1, 0, 31, 2, 0, 0 }, mapGuard.ArgData.Take(6).ToArray());
        Assert.True(IndexOf(list, 1003, 8) < IndexOf(list, 2003, 66));
    }

    // The animation forced by the instruction right after the warp: in game,
    // an arrival animation (5010) there showed the actor dropping in, left
    // behind by the running player.
    private static int AnimationRightAfterWarp(List<EMEVD.Instruction> list)
    {
        var next = list[IndexOf(list, 2004, 42) + 1];
        Assert.Equal((2003, 18), (next.Bank, next.ID));
        return BitConverter.ToInt32(next.ArgData, 4);
    }

    // The actor's AI switched on (2004:1, state byte 1); the event's first
    // lines switch it off.
    private static int AIEnableIndex(List<EMEVD.Instruction> list)
        => IndexOf(list, 2004, 1, i => i.ArgData[4] == 1);

    [Fact]
    public void BuildEvent_ScareBurstsRightAfterTheWarpAsAFoeWithoutItsAI()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Scare).Instructions;

        // Hostile, so the burst lands on the player, but the actor never
        // fights: no team change, no AI.
        Assert.Equal(-1, IndexOf(list, 2004, 2));
        Assert.Equal(HalloweenJumpscareInjector.BURST_ANIM, AnimationRightAfterWarp(list));
        Assert.Equal(-1, AIEnableIndex(list));
    }

    [Fact]
    public void BuildEvent_StrikeFightsWithItsAIBetweenTheBurstAndThePuff()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), Strike).Instructions;

        Assert.Equal(-1, IndexOf(list, 2004, 2));
        Assert.Equal(HalloweenJumpscareInjector.BURST_ANIM, AnimationRightAfterWarp(list));
        // The burst plays out (its wait) before the AI takes over, and the
        // AI fights for FIGHT_SECONDS before the puff.
        int burst = IndexOf(list, 2003, 18);
        int ai = AIEnableIndex(list);
        Assert.Equal(burst + 2, ai);  // right after the burst's wait
        Assert.Equal(755920000, U32(list[ai], 0));
        Assert.Equal((1001, 0), (list[ai + 1].Bank, list[ai + 1].ID));
        Assert.Equal(HalloweenJumpscareInjector.FIGHT_SECONDS, BitConverter.ToSingle(list[ai + 1].ArgData, 0));
        Assert.Equal(ai + 2, IndexOf(list, 2006, 3));
    }

    [Fact]
    public void BuildEvent_VanishesInAPuffRightBeforeTheDisableInEveryModeAndLayer()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // The exit depends neither on the mode nor on the drawn layer.
        foreach (var settings in new[] { Scare, Strike })
        foreach (var (layer, layerFlag) in new[] { (3, 1050290010), (8, 1050290011), (15, 1050290012) })
        {
            var actor = new JumpscareActor("mini_a", layer, "m31_02_00_00", layerFlag, 1050290030, 1050290020,
                "AEG099_001_9000", 755920000, 755865600);
            var list = HalloweenJumpscareInjector.BuildEvent(events, actor, settings).Instructions;

            // The event ends on the puff, a short wait and the disable; only
            // waits and the strike AI come between the burst and the puff
            // (no death, no dissolve).
            var end = list.TakeLast(4).ToList();
            Assert.Equal(
                new[] { (2006, 3), (2006, 3), (1001, 0), (2004, 5) },
                end.Select(i => (i.Bank, i.ID)).ToArray());
            int burst = IndexOf(list, 2003, 18);
            // The burst plays out before anything follows it.
            Assert.Equal((1001, 0), (list[burst + 1].Bank, list[burst + 1].ID));
            Assert.Equal(HalloweenJumpscareInjector.BURST_SECONDS, BitConverter.ToSingle(list[burst + 1].ArgData, 0));
            Assert.All(list.Skip(burst + 1).SkipLast(4),
                i => Assert.Contains((i.Bank, i.ID), new[] { (1001, 0), (2004, 1) }));
            var puffs = end.Where(i => i.Bank == 2006).ToList();
            // Spawned on the actor itself (Target Entity Type 2: Character).
            Assert.All(puffs, p => Assert.Equal((2, 755920000), (U32(p, 0), U32(p, 4))));
            Assert.Equal(
                new[]
                {
                    (HalloweenJumpscareInjector.PUFF_HEAD_DUMMY, HalloweenJumpscareInjector.PUFF_HEAD_SFX),
                    (HalloweenJumpscareInjector.PUFF_BODY_DUMMY, HalloweenJumpscareInjector.PUFF_BODY_SFX),
                },
                puffs.Select(p => (BitConverter.ToInt32(p.ArgData, 8), BitConverter.ToInt32(p.ArgData, 12))).ToArray());
            Assert.Equal(755920000, U32(end[^1], 0));
            Assert.Equal(0, end[^1].ArgData[4]);  // Disabled
        }
    }

    [Fact]
    public void BuildEvent_FormatsFractionalDelaysInAnyCulture()
    {
        var events = BuildEvents();
        if (events == null)
            return;
        var previous = CultureInfo.CurrentCulture;
        try
        {
            // fr-FR writes 2,5: a culture-sensitive format would break the parse.
            CultureInfo.CurrentCulture = new CultureInfo("fr-FR");
            var settings = Scare with { JumpscareDelayMin = 2.5f, JumpscareDelayMax = 10f };

            var list = HalloweenJumpscareInjector.BuildEvent(events, Actor(755920000), settings).Instructions;

            var wait = list[IndexOf(list, 1001, 2)];
            Assert.Equal(2.5f, BitConverter.ToSingle(wait.ArgData, 0));
            Assert.Equal(10f, BitConverter.ToSingle(wait.ArgData, 4));
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }
}
