using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using FogModWrapper;
using FogModWrapper.Plugins.RaceItems;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SoulsFormats;

namespace StaticModBuilder;

/// <summary>
/// Ships new item icon textures (the Halloween icons and the race item icons)
/// as a superset of the vanilla menu/{hi,low}/05_dummy.tpf.dcx, so the per-seed
/// injectors (HalloweenIconInjector, see docs/plugins/halloween-icons.md, and
/// the race items plugin, see docs/plugins/race-items.md) can repoint
/// EquipParamGoods.iconId at them without shipping a bloated shared UI atlas.
/// Both icon sets live in this ONE overlay: ModEngine 2 keeps the first copy of
/// a file, so a second overlay shipping its own 05_dummy.tpf.dcx would hide the
/// other's icons.
///
/// Each icon is one PNG (a non-square one centered on a transparent square)
/// from which two textures are derived by resizing: MENU_ItemIcon_&lt;iconId&gt; (160x160, the inventory list and quick
/// slots) and MENU_Knowledge_&lt;iconId&gt; (the inventory's item detail panel;
/// vanilla keeps those in menu/{hi,low}/00_solo.tpfbhd, far too big to ship).
/// Both resolve by name through the same TPF name fallback. The large one is
/// 160x160 too and reuses the small texture's encode: at the vanilla sizes
/// (1024x1024 in hi, 512x512 in low) the serial BC7 encode under Wine added
/// about 8 minutes to every bootstrap. `Variants` holds the sizes to restore
/// for sharper large icons.
///
/// Unlike TitleScreenPatcher, these are standalone icons with no vanilla
/// pixels to composite onto. There is still no icon-sized vanilla DDS to
/// splice a header from, so each variant's own MENU_DummyTransparent entry
/// (a trivial 4x4 BC7 texture already present in 05_dummy.tpf.dcx) is reused
/// purely as the 148-byte DX10 header template for
/// DdsAtlas.BuildStandaloneDds; only the header shape is borrowed; width,
/// height and pixel data are fully replaced. Written to its own
/// --icons-dir output rather than data/mods/speedfog/, so the icons stay
/// opt-in (shipped when Halloween or race items is enabled) and separate from
/// the base mod.
/// </summary>
public static class ItemIconPatcher
{
    private const int ICON_SIZE = 160; // vanilla item icon size
    private const byte TPF_FORMAT_BC7 = 102; // format byte used by the SB_* menu textures
    private const string TEMPLATE_TEXTURE_NAME = "MENU_DummyTransparent";
    // menu/<Name> and the size of its large icons. The vanilla MENU_Knowledge_*
    // sizes are 1024 (hi) and 512 (low); a size equal to ICON_SIZE reuses the
    // small encode.
    private static readonly (string Name, int LargeIconSize)[] Variants =
        { ("hi", ICON_SIZE), ("low", ICON_SIZE) };

    // The two Halloween icons. The per-seed injector repoints
    // EquipParamGoods.iconId to these numbers (see HalloweenIconInjector).
    private static readonly (int IconId, string Png)[] HalloweenIcons =
    {
        (SpeedFogIds.HalloweenGoldenSeedIcon, "halloween_icon_pumpkin_seed.png"),
        (SpeedFogIds.HalloweenSacredTearIcon, "halloween_icon_profane_tear.png"),
    };

    /// <summary>
    /// The Halloween icons followed by the race item icons, one entry per
    /// distinct (icon, icon id) of the race items catalog
    /// (dataDir/plugins/race_items.toml, [icons]): icon id and source PNG
    /// (race_items_icon_&lt;icon&gt;.png) under dataDir/plugins.
    /// </summary>
    private static (int IconId, string Png)[] LoadIcons(string dataDir)
    {
        var catalog = RaceItemsCatalog.Load(Path.Combine(dataDir, "plugins", "race_items.toml"));
        var raceIcons = catalog
            .Select(item => (item.Icon, item.IconId))
            .Distinct()
            .OrderBy(icon => icon.IconId)
            .Select(icon => (icon.IconId, $"race_items_icon_{icon.Icon}.png"));
        return HalloweenIcons.Concat(raceIcons).ToArray();
    }

    /// <summary>
    /// For menu/{hi,low}: read 05_dummy.tpf.dcx from gameDir, add (or
    /// replace) the small and large texture of every Halloween and race item
    /// icon, and write the superset into iconsDir. Returns the number of
    /// textures written across variants.
    /// </summary>
    public static int Patch(string gameDir, string iconsDir, string dataDir)
        => Patch(gameDir, iconsDir, dataDir, ICON_SIZE, Variants);

    /// <summary>
    /// Patch with explicit texture sizes, so tests can run on small textures:
    /// a serial BC7 encode of one 1024x1024 texture takes about 20 s.
    /// </summary>
    internal static int Patch(
        string gameDir, string iconsDir, string dataDir,
        int iconSize, IReadOnlyList<(string Name, int LargeIconSize)> variants)
    {
        var catalogPath = Path.Combine(dataDir, "plugins", "race_items.toml");
        if (!File.Exists(catalogPath))
        {
            Console.WriteLine($"Warning: {catalogPath} not found in data dir, skipping item icon overlay");
            return 0;
        }
        var icons = LoadIcons(dataDir);
        var pngPaths = icons.Select(icon => Path.Combine(dataDir, "plugins", icon.Png)).ToArray();
        var missing = pngPaths.FirstOrDefault(p => !File.Exists(p));
        if (missing != null)
        {
            Console.WriteLine($"Warning: {missing} not found in data dir, skipping item icon overlay");
            return 0;
        }

        // Every PNG is checked (header only) before any slow encode: its longer
        // side at least the small icon size. Any shape is accepted (the race
        // item art is downloaded as is), centered on a transparent square
        // below. Smaller than the large size is accepted too, its large
        // texture is then an upscale.
        foreach (var path in pngPaths)
        {
            string? problem;
            try
            {
                var info = Image.Identify(path);
                problem = Math.Max(info.Width, info.Height) < iconSize
                    ? $"is {info.Width}x{info.Height}, expected at least {iconSize} pixels on its longer side"
                    : null;
            }
            catch (ImageFormatException e)
            {
                problem = $"is not a readable image ({e.Message})";
            }
            if (problem != null)
            {
                Console.WriteLine($"Warning: {path} {problem}; skipping item icon overlay");
                return 0;
            }
        }

        // Encode each texture once (each large size once, whatever the number
        // of variants using it) to raw BC7 blocks; wrapping into a full DDS
        // (cheap header copy) happens per variant below, since each variant's
        // 05_dummy.tpf.dcx supplies its own header template.
        var largeSizes = variants.Select(v => v.LargeIconSize).Distinct().ToArray();
        int maxLargeSize = largeSizes.Max();
        var smallBlocks = new byte[icons.Length][];
        var largeBlocks = largeSizes.ToDictionary(size => size, _ => new byte[icons.Length][]);
        for (int i = 0; i < icons.Length; i++)
        {
            using var image = Image.Load<Rgba32>(pngPaths[i]);
            if (image.Width != image.Height)
            {
                int side = Math.Max(image.Width, image.Height);
                image.Mutate(x => x.Pad(side, side, Color.Transparent));
            }
            if (image.Width < maxLargeSize)
            {
                Console.WriteLine(
                    $"Note: {pngPaths[i]} is {image.Width}x{image.Height},"
                    + $" its {maxLargeSize}x{maxLargeSize} large icon is an upscale");
            }
            smallBlocks[i] = EncodeToBc7Blocks(image, iconSize);
            foreach (var size in largeSizes)
            {
                largeBlocks[size][i] = size == iconSize ? smallBlocks[i] : EncodeToBc7Blocks(image, size);
            }
        }

        int written = 0;
        foreach (var (variant, largeIconSize) in variants)
        {
            var dummySubPath = Path.Combine("menu", variant, "05_dummy.tpf.dcx");
            var dummyPath = Path.Combine(gameDir, dummySubPath);
            if (!File.Exists(dummyPath))
            {
                Console.WriteLine($"Warning: {dummySubPath} not found in game dir, skipping");
                continue;
            }

            var tpf = TPF.Read(dummyPath);
            var template = tpf.Textures.Find(t => t.Name == TEMPLATE_TEXTURE_NAME);
            if (template == null)
            {
                Console.WriteLine($"Warning: {TEMPLATE_TEXTURE_NAME} not found in {dummySubPath}, skipping");
                continue;
            }

            var templateContext = $"{TEMPLATE_TEXTURE_NAME} in {dummySubPath}";
            if (!DdsAtlas.TryValidateBc7Header(template.Bytes, out _, out var reason))
            {
                Console.WriteLine($"Warning: {templateContext}{reason}");
                continue;
            }

            var textures = new List<(string Name, byte[] Dds)>(icons.Length * 2);
            try
            {
                for (int i = 0; i < icons.Length; i++)
                {
                    textures.Add((
                        $"MENU_ItemIcon_{icons[i].IconId}",
                        DdsAtlas.BuildStandaloneDds(template.Bytes, smallBlocks[i], iconSize, iconSize)));
                    textures.Add((
                        $"MENU_Knowledge_{icons[i].IconId}",
                        DdsAtlas.BuildStandaloneDds(
                            template.Bytes, largeBlocks[largeIconSize][i], largeIconSize, largeIconSize)));
                }
            }
            catch (ArgumentException e)
            {
                Console.WriteLine($"Warning: {templateContext}: {e.Message}, skipping");
                continue;
            }
            int added = AddTexturesToTpf(tpf, textures);

            var dest = Path.Combine(iconsDir, dummySubPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            tpf.Write(dest);
            written += added;
        }

        if (written > 0)
        {
            Console.WriteLine(
                $"Item icon overlay: added {icons.Length} icon(s), small and large,"
                + $" across {variants.Count} variant(s)");
        }
        return written;
    }

    /// <summary>
    /// Add each texture to the TPF, replacing an existing entry of the same
    /// name in place (so rerunning the builder is idempotent). Returns the
    /// number of textures processed.
    /// </summary>
    internal static int AddTexturesToTpf(TPF tpf, IReadOnlyList<(string Name, byte[] Dds)> textures)
    {
        int count = 0;
        foreach (var (name, dds) in textures)
        {
            var existing = tpf.Textures.Find(t => t.Name == name);
            if (existing != null)
            {
                existing.Bytes = dds;
            }
            else
            {
                // Not the (name, format, flags1, bytes, platform) constructor:
                // it eagerly parses bytes as a DDS, which TPF.Write's own
                // WriteHeader re-derives Type/Mipmaps from anyway at write
                // time (see TPF.Texture.WriteHeader), so building via the
                // empty constructor and setting fields directly is equivalent
                // for real DDS bytes and keeps this path usable in unit tests
                // that exercise it with non-DDS placeholder bytes.
                tpf.Textures.Add(new TPF.Texture(TPF.TPFPlatform.PC)
                {
                    Name = name,
                    Format = TPF_FORMAT_BC7,
                    Flags1 = 0,
                    Bytes = dds,
                });
            }
            count++;
        }
        return count;
    }

    /// <summary>The image resized to size x size (unless it already is), as raw BC7 blocks.</summary>
    private static byte[] EncodeToBc7Blocks(Image<Rgba32> source, int size)
    {
        using var resized = source.Width == size ? null : source.Clone(x => x.Resize(size, size));
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        // Same Wine constraint as TitleScreenPatcher.CompositeAndEncode:
        // BCnEncoder's Parallel.For encode loop crashes non-deterministically
        // under Wine's .NET runtime; serial is the only known-stable mode
        // there. A 160x160 texture encodes in well under a second, a
        // 1024x1024 one in about 20 s (Fast quality is barely faster for BC7).
        encoder.Options.IsParallel = false;
        return encoder.EncodeToRawBytes(resized ?? source)[0];
    }
}
