using System.Text;
using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StaticModBuilder.Winter;

namespace StaticModBuilder.Tests.Winter;

internal static class DdsFixtures
{
    /// <summary>
    /// Synthetic DDS: header plus one payload run per level of LevelSize bytes, every byte of
    /// level i equal to (byte)(0x10 * (i + 1)). A DX10 fourCC writes the 148-byte header.
    /// </summary>
    internal static byte[] MakeDds(int width, int height, int mips, string fourCC, int dxgi = 0, int arraySize = 1, int caps2 = 0)
    {
        bool dx10 = fourCC == "DX10";
        int bpb = dx10 ? (dxgi is 71 or 72 or 80 ? 8 : 16) : (fourCC is "DXT1" or "ATI1" or "BC4U" ? 8 : 16);
        int offset = dx10 ? 148 : 128;
        var levels = new List<int>();
        int w = width, h = height;
        for (int i = 0; i < mips; i++)
        {
            levels.Add(DdsImage.LevelSize(w, h, bpb));
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        var dds = new byte[offset + levels.Sum()];
        Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);
        BitConverter.GetBytes(0x000A1007).CopyTo(dds, 8);
        BitConverter.GetBytes(height).CopyTo(dds, 12);
        BitConverter.GetBytes(width).CopyTo(dds, 16);
        BitConverter.GetBytes(levels[0]).CopyTo(dds, 20);
        BitConverter.GetBytes(mips).CopyTo(dds, 28);
        BitConverter.GetBytes(32).CopyTo(dds, 76);
        BitConverter.GetBytes(4).CopyTo(dds, 80);
        Encoding.ASCII.GetBytes(fourCC).CopyTo(dds, 84);
        BitConverter.GetBytes(0x401008).CopyTo(dds, 108);
        BitConverter.GetBytes(caps2).CopyTo(dds, 112);
        if (dx10)
        {
            BitConverter.GetBytes(dxgi).CopyTo(dds, 128);
            BitConverter.GetBytes(3).CopyTo(dds, 132);
            BitConverter.GetBytes(arraySize).CopyTo(dds, 140);
        }
        int pos = offset;
        for (int i = 0; i < levels.Count; i++)
        {
            Array.Fill(dds, (byte)(0x10 * (i + 1)), pos, levels[i]);
            pos += levels[i];
        }
        return dds;
    }

    /// <summary>Synthetic BC1 (DXT1) chain; level i is filled with (byte)(0x10 * (i + 1)).</summary>
    internal static byte[] MakeBc1Dds(int width, int height, int mips) =>
        MakeDds(width, height, mips, "DXT1");

    /// <summary>Synthetic BC7 (DX10, dxgi 98) chain; level i is filled with (byte)(0x10 * (i + 1)).</summary>
    internal static byte[] MakeBc7Dds(int width, int height, int mips) =>
        MakeDds(width, height, mips, "DX10", 98);

    /// <summary>Really encoded BC1 chain (green opaque left half, transparent right half), DX10 dxgi 72.</summary>
    internal static byte[] MakeRealBc1Dds(int size, bool singleMip = false)
    {
        using var image = new Image<Rgba32>(size, size);
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
                image[x, y] = x < size / 2 ? new Rgba32(0, 128, 0, 255) : new Rgba32(0, 0, 0, 0);
        var encoder = new BcEncoder(CompressionFormat.Bc1WithAlpha);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.Options.IsParallel = false;
        var mips = encoder.EncodeToRawBytes(image);
        if (singleMip) mips = new[] { mips[0] };
        return DdsImage.BuildBc1Dds(new byte[0], mips, size, size);
    }
}
