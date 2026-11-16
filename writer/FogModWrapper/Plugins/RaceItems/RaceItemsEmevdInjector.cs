using FogModWrapper.Common;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Plugins.RaceItems;

/// <summary>
/// Adds the race items' common.emevd events: one use event per usable goods
/// (marker SpEffect applied, raise the use flag, wait for the marker to end,
/// restart) and one removal event per goods (the racing mod raises the removal
/// flag, the event removes the goods and clears the flag). Each talisman of
/// the talisman table gets a removal event of the same shape, for the
/// Talisman Thief.
/// Adapted from FogRando's common_bellofreturn
/// (data/fogevents.txt:389-406), which also waits on a goods marker SpEffect
/// (<c>IfCharacterHasSpEffect(MAIN, 10000, 141626, true, ...)</c>) and ends
/// with <c>EndUnconditionally(EventEndType.Restart)</c>.
/// Event ids: <c>RaceItemEvents.Base + n</c> (use), <c>+ 50 + n</c> (removal),
/// n = goods id - 1450800; talisman removal: <c>RaceItemTalismanEvents.Base + n</c>,
/// n = the talisman's index in the table.
/// </summary>
public static class RaceItemsEmevdInjector
{
    private const int FirstGoodsId = 1450800;
    private const int RemovalOffset = 50;

    public static void Inject(
        EMEVD commonEmevd, Events events, IReadOnlyList<RaceItemGoods> goods,
        IReadOnlyList<RaceItemTalisman> talismans)
    {
        // The talisman table's loader caps it at the flag count, equal to the
        // event range's capacity; this guards the event range itself.
        if (talismans.Count > SpeedFogIds.RaceItemTalismanEvents.Capacity)
            throw new InvalidOperationException(
                $"race_items: {talismans.Count} talismans, more than the talisman event slots");

        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping race item events");
            return;
        }

        int useCount = 0;
        foreach (var g in goods)
        {
            int n = g.GoodsId - FirstGoodsId;
            if (n < 0 || n >= RemovalOffset)
                throw new InvalidOperationException(
                    $"race_items: goods id {g.GoodsId} outside the event slot range");

            if (g.UseFlag is int useFlag && g.Marker is not null)
            {
                AddEvent(commonEmevd, initEvent, events, SpeedFogIds.RaceItemEvents.Base + n, new[]
                {
                    $"IfCharacterHasSpEffect(MAIN, 10000, {g.Marker.Id}, true, ComparisonType.Equal, 1)",
                    $"SetEventFlag(TargetEventFlagType.EventFlag, {useFlag}, ON)",
                    $"IfCharacterHasSpEffect(MAIN, 10000, {g.Marker.Id}, false, ComparisonType.Equal, 1)",
                    "EndUnconditionally(EventEndType.Restart)",
                });
                useCount++;
            }

            AddEvent(commonEmevd, initEvent, events, SpeedFogIds.RaceItemEvents.Base + RemovalOffset + n, new[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {g.RemoveFlag})",
                $"RemoveItemFromPlayer(ItemType.Goods, {g.GoodsId}, 1)",
                $"SetEventFlag(TargetEventFlagType.EventFlag, {g.RemoveFlag}, OFF)",
                "EndUnconditionally(EventEndType.Restart)",
            });
        }

        for (int n = 0; n < talismans.Count; n++)
        {
            var t = talismans[n];
            AddEvent(commonEmevd, initEvent, events, SpeedFogIds.RaceItemTalismanEvents.Base + n, new[]
            {
                $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {t.RemoveFlag})",
                $"RemoveItemFromPlayer(ItemType.Ring, {t.Id}, 1)",
                $"SetEventFlag(TargetEventFlagType.EventFlag, {t.RemoveFlag}, OFF)",
                "EndUnconditionally(EventEndType.Restart)",
            });
        }

        Console.WriteLine(
            $"race_items: {useCount} use events, {goods.Count} removal events "
            + $"and {talismans.Count} talisman removal events");
    }

    private static void AddEvent(
        EMEVD commonEmevd, EMEVD.Event initEvent, Events events, int id, string[] lines)
    {
        var evt = new EMEVD.Event(id);
        lock (events)
        {
            foreach (var line in lines)
                evt.Instructions.Add(events.ParseAdd(line));
        }
        commonEmevd.Events.Add(evt);
        initEvent.Instructions.Add(EmevdHelper.InitializeEvent(id));
    }
}
