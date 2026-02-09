using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) Modify fogwarp template to add parameterized SetEventFlag, extend InitializeEvent calls
/// B) Boss death monitor event that sets finish_event on final boss defeat
/// </summary>
public static class ZoneTrackingInjector
{
    private const int BOSS_DEATH_EVENT_ID = 755862000;

    /// <summary>
    /// Inject zone tracking events into EMEVD files.
    /// </summary>
    /// <param name="modDir">Path to mod output directory (contains event/ subdirectory)</param>
    /// <param name="events">Events instance for instruction parsing</param>
    /// <param name="warpMatches">Warp data extracted from FogMod's Graph</param>
    /// <param name="finishEvent">The finish_event flag ID</param>
    /// <param name="bossDefeatFlag">The boss defeat flag from FogMod's Graph</param>
    public static void Inject(
        string modDir,
        Events events,
        List<WarpMatchData> warpMatches,
        int finishEvent,
        int bossDefeatFlag)
    {
        // Part A: Modify fogwarp template + extend InitializeEvent calls
        InjectFogGateFlags(modDir, events, warpMatches);

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

    private const int FOGWARP_EVENT_ID = 9005777;
    private const int BANK_LABEL = 1014;
    private const int LABEL10 = 10;
    private const int BANK_INIT_EVENT = 2000;
    private const int ID_INIT_EVENT = 0;

    /// <summary>
    /// Modify the fogwarp template event (9005777) to include a parameterized SetEventFlag,
    /// then extend all InitializeEvent calls to pass the tracking flag ID.
    /// </summary>
    private static void InjectFogGateFlags(
        string modDir, Events events, List<WarpMatchData> warpMatches)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping fog gate flag injection");
            return;
        }

        // Build lookup: (area, block, sub, sub2, region) → flagId
        var lookup = new Dictionary<(byte, byte, byte, byte, int), int>();
        foreach (var wm in warpMatches)
        {
            if (wm.DestMapBytes.Length < 4)
                continue;
            var key = (wm.DestMapBytes[0], wm.DestMapBytes[1],
                       wm.DestMapBytes[2], wm.DestMapBytes[3], wm.DestRegion);
            lookup.TryAdd(key, wm.FlagId);  // first wins if duplicate
        }

        Console.WriteLine($"Zone tracking: {lookup.Count} warp destinations to match");

        // Step 1: Determine instruction arg byte offsets using sentinel values
        const int SENTINEL = 0x7F7F7F7F;
        var sentinelGoto = events.ParseAdd(
            $"GotoIfComparison(Label.Label20, ComparisonType.Equal, {SENTINEL}, 0)");
        int gotoFlagOffset = FindInt32Offset(sentinelGoto.ArgData, SENTINEL);

        var sentinelSetFlag = events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {SENTINEL}, ON)");
        int setFlagOffset = FindInt32Offset(sentinelSetFlag.ArgData, SENTINEL);

        Console.WriteLine($"Zone tracking: GotoIfComparison flag offset={gotoFlagOffset}, SetEventFlag flag offset={setFlagOffset}");

        // Find fogwarp event 9005777 and modify it; extend all InitializeEvent calls
        bool templateModified = false;
        int totalExtended = 0;

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(emevdPath);
            bool fileModified = false;

            // Look for the fogwarp template event
            if (!templateModified)
            {
                var fogwarpEvent = emevd.Events.Find(e => e.ID == FOGWARP_EVENT_ID);
                if (fogwarpEvent != null)
                {
                    ModifyFogwarpTemplate(fogwarpEvent, events, gotoFlagOffset, setFlagOffset);
                    templateModified = true;
                    fileModified = true;
                    Console.WriteLine($"Zone tracking: modified fogwarp template in {Path.GetFileName(emevdPath)}");
                }
            }

            // Extend InitializeEvent calls targeting fogwarp
            int extended = ExtendInitializeEventCalls(emevd, lookup);
            if (extended > 0)
            {
                fileModified = true;
                totalExtended += extended;
                Console.WriteLine($"Zone tracking: extended {extended} InitializeEvent calls in {Path.GetFileName(emevdPath)}");
            }

            if (fileModified)
            {
                emevd.Write(emevdPath);
            }
        }

        if (!templateModified)
        {
            Console.WriteLine("Warning: fogwarp event 9005777 not found in any EMEVD file");
        }

        Console.WriteLine($"Zone tracking: extended {totalExtended} fogwarp calls total, {lookup.Count} warp destinations available");
    }

    /// <summary>
    /// Modify the fogwarp template event to add SetEventFlag with parameterized X40_4.
    /// Inserts 3 instructions after Label10:
    ///   GotoIfComparison(Label20, Equal, X40_4, 0)  — skip if flag is 0
    ///   SetEventFlag(EventFlag, X40_4, ON)
    ///   Label20()
    /// </summary>
    private static void ModifyFogwarpTemplate(
        EMEVD.Event fogwarpEvent, Events events, int gotoFlagOffset, int setFlagOffset)
    {
        // Find Label10 instruction
        int label10Index = -1;
        for (int i = 0; i < fogwarpEvent.Instructions.Count; i++)
        {
            var instr = fogwarpEvent.Instructions[i];
            if (instr.Bank == BANK_LABEL && instr.ID == LABEL10)
            {
                label10Index = i;
                break;
            }
        }

        if (label10Index < 0)
        {
            Console.WriteLine("Warning: Label10 not found in fogwarp event, skipping template modification");
            return;
        }

        int insertIndex = label10Index + 1;

        // Create the 3 new instructions with placeholder values (will be parameterized)
        var gotoInstr = events.ParseAdd("GotoIfComparison(Label.Label20, ComparisonType.Equal, 0, 0)");
        var setFlagInstr = events.ParseAdd("SetEventFlag(TargetEventFlagType.EventFlag, 0, ON)");
        var label20Instr = new EMEVD.Instruction(BANK_LABEL, 20);

        // Insert in order after Label10
        fogwarpEvent.Instructions.Insert(insertIndex, gotoInstr);
        fogwarpEvent.Instructions.Insert(insertIndex + 1, setFlagInstr);
        fogwarpEvent.Instructions.Insert(insertIndex + 2, label20Instr);

        // Step 4: Shift existing Parameter entries for instructions after the insertion point
        foreach (var param in fogwarpEvent.Parameters)
        {
            if (param.InstructionIndex >= insertIndex)
            {
                param.InstructionIndex += 3;
            }
        }

        // Step 5: Add new Parameter entries for X40_4 (SourceStartByte = 40)
        fogwarpEvent.Parameters.Add(new EMEVD.Parameter
        {
            InstructionIndex = insertIndex,
            TargetStartByte = gotoFlagOffset,
            SourceStartByte = 40,
            ByteCount = 4,
        });
        fogwarpEvent.Parameters.Add(new EMEVD.Parameter
        {
            InstructionIndex = insertIndex + 1,
            TargetStartByte = setFlagOffset,
            SourceStartByte = 40,
            ByteCount = 4,
        });
    }

    /// <summary>
    /// Extend InitializeEvent calls targeting fogwarp (9005777) to include the tracking flag.
    /// Returns the number of calls extended.
    /// </summary>
    private static int ExtendInitializeEventCalls(
        EMEVD emevd, Dictionary<(byte, byte, byte, byte, int), int> lookup)
    {
        int count = 0;

        foreach (var evt in emevd.Events)
        {
            foreach (var instr in evt.Instructions)
            {
                // InitializeEvent: bank 2000, id 0
                if (instr.Bank != BANK_INIT_EVENT || instr.ID != ID_INIT_EVENT)
                    continue;

                var args = instr.ArgData;
                // InitializeEvent args: [0..3] slot, [4..7] eventId, [8..] event params
                if (args.Length < 8)
                    continue;

                int eventId = BitConverter.ToInt32(args, 4);
                if (eventId != FOGWARP_EVENT_ID)
                    continue;

                // fogwarp InitializeEvent layout (event params start at offset 8):
                //   [8..11]  X0_4  fog gate entity
                //   [12..15] X4_4  button param
                //   [16..19] X8_4  warp target region
                //   [20..23] X12   map bytes (4×byte)
                //   [24..27] X16_4 defeat flag
                //   [28..31] X20_4 trap flag
                //   [32..35] X24_4 alt flag
                //   [36..39] X28_4 alt warp target
                //   [40..43] X32   alt map bytes
                //   [44..47] X36_4 rotate target
                // We need at least 24 bytes (up to map bytes) to match
                if (args.Length < 24)
                    continue;

                // Extract match keys: map bytes at [20..23], region at [16..19]
                byte mapA = args[20];
                byte mapB = args[21];
                byte mapC = args[22];
                byte mapD = args[23];
                int region = BitConverter.ToInt32(args, 16);

                var key = (mapA, mapB, mapC, mapD, region);
                int flagId = lookup.GetValueOrDefault(key, 0);

                // Extend args from current size to current + 4, appending flagId
                var newArgs = new byte[args.Length + 4];
                Array.Copy(args, newArgs, args.Length);
                BitConverter.GetBytes(flagId).CopyTo(newArgs, args.Length);
                instr.ArgData = newArgs;

                count++;
            }
        }

        return count;
    }

    /// <summary>
    /// Find the byte offset of a sentinel int32 value in instruction ArgData.
    /// </summary>
    private static int FindInt32Offset(byte[] argData, int sentinel)
    {
        var sentinelBytes = BitConverter.GetBytes(sentinel);
        for (int i = 0; i <= argData.Length - 4; i++)
        {
            if (argData[i] == sentinelBytes[0]
                && argData[i + 1] == sentinelBytes[1]
                && argData[i + 2] == sentinelBytes[2]
                && argData[i + 3] == sentinelBytes[3])
            {
                return i;
            }
        }

        throw new InvalidOperationException(
            $"Sentinel value 0x{sentinel:X8} not found in ArgData ({argData.Length} bytes)");
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
