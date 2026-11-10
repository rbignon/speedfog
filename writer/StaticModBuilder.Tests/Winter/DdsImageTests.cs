using System.Text;
using Xunit;
using StaticModBuilder.Winter;

namespace StaticModBuilder.Tests.Winter;

public class DdsImageTests
{
    private static byte[] MakeDds(int width, int height, int mips, string fourCC, int dxgi = 0, int arraySize = 1, int caps2 = 0) =>
        DdsFixtures.MakeDds(width, height, mips, fourCC, dxgi, arraySize, caps2);

    [Fact]
    public void Parse_ReadsDx10Bc7()
    {
        var info = DdsImage.Parse(MakeDds(512, 256, 10, "DX10", 98));
        Assert.Equal(512, info.Width);
        Assert.Equal(256, info.Height);
        Assert.Equal(10, info.MipCount);
        Assert.Equal(DdsBlockFormat.Bc7, info.Format);
        Assert.True(info.IsDx10);
        Assert.Equal(98, info.DxgiFormat);
        Assert.Equal(16, info.BytesPerBlock);
        Assert.Equal(148, info.DataOffset);
    }

    [Fact]
    public void Parse_ReadsLegacyDxt1AndAti1()
    {
        var dxt1 = DdsImage.Parse(MakeDds(64, 64, 3, "DXT1"));
        Assert.Equal(DdsBlockFormat.Bc1, dxt1.Format);
        Assert.Equal(8, dxt1.BytesPerBlock);
        Assert.Equal(128, dxt1.DataOffset);
        Assert.False(dxt1.IsDx10);

        var ati1 = DdsImage.Parse(MakeDds(64, 64, 3, "ATI1"));
        Assert.Equal(DdsBlockFormat.Bc4, ati1.Format);
        Assert.Equal(8, ati1.BytesPerBlock);
    }

    [Fact]
    public void Parse_RejectsArrayAndCube()
    {
        Assert.Throws<InvalidDataException>(() => DdsImage.Parse(MakeDds(64, 64, 1, "DX10", 98, arraySize: 6)));
        Assert.Throws<InvalidDataException>(() => DdsImage.Parse(MakeDds(64, 64, 1, "DXT1", caps2: 0x200)));
    }

    [Fact]
    public void Parse_RejectsTruncatedPayload()
    {
        var dds = MakeDds(64, 64, 4, "DXT1");
        Assert.Throws<InvalidDataException>(() => DdsImage.Parse(dds[..^1]));
    }

    [Fact]
    public void Parse_AcceptsPaddedPayload()
    {
        var dds = MakeDds(64, 64, 4, "DXT1");
        Array.Resize(ref dds, dds.Length + 16);
        Assert.Equal(4, DdsImage.Parse(dds).MipCount);
    }

    [Fact]
    public void TargetLongSide_FloorAt128()
    {
        Assert.Equal(2048, DdsImage.TargetLongSide(4096));
        Assert.Equal(64, DdsImage.TargetLongSide(128));
        Assert.Equal(64, DdsImage.TargetLongSide(64));
        Assert.Equal(4, DdsImage.TargetLongSide(4));
        Assert.Equal(1, DdsImage.LevelsToDrop(128));
        Assert.Equal(0, DdsImage.LevelsToDrop(64));
    }

    [Fact]
    public void DropTopMips_RemovesLevelZeroAndRewritesHeader()
    {
        var dropped = DdsImage.DropTopMips(MakeDds(256, 128, 8, "DXT1"), 1);
        var info = DdsImage.Parse(dropped);
        Assert.Equal(128, info.Width);
        Assert.Equal(64, info.Height);
        Assert.Equal(7, info.MipCount);
        Assert.Equal(DdsImage.LevelSize(128, 64, 8), BitConverter.ToInt32(dropped, 20));
        Assert.Equal(0x20, dropped[info.DataOffset]);
        int expected = 0;
        int w = 128, h = 64;
        for (int i = 0; i < 7; i++)
        {
            expected += DdsImage.LevelSize(w, h, 8);
            w = Math.Max(1, w / 2);
            h = Math.Max(1, h / 2);
        }
        Assert.Equal(expected, dropped.Length - info.DataOffset);
    }

    [Fact]
    public void DropTopMips_RefusesToEmptyTheChain()
    {
        Assert.Throws<ArgumentException>(() => DdsImage.DropTopMips(MakeDds(64, 64, 1, "DXT1"), 1));
        Assert.Throws<ArgumentException>(() => DdsImage.DropTopMips(MakeDds(64, 64, 3, "DXT1"), 0));
    }

    [Fact]
    public void BuildBc1Dds_FromDx10SourcePatchesFieldsOnly()
    {
        var source = MakeDds(64, 64, 4, "DX10", 98);
        var blocks = new List<byte[]>
        {
            Enumerable.Repeat((byte)1, DdsImage.LevelSize(32, 32, 8)).ToArray(),
            Enumerable.Repeat((byte)2, DdsImage.LevelSize(16, 16, 8)).ToArray(),
            Enumerable.Repeat((byte)3, DdsImage.LevelSize(8, 8, 8)).ToArray(),
        };
        var result = DdsImage.BuildBc1Dds(source, blocks, 32, 32);

        for (int i = 0; i < 148; i++)
        {
            bool patched = (i >= 12 && i < 32) || (i >= 128 && i < 132);
            if (!patched) Assert.Equal(source[i], result[i]);
        }
        Assert.Equal(72, BitConverter.ToInt32(result, 128));
        Assert.Equal(3, BitConverter.ToInt32(result, 28));
        Assert.Equal(32, BitConverter.ToInt32(result, 12));
        Assert.Equal(32, BitConverter.ToInt32(result, 16));
        Assert.Equal(DdsImage.LevelSize(32, 32, 8), BitConverter.ToInt32(result, 20));
        Assert.Equal(blocks.Sum(b => b.Length), result.Length - 148);
        Assert.Equal(1, result[148]);
        Assert.Equal(3, result[^1]);
    }

    [Fact]
    public void BuildBc1Dds_FromLegacySourceSynthesizesDx10Header()
    {
        var source = MakeDds(64, 64, 2, "DXT5");
        var blocks = new List<byte[]>
        {
            new byte[DdsImage.LevelSize(16, 8, 8)],
            new byte[DdsImage.LevelSize(8, 4, 8)],
        };
        var result = DdsImage.BuildBc1Dds(source, blocks, 16, 8);
        var info = DdsImage.Parse(result);
        Assert.Equal(DdsBlockFormat.Bc1, info.Format);
        Assert.True(info.IsDx10);
        Assert.Equal(72, info.DxgiFormat);
        Assert.Equal(16, info.Width);
        Assert.Equal(8, info.Height);
        Assert.Equal(2, info.MipCount);
    }
}
