using BCnEncoder.Decoder;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// Coefficients of the frost formula: v = Base + Slope * luminance (luminance in 0..1), output
/// (v + TintRed, v, v + TintBlue) clamped to 0..1, alpha kept. Normal keeps the texture's grain
/// (a black pixel reaches a mid grey, 158/255). Heavy is meant for dark textures under warm, dim
/// light, where Normal reads as pale wood rather than snow (the Shadow Keep elevator cabin).
/// </summary>
public sealed record FrostStrength(string Name, double Base, double Slope, double TintRed, double TintBlue)
{
    public static readonly FrostStrength Normal = new("normal", 0.62, 0.38, -0.02, 0.03);
    public static readonly FrostStrength Heavy = new("heavy", 0.78, 0.22, -0.03, 0.05);
}

/// <summary>
/// Frost formula and BC1 re-encoding of vanilla albedo textures: decode a level, whiten every
/// pixel, encode BC1 with 1-bit alpha and a full mip chain.
/// </summary>
public static class FrostEncoder
{
    public static Rgba32 Frost(Rgba32 p, FrostStrength strength)
    {
        double lum = (0.30 * p.R + 0.59 * p.G + 0.11 * p.B) / 255.0;
        double v = strength.Base + strength.Slope * lum;
        return new Rgba32(ToByte(v + strength.TintRed), ToByte(v), ToByte(v + strength.TintBlue), p.A);
    }

    private static byte ToByte(double v) => (byte)Math.Round(Math.Clamp(v, 0.0, 1.0) * 255.0);

    private static CompressionFormat DecodeFormat(DdsBlockFormat format) => format switch
    {
        DdsBlockFormat.Bc1 => CompressionFormat.Bc1WithAlpha,
        DdsBlockFormat.Bc3 => CompressionFormat.Bc3,
        DdsBlockFormat.Bc4 => CompressionFormat.Bc4,
        DdsBlockFormat.Bc5 => CompressionFormat.Bc5,
        DdsBlockFormat.Bc7 => CompressionFormat.Bc7,
        _ => throw new ArgumentOutOfRangeException(nameof(format)),
    };

    public static Image<Rgba32> DecodeLevel(byte[] dds, int level)
    {
        var info = DdsImage.Parse(dds);
        if (level < 0 || level >= info.MipCount)
            throw new ArgumentOutOfRangeException(nameof(level), $"level {level} of {info.MipCount}");

        int offset = info.DataOffset;
        int w = info.Width, h = info.Height;
        for (int i = 0; i < level; i++)
        {
            offset += DdsImage.LevelSize(w, h, info.BytesPerBlock);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        var blocks = new byte[DdsImage.LevelSize(w, h, info.BytesPerBlock)];
        Array.Copy(dds, offset, blocks, 0, blocks.Length);

        var decoder = new BcDecoder();
        decoder.Options.IsParallel = false;
        return decoder.DecodeRawToImageRgba32(blocks, w, h, DecodeFormat(info.Format));
    }

    public static byte[] FrostAlbedo(byte[] vanillaDds, int targetLongSide, FrostStrength strength)
    {
        var info = DdsImage.Parse(vanillaDds);
        int longSide = Math.Max(info.Width, info.Height);
        int level;
        if (targetLongSide == longSide) level = 0;
        else if (info.MipCount > 1 && targetLongSide == Math.Max(1, longSide / 2)) level = 1;
        else throw new WinterBuildException(
            $"target long side {targetLongSide} is neither level 0 ({longSide}) nor level 1 of the {info.Width}x{info.Height} chain");

        using var image = DecodeLevel(vanillaDds, level);
        image.ProcessPixelRows(accessor =>
        {
            for (int y = 0; y < accessor.Height; y++)
            {
                var row = accessor.GetRowSpan(y);
                for (int x = 0; x < row.Length; x++) row[x] = Frost(row[x], strength);
            }
        });

        var encoder = new BcEncoder(CompressionFormat.Bc1WithAlpha);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.Options.IsParallel = false;
        var mips = encoder.EncodeToRawBytes(image);
        return DdsImage.BuildBc1Dds(vanillaDds, mips, image.Width, image.Height);
    }
}
