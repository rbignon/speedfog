using System.Text.RegularExpressions;
using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class MatbinRetargeterTests
{
    private const string GroundAlbedo = @"N:\GR\data\Asset\Environment\texture\AET000\AET000_081_a.tif";

    private static MATBIN.Sampler Sampler(string path) =>
        new() { Type = "g_Diffuse", Path = path, Key = 0, Unk14 = new System.Numerics.Vector2(1, 1) };

    private static MATBIN Matbin(params string[] paths) => new()
    {
        ShaderPath = @"N:\x\SPX\M[Road_Ground].spx",
        SourcePath = "",
        Key = 1,
        Samplers = paths.Select(Sampler).ToList(),
    };

    private static TerrainSelector Selector() => new(
        new Regex(@"Road_Ground|Cliff_001|Far_AN2|cheap_C|\[Mb[345](?:_Ch2|_N)?\](?:\[[^\]]+\])*\[(3Mask|Far_AN|Far|Ov_N2|Ov_AN|Cover)\]|\[Mb[345](?:_Ch2|_N)?\]\[Far_AN\]"),
        new Regex(@"Grass|Vesitation|Flower|Edge|Translucen|Snow|Crystal|Emissive|Glow|Skin|magma|Water|Lake|Decal|Sway", RegexOptions.IgnoreCase),
        new Regex(@"magma|lava|lake|underwater|swamp|river|wet|water|grow|snow|Glow|Emissive|VolcanoGrass|AEGgrass|\.bak|\.mat$", RegexOptions.IgnoreCase),
        new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            { "AET000_081_a", "AET099_986_a", "AET000_080_a", "AET005_076_a", "AET005_068_a" });

    [Fact]
    public void ApplyTextureRetargets_RewritesListedSuffixesOnly()
    {
        var m = Matbin(
            @"N:\t\AET005\AET005_070_a.tif",
            @"N:\t\AET005\AET005_070_n.tif",
            @"N:\t\AET000\AET000_003_n.tif",
            @"N:\t\AET005\AET005_099_a.tif");
        var retargets = new List<TextureRetarget>
        {
            new("AET005_070", "AET005_076", new[] { "a" }),
            new("AET000_003", "AET000_081", new[] { "a", "n" }),
        };

        int count = MatbinRetargeter.ApplyTextureRetargets(m, retargets);

        Assert.Equal(2, count);
        Assert.Equal(@"N:\t\AET005\AET005_076_a.tif", m.Samplers[0].Path);
        Assert.Equal(@"N:\t\AET005\AET005_070_n.tif", m.Samplers[1].Path);
        Assert.Equal(@"N:\t\AET000\AET000_081_n.tif", m.Samplers[2].Path);
        Assert.Equal(@"N:\t\AET005\AET005_099_a.tif", m.Samplers[3].Path);
    }

    [Theory]
    [InlineData("M[AMSN_V][Mb3][3Mask][Ov_N]", "m60_00_123", "Map_m60_00", true)]
    [InlineData("M[AMSN_V][Mb3][3Mask][Ov_N][Snow]", "m60_00_123", "Map_m60_00", false)]
    [InlineData("M[AMSN_V]_Grass", "m60_00_123", "Map_m60_00", false)]
    [InlineData("M[A]", "Field_01_grass", "Map_Preset", true)]
    [InlineData("M[A]", "Field_01_grass", "Map_m60_00", false)]
    [InlineData("M[Road_Ground]", "m61_00_lake_bed", "Map_m61_00", false)]
    [InlineData("M[Road_Ground]", "m61_00_010", "Map_m61_00", true)]
    public void TerrainApplies_FollowsIncludeExcludeAndFieldPresets(string shader, string stem, string folder, bool expected)
    {
        Assert.Equal(expected, MatbinRetargeter.TerrainApplies(shader, stem, folder, Selector()));
    }

    [Fact]
    public void RetargetTerrainAlbedos_SkipsKeepListNormalsAndNonAetPaths()
    {
        var m = Matbin(
            @"N:\GR\data\Asset\Environment\texture\AET000\AET000_003_a.tif",
            @"N:\GR\data\Asset\Environment\texture\AET000\AET000_081_a.tif",
            @"N:\GR\data\Asset\Environment\texture\AET000\AET000_003_n.tif",
            @"N:\GR\data\Other\SYSTEX\SYSTEX_DummyAlbedo_a.tif");

        int count = MatbinRetargeter.RetargetTerrainAlbedos(m, Selector(), GroundAlbedo);

        Assert.Equal(1, count);
        Assert.Equal(GroundAlbedo, m.Samplers[0].Path);
        Assert.Equal(@"N:\GR\data\Asset\Environment\texture\AET000\AET000_081_a.tif", m.Samplers[1].Path);
        Assert.Equal(@"N:\GR\data\Asset\Environment\texture\AET000\AET000_003_n.tif", m.Samplers[2].Path);
        Assert.Equal(@"N:\GR\data\Other\SYSTEX\SYSTEX_DummyAlbedo_a.tif", m.Samplers[3].Path);
    }
}
