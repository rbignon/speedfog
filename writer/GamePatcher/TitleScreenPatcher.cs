using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SoulsFormats;

namespace GamePatcher;

/// <summary>
/// Replaces the title screen artwork (ELDEN RING logo + ring art) with the
/// SpeedFog artwork from data/title_screen.png.
///
/// The title screen image is the MENU_Title_EldenRing_01 sprite inside the
/// SB_Title_01 atlas of menu/{hi,low}/01_common.tpf.dcx. Its rect, from
/// SB_Title_01.layout in 01_common.sblytbnd.dcx, is 4px-block-aligned, so the
/// artwork is BC7-encoded and spliced into the vanilla atlas without decoding
/// or re-encoding the other sprites sharing it (HUD bars, inventory icons).
/// </summary>
public static class TitleScreenPatcher
{
    private const string ARTWORK_NAME = "title_screen.png";
    private const string TEXTURE_NAME = "SB_Title_01";
    private static readonly string[] Variants = { "hi", "low" };

    // MENU_Title_EldenRing_01 sprite rect within the SB_Title_01 atlas.
    private const int SPRITE_X = 0;
    private const int SPRITE_Y = 60;
    private const int SPRITE_W = 2532;
    private const int SPRITE_H = 1532;

    private const int ATLAS_WIDTH = 4096;
    private const int ATLAS_HEIGHT = 2048;
    private const int DXGI_BC7_UNORM = 98;

    /// <summary>
    /// Read menu/{hi,low}/01_common.tpf.dcx from gameDir, splice the artwork
    /// into SB_Title_01, write the patched TPFs to outputDir. Returns the
    /// number of TPFs patched, or 0 if the artwork or textures were not found.
    /// </summary>
    public static int Patch(string gameDir, string outputDir, string dataDir)
    {
        var artworkPath = Path.Combine(dataDir, ARTWORK_NAME);
        if (!File.Exists(artworkPath))
        {
            Console.WriteLine($"Warning: {ARTWORK_NAME} not found in data dir, skipping title screen patch");
            return 0;
        }

        byte[] regionBlocks;
        using (var image = Image.Load<Rgba32>(artworkPath))
        {
            if (image.Width != SPRITE_W || image.Height != SPRITE_H)
            {
                Console.WriteLine(
                    $"Warning: {ARTWORK_NAME} is {image.Width}x{image.Height}, expected {SPRITE_W}x{SPRITE_H}; skipping title screen patch");
                return 0;
            }
            regionBlocks = EncodeBc7(image);
        }

        int patched = 0;
        foreach (var variant in Variants)
        {
            var subPath = Path.Combine("menu", variant, "01_common.tpf.dcx");
            var srcPath = Path.Combine(gameDir, subPath);
            if (!File.Exists(srcPath))
            {
                Console.WriteLine($"Warning: {subPath} not found in game dir, skipping");
                continue;
            }

            var tpf = TPF.Read(srcPath);
            var tex = tpf.Textures.Find(
                t => Path.GetFileNameWithoutExtension(t.Name).Equals(TEXTURE_NAME, StringComparison.OrdinalIgnoreCase));
            if (tex == null)
            {
                Console.WriteLine($"Warning: {TEXTURE_NAME} not found in {subPath}, skipping");
                continue;
            }

            DdsInfo info;
            try
            {
                info = DdsAtlas.ParseHeader(tex.Bytes);
            }
            catch (InvalidDataException e)
            {
                Console.WriteLine($"Warning: {TEXTURE_NAME} in {subPath}: {e.Message}, skipping");
                continue;
            }

            if (info.Width != ATLAS_WIDTH || info.Height != ATLAS_HEIGHT
                || info.DxgiFormat != DXGI_BC7_UNORM || info.MipCount > 1)
            {
                Console.WriteLine(
                    $"Warning: {TEXTURE_NAME} in {subPath} is {info.Width}x{info.Height}"
                    + $" dxgi={info.DxgiFormat} mips={info.MipCount},"
                    + $" expected {ATLAS_WIDTH}x{ATLAS_HEIGHT} dxgi={DXGI_BC7_UNORM} mips<=1; skipping");
                continue;
            }

            DdsAtlas.SpliceBlocks(tex.Bytes, info, regionBlocks, SPRITE_X, SPRITE_Y, SPRITE_W, SPRITE_H);

            var destPath = Path.Combine(outputDir, subPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            tpf.Write(destPath);
            patched++;
        }

        if (patched > 0)
        {
            Console.WriteLine($"Title screen patch: spliced {ARTWORK_NAME} into {patched} menu TPF(s)");
        }
        return patched;
    }

    private static byte[] EncodeBc7(Image<Rgba32> image)
    {
        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        // mip 0 is the only level generated
        return encoder.EncodeToRawBytes(image)[0];
    }
}
