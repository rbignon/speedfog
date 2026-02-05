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
    private const int LORDS_RUNE_GOOD_ID = 2919;  // 50,000 runes when used

    // Base event ID for resource events
    private const int BASE_EVENT_ID = 755861000;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    // Flag to track if we already gave the starting resources (prevents re-giving on reload)
    // Using a flag in the 10402XXXXX range (same as FINGER_PICKUP_FLAG and FogRando's custom flags)
    // FogRando uses offsets up to +5200, so we use +9000 to avoid conflicts
    private const int RESOURCES_GIVEN_FLAG = 1040299000;

    /// <summary>
    /// Inject starting resources into the mod files via EMEVD.
    /// Uses a flag to track if resources were already given (for stackable items).
    /// </summary>
    public static void Inject(string modDir, Events events, int runes, int goldenSeeds, int sacredTears)
    {
        if (runes <= 0 && goldenSeeds <= 0 && sacredTears <= 0)
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

        // Convert runes to Lord's Rune items
        int lordsRunes = ResourceCalculations.ConvertRunesToLordsRunes(runes);
        lordsRunes = ResourceCalculations.ClampLordsRunes(lordsRunes, out var runesClamped);
        if (runesClamped)
        {
            int maxRunes = ResourceCalculations.LordsRunesToRunes(ResourceCalculations.MaxLordsRunes);
            Console.WriteLine($"Warning: starting_runes capped at {maxRunes:N0} ({ResourceCalculations.MaxLordsRunes} Lord's Runes)");
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

        for (int i = 0; i < lordsRunes; i++)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {LORDS_RUNE_GOOD_ID}, 6001, 1)"));
        }
        if (lordsRunes > 0)
        {
            int actualRunes = lordsRunes * 50000;
            Console.WriteLine($"  Added {lordsRunes} Lord's Runes ({actualRunes:N0} runes when used)");
        }

        // Set flag so we don't give items again on reload
        evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {RESOURCES_GIVEN_FLAG}, ON)"));

        // Add event to EMEVD
        emevd.Events.Add(evt);

        // Add initialization call to event 0 (same pattern as StartingItemInjector)
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);              // slot = 0
        BitConverter.GetBytes(BASE_EVENT_ID).CopyTo(initArgs, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);              // arg0 = 0 (unused)
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        emevd.Write(emevdPath);
        Console.WriteLine("Starting resources injected successfully");
    }
}
