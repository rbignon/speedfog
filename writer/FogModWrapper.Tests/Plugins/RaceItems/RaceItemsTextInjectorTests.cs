using FogModWrapper.Plugins.RaceItems;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests.Plugins.RaceItems;

public class RaceItemsTextInjectorTests : IDisposable
{
    private const string BndName = "item_dlc02.msgbnd.dcx";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "speedfog-raceitems-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_root))
            Directory.Delete(_root, true);
    }

    private static BinderFile FmgFile(int id, string name, params (int Id, string Text)[] entries)
    {
        var fmg = new FMG();
        foreach (var (eid, text) in entries)
            fmg.Entries.Add(new FMG.Entry(eid, text));
        return new BinderFile(Binder.FileFlags.Flag1, id, name, fmg.Write());
    }

    private string WriteVanillaBnd(string gameDir, string lang)
    {
        var bnd = new BND4();
        int id = 0;
        foreach (var name in new[] {
            "GoodsName.fmg", "GoodsName_dlc01.fmg", "GoodsName_dlc02.fmg",
            "GoodsInfo.fmg", "GoodsInfo_dlc01.fmg", "GoodsCaption.fmg", "GoodsCaption_dlc01.fmg" })
            bnd.Files.Add(FmgFile(id++, $"N:\\GR\\data\\Msg\\{name}", (1, "vanilla")));
        var dir = Path.Combine(gameDir, "msg", lang);
        Directory.CreateDirectory(dir);
        bnd.Write(Path.Combine(dir, BndName));
        return dir;
    }

    private static RaceItemGoods Goods(int goodsId, string name, string info, string caption) =>
        new("race_item", "item_box", 60396, name, info, caption, goodsId, 1050290101, 1050290201, null);

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

    private static FMG ReadFmg(string path, string fileName)
    {
        var bnd = BND4.Read(path);
        return FMG.Read(bnd.Files.Single(f => Path.GetFileName(f.Name.Replace('\\', '/')) == fileName).Bytes);
    }

    [Fact]
    public void Writes_name_info_caption_in_base_fmgs()
    {
        var gameDir = Path.Combine(_root, "game");
        var modDir = Path.Combine(_root, "mod");
        WriteVanillaBnd(gameDir, "engus");
        WriteVanillaBnd(gameDir, "frafr");

        RaceItemsTextInjector.Apply(modDir, gameDir, new[] { Goods(1450801, "Race Item", "Held.", "A box.") });

        foreach (var lang in new[] { "engus", "frafr" })
        {
            var path = Path.Combine(modDir, "msg", lang, BndName);
            Assert.Equal("Race Item", ReadFmg(path, "GoodsName.fmg").Entries.Single(e => e.ID == 1450801).Text);
            Assert.Equal("Held.", ReadFmg(path, "GoodsInfo.fmg").Entries.Single(e => e.ID == 1450801).Text);
            Assert.Equal("A box.", ReadFmg(path, "GoodsCaption.fmg").Entries.Single(e => e.ID == 1450801).Text);
            foreach (var other in new[] { "GoodsName_dlc01.fmg", "GoodsName_dlc02.fmg", "GoodsInfo_dlc01.fmg", "GoodsCaption_dlc01.fmg" })
                Assert.DoesNotContain(ReadFmg(path, other).Entries, e => e.ID == 1450801);
        }
    }

    [Fact]
    public void Writes_one_entry_per_goods_of_the_shipped_catalog()
    {
        var gameDir = Path.Combine(_root, "game");
        var modDir = Path.Combine(_root, "mod");
        WriteVanillaBnd(gameDir, "engus");

        RaceItemsTextInjector.Apply(modDir, gameDir, RaceItemsCatalog.Load(ShippedCatalogPath()).Goods);

        // The items have no text of their own: only the two goods get entries.
        var path = Path.Combine(modDir, "msg", "engus", BndName);
        foreach (var fmgName in new[] { "GoodsName.fmg", "GoodsInfo.fmg", "GoodsCaption.fmg" })
            Assert.Equal(new[] { 1, 1450800, 1450801 }, ReadFmg(path, fmgName).Entries.Select(e => e.ID).Order());
        var names = ReadFmg(path, "GoodsName.fmg").Entries;
        Assert.Equal("Shield", names.Single(e => e.ID == 1450800).Text);
        Assert.Equal("Race Item", names.Single(e => e.ID == 1450801).Text);
    }

    [Fact]
    public void Replaces_an_existing_entry()
    {
        var gameDir = Path.Combine(_root, "game");
        var modDir = Path.Combine(_root, "mod");
        WriteVanillaBnd(gameDir, "engus");
        var goods = new[] { Goods(1450801, "Race Item", "a", "b") };

        RaceItemsTextInjector.Apply(modDir, gameDir, goods);
        RaceItemsTextInjector.Apply(modDir, gameDir, new[] { Goods(1450801, "Race Item 2", "a", "b") });

        var fmg = ReadFmg(Path.Combine(modDir, "msg", "engus", BndName), "GoodsName.fmg");
        Assert.Equal("Race Item 2", fmg.Entries.Single(e => e.ID == 1450801).Text);
    }
}
