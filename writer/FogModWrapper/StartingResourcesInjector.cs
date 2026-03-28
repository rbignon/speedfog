using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects starting resources (runes, golden seeds, sacred tears) into the game via EMEVD.
/// Uses DirectlyGivePlayerItem with a flag to prevent re-giving on reload.
/// </summary>
public static class StartingResourcesInjector
{
    // Good IDs for consumables (from practice tool / game data)
    private const int GOLDEN_SEED_GOOD_ID = 10010;
    private const int SACRED_TEAR_GOOD_ID = 10020;
    private const int LARVAL_TEAR_GOOD_ID = 8185;  // Larval Tear - used for rebirth
    private const int STONESWORD_KEY_GOOD_ID = 8000;  // Stonesword Key - unlocks imp seals

    // Base event ID for resource events
    private const int BASE_EVENT_ID = 755861000;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    // Flag to track if we already gave the starting resources (prevents re-giving on reload)
    // Using a flag in the 10402XXXXX range (same as FINGER_PICKUP_FLAG and FogRando's custom flags)
    // FogRando uses offsets up to +5200, so we use +9000 to avoid conflicts
    private const int RESOURCES_GIVEN_FLAG = 1040299000;

    /// <summary>
    /// Inject starting resources into the provided common EMEVD.
    /// Uses a flag to track if resources were already given (for stackable items).
    /// Starting runes are handled separately by StartingRuneInjector (CharaInitParam).
    /// </summary>
    public static void Inject(EMEVD commonEmevd, Events events, int goldenSeeds, int sacredTears, int larvalTears = 0, int stoneswordKeys = 0)
    {
        if (goldenSeeds <= 0 && sacredTears <= 0 && larvalTears <= 0 && stoneswordKeys <= 0)
        {
            return;
        }

        // Clamp values to safe ranges using ResourceCalculations
        goldenSeeds = ResourceCalculations.ClampGoldenSeeds(goldenSeeds, out var seedsClamped);
        if (seedsClamped)
        {
            Console.WriteLine($"Warning: golden_seeds capped at {ResourceCalculations.MaxGoldenSeeds}");
        }

        sacredTears = ResourceCalculations.ClampSacredTears(sacredTears, out var tearsClamped);
        if (tearsClamped)
        {
            Console.WriteLine($"Warning: sacred_tears capped at {ResourceCalculations.MaxSacredTears}");
        }

        Console.WriteLine("Injecting starting resources via EMEVD...");

        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd");
            return;
        }

        // Create the event manually (same pattern as StartingItemInjector)
        var evt = new EMEVD.Event(BASE_EVENT_ID);

        // Wait for finger pickup (same pattern as StartingItemInjector)
        evt.Instructions.Add(events.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {FINGER_PICKUP_FLAG})"));

        // Exit if we already gave the resources (flag-based check for stackable items)
        evt.Instructions.Add(events.ParseAdd($"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {RESOURCES_GIVEN_FLAG})"));

        // Give all items (only reached if flag was OFF, meaning first time)
        for (int i = 0; i < goldenSeeds; i++)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {GOLDEN_SEED_GOOD_ID}, 6001, 1)"));
        }
        if (goldenSeeds > 0)
            Console.WriteLine($"  Added {goldenSeeds} Golden Seeds");

        for (int i = 0; i < sacredTears; i++)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {SACRED_TEAR_GOOD_ID}, 6001, 1)"));
        }
        if (sacredTears > 0)
            Console.WriteLine($"  Added {sacredTears} Sacred Tears");

        for (int i = 0; i < larvalTears; i++)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {LARVAL_TEAR_GOOD_ID}, 6001, 1)"));
        }
        if (larvalTears > 0)
            Console.WriteLine($"  Added {larvalTears} Larval Tears");

        for (int i = 0; i < stoneswordKeys; i++)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {STONESWORD_KEY_GOOD_ID}, 6001, 1)"));
        }
        if (stoneswordKeys > 0)
            Console.WriteLine($"  Added {stoneswordKeys} Stonesword Keys");

        // Set flag so we don't give items again on reload
        evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {RESOURCES_GIVEN_FLAG}, ON)"));

        // Add event to EMEVD
        commonEmevd.Events.Add(evt);

        // Add initialization call to event 0 (same pattern as StartingItemInjector)
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);              // slot = 0
        BitConverter.GetBytes(BASE_EVENT_ID).CopyTo(initArgs, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);              // arg0 = 0 (unused)
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        Console.WriteLine("Starting resources injected successfully");
    }
}
