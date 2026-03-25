using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) Inject SetEventFlag before warp instructions using region-based lookup
/// B) Boss death monitor event that sets finish_event on final boss defeat
///
/// The region-to-flags mapping is built by ConnectionInjector from
/// entranceEdge.Side.Warp.Region after Graph.Connect(), before compilation.
/// This avoids reverse-engineering compiled events and eliminates all
/// heuristic matching strategies. See docs/specs/2026-03-12-region-based-zone-tracking.md.
/// </summary>
public static class ZoneTrackingInjector
{
    private const int BOSS_DEATH_EVENT_ID = 755862000;

    /// <summary>
    /// Destination map and region extracted from a warp instruction.
    /// </summary>
    internal readonly struct WarpInfo
    {
        public readonly (byte, byte, byte, byte) DestMap;
        public readonly int Region;

        public WarpInfo((byte, byte, byte, byte) destMap, int region)
        {
            DestMap = destMap;
            Region = region;
        }
    }

    /// <summary>
    /// Try to extract warp destination info from an EMEVD instruction.
    /// Handles two instruction families that FogMod uses for zone transitions:
    ///
    /// 1. WarpPlayer (bank 2003, id 14):
    ///    ArgData layout: [area(1), block(1), sub(1), sub2(1), region(4), unk(4)]
    ///    Used by fogwarp template events and WarpBonfire portal events.
    ///
    /// 2. PlayCutsceneToPlayerAndWarp (bank 2002, id 11/12):
    ///    ArgData layout: [cutsceneId(4), playback(4), region(4), mapId(4), ...]
    ///    mapId is packed decimal: area*1000000 + block*10000 + sub*100 + sub2
    ///    Used by cutscene-based transitions (e.g., Erdtree burning at Forge of the Giants).
    ///    FogMod's EventEditor replaces the region and map in these instructions.
    /// </summary>
    internal static WarpInfo? TryExtractWarpInfo(EMEVD.Instruction instr)
    {
        var a = instr.ArgData;

        // WarpPlayer (bank 2003, id 14)
        if (instr.Bank == 2003 && instr.ID == 14)
        {
            if (a.Length < 8)
                return null;
            var destMap = (a[0], a[1], a[2], a[3]);
            if (destMap == (0, 0, 0, 0))
                return null; // parameterized template
            int region = BitConverter.ToInt32(a, 4);
            return new WarpInfo(destMap, region);
        }

        // PlayCutsceneToPlayerAndWarp (bank 2002, id 11 or 12)
        if (instr.Bank == 2002 && (instr.ID == 11 || instr.ID == 12))
        {
            if (a.Length < 16)
                return null;
            int region = BitConverter.ToInt32(a, 8);
            int mapInt = BitConverter.ToInt32(a, 12);
            if (mapInt == 0)
                return null; // parameterized template
            var destMap = UnpackMapId(mapInt);
            return new WarpInfo(destMap, region);
        }

        return null;
    }

    /// <summary>
    /// Unpack a packed map ID integer to (area, block, sub, sub2) bytes.
    /// Packed format: area*1000000 + block*10000 + sub*100 + sub2.
    /// </summary>
    private static (byte, byte, byte, byte) UnpackMapId(int mapInt)
    {
        int area = mapInt / 1000000;
        int block = (mapInt % 1000000) / 10000;
        int sub = (mapInt % 10000) / 100;
        int sub2 = mapInt % 100;
        return ((byte)area, (byte)block, (byte)sub, (byte)sub2);
    }

    /// <summary>
    /// Patch a single EMEVD: inject SetEventFlag before warp instructions whose
    /// region matches an entry in regionToFlags. Returns the number of SetEventFlag
    /// instructions inserted.
    /// </summary>
    /// <param name="emevd">In-memory EMEVD to patch</param>
    /// <param name="events">Events parser for instruction generation</param>
    /// <param name="regionToFlags">Region entity -> flag IDs mapping from ConnectionInjector</param>
    /// <param name="injectedFlags">Accumulator: flag IDs that were injected (for validation)</param>
    public static int PatchEmevdFile(
        EMEVD emevd, Events events,
        Dictionary<int, List<int>> regionToFlags,
        HashSet<int> injectedFlags)
    {
        int totalInjected = 0;

        foreach (var evt in emevd.Events)
        {
            // First pass: find all warp positions with matching regions.
            var warpPositions = new List<(int index, List<int> flagIds)>();

            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var warpInfo = TryExtractWarpInfo(evt.Instructions[i]);
                if (warpInfo == null)
                    continue;

                int region = warpInfo.Value.Region;

                // Region 0 with a valid dest map suggests an unresolved parameterized
                // region. TryExtractWarpInfo already filters zero dest maps (template
                // placeholders), but a zero region with non-zero map is unexpected.
                if (region == 0)
                    continue;

                if (regionToFlags.TryGetValue(region, out var flagIds))
                {
                    warpPositions.Add((i, flagIds));
                }
            }

            if (warpPositions.Count == 0)
                continue;

            // Second pass: insert SetEventFlag before each warp, from last to first
            // to avoid index shifting affecting earlier positions.
            // For shared entrances, inject ALL flag_ids (all map to the same cluster).
            //
            // Parameter shifting correctness: the inner loop inserts N flags at
            // the same warpIdx, each time incrementing Parameter indices >= warpIdx.
            // After N insertions, each Parameter is shifted N times, correct because
            // N instructions were inserted before it. The outer reverse loop ensures
            // earlier warp positions are unaffected by later insertions.
            int insertCount = 0;
            for (int j = warpPositions.Count - 1; j >= 0; j--)
            {
                var (warpIdx, flagIds) = warpPositions[j];

                // Insert flags in reverse order so they appear in original order
                // before the warp instruction.
                for (int k = flagIds.Count - 1; k >= 0; k--)
                {
                    var setFlagInstr = events.ParseAdd(
                        $"SetEventFlag(TargetEventFlagType.EventFlag, {flagIds[k]}, ON)");
                    evt.Instructions.Insert(warpIdx, setFlagInstr);
                    insertCount++;

                    // Shift Parameter entries for instructions at or after insertion point
                    foreach (var param in evt.Parameters)
                    {
                        if (param.InstructionIndex >= warpIdx)
                        {
                            param.InstructionIndex++;
                        }
                    }
                }

                foreach (var fid in flagIds)
                    injectedFlags.Add(fid);
            }

            totalInjected += insertCount;
        }

        return totalInjected;
    }

    /// <summary>
    /// Validate that every expected flag was injected during the EMEVD scan.
    /// Throws if any flags are missing.
    /// </summary>
    public static void ValidateInjectedFlags(
        HashSet<int> injectedFlags, HashSet<int> expectedFlags, int totalInjected)
    {
        Console.WriteLine($"Zone tracking: injected {totalInjected} SetEventFlag instructions " +
            $"({injectedFlags.Count} unique flags)");

        var missingFlags = expectedFlags.Except(injectedFlags).OrderBy(f => f).ToList();
        if (missingFlags.Count > 0)
        {
            throw new Exception(
                $"Zone tracking: {missingFlags.Count} flags NOT injected: " +
                string.Join(", ", missingFlags) +
                ". Cannot produce a valid mod output.");
        }
    }

    /// <summary>
    /// Inject boss death monitor event into the provided common EMEVD.
    /// Sets finish_event when the final boss defeat flag is triggered.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    /// <param name="events">Events parser for instruction generation</param>
    /// <param name="finishEvent">Flag ID to set on final boss death</param>
    /// <param name="bossDefeatFlag">Vanilla flag for final boss defeat</param>
    public static void InjectBossDeathEvent(
        EMEVD commonEmevd, Events events, int finishEvent, int bossDefeatFlag)
    {
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping boss death event");
            return;
        }

        // Create boss death monitor event
        var evt = new EMEVD.Event(BOSS_DEATH_EVENT_ID);
        // Wait for boss defeat flag
        evt.Instructions.Add(events.ParseAdd(
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {bossDefeatFlag})"));
        // Set our finish tracking flag
        evt.Instructions.Add(events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {finishEvent}, ON)"));

        commonEmevd.Events.Add(evt);

        // Register in event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);                     // slot = 0
        BitConverter.GetBytes(BOSS_DEATH_EVENT_ID).CopyTo(initArgs, 4);   // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        Console.WriteLine($"Zone tracking: boss death monitor event {BOSS_DEATH_EVENT_ID} " +
                          $"(defeat flag {bossDefeatFlag} -> finish event {finishEvent})");
    }
}
