using FogModWrapper.Common;
using FogModWrapper.Racing;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Plugins.Relay;

/// <summary>
/// EMEVD side of [plugin.relay] (docs/plugins/relay.md):
/// A) in every event holding a graph warp, a wait on the lock flag at the
///    top and a restart check after the event's trigger, so a locked player
///    cannot leave through a fog gate;
/// B) one common.emevd event per connection, warping the player to that
///    connection's entrance when its flag is raised, plus one event warping
///    back to the Chapel grace.
///
/// Same warp-patching pattern as ZoneTrackingInjector and
/// BossTriggerInjector: scan the compiled events for warp instructions and
/// match their destination region against RegionToFlags.
/// </summary>
public static class RelayInjector
{
    /// <summary>Slot of the first connection warp event in
    /// <see cref="SpeedFogIds.RelayEvents"/> (slot 0 is the Chapel warp).</summary>
    private const int WarpEventSlotBase = 10;

    /// <summary>What the scan learned about a graph warp region: its map and
    /// the flags the gate sets on the way in (the SetEventFlag instructions
    /// the core patches put before the warp).</summary>
    public sealed record WarpTarget((byte, byte, byte, byte) Map, IReadOnlyList<EMEVD.Instruction> Prelude);

    /// <summary>
    /// Patch a single EMEVD. Each event with a warp instruction whose region
    /// is in <paramref name="regionToFlags"/> gets a wait for the lock flag
    /// to be OFF at the top (a locked event stays dormant, prompt included)
    /// and, right after the last wait that precedes the warp, a check
    /// restarting the event when the lock was raised meanwhile. Records
    /// each matched region in <paramref name="targets"/>. Returns the number
    /// of warps locked.
    /// </summary>
    public static int PatchEmevdFile(
        EMEVD emevd, Events events,
        Dictionary<int, List<int>> regionToFlags,
        Dictionary<int, WarpTarget> targets)
    {
        int totalInjected = 0;

        foreach (var evt in emevd.Events)
        {
            int warps = 0;
            var checkPositions = new SortedSet<int>();

            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
                if (warpInfo == null || warpInfo.Value.Region == 0)
                    continue;
                if (!regionToFlags.TryGetValue(warpInfo.Value.Region, out var zoneFlags))
                    continue;
                warps++;

                // The core patches put their SetEventFlag right before the
                // warp, zone tracking first: from the region's first zone
                // flag down, they are what the gate sets on the way in.
                // SetEventFlag instructions above are the event's own.
                int runStart = i;
                while (runStart > 0 && IsSetEventFlag(evt.Instructions[runStart - 1]))
                    runStart--;
                int preludeStart = i;
                for (int k = runStart; k < i; k++)
                {
                    if (zoneFlags.Contains(SetEventFlagId(evt.Instructions[k])))
                    {
                        preludeStart = k;
                        break;
                    }
                }
                targets.TryAdd(warpInfo.Value.Region, new WarpTarget(
                    warpInfo.Value.DestMap,
                    evt.Instructions.GetRange(preludeStart, i - preludeStart)
                        .Select(x => new EMEVD.Instruction(x.Bank, x.ID, (byte[])x.ArgData.Clone()))
                        .ToList()));

                // The check goes right after the event's trigger (its last
                // wait before the warp), above everything the trigger sets
                // off: a fog gate walks the character through the fog wall
                // before warping, so a later check would leave the player on
                // the far side of the gate. No wait above the warp: the top
                // wait alone covers the event.
                for (int k = i - 1; k >= 0; k--)
                {
                    if (IsMainWait(evt.Instructions[k]))
                    {
                        checkPositions.Add(k + 1);
                        break;
                    }
                }
            }

            if (warps == 0)
                continue;
            totalInjected += warps;

            // From last to first, so an insertion does not shift the
            // positions still to fill.
            foreach (int at in checkPositions.Reverse())
            {
                InsertAt(evt, at, events.ParseAdd(
                    "EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, "
                    + $"{SpeedFogIds.RelayLockFlag})"));
            }
            InsertAt(evt, 0, events.ParseAdd(
                $"IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, {SpeedFogIds.RelayLockFlag})"));
        }

        return totalInjected;
    }

    /// <summary>A condition instruction (banks below 1000, first argument =
    /// result condition group) evaluated on MAIN (group 0): the event waits
    /// there until the condition holds.</summary>
    private static bool IsMainWait(EMEVD.Instruction instr) =>
        instr.Bank < 1000 && instr.ArgData.Length >= 1 && instr.ArgData[0] == 0;

    /// <summary>Insert an instruction, keeping the event's parameters on
    /// their instruction and every skip-by-line-count above it on its
    /// target: a skip whose range holds the insertion point skips one more
    /// line.</summary>
    private static void InsertAt(EMEVD.Event evt, int at, EMEVD.Instruction instr)
    {
        for (int s = 0; s < at; s++)
        {
            var skip = evt.Instructions[s];
            if (IsSkipByLineCount(skip) && s + skip.ArgData[0] >= at)
            {
                if (skip.ArgData[0] == byte.MaxValue)
                    throw new InvalidDataException($"relay: skip count overflow in event {evt.ID}");
                skip.ArgData[0]++;
            }
        }

        evt.Instructions.Insert(at, instr);
        foreach (var param in evt.Parameters)
        {
            if (param.InstructionIndex >= at)
                param.InstructionIndex++;
        }
    }

    /// <summary>SetEventFlag is bank 2003, id 66: [type(4), flag(4), state(1)].</summary>
    private static bool IsSetEventFlag(EMEVD.Instruction instr) =>
        instr.Bank == 2003 && instr.ID == 66 && instr.ArgData.Length >= 8;

    private static int SetEventFlagId(EMEVD.Instruction instr) =>
        BitConverter.ToInt32(instr.ArgData, 4);

    /// <summary>The "SKIP ..." instructions of er-common.emedf.json; their
    /// first argument byte is the number of lines skipped.</summary>
    private static readonly HashSet<(int Bank, int Id)> SkipInstructions = new()
    {
        (1000, 1), (1000, 3), (1000, 5), (1000, 7), (1000, 10), (1000, 109),
        (1003, 1), (1003, 3), (1003, 5), (1003, 7), (1003, 9), (1003, 12),
        (1003, 202), (1003, 203), (1003, 206), (1003, 212),
        (1004, 0), (1004, 3), (1005, 1),
    };

    private static bool IsSkipByLineCount(EMEVD.Instruction instr) =>
        instr.ArgData.Length >= 1 && SkipInstructions.Contains((instr.Bank, instr.ID));

    /// <summary>
    /// Add the warp events to common.emevd. The event of a connection sets
    /// the flags its fog gate sets before warping (the region's recorded
    /// prelude: zone-tracking flags, boss trap flag, map variant flag).
    /// <paramref name="chapelPlayerEntity"/> null leaves the Chapel warp out
    /// (seed without the Chapel grace).
    /// </summary>
    public static void InjectWarpEvents(
        EMEVD commonEmevd, Events events,
        Dictionary<int, List<int>> regionToFlags,
        Dictionary<int, WarpTarget> targets,
        IReadOnlyDictionary<string, string> eventMap,
        uint? chapelPlayerEntity)
    {
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0)
            ?? throw new InvalidOperationException("relay: Event 0 not found in common.emevd");

        if (chapelPlayerEntity != null)
        {
            var evt = NewEvent(commonEmevd, initEvent, events, SpeedFogIds.RelayEvents.Base, SpeedFogIds.RelayChapelWarpFlag);
            evt.Instructions.Add(events.ParseAdd($"WarpPlayer(10, 1, 0, 0, {chapelPlayerEntity}, 0)"));
            EndEvent(evt, events);
            Console.WriteLine($"relay: flag {SpeedFogIds.RelayChapelWarpFlag} -> Chapel grace");
        }
        else
        {
            Console.WriteLine("Warning: relay: no Chapel grace in this seed, Chapel warp event left out");
        }

        // A connection whose entrance has an alternate side (AlternateFlag)
        // maps to two regions: its event takes the first one a warp was
        // found for, in region order.
        var flagToRegions = new SortedDictionary<int, List<int>>();
        foreach (var (region, flagIds) in regionToFlags.OrderBy(kv => kv.Key))
        {
            foreach (var flagId in flagIds)
            {
                // RegionToFlags also carries flags plugins appended (the
                // Halloween mirror flags): only connections get a warp.
                if (!eventMap.ContainsKey(flagId.ToString()))
                    continue;
                if (!flagToRegions.TryGetValue(flagId, out var regions))
                    flagToRegions[flagId] = regions = new List<int>();
                regions.Add(region);
            }
        }

        int count = 0;
        foreach (var (flagId, regions) in flagToRegions)
        {
            int n = flagId - SpeedFogIds.ZoneFlagBase;
            if (n < 0 || n >= SpeedFogIds.RelayWarpFlagCount)
            {
                throw new InvalidDataException(
                    $"relay: connection flag {flagId} is outside the {SpeedFogIds.RelayWarpFlagCount} "
                    + "warp flags the plugin holds");
            }

            int region = regions.FirstOrDefault(targets.ContainsKey);
            if (region == 0)
            {
                Console.WriteLine($"Warning: relay: no warp found for region(s) {string.Join(", ", regions)}, " +
                    $"connection flag {flagId} gets no warp event");
                continue;
            }
            var (map, prelude) = targets[region];

            int warpFlag = SpeedFogIds.RelayWarpFlagBase + n;
            var evt = NewEvent(
                commonEmevd, initEvent, events, SpeedFogIds.RelayEvents.Base + WarpEventSlotBase + n, warpFlag);
            foreach (var instr in prelude)
                evt.Instructions.Add(new EMEVD.Instruction(instr.Bank, instr.ID, (byte[])instr.ArgData.Clone()));
            evt.Instructions.Add(events.ParseAdd(
                $"WarpPlayer({map.Item1}, {map.Item2}, {map.Item3}, {map.Item4}, {region}, 0)"));
            EndEvent(evt, events);

            Console.WriteLine($"relay: flag {warpFlag} -> {eventMap[flagId.ToString()]} " +
                $"(m{map.Item1:D2}_{map.Item2:D2}_{map.Item3:D2}_{map.Item4:D2}, region {region}, " +
                $"sets {string.Join(", ", prelude.Select(SetEventFlagId))})");
            count++;
        }

        Console.WriteLine($"relay: {count} connection warp event(s)");
    }

    /// <summary>Start an event that waits for <paramref name="flag"/> and
    /// clears it, registered in Event 0.</summary>
    private static EMEVD.Event NewEvent(
        EMEVD commonEmevd, EMEVD.Event initEvent, Events events, int id, int flag)
    {
        var evt = new EMEVD.Event(id);
        evt.Instructions.Add(events.ParseAdd(
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {flag})"));
        evt.Instructions.Add(events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {flag}, OFF)"));
        commonEmevd.Events.Add(evt);
        initEvent.Instructions.Add(EmevdHelper.InitializeEvent(id));
        return evt;
    }

    private static void EndEvent(EMEVD.Event evt, Events events)
    {
        evt.Instructions.Add(events.ParseAdd("WaitFixedTimeFrames(1)"));
        evt.Instructions.Add(events.ParseAdd("EndUnconditionally(EventEndType.Restart)"));
    }
}
