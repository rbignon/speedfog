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
    /// Inject zone tracking events: fog gate flags (Part A) and boss death monitor (Part B).
    /// </summary>
    /// <param name="modDir">Mod output directory containing event/ subfolder</param>
    /// <param name="events">SoulsIds Events for parsing EMEVD instructions</param>
    /// <param name="regionToFlags">Region entity → flag IDs mapping from ConnectionInjector</param>
    /// <param name="expectedFlags">Set of all flag IDs that must be injected (from connections)</param>
    /// <param name="finishEvent">Flag ID to set on final boss death</param>
    /// <param name="bossDefeatFlag">Vanilla flag for final boss defeat</param>
    public static void Inject(
        string modDir,
        Events events,
        Dictionary<int, List<int>> regionToFlags,
        HashSet<int> expectedFlags,
        int finishEvent,
        int bossDefeatFlag)
    {
        // Part A: Inject SetEventFlag before warp instructions using region lookup
        InjectFogGateFlags(modDir, events, regionToFlags, expectedFlags);

        // Part B: Create boss death monitor in common.emevd
        if (finishEvent > 0 && bossDefeatFlag > 0)
        {
            InjectBossDeathEvent(modDir, events, finishEvent, bossDefeatFlag);
        }
        else
        {
            Console.WriteLine("Warning: Skipping boss death event (finishEvent={0}, bossDefeatFlag={1})",
                finishEvent, bossDefeatFlag);
        }
    }

    /// <summary>
    /// Scan all EMEVD files for warp instructions. For each WarpPlayer or
    /// PlayCutsceneToPlayerAndWarp, extract the region parameter and look it up
    /// in the regionToFlags dictionary. If found, inject SetEventFlag for each
    /// associated flag_id before the warp instruction.
    ///
    /// This replaces the previous 5-strategy heuristic matching with a single
    /// dictionary lookup. The mapping is built by ConnectionInjector from
    /// entranceEdge.Side.Warp.Region — the same value FogMod bakes into
    /// compiled warp instructions.
    /// </summary>
    private static void InjectFogGateFlags(
        string modDir, Events events,
        Dictionary<int, List<int>> regionToFlags,
        HashSet<int> expectedFlags)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping fog gate flag injection");
            return;
        }

        Console.WriteLine($"Zone tracking: region lookup with {regionToFlags.Count} regions, " +
            $"{regionToFlags.Values.Sum(f => f.Count)} flag entries");

        int totalInjected = 0;
        var injectedFlags = new HashSet<int>();

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(emevdPath);
            bool fileModified = false;

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
                // After N insertions, each Parameter is shifted N times — correct because
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
                fileModified = true;
            }

            if (fileModified)
            {
                emevd.Write(emevdPath);
            }
        }

        Console.WriteLine($"Zone tracking: injected {totalInjected} SetEventFlag instructions " +
            $"({injectedFlags.Count} unique flags)");

        // Phase 3 validation: every expected flag must have been injected.
        var missingFlags = expectedFlags.Except(injectedFlags).OrderBy(f => f).ToList();
        if (missingFlags.Count > 0)
        {
            throw new Exception(
                $"Zone tracking: {missingFlags.Count} flags NOT injected: " +
                string.Join(", ", missingFlags) +
                ". Cannot produce a valid mod output.");
        }
    }

    private static string FormatMap((byte, byte, byte, byte) map)
    {
        return $"m{map.Item1}_{map.Item2:D2}_{map.Item3:D2}_{map.Item4:D2}";
    }

    /// <summary>
    /// Create a boss death monitor event in common.emevd that sets finish_event
    /// when the final boss defeat flag is triggered.
    /// </summary>
    private static void InjectBossDeathEvent(
        string modDir, Events events, int finishEvent, int bossDefeatFlag)
    {
        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine("Warning: common.emevd.dcx not found, skipping boss death event");
            return;
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
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

        emevd.Events.Add(evt);

        // Register in event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);                     // slot = 0
        BitConverter.GetBytes(BOSS_DEATH_EVENT_ID).CopyTo(initArgs, 4);   // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);

        Console.WriteLine($"Zone tracking: boss death monitor event {BOSS_DEATH_EVENT_ID} " +
                          $"(defeat flag {bossDefeatFlag} -> finish event {finishEvent})");
    }
}
