using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) Inject SetEventFlag before WarpPlayer in FogMod-generated per-instance warp events
/// B) Boss death monitor event that sets finish_event on final boss defeat
/// </summary>
public static class ZoneTrackingInjector
{
    private const int BOSS_DEATH_EVENT_ID = 755862000;

    /// <summary>
    /// FogMod allocates warp target region entity IDs starting from this base.
    /// Vanilla WarpPlayer events use map-specific entity IDs (e.g., 14003900, 16002701)
    /// which are well below this threshold. Filtering on region >= this value ensures
    /// we only modify FogMod-generated warp events, not vanilla ones.
    /// </summary>
    private const int FOGMOD_ENTITY_BASE = 755890000;

    /// <summary>
    /// Inject zone tracking events into EMEVD files.
    /// </summary>
    /// <param name="modDir">Path to mod output directory (contains event/ subdirectory)</param>
    /// <param name="events">Events instance for instruction parsing</param>
    /// <param name="connections">Connections from graph.json with flag_id per connection</param>
    /// <param name="finishEvent">The finish_event flag ID</param>
    /// <param name="bossDefeatFlag">The boss defeat flag from FogMod's Graph</param>
    public static void Inject(
        string modDir,
        Events events,
        List<Connection> connections,
        int finishEvent,
        int bossDefeatFlag)
    {
        // Part A: Inject SetEventFlag before WarpPlayer in per-instance warp events
        InjectFogGateFlags(modDir, events, connections);

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
    /// Scan all EMEVD files for WarpPlayer instructions with literal destination map bytes.
    /// When a destination matches a connection's entrance gate map, inject SetEventFlag before
    /// the WarpPlayer to set the zone tracking flag on traversal.
    ///
    /// FogMod's EventEditor compiles the fogwarp template (9005777) into per-instance events
    /// with unique IDs and literal WarpPlayer values. These events are placed in map EMEVD files
    /// and called via InitializeEvent (2000:0). We post-process them to add zone tracking.
    /// </summary>
    private static void InjectFogGateFlags(
        string modDir, Events events, List<Connection> connections)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping fog gate flag injection");
            return;
        }

        // Build lookup: destination (area, block, sub, sub2) → flagId
        // Parse map bytes from entrance_gate name (e.g., "m31_05_00_00_AEG099_230_9001" → [31,5,0,0])
        // The entrance_gate is in the destination area's map, so its map prefix matches
        // the WarpPlayer destination bytes.
        var lookup = new Dictionary<(byte, byte, byte, byte), int>();
        foreach (var conn in connections)
        {
            if (conn.FlagId <= 0)
                continue;
            var mapBytes = ParseMapBytesFromGateName(conn.EntranceGate);
            if (mapBytes == null)
                continue;
            var key = (mapBytes[0], mapBytes[1], mapBytes[2], mapBytes[3]);
            if (lookup.TryGetValue(key, out int existing) && existing != conn.FlagId)
            {
                Console.WriteLine($"Warning: Zone tracking map collision for {conn.EntranceGate}: " +
                                  $"flag {conn.FlagId} conflicts with existing flag {existing}");
            }
            lookup.TryAdd(key, conn.FlagId);  // first wins if duplicate (diamond merges share flagId)
        }

        Console.WriteLine($"Zone tracking: {lookup.Count} destination maps to match");

        int totalInjected = 0;

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(emevdPath);
            bool fileModified = false;

            foreach (var evt in emevd.Events)
            {
                // Find WarpPlayer instruction (bank 2003, id 14) with literal map bytes
                for (int i = 0; i < evt.Instructions.Count; i++)
                {
                    var instr = evt.Instructions[i];
                    if (instr.Bank != 2003 || instr.ID != 14)
                        continue;

                    var a = instr.ArgData;
                    if (a.Length < 8)
                        continue;

                    // Skip parameterized WarpPlayer (all-zero map = template placeholder)
                    var destMap = (a[0], a[1], a[2], a[3]);
                    if (destMap == (0, 0, 0, 0))
                        continue;

                    // Only match FogMod-generated warp events, not vanilla ones.
                    // FogMod allocates warp target regions from FOGMOD_ENTITY_BASE;
                    // vanilla events use map-specific IDs well below that range.
                    int region = BitConverter.ToInt32(a, 4);
                    if (region < FOGMOD_ENTITY_BASE)
                        continue;

                    // Match against our connection lookup
                    if (!lookup.TryGetValue(destMap, out int flagId))
                        continue;

                    // Insert SetEventFlag(flagId, ON) before WarpPlayer
                    var setFlagInstr = events.ParseAdd(
                        $"SetEventFlag(TargetEventFlagType.EventFlag, {flagId}, ON)");
                    evt.Instructions.Insert(i, setFlagInstr);

                    // Shift Parameter entries for instructions at or after insertion point
                    foreach (var param in evt.Parameters)
                    {
                        if (param.InstructionIndex >= i)
                        {
                            param.InstructionIndex++;
                        }
                    }

                    totalInjected++;
                    fileModified = true;
                    break;  // Alt-warp uses same destination map; indices shifted, so stop
                }
            }

            if (fileModified)
            {
                emevd.Write(emevdPath);
            }
        }

        Console.WriteLine($"Zone tracking: injected {totalInjected} fog gate tracking flags");
    }

    /// <summary>
    /// Parse map bytes from a gate name like "m31_05_00_00_AEG099_230_9001" → [31, 5, 0, 0].
    /// </summary>
    private static byte[]? ParseMapBytesFromGateName(string gateName)
    {
        if (string.IsNullOrEmpty(gateName))
            return null;

        var name = gateName.TrimStart('m');
        var parts = name.Split('_');
        if (parts.Length < 4)
            return null;

        try
        {
            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3]),
            };
        }
        catch (FormatException)
        {
            Console.WriteLine($"Warning: Could not parse map bytes from gate name: {gateName}");
            return null;
        }
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
