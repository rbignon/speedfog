namespace StaticModBuilder;

/// <summary>
/// Parsed DX10 DDS header fields needed to splice block-compressed data.
/// </summary>
internal readonly record struct DdsInfo(int Width, int Height, int DxgiFormat, int MipCount, int DataOffset);

/// <summary>
/// Minimal DDS/BC7 helpers for reading texture atlases.
///
/// BC7 stores 4x4 pixel blocks of 16 bytes in raster order, so a block-aligned
/// sub-rectangle can be pulled out (or wrapped into a standalone DDS) without
/// decoding the rest of the atlas.
/// </summary>
internal static class DdsAtlas
{
    private const int BLOCK_DIM = 4;    // BC7 block edge in pixels
    private const int BLOCK_SIZE = 16;  // BC7 block size in bytes
    private const int DX10_DATA_OFFSET = 148; // 4 magic + 124 header + 20 DX10 extension

    /// <summary>
    /// Parse a DX10-extended DDS header. Throws InvalidDataException on legacy
    /// (non-DX10) headers so callers never misread the data offset.
    /// </summary>
    internal static DdsInfo ParseHeader(byte[] dds)
    {
        if (dds.Length < DX10_DATA_OFFSET)
        {
            throw new InvalidDataException($"DDS too short: {dds.Length} bytes");
        }
        if (dds[0] != 'D' || dds[1] != 'D' || dds[2] != 'S' || dds[3] != ' ')
        {
            throw new InvalidDataException("not a DDS file (bad magic)");
        }

        int height = BitConverter.ToInt32(dds, 12);
        int width = BitConverter.ToInt32(dds, 16);
        int mipCount = BitConverter.ToInt32(dds, 28);

        var fourCC = System.Text.Encoding.ASCII.GetString(dds, 84, 4);
        if (fourCC != "DX10")
        {
            throw new InvalidDataException($"unsupported DDS pixel format '{fourCC}' (expected DX10 extension)");
        }

        int dxgiFormat = BitConverter.ToInt32(dds, 128);

        // require at least the base mip level of 16-byte blocks, so a
        // truncated file becomes a warn+skip instead of an Array.Copy crash
        long mip0Size = (long)((width + 3) / 4) * ((height + 3) / 4) * BLOCK_SIZE;
        if (dds.Length < DX10_DATA_OFFSET + mip0Size)
        {
            throw new InvalidDataException(
                $"DDS payload truncated: {dds.Length} bytes, expected at least {DX10_DATA_OFFSET + mip0Size} for {width}x{height}");
        }

        return new DdsInfo(width, height, dxgiFormat, mipCount, DX10_DATA_OFFSET);
    }

    /// <summary>
    /// Read the BC7 blocks of a block-aligned rectangle out of an atlas, in
    /// raster order.
    /// </summary>
    internal static byte[] ExtractBlocks(byte[] atlasDds, DdsInfo info, int x, int y, int width, int height)
    {
        ValidateRect(info, x, y, width, height);

        int regionBlockCols = width / BLOCK_DIM;
        int regionBlockRows = height / BLOCK_DIM;
        var regionBlocks = new byte[regionBlockCols * regionBlockRows * BLOCK_SIZE];

        int atlasBlockCols = info.Width / BLOCK_DIM;
        int firstBlockCol = x / BLOCK_DIM;
        int firstBlockRow = y / BLOCK_DIM;

        for (int row = 0; row < regionBlockRows; row++)
        {
            int srcOffset = info.DataOffset + ((firstBlockRow + row) * atlasBlockCols + firstBlockCol) * BLOCK_SIZE;
            int dstOffset = row * regionBlockCols * BLOCK_SIZE;
            Array.Copy(atlasDds, srcOffset, regionBlocks, dstOffset, regionBlockCols * BLOCK_SIZE);
        }
        return regionBlocks;
    }

    /// <summary>
    /// Wrap raw BC7 blocks into a standalone single-mip DDS, reusing the
    /// 148-byte DX10 header of an existing DDS as template (dimensions and
    /// linear size patched).
    /// </summary>
    internal static byte[] BuildStandaloneDds(byte[] templateDds, byte[] regionBlocks, int width, int height)
    {
        if (templateDds.Length < DX10_DATA_OFFSET)
        {
            throw new ArgumentException($"template DDS too short: {templateDds.Length} bytes");
        }
        if (width % BLOCK_DIM != 0 || height % BLOCK_DIM != 0)
        {
            throw new ArgumentException($"{width}x{height} is not aligned to {BLOCK_DIM}px BC7 blocks");
        }
        int expected = (width / BLOCK_DIM) * (height / BLOCK_DIM) * BLOCK_SIZE;
        if (regionBlocks.Length != expected)
        {
            throw new ArgumentException(
                $"region data is {regionBlocks.Length} bytes, expected {expected} for {width}x{height}");
        }

        var dds = new byte[DX10_DATA_OFFSET + regionBlocks.Length];
        Array.Copy(templateDds, dds, DX10_DATA_OFFSET);
        BitConverter.GetBytes(height).CopyTo(dds, 12);
        BitConverter.GetBytes(width).CopyTo(dds, 16);
        BitConverter.GetBytes(regionBlocks.Length).CopyTo(dds, 20); // linear size
        regionBlocks.CopyTo(dds, DX10_DATA_OFFSET);
        return dds;
    }

    private static void ValidateRect(DdsInfo info, int x, int y, int width, int height)
    {
        if (x % BLOCK_DIM != 0 || y % BLOCK_DIM != 0 || width % BLOCK_DIM != 0 || height % BLOCK_DIM != 0)
        {
            throw new ArgumentException($"rect ({x},{y},{width},{height}) is not aligned to {BLOCK_DIM}px BC7 blocks");
        }
        if (x < 0 || y < 0 || x + width > info.Width || y + height > info.Height)
        {
            throw new ArgumentException($"rect ({x},{y},{width},{height}) exceeds atlas {info.Width}x{info.Height}");
        }
    }
}
