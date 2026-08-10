using System.Text;
using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using SoulsFormats;

namespace StaticModBuilder;

/// <summary>
/// Overlays the SpeedFog badge (data/title_screen_overlay.png, transparent
/// RGBA) onto the title screen artwork, without shipping the 65 MB shared UI
/// atlas that contains it.
///
/// The title GFX references the artwork by name (GFxDefineExternalImage2
/// "MENU_Title_EldenRing_01"), resolved at runtime first through the atlas
/// layouts of 01_common.sblytbnd.dcx, then as a standalone TPF texture (how
/// boot logos and MENU_Dummy* fallbacks resolve in vanilla). So the patch
/// redirects the lookup: it removes the sprite's SubTexture entry from
/// SB_Title_01.layout and adds a standalone texture with that name (vanilla
/// sprite pixels + badge) to 02_title.tpf.dcx, the title screen's own
/// resource block. Shipped output is ~1.5 MB instead of ~112 MB. Validated
/// in-game on 2026-08-03.
/// </summary>
public static class TitleScreenPatcher
{
    private const string OVERLAY_NAME = "title_screen_overlay.png";
    private const string SPRITE_NAME = "MENU_Title_EldenRing_01";
    private const string ATLAS_NAME = "SB_Title_01";
    private const int DXGI_BC7_UNORM = 98;
    private const byte TPF_FORMAT_BC7 = 102; // format byte used by the SB_* menu textures
    private static readonly string[] Variants = { "hi", "low" };

    /// <summary>
    /// For menu/{hi,low}: rewrite 01_common.sblytbnd.dcx (sprite entry
    /// removed) and 02_title.tpf.dcx (standalone composited texture added)
    /// into outputDir. The 01_common.tpf.dcx atlas is only read. Returns the
    /// number of variants patched.
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

        // hi and low pack their atlases differently but hold the same sprite
        // pixels, so the encoded standalone texture is cached on pixel content
        byte[]? cachedPixels = null;
        byte[]? cachedDds = null;

        int patched = 0;
        foreach (var variant in Variants)
        {
            var layoutSubPath = Path.Combine("menu", variant, "01_common.sblytbnd.dcx");
            var atlasSubPath = Path.Combine("menu", variant, "01_common.tpf.dcx");
            var titleSubPath = Path.Combine("menu", variant, "02_title.tpf.dcx");
            var missing = new[] { layoutSubPath, atlasSubPath, titleSubPath }
                .FirstOrDefault(p => !File.Exists(Path.Combine(gameDir, p)));
            if (missing != null)
            {
                Console.WriteLine($"Warning: {missing} not found in game dir, skipping");
                continue;
            }

            // locate the sprite in the variant's layout
            var layoutBnd = BND4.Read(Path.Combine(gameDir, layoutSubPath));
            var layoutFile = layoutBnd.Files.Find(
                f => Path.GetFileNameWithoutExtension(f.Name).Equals(ATLAS_NAME, StringComparison.OrdinalIgnoreCase));
            if (layoutFile == null)
            {
                Console.WriteLine($"Warning: {ATLAS_NAME}.layout not found in {layoutSubPath}, skipping");
                continue;
            }
            var layoutXml = Encoding.UTF8.GetString(layoutFile.Bytes);

            (int X, int Y, int Width, int Height) rect;
            try
            {
                rect = LayoutFile.FindSubTexture(layoutXml, SPRITE_NAME);
            }
            catch (InvalidDataException e)
            {
                Console.WriteLine($"Warning: {layoutSubPath}: {e.Message}, skipping");
                continue;
            }
            if (overlay.Width != rect.Width || overlay.Height != rect.Height)
            {
                Console.WriteLine(
                    $"Warning: {OVERLAY_NAME} is {overlay.Width}x{overlay.Height},"
                    + $" expected {rect.Width}x{rect.Height} per {layoutSubPath}; skipping");
                continue;
            }

            // pull the vanilla sprite blocks out of the atlas (read-only)
            var atlasTpf = TPF.Read(Path.Combine(gameDir, atlasSubPath));
            var atlasTex = atlasTpf.Textures.Find(
                t => Path.GetFileNameWithoutExtension(t.Name).Equals(ATLAS_NAME, StringComparison.OrdinalIgnoreCase));
            if (atlasTex == null)
            {
                Console.WriteLine($"Warning: {ATLAS_NAME} not found in {atlasSubPath}, skipping");
                continue;
            }

            byte[] spriteBlocks;
            byte[] spriteDds;
            try
            {
                var info = DdsAtlas.ParseHeader(atlasTex.Bytes);
                if (info.DxgiFormat != DXGI_BC7_UNORM || info.MipCount > 1)
                {
                    Console.WriteLine(
                        $"Warning: {ATLAS_NAME} in {atlasSubPath} is dxgi={info.DxgiFormat} mips={info.MipCount},"
                        + $" expected dxgi={DXGI_BC7_UNORM} mips<=1; skipping");
                    continue;
                }
                spriteBlocks = DdsAtlas.ExtractBlocks(atlasTex.Bytes, info, rect.X, rect.Y, rect.Width, rect.Height);
                spriteDds = DdsAtlas.BuildStandaloneDds(atlasTex.Bytes, spriteBlocks, rect.Width, rect.Height);
            }
            catch (InvalidDataException e)
            {
                Console.WriteLine($"Warning: {ATLAS_NAME} in {atlasSubPath}: {e.Message}, skipping");
                continue;
            }
            catch (ArgumentException e)
            {
                Console.WriteLine($"Warning: {ATLAS_NAME} in {atlasSubPath}: {e.Message}, skipping");
                continue;
            }

            var composited = CompositeAndEncode(spriteDds, spriteBlocks, overlay, ref cachedPixels, ref cachedDds);

            // build both artifacts before writing either, so a failure never
            // leaves a layout-without-texture partial output
            layoutFile.Bytes = Encoding.UTF8.GetBytes(LayoutFile.RemoveSubTexture(layoutXml, SPRITE_NAME));
            var titleTpf = TPF.Read(Path.Combine(gameDir, titleSubPath));
            var existing = titleTpf.Textures.Find(
                t => Path.GetFileNameWithoutExtension(t.Name).Equals(SPRITE_NAME, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.Bytes = composited;
            }
            else
            {
                titleTpf.Textures.Add(
                    new TPF.Texture(SPRITE_NAME, TPF_FORMAT_BC7, 0, composited, TPF.TPFPlatform.PC));
            }

            var layoutDest = Path.Combine(outputDir, layoutSubPath);
            Directory.CreateDirectory(Path.GetDirectoryName(layoutDest)!);
            layoutBnd.Write(layoutDest);
            titleTpf.Write(Path.Combine(outputDir, titleSubPath));

            // drop the full-atlas override left behind by pre-redirect
            // SpeedFog versions: it would silently re-bloat every seed by
            // ~110 MB and re-ship that era's broken low-variant splice
            var staleAtlas = Path.Combine(outputDir, atlasSubPath);
            if (File.Exists(staleAtlas))
            {
                File.Delete(staleAtlas);
                Console.WriteLine($"Removed stale {atlasSubPath} override from a previous SpeedFog version");
            }
            patched++;
        }

        if (patched > 0)
        {
            Console.WriteLine($"Title screen patch: redirected {SPRITE_NAME} to 02_title with {OVERLAY_NAME} composited ({patched} variant(s))");
        }
        return patched;
    }

    private static byte[] CompositeAndEncode(
        byte[] spriteDds, byte[] spriteBlocks, Image<Rgba32> overlay,
        ref byte[]? cachedPixels, ref byte[]? cachedDds)
    {
        var decoder = new BcDecoder();
        // same Wine constraint as the encoder below; decode is a few seconds
        decoder.Options.IsParallel = false;
        using var sprite = decoder.DecodeRawToImageRgba32(
            spriteBlocks, overlay.Width, overlay.Height, CompressionFormat.Bc7);

        var pixels = new byte[overlay.Width * overlay.Height * 4];
        sprite.CopyPixelDataTo(pixels);
        if (cachedPixels != null && pixels.AsSpan().SequenceEqual(cachedPixels))
        {
            return cachedDds!;
        }

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
        var encoded = encoder.EncodeToRawBytes(sprite)[0];

        var dds = DdsAtlas.BuildStandaloneDds(spriteDds, encoded, overlay.Width, overlay.Height);
        cachedPixels = pixels;
        cachedDds = dds;
        return dds;
    }
}
