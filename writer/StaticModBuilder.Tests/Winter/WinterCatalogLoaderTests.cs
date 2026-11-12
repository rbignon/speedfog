using Xunit;
using StaticModBuilder.Winter;

namespace StaticModBuilder.Tests.Winter;

public class WinterCatalogLoaderTests
{
    private const string Minimal = """
        [snow_textures]
        albedo = 'N:\a\AET000_080_a.tif'
        normal = 'N:\a\AET001_078_n.tif'
        ground_albedo = 'N:\a\AET000_081_a.tif'
        [[templates]]
        shader = "M[AMSN_V][Snow]"
        matbin = "m13_00_503"
        samplers = ["Texture2D_0_AlbedoMap_0", "Texture2D_2_AlbedoMap_0"]
        uv_sets = 1
        [templates.params]
        float_0 = 0.0
        float3_0 = [0.0, 2.0, 0.0]
        [[rules]]
        source = "M[AMSN_V]"
        twin = "M[AMSN_V][Snow]"
        source_samplers = ["Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0"]
        [rules.plan]
        Texture2D_0_AlbedoMap_0 = "snow_albedo"
        Texture2D_2_AlbedoMap_0 = "Texture2D_2_AlbedoMap_0"
        [[texture_retargets]]
        source = "AET005_070"
        target = "AET005_076"
        suffixes = ["a"]
        [terrain_retarget]
        shader_include = 'Road_Ground'
        shader_exclude = 'Grass'
        name_exclude = 'magma'
        keep_textures = ["AET000_081_a"]
        """;

    private static void AssertRejected(string toml, string messagePart)
    {
        var e = Assert.Throws<InvalidDataException>(() => WinterCatalogLoader.ParseMaterials(toml));
        Assert.StartsWith(WinterCatalogLoader.Prefix, e.Message);
        Assert.Contains(messagePart, e.Message);
    }

    [Fact]
    public void ParseMaterials_ReadsEveryTable()
    {
        var c = WinterCatalogLoader.ParseMaterials(Minimal);
        Assert.Equal(@"N:\a\AET000_080_a.tif", c.Snow.Albedo);
        Assert.Equal(new[] { 0f, 2f, 0f }, c.Template("M[AMSN_V][Snow]").Params["float3_0"]);
        Assert.Equal(1, c.Template("M[AMSN_V][Snow]").UvSets);
        Assert.Equal("snow_albedo", c.Rules[0].Plan["Texture2D_0_AlbedoMap_0"]);
        Assert.Contains("Texture2D_1_NormalMap_0", c.Rules[0].SourceSamplers);
        Assert.Equal(new[] { "a" }, c.Retargets[0].Suffixes);
        Assert.Matches(c.Terrain.ShaderInclude, "M[Road_Ground]");
        Assert.Matches(c.Terrain.NameExclude, "AEG_MAGMA_01");
    }

    [Fact]
    public void ParseMaterials_RejectsTemplateWithoutUvSets() =>
        AssertRejected(Minimal.Replace("uv_sets = 1\n", ""), "uv_sets");

    [Theory]
    [InlineData("0")]
    [InlineData("1.5")]
    [InlineData("\"2\"")]
    public void ParseMaterials_RejectsUvSetsThatAreNotAPositiveInteger(string value) =>
        AssertRejected(Minimal.Replace("uv_sets = 1", "uv_sets = " + value), "uv_sets");

    [Fact]
    public void ParseMaterials_RejectsPlanKeysOutsideTheTwinLayout() =>
        AssertRejected(Minimal.Replace("Texture2D_2_AlbedoMap_0 = \"Texture2D_2", "Texture2D_9_Foo = \"Texture2D_2"), "plan");

    [Fact]
    public void ParseMaterials_RejectsUnknownPlanSource() =>
        AssertRejected(Minimal.Replace("= \"Texture2D_2_AlbedoMap_0\"\n", "= \"Texture2D_7_NormalMap_0\"\n"), "plan");

    [Fact]
    public void ParseMaterials_RejectsDuplicateRuleSource()
    {
        var rule = Minimal[Minimal.IndexOf("[[rules]]", StringComparison.Ordinal)..Minimal.IndexOf("[[texture_retargets]]", StringComparison.Ordinal)];
        AssertRejected(Minimal.Replace("[[texture_retargets]]", rule + "[[texture_retargets]]"), "duplicate");
    }

    [Fact]
    public void ParseMaterials_RejectsRuleWithoutTemplate() =>
        AssertRejected(Minimal.Replace("twin = \"M[AMSN_V][Snow]\"", "twin = \"M[AMSN][Snow]\""), "template");

    [Fact]
    public void ParseMaterials_RejectsRetargetSuffixOutsideAN() =>
        AssertRejected(Minimal.Replace("suffixes = [\"a\"]", "suffixes = [\"a\", \"m\"]"), "suffix");

    [Fact]
    public void ParseMaterials_RejectsBadRegex() =>
        AssertRejected(Minimal.Replace("shader_include = 'Road_Ground'", "shader_include = '[unclosed'"), "regex");

    [Fact]
    public void ParseTextures_ReturnsLowerCaseStemsWithTheNormalFrost() =>
        Assert.Equal(
            new[] { new TextureEntry("aet210_414", FrostStrength.Normal), new TextureEntry("aet066_003", FrostStrength.Normal) },
            WinterCatalogLoader.ParseTextures("""tpfs = ["AET210_414", "aet066_003"]"""));

    [Fact]
    public void ParseTextures_AppendsTheHeavyListWithTheHeavyFrost() =>
        Assert.Equal(
            new[] { new TextureEntry("aet210_414", FrostStrength.Normal), new TextureEntry("aet420_389", FrostStrength.Heavy) },
            WinterCatalogLoader.ParseTextures("""
                heavy_tpfs = ["AET420_389"]
                tpfs = ["aet210_414"]
                """));

    [Theory]
    [InlineData("""tpfs = ["aet210_414", "AET210_414"]""", "tpfs: texture stem 'aet210_414' listed twice")]
    [InlineData("""
        tpfs = ["aet210_414"]
        heavy_tpfs = ["aet210_414"]
        """, "heavy_tpfs: texture stem 'aet210_414' listed twice")]
    [InlineData("""
        tpfs = ["aet210_414"]
        heavy_tpfs = ["aet420_389.tpf"]
        """, "heavy_tpfs: bad texture stem")]
    [InlineData("""heavy_tpfs = ["aet420_389"]""", "missing array 'tpfs'")]
    [InlineData("""tpfs = []""", "both empty")]
    public void ParseTextures_RejectsRepeatedBadOrListlessStemsAndAnEmptyCatalogue(string toml, string message)
    {
        var ex = Assert.Throws<InvalidDataException>(() => WinterCatalogLoader.ParseTextures(toml));
        Assert.StartsWith(WinterCatalogLoader.Prefix, ex.Message);
        Assert.Contains(message, ex.Message);
    }

    [Fact]
    public void LoadMaterials_MissingFileIsInvalidData() =>
        Assert.Throws<InvalidDataException>(() => WinterCatalogLoader.LoadMaterials("/nonexistent/winter.toml"));

    [Fact]
    public void ShippedCatalogues_Load()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Combine(dir.FullName, "data", "plugins", "winter_materials.toml")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var plugins = Path.Combine(dir!.FullName, "data", "plugins");

        var c = WinterCatalogLoader.LoadMaterials(Path.Combine(plugins, "winter_materials.toml"));
        Assert.Equal(3, c.Templates.Count);
        Assert.Equal(35, c.Rules.Count);
        Assert.Equal(26, c.Retargets.Count);
        Assert.All(c.Rules, r => c.Template(r.Twin));
        Assert.Equal(2, c.Template("M[AMSN_V][Mb2][Ov_N][Snow]").UvSets);
        var textures = WinterCatalogLoader.LoadTextures(Path.Combine(plugins, "winter_textures.toml"));
        Assert.Equal(135, textures.Count);
        // The shipped catalogue exercises both lists.
        Assert.Contains(textures, t => t.Frost == FrostStrength.Heavy);
    }
}
