using System.Text;

namespace StaticModBuilder.Winter;

public enum DdsBlockFormat { Bc1, Bc3, Bc4, Bc5, Bc7 }

/// <summary>Parsed header of a block-compressed 2D DDS (DX10 or legacy FourCC).</summary>
public readonly record struct DdsHeaderInfo(
    int Width, int Height, int MipCount, DdsBlockFormat Format,
    bool IsDx10, int DxgiFormat, int DataOffset, int BytesPerBlock);

/// <summary>
/// General block-compressed DDS helpers for the winter texture pipeline: header parsing,
/// dropping top mip levels without re-encoding, and wrapping BC1 blocks into a DX10 DDS.
/// Byte arrays in, byte arrays out. (DdsAtlas stays BC7-only for the title screen.)
/// </summary>
public static class DdsImage
{
    public const int Dx10DataOffset = 148;
    public const int LegacyDataOffset = 128;

    private const int OffHeight = 12;
    private const int OffWidth = 16;
    private const int OffPitch = 20;
    private const int OffDepth = 24;
    private const int OffMipCount = 28;
    private const int OffPfFlags = 80;
    private const int OffFourCC = 84;
    private const int OffCaps = 108;
    private const int OffCaps2 = 112;
    private const int OffDxgi = 128;
    private const int OffResourceDimension = 132;
    private const int OffArraySize = 140;

    private const int Caps2Cubemap = 0x200;
    private const int Caps2Volume = 0x200000;
    private const int DxgiBc1UnormSrgb = 72;

    public static DdsHeaderInfo Parse(byte[] dds)
    {
        if (dds.Length < LegacyDataOffset)
            throw new InvalidDataException($"DDS too short: {dds.Length} bytes");
        if (dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
            throw new InvalidDataException("not a DDS file (bad magic)");

        int height = BitConverter.ToInt32(dds, OffHeight);
        int width = BitConverter.ToInt32(dds, OffWidth);
        int mipCount = Math.Max(1, BitConverter.ToInt32(dds, OffMipCount));
        int caps2 = BitConverter.ToInt32(dds, OffCaps2);
        if ((caps2 & (Caps2Cubemap | Caps2Volume)) != 0)
            throw new InvalidDataException("cube and volume DDS are not supported");
        if (width < 1 || height < 1)
            throw new InvalidDataException($"invalid DDS size {width}x{height}");

        string fourCC = Encoding.ASCII.GetString(dds, OffFourCC, 4);
        DdsBlockFormat format;
        int bpb;
        bool isDx10 = fourCC == "DX10";
        int dxgi = 0;
        int dataOffset = LegacyDataOffset;
        if (isDx10)
        {
            if (dds.Length < Dx10DataOffset)
                throw new InvalidDataException($"DX10 DDS too short: {dds.Length} bytes");
            dataOffset = Dx10DataOffset;
            dxgi = BitConverter.ToInt32(dds, OffDxgi);
            if (BitConverter.ToInt32(dds, OffArraySize) > 1)
                throw new InvalidDataException("array DDS is not supported");
            (format, bpb) = dxgi switch
            {
                71 or 72 => (DdsBlockFormat.Bc1, 8),
                77 or 78 => (DdsBlockFormat.Bc3, 16),
                80 => (DdsBlockFormat.Bc4, 8),
                83 => (DdsBlockFormat.Bc5, 16),
                98 or 99 => (DdsBlockFormat.Bc7, 16),
                _ => throw new InvalidDataException($"unsupported DXGI format {dxgi}"),
            };
        }
        else
        {
            (format, bpb) = fourCC switch
            {
                "DXT1" => (DdsBlockFormat.Bc1, 8),
                "DXT5" => (DdsBlockFormat.Bc3, 16),
                "ATI1" or "BC4U" => (DdsBlockFormat.Bc4, 8),
                "ATI2" or "BC5U" => (DdsBlockFormat.Bc5, 16),
                _ => throw new InvalidDataException($"unsupported DDS pixel format '{fourCC}'"),
            };
        }

        long chain = 0;
        int w = width, h = height;
        for (int i = 0; i < mipCount; i++)
        {
            chain += LevelSize(w, h, bpb);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        if (dds.Length < dataOffset + chain)
            throw new InvalidDataException(
                $"DDS payload truncated: {dds.Length} bytes, expected at least {dataOffset + chain} for {width}x{height} with {mipCount} mips");

        return new DdsHeaderInfo(width, height, mipCount, format, isDx10, dxgi, dataOffset, bpb);
    }

    public static int LevelSize(int width, int height, int bytesPerBlock) =>
        Math.Max(1, (width + 3) / 4) * Math.Max(1, (height + 3) / 4) * bytesPerBlock;

    public static int TargetLongSide(int nativeLongSide) =>
        nativeLongSide >= 128 ? nativeLongSide / 2 : nativeLongSide;

    public static int LevelsToDrop(int nativeLongSide) =>
        TargetLongSide(nativeLongSide) == nativeLongSide ? 0 : 1;

    public static byte[] DropTopMips(byte[] dds, int levels)
    {
        var info = Parse(dds);
        if (levels < 1 || levels >= info.MipCount)
            throw new ArgumentException($"cannot drop {levels} of {info.MipCount} mip levels");

        int skip = 0;
        int w = info.Width, h = info.Height;
        for (int i = 0; i < levels; i++)
        {
            skip += LevelSize(w, h, info.BytesPerBlock);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }

        int start = info.DataOffset + skip;
        var result = new byte[dds.Length - skip];
        Array.Copy(dds, result, info.DataOffset);
        Array.Copy(dds, start, result, info.DataOffset, dds.Length - start);
        BitConverter.GetBytes(h).CopyTo(result, OffHeight);
        BitConverter.GetBytes(w).CopyTo(result, OffWidth);
        BitConverter.GetBytes(LevelSize(w, h, info.BytesPerBlock)).CopyTo(result, OffPitch);
        BitConverter.GetBytes(info.MipCount - levels).CopyTo(result, OffMipCount);
        return result;
    }

    public static byte[] BuildBc1Dds(byte[] headerSource, IReadOnlyList<byte[]> mipBlocks, int width, int height)
    {
        int payload = mipBlocks.Sum(b => b.Length);
        var dds = new byte[Dx10DataOffset + payload];

        bool sourceIsDx10 = headerSource.Length >= Dx10DataOffset
            && Encoding.ASCII.GetString(headerSource, OffFourCC, 4) == "DX10";
        if (sourceIsDx10)
        {
            Array.Copy(headerSource, dds, Dx10DataOffset);
        }
        else
        {
            Encoding.ASCII.GetBytes("DDS ").CopyTo(dds, 0);
            BitConverter.GetBytes(124).CopyTo(dds, 4);
            BitConverter.GetBytes(0x000A1007).CopyTo(dds, 8);
            BitConverter.GetBytes(1).CopyTo(dds, OffDepth);
            BitConverter.GetBytes(32).CopyTo(dds, 76);
            BitConverter.GetBytes(0x4).CopyTo(dds, OffPfFlags);
            Encoding.ASCII.GetBytes("DX10").CopyTo(dds, OffFourCC);
            BitConverter.GetBytes(0x401008).CopyTo(dds, OffCaps);
            BitConverter.GetBytes(3).CopyTo(dds, OffResourceDimension);
            BitConverter.GetBytes(1).CopyTo(dds, OffArraySize);
        }

        BitConverter.GetBytes(height).CopyTo(dds, OffHeight);
        BitConverter.GetBytes(width).CopyTo(dds, OffWidth);
        BitConverter.GetBytes(LevelSize(width, height, 8)).CopyTo(dds, OffPitch);
        BitConverter.GetBytes(mipBlocks.Count).CopyTo(dds, OffMipCount);
        BitConverter.GetBytes(DxgiBc1UnormSrgb).CopyTo(dds, OffDxgi);

        int pos = Dx10DataOffset;
        foreach (var block in mipBlocks)
        {
            block.CopyTo(dds, pos);
            pos += block.Length;
        }
        return dds;
    }
}
