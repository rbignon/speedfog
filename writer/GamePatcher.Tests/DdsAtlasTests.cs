using Xunit;

namespace GamePatcher.Tests;

public class DdsAtlasTests
{
    private const int HEADER_SIZE = 148; // 4 magic + 124 header + 20 DX10 extension
    private const int BLOCK_SIZE = 16;   // BC7: 16 bytes per 4x4 block

    /// <summary>
    /// Build a minimal DX10 BC7 DDS: header + one byte-per-block-index payload
    /// (every byte of block i is set to (byte)i, so any splice mistake shows up).
    /// </summary>
    private static byte[] MakeDds(int width, int height, int dxgiFormat = 98, int mipCount = 1, string fourCC = "DX10")
    {
        int blocks = (width / 4) * (height / 4);
        var dds = new byte[HEADER_SIZE + blocks * BLOCK_SIZE];
        "DDS "u8.ToArray().CopyTo(dds, 0);
        BitConverter.GetBytes(124).CopyTo(dds, 4);          // dwSize
        BitConverter.GetBytes(height).CopyTo(dds, 12);
        BitConverter.GetBytes(width).CopyTo(dds, 16);
        BitConverter.GetBytes(mipCount).CopyTo(dds, 28);
        BitConverter.GetBytes(32).CopyTo(dds, 76);          // ddspf dwSize
        System.Text.Encoding.ASCII.GetBytes(fourCC).CopyTo(dds, 84);
        BitConverter.GetBytes(dxgiFormat).CopyTo(dds, 128);
        for (int i = 0; i < blocks; i++)
        {
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                dds[HEADER_SIZE + i * BLOCK_SIZE + j] = (byte)i;
            }
        }
        return dds;
    }

    private static byte[] MakeRegionBlocks(int width, int height, byte fill)
    {
        var data = new byte[(width / 4) * (height / 4) * BLOCK_SIZE];
        Array.Fill(data, fill);
        return data;
    }

    [Fact]
    public void ParseHeader_ReadsDx10Bc7Header()
    {
        var dds = MakeDds(8, 4, dxgiFormat: 98, mipCount: 1);

        var info = DdsAtlas.ParseHeader(dds);

        Assert.Equal(8, info.Width);
        Assert.Equal(4, info.Height);
        Assert.Equal(98, info.DxgiFormat);
        Assert.Equal(1, info.MipCount);
        Assert.Equal(HEADER_SIZE, info.DataOffset);
    }

    [Fact]
    public void ParseHeader_RejectsBadMagic()
    {
        var dds = MakeDds(8, 4);
        dds[0] = (byte)'X';

        Assert.Throws<InvalidDataException>(() => DdsAtlas.ParseHeader(dds));
    }

    [Fact]
    public void ParseHeader_RejectsTruncatedHeader()
    {
        var dds = MakeDds(8, 4).Take(100).ToArray();

        Assert.Throws<InvalidDataException>(() => DdsAtlas.ParseHeader(dds));
    }

    [Fact]
    public void ParseHeader_RejectsTruncatedPayload()
    {
        // header claims 16x16 (16 blocks) but only 10 blocks of data follow
        var dds = MakeDds(16, 16).Take(HEADER_SIZE + 10 * BLOCK_SIZE).ToArray();

        Assert.Throws<InvalidDataException>(() => DdsAtlas.ParseHeader(dds));
    }

    [Fact]
    public void ParseHeader_RejectsLegacyNonDx10Header()
    {
        var dds = MakeDds(8, 4, fourCC: "DXT1");

        Assert.Throws<InvalidDataException>(() => DdsAtlas.ParseHeader(dds));
    }

    [Fact]
    public void SpliceBlocks_ReplacesTargetRegionAndPreservesRest()
    {
        // 16x16 atlas = 4x4 blocks; splice an 8x8 region at (4, 4) = blocks (1,1)-(2,2)
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);
        var region = MakeRegionBlocks(8, 8, 0xAA);

        DdsAtlas.SpliceBlocks(dds, info, region, 4, 4, 8, 8);

        var replaced = new[] { 5, 6, 9, 10 }; // block index = row * 4 + col
        for (int i = 0; i < 16; i++)
        {
            byte expected = replaced.Contains(i) ? (byte)0xAA : (byte)i;
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal(expected, dds[HEADER_SIZE + i * BLOCK_SIZE + j]);
            }
        }
    }

    [Fact]
    public void SpliceBlocks_AcceptsRegionTouchingAtlasEdges()
    {
        // full-width region at the top edge: blocks (0,0)-(3,0)
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);
        var region = MakeRegionBlocks(16, 4, 0xBB);

        DdsAtlas.SpliceBlocks(dds, info, region, 0, 0, 16, 4);

        for (int i = 0; i < 16; i++)
        {
            byte expected = i < 4 ? (byte)0xBB : (byte)i;
            Assert.Equal(expected, dds[HEADER_SIZE + i * BLOCK_SIZE]);
        }
    }

    [Fact]
    public void ExtractBlocks_ReadsTargetRegionInRasterOrder()
    {
        // 16x16 atlas = 4x4 blocks; extract the 8x8 region at (4, 4) = blocks 5, 6, 9, 10
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);

        var region = DdsAtlas.ExtractBlocks(dds, info, 4, 4, 8, 8);

        Assert.Equal(4 * BLOCK_SIZE, region.Length);
        var expected = new[] { 5, 6, 9, 10 };
        for (int i = 0; i < 4; i++)
        {
            for (int j = 0; j < BLOCK_SIZE; j++)
            {
                Assert.Equal((byte)expected[i], region[i * BLOCK_SIZE + j]);
            }
        }
    }

    [Fact]
    public void ExtractBlocks_RoundtripsWithSpliceBlocks()
    {
        var dds = MakeDds(16, 16);
        var original = (byte[])dds.Clone();
        var info = DdsAtlas.ParseHeader(dds);

        var region = DdsAtlas.ExtractBlocks(dds, info, 4, 4, 8, 8);
        DdsAtlas.SpliceBlocks(dds, info, region, 4, 4, 8, 8);

        Assert.Equal(original, dds);
    }

    [Fact]
    public void ExtractBlocks_RejectsUnalignedOrOutOfBoundsRect()
    {
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);

        Assert.Throws<ArgumentException>(() => DdsAtlas.ExtractBlocks(dds, info, 2, 4, 8, 8));
        Assert.Throws<ArgumentException>(() => DdsAtlas.ExtractBlocks(dds, info, 12, 12, 8, 8));
    }

    [Fact]
    public void SpliceBlocks_RejectsUnalignedRect()
    {
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);
        var region = MakeRegionBlocks(8, 8, 0xAA);

        Assert.Throws<ArgumentException>(() => DdsAtlas.SpliceBlocks(dds, info, region, 2, 4, 8, 8));
        Assert.Throws<ArgumentException>(() => DdsAtlas.SpliceBlocks(dds, info, region, 4, 4, 6, 8));
    }

    [Fact]
    public void SpliceBlocks_RejectsRegionSizeMismatch()
    {
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);
        var region = MakeRegionBlocks(8, 4, 0xAA); // too small for an 8x8 rect

        Assert.Throws<ArgumentException>(() => DdsAtlas.SpliceBlocks(dds, info, region, 4, 4, 8, 8));
    }

    [Fact]
    public void SpliceBlocks_RejectsRectOutsideAtlas()
    {
        var dds = MakeDds(16, 16);
        var info = DdsAtlas.ParseHeader(dds);
        var region = MakeRegionBlocks(8, 8, 0xAA);

        Assert.Throws<ArgumentException>(() => DdsAtlas.SpliceBlocks(dds, info, region, 12, 12, 8, 8));
    }
}
