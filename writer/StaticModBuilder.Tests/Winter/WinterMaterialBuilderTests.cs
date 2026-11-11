using System.Numerics;
using SoulsFormats;
using StaticModBuilder.Winter;
using Xunit;

namespace StaticModBuilder.Tests.Winter;

public class WinterMaterialBuilderTests : IDisposable
{
    private const string Root = @"N:\GR\data\INTERROOT_win64\material\matbin";
    private const string Textures = @"N:\GR\data\Asset\Environment\texture";
    private const string OvN = "M[AMSN_V][Ov_N]";
    private const string TerrainRule = "M[AMSN_V][Mb3][3Mask]";
    private const string GroundAlbedo = Textures + @"\AET000\AET000_081_a.tif";

    private static readonly string[] TwinLayout =
        { "Texture2D_0_AlbedoMap_0", "Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0", "Texture2D_3_NormalMap_0" };

    // Appendix A's M[AMSN_V][Ov_N] rule, a test rule for M[AMSN_V][Mb3][3Mask] (a shader the
    // terrain selector includes), three Appendix D retargets and the Appendix D selector.
    private const string Catalogue = """
        [snow_textures]
        albedo = 'N:\GR\data\Asset\Environment\texture\AET000\AET000_080_a.tif'
        normal = 'N:\GR\data\Asset\Environment\texture\AET001\AET001_078_n.tif'
        ground_albedo = 'N:\GR\data\Asset\Environment\texture\AET000\AET000_081_a.tif'
        [[templates]]
        shader = "M[AMSN_V][Snow]"
        matbin = "m13_00_503"
        samplers = ["Texture2D_0_AlbedoMap_0", "Texture2D_2_AlbedoMap_0", "Texture2D_1_NormalMap_0", "Texture2D_3_NormalMap_0"]
        uv_sets = 1
        [templates.params]
        float_0 = 0.0
        float3_0 = [0.0, 2.0, 0.0]
        "group_1_CommonUV-UVParam" = [1.0, 1.0]
        "group_2_CommonUV-UVParam" = [1.0, 1.0]
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
        source = "M[AMSN_V][Mb3][3Mask]"
        twin = "M[AMSN_V][Snow]"
        source_samplers = ["Texture2D_2_NormalMap_0", "Texture2D_7_NormalMap_0", "Texture2D_8_AlbedoMap_0"]
        [rules.plan]
        Texture2D_0_AlbedoMap_0 = "snow_albedo"
        Texture2D_2_AlbedoMap_0 = "Texture2D_8_AlbedoMap_0"
        Texture2D_1_NormalMap_0 = "Texture2D_7_NormalMap_0"
        Texture2D_3_NormalMap_0 = "snow_normal"
        [[texture_retargets]]
        source = "AET000_003"
        target = "AET000_081"
        suffixes = ["a", "n"]
        [[texture_retargets]]
        source = "AET000_052"
        target = "AET000_081"
        suffixes = ["a", "n"]
        [[texture_retargets]]
        source = "AET000_601"
        target = "AET099_986"
        suffixes = ["a"]
        [terrain_retarget]
        shader_include = 'Road_Ground|Cliff_001|Far_AN2|cheap_C|\[Mb[345](?:_Ch2|_N)?\](?:\[[^\]]+\])*\[(3Mask|Far_AN|Far|Ov_N2|Ov_AN|Cover)\]|\[Mb[345](?:_Ch2|_N)?\]\[Far_AN\]'
        shader_exclude = 'Grass|Vesitation|Flower|Edge|Translucen|Snow|Crystal|Emissive|Glow|Skin|magma|Water|Lake|Decal|Sway'
        name_exclude = 'magma|lava|lake|underwater|swamp|river|wet|water|grow|snow|Glow|Emissive|VolcanoGrass|AEGgrass|\.bak|\.mat$'
        keep_textures = ["AET000_081_a", "AET099_986_a", "AET000_080_a", "AET005_076_a", "AET005_068_a"]
        """;

    private static readonly WinterCatalog Catalog = WinterCatalogLoader.ParseMaterials(Catalogue);

    private readonly string _temp = Path.Combine(Path.GetTempPath(), "winter-mat-" + Guid.NewGuid().ToString("N"));

    public WinterMaterialBuilderTests() => Directory.CreateDirectory(_temp);

    public void Dispose() => Directory.Delete(_temp, true);

    private static string Tex(string stem) => $@"{Textures}\{stem[..6]}\{stem}.tif";

    private static string EntryName(string folder, string stem) => $@"{Root}\{folder}\matxml\{stem}.matbin";

    private static MATBIN MakeMatbin(string shader, params (string Suffix, string Path)[] samplers)
    {
        string typePrefix = shader.Replace('[', '_').Replace(']', '_');
        return new MATBIN
        {
            ShaderPath = $@"N:\GR\data\Material\sat\OutputData\SPX\{shader}.spx",
            SourcePath = "x",
            Key = 1,
            Samplers = samplers
                .Select((s, i) => new MATBIN.Sampler { Type = $"{typePrefix}_snp_{s.Suffix}", Path = s.Path, Key = (uint)(100 + i), Unk14 = Vector2.Zero })
                .ToList(),
            Params = new List<MATBIN.Param>
            {
                new() { Name = MatbinNames.ParamPrefix(shader) + "float_1", Value = 0.5f, Type = MATBIN.ParamType.Float, Key = 200 },
                new() { Name = "g_GX_CameraFadeParamId", Value = 0, Type = MATBIN.ParamType.Int, Key = 201 },
            },
        };
    }

    private static MATBIN TemplateMatbin()
    {
        var m = MakeMatbin("M[AMSN_V][Snow]", TwinLayout.Select(s => (s, Tex("AET005_099_a"))).ToArray());
        string prefix = MatbinNames.ParamPrefix("M[AMSN_V][Snow]");
        m.Params = new List<MATBIN.Param>
        {
            new() { Name = prefix + "float_0", Value = 1f, Type = MATBIN.ParamType.Float, Key = 300 },
            new() { Name = prefix + "float3_0", Value = new[] { 1f, 1f, 1f }, Type = MATBIN.ParamType.Float3, Key = 301 },
            new() { Name = "group_1_CommonUV-UVParam", Value = new[] { 3f, 3f }, Type = MATBIN.ParamType.Float2, Key = 302 },
            new() { Name = "group_2_CommonUV-UVParam", Value = new[] { 3f, 3f }, Type = MATBIN.ParamType.Float2, Key = 303 },
        };
        return m;
    }

    /// <summary>An M[AMSN_V][Ov_N]-shaped source (the layout of the Ov_N rule) under any shader.</summary>
    private static MATBIN OvNShaped(string shader, bool withDetailNormal = true)
    {
        var samplers = new List<(string, string)>();
        if (withDetailNormal)
        {
            samplers.Add(("Texture2D_2_NormalMap_0", Tex("AET005_098_n")));
        }
        samplers.Add(("Texture2D_7_NormalMap_0", Tex("AET005_099_n")));
        samplers.Add(("Texture2D_8_AlbedoMap_0", Tex("AET005_099_a")));
        return MakeMatbin(shader, samplers.ToArray());
    }

    private static BND4 MakeBinder(params (string Folder, string Stem, MATBIN Matbin)[] entries)
    {
        var binder = new BND4();
        for (int i = 0; i < entries.Length; i++)
        {
            var (folder, stem, matbin) = entries[i];
            binder.Files.Add(new BinderFile(Binder.FileFlags.Flag1, i, EntryName(folder, stem), matbin.Write()));
        }
        return binder;
    }

    private static BinderFile Entry(BND4 binder, string stem) =>
        binder.Files.Single(f => f.Name.EndsWith($@"\{stem}.matbin", StringComparison.Ordinal));

    private static MATBIN ReadEntry(BND4 binder, string stem) => MATBIN.Read(Entry(binder, stem).Bytes);

    private static IReadOnlyDictionary<string, ResolvedTemplate> Templates() =>
        WinterMaterialBuilder.ResolveTemplates(MakeBinder(("Map_m13_00", "m13_00_503", TemplateMatbin())), Catalog);

    private static MaterialLayouts NoLayouts() => new();

    /// <summary>Writes an uncompressed TPF holding the named (dummy BC1) textures under the fake game folder.</summary>
    private void WriteTpf(string gameDir, string stem, params string[] textureNames)
    {
        var tpf = new TPF();
        foreach (string name in textureNames)
        {
            tpf.Textures.Add(new TPF.Texture(TPF.TPFPlatform.PC)
            {
                Name = name,
                Format = 1,
                Flags1 = 0,
                Bytes = DdsFixtures.MakeBc1Dds(4, 4, 1),
            });
        }
        string dir = Path.Combine(gameDir, "asset", "aet", stem[..6]);
        Directory.CreateDirectory(dir);
        tpf.Write(Path.Combine(dir, stem + ".tpf.dcx"));
    }

    [Fact]
    public void FolderOf_And_StemOf()
    {
        var file = new BinderFile(Binder.FileFlags.Flag1, 0,
            @"N:\GR\data\INTERROOT_win64\material\mtd\Map_m60_00\matxml\AEG001_413.matbin", Array.Empty<byte>());
        Assert.Equal("Map_m60_00", WinterMaterialBuilder.FolderOf(file));
        Assert.Equal("AEG001_413", WinterMaterialBuilder.StemOf(file));

        var dlc = new BinderFile(Binder.FileFlags.Flag1, 1,
            @"N:\GR\data\INTERROOT_win64\material\matbin_DLC02\Map_m61_00\matxml\m61_00_001.matbin", Array.Empty<byte>());
        Assert.Equal("Map_m61_00", WinterMaterialBuilder.FolderOf(dlc));
        Assert.Equal("m61_00_001", WinterMaterialBuilder.StemOf(dlc));

        // Two vanilla character entries have no matxml level: the parent directory is the folder.
        var noMatxml = new BinderFile(Binder.FileFlags.Flag1, 2, $@"{Root}\Chr\C[c2120]_Hair.matbin", Array.Empty<byte>());
        Assert.Equal("Chr", WinterMaterialBuilder.FolderOf(noMatxml));
        Assert.Equal("C[c2120]_Hair", WinterMaterialBuilder.StemOf(noMatxml));
    }

    [Theory]
    [InlineData("Map_m60_00", "m60_00_900", true)]
    [InlineData("Map_Preset", "AEG001_422_Branch_Edge", true)]
    [InlineData("Map_Preset", "AET000_003", true)]
    [InlineData("Map_Preset", "Field_01_grass", false)]
    [InlineData("Chr", "c1000", false)]
    [InlineData("Parts", "AEG001_001", false)]
    public void IsConversionCandidate_LimitsMapPresetToAssetStems(string folder, string stem, bool expected) =>
        Assert.Equal(expected, WinterMaterialBuilder.IsConversionCandidate(folder, stem));

    [Fact]
    public void ResolveTemplates_FindsTheTemplateMatbin()
    {
        var binder = MakeBinder(
            ("Chr", "c1000", OvNShaped(OvN)),
            ("Map_m13_00", "M13_00_503", TemplateMatbin()));

        var templates = WinterMaterialBuilder.ResolveTemplates(binder, Catalog);

        var template = Assert.Single(templates);
        Assert.Equal("M[AMSN_V][Snow]", template.Key);
        Assert.Equal(4, template.Value.Samplers.Count);
    }

    [Fact]
    public void ResolveTemplates_ThrowsWhenMissing()
    {
        // The template stem outside the Map_* folders does not count.
        var binder = MakeBinder(("Chr", "m13_00_503", TemplateMatbin()), ("Map_m60_00", "AEG001_001", OvNShaped(OvN)));

        var e = Assert.Throws<WinterBuildException>(() => WinterMaterialBuilder.ResolveTemplates(binder, Catalog));

        Assert.Contains("m13_00_503", e.Message);
    }

    [Fact]
    public void ResolveTemplates_ThrowsWhenAmbiguous()
    {
        var binder = MakeBinder(("Map_m13_00", "m13_00_503", TemplateMatbin()), ("Map_m60_00", "m13_00_503", TemplateMatbin()));

        var e = Assert.Throws<WinterBuildException>(() => WinterMaterialBuilder.ResolveTemplates(binder, Catalog));

        Assert.Contains("m13_00_503", e.Message);
    }

    [Fact]
    public void ProcessBinder_AppliesPassesInOrder()
    {
        var binder = MakeBinder(
            ("Map_m13_00", "m13_00_503", TemplateMatbin()),
            ("Map_m60_00", "AEG001_001", OvNShaped(OvN)),
            ("Map_m10_00", "m10_00_519", OvNShaped(OvN)),
            ("Map_Preset", "Field_01_grass", MakeMatbin("M[A]",
                ("Texture2D_0_AlbedoMap_0", Tex("AET000_003_a")),
                ("Texture2D_1_AlbedoMap_0", Tex("AET000_601_a")))),
            ("Chr", "c1000", OvNShaped(OvN)),
            ("Map_m60_00", "m60_00_900", MakeMatbin("M[AMSN_V][Mb3][3Mask][Ov_N]",
                ("Texture2D_0_GSBlendMap_AlbedoMap_0", Tex("AET000_052_a")),
                ("Texture2D_1_GSBlendMap_NormalMap_0", Tex("AET000_052_n")))),
            ("Map_m60_00", "m60_00_777", MakeMatbin("M[A]", ("Texture2D_0_AlbedoMap_0", Tex("AET005_099_a")))));
        byte[] skinnedBytes = Entry(binder, "m10_00_519").Bytes;
        byte[] skinnedNoRuleBytes = Entry(binder, "m60_00_777").Bytes;
        byte[] chrBytes = Entry(binder, "c1000").Bytes;
        byte[] templateBytes = Entry(binder, "m13_00_503").Bytes;
        // Another case: stems are compared case-insensitively. m60_00_777 has no rule: it is not
        // a skinned exclusion.
        var layouts = new MaterialLayouts();
        layouts.Add(new FlverMaterialEntry("M10_00_519", Skinned: true, UvSets: 1));
        layouts.Add(new FlverMaterialEntry("m60_00_777", Skinned: true, UvSets: 1));
        var report = new WinterReport();

        var result = WinterMaterialBuilder.ProcessBinder(binder, Catalog, Templates(), layouts, report);

        var converted = ReadEntry(binder, "AEG001_001");
        Assert.Contains("[Snow]", converted.ShaderPath);
        Assert.Equal(Tex("AET005_099_a"), converted.Samplers[1].Path);

        Assert.Same(skinnedBytes, Entry(binder, "m10_00_519").Bytes);
        Assert.Contains(report.Lines, l => l.Contains(EntryName("Map_m10_00", "m10_00_519")));
        Assert.Same(skinnedNoRuleBytes, Entry(binder, "m60_00_777").Bytes);
        Assert.DoesNotContain(report.Lines, l => l.Contains(EntryName("Map_m60_00", "m60_00_777")));

        // Pass 2 before pass 4: the LOD macro goes to the flat white, not to the snow ground.
        var field = ReadEntry(binder, "Field_01_grass");
        Assert.Equal(new[] { Tex("AET000_081_a"), Tex("AET099_986_a") }, field.Samplers.Select(s => s.Path));

        var terrain = ReadEntry(binder, "m60_00_900");
        Assert.Equal(new[] { Tex("AET000_081_a"), Tex("AET000_081_n") }, terrain.Samplers.Select(s => s.Path));

        Assert.Same(chrBytes, Entry(binder, "c1000").Bytes);
        Assert.Same(templateBytes, Entry(binder, "m13_00_503").Bytes);

        Assert.Equal(1, result.Converted);
        Assert.Equal(2, result.RetargetedTextures);
        Assert.Equal(0, result.RetargetedTerrain);
        Assert.Equal(0, result.SkippedLayout);
        Assert.Equal(1, result.SkippedSkinned);
        Assert.Equal(0, result.SkippedUvSets);
        Assert.Equal(new KeyValuePair<string, int>(OvN, 1), Assert.Single(result.ConvertedPerRule));
    }

    [Fact]
    public void ProcessBinder_LeavesVanillaAMatbinWhoseMeshesLackTheUvSetsTheTwinReads()
    {
        // The twin reads two UV sets here. m60_00_001's meshes provide one, m60_00_002's two,
        // m60_00_003 is drawn by no scanned mesh (nothing against it), m60_00_777 has no rule.
        var catalog = WinterCatalogLoader.ParseMaterials(Catalogue.Replace("uv_sets = 1", "uv_sets = 2"));
        var templates = WinterMaterialBuilder.ResolveTemplates(MakeBinder(("Map_m13_00", "m13_00_503", TemplateMatbin())), catalog);
        var binder = MakeBinder(
            ("Map_m60_00", "m60_00_001", OvNShaped(OvN)),
            ("Map_m60_00", "m60_00_002", OvNShaped(OvN)),
            ("Map_m60_00", "m60_00_003", OvNShaped(OvN)),
            ("Map_m60_00", "m60_00_777", MakeMatbin("M[A]", ("Texture2D_0_AlbedoMap_0", Tex("AET005_099_a")))));
        byte[] oneUvBytes = Entry(binder, "m60_00_001").Bytes;
        var layouts = new MaterialLayouts();
        layouts.Add(new FlverMaterialEntry("M60_00_001", Skinned: false, UvSets: 1));
        layouts.Add(new FlverMaterialEntry("m60_00_002", Skinned: false, UvSets: 2));
        layouts.Add(new FlverMaterialEntry("m60_00_777", Skinned: false, UvSets: 1));
        var report = new WinterReport();

        var result = WinterMaterialBuilder.ProcessBinder(binder, catalog, templates, layouts, report);

        Assert.Same(oneUvBytes, Entry(binder, "m60_00_001").Bytes);
        Assert.Contains("[Snow]", ReadEntry(binder, "m60_00_002").ShaderPath);
        Assert.Contains("[Snow]", ReadEntry(binder, "m60_00_003").ShaderPath);
        Assert.Equal(2, result.Converted);
        Assert.Equal(1, result.SkippedUvSets);
        Assert.Equal(0, result.SkippedSkinned);
        string line = Assert.Single(report.Lines, l => l.Contains(EntryName("Map_m60_00", "m60_00_001")));
        Assert.Contains("1 UV set,", line);
        Assert.Contains("M[AMSN_V][Snow] reads 2", line);
        Assert.DoesNotContain(report.Lines, l => l.Contains("m60_00_777"));
    }

    [Fact]
    public void ProcessBinder_TerrainPassSkipsConvertedMatbins()
    {
        var binder = MakeBinder(
            ("Map_m60_00", "m60_00_901", OvNShaped(TerrainRule)),
            ("Map_m60_00", "m60_00_902", OvNShaped(TerrainRule, withDetailNormal: false)));
        var report = new WinterReport();

        var result = WinterMaterialBuilder.ProcessBinder(binder, Catalog, Templates(), NoLayouts(), report);

        var converted = ReadEntry(binder, "m60_00_901");
        Assert.Contains("[Snow]", converted.ShaderPath);
        Assert.Equal(
            new[] { Catalog.Snow.Albedo, Tex("AET005_099_a"), Tex("AET005_099_n"), Catalog.Snow.Normal },
            converted.Samplers.Select(s => s.Path));

        // The same shader left vanilla for its layout takes the terrain pass.
        var layout = ReadEntry(binder, "m60_00_902");
        Assert.Contains(TerrainRule, layout.ShaderPath);
        Assert.Equal(new[] { Tex("AET005_099_n"), GroundAlbedo }, layout.Samplers.Select(s => s.Path));
        Assert.Contains(report.Lines, l => l.Contains(EntryName("Map_m60_00", "m60_00_902")) && l.Contains("layout"));

        Assert.Equal(1, result.Converted);
        Assert.Equal(1, result.SkippedLayout);
        Assert.Equal(1, result.RetargetedTerrain);
        Assert.Equal(0, result.RetargetedTextures);
    }

    [Fact]
    public void ProcessBinder_NamesTheEntryOnFailure()
    {
        var unreadable = MakeBinder(("Map_m60_00", "AEG001_001", OvNShaped(OvN)));
        unreadable.Files[0].Bytes = new byte[] { 1, 2, 3, 4 };
        var e = Assert.Throws<WinterBuildException>(() =>
            WinterMaterialBuilder.ProcessBinder(unreadable, Catalog, Templates(), NoLayouts(), new WinterReport()));
        Assert.Contains(EntryName("Map_m60_00", "AEG001_001"), e.Message);

        // A template without float3_0 breaks the conversion's post-condition.
        var noFloat3 = WinterCatalogLoader.ParseMaterials(Catalogue.Replace("float3_0 = [0.0, 2.0, 0.0]\n", ""));
        var templates = WinterMaterialBuilder.ResolveTemplates(MakeBinder(("Map_m13_00", "m13_00_503", TemplateMatbin())), noFloat3);
        var binder = MakeBinder(("Map_m60_00", "AEG001_001", OvNShaped(OvN)));
        e = Assert.Throws<WinterBuildException>(() =>
            WinterMaterialBuilder.ProcessBinder(binder, noFloat3, templates, NoLayouts(), new WinterReport()));
        Assert.Contains(EntryName("Map_m60_00", "AEG001_001"), e.Message);
    }

    [Fact]
    public void VerifyRetargetTargets_RejectsMissingSuffix()
    {
        string gameDir = Path.Combine(_temp, "game");
        WriteTpf(gameDir, "aet005_047", "AET005_047_a");

        var e = Assert.Throws<WinterBuildException>(() => WinterMaterialBuilder.VerifyRetargetTargets(gameDir,
            new[] { new TextureRetarget("AET005_044", "AET005_047", new[] { "a", "n" }) }));
        Assert.Contains("AET005_047_n", e.Message);

        WinterMaterialBuilder.VerifyRetargetTargets(gameDir,
            new[] { new TextureRetarget("AET005_044", "AET005_047", new[] { "a" }) });

        e = Assert.Throws<WinterBuildException>(() => WinterMaterialBuilder.VerifyRetargetTargets(gameDir,
            new[] { new TextureRetarget("AET005_044", "AET005_048", new[] { "a" }) }));
        Assert.Contains("aet005_048.tpf.dcx", e.Message);
    }

    [Fact]
    public void Build_WritesBothBindersUnderMaterial()
    {
        string gameDir = Path.Combine(_temp, "game");
        string winterDir = Path.Combine(_temp, "winter");
        // No retarget target TPF: Build leaves VerifyRetargetTargets to WinterModBuilder.
        var baseBinder = MakeBinder(
            ("Map_m13_00", "m13_00_503", TemplateMatbin()),
            ("Map_m60_00", "AEG001_001", OvNShaped(OvN)));
        baseBinder.Compression = new DCX.DcxDfltCompressionInfo(DCX.DfltCompressionPreset.DCX_DFLT_10000_44_9);
        // No template matbin in the DLC binder: its conversions use the base binder's templates.
        var dlcBinder = MakeBinder(("Map_m61_00", "m61_00_001", OvNShaped(OvN)));
        Directory.CreateDirectory(Path.Combine(gameDir, "material"));
        baseBinder.Write(Path.Combine(gameDir, WinterMaterialBuilder.BinderSubPaths[0]));
        dlcBinder.Write(Path.Combine(gameDir, WinterMaterialBuilder.BinderSubPaths[1]));
        var report = new WinterReport();

        var result = WinterMaterialBuilder.Build(gameDir, winterDir, Catalog, NoLayouts(), report);

        string baseOut = Path.Combine(winterDir, "material", "allmaterial.matbinbnd.dcx");
        string dlcOut = Path.Combine(winterDir, "material", "allmaterial_dlc02.matbinbnd.dcx");
        Assert.True(File.Exists(baseOut));
        Assert.True(File.Exists(dlcOut));
        var baseBack = BND4.Read(baseOut);
        Assert.Equal(DCX.Type.DCX_DFLT, baseBack.Compression.Type);
        Assert.Contains("[Snow]", ReadEntry(baseBack, "AEG001_001").ShaderPath);
        var dlcBack = BND4.Read(dlcOut);
        Assert.Equal(DCX.Type.None, dlcBack.Compression.Type);
        Assert.Contains("[Snow]", ReadEntry(dlcBack, "m61_00_001").ShaderPath);

        Assert.Equal(2, result.Converted);
        Assert.Equal(new KeyValuePair<string, int>(OvN, 2), Assert.Single(result.ConvertedPerRule));
        Assert.Contains(report.Lines, l => l.Contains("allmaterial.matbinbnd.dcx"));
        Assert.Contains(report.Lines, l => l.Contains("allmaterial_dlc02.matbinbnd.dcx"));
    }
}
