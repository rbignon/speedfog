using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects EMEVD events for racing zone tracking:
/// A) SetEventFlag before each WarpPlayer instruction matching our connections
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
        // Part A: Inject SetEventFlag before WarpPlayer in map EMEVDs
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

    /// <summary>
    /// Scan map EMEVD files for WarpPlayer instructions and inject SetEventFlag before matches.
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

        Console.WriteLine($"Zone tracking: scanning EMEVDs for {lookup.Count} warp destinations...");

        int totalInjected = 0;

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(emevdPath);
            bool modified = false;

            foreach (var evt in emevd.Events)
            {
                for (int i = 0; i < evt.Instructions.Count; i++)
                {
                    var instr = evt.Instructions[i];
                    // WarpPlayer: bank 2003, id 14
                    if (instr.Bank != 2003 || instr.ID != 14)
                        continue;

                    var args = instr.ArgData;
                    if (args.Length < 8)
                        continue;

                    var key = (args[0], args[1], args[2], args[3],
                               BitConverter.ToInt32(args, 4));

                    if (lookup.TryGetValue(key, out int flagId))
                    {
                        var setFlag = events.ParseAdd(
                            $"SetEventFlag(TargetEventFlagType.EventFlag, {flagId}, ON)");
                        evt.Instructions.Insert(i, setFlag);
                        i++;  // skip past insertion
                        modified = true;
                        totalInjected++;
                    }
                }
            }

            if (modified)
            {
                emevd.Write(emevdPath);
            }
        }

        Console.WriteLine($"Zone tracking: injected {totalInjected} SetEventFlag instructions");
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
