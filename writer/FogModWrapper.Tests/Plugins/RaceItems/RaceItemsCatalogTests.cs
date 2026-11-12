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
        var items = RaceItemsCatalog.Load(ShippedCatalogPath());

        Assert.Equal(13, items.Count);
        Assert.Equal(items.OrderBy(i => i.GoodsId).Select(i => i.GoodsId), items.Select(i => i.GoodsId));

        var hammer = items.Single(i => i.Key == "hammer");
        Assert.Equal(60390, hammer.IconId);
        Assert.Equal(1450807, hammer.Marker!.Id);
        Assert.Equal(10600, hammer.Marker.Template);
        Assert.Equal(1450827, hammer.Effect!.Id);
        Assert.Equal(10600, hammer.Effect.Template);
        Assert.Equal(0.35, hammer.Effect.Fields["equipWeightChangeRate"]);
        Assert.Equal(15, hammer.Effect.Fields["effectEndurance"]);

        var shield = items.Single(i => i.Key == "shield");
        Assert.Null(shield.UseFlag);
        Assert.Null(shield.Marker);
    }

    private const string Icons = "[icons]\nshield = 60385\n";

    private static string Item(string key, int goodsId, string extra = "") => $"""
        [[item]]
        key = "{key}"
        icon = "shield"
        name = "N"
        info = "I"
        caption = "C"
        goods_id = {goodsId}
        remove_flag = 1050290200
        {extra}

        """;

    [Fact]
    public void Rejects_duplicate_goods_id()
    {
        var toml = Icons + Item("a", 1450800) + Item("b", 1450800);
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
    }

    [Fact]
    public void Rejects_duplicate_key()
    {
        var toml = Icons + Item("a", 1450800) + Item("a", 1450801);
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
    }

    [Fact]
    public void Rejects_goods_id_outside_reserved_range()
    {
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(Icons + Item("a", 1450900)));
    }

    [Fact]
    public void Rejects_use_flag_without_marker_and_marker_without_use_flag()
    {
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + Item("a", 1450800, "use_flag = 1050290100")));
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + Item("a", 1450800, "marker = { template = 10600, fields = {} }")));
    }

    [Fact]
    public void Rejects_flags_outside_their_ranges()
    {
        const string marker = "marker = { template = 10600, fields = {} }\n";
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + Item("a", 1450800, marker + "use_flag = 1050290200")));
        var badRemove = Icons + Item("a", 1450800).Replace("remove_flag = 1050290200", "remove_flag = 1050290100");
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(badRemove));
    }

    [Fact]
    public void Rejects_effect_id_outside_reserved_range()
    {
        Assert.Throws<InvalidDataException>(() =>
            RaceItemsCatalog.Parse(Icons + Item("a", 1450800, "effect = { id = 1450900, template = 10600, fields = {} }")));
    }

    [Fact]
    public void Rejects_unknown_icon()
    {
        var toml = Icons + Item("a", 1450800).Replace("icon = \"shield\"", "icon = \"nope\"");
        Assert.Throws<InvalidDataException>(() => RaceItemsCatalog.Parse(toml));
    }
}
