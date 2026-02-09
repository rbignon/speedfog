using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects starting item events into the common.emevd after FogMod writes.
/// Uses DirectlyGivePlayerItem to give items by type + ID, which is not affected
/// by Item Randomizer (unlike AwardItemLot which uses ItemLotParam).
/// </summary>
public static class StartingItemInjector
{
    // Base event ID for our starting item events (using a safe range)
    private const int BASE_EVENT_ID = 755860000;

    // Flag set when player picks up the Tarnished's Wizened Finger
    private const int FINGER_PICKUP_FLAG = 1040292051;

    // Flag to track if we already gave the starting items (prevents re-giving on reload)
    private const int ITEMS_GIVEN_FLAG = 1040299001;

    // ItemType enum names for DirectlyGivePlayerItem instruction
    private static readonly string[] ItemTypeNames = { "ItemType.Weapon", "ItemType.Protector", "ItemType.Accessory", "ItemType.Goods" };

    /// <summary>
    /// Inject starting item events into common.emevd.
    /// Gives Good IDs (key items) and care package items (typed) at game start.
    /// </summary>
    /// <param name="modDir">Directory containing the mod files (with event/common.emevd)</param>
    /// <param name="goodIds">List of Good IDs to award (key items, great runes)</param>
    /// <param name="carePackage">List of typed care package items (weapons, armor, etc.)</param>
    /// <param name="events">Events parser for instruction generation</param>
    public static void Inject(string modDir, List<int> goodIds, List<CarePackageItem> carePackage, Events events)
    {
        var totalItems = goodIds.Count + carePackage.Count;
        if (totalItems == 0)
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

        Console.WriteLine($"Injecting {totalItems} starting items into common.emevd...");

        // Load the EMEVD
        var emevd = EMEVD.Read(emevdPath);

        // Find the common event initialization (event 0) to add our event calls
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 (init) not found in common.emevd, skipping starting item injection");
            return;
        }

        // Create a single event that gives all items (more efficient than one per item)
        var evt = new EMEVD.Event(BASE_EVENT_ID);

        // Wait for finger pickup (same pattern as StartingResourcesInjector)
        evt.Instructions.Add(events.ParseAdd($"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {FINGER_PICKUP_FLAG})"));

        // Exit if we already gave the items (flag-based check for unique items)
        evt.Instructions.Add(events.ParseAdd($"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {ITEMS_GIVEN_FLAG})"));

        // Give Good IDs (key items, great runes) using ItemType.Goods
        foreach (var goodId in goodIds)
        {
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem(ItemType.Goods, {goodId}, 6001, 1)"));
            Console.WriteLine($"  Added Good ID {goodId}");
        }

        // Give care package items with their specific ItemType
        foreach (var item in carePackage)
        {
            var itemType = item.Type >= 0 && item.Type < ItemTypeNames.Length
                ? ItemTypeNames[item.Type]
                : "ItemType.Goods";
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem({itemType}, {item.Id}, 6001, 1)"));
            Console.WriteLine($"  Added {itemType} {item.Name} (id={item.Id})");
        }

        // Set flag so we don't give items again on reload
        evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {ITEMS_GIVEN_FLAG}, ON)"));

        // Add event to EMEVD
        emevd.Events.Add(evt);

        // Add initialization call to event 0 (same pattern as StartingResourcesInjector)
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);              // slot = 0
        BitConverter.GetBytes(BASE_EVENT_ID).CopyTo(initArgs, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);              // arg0 = 0 (unused)
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        // Save the modified EMEVD
        emevd.Write(emevdPath);
        Console.WriteLine("Starting item events injected successfully");
    }
}
