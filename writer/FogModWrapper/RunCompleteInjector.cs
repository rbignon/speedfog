using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects a "RUN COMPLETE" full screen message that displays after the final boss is defeated.
/// - FMG: Adds EventTextForMap entry to menu_dlc02.msgbnd.dcx
/// - EMEVD: Creates event that waits for finish_event flag, delays 7s, shows message
/// </summary>
public static class RunCompleteInjector
{
    private const int EVENT_ID = 755863000;
    private const int MESSAGE_ID = 755863000;
    private const string MESSAGE_TEXT = "RUN COMPLETE";
    private const float DELAY_SECONDS = 7.0f;

    /// <summary>
    /// Inject the "RUN COMPLETE" message display into both FMG and EMEVD.
    /// </summary>
    public static void Inject(string modDir, Events events, int finishEvent)
    {
        if (finishEvent <= 0)
        {
            Console.WriteLine("Warning: No finish event, skipping run complete message injection");
            return;
        }

        InjectFmgEntry(modDir);
        InjectEmevdEvent(modDir, events, finishEvent);
    }

    /// <summary>
    /// Add "RUN COMPLETE" text entry to EventTextForMap FMG in menu_dlc02.msgbnd.dcx.
    /// </summary>
    private static void InjectFmgEntry(string modDir)
    {
        var msgPath = Path.Combine(modDir, "msg", "engus", "menu_dlc02.msgbnd.dcx");
        if (!File.Exists(msgPath))
        {
            Console.WriteLine("Warning: menu_dlc02.msgbnd.dcx not found, skipping FMG injection");
            return;
        }

        var bnd = BND4.Read(msgPath);

        // Find the EventTextForMap FMG file within the bundle
        var fmgFile = bnd.Files.Find(f => f.Name.Contains("EventTextForMap"));
        if (fmgFile == null)
        {
            Console.WriteLine("Warning: EventTextForMap FMG not found in menu_dlc02.msgbnd.dcx");
            return;
        }

        var fmg = FMG.Read(fmgFile.Bytes);

        // Add our message entry
        fmg.Entries.Add(new FMG.Entry(MESSAGE_ID, MESSAGE_TEXT));

        fmgFile.Bytes = fmg.Write();
        bnd.Write(msgPath);

        Console.WriteLine($"Run complete: added FMG entry {MESSAGE_ID} = \"{MESSAGE_TEXT}\"");
    }

    /// <summary>
    /// Create EMEVD event that waits for finish_event, delays, then displays the message.
    /// </summary>
    private static void InjectEmevdEvent(string modDir, Events events, int finishEvent)
    {
        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine("Warning: common.emevd.dcx not found, skipping run complete event injection");
            return;
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping run complete event");
            return;
        }

        // Create the run complete event
        var evt = new EMEVD.Event(EVENT_ID);

        // 1. Wait for finish_event flag (set by boss death monitor)
        evt.Instructions.Add(events.ParseAdd(
            $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {finishEvent})"));

        // 2. Delay for boss death banner to fade (~7 seconds)
        evt.Instructions.Add(events.ParseAdd(
            $"WaitFixedTimeSeconds({DELAY_SECONDS})"));

        // 3. Display full screen message (bank 2007, index 9, single int32 arg)
        var msgArgs = BitConverter.GetBytes(MESSAGE_ID);
        evt.Instructions.Add(new EMEVD.Instruction(2007, 9, msgArgs));

        emevd.Events.Add(evt);

        // Register in Event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);           // slot = 0
        BitConverter.GetBytes(EVENT_ID).CopyTo(initArgs, 4);    // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);

        Console.WriteLine($"Run complete: event {EVENT_ID} " +
                          $"(finish flag {finishEvent} -> delay {DELAY_SECONDS}s -> message {MESSAGE_ID})");
    }
}
