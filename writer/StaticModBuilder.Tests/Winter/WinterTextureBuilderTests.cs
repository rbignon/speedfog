using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class WinterTextureBuilderTests : IDisposable
{
    private readonly string _temp = Path.Combine(Path.GetTempPath(), "winter-tex-" + Guid.NewGuid().ToString("N"));

    public WinterTextureBuilderTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, true);

    private static TPF MakeTpf(params (string Name, byte Format, byte[] Dds)[] textures)
    {
        var tpf = new TPF();
        foreach (var (name, format, dds) in textures)
        {
            tpf.Textures.Add(new TPF.Texture(TPF.TPFPlatform.PC)
            {
                Name = name,
                Format = format,
                Flags1 = 0,
                Bytes = dds,
            });
        }
        return tpf;
    }

    [Theory]
    [InlineData("AET066_003_a", true)]
    [InlineData("AET490_010_Billboards_a", true)]
    [InlineData("AET066_003_a_l", true)]
    [InlineData("AET066_003_n", false)]
    [InlineData("AET490_010_1m", false)]
    [InlineData("AET066_003_n_l", false)]
    public void IsAlbedo_MatchesTheAlbedoSuffixes(string name, bool expected) =>
        Assert.Equal(expected, WinterTextureBuilder.IsAlbedo(name));

    [Fact]
    public void ProcessTpf_FrostsAlbedoReducesNormalLeavesSmallMask()
    {
        var mask = DdsFixtures.MakeDds(64, 64, 7, "ATI1");
        var tpf = MakeTpf(
            ("X_a", 1, DdsFixtures.MakeRealBc1Dds(256)),
            ("X_n", 108, DdsFixtures.MakeBc7Dds(256, 256, 9)),
            ("X_1m", 103, mask));
        var report = new WinterReport();

        var result = WinterTextureBuilder.ProcessTpf(tpf, "x", FrostStrength.Normal, report);

        Assert.Equal(new TpfResult(1, 1, 1, 0), result);
        var albedo = DdsImage.Parse(tpf.Textures[0].Bytes);
        Assert.Equal(128, albedo.Width);
        Assert.Equal(DdsBlockFormat.Bc1, albedo.Format);
        Assert.Equal(72, albedo.DxgiFormat);
        Assert.Equal(WinterTextureBuilder.TpfFormatBc1, tpf.Textures[0].Format);

        var normal = tpf.Textures[1];
        var normalInfo = DdsImage.Parse(normal.Bytes);
        Assert.Equal(128, normalInfo.Width);
        Assert.Equal(8, normalInfo.MipCount);
        Assert.Equal(0x20, normal.Bytes[normalInfo.DataOffset]);
        Assert.Equal(108, normal.Format);

        Assert.Equal(103, tpf.Textures[2].Format);
        Assert.Equal(mask, tpf.Textures[2].Bytes);
    }

    [Fact]
    public void ProcessTpf_FrostsTheAlbedoWithTheGivenStrength()
    {
        var normal = MakeTpf(("X_a", 1, DdsFixtures.MakeRealBc1Dds(256)));
        var heavy = MakeTpf(("X_a", 1, DdsFixtures.MakeRealBc1Dds(256)));

        WinterTextureBuilder.ProcessTpf(normal, "x", FrostStrength.Normal, new WinterReport());
        WinterTextureBuilder.ProcessTpf(heavy, "x", FrostStrength.Heavy, new WinterReport());

        using var normalLevel = FrostEncoder.DecodeLevel(normal.Textures[0].Bytes, 0);
        using var heavyLevel = FrostEncoder.DecodeLevel(heavy.Textures[0].Bytes, 0);
        Assert.True(heavyLevel[8, 8].G > normalLevel[8, 8].G + 20, $"heavy {heavyLevel[8, 8]} vs normal {normalLevel[8, 8]}");
    }

    [Fact]
    public void ProcessTpf_WarnsOnSingleMipNormal()
    {
        var dds = DdsFixtures.MakeBc7Dds(256, 256, 1);
        var tpf = MakeTpf(("Y_n", 108, dds));
        var report = new WinterReport();

        var result = WinterTextureBuilder.ProcessTpf(tpf, "y", FrostStrength.Normal, report);

        Assert.Equal(new TpfResult(0, 0, 1, 1), result);
        Assert.Equal(dds, tpf.Textures[0].Bytes);
        Assert.Contains(report.Lines, l => l.Contains("Y_n"));
    }

    [Fact]
    public void ProcessTpf_FrostsSingleMipAlbedoAtNativeSize()
    {
        var tpf = MakeTpf(("S_a", 1, DdsFixtures.MakeRealBc1Dds(256, singleMip: true)));
        var report = new WinterReport();

        var result = WinterTextureBuilder.ProcessTpf(tpf, "s", FrostStrength.Normal, report);

        Assert.Equal(new TpfResult(1, 0, 0, 1), result);
        var info = DdsImage.Parse(tpf.Textures[0].Bytes);
        Assert.Equal(256, info.Width);
        Assert.Equal(DdsBlockFormat.Bc1, info.Format);
        Assert.Equal(72, info.DxgiFormat);
        Assert.Equal(WinterTextureBuilder.TpfFormatBc1, tpf.Textures[0].Format);
        Assert.Contains(report.Lines, l => l.Contains("s/S_a") && l.Contains("single mip level"));
    }

    [Fact]
    public void ProcessTpf_NamesTheTextureWhenFrostingFails()
    {
        var truncated = DdsFixtures.MakeBc1Dds(256, 256, 9);
        var tpf = MakeTpf(("F_a", 1, truncated[..^1]));
        var ex = Assert.Throws<WinterBuildException>(() =>
            WinterTextureBuilder.ProcessTpf(tpf, "f", FrostStrength.Normal, new WinterReport()));
        Assert.StartsWith("f/F_a: ", ex.Message);
    }

    [Fact]
    public void ProcessTpf_ThrowsOnUnsupportedTexture()
    {
        var tpf = MakeTpf(("Z_n", 108, DdsFixtures.MakeDds(256, 256, 3, "DXT3")));
        var ex = Assert.Throws<WinterBuildException>(() =>
            WinterTextureBuilder.ProcessTpf(tpf, "z", FrostStrength.Normal, new WinterReport()));
        Assert.Contains("Z_n", ex.Message);
    }

    [Fact]
    public void Build_WritesMainAndTwinAndSkipsMissingTwin()
    {
        var gameDir = Path.Combine(_temp, "game");
        var winterDir = Path.Combine(_temp, "winter");
        var src = Path.Combine(gameDir, "asset", "aet", "aet066");
        Directory.CreateDirectory(src);
        MakeTpf(
            ("AET066_003_a", 1, DdsFixtures.MakeRealBc1Dds(256)),
            ("AET066_003_n", 108, DdsFixtures.MakeBc7Dds(256, 256, 9)))
            .Write(Path.Combine(src, "aet066_003.tpf.dcx"));
        var report = new WinterReport();

        int written = WinterTextureBuilder.Build(gameDir, winterDir,
            new[] { new TextureEntry("aet066_003", FrostStrength.Heavy) }, report);

        Assert.Equal(1, written);
        var output = Path.Combine(winterDir, "asset", "aet", "aet066", "aet066_003.tpf.dcx");
        Assert.True(File.Exists(output));
        var back = TPF.Read(output);
        Assert.Equal(128, DdsImage.Parse(back.Textures[0].Bytes).Width);
        Assert.Equal(128, DdsImage.Parse(back.Textures[1].Bytes).Width);
        // The entry's strength reaches the encoder: heavy frost on (0, 128, 0) lands near 216.
        using var albedo = FrostEncoder.DecodeLevel(back.Textures[0].Bytes, 0);
        Assert.InRange(albedo[4, 4].G, 205, 225);
        Assert.Contains(report.Lines, l => l.Contains("aet066_003_l"));
        Assert.Contains("aet066_003: 1 frosted (heavy), 1 reduced, 0 untouched", report.Lines);
    }

    [Fact]
    public void Build_ThrowsWhenMainTpfMissing() =>
        Assert.Throws<WinterBuildException>(() =>
            WinterTextureBuilder.Build(Path.Combine(_temp, "game"), Path.Combine(_temp, "winter"),
                new[] { new TextureEntry("aet066_003", FrostStrength.Normal) }, new WinterReport()));
}
