using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Injects starting resources (runes, golden seeds, sacred tears) into the game via EMEVD.
/// Uses DirectlyGivePlayerItem following the same pattern as FogRando's cheatkeys.
/// </summary>
public static class StartingResourcesInjector
{
    // Good IDs for consumables (from practice tool / game data)
    private const int GOLDEN_SEED_GOOD_ID = 10010;
    private const int SACRED_TEAR_GOOD_ID = 10020;
    private const int LORDS_RUNE_GOOD_ID = 2919;  // 50,000 runes when used

    // Base event ID for resource events
    private const int BASE_EVENT_ID = 755861000;

    // Flag used by FogRando for DirectlyGivePlayerItem (from cheatkeys)
    private const int ITEM_FLAG_ID = 6001;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    // Flag to track if we already gave the starting resources (prevents re-giving on reload)
    // Must be in 755861XXX range (same as event IDs) to be properly saved
    private const int RESOURCES_GIVEN_FLAG = 755861999;

    /// <summary>
    /// Inject starting resources into the mod files via EMEVD.
    /// </summary>
    public static void Inject(string modDir, int runes, int goldenSeeds, int sacredTears)
    {
        if (runes <= 0 && goldenSeeds <= 0 && sacredTears <= 0)
        {
            return;
        }

        // Clamp values to safe ranges
        if (goldenSeeds > 99)
        {
            Console.WriteLine($"Warning: golden_seeds capped at 99 (was {goldenSeeds})");
            goldenSeeds = 99;
        }
        if (sacredTears > 12)
        {
            Console.WriteLine($"Warning: sacred_tears capped at 12 (was {sacredTears})");
            sacredTears = 12;
        }

        // Convert runes to Lord's Rune items (50,000 runes each)
        int lordsRunes = runes > 0 ? (runes + 49999) / 50000 : 0;
        if (lordsRunes > 200)
        {
            Console.WriteLine($"Warning: starting_runes capped at 10,000,000 (200 Lord's Runes)");
            lordsRunes = 200;
        }

        var emevdPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(emevdPath))
        {
            Console.WriteLine($"Warning: common.emevd.dcx not found, skipping resource injection");
            return;
        }

        Console.WriteLine("Injecting starting resources via EMEVD...");

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd");
            return;
        }

        int eventIndex = 0;

        // Create a single event that gives all resources
        var evt = new EMEVD.Event(BASE_EVENT_ID + eventIndex);

        // Wait for finger pickup (same pattern as StartingItemInjector)
        AddIfEventFlag(evt, FINGER_PICKUP_FLAG);

        // TODO: Add flag check to prevent re-giving on reload
        // For now, items will be given every reload (to isolate issues)

        // Add golden seeds
        for (int i = 0; i < goldenSeeds; i++)
        {
            AddDirectlyGiveItem(evt, GOLDEN_SEED_GOOD_ID, i + 1);
        }
        if (goldenSeeds > 0)
            Console.WriteLine($"  Added {goldenSeeds} Golden Seeds");

        // Add sacred tears
        for (int i = 0; i < sacredTears; i++)
        {
            AddDirectlyGiveItem(evt, SACRED_TEAR_GOOD_ID, goldenSeeds + i + 1);
        }
        if (sacredTears > 0)
            Console.WriteLine($"  Added {sacredTears} Sacred Tears");

        // Add Lord's Runes
        for (int i = 0; i < lordsRunes; i++)
        {
            AddDirectlyGiveItem(evt, LORDS_RUNE_GOOD_ID, goldenSeeds + sacredTears + i + 1);
        }
        if (lordsRunes > 0)
        {
            int actualRunes = lordsRunes * 50000;
            Console.WriteLine($"  Added {lordsRunes} Lord's Runes ({actualRunes:N0} runes when used)");
        }

        // TODO: Set flag when we re-enable the flag check
        // AddSetEventFlag(evt, RESOURCES_GIVEN_FLAG, true);

        emevd.Events.Add(evt);

        // Add initialization call to event 0
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);
        BitConverter.GetBytes(BASE_EVENT_ID + eventIndex).CopyTo(initArgs, 4);
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);
        Console.WriteLine("Starting resources injected successfully");
    }

    /// <summary>
    /// Add IfEventFlag instruction to wait for a flag (MAIN condition group).
    /// Instruction 3:3
    /// </summary>
    private static void AddIfEventFlag(EMEVD.Event evt, int flagId)
    {
        var args = new byte[8];
        args[0] = 0;  // MAIN condition group
        args[1] = 1;  // ON state
        args[2] = 0;  // EventFlag type
        args[3] = 0;  // padding
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        evt.Instructions.Add(new EMEVD.Instruction(3, 3, args));
    }

    /// <summary>
    /// Add EndIfEventFlag - ends the event if flag matches state.
    /// Instruction 1003:2
    /// </summary>
    private static void AddEndIfEventFlag(EMEVD.Event evt, int flagId)
    {
        // EndIfEventFlag(ComparisonType, FlagState, EventFlagType, FlagId)
        // Args: comparisonType (1), flagState (1), eventFlagType (1), padding (1), flagId (4)
        var args = new byte[8];
        args[0] = 0;  // ComparisonType.Equal
        args[1] = 1;  // FlagState.ON - end if flag is ON
        args[2] = 0;  // EventFlagType.EventFlag
        args[3] = 0;  // padding
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        evt.Instructions.Add(new EMEVD.Instruction(1003, 2, args));
    }

    /// <summary>
    /// Add SetEventFlag instruction.
    /// Instruction 2003:66
    /// </summary>
    private static void AddSetEventFlag(EMEVD.Event evt, int flagId, bool state)
    {
        var args = new byte[12];
        args[0] = 0;  // EventFlag type
        args[1] = 0;  // padding
        args[2] = 0;  // padding
        args[3] = 0;  // padding
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        args[8] = state ? (byte)1 : (byte)0;  // ON/OFF
        args[9] = 0;  // padding
        args[10] = 0; // padding
        args[11] = 0; // padding
        evt.Instructions.Add(new EMEVD.Instruction(2003, 66, args));
    }

    /// <summary>
    /// Add DirectlyGivePlayerItem instruction.
    /// Following FogRando's pattern: DirectlyGivePlayerItem(ItemType.Goods, itemId, 6001, 1)
    /// Instruction 2003:43
    /// </summary>
    private static void AddDirectlyGiveItem(EMEVD.Event evt, int goodId, int index)
    {
        // DirectlyGivePlayerItem(itemType, itemId, baseFlagId, flagBits)
        // Args: itemType (1), padding (3), itemId (4), baseFlagId (4), flagBits (4)
        var args = new byte[16];
        args[0] = 3;  // ItemType.Goods
        args[1] = 0;  // padding
        args[2] = 0;  // padding
        args[3] = 0;  // padding
        BitConverter.GetBytes(goodId).CopyTo(args, 4);       // Item ID
        BitConverter.GetBytes(ITEM_FLAG_ID).CopyTo(args, 8); // Base Event Flag ID (6001)
        BitConverter.GetBytes(1).CopyTo(args, 12);           // Number of Used Flag Bits
        evt.Instructions.Add(new EMEVD.Instruction(2003, 43, args));
    }
}
