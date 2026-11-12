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

    private static (PARAM goods, PARAM sp) Inject()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        // 10600, 3269 and 3370 are the SpEffect templates the catalog clones from.
        var sp = BuildParamFromDef("SpEffect", 10600);
        AddRowFromTemplate(sp, 3269);
        AddRowFromTemplate(sp, 3370);
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
        Assert.Equal(0f, sp[1450806]!["consumeStaminaRate"].Value);
        Assert.Equal(15f, sp[1450806]!["effectEndurance"].Value);
        Assert.Equal(40000, sp[1450803]!["soul"].Value);
        Assert.Equal((ushort)287, sp[1450830]!["stateInfo"].Value);
    }

    [Fact]
    public void Inject_twice_pins_second_call_behavior()
    {
        var goods = BuildParamFromDef("EquipParamGoods", 250);
        var sp = BuildParamFromDef("SpEffect", 10600);
        AddRowFromTemplate(sp, 3269);
        AddRowFromTemplate(sp, 3370);
        var items = RaceItemsCatalog.Load(ShippedCatalogPath());
        RaceItemsRegulationInjector.Apply(goods, sp, items);
        // GameEditor.AddRow refuses an existing id: the plugin runs once per build.
        Assert.Throws<ArgumentException>(() => RaceItemsRegulationInjector.Apply(goods, sp, items));
    }
}
