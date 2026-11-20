using FogModWrapper.Plugins.Relay;
using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests.Plugins.Relay;

/// <summary>
/// RelayInjector builds its instructions through FogMod's Events.ParseAdd,
/// so the tests need a real SoulsIds.Events instance backed by the
/// gitignored data/er-common.emedf.json; they skip (early return) when it is
/// absent, like RaceItemsEmevdInjectorTests.
/// </summary>
public class RelayInjectorTests
{
    private const int Region = 755890042;
    private const int ZoneFlag = 1050294003;
    private static readonly (byte, byte, byte, byte) Map = (31, 5, 0, 0);

    private static Events? BuildEvents()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        var candidate = Path.GetFullPath(
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "../../../../..", "data"));
        foreach (var dir in new[] { envDir, candidate })
        {
            if (!string.IsNullOrEmpty(dir) && File.Exists(Path.Combine(dir, "er-common.emedf.json")))
                return new Events(Path.Combine(dir, "er-common.emedf.json"), darkScriptMode: true, paramAwareMode: true);
        }
        return null;
    }

    private static string Warp(int region) => $"WarpPlayer(31, 5, 0, 0, {region}, 0)";

    private static EMEVD.Event BuildEvent(Events events, params string[] lines)
    {
        var evt = new EMEVD.Event(1040290001);
        foreach (var line in lines)
            evt.Instructions.Add(events.ParseAdd(line));
        return evt;
    }

    private static bool Same(EMEVD.Instruction instr, EMEVD.Instruction expected) =>
        instr.Bank == expected.Bank && instr.ID == expected.ID
            && instr.ArgData.SequenceEqual(expected.ArgData);

    private static bool IsLockCheck(Events events, EMEVD.Instruction instr) => Same(instr, events.ParseAdd(
        $"EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, {SpeedFogIds.RelayLockFlag})"));

    private static bool IsLockWait(Events events, EMEVD.Instruction instr) => Same(instr, events.ParseAdd(
        $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {SpeedFogIds.RelayLockFlag})"));

    private static string SetFlag(int flag, string state = "ON") =>
        $"SetEventFlag(TargetEventFlagType.EventFlag, {flag}, {state})";

    private static (EMEVD.Event Evt, Dictionary<int, RelayInjector.WarpTarget> Targets, int Count) Patch(
        Events events, Dictionary<int, List<int>> regionToFlags, params string[] lines)
    {
        var evt = BuildEvent(events, lines);
        var emevd = new EMEVD();
        emevd.Events.Add(evt);
        var targets = new Dictionary<int, RelayInjector.WarpTarget>();
        int n = RelayInjector.PatchEmevdFile(emevd, events, regionToFlags, targets);
        return (evt, targets, n);
    }

    private const string Prompt = "IfActionButtonInArea(MAIN, 9000, 10011800)";

    [Fact]
    public void PatchEmevdFile_ChecksTheLockRightAfterThePromptBeforeTheGateAnimation()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // A fog gate once the core patches ran: prompt, walk through the fog
        // wall, then the zone flag and the trap flag right before the warp.
        var evt = BuildEvent(events,
            Prompt,
            "RotateCharacter(10000, 755890022, 60060, false)",
            "WaitFixedTimeSeconds(1)",
            SetFlag(ZoneFlag), SetFlag(10000851), Warp(Region));
        evt.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(5, 4, 4, 4));
        var emevd = new EMEVD();
        emevd.Events.Add(evt);
        var targets = new Dictionary<int, RelayInjector.WarpTarget>();

        int n = RelayInjector.PatchEmevdFile(
            emevd, events, new() { [Region] = new() { ZoneFlag } }, targets);

        Assert.Equal(1, n);
        // wait, prompt, check, rotate, wait 1 s, zone flag, trap flag, warp
        Assert.Equal(8, evt.Instructions.Count);
        Assert.True(IsLockWait(events, evt.Instructions[0]));
        Assert.True(Same(evt.Instructions[1], events.ParseAdd(Prompt)));
        Assert.True(IsLockCheck(events, evt.Instructions[2]));
        Assert.Equal(1, evt.Instructions.Count(i => IsLockCheck(events, i)));
        // Parameters follow their instruction.
        Assert.Equal(1, evt.Parameters[0].InstructionIndex);
        Assert.Equal(7, evt.Parameters[1].InstructionIndex);

        // The target carries the map and the flags the gate sets.
        Assert.Equal(Map, targets[Region].Map);
        Assert.Equal(2, targets[Region].Prelude.Count);
        Assert.True(Same(targets[Region].Prelude[0], events.ParseAdd(SetFlag(ZoneFlag))));
        Assert.True(Same(targets[Region].Prelude[1], events.ParseAdd(SetFlag(10000851))));
    }

    [Fact]
    public void PatchEmevdFile_TheGatesFlagsLeaveOutTheEventsOwn()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // Shape of the vanilla cutscene warp events (common.emevd 902): the
        // event sets its own flags right above the zone flag.
        var (_, targets, _) = Patch(events, new() { [Region] = new() { ZoneFlag } },
            SetFlag(780010, "OFF"), SetFlag(780011), SetFlag(ZoneFlag), SetFlag(300), Warp(Region));

        Assert.Equal(2, targets[Region].Prelude.Count);
        Assert.True(Same(targets[Region].Prelude[0], events.ParseAdd(SetFlag(ZoneFlag))));
        Assert.True(Same(targets[Region].Prelude[1], events.ParseAdd(SetFlag(300))));
    }

    [Fact]
    public void PatchEmevdFile_ASkipOverTheCheckStillLandsOnItsTarget()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // A branch skipping over the prompt of the first warp.
        var (evt, _, n) = Patch(events,
            new() { [Region] = new() { ZoneFlag }, [Region + 1] = new() { ZoneFlag } },
            "SkipIfEventFlag(2, ON, TargetEventFlagType.EventFlag, 330)",
            Prompt,
            Warp(Region),
            Warp(Region + 1));

        Assert.Equal(2, n);
        // wait, skip, prompt, check, warp, warp: one check serves both
        // warps, and the skip still lands on the second warp.
        Assert.Equal(
            new[] { false, false, false, true, false, false },
            evt.Instructions.Select(i => IsLockCheck(events, i)));
        Assert.Equal(3, evt.Instructions[1].ArgData[0]);
    }

    [Fact]
    public void PatchEmevdFile_OnlyAWaitOnMainCountsAsTheTrigger()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // The gate template registers a condition in a group after its
        // prompt (the trapped check): it is not a wait.
        var (evt, _, _) = Patch(events, new() { [Region] = new() { ZoneFlag } },
            Prompt,
            "IfCharacterHasSpEffect(AND_06, 10000, 4280, false, ComparisonType.Equal, 1)",
            Warp(Region));

        // wait, prompt, check, condition, warp
        Assert.Equal(
            new[] { false, false, true, false, false },
            evt.Instructions.Select(i => IsLockCheck(events, i)));
    }

    [Fact]
    public void PatchEmevdFile_TwoTriggersInOneEvent_EachGetItsCheckAndSkipsKeepTheirTargets()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var regions = new Dictionary<int, List<int>>
        {
            [Region] = new() { ZoneFlag },
            [Region + 1] = new() { ZoneFlag },
        };
        var evt = BuildEvent(events,
            "SkipIfEventFlag(1, ON, TargetEventFlagType.EventFlag, 100)", // lands on the first warp
            Prompt,
            Warp(Region),
            "SkipIfEventFlag(2, ON, TargetEventFlagType.EventFlag, 101)", // lands past the second warp
            Prompt,
            Warp(Region + 1),
            "WaitFixedTimeFrames(1)");
        evt.Parameters.Add(new EMEVD.Parameter(2, 4, 4, 4));
        evt.Parameters.Add(new EMEVD.Parameter(5, 4, 4, 4));
        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        RelayInjector.PatchEmevdFile(emevd, events, regions, new());

        // wait, skip, prompt, check, warp, skip, prompt, check, warp, wait 1 frame
        Assert.Equal(
            new[] { false, false, false, true, false, false, false, true, false, false },
            evt.Instructions.Select(i => IsLockCheck(events, i)));
        // The first skip landed on the first warp's check position: it now
        // runs the check before the warp, its count unchanged. The second
        // skips its prompt, the check and the warp.
        Assert.Equal(1, evt.Instructions[1].ArgData[0]);
        Assert.Equal(3, evt.Instructions[5].ArgData[0]);
        Assert.Equal(4, evt.Parameters[0].InstructionIndex);
        Assert.Equal(8, evt.Parameters[1].InstructionIndex);
    }

    [Fact]
    public void PatchEmevdFile_AnEventWithoutAWaitAboveItsWarp_OnlyGetsTheTopWait()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var (evt, _, _) = Patch(events, new() { [Region] = new() { ZoneFlag } },
            SetFlag(ZoneFlag), Warp(Region));

        Assert.True(IsLockWait(events, evt.Instructions[0]));
        Assert.Equal(3, evt.Instructions.Count);
    }

    [Fact]
    public void PatchEmevdFile_LeavesEventsWithoutAGraphWarpUntouched()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var (evt, targets, n) = Patch(events, new() { [Region] = new() { ZoneFlag } },
            Prompt, SetFlag(ZoneFlag), Warp(999));

        Assert.Equal(0, n);
        Assert.Equal(3, evt.Instructions.Count);
        Assert.Empty(targets);
    }

    [Fact]
    public void ApplyFastTravelGate_PointsTheChapelMapAtTheFastTravelFlagAndNoOtherMap()
    {
        var param = ParamTestHelper.BuildParamFromDef("MapDefaultInfoParam", RelayInjector.ChapelMapRow);
        ParamTestHelper.AddRowFromTemplate(param, 11000000);

        RelayInjector.ApplyFastTravelGate(param);

        Assert.Equal(
            (uint)SpeedFogIds.RelayFastTravelFlag,
            param[RelayInjector.ChapelMapRow]!["EnableFastTravelEventFlagId"].Value);
        Assert.Equal(0u, param[11000000]!["EnableFastTravelEventFlagId"].Value);
    }

    [Fact]
    public void ApplyFastTravelGate_WithoutTheChapelRow_Throws()
    {
        var param = ParamTestHelper.BuildParamFromDef("MapDefaultInfoParam", 11000000);

        Assert.Throws<InvalidDataException>(() => RelayInjector.ApplyFastTravelGate(param));
    }

    [Fact]
    public void InjectFastTravelEvent_ReadsTheLockOnEachPassAndNeverWaitsOnIt()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var common = new EMEVD();
        common.Events.Add(new EMEVD.Event(0));
        RelayInjector.InjectFastTravelEvent(common, events);

        // Each pass writes the flag the lock's state calls for, then the
        // event restarts after a fixed pause: no wait on the lock, which
        // would miss a lock changed during a loading screen.
        var evt = Assert.Single(common.Events, e => e.ID != 0);
        int lockFlag = SpeedFogIds.RelayLockFlag;
        var expected = new[]
        {
            $"SkipIfEventFlag(1, ON, TargetEventFlagType.EventFlag, {lockFlag})",
            SetFlag(SpeedFogIds.RelayFastTravelFlag),
            $"SkipIfEventFlag(1, OFF, TargetEventFlagType.EventFlag, {lockFlag})",
            SetFlag(SpeedFogIds.RelayFastTravelFlag, "OFF"),
        }.Select(events.ParseAdd).ToList();
        for (int i = 0; i < expected.Count; i++)
            Assert.True(Same(evt.Instructions[i], expected[i]), $"instruction {i}");
        // No condition instruction at all (banks below 1000), so no wait on
        // a flag; a timer wait (bank 1001) paces the loop before the restart.
        Assert.Equal(6, evt.Instructions.Count);
        Assert.DoesNotContain(evt.Instructions, i => i.Bank < 1000);
        Assert.Equal(1001, evt.Instructions[4].Bank);
        Assert.True(Same(evt.Instructions[5], events.ParseAdd("EndUnconditionally(EventEndType.Restart)")));

        var init = Assert.Single(common.Events[0].Instructions);
        Assert.Equal((int)evt.ID, BitConverter.ToInt32(init.ArgData, 4));
    }

    private static readonly RelayInjector.ChapelTarget Chapel = new(10010982, 10012021);
    // The two ids differ, so a swap of the record's fields shows in the event.

    [Fact]
    public void InjectWarpEvents_TheChapelWarpMovesTheRespawnPointBeforeWarping()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var common = RunInjectWarpEvents(events, new(), Array.Empty<int>());

        // A death at the Chapel must respawn there, not at the grace the
        // player used last: the respawn point is set before the warp.
        var evt = Assert.Single(common.Events, e => e.ID == SpeedFogIds.RelayEvents.Base);
        int respawnAt = evt.Instructions.FindIndex(
            i => Same(i, events.ParseAdd("SetPlayerRespawnPoint(10012021)")));
        int warpAt = evt.Instructions.FindIndex(
            i => Same(i, events.ParseAdd("WarpPlayer(10, 1, 0, 0, 10010982, 0)")));
        Assert.True(respawnAt >= 0 && warpAt > respawnAt);
    }

    private static EMEVD RunInjectWarpEvents(
        Events events,
        Dictionary<int, List<int>> regionToFlags,
        IEnumerable<int> warpRegions,
        bool chapel = true,
        params string[] prelude)
    {
        var common = new EMEVD();
        common.Events.Add(new EMEVD.Event(0));
        RelayInjector.InjectWarpEvents(
            common, events, regionToFlags,
            warpRegions.ToDictionary(r => r, _ => new RelayInjector.WarpTarget(
                Map, prelude.Select(events.ParseAdd).ToList())),
            new Dictionary<string, string> { [ZoneFlag.ToString()] = "node_a" },
            chapel ? Chapel : null);
        return common;
    }

    [Fact]
    public void InjectWarpEvents_AConnectionWarpSetsTheGatesFlagsThenWarps()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var common = RunInjectWarpEvents(
            events,
            // 1050290020: a mirror flag a plugin appended, not a connection.
            new() { [Region] = new() { ZoneFlag, 1050290020 } },
            new[] { Region },
            prelude: new[] { SetFlag(ZoneFlag), SetFlag(10000851) });

        int slot = ZoneFlag - SpeedFogIds.ZoneFlagBase;
        int warpFlag = SpeedFogIds.RelayWarpFlagBase + slot;
        var evt = Assert.Single(common.Events, e => e.ID == SpeedFogIds.RelayEvents.Base + 10 + slot);
        var expected = new[]
        {
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {warpFlag})",
            SetFlag(warpFlag, "OFF"),
            SetFlag(ZoneFlag),
            SetFlag(10000851),
            Warp(Region),
        }.Select(events.ParseAdd).ToList();
        Assert.True(evt.Instructions.Count >= expected.Count);
        for (int i = 0; i < expected.Count; i++)
            Assert.True(Same(evt.Instructions[i], expected[i]), $"instruction {i}");

        // Chapel warp + the one connection: the mirror flag got no event.
        Assert.Equal(3, common.Events.Count);
        // Every added event is initialized from Event 0.
        Assert.Equal(2, common.Events[0].Instructions.Count);
    }

    [Fact]
    public void InjectWarpEvents_AConnectionWithTwoRegions_GetsOneEventOnTheRegionThatHasAWarp()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        // Entrance with an alternate side: two regions carry the flag, and
        // the scan only met a warp to the second.
        var common = RunInjectWarpEvents(
            events,
            new() { [Region] = new() { ZoneFlag }, [Region + 1] = new() { ZoneFlag } },
            new[] { Region + 1 },
            chapel: false);

        var evt = Assert.Single(common.Events, e => e.ID != 0);
        Assert.Contains(evt.Instructions, i => Same(i, events.ParseAdd(Warp(Region + 1))));
    }

    [Fact]
    public void InjectWarpEvents_WithoutChapelGrace_LeavesTheChapelWarpOut()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var common = RunInjectWarpEvents(
            events, new() { [Region] = new() { ZoneFlag } }, new[] { Region }, chapel: false);

        Assert.DoesNotContain(common.Events, e => e.ID == SpeedFogIds.RelayEvents.Base);
    }

    [Fact]
    public void InjectWarpEvents_AConnectionBeyondTheWarpFlags_Throws()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        int flag = SpeedFogIds.ZoneFlagBase + SpeedFogIds.RelayWarpFlagCount;
        var common = new EMEVD();
        common.Events.Add(new EMEVD.Event(0));

        Assert.Throws<InvalidDataException>(() => RelayInjector.InjectWarpEvents(
            common, events,
            new() { [Region] = new() { flag } },
            new() { [Region] = new RelayInjector.WarpTarget(Map, new List<EMEVD.Instruction>()) },
            new Dictionary<string, string> { [flag.ToString()] = "node_a" },
            Chapel));
    }
}
