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

    private static EMEVD RunInject(Events events)
    {
        var commonEmevd = new EMEVD();
        commonEmevd.Events.Add(new EMEVD.Event(0));
        RaceItemsEmevdInjector.Inject(commonEmevd, events, RaceItemsCatalog.Load(ShippedCatalogPath()));
        return commonEmevd;
    }

    private static EMEVD.Event Evt(EMEVD emevd, long id) => emevd.Events.Single(e => e.ID == id);

    private static long UseId(int n) => SpeedFogIds.RaceItemEvents.Base + n;
    private static long RemoveId(int n) => SpeedFogIds.RaceItemEvents.Base + 50 + n;

    [Fact]
    public void Adds_one_use_event_per_usable_item_and_one_removal_event_per_item()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var emevd = RunInject(events);

        for (int n = 1; n <= 12; n++)
            Assert.Single(emevd.Events, e => e.ID == UseId(n));
        for (int n = 0; n <= 12; n++)
            Assert.Single(emevd.Events, e => e.ID == RemoveId(n));
        // The Shield (n = 0) is not usable: no use event.
        Assert.DoesNotContain(emevd.Events, e => e.ID == UseId(0));
        Assert.Equal(1 + 12 + 13, emevd.Events.Count);

        var inits = Evt(emevd, 0).Instructions.Where(i => i.Bank == 2000 && i.ID == 0).ToList();
        Assert.Equal(25, inits.Count);
    }

    [Fact]
    public void UseEvent_waits_for_marker_to_end_before_restart()
    {
        var events = BuildEvents();
        if (events == null)
            return;

        var mushroom = Evt(RunInject(events), UseId(6));

        // IF Character Has SpEffect, Set Event Flag, IF Character Has SpEffect
        // (not), END Unconditionally; ids from data/er-common.emedf.json.
        Assert.Equal(
            new[] { (4, 5), (2003, 66), (4, 5), (1000, 4) },
            mushroom.Instructions.Select(i => (i.Bank, i.ID)).ToArray());
        Assert.Equal(1450806, BitConverter.ToInt32(mushroom.Instructions[0].ArgData, 8));
        Assert.Equal(1050290106u, BitConverter.ToUInt32(mushroom.Instructions[1].ArgData, 4));
        Assert.Equal(1450806, BitConverter.ToInt32(mushroom.Instructions[2].ArgData, 8));
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
    }
}
