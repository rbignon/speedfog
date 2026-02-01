// writer/SpeedFogWriter/Writers/StartingItemsWriter.cs
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

public class StartingItemsWriter
{
    private const int ItemTypeGoods = 3;

    private static readonly List<(int ItemId, int Quantity, string Name)> CoreKeyItems = new()
    {
        (8109, 1, "Academy Glintstone Key"),
        (8010, 1, "Rusty Key"),
        (8105, 1, "Dectus Medallion (Left)"),
        (8106, 1, "Dectus Medallion (Right)"),
        (8107, 1, "Rold Medallion"),
        (8000, 10, "Stonesword Key"),
        (2160, 1, "Pureblood Knight's Medal"),
    };

    private static readonly Dictionary<string, (int ItemId, int Quantity)> ZoneSpecificItems = new()
    {
        ["academy"] = (8109, 1),
        ["volcano_manor"] = (8134, 1),
        ["carian_study_hall"] = (8111, 1),
        ["haligtree"] = (8175, 1),
        ["consecrated_snowfield"] = (8175, 1),
    };

    private readonly EMEVD _commonEmevd;
    private readonly Events _events;
    private readonly EntityIdAllocator _idAllocator;
    private uint _nextGiveItemFlag = 79900100;

    public StartingItemsWriter(EMEVD commonEmevd, Events events, EntityIdAllocator idAllocator)
    {
        _commonEmevd = commonEmevd;
        _events = events;
        _idAllocator = idAllocator;
    }

    public void AddCoreItems()
    {
        foreach (var (itemId, quantity, name) in CoreKeyItems)
        {
            AddItemGiveEvent(itemId, quantity, name);
        }
    }

    public void AddItemsForZones(IEnumerable<string> zones)
    {
        var addedItems = new HashSet<int>();

        foreach (var zone in zones)
        {
            foreach (var (zonePrefix, item) in ZoneSpecificItems)
            {
                if (zone.StartsWith(zonePrefix) && !addedItems.Contains(item.ItemId))
                {
                    AddItemGiveEvent(item.ItemId, item.Quantity, zonePrefix);
                    addedItems.Add(item.ItemId);
                }
            }
        }

        // Haligtree needs both medallion halves
        if (addedItems.Contains(8175) && !addedItems.Contains(8176))
        {
            AddItemGiveEvent(8176, 1, "Haligtree Medallion (Right)");
        }
    }

    private void AddItemGiveEvent(int itemId, int quantity, string debugName)
    {
        var giveFlag = _nextGiveItemFlag++;
        var eventId = _idAllocator.AllocateEventId();

        var evt = new EMEVD.Event(eventId, EMEVD.Event.RestBehaviorType.Default);

        // EndIfEventFlag(End, ON, EventFlag, giveFlag)
        evt.Instructions.Add(_events.ParseAdd(
            $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {giveFlag})"));

        // DirectlyGivePlayerItem(ItemType.Goods, itemId, 6001, quantity)
        evt.Instructions.Add(_events.ParseAdd(
            $"DirectlyGivePlayerItem({ItemTypeGoods}, {itemId}, 6001, {quantity})"));

        // SetEventFlag(EventFlag, giveFlag, ON)
        evt.Instructions.Add(_events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {giveFlag}, ON)"));

        _commonEmevd.Events.Add(evt);
        AddEventInitialization(eventId);

        Console.WriteLine($"    Added: {debugName} (id={itemId}, qty={quantity})");
    }

    private void AddEventInitialization(uint eventId)
    {
        var event0 = _commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (event0 == null)
        {
            event0 = new EMEVD.Event(0, EMEVD.Event.RestBehaviorType.Default);
            _commonEmevd.Events.Insert(0, event0);
        }

        event0.Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object>
        {
            0,
            (int)eventId,
            0
        }));
    }
}
