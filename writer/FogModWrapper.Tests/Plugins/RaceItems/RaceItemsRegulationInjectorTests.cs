using FogModWrapper.Plugins.Halloween;
using FogModWrapper.Plugins.RaceItems;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests.Plugins.RaceItems;

public class RaceItemsRegulationInjectorTests
{
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

    /// <summary>The SpEffect templates the shipped catalog clones from.</summary>
    private static PARAM SpEffectTemplates()
    {
        var sp = BuildParamFromDef("SpEffect", 10600);
        AddRowFromTemplate(sp, 3269);
        AddRowFromTemplate(sp, 3370);
        return sp;
    }

    private static (PARAM goods, PARAM sp) Inject()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var sp = SpEffectTemplates();
        { var (npc, think) = HunterSources(); RaceItemsRegulationInjector.Apply(goods, sp, RaceItemsCatalog.Load(ShippedCatalogPath()), npc, think); }
        return (goods, sp);
    }

    [Fact]
    public void Race_item_goods_is_usable_and_auto_equipped()
    {
        var (goods, sp) = Inject();
        var row = goods[1450801]!;
        Assert.Equal((byte)0, row["goodsType"].Value);
        Assert.Equal(-1, row["replaceItemId"].Value);
        Assert.Equal((byte)2, row["refCategory"].Value);
        Assert.Equal(1450801, row["refId_default"].Value);
        Assert.Equal((byte)1, row["isAutoEquip"].Value);
        Assert.Equal((byte)1, row["isConsume"].Value);
        Assert.Equal((ushort)60396, row["iconId"].Value);
        Assert.Equal((short)1, row["maxNum"].Value);
        Assert.Equal((byte)0, row["isDrop"].Value);
        // Its neutral marker, which the use event waits on.
        Assert.Equal(1f, sp[1450801]!["effectEndurance"].Value);
    }

    [Fact]
    public void Goods_row_drops_the_physick_supply_and_tutorial_fields()
    {
        // The template carries the Physick's grace refill and tutorial popup.
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var template = goods[250]!;
        template["isFullSuppleItem"].Value = (byte)1;
        template["suppleType"].Value = (byte)3;
        template["itemGetTutorialFlagId"].Value = 710590u;
        var sp = SpEffectTemplates();
        { var (npc, think) = HunterSources(); RaceItemsRegulationInjector.Apply(goods, sp, RaceItemsCatalog.Load(ShippedCatalogPath()), npc, think); }

        foreach (var id in new[] { 1450800, 1450801 })
        {
            var row = goods[id]!;
            Assert.Equal((byte)0, row["isFullSuppleItem"].Value);
            Assert.Equal((byte)0, row["suppleType"].Value);
            Assert.Equal(0u, row["itemGetTutorialFlagId"].Value);
        }
    }

    [Fact]
    public void Unknown_field_names_the_field_the_row_and_the_param()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var sp = BuildParamFromDef("SpEffect", 10600);
        var catalog = new RaceItemsCatalog(
            new[]
            {
                new RaceItemGoods("x", "i", 60385, "X", "i", "c", 1450850, 1050290150, 1050290250,
                    new RaceItemSpEffect(1450850, 10600, new Dictionary<string, double> { ["noSuchField"] = 1 })),
            },
            Array.Empty<RaceItem>());
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsRegulationInjector.Apply(goods, sp, catalog));
        Assert.Contains("noSuchField", ex.Message);
        Assert.Contains("1450850", ex.Message);
        Assert.Contains("SpEffectParam", ex.Message);
    }

    [Fact]
    public void Shield_is_passive()
    {
        var (goods, sp) = Inject();
        var row = goods[1450800]!;
        Assert.Equal((byte)0, row["isAutoEquip"].Value);
        Assert.Equal((byte)0, row["isConsume"].Value);
        Assert.Equal(-1, row["refId_default"].Value);
        Assert.Equal((ushort)60385, row["iconId"].Value);
        Assert.Null(sp[1450800]);
    }

    [Fact]
    public void Item_effect_rows_carry_their_fields()
    {
        var (_, sp) = Inject();
        Assert.Equal(102360, sp[1450826]!["cycleOccurrenceSpEffectId"].Value);
        Assert.Equal(15f, sp[1450826]!["effectEndurance"].Value);
        Assert.Equal(0.5f, sp[1450826]!["motionInterval"].Value);
        Assert.Equal(20000, sp[1450822]!["soul"].Value);
        Assert.Equal(0.5f, sp[1450829]!["maxHpRate"].Value);
        Assert.Equal(15f, sp[1450829]!["effectEndurance"].Value);
        Assert.Equal(99999, sp[1450832]!["poizonAttackPower"].Value);
        Assert.Equal(4101, sp[1450830]!["cycleOccurrenceSpEffectId"].Value);
        Assert.Equal(15f, sp[1450830]!["effectEndurance"].Value);
    }

    [Fact]
    public void No_goods_row_per_item()
    {
        // Two goods carry every item: no row (nor marker) for the v1 per-item
        // goods ids.
        var (goods, sp) = Inject();
        for (int id = 1450802; id <= 1450812; id++)
        {
            Assert.Null(goods[id]);
            Assert.Null(sp[id]);
        }
    }

    [Fact]
    public void Inject_twice_pins_second_call_behavior()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var sp = SpEffectTemplates();
        var catalog = RaceItemsCatalog.Load(ShippedCatalogPath());
        var (npc, think) = HunterSources();
        RaceItemsRegulationInjector.Apply(goods, sp, catalog, npc, think);
        // GameEditor.AddRow refuses an existing id: the plugin runs once per build.
        Assert.Throws<ArgumentException>(() => RaceItemsRegulationInjector.Apply(goods, sp, catalog, npc, think));
    }

    // --- The Hunter's rows (Margit cloned once per enemy tier) ---

    private const int HunterSourceNpc = 21300014;
    private const int HunterSourceThink = 21300000;

    /// <summary>NpcParam and NpcThinkParam fixtures holding Margit's rows; the
    /// source carries what a seed's randomizers may have left on it.</summary>
    private static (PARAM npc, PARAM think) HunterSources(int backhome = 9999, int eye = 60)
    {
        var npc = BuildParamFromDef("NpcParam", HunterSourceNpc);
        var src = npc[HunterSourceNpc]!;
        src["spEffectID3"].Value = 7030;   // his own tier 3 scaling
        src["spEffectID4"].Value = 5402;   // a resident SpEffect that must stay
        src["getSoul"].Value = 5000u;
        src["isSoulGetByBoss"].Value = (byte)1;
        foreach (var f in new[] { "itemLotId_enemy", "itemLotId_map", "sleepCollectorItemLotId_enemy", "sleepCollectorItemLotId_map" })
            src[f].Value = 12345;
        src["behaviorVariationId"].Value = 21300;
        src["teamType"].Value = (byte)7;

        var think = BuildParamFromDef("NpcThinkParam", HunterSourceThink);
        var t = think[HunterSourceThink]!;
        foreach (var f in new[] { "maxBackhomeDist", "backhomeDist", "backhomeBattleDist" })
            t[f].Value = (ushort)backhome;
        t["eye_dist"].Value = (ushort)eye;
        t["BattleStartDist"].Value = (ushort)10;
        return (npc, think);
    }

    private static RaceItemsCatalog WithHunter(RaceItemHunter? hunter) =>
        new(Array.Empty<RaceItemGoods>(), Array.Empty<RaceItem>(), hunter);

    private static readonly RaceItemHunter Hunter = new(2130, HunterSourceNpc, HunterSourceThink, 1450850, 1450850);

    private static void ApplyHunter(PARAM npc, PARAM think, RaceItemHunter? hunter) =>
        RaceItemsRegulationInjector.Apply(
            BuildParamFromDef("EquipParamGoods", 250), BuildParamFromDef("SpEffect", 10600),
            WithHunter(hunter), npc, think);

    [Fact]
    public void Hunter_rows_carry_one_tier_each()
    {
        var (npc, think) = HunterSources();
        ApplyHunter(npc, think, Hunter);

        Assert.Equal(34, npc.Rows.Count(r => r.ID >= 1450850 && r.ID <= 1450899));
        var expected = new Dictionary<int, int> { [1] = 7010, [20] = 7200, [21] = 20007000, [34] = 20007130 };
        for (int tier = 1; tier <= 34; tier++)
        {
            var row = npc[1450850 + tier - 1]!;
            var scaling = Enumerable.Range(0, 32)
                .Select(i => (int)row[$"spEffectID{i}"].Value)
                .Where(v => (v >= 7000 && v <= 7200) || (v >= 20007000 && v <= 20007130))
                .ToList();
            Assert.Equal(new[] { AmbientSpawnInjector.AreaScalingSpEffect(tier) }, scaling);
            if (expected.TryGetValue(tier, out var want))
                Assert.Equal(want, scaling[0]);
            Assert.Equal(5402, row["spEffectID4"].Value); // resident SpEffects stay

            Assert.Equal(0u, row["getSoul"].Value);
            Assert.Equal((byte)0, row["isSoulGetByBoss"].Value);
            Assert.Equal(-1, row["itemLotId_enemy"].Value);
            Assert.Equal(-1, row["itemLotId_map"].Value);
            Assert.Equal(-1, row["sleepCollectorItemLotId_enemy"].Value);
            Assert.Equal(-1, row["sleepCollectorItemLotId_map"].Value);
            Assert.Equal(21300, row["behaviorVariationId"].Value);
            Assert.Equal((byte)7, row["teamType"].Value);
        }
        // The source row is not touched.
        Assert.Equal(5000u, npc[HunterSourceNpc]!["getSoul"].Value);
        Assert.Equal(7030, npc[HunterSourceNpc]!["spEffectID3"].Value);
    }

    [Fact]
    public void Hunter_think_never_goes_home()
    {
        // The enemy randomizer capped the source row.
        var (npc, think) = HunterSources(backhome: 200, eye: 100);
        ApplyHunter(npc, think, Hunter);

        var row = think[1450850]!;
        foreach (var f in new[] { "maxBackhomeDist", "backhomeDist", "backhomeBattleDist" })
            Assert.Equal((ushort)9999, row[f].Value);
        Assert.Equal((ushort)60, row["eye_dist"].Value);
        Assert.Equal((ushort)10, row["BattleStartDist"].Value);
        Assert.Equal((ushort)200, think[HunterSourceThink]!["maxBackhomeDist"].Value);
    }

    [Fact]
    public void A_catalog_without_hunter_adds_no_npc_row()
    {
        var (npc, think) = HunterSources();
        ApplyHunter(npc, think, null);
        Assert.Single(npc.Rows);
        Assert.Single(think.Rows);
    }

    [Fact]
    public void Hunter_with_a_missing_source_row_fails_loudly()
    {
        var npc = BuildParamFromDef("NpcParam", 21300099);
        var think = BuildParamFromDef("NpcThinkParam", HunterSourceThink);
        Assert.ThrowsAny<Exception>(() => ApplyHunter(npc, think, Hunter));
    }

    [Fact]
    public void Shipped_catalog_hunter_lands_on_the_reserved_rows()
    {
        var catalog = RaceItemsCatalog.Load(ShippedCatalogPath());
        Assert.Equal(1450850, catalog.Hunter!.NpcParamBase);
        Assert.Equal(1450850, catalog.Hunter.ThinkParam);
    }
}
