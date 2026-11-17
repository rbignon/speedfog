using FogModWrapper.Plugins.RaceItems;
using Xunit;

namespace FogModWrapper.Tests.Plugins.RaceItems;

public class RaceItemsCatalogTests
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

    [Fact]
    public void Loads_shipped_catalog()
    {
        var catalog = RaceItemsCatalog.Load(ShippedCatalogPath());

        Assert.Equal(2, catalog.Goods.Count);
        Assert.Equal(22, catalog.Items.Count);
        Assert.Equal(catalog.Goods.OrderBy(g => g.GoodsId).Select(g => g.GoodsId), catalog.Goods.Select(g => g.GoodsId));

        var raceItem = catalog.Goods.Single(g => g.Key == "race_item");
        Assert.Equal(1450801, raceItem.GoodsId);
        Assert.Equal(1050290101, raceItem.UseFlag);
        Assert.Equal(1050290201, raceItem.RemoveFlag);
        Assert.Equal(60396, raceItem.IconId);
        Assert.Equal(1450801, raceItem.Marker!.Id);
        Assert.Equal(10600, raceItem.Marker.Template);

        var shield = catalog.Goods.Single(g => g.Key == "shield");
        Assert.Null(shield.UseFlag);
        Assert.Null(shield.Marker);

        var hammer = catalog.Items.Single(i => i.Key == "hammer");
        Assert.Equal("race_item", hammer.Goods);
        Assert.Equal(60390, hammer.IconId);
        Assert.Equal(1450827, hammer.Effect!.Id);
        Assert.Equal(10600, hammer.Effect.Template);
        Assert.Equal(0.35, hammer.Effect.Fields["equipWeightChangeRate"]);
        Assert.Equal(15, hammer.Effect.Fields["effectEndurance"]);

        Assert.Equal("shield", catalog.Items.Single(i => i.Key == "shield").Goods);
    }

    private const string Icons = "[icons]\nshield = 60385\n";
    private const string Marker = "marker = { template = 10600, fields = {} }";

    private static string Goods(string key, int goodsId, string extra = "") => $"""
        [[goods]]
        key = "{key}"
        icon = "shield"
        name = "N"
        info = "I"
        caption = "C"
        goods_id = {goodsId}
        {extra}

        """;

    /// <summary>A usable goods (one with a marker) keyed "g".</summary>
    private static string UsableGoods() => Goods("g", 1450801, Marker);

    private static string Item(string key, string goods = "g", string extra = "") => $"""
        [[item]]
        key = "{key}"
        goods = "{goods}"
        icon = "shield"
        name = "N"
        {extra}

        """;

    private static string Effect(int id) => $"effect = {{ id = {id}, template = 10600, fields = {{}} }}";

    [Fact]
    public void Rejects_duplicate_goods_id()
    {
        var toml = Icons + Goods("a", 1450800) + Goods("b", 1450800);
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains("duplicate goods_id", ex.Message);
    }

    [Fact]
    public void Rejects_duplicate_key()
    {
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + Goods("a", 1450800) + Goods("a", 1450802)));
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + UsableGoods() + Item("a") + Item("a")));
    }

    [Fact]
    public void Goods_and_items_keys_are_separate_namespaces()
    {
        // The shipped catalog has a "shield" goods and a "shield" item.
        var catalog = RaceItemsCatalog.Parse(Icons + Goods("shield", 1450800) + Item("shield", "shield"));
        Assert.Equal("shield", catalog.Items.Single().Goods);
    }

    [Fact]
    public void Rejects_goods_id_outside_reserved_range()
    {
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(Icons + Goods("a", 1450900)));
    }

    [Fact]
    public void Rejects_goods_id_in_the_effect_slice()
    {
        // A usable goods at 1450820 would give a marker id an item effect can take.
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(Icons + Goods("a", 1450820)));
        Assert.Contains("goods_id 1450820 outside 1450800-1450819", ex.Message);
    }

    [Fact]
    public void Derives_the_flags_from_the_goods_id()
    {
        // The flags are not in the catalog: two goods cannot share one, and
        // only a goods with a marker (one that can be used) gets a use flag.
        var catalog = RaceItemsCatalog.Parse(Icons + Goods("passive", 1450804) + Goods("usable", 1450807, Marker));
        var passive = catalog.Goods.Single(g => g.Key == "passive");
        Assert.Null(passive.UseFlag);
        Assert.Equal(1050290204, passive.RemoveFlag);
        var usable = catalog.Goods.Single(g => g.Key == "usable");
        Assert.Equal(1050290107, usable.UseFlag);
        Assert.Equal(1050290207, usable.RemoveFlag);
    }

    [Fact]
    public void Rejects_effect_id_outside_the_effect_range()
    {
        // 1450819 is a reserved param id, but below the item effect range.
        var ex = Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + UsableGoods() + Item("a", extra: Effect(1450819))));
        Assert.Contains("effect id 1450819 outside", ex.Message);
    }

    [Fact]
    public void Rejects_duplicate_effect_id()
    {
        var toml = Icons + UsableGoods() + Item("a", extra: Effect(1450820)) + Item("b", extra: Effect(1450820));
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains("duplicate effect id", ex.Message);
    }

    [Fact]
    public void Rejects_item_on_unknown_goods()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + UsableGoods() + Item("a", "nope")));
        Assert.Contains("unknown goods 'nope'", ex.Message);
    }

    [Fact]
    public void Rejects_effect_on_goods_without_use()
    {
        // The Shield goods is never used: nothing would apply the effect.
        var toml = Icons + Goods("shield", 1450800) + Item("a", "shield", Effect(1450820));
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains("cannot be used", ex.Message);
    }

    [Fact]
    public void Rejects_icon_id_outside_the_reserved_range()
    {
        var toml = "[icons]\nshield = 60415\n" + Goods("a", 1450800);
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains("icon id", ex.Message);
    }

    [Fact]
    public void Rejects_duplicate_icon_id()
    {
        var toml = "[icons]\nshield = 60385\nbanana = 60385\n" + Goods("a", 1450800);
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
        Assert.Contains("share id", ex.Message);
    }

    [Fact]
    public void Rejects_unknown_icon()
    {
        var badGoods = Icons + Goods("a", 1450800).Replace("icon = \"shield\"", "icon = \"nope\"");
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(badGoods));
        var badItem = Icons + UsableGoods() + Item("a").Replace("icon = \"shield\"", "icon = \"nope\"");
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(badItem));
    }

    private const string HunterToml = """

        [hunter]
        chr_id = 2130
        source_npc_param = 21300014
        source_think = 21300000
        npc_param_base = {0}
        think_param = {1}
        """;

    private static string HunterTable(int baseId, int think) =>
        HunterToml.Replace("{0}", baseId.ToString()).Replace("{1}", think.ToString());

    [Fact]
    public void Hunter_table_is_optional()
    {
        Assert.Null(RaceItemsCatalog.Parse("").Hunter);
        var hunter = RaceItemsCatalog.Parse(HunterTable(1450850, 1450850)).Hunter!;
        Assert.Equal(new RaceItemHunter(2130, 21300014, 21300000, 1450850, 1450850), hunter);
    }

    [Theory]
    [InlineData(1450849, 1450850)]  // below the reserved range
    [InlineData(1450867, 1450850)]  // the 34th tier row leaves the range
    [InlineData(1450850, 1450900)]  // think row outside
    public void Rejects_a_hunter_whose_rows_leave_the_range(int baseId, int think)
    {
        var ex = Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(HunterTable(baseId, think)));
        Assert.Contains("hunter", ex.Message);
    }
}
