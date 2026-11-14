using FogModWrapper;
using FogModWrapper.Plugins.RaceItems;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;
using StaticModBuilder.Winter;
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

    /// <summary>
    /// A data dir with the real race items catalog and one opaque PNG per icon,
    /// each of its own colour (returned by file name): the Halloween PNGs at
    /// halloweenSize, the race item PNGs at raceItemWidth x raceItemHeight.
    /// </summary>
    private static (string DataDir, Dictionary<string, Rgba32> Colors) MakeDataDir(
        string root, int halloweenSize, int raceItemWidth, int raceItemHeight)
    {
        var plugins = Path.Combine(root, "data", "plugins");
        Directory.CreateDirectory(plugins);
        var toml = FindRaceItemsToml();
        File.Copy(toml, Path.Combine(plugins, "race_items.toml"));
        var names = new List<string> { "halloween_icon_pumpkin_seed.png", "halloween_icon_profane_tear.png" };
        names.AddRange(RaceItemsCatalog.Load(toml).Select(i => $"race_items_icon_{i.Icon}.png").Distinct());
        var colors = new Dictionary<string, Rgba32>();
        foreach (var (name, k) in names.Select((name, k) => (name, k)))
        {
            var color = new Rgba32((byte)(16 + 17 * k), (byte)(240 - 13 * k), (byte)(40 + 5 * k), 255);
            bool race = name.StartsWith("race_items_");
            using var img = new Image<Rgba32>(
                race ? raceItemWidth : halloweenSize, race ? raceItemHeight : halloweenSize, color);
            img.SaveAsPng(Path.Combine(plugins, name));
            colors[name] = color;
        }
        return (Path.Combine(root, "data"), colors);
    }

    /// <summary>
    /// The texture exists, decodes at size x size, and carries the source PNG's
    /// colour at its centre (alpha within BC7's loss: a block mixing the
    /// transparent margin with opaque pixels decodes at about 253).
    /// </summary>
    private static void AssertTexture(TPF tpf, string name, int size, Rgba32 color)
    {
        var texture = Assert.Single(tpf.Textures, t => t.Name == name);
        using var image = FrostEncoder.DecodeLevel(texture.Bytes, 0);
        Assert.Equal((size, size), (image.Width, image.Height));
        var pixel = image[size / 2, size / 2];
        Assert.True(
            Math.Abs(pixel.R - color.R) <= 2 && Math.Abs(pixel.G - color.G) <= 2
                && Math.Abs(pixel.B - color.B) <= 2 && pixel.A >= 250,
            $"{name}: {pixel} instead of {color}");
    }

    [Fact]
    public void Patch_writes_a_small_and_a_large_texture_per_icon()
    {
        // Small sizes keep the serial BC7 encode fast, with each variant's own
        // large size, as with the vanilla sizes (hi twice low). The Halloween
        // PNGs are at the small size (their large textures are upscales) and
        // the race item PNGs at the hi large size (their small and low large
        // textures are downscales).
        const int small = 8;
        var variants = new[] { ("hi", 32), ("low", 16) };
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var (dataDir, colors) = MakeDataDir(root, halloweenSize: small, raceItemWidth: 32, raceItemHeight: 32);
            var iconsDir = Path.Combine(root, "icons");
            Directory.CreateDirectory(iconsDir);

            int written = ItemIconPatcher.Patch(gameDir, iconsDir, dataDir, small, variants);

            // 13 icons (2 Halloween + 11 race item), 2 textures each, 2 variants.
            Assert.Equal(52, written);
            var pngById = new Dictionary<int, string>
            {
                [SpeedFogIds.HalloweenGoldenSeedIcon] = "halloween_icon_pumpkin_seed.png",
                [SpeedFogIds.HalloweenSacredTearIcon] = "halloween_icon_profane_tear.png",
            };
            foreach (var item in RaceItemsCatalog.Load(FindRaceItemsToml()))
            {
                pngById[item.IconId] = $"race_items_icon_{item.Icon}.png";
            }
            Assert.Equal(new[] { 60383, 60384 }.Concat(Enumerable.Range(60385, 11)), pngById.Keys.Order());
            foreach (var (variant, large) in variants)
            {
                var tpf = TPF.Read(Path.Combine(iconsDir, "menu", variant, "05_dummy.tpf.dcx"));
                foreach (var (id, png) in pngById)
                {
                    AssertTexture(tpf, $"MENU_ItemIcon_{id}", small, colors[png]);
                    AssertTexture(tpf, $"MENU_Knowledge_{id}", large, colors[png]);
                }
                Assert.Contains(tpf.Textures, t => t.Name == "MENU_DummyTransparent");
            }

            // A rerun replaces by name: still 26 icon textures + the template.
            ItemIconPatcher.Patch(gameDir, iconsDir, dataDir, small, variants);
            var again = TPF.Read(Path.Combine(iconsDir, "menu", "hi", "05_dummy.tpf.dcx"));
            Assert.Equal(27, again.Textures.Count);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void Large_textures_at_the_small_size_reuse_its_encode()
    {
        // As in production: the large icon is the small texture again.
        const int small = 8;
        var variants = new[] { ("hi", small), ("low", small) };
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var (dataDir, colors) = MakeDataDir(root, halloweenSize: small, raceItemWidth: 32, raceItemHeight: 32);
            var iconsDir = Path.Combine(root, "icons");
            Directory.CreateDirectory(iconsDir);

            Assert.Equal(52, ItemIconPatcher.Patch(gameDir, iconsDir, dataDir, small, variants));
            foreach (var (variant, _) in variants)
            {
                var tpf = TPF.Read(Path.Combine(iconsDir, "menu", variant, "05_dummy.tpf.dcx"));
                var id = SpeedFogIds.HalloweenGoldenSeedIcon;
                AssertTexture(tpf, $"MENU_Knowledge_{id}", small, colors["halloween_icon_pumpkin_seed.png"]);
                var smallBytes = tpf.Textures.Single(t => t.Name == $"MENU_ItemIcon_{id}").Bytes;
                var largeBytes = tpf.Textures.Single(t => t.Name == $"MENU_Knowledge_{id}").Bytes;
                Assert.Equal(smallBytes, largeBytes);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void A_race_item_icon_gets_a_margin_and_a_halloween_one_does_not()
    {
        // Downloaded art fills its canvas: a 16x32 opaque race item PNG becomes
        // a 40x40 square (its 32 px side spans 80%), so at 32x32 transparent
        // bars of 9.6 px left and right and 3.2 px top and bottom. The
        // Halloween PNGs (opaque squares) keep their edge-to-edge art.
        const int small = 32;
        var variants = new[] { ("hi", small), ("low", small) };
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var (dataDir, colors) = MakeDataDir(root, halloweenSize: small, raceItemWidth: 16, raceItemHeight: 32);
            var iconsDir = Path.Combine(root, "icons");
            Directory.CreateDirectory(iconsDir);

            Assert.Equal(52, ItemIconPatcher.Patch(gameDir, iconsDir, dataDir, small, variants));
            var tpf = TPF.Read(Path.Combine(iconsDir, "menu", "hi", "05_dummy.tpf.dcx"));
            var banana = RaceItemsCatalog.Load(FindRaceItemsToml()).First(i => i.Icon == "banana");
            var name = $"MENU_ItemIcon_{banana.IconId}";
            AssertTexture(tpf, name, small, colors["race_items_icon_banana.png"]);
            using (var image = FrostEncoder.DecodeLevel(tpf.Textures.Single(t => t.Name == name).Bytes, 0))
            {
                // Near zero, not zero: a BC7 block holding the margin's edge
                // mixes it with opaque pixels.
                Assert.True(image[1, small / 2].A < 16);
                Assert.True(image[small - 2, small / 2].A < 16);
                Assert.True(image[small / 2, 1].A < 16);
                Assert.True(image[small / 2, small - 2].A < 16);
            }
            var seed = $"MENU_ItemIcon_{SpeedFogIds.HalloweenGoldenSeedIcon}";
            using (var image = FrostEncoder.DecodeLevel(tpf.Textures.Single(t => t.Name == seed).Bytes, 0))
            {
                Assert.Equal(255, image[1, 1].A);
            }
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Fact]
    public void FitToSquare_crops_to_the_visible_pixels_before_the_margin()
    {
        // A 10x4 opaque bar inside a mostly empty 100x60 canvas: only the bar
        // counts, so the square is 13x13 (10 / 0.8, rounded up).
        using var image = new Image<Rgba32>(100, 60, new Rgba32(0, 0, 0, 0));
        for (int y = 30; y < 34; y++)
        {
            for (int x = 70; x < 80; x++)
            {
                image[x, y] = new Rgba32(200, 10, 10, 255);
            }
        }

        ItemIconPatcher.FitToSquare(image, 0.8);

        Assert.Equal((13, 13), (image.Width, image.Height));
        Assert.Equal(255, image[6, 6].A);
        Assert.Equal(0, image[0, 6].A);
        Assert.Equal(0, image[6, 0].A);

        // Nothing visible: nothing to fit.
        using var empty = new Image<Rgba32>(20, 10, new Rgba32(0, 0, 0, 0));
        ItemIconPatcher.FitToSquare(empty, 0.8);
        Assert.Equal((20, 10), (empty.Width, empty.Height));
    }

    [Theory]
    [InlineData(128, 128)] // below the 160x160 small icon
    [InlineData(100, 150)] // longer side still below it
    public void Patch_skips_the_whole_overlay_when_a_race_item_icon_has_a_bad_size(int width, int height)
    {
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var (dataDir, _) = MakeDataDir(root, halloweenSize: 160, raceItemWidth: width, raceItemHeight: height);
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

    [Fact]
    public void Patch_skips_the_whole_overlay_when_an_icon_is_not_an_image()
    {
        var root = MakeTempDir();
        try
        {
            var gameDir = MakeGameDir(root);
            var (dataDir, _) = MakeDataDir(root, halloweenSize: 160, raceItemWidth: 160, raceItemHeight: 160);
            File.WriteAllText(Path.Combine(dataDir, "plugins", "race_items_icon_banana.png"), "not a png");
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
