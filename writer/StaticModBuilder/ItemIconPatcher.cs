using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using FogModWrapper.Plugins.RaceItems;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
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
    private const int ICON_SIZE = 160;
    private const byte TPF_FORMAT_BC7 = 102; // format byte used by the SB_* menu textures
    private const string TEMPLATE_TEXTURE_NAME = "MENU_DummyTransparent";
    private static readonly string[] Variants = { "hi", "low" };

    // The two Halloween icons. The per-seed injector repoints
    // EquipParamGoods.iconId to these numbers (see HalloweenIconInjector).
    private static readonly (string Name, string Png)[] HalloweenIcons =
    {
        ("MENU_ItemIcon_60383", "halloween_icon_pumpkin_seed.png"),
        ("MENU_ItemIcon_60384", "halloween_icon_profane_tear.png"),
    };

    /// <summary>
    /// The Halloween icons followed by the race item icons, one entry per
    /// distinct (icon, icon id) of the race items catalog
    /// (dataDir/plugins/race_items.toml, [icons]): texture name and source PNG
    /// (race_items_icon_&lt;icon&gt;.png) under dataDir/plugins.
    /// </summary>
    private static (string Name, string Png)[] LoadIcons(string dataDir)
    {
        var catalog = RaceItemsCatalog.Load(Path.Combine(dataDir, "plugins", "race_items.toml"));
        var raceIcons = catalog
            .Select(item => (item.Icon, item.IconId))
            .Distinct()
            .OrderBy(icon => icon.IconId)
            .Select(icon => ($"MENU_ItemIcon_{icon.IconId}", $"race_items_icon_{icon.Icon}.png"));
        return HalloweenIcons.Concat(raceIcons).ToArray();
    }

    /// <summary>
    /// For menu/{hi,low}: read 05_dummy.tpf.dcx from gameDir, add (or
    /// replace) the Halloween and race item icon textures, and write the
    /// superset into iconsDir. Returns the number of textures written across variants.
    /// </summary>
    public static int Patch(string gameDir, string iconsDir, string dataDir)
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

        // Encode each icon once to raw BC7 blocks; wrapping into a full DDS
        // (cheap header copy) happens per variant below, since each variant's
        // 05_dummy.tpf.dcx supplies its own header template.
        var blocksByIcon = new byte[icons.Length][];
        for (int i = 0; i < icons.Length; i++)
        {
            using var image = Image.Load<Rgba32>(pngPaths[i]);
            if (image.Width != ICON_SIZE || image.Height != ICON_SIZE)
            {
                Console.WriteLine(
                    $"Warning: {pngPaths[i]} is {image.Width}x{image.Height}, expected {ICON_SIZE}x{ICON_SIZE};"
                    + " skipping item icon overlay");
                return 0;
            }
            blocksByIcon[i] = EncodeToBc7Blocks(image);
        }

        int written = 0;
        foreach (var variant in Variants)
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

            byte[][] iconDds;
            try
            {
                iconDds = blocksByIcon
                    .Select(blocks => DdsAtlas.BuildStandaloneDds(template.Bytes, blocks, ICON_SIZE, ICON_SIZE))
                    .ToArray();
            }
            catch (ArgumentException e)
            {
                Console.WriteLine($"Warning: {templateContext}: {e.Message}, skipping");
                continue;
            }

            var textures = new (string Name, byte[] Dds)[icons.Length];
            for (int i = 0; i < icons.Length; i++)
            {
                textures[i] = (icons[i].Name, iconDds[i]);
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
                $"Item icon overlay: added {icons.Length} icon texture(s) across {Variants.Length} variant(s)");
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

    private static byte[] EncodeToBc7Blocks(Image<Rgba32> icon)
    {
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        // Same Wine constraint as TitleScreenPatcher.CompositeAndEncode:
        // BCnEncoder's Parallel.For encode loop crashes non-deterministically
        // under Wine's .NET runtime; serial is the only known-stable mode
        // there, and a single 160x160 icon encodes in well under a second.
        encoder.Options.IsParallel = false;
        return encoder.EncodeToRawBytes(icon)[0];
    }
}
