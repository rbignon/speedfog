using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;
using Xunit;

namespace StaticModBuilder.Tests;

public class ItemIconPatcherTests
{
    /// <summary>
    /// Minimal valid DX10 BC7 DDS: 148-byte header (magic, dwSize=124,
    /// ddspf.dwFourCC="DX10", header10.dxgiFormat=BC7_UNORM=98) plus one 4x4
    /// block. TPF.Texture's (name, format, flags1, bytes, platform)
    /// constructor eagerly parses `bytes` as a real DDS in SoulsFormatsNEXT,
    /// unlike the brief's original fixture of a bare byte[16], so the fixture
    /// needs a real (if minimal) DDS instead.
    /// </summary>
    private static byte[] MakeMinimalDds(int dxgiFormat = 98, int mipCount = 1)
    {
        var dds = new byte[148 + 16];
        "DDS "u8.ToArray().CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);   // dwSize
        BitConverter.GetBytes(4).CopyTo(dds, 12);    // dwHeight
        BitConverter.GetBytes(4).CopyTo(dds, 16);    // dwWidth
        BitConverter.GetBytes(mipCount).CopyTo(dds, 28); // dwMipMapCount
        BitConverter.GetBytes(32).CopyTo(dds, 76);   // ddspf dwSize
        System.Text.Encoding.ASCII.GetBytes("DX10").CopyTo(dds, 84); // ddspf dwFourCC
        BitConverter.GetBytes(dxgiFormat).CopyTo(dds, 128); // header10 dxgiFormat
        BitConverter.GetBytes(3).CopyTo(dds, 132);   // header10 resourceDimension = TEXTURE2D
        BitConverter.GetBytes(1).CopyTo(dds, 140);   // header10 arraySize
        return dds;
    }

    private static TPF MakeDummyTpf()
    {
        var tpf = new TPF();
        tpf.Textures.Add(new TPF.Texture("MENU_DummyFace_01", 102, 0, MakeMinimalDds(), TPF.TPFPlatform.PC));
        return tpf;
    }

    [Fact]
    public void AddTexturesToTpf_AppendsNewTextures()
    {
        var tpf = MakeDummyTpf();
        int added = ItemIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 1, 2, 3 }),
            ("MENU_ItemIcon_99999", new byte[] { 4, 5, 6 }),
        });
        Assert.Equal(2, added);
        Assert.Equal(3, tpf.Textures.Count);
        Assert.Contains(tpf.Textures, t => t.Name == "MENU_ItemIcon_60383");
        Assert.Contains(tpf.Textures, t => t.Name == "MENU_DummyFace_01");
    }

    [Fact]
    public void AddTexturesToTpf_ReplacesExistingByName()
    {
        var tpf = MakeDummyTpf();
        ItemIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 1 }),
        });
        int addedAgain = ItemIconPatcher.AddTexturesToTpf(tpf, new[]
        {
            ("MENU_ItemIcon_60383", new byte[] { 9, 9 }),
        });
        Assert.Equal(1, addedAgain);
        var tex = tpf.Textures.Single(t => t.Name == "MENU_ItemIcon_60383");
        Assert.Equal(new byte[] { 9, 9 }, tex.Bytes);
        Assert.Equal(2, tpf.Textures.Count);
    }

    private static string FindRaceItemsToml()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "plugins", "race_items.toml");
            if (File.Exists(candidate)) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("data/plugins/race_items.toml not found above the test binaries");
    }

    private static string MakeTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "itemiconpatcher-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    /// <summary>A game dir whose menu/{hi,low}/05_dummy.tpf.dcx holds the template texture.</summary>
    private static string MakeGameDir(string root)
    {
        var gameDir = Path.Combine(root, "game");
        foreach (var variant in new[] { "hi", "low" })
        {
            var tpf = new TPF();
            tpf.Textures.Add(new TPF.Texture("MENU_DummyTransparent", 102, 0, MakeMinimalDds(), TPF.TPFPlatform.PC));
            var path = Path.Combine(gameDir, "menu", variant, "05_dummy.tpf.dcx");
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            tpf.Write(path);
        }
        return gameDir;
    }

    /// <summary>A data dir with the real race items catalog and a 160x160 PNG per icon.</summary>
    private static string MakeDataDir(string root, int raceItemSize = 160)
    {
        var plugins = Path.Combine(root, "data", "plugins");
        Directory.CreateDirectory(plugins);
        var toml = FindRaceItemsToml();
        File.Copy(toml, Path.Combine(plugins, "race_items.toml"));
        var names = new List<string> { "halloween_icon_pumpkin_seed.png", "halloween_icon_profane_tear.png" };
        names.AddRange(FogModWrapper.Plugins.RaceItems.RaceItemsCatalog.Load(toml)
            .Select(i => $"race_items_icon_{i.Icon}.png").Distinct());
        foreach (var name in names)
        {
            int size = name.StartsWith("race_items_") ? raceItemSize : 160;
            using var img = new Image<Rgba32>(size, size);
            img.SaveAsPng(Path.Combine(plugins, name));
        }
        return Path.Combine(root, "data");
    }

    [Fact]
    public void Patch_writes_halloween_and_race_item_icons()
    {
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var dataDir = MakeDataDir(root);
            var iconsDir = Path.Combine(root, "icons");
            Directory.CreateDirectory(iconsDir);

            int written = ItemIconPatcher.Patch(gameDir, iconsDir, dataDir);

            Assert.Equal(26, written);
            var expected = new[] { 60383, 60384 }.Concat(Enumerable.Range(60385, 11))
                .Select(id => $"MENU_ItemIcon_{id}").ToArray();
            foreach (var variant in new[] { "hi", "low" })
            {
                var tpf = TPF.Read(Path.Combine(iconsDir, "menu", variant, "05_dummy.tpf.dcx"));
                foreach (var name in expected)
                {
                    Assert.Contains(tpf.Textures, t => t.Name == name);
                }
                Assert.Contains(tpf.Textures, t => t.Name == "MENU_DummyTransparent");
            }

            // A rerun replaces by name: still 13 icons + the template.
            ItemIconPatcher.Patch(gameDir, iconsDir, dataDir);
            var again = TPF.Read(Path.Combine(iconsDir, "menu", "hi", "05_dummy.tpf.dcx"));
            Assert.Equal(14, again.Textures.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Patch_skips_the_whole_overlay_when_a_race_item_icon_has_the_wrong_size()
    {
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var dataDir = MakeDataDir(root, raceItemSize: 128);
            var iconsDir = Path.Combine(root, "icons");
            Directory.CreateDirectory(iconsDir);

            Assert.Equal(0, ItemIconPatcher.Patch(gameDir, iconsDir, dataDir));
            Assert.False(File.Exists(Path.Combine(iconsDir, "menu", "hi", "05_dummy.tpf.dcx")));
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
