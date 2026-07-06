using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects an event to unlock Roundtable Hold immediately after game start.
/// This bypasses the DLC-specific finger pickup detection that can fail.
/// </summary>
public static class RoundtableUnlockInjector
{
    // Flag that common_roundtable waits for before enabling Roundtable
    private const int START_FLAG = SpeedFogIds.FingerPickupFlag;

    // Event ID for our unlock event (using a safe range)
    private static readonly int UNLOCK_EVENT_ID = SpeedFogIds.RoundtableUnlockEvents.Base;

    /// <summary>
    /// Inject the Roundtable unlock event into the provided common EMEVD.
    /// This event activates the START_FLAG immediately, allowing
    /// common_roundtable to enable the Roundtable Hold.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    public static void Inject(EMEVD commonEmevd)
    {
        Console.WriteLine("Injecting Roundtable unlock event into common.emevd...");

        // Find the common event initialization (event 0) to add our event call
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 (init) not found in common.emevd, skipping Roundtable unlock injection");
            return;
        }

        // Create the event that sets the start flag immediately
        var unlockEvent = CreateUnlockEvent(UNLOCK_EVENT_ID);
        commonEmevd.Events.Add(unlockEvent);

        // Add initialization call to event 0
        var initInstruction = EmevdHelper.InitializeEvent(UNLOCK_EVENT_ID);
        initEvent.Instructions.Add(initInstruction);

        Console.WriteLine($"Roundtable unlock event {UNLOCK_EVENT_ID} injected successfully");
    }

    /// <summary>
    /// Create an event that immediately sets the start flag.
    /// This enables common_roundtable to proceed with Roundtable unlock.
    /// </summary>
    private static EMEVD.Event CreateUnlockEvent(int eventId)
    {
        var evt = new EMEVD.Event(eventId);

        // SetEventFlag(TargetEventFlagType.EventFlag, START_FLAG, ON)
        // Bank 2003, ID 66: SetEventFlag
        // Args: flagType (1 byte), padding (3 bytes), flagId (4 bytes), state (1 byte), padding (3 bytes)
        var setFlagArgs = new byte[12];
        setFlagArgs[0] = 0;  // EventFlag type
        setFlagArgs[1] = 0;  // padding
        setFlagArgs[2] = 0;  // padding
        setFlagArgs[3] = 0;  // padding
        BitConverter.GetBytes(START_FLAG).CopyTo(setFlagArgs, 4);  // flagId
        setFlagArgs[8] = 1;  // ON state
        setFlagArgs[9] = 0;  // padding
        setFlagArgs[10] = 0; // padding
        setFlagArgs[11] = 0; // padding

        evt.Instructions.Add(new EMEVD.Instruction(2003, 66, setFlagArgs));

        return evt;
    }
}
