using System.Linq;
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
    // Must match EMEDF enum: Weapon=0, Armor=1, Ring=2, Goods=3
    private static readonly string[] ItemTypeNames = { "ItemType.Weapon", "ItemType.Armor", "ItemType.Ring", "ItemType.Goods" };

    // Auxiliary event flags that the game checks to unlock weapon infusion affinities.
    // DirectlyGivePlayerItem puts items in inventory but doesn't set these flags,
    // so the Ashes of War menu won't show the corresponding affinities without them.
    // Source: Item Randomizer itemevents.txt event 1450 / CharacterWriter.cs
    private static readonly Dictionary<int, int> WhetbladeFlags = new()
    {
        { 8970, 65610 },  // Iron Whetblade → Heavy, Keen, Quality
        { 8971, 65640 },  // Red-Hot Whetblade → Fire, Flame Art
        { 8972, 65660 },  // Sanctified Whetblade → Lightning, Sacred
        { 8973, 65680 },  // Glintstone Whetblade → Magic, Cold
        { 8974, 65720 },  // Black Whetblade → Poison, Blood, Occult
    };

    // Vanilla event flag set when the player has activated 2+ Great Runes at Divine Towers.
    // Checked by sending gate events (e.g. Deeproot→Leyndell, event 12032500 in fogevents.txt)
    // and the Leyndell capital barrier. Giving restored Great Runes via DirectlyGivePlayerItem
    // does NOT set this flag — the vanilla game only sets it via Divine Tower activation.
    private const int GREAT_RUNES_ACTIVATED_FLAG = 182;

    // Restored Great Rune Good IDs (191-196). When any 2+ are given, we must also
    // set GREAT_RUNES_ACTIVATED_FLAG so vanilla gate checks pass.
    private static readonly HashSet<int> GreatRuneGoodIds = new()
    {
        191,  // Godrick's Great Rune
        192,  // Radahn's Great Rune
        193,  // Morgott's Great Rune
        194,  // Rykard's Great Rune
        195,  // Mohg's Great Rune
        196,  // Malenia's Great Rune
    };

    /// <summary>
    /// Inject starting item events into the provided common EMEVD.
    /// Gives Good IDs (key items) and care package items (typed) at game start.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    /// <param name="goodIds">List of Good IDs to award (key items, great runes)</param>
    /// <param name="carePackage">List of typed care package items (weapons, armor, etc.)</param>
    /// <param name="events">Events parser for instruction generation</param>
    public static void Inject(EMEVD commonEmevd, List<int> goodIds, List<CarePackageItem> carePackage, Events events)
    {
        var totalItems = goodIds.Count + carePackage.Count;
        if (totalItems == 0)
        {
            Console.WriteLine("No starting items to inject");
            return;
        }

        Console.WriteLine($"Injecting {totalItems} starting items into common.emevd...");

        // Find the common event initialization (event 0) to add our event calls
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
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

            // Set auxiliary flags for whetblades so the game unlocks infusion affinities
            if (WhetbladeFlags.TryGetValue(goodId, out var auxFlag))
            {
                evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {auxFlag}, ON)"));
                Console.WriteLine($"  Added Good ID {goodId} + infusion flag {auxFlag}");
            }
            else
            {
                Console.WriteLine($"  Added Good ID {goodId}");
            }
        }

        // Set the "2+ Great Runes activated" vanilla flag if we gave enough Great Runes.
        // Without this, sending gates (Deeproot→Leyndell) and the capital barrier
        // show "not enough Great Runes" even though the items are in inventory.
        int greatRuneCount = goodIds.Count(id => GreatRuneGoodIds.Contains(id));
        if (greatRuneCount >= 2)
        {
            evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {GREAT_RUNES_ACTIVATED_FLAG}, ON)"));
            Console.WriteLine($"  Set Great Runes activated flag ({GREAT_RUNES_ACTIVATED_FLAG}) — {greatRuneCount} runes given");
        }

        // Give care package items with their specific ItemType
        // Skip type >= 4 (Gem/Ash of War) — not supported by DirectlyGivePlayerItem,
        // runtime-spawned by the racing mod instead
        foreach (var item in carePackage.Where(i => i.Type < 4))
        {
            var itemType = item.Type >= 0 && item.Type < ItemTypeNames.Length
                ? ItemTypeNames[item.Type]
                : "ItemType.Goods";
            evt.Instructions.Add(events.ParseAdd($"DirectlyGivePlayerItem({itemType}, {item.Id}, 6001, 1)"));
            Console.WriteLine($"  Added {itemType} {item.Name} (id={item.Id})");
        }

        // Log skipped gem items
        foreach (var item in carePackage.Where(i => i.Type >= 4))
        {
            Console.WriteLine($"  Skipping gem item {item.Name} (id={item.Id}, runtime-spawned by mod)");
        }

        // Set flag so we don't give items again on reload
        evt.Instructions.Add(events.ParseAdd($"SetEventFlag(TargetEventFlagType.EventFlag, {ITEMS_GIVEN_FLAG}, ON)"));

        // Add event to EMEVD
        commonEmevd.Events.Add(evt);

        // Add initialization call to event 0 (same pattern as StartingResourcesInjector)
        var initArgs = new byte[12];
        BitConverter.GetBytes(0).CopyTo(initArgs, 0);              // slot = 0
        BitConverter.GetBytes(BASE_EVENT_ID).CopyTo(initArgs, 4);  // eventId
        BitConverter.GetBytes(0).CopyTo(initArgs, 8);              // arg0 = 0 (unused)
        initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));

        Console.WriteLine("Starting item events injected successfully");
    }
}
