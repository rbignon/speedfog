using BCnEncoder.Encoder;
using BCnEncoder.ImageSharp;
using BCnEncoder.Shared;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class FrostEncoderTests
{
    [Theory]
    [InlineData(0, 0, 0, 255, 153, 158, 165)]
    [InlineData(255, 255, 255, 128, 250, 255, 255)]
    [InlineData(0, 128, 0, 255, 182, 187, 195)]
    public void Frost_MatchesTheNormalFormula(byte r, byte g, byte b, byte a, byte er, byte eg, byte eb) =>
        AssertFrost(FrostStrength.Normal, new Rgba32(r, g, b, a), er, eg, eb);

    [Theory]
    [InlineData(0, 0, 0, 255, 191, 199, 212)]
    [InlineData(255, 255, 255, 128, 247, 255, 255)]
    [InlineData(0, 128, 0, 255, 208, 216, 228)]
    public void Frost_MatchesTheHeavyFormula(byte r, byte g, byte b, byte a, byte er, byte eg, byte eb) =>
        AssertFrost(FrostStrength.Heavy, new Rgba32(r, g, b, a), er, eg, eb);

    private static void AssertFrost(FrostStrength strength, Rgba32 source, byte er, byte eg, byte eb)
    {
        var p = FrostEncoder.Frost(source, strength);
        Assert.InRange(p.R, er - 1, er + 1);
        Assert.InRange(p.G, eg - 1, eg + 1);
        Assert.InRange(p.B, eb - 1, eb + 1);
        Assert.Equal(source.A, p.A);
    }

    private static byte[] Fixture()
    {
        using var image = new Image<Rgba32>(16, 16);
        for (int y = 0; y < 16; y++)
            for (int x = 0; x < 16; x++)
                image[x, y] = x < 8 ? new Rgba32(0, 128, 0, 255) : new Rgba32(0, 0, 0, 0);
        var encoder = new BcEncoder(CompressionFormat.Bc1WithAlpha);
        encoder.OutputOptions.GenerateMipMaps = true;
        encoder.OutputOptions.Quality = CompressionQuality.Balanced;
        encoder.Options.IsParallel = false;
        var mips = encoder.EncodeToRawBytes(image);
        return DdsImage.BuildBc1Dds(new byte[0], mips, 16, 16);
    }

    [Fact]
    public void FrostAlbedo_HalvesAndWhitensABc1Chain()
    {
        var result = FrostEncoder.FrostAlbedo(Fixture(), 8, FrostStrength.Normal);
        var info = DdsImage.Parse(result);
        Assert.Equal(8, info.Width);
        Assert.Equal(8, info.Height);
        Assert.Equal(DdsBlockFormat.Bc1, info.Format);
        Assert.Equal(72, info.DxgiFormat);
        Assert.Equal(4, info.MipCount);

        using var level0 = FrostEncoder.DecodeLevel(result, 0);
        int opaque = 0, transparent = 0;
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
            {
                var p = level0[x, y];
                if (p.A == 0) { transparent++; continue; }
                opaque++;
                Assert.True(p.R >= 150 && p.G >= 150 && p.B >= 150, $"({x},{y}) = {p}");
                Assert.True(Math.Abs(p.R - p.G) <= 12, $"({x},{y}) = {p}");
            }
        Assert.True(opaque > 0 && transparent > 0);
    }

    [Fact]
    public void FrostAlbedo_HeavyFrostWhitensMoreThanNormal()
    {
        using var normal = FrostEncoder.DecodeLevel(FrostEncoder.FrostAlbedo(Fixture(), 16, FrostStrength.Normal), 0);
        using var heavy = FrostEncoder.DecodeLevel(FrostEncoder.FrostAlbedo(Fixture(), 16, FrostStrength.Heavy), 0);
        // The green half: normal frost reaches a light grey, heavy frost a lighter one.
        Assert.InRange(normal[2, 2].G, 180, 195);
        Assert.InRange(heavy[2, 2].G, 210, 222);
        Assert.True(heavy[2, 2].B > heavy[2, 2].R, $"heavy frost is cool: {heavy[2, 2]}");
        Assert.Equal(0, heavy[12, 12].A);
    }

    [Fact]
    public void FrostAlbedo_KeepsFullSizeWhenTargetIsNative()
    {
        var info = DdsImage.Parse(FrostEncoder.FrostAlbedo(Fixture(), 16, FrostStrength.Normal));
        Assert.Equal(16, info.Width);
        Assert.Equal(5, info.MipCount);
    }

    [Fact]
    public void FrostAlbedo_RejectsAnUnreachableTarget() =>
        Assert.Throws<WinterBuildException>(() => FrostEncoder.FrostAlbedo(Fixture(), 4, FrostStrength.Normal));
}
