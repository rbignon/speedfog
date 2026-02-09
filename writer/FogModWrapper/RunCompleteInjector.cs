using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects a "RUN COMPLETE" banner that displays after the final boss is defeated.
/// - FMG: Overwrites the "VICTORY" banner text in GR_MenuText (menu_dlc02.msgbnd.dcx)
///        for all game languages
/// - EMEVD: Creates event that waits for finish_event flag, delays 7s, shows banner
///
/// Uses the golden banner style (same as "GREAT ENEMY FELLED", "GOD SLAIN", etc.)
/// by repurposing the "VICTORY" banner type (Colosseum-only, unused in PvE).
/// </summary>
public static class RunCompleteInjector
{
    private const int EVENT_ID = 755863000;
    private const string MESSAGE_TEXT = "RUN COMPLETE";
    private const float DELAY_SECONDS = 7.0f;

    // TextBannerType.Victory (33) - Colosseum-only, safe to repurpose in PvE
    private const byte BANNER_TYPE = 33;
    // GR_MenuText FMG ID for the "VICTORY" banner text
    private const int BANNER_FMG_ID = 331314;

    /// <summary>
    /// Inject the "RUN COMPLETE" message display into both FMG and EMEVD.
    /// </summary>
    public static void Inject(string modDir, string gameDir, Events events, int finishEvent)
    {
        if (finishEvent <= 0)
        {
            Console.WriteLine("Warning: No finish event, skipping run complete message injection");
            return;
        }

        InjectFmgEntries(modDir, gameDir);
        InjectEmevdEvent(modDir, events, finishEvent);
    }

    /// <summary>
    /// Overwrite the "VICTORY" banner text in GR_MenuText FMG with "RUN COMPLETE"
    /// for all game languages.
    /// </summary>
    private static void InjectFmgEntries(string modDir, string gameDir)
    {
        var gameMsgDir = Path.Combine(gameDir, "msg");
        if (!Directory.Exists(gameMsgDir))
        {
            Console.WriteLine("Warning: Game msg directory not found, skipping FMG injection");
            return;
        }

        int count = 0;
        foreach (var langDir in Directory.GetDirectories(gameMsgDir))
        {
            var langName = Path.GetFileName(langDir);
            var vanillaPath = Path.Combine(langDir, "menu_dlc02.msgbnd.dcx");
            if (!File.Exists(vanillaPath))
                continue;

            // For English, FogMod already created the file in modDir - modify that.
            // For other languages, read from vanilla and write a new file to modDir.
            var modMsgPath = Path.Combine(modDir, "msg", langName, "menu_dlc02.msgbnd.dcx");
            var sourcePath = File.Exists(modMsgPath) ? modMsgPath : vanillaPath;

            var bnd = BND4.Read(sourcePath);

            var fmgFile = bnd.Files.Find(f => f.Name.Contains("GR_MenuText"));
            if (fmgFile == null)
                continue;

            var fmg = FMG.Read(fmgFile.Bytes);

            var existing = fmg.Entries.Find(e => e.ID == BANNER_FMG_ID);
            if (existing != null)
            {
                existing.Text = MESSAGE_TEXT;
            }
            else
            {
                fmg.Entries.Add(new FMG.Entry(BANNER_FMG_ID, MESSAGE_TEXT));
            }

            fmgFile.Bytes = fmg.Write();

            Directory.CreateDirectory(Path.GetDirectoryName(modMsgPath)!);
            bnd.Write(modMsgPath);
            count++;
        }

        Console.WriteLine($"Run complete: set GR_MenuText[{BANNER_FMG_ID}] = \"{MESSAGE_TEXT}\" in {count} languages");
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

        // 3. Display banner (bank 2007, index 2, single byte arg = banner type)
        evt.Instructions.Add(new EMEVD.Instruction(2007, 2, new[] { BANNER_TYPE }));

        emevd.Events.Add(evt);

        // Register in Event 0 (InitializeEvent: bank 2000, id 0)
        var initArgs = new byte[8];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);           // slot = 0
        BitConverter.GetBytes(EVENT_ID).CopyTo(initArgs, 4);    // eventId
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);

        Console.WriteLine($"Run complete: event {EVENT_ID} " +
                          $"(finish flag {finishEvent} -> delay {DELAY_SECONDS}s -> banner type {BANNER_TYPE})");
    }
}
