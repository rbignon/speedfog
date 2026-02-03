using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects starting item events into the common.emevd after FogMod writes.
/// Uses the common_startingitem template pattern from fogevents.txt.
/// </summary>
public static class StartingItemInjector
{
    // Event template ID for common_startingitem (from fogevents.txt)
    // This event waits for flag 1040292051 (finger pickup) then awards an item lot
    private const int TEMPLATE_EVENT_ID = 755856200;

    // Base event ID for our starting item events (using a safe range)
    private const int BASE_EVENT_ID = 755860000;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    /// <summary>
    /// Inject starting item events into common.emevd.
    /// </summary>
    /// <param name="modDir">Directory containing the mod files (with event/common.emevd)</param>
    /// <param name="itemLots">List of ItemLot IDs to award at game start</param>
    public static void Inject(string modDir, List<int> itemLots)
    {
        if (itemLots.Count == 0)
        {
            Console.WriteLine("No starting items to inject");
            return;
        }

        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine($"Warning: common.emevd.dcx not found at {emevdPath}, skipping starting item injection");
            return;
        }

        Console.WriteLine($"Injecting {itemLots.Count} starting item events into common.emevd...");

        // Load the EMEVD
        var emevd = EMEVD.Read(emevdPath);

        // Find the common event initialization (event 0) to add our event calls
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 (init) not found in common.emevd, skipping starting item injection");
            return;
        }

        // Add events for each item lot
        for (int i = 0; i < itemLots.Count; i++)
        {
            var itemLot = itemLots[i];
            var eventId = BASE_EVENT_ID + i;

            // Create the event that awards this item lot
            var newEvent = CreateStartingItemEvent(eventId, itemLot);
            emevd.Events.Add(newEvent);

            // Add initialization call to event 0
            // InitializeEvent(0, eventId, 0) - the 0 at end is slot
            var initInstruction = CreateInitializeEventInstruction(eventId);
            initEvent.Instructions.Add(initInstruction);

            Console.WriteLine($"  Added event {eventId} for ItemLot {itemLot}");
        }

        // Save the modified EMEVD
        emevd.Write(emevdPath);
        Console.WriteLine($"Starting item events injected successfully");
    }

    /// <summary>
    /// Create an event that waits for finger pickup flag then awards an item lot.
    /// Matches the common_startingitem template from fogevents.txt.
    /// </summary>
    private static EMEVD.Event CreateStartingItemEvent(int eventId, int itemLot)
    {
        var evt = new EMEVD.Event(eventId);

        // IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, 1040292051)
        // Bank 3, ID 0: IfConditionGroup (wait for condition MAIN to pass)
        // Bank 3, ID 3: IfEventFlag
        // We need to:
        // 1. IfEventFlag(MAIN, ON, EventFlag, FINGER_PICKUP_FLAG)
        // 2. AwardItemLot(itemLot)

        // Instruction: IfEventFlag(conditionGroup, state, flagType, flagId)
        // Bank 3, ID 3
        // Args: condition (1 byte), state (1 byte), flagType (1 byte), padding (1 byte), flagId (4 bytes)
        var ifFlagArgs = new byte[8];
        ifFlagArgs[0] = 0;  // MAIN condition group
        ifFlagArgs[1] = 1;  // ON state
        ifFlagArgs[2] = 0;  // EventFlag type
        ifFlagArgs[3] = 0;  // padding
        BitConverter.GetBytes(FINGER_PICKUP_FLAG).CopyTo(ifFlagArgs, 4);

        evt.Instructions.Add(new EMEVD.Instruction(3, 3, ifFlagArgs));

        // Instruction: AwardItemLot(itemLotId)
        // Bank 2003, ID 4 - DirectlyGivePlayerItem / AwardItemLot
        // Args: itemLotId (4 bytes)
        var awardArgs = new byte[4];
        BitConverter.GetBytes(itemLot).CopyTo(awardArgs, 0);

        evt.Instructions.Add(new EMEVD.Instruction(2003, 4, awardArgs));

        return evt;
    }

    /// <summary>
    /// Create an InitializeEvent instruction to call from event 0.
    /// </summary>
    private static EMEVD.Instruction CreateInitializeEventInstruction(int eventId)
    {
        // InitializeEvent(slot, eventId, args...)
        // Bank 2000, ID 0
        // Args: slot (4 bytes), eventId (4 bytes), followed by event args
        // For common_startingitem template, the only arg is X0_4 = 0 (unused in our case)
        var args = new byte[12];
        BitConverter.GetBytes(0).CopyTo(args, 0);        // slot = 0
        BitConverter.GetBytes(eventId).CopyTo(args, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(args, 8);        // arg0 = 0 (unused)

        return new EMEVD.Instruction(2000, 0, args);
    }
}
