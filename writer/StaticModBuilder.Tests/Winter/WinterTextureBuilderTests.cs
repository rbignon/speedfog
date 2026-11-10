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

        var result = WinterTextureBuilder.ProcessTpf(tpf, "x", report);

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
    public void ProcessTpf_WarnsOnSingleMipNormal()
    {
        var dds = DdsFixtures.MakeBc7Dds(256, 256, 1);
        var tpf = MakeTpf(("Y_n", 108, dds));
        var report = new WinterReport();

        var result = WinterTextureBuilder.ProcessTpf(tpf, "y", report);

        Assert.Equal(new TpfResult(0, 0, 1, 1), result);
        Assert.Equal(dds, tpf.Textures[0].Bytes);
        Assert.Contains(report.Lines, l => l.Contains("Y_n"));
    }

    [Fact]
    public void ProcessTpf_ThrowsOnUnsupportedTexture()
    {
        var tpf = MakeTpf(("Z_n", 108, DdsFixtures.MakeDds(256, 256, 3, "DXT3")));
        var ex = Assert.Throws<WinterBuildException>(() =>
            WinterTextureBuilder.ProcessTpf(tpf, "z", new WinterReport()));
        Assert.Contains("Z_n", ex.Message);
    }

    [Fact]
    public void Build_WritesMainAndTwinAndSkipsMissingTwin()
    {
        var gameDir = Path.Combine(_temp, "game");
        var winterDir = Path.Combine(_temp, "winter");
        var src = Path.Combine(gameDir, "asset", "aet", "aet066");
        Directory.CreateDirectory(src);
        MakeTpf(("AET066_003_n", 108, DdsFixtures.MakeBc7Dds(256, 256, 9)))
            .Write(Path.Combine(src, "aet066_003.tpf.dcx"));
        var report = new WinterReport();

        int written = WinterTextureBuilder.Build(gameDir, winterDir, new[] { "aet066_003" }, report);

        Assert.Equal(1, written);
        var output = Path.Combine(winterDir, "asset", "aet", "aet066", "aet066_003.tpf.dcx");
        Assert.True(File.Exists(output));
        var back = TPF.Read(output);
        Assert.Equal(128, DdsImage.Parse(back.Textures[0].Bytes).Width);
        Assert.Contains(report.Lines, l => l.Contains("aet066_003_l"));
    }

    [Fact]
    public void Build_ThrowsWhenMainTpfMissing() =>
        Assert.Throws<WinterBuildException>(() =>
            WinterTextureBuilder.Build(Path.Combine(_temp, "game"), Path.Combine(_temp, "winter"),
                new[] { "aet066_003" }, new WinterReport()));
}
