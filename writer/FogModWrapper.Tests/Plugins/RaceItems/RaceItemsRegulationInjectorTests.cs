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
        RaceItemsRegulationInjector.Apply(goods, sp, RaceItemsCatalog.Load(ShippedCatalogPath()));
        return (goods, sp);
    }

    [Fact]
    public void Goods_row_drops_flask_fields()
    {
        var (goods, _) = Inject();
        var row = goods[1450807]!;
        Assert.Equal((byte)0, row["goodsType"].Value);
        Assert.Equal(-1, row["replaceItemId"].Value);
        Assert.Equal((byte)2, row["refCategory"].Value);
        Assert.Equal(1450807, row["refId_default"].Value);
        Assert.Equal((byte)1, row["isAutoEquip"].Value);
        Assert.Equal((byte)1, row["isConsume"].Value);
        Assert.Equal((ushort)60390, row["iconId"].Value);
        Assert.Equal((short)1, row["maxNum"].Value);
        Assert.Equal((byte)0, row["isDrop"].Value);
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
        RaceItemsRegulationInjector.Apply(goods, sp, RaceItemsCatalog.Load(ShippedCatalogPath()));

        foreach (var id in new[] { 1450800, 1450807 })
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
        var items = new[]
        {
            new RaceItem("x", "i", 60385, "X", "i", "c", 1450850, 1050290150, 1050290250,
                new RaceItemSpEffect(1450850, 10600, new Dictionary<string, double> { ["noSuchField"] = 1 }),
                null),
        };
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsRegulationInjector.Apply(goods, sp, items));
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
        Assert.Null(sp[1450800]);
    }

    [Fact]
    public void Effect_rows_carry_their_fields()
    {
        var (_, sp) = Inject();
        Assert.Equal(0.5f, sp[1450829]!["maxHpRate"].Value);
        Assert.Equal(15f, sp[1450829]!["effectEndurance"].Value);
        Assert.Equal(99999, sp[1450832]!["poizonAttackPower"].Value);
        Assert.Equal(102360, sp[1450806]!["cycleOccurrenceSpEffectId"].Value);
        Assert.Equal(0.5f, sp[1450806]!["motionInterval"].Value);
        Assert.Equal(40000, sp[1450803]!["soul"].Value);
        Assert.Equal(4101, sp[1450830]!["cycleOccurrenceSpEffectId"].Value);
        Assert.Equal(15f, sp[1450830]!["effectEndurance"].Value);
        Assert.Equal(1f, sp[1450807]!["effectEndurance"].Value);
    }

    [Fact]
    public void Inject_twice_pins_second_call_behavior()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var sp = SpEffectTemplates();
        var items = RaceItemsCatalog.Load(ShippedCatalogPath());
        RaceItemsRegulationInjector.Apply(goods, sp, items);
        // GameEditor.AddRow refuses an existing id: the plugin runs once per build.
        Assert.Throws<ArgumentException>(() => RaceItemsRegulationInjector.Apply(goods, sp, items));
    }
}
