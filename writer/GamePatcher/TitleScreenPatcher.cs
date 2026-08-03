using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SoulsFormats;

namespace GamePatcher;

/// <summary>
/// Overlays the SpeedFog badge (data/title_screen_overlay.png, transparent
/// RGBA) onto the vanilla title screen artwork.
///
/// The title screen image is the MENU_Title_EldenRing_01 sprite inside the
/// SB_Title_01 atlas of menu/{hi,low}/01_common.tpf.dcx. Its rect, from
/// SB_Title_01.layout in 01_common.sblytbnd.dcx, is 4px-block-aligned, so the
/// sprite's BC7 blocks are extracted, decoded, alpha-composited with the
/// overlay, re-encoded, and spliced back without touching the other sprites
/// sharing the atlas (HUD bars, inventory icons). The vanilla art stays the
/// base, so the sprite keeps blending into the black title screen.
/// </summary>
public static class TitleScreenPatcher
{
    private const string OVERLAY_NAME = "title_screen_overlay.png";
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
    /// Read menu/{hi,low}/01_common.tpf.dcx from gameDir, composite the overlay
    /// onto SB_Title_01's title sprite, write the patched TPFs to outputDir.
    /// Returns the number of TPFs patched, or 0 if the overlay or textures were
    /// not found.
    /// </summary>
    public static int Patch(string gameDir, string outputDir, string dataDir)
    {
        var overlayPath = Path.Combine(dataDir, OVERLAY_NAME);
        if (!File.Exists(overlayPath))
        {
            Console.WriteLine($"Warning: {OVERLAY_NAME} not found in data dir, skipping title screen patch");
            return 0;
        }

        using var overlay = Image.Load<Rgba32>(overlayPath);
        if (overlay.Width != SPRITE_W || overlay.Height != SPRITE_H)
        {
            Console.WriteLine(
                $"Warning: {OVERLAY_NAME} is {overlay.Width}x{overlay.Height}, expected {SPRITE_W}x{SPRITE_H}; skipping title screen patch");
            return 0;
        }

        // hi and low ship identical SB_Title_01 textures, so cache the encoded
        // result keyed on the extracted vanilla blocks
        byte[]? cachedVanilla = null;
        byte[]? cachedEncoded = null;

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

            var vanillaBlocks = DdsAtlas.ExtractBlocks(tex.Bytes, info, SPRITE_X, SPRITE_Y, SPRITE_W, SPRITE_H);
            byte[] encoded;
            if (cachedVanilla != null && vanillaBlocks.AsSpan().SequenceEqual(cachedVanilla))
            {
                encoded = cachedEncoded!;
            }
            else
            {
                encoded = CompositeAndEncode(vanillaBlocks, overlay);
                cachedVanilla = vanillaBlocks;
                cachedEncoded = encoded;
            }

            DdsAtlas.SpliceBlocks(tex.Bytes, info, encoded, SPRITE_X, SPRITE_Y, SPRITE_W, SPRITE_H);

            var destPath = Path.Combine(outputDir, subPath);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            tpf.Write(destPath);
            patched++;
        }

        if (patched > 0)
        {
            Console.WriteLine($"Title screen patch: composited {OVERLAY_NAME} into {patched} menu TPF(s)");
        }
        return patched;
    }

    private static byte[] CompositeAndEncode(byte[] vanillaBlocks, Image<Rgba32> overlay)
    {
        var decoder = new BcDecoder();
        // same Wine constraint as the encoder below; decode is sub-second anyway
        decoder.Options.IsParallel = false;
        using var sprite = decoder.DecodeRawToImageRgba32(vanillaBlocks, SPRITE_W, SPRITE_H, CompressionFormat.Bc7);
        sprite.Mutate(ctx => ctx.DrawImage(overlay, 1f));

        var encoder = new BcEncoder(CompressionFormat.Bc7);
        encoder.OutputOptions.GenerateMipMaps = false;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        // BCnEncoder's Parallel.For encode loop crashes non-deterministically
        // (access violations at varying sites, ~50% of runs, both quality
        // modes) under Wine's .NET runtime; the same workload is stable on
        // native Linux and stable under Wine when single-threaded. Serial
        // Balanced encodes this sprite in ~90s, acceptable for a one-shot
        // setup step, and re-encoding BC7-decoded content gains nothing
        // visible from BestQuality (which takes ~4min serial).
        encoder.Options.IsParallel = false;
        // mip 0 is the only level generated
        return encoder.EncodeToRawBytes(sprite)[0];
    }
}
