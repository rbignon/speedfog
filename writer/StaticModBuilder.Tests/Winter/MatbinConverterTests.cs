using System.Numerics;
using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class MatbinConverterTests
{
    private const string Twin = "M[AMSN_V][Snow]";
    private const string TwinShaderPath = @"N:\x\SPX\M[AMSN_V][Snow].spx";
    private const string TwinPrefix = "M_AMSN_V__Snow__snp_0_";
    private const string SnowAlbedo = @"N:\a\AET000\AET000_080_a.tif";
    private const string SnowNormal = @"N:\a\AET001\AET001_078_n.tif";

    private static readonly string[] Layout =
        { "Texture2D_0_AlbedoMap_0", "Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0", "Texture2D_3_NormalMap_0" };

    // The M[AMSN_V][Ov_N] rule is Appendix A's; the M[AMSN_V] plan is a test plan that
    // exercises the "template" and "unbound" plan values. The template params are listed out
    // of the template matbin's order: the matbin's order wins (it is the validated spike's).
    private const string Catalogue = """
        [snow_textures]
        albedo = 'N:\a\AET000\AET000_080_a.tif'
        normal = 'N:\a\AET001\AET001_078_n.tif'
        ground_albedo = 'N:\a\AET000\AET000_081_a.tif'
        [[templates]]
        shader = "M[AMSN_V][Snow]"
        matbin = "m13_00_503"
        samplers = ["Texture2D_0_AlbedoMap_0", "Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0", "Texture2D_3_NormalMap_0"]
        uv_sets = 1
        [templates.params]
        "group_2_CommonUV-UVParam" = [1.0, 1.0]
        float3_0 = [0.0, 2.0, 0.0]
        "group_1_CommonUV-UVParam" = [1.0, 1.0]
        float_0 = 0.0
        [[rules]]
        source = "M[AMSN_V][Ov_N]"
        twin = "M[AMSN_V][Snow]"
        source_samplers = ["Texture2D_2_NormalMap_0", "Texture2D_7_NormalMap_0", "Texture2D_8_AlbedoMap_0"]
        [rules.plan]
        Texture2D_0_AlbedoMap_0 = "snow_albedo"
        Texture2D_2_AlbedoMap_0 = "Texture2D_8_AlbedoMap_0"
        Texture2D_1_NormalMap_0 = "Texture2D_7_NormalMap_0"
        Texture2D_3_NormalMap_0 = "snow_normal"
        [[rules]]
        source = "M[AMSN_V]"
        twin = "M[AMSN_V][Snow]"
        source_samplers = ["Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0"]
        [rules.plan]
        Texture2D_0_AlbedoMap_0 = "template"
        Texture2D_2_AlbedoMap_0 = "Texture2D_2_AlbedoMap_0"
        Texture2D_1_NormalMap_0 = "Texture2D_1_NormalMap_0"
        Texture2D_3_NormalMap_0 = "unbound"
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

    private static readonly WinterCatalog Catalog = WinterCatalogLoader.ParseMaterials(Catalogue);

    private static MATBIN MakeMatbin(
        string shaderPath,
        (string type, string path, float ux, float uy)[] samplers,
        (string name, object value, MATBIN.ParamType type)[] @params) => new()
    {
        ShaderPath = shaderPath,
        SourcePath = "x",
        Key = 1,
        Samplers = samplers
            .Select((s, i) => new MATBIN.Sampler { Type = s.type, Path = s.path, Key = (uint)(100 + i), Unk14 = new Vector2(s.ux, s.uy) })
            .ToList(),
        Params = @params
            .Select((p, i) => new MATBIN.Param { Name = p.name, Value = p.value, Type = p.type, Key = (uint)(100 + i) })
            .ToList(),
    };

    private static MATBIN TemplateMatbin(int samplerCount = 4) => MakeMatbin(
        TwinShaderPath,
        Layout.Take(samplerCount).Select((s, i) => ("M_AMSN_V_Snow__snp_" + s, $"tpl_{i}.tif", 4f, 4f)).ToArray(),
        new (string, object, MATBIN.ParamType)[]
        {
            ("g_GX_bOverrideCAParam", true, MATBIN.ParamType.Bool),
            (TwinPrefix + "float_0", 9f, MATBIN.ParamType.Float),
            (TwinPrefix + "float3_0", new[] { 1f, 1f, 1f }, MATBIN.ParamType.Float3),
            ("group_1_CommonUV-UVParam", new[] { 3f, 3f }, MATBIN.ParamType.Float2),
            ("group_2_CommonUV-UVParam", new[] { 3f, 3f }, MATBIN.ParamType.Float2),
            ("g_GX_CameraFadeParamId", 0, MATBIN.ParamType.Int),
        });

    private static ResolvedTemplate Resolved() => MatbinConverter.Resolve(Catalog.Template(Twin), TemplateMatbin());

    private static MatbinConverter Converter() =>
        new(Catalog, new Dictionary<string, ResolvedTemplate> { [Twin] = Resolved() });

    private static MATBIN OvNSource(bool withDetailNormal = true)
    {
        var samplers = new List<(string, string, float, float)>();
        if (withDetailNormal)
        {
            samplers.Add(("M_AMSN_V_Ov_N__snp_Texture2D_2_NormalMap_0", "det_n.tif", 2f, 2f));
        }
        samplers.Add(("M_AMSN_V_Ov_N__snp_Texture2D_7_NormalMap_0", "base_n.tif", 0f, 0f));
        samplers.Add(("M_AMSN_V_Ov_N__snp_Texture2D_8_AlbedoMap_0", "base_a.tif", 0f, 0f));
        return MakeMatbin(
            @"N:\x\SPX\M[AMSN_V][Ov_N].spx",
            samplers.ToArray(),
            new (string, object, MATBIN.ParamType)[]
            {
                ("g_GX_bOverrideCAParam", false, MATBIN.ParamType.Bool),
                ("M_AMSN_V__Ov_N__snp_0_float_1", 0.5f, MATBIN.ParamType.Float),
                ("group_1_CommonUV-UVParam", new[] { 5f, 5f }, MATBIN.ParamType.Float2),
                ("group_4_CommonUV-UVParam", new[] { 7f, 7f }, MATBIN.ParamType.Float2),
                ("g_GX_CameraFadeParamId", 3, MATBIN.ParamType.Int),
            });
    }

    private static MATBIN.Param ParamNamed(MATBIN m, string name) => Assert.Single(m.Params, p => p.Name == name);

    [Fact]
    public void ShaderName_StripsDirectoryAndExtension() =>
        Assert.Equal("M[Road_Ground]", MatbinNames.ShaderName(new MATBIN { ShaderPath = @"N:\x\SPX_DLC02\M[Road_Ground].spx" }));

    [Fact]
    public void ParamPrefix_ReplacesBrackets() =>
        Assert.Equal("M_AMSN_V__Ov_N__Snow__Edge_snp_0_", MatbinNames.ParamPrefix("M[AMSN_V][Ov_N][Snow]_Edge"));

    [Fact]
    public void SamplerSuffix_TakesThePartAfterTheLastSnp() =>
        Assert.Equal("Texture2D_0_AlbedoMap_0",
            MatbinNames.SamplerSuffix(new MATBIN.Sampler { Type = "M_AMSN_V_Snow__snp_Texture2D_0_AlbedoMap_0" }));

    [Fact]
    public void TextureStemAndSuffix_SplitsNames()
    {
        Assert.Equal(("AET005_070", "a"), MatbinNames.TextureStemAndSuffix(@"N:\x\AET005\AET005_070_a.tif"));
        Assert.Equal(("AET099_001", "1m"), MatbinNames.TextureStemAndSuffix("AET099_001_1m.tif"));
        Assert.Equal(("SYSTEX", ""), MatbinNames.TextureStemAndSuffix(@"N:\x\SYSTEX.tif"));
        Assert.Equal(@"N:\GR\data\Asset\Environment\texture\AET000\AET000_081_a.tif",
            MatbinNames.WithTextureStem(@"N:\GR\data\Asset\Environment\texture\AET005\AET005_070_a.tif", "AET000_081"));
        Assert.Throws<WinterBuildException>(() => MatbinNames.WithTextureStem(@"N:\x\AET005\AET005_070_a.tif", "AET0"));
    }

    [Fact]
    public void Resolve_RejectsATemplateWithAnotherLayout()
    {
        var template = Catalog.Template(Twin);
        Assert.Throws<WinterBuildException>(() => MatbinConverter.Resolve(template, TemplateMatbin(samplerCount: 3)));

        var otherShader = TemplateMatbin();
        otherShader.ShaderPath = @"N:\x\SPX\M[AMSN_V].spx";
        Assert.Throws<WinterBuildException>(() => MatbinConverter.Resolve(template, otherShader));
    }

    [Fact]
    public void Resolve_TakesParamValuesFromTheCatalogue()
    {
        var templateMatbin = TemplateMatbin();
        var resolved = MatbinConverter.Resolve(Catalog.Template(Twin), templateMatbin);

        Assert.Equal(Twin, resolved.Shader);
        Assert.Equal(TwinShaderPath, resolved.ShaderPath);
        Assert.Equal(templateMatbin.Samplers.Select(s => (s.Type, s.Key, s.Path, s.Unk14)),
            resolved.Samplers.Select(s => (s.Type, s.Key, s.Path, s.Unk14)));
        Assert.All(resolved.Samplers, s => Assert.DoesNotContain(s, templateMatbin.Samplers));

        Assert.Equal(
            new[] { TwinPrefix + "float_0", TwinPrefix + "float3_0", "group_1_CommonUV-UVParam", "group_2_CommonUV-UVParam" },
            resolved.Params.Select(p => p.Name));
        var float0 = resolved.Params[0];
        Assert.Equal((101u, MATBIN.ParamType.Float), (float0.Key, float0.Type));
        Assert.Equal(0f, Assert.IsType<float>(float0.Value));
        Assert.Equal(new[] { 0f, 2f, 0f }, Assert.IsType<float[]>(resolved.Params[1].Value));
        var group1 = resolved.Params[2];
        Assert.Equal((103u, MATBIN.ParamType.Float2), (group1.Key, group1.Type));
        Assert.Equal(new[] { 1f, 1f }, Assert.IsType<float[]>(group1.Value));
        Assert.All(resolved.Params, p => Assert.DoesNotContain(p, templateMatbin.Params));
    }

    [Fact]
    public void Resolve_RejectsATemplateWithoutACatalogueParam()
    {
        var template = Catalog.Template(Twin);
        var missing = TemplateMatbin();
        missing.Params.RemoveAll(p => p.Name == TwinPrefix + "float3_0");
        var e = Assert.Throws<WinterBuildException>(() => MatbinConverter.Resolve(template, missing));
        Assert.Contains(TwinPrefix + "float3_0", e.Message);

        var wrongType = TemplateMatbin();
        wrongType.Params.Single(p => p.Name == "group_1_CommonUV-UVParam").Type = MATBIN.ParamType.Float3;
        Assert.Throws<WinterBuildException>(() => MatbinConverter.Resolve(template, wrongType));
    }

    [Fact]
    public void TryConvert_ThrowsOnBrokenPostConditionsAndLeavesTheMatbin()
    {
        var noFloat3 = WinterCatalogLoader.ParseMaterials(Catalogue.Replace("float3_0 = [0.0, 2.0, 0.0]\n", ""));
        var converter = new MatbinConverter(noFloat3,
            new Dictionary<string, ResolvedTemplate> { [Twin] = MatbinConverter.Resolve(noFloat3.Template(Twin), TemplateMatbin()) });
        var m = OvNSource();
        var e = Assert.Throws<WinterBuildException>(() => converter.TryConvert(m, out _));
        Assert.Contains("M[AMSN_V][Ov_N]", e.Message);
        Assert.Equal(@"N:\x\SPX\M[AMSN_V][Ov_N].spx", m.ShaderPath);
        Assert.Equal(5, m.Params.Count);

        // A rule from the twin to itself re-inserts params carrying the source prefix.
        var selfRule = WinterCatalogLoader.ParseMaterials(Catalogue.Replace("[[texture_retargets]]", $$"""
            [[rules]]
            source = "{{Twin}}"
            twin = "{{Twin}}"
            source_samplers = [{{string.Join(", ", Layout.Select(l => $"\"{l}\""))}}]
            [rules.plan]
            {{string.Join("\n", Layout.Select(l => $"{l} = \"{l}\""))}}
            [[texture_retargets]]
            """));
        converter = new MatbinConverter(selfRule, new Dictionary<string, ResolvedTemplate> { [Twin] = Resolved() });
        Assert.Throws<WinterBuildException>(() => converter.TryConvert(TemplateMatbin(), out _));
    }

    [Fact]
    public void TryConvert_RewritesSamplersParamsAndShader()
    {
        var resolved = Resolved();
        var converter = new MatbinConverter(Catalog, new Dictionary<string, ResolvedTemplate> { [Twin] = resolved });
        var m = OvNSource();

        Assert.True(converter.TryConvert(m, out var reason));

        Assert.Null(reason);
        Assert.Equal(TwinShaderPath, m.ShaderPath);
        Assert.Equal(("x", 1u), (m.SourcePath, m.Key));
        Assert.Equal(resolved.Samplers.Select(s => (s.Type, s.Key)), m.Samplers.Select(s => (s.Type, s.Key)));
        Assert.Equal(
            new[] { (SnowAlbedo, Vector2.Zero), ("base_a.tif", Vector2.Zero), ("base_n.tif", Vector2.Zero), (SnowNormal, Vector2.Zero) },
            m.Samplers.Select(s => (s.Path, s.Unk14)));
        Assert.All(m.Samplers, s => Assert.DoesNotContain(s, resolved.Samplers));

        Assert.Equal(
            new[]
            {
                "g_GX_bOverrideCAParam", "group_1_CommonUV-UVParam",
                TwinPrefix + "float_0", TwinPrefix + "float3_0", "group_2_CommonUV-UVParam",
                "g_GX_CameraFadeParamId",
            },
            m.Params.Select(p => p.Name));
        Assert.DoesNotContain(m.Params, p => p.Name.StartsWith("M_AMSN_V__Ov_N__snp_0_", StringComparison.Ordinal));
        var group1 = ParamNamed(m, "group_1_CommonUV-UVParam");
        Assert.Equal(102u, group1.Key);
        Assert.Equal(new[] { 5f, 5f }, group1.Value);
        Assert.Equal(new[] { 1f, 1f }, ParamNamed(m, "group_2_CommonUV-UVParam").Value);
        Assert.Equal(0f, ParamNamed(m, TwinPrefix + "float_0").Value);
        var float3 = ParamNamed(m, TwinPrefix + "float3_0");
        Assert.Equal(new[] { 0f, 2f, 0f }, float3.Value);
        Assert.DoesNotContain(float3, resolved.Params);
        Assert.NotSame(resolved.Params[1].Value, float3.Value);
    }

    [Fact]
    public void TryConvert_LeavesAMatbinWithAnotherSamplerSet()
    {
        var converter = Converter();
        var missing = OvNSource(withDetailNormal: false);

        Assert.False(converter.TryConvert(missing, out var reason));

        Assert.Equal("layout", reason);
        Assert.Equal(@"N:\x\SPX\M[AMSN_V][Ov_N].spx", missing.ShaderPath);
        Assert.Equal(2, missing.Samplers.Count);
        Assert.Equal(5, missing.Params.Count);

        var duplicated = OvNSource();
        duplicated.Samplers.Add(new MATBIN.Sampler { Type = duplicated.Samplers[0].Type, Path = "dup_n.tif" });
        Assert.False(converter.TryConvert(duplicated, out reason));
        Assert.Equal("layout", reason);
        Assert.Equal(4, duplicated.Samplers.Count);
    }

    [Fact]
    public void TryConvert_IgnoresShadersWithoutRule()
    {
        var m = MakeMatbin(@"N:\x\SPX\M[A].spx", new[] { ("M_A__snp_Texture2D_0_AlbedoMap_0", "a.tif", 0f, 0f) },
            Array.Empty<(string, object, MATBIN.ParamType)>());

        Assert.False(Converter().TryConvert(m, out var reason));

        Assert.Equal("no rule", reason);
        Assert.Equal(@"N:\x\SPX\M[A].spx", m.ShaderPath);
    }

    [Fact]
    public void TryConvert_UnboundAndTemplatePlanValues()
    {
        var m = MakeMatbin(
            @"N:\x\SPX\M[AMSN_V].spx",
            new[]
            {
                ("M_AMSN_V__snp_Texture2D_2_AlbedoMap_0", "src_a.tif", 2f, 2f),
                ("M_AMSN_V__snp_Texture2D_1_NormalMap_0", "src_n.tif", 0f, 0f),
            },
            new (string, object, MATBIN.ParamType)[] { ("M_AMSN_V__snp_0_float_0", 1f, MATBIN.ParamType.Float) });

        Assert.True(Converter().TryConvert(m, out _));

        Assert.Equal(
            new[] { ("tpl_0.tif", new Vector2(4, 4)), ("src_a.tif", new Vector2(2, 2)), ("src_n.tif", Vector2.Zero), ("", Vector2.One) },
            m.Samplers.Select(s => (s.Path, s.Unk14)));
        Assert.Equal(
            new[] { TwinPrefix + "float_0", TwinPrefix + "float3_0", "group_1_CommonUV-UVParam", "group_2_CommonUV-UVParam" },
            m.Params.Select(p => p.Name));
    }

    [Fact]
    public void Constructor_RejectsARuleWhoseTwinIsNotResolved() =>
        Assert.Throws<WinterBuildException>(() => new MatbinConverter(Catalog, new Dictionary<string, ResolvedTemplate>()));
}
