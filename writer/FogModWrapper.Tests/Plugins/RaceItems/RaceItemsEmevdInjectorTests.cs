using FogModWrapper.Plugins.RaceItems;
using SoulsFormats;
using SoulsIds;
using Xunit;

namespace FogModWrapper.Tests.Plugins.RaceItems;

/// <summary>
/// RaceItemsEmevdInjector builds its events through FogMod's
/// Events.ParseAdd, so the tests need a real SoulsIds.Events instance backed
/// by the gitignored data/er-common.emedf.json; they skip (early return) when
/// it is absent, like HalloweenDarknessInjectorTests.
/// </summary>
public class RaceItemsEmevdInjectorTests
{
    private static string? FindDataDir()
    {
        var envDir = Environment.GetEnvironmentVariable("DATA_DIR");
        if (!string.IsNullOrEmpty(envDir) && File.Exists(Path.Combine(envDir, "er-common.emedf.json")))
            return envDir;

        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var candidate = Path.GetFullPath(Path.Combine(baseDir, "../../../../..", "data"));
        if (File.Exists(Path.Combine(candidate, "er-common.emedf.json")))
            return candidate;

        return null;
    }

    private static Events? BuildEvents()
    {
        var dataDir = FindDataDir();
        return dataDir == null
            ? null
            : new Events(Path.Combine(dataDir, "er-common.emedf.json"), darkScriptMode: true, paramAwareMode: true);
    }

    private static string ShippedCatalogPath()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "plugins", "race_items.toml");
            if (File.Exists(candidate))
                return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("data/plugins/race_items.toml not found above " + AppContext.BaseDirectory);
    }

    private static readonly IReadOnlyList<RaceItemTalisman> TwoTalismans = new[]
    {
        new RaceItemTalisman(1000, "Crimson Amber Medallion", 1050290300),
        new RaceItemTalisman(8240, "Talisman of All Crucibles", 1050290301),
    };

    private static EMEVD RunInject(Events events, IReadOnlyList<RaceItemTalisman>? talismans = null)
    {
        var commonEmevd = new EMEVD();
        commonEmevd.Events.Add(new EMEVD.Event(0));
        RaceItemsEmevdInjector.Inject(
            commonEmevd, events, RaceItemsCatalog.Load(ShippedCatalogPath()).Goods,
            talismans ?? Array.Empty<RaceItemTalisman>());
        return commonEmevd;
    }

    private static EMEVD.Event Evt(EMEVD emevd, long id) => emevd.Events.Single(e => e.ID == id);

    private static long UseId(int n) => SpeedFogIds.RaceItemEvents.Base + n;
    private static long RemoveId(int n) => SpeedFogIds.RaceItemEvents.Base + 50 + n;

    [Fact]
    public void Adds_one_use_event_for_the_race_item_and_one_removal_event_per_goods()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var emevd = RunInject(events);

        // The Race Item (n = 1) has a use event; the Shield (n = 0) is never
        // used. Both goods have a removal event.
        var expectedIds = new long[] { UseId(1), RemoveId(0), RemoveId(1) };
        var range = SpeedFogIds.RaceItemEvents;
        Assert.Equal(expectedIds,
            emevd.Events.Select(e => e.ID).Where(id => id >= range.Base && id < range.Base + range.Capacity).Order());
        Assert.Equal(1 + 3, emevd.Events.Count);

        var inits = Evt(emevd, 0).Instructions.Where(i => i.Bank == 2000 && i.ID == 0).ToList();
        var initIds = inits.Select(i => (long)BitConverter.ToInt32(i.ArgData, 4)).Order();
        Assert.Equal(expectedIds, initIds);
    }

    [Fact]
    public void UseEvent_waits_for_marker_to_end_before_restart()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var use = Evt(RunInject(events), UseId(1));

        // IF Character Has SpEffect, Set Event Flag, IF Character Has SpEffect
        // (not), END Unconditionally; ids from data/er-common.emedf.json.
        Assert.Equal(
            new[] { (4, 5), (2003, 66), (4, 5), (1000, 4) },
            use.Instructions.Select(i => (i.Bank, i.ID)).ToArray());
        // Has-SpEffect layout: entity @4, SpEffect @8, should-have @12,
        // comparison type @13, number of characters (float) @16.
        AssertHasSpEffect(use.Instructions[0], 1450801, shouldHave: 1);
        AssertHasSpEffect(use.Instructions[2], 1450801, shouldHave: 0);
        // Set Event Flag: flag @4, state @8 (ON = 1).
        Assert.Equal(1050290101u, BitConverter.ToUInt32(use.Instructions[1].ArgData, 4));
        Assert.Equal(1, use.Instructions[1].ArgData[8]);
        AssertRestart(use.Instructions[3]);
    }

    [Fact]
    public void RemovalEvent_removes_its_goods_and_clears_its_flag()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var shield = Evt(RunInject(events), RemoveId(0));

        Assert.Equal(
            new[] { (3, 0), (2003, 24), (2003, 66), (1000, 4) },
            shield.Instructions.Select(i => (i.Bank, i.ID)).ToArray());
        // Remove Item From Player: item type Goods (3), id, quantity.
        Assert.Equal(3, shield.Instructions[1].ArgData[0]);
        Assert.Equal(1450800, BitConverter.ToInt32(shield.Instructions[1].ArgData, 4));
        Assert.Equal(1, BitConverter.ToInt32(shield.Instructions[1].ArgData, 8));
        // Set Event Flag: flag 1050290200, state OFF (0).
        Assert.Equal(1050290200u, BitConverter.ToUInt32(shield.Instructions[2].ArgData, 4));
        Assert.Equal(0, shield.Instructions[2].ArgData[8]);
        // IF Event Flag: state @1 (ON = 1), flag @4.
        Assert.Equal(1, shield.Instructions[0].ArgData[1]);
        Assert.Equal(1050290200u, BitConverter.ToUInt32(shield.Instructions[0].ArgData, 4));
        AssertRestart(shield.Instructions[3]);
    }

    [Fact]
    public void Adds_one_removal_event_per_talisman_in_its_own_range()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var emevd = RunInject(events, TwoTalismans);

        var range = SpeedFogIds.RaceItemTalismanEvents;
        var expectedIds = new long[] { range.Base, range.Base + 1 };
        Assert.Equal(expectedIds,
            emevd.Events.Select(e => e.ID).Where(id => id >= range.Base && id < range.End).Order());
        var initIds = Evt(emevd, 0).Instructions
            .Where(i => i.Bank == 2000 && i.ID == 0)
            .Select(i => (long)BitConverter.ToInt32(i.ArgData, 4))
            .Where(id => id >= range.Base && id < range.End)
            .Order();
        Assert.Equal(expectedIds, initIds);
    }

    [Fact]
    public void TalismanRemovalEvent_removes_its_talisman_and_clears_its_flag()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var second = Evt(RunInject(events, TwoTalismans), SpeedFogIds.RaceItemTalismanEvents.Base + 1);

        Assert.Equal(
            new[] { (3, 0), (2003, 24), (2003, 66), (1000, 4) },
            second.Instructions.Select(i => (i.Bank, i.ID)).ToArray());
        // IF Event Flag: state @1 (ON = 1), flag @4.
        Assert.Equal(1, second.Instructions[0].ArgData[1]);
        Assert.Equal(1050290301u, BitConverter.ToUInt32(second.Instructions[0].ArgData, 4));
        // Remove Item From Player: item type Ring (2, the talismans), id, quantity.
        Assert.Equal(2, second.Instructions[1].ArgData[0]);
        Assert.Equal(8240, BitConverter.ToInt32(second.Instructions[1].ArgData, 4));
        Assert.Equal(1, BitConverter.ToInt32(second.Instructions[1].ArgData, 8));
        // Set Event Flag: the same flag, OFF (0).
        Assert.Equal(1050290301u, BitConverter.ToUInt32(second.Instructions[2].ArgData, 4));
        Assert.Equal(0, second.Instructions[2].ArgData[8]);
        AssertRestart(second.Instructions[3]);
    }

    [Fact]
    public void Rejects_more_talismans_than_event_slots()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var tooMany = Enumerable.Range(0, SpeedFogIds.RaceItemTalismanEvents.Capacity + 1)
            .Select(n => new RaceItemTalisman(1000 + n, "T", SpeedFogIds.RaceItemTalismanRemoveFlagBase + n))
            .ToList();
        Assert.Throws<InvalidOperationException>(() => RunInject(events, tooMany));
    }

    private static void AssertHasSpEffect(EMEVD.Instruction ins, int speffect, int shouldHave)
    {
        Assert.Equal(10000, BitConverter.ToInt32(ins.ArgData, 4));
        Assert.Equal(speffect, BitConverter.ToInt32(ins.ArgData, 8));
        Assert.Equal(shouldHave, ins.ArgData[12]);
        Assert.Equal(0, ins.ArgData[13]); // ComparisonType.Equal
        Assert.Equal(1f, BitConverter.ToSingle(ins.ArgData, 16));
    }

    private static void AssertRestart(EMEVD.Instruction ins) =>
        Assert.Equal(1, ins.ArgData[0]); // EventEndType.Restart
}
