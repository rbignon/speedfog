using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;

namespace StaticModBuilder;

/// <summary>
/// Ships two new item icon textures (Halloween care package art) as a
/// superset of the vanilla menu/{hi,low}/05_dummy.tpf.dcx, so the per-seed
/// injector (HalloweenIconInjector, see Task 3) can repoint
/// EquipParamGoods.iconId at them without shipping a bloated shared UI atlas.
///
/// Unlike TitleScreenPatcher, these are standalone icons with no vanilla
/// pixels to composite onto. There is still no icon-sized vanilla DDS to
/// splice a header from, so each variant's own MENU_DummyTransparent entry
/// (a trivial 4x4 BC7 texture already present in 05_dummy.tpf.dcx) is reused
/// purely as the 148-byte DX10 header template for
/// DdsAtlas.BuildStandaloneDds; only the header shape is borrowed; width,
/// height and pixel data are fully replaced. Written to its own
/// --halloween-dir output rather than data/mods/speedfog/, so the Halloween
/// plugin's assets stay opt-in and separate from the base mod.
/// </summary>
public static class HalloweenIconPatcher
{
    private const int ICON_SIZE = 160;
    private const byte TPF_FORMAT_BC7 = 102; // format byte used by the SB_* menu textures
    private const string TEMPLATE_TEXTURE_NAME = "MENU_DummyTransparent";
    private static readonly string[] Variants = { "hi", "low" };

    // (icon name, source PNG under dataDir). The per-seed injector repoints
    // EquipParamGoods.iconId to these numbers (see HalloweenIconInjector).
    private static readonly (string Name, string Png)[] Icons =
    {
        ("MENU_ItemIcon_60383", "halloween_icon_pumpkin_seed.png"),
        ("MENU_ItemIcon_63075", "halloween_icon_gummy_worm.png"),
    };

    /// <summary>
    /// For menu/{hi,low}: read 05_dummy.tpf.dcx from gameDir, add (or
    /// replace) the Halloween icon textures, and write the superset into
    /// halloweenDir. Returns the number of textures written across variants.
    /// </summary>
    public static int Patch(string gameDir, string halloweenDir, string dataDir)
    {
        var pngPaths = Icons.Select(icon => Path.Combine(dataDir, icon.Png)).ToArray();
        var missing = pngPaths.FirstOrDefault(p => !File.Exists(p));
        if (missing != null)
        {
            Console.WriteLine($"Warning: {missing} not found in data dir, skipping Halloween icon overlay");
            return 0;
        }

        // Encode each icon once to raw BC7 blocks; wrapping into a full DDS
        // (cheap header copy) happens per variant below, since each variant's
        // 05_dummy.tpf.dcx supplies its own header template.
        var blocksByIcon = new byte[Icons.Length][];
        for (int i = 0; i < Icons.Length; i++)
        {
            using var image = Image.Load<Rgba32>(pngPaths[i]);
            if (image.Width != ICON_SIZE || image.Height != ICON_SIZE)
            {
                Console.WriteLine(
                    $"Warning: {pngPaths[i]} is {image.Width}x{image.Height}, expected {ICON_SIZE}x{ICON_SIZE};"
                    + " skipping Halloween icon overlay");
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

            var textures = new (string Name, byte[] Dds)[Icons.Length];
            for (int i = 0; i < Icons.Length; i++)
            {
                textures[i] = (
                    Icons[i].Name,
                    DdsAtlas.BuildStandaloneDds(template.Bytes, blocksByIcon[i], ICON_SIZE, ICON_SIZE));
            }
            int added = AddTexturesToTpf(tpf, textures);

            var dest = Path.Combine(halloweenDir, dummySubPath);
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            tpf.Write(dest);
            written += added;
        }

        if (written > 0)
        {
            Console.WriteLine(
                $"Halloween icon overlay: added {Icons.Length} icon texture(s) across {Variants.Length} variant(s)");
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
