using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// Counters of a material build, in matbins (not samplers): a converted matbin can also count
/// as retargeted. ConvertedPerRule is keyed by source shader name.
/// </summary>
public sealed record MaterialResult(
    int Converted,
    int RetargetedTextures,
    int RetargetedTerrain,
    int SkippedLayout,
    int SkippedSkinned,
    IReadOnlyDictionary<string, int> ConvertedPerRule);

/// <summary>
/// Rewrites the game's two material binders for the winter look: texture retargets (pass 2),
/// conversions to the snow twins (pass 3) and terrain albedo retargets (pass 4), over the
/// matbins of the Map_* folders only. Matbins no pass changed keep their original bytes.
/// </summary>
public static class WinterMaterialBuilder
{
    public static readonly string[] BinderSubPaths =
    {
        Path.Combine("material", "allmaterial.matbinbnd.dcx"),
        Path.Combine("material", "allmaterial_dlc02.matbinbnd.dcx"),
    };

    private const string MatxmlSegment = "matxml";
    private const string MatbinExtension = ".matbin";
    private const string CandidatePrefix = "Map_";
    private const string MapPresetFolder = "Map_Preset";
    private const string SnowMarker = "Snow";
    private const int FamilyLength = 6;
    private static readonly char[] Separators = { '\\', '/' };
    private static readonly string[] PresetStemPrefixes = { "AEG", "AET" };

    /// <summary>
    /// The binder folder of an entry: the path segment before "matxml"
    /// (@"...\material\matbin\Map_m60_00\matxml\AEG001_413.matbin" gives "Map_m60_00"), or the
    /// parent directory for the few entries without a matxml level.
    /// </summary>
    public static string FolderOf(BinderFile file)
    {
        string[] segments = file.Name.Split(Separators);
        int matxml = Array.FindLastIndex(segments, s => s.Equals(MatxmlSegment, StringComparison.OrdinalIgnoreCase));
        int folder = matxml > 0 ? matxml - 1 : segments.Length - 2;
        return folder >= 0 ? segments[folder] : "";
    }

    /// <summary>File name of the entry without ".matbin".</summary>
    public static string StemOf(BinderFile file) => FlverMaterialTable.MatbinStem(file.Name);

    public static bool IsCandidateFolder(string folder) => folder.StartsWith(CandidatePrefix, StringComparison.Ordinal);

    /// <summary>Map_* folders, and in Map_Preset only the AEG/AET stems (Field_* presets are terrain).</summary>
    public static bool IsConversionCandidate(string folder, string stem) =>
        IsCandidateFolder(folder)
        && (folder != MapPresetFolder || PresetStemPrefixes.Any(p => stem.StartsWith(p, StringComparison.Ordinal)));

    /// <summary>
    /// Resolves each twin template from its named matbin, which must appear exactly once (stem,
    /// case-insensitive) in the Map_* folders of the base binder.
    /// </summary>
    public static IReadOnlyDictionary<string, ResolvedTemplate> ResolveTemplates(BND4 baseBinder, WinterCatalog catalog)
    {
        var resolved = new Dictionary<string, ResolvedTemplate>();
        foreach (var template in catalog.Templates)
        {
            var hits = MapMatbins(baseBinder)
                .Where(f => string.Equals(StemOf(f), template.Matbin, StringComparison.OrdinalIgnoreCase))
                .ToList();
            string where = $"template matbin '{template.Matbin}' of '{template.Shader}'";
            if (hits.Count == 0)
            {
                throw new WinterBuildException($"{where}: not found in the Map_* folders of the base binder");
            }
            if (hits.Count > 1)
            {
                throw new WinterBuildException($"{where}: found {hits.Count} times: {string.Join(", ", hits.Select(f => f.Name))}");
            }
            resolved[template.Shader] = MatbinConverter.Resolve(template, ReadMatbin(hits[0]));
        }
        return resolved;
    }

    /// <summary>
    /// Checks that every retarget target TPF exists in the game folder
    /// (asset/aet/&lt;family&gt;/&lt;target&gt;.tpf.dcx) and holds "&lt;Target&gt;_&lt;suffix&gt;" for each listed
    /// suffix, so no matbin points at a texture the game does not ship.
    /// </summary>
    public static void VerifyRetargetTargets(string gameDir, IReadOnlyList<TextureRetarget> retargets)
    {
        var texturesByTpf = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var retarget in retargets)
        {
            string where = $"texture retarget {retarget.Source} -> {retarget.Target}";
            string stem = retarget.Target.ToLowerInvariant();
            if (stem.Length < FamilyLength)
            {
                throw new WinterBuildException($"{where}: target shorter than its {FamilyLength}-character family");
            }
            string path = Path.Combine(gameDir, "asset", "aet", stem[..FamilyLength], stem + ".tpf.dcx");
            if (!texturesByTpf.TryGetValue(path, out var names))
            {
                if (!File.Exists(path))
                {
                    throw new WinterBuildException($"{where}: missing {path}");
                }
                TPF tpf;
                try
                {
                    tpf = TPF.Read(path);
                }
                catch (Exception e)
                {
                    throw new WinterBuildException($"{where}: {path}: {e.Message}");
                }
                names = tpf.Textures.Select(t => t.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
                texturesByTpf[path] = names;
            }
            foreach (string suffix in retarget.Suffixes)
            {
                string texture = $"{retarget.Target}_{suffix}";
                if (!names.Contains(texture))
                {
                    throw new WinterBuildException($"{where}: {path} has no texture {texture}");
                }
            }
        }
    }

    /// <summary>
    /// Runs passes 2 to 4 over every matbin of the Map_* folders and rewrites the entries that
    /// changed. Skinned stems (compared case-insensitively) are never converted.
    /// </summary>
    public static MaterialResult ProcessBinder(
        BND4 binder,
        WinterCatalog catalog,
        IReadOnlyDictionary<string, ResolvedTemplate> templates,
        IReadOnlySet<string> skinned,
        WinterReport report)
    {
        var converter = new MatbinConverter(catalog, templates);
        var skinnedStems = new HashSet<string>(skinned, StringComparer.OrdinalIgnoreCase);
        int converted = 0, retargetedTextures = 0, retargetedTerrain = 0, skippedLayout = 0, skippedSkinned = 0;
        var perRule = new SortedDictionary<string, int>(StringComparer.Ordinal);

        foreach (var file in MapMatbins(binder))
        {
            string folder = FolderOf(file);
            string stem = StemOf(file);
            var m = ReadMatbin(file);
            bool changed = false;

            if (MatbinRetargeter.ApplyTextureRetargets(m, catalog.Retargets) > 0)
            {
                retargetedTextures++;
                changed = true;
            }

            if (IsConversionCandidate(folder, stem))
            {
                string shader = MatbinNames.ShaderName(m);
                if (skinnedStems.Contains(stem))
                {
                    report.Line($"{file.Name}: left vanilla: skinned ({shader})");
                    skippedSkinned++;
                }
                else if (TryConvert(converter, m, file, out string? reason))
                {
                    perRule[shader] = perRule.GetValueOrDefault(shader) + 1;
                    converted++;
                    changed = true;
                }
                else if (reason == MatbinConverter.Layout)
                {
                    report.Line($"warning: {file.Name}: left vanilla: sampler layout differs from the rule of {shader}");
                    skippedLayout++;
                }
            }

            string current = MatbinNames.ShaderName(m);
            if (!current.Contains(SnowMarker, StringComparison.Ordinal)
                && MatbinRetargeter.TerrainApplies(current, stem, folder, catalog.Terrain)
                && MatbinRetargeter.RetargetTerrainAlbedos(m, catalog.Terrain, catalog.Snow.GroundAlbedo) > 0)
            {
                retargetedTerrain++;
                changed = true;
            }

            if (changed)
            {
                file.Bytes = m.Write();
            }
        }

        return new MaterialResult(converted, retargetedTextures, retargetedTerrain, skippedLayout, skippedSkinned, perRule);
    }

    /// <summary>
    /// Verifies the retarget targets, reads both binders, resolves the templates from the base
    /// binder (used for the DLC binder too), processes both and only then writes them under
    /// &lt;winterDir&gt;/material with the compression they were read with.
    /// </summary>
    public static MaterialResult Build(string gameDir, string winterDir, WinterCatalog catalog, IReadOnlySet<string> skinned, WinterReport report)
    {
        VerifyRetargetTargets(gameDir, catalog.Retargets);
        var binders = BinderSubPaths.Select(sub => ReadBinder(Path.Combine(gameDir, sub))).ToList();
        var templates = ResolveTemplates(binders[0], catalog);

        var results = new List<MaterialResult>();
        for (int i = 0; i < binders.Count; i++)
        {
            var result = ProcessBinder(binders[i], catalog, templates, skinned, report);
            Summarize(Path.GetFileName(BinderSubPaths[i]), result, report);
            results.Add(result);
        }

        string outputDir = Path.Combine(winterDir, "material");
        Directory.CreateDirectory(outputDir);
        for (int i = 0; i < binders.Count; i++)
        {
            binders[i].Write(Path.Combine(outputDir, Path.GetFileName(BinderSubPaths[i])));
        }
        return Sum(results);
    }

    private static IEnumerable<BinderFile> MapMatbins(BND4 binder) =>
        binder.Files.Where(f => f.Name.EndsWith(MatbinExtension, StringComparison.OrdinalIgnoreCase) && IsCandidateFolder(FolderOf(f)));

    private static MATBIN ReadMatbin(BinderFile file)
    {
        try
        {
            return MATBIN.Read(file.Bytes);
        }
        catch (Exception e)
        {
            throw new WinterBuildException($"{file.Name}: unreadable matbin: {e.Message}");
        }
    }

    private static bool TryConvert(MatbinConverter converter, MATBIN m, BinderFile file, out string? reason)
    {
        try
        {
            return converter.TryConvert(m, out reason);
        }
        catch (WinterBuildException e)
        {
            throw new WinterBuildException($"{file.Name}: {e.Message}");
        }
    }

    private static BND4 ReadBinder(string path)
    {
        if (!File.Exists(path))
        {
            throw new WinterBuildException($"missing material binder {path}");
        }
        try
        {
            return BND4.Read(path);
        }
        catch (Exception e)
        {
            throw new WinterBuildException($"material binder {path}: {e.Message}");
        }
    }

    private static void Summarize(string binderName, MaterialResult r, WinterReport report)
    {
        report.Line(
            $"{binderName}: {r.Converted} converted, {r.RetargetedTextures} texture-retargeted, "
            + $"{r.RetargetedTerrain} terrain-retargeted, {r.SkippedLayout} left for layout, {r.SkippedSkinned} left as skinned");
        foreach (var (rule, count) in r.ConvertedPerRule)
        {
            report.Line($"{binderName}:   {rule}: {count} converted");
        }
    }

    private static MaterialResult Sum(IReadOnlyList<MaterialResult> results)
    {
        var perRule = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var (rule, count) in results.SelectMany(r => r.ConvertedPerRule))
        {
            perRule[rule] = perRule.GetValueOrDefault(rule) + count;
        }
        return new MaterialResult(
            results.Sum(r => r.Converted),
            results.Sum(r => r.RetargetedTextures),
            results.Sum(r => r.RetargetedTerrain),
            results.Sum(r => r.SkippedLayout),
            results.Sum(r => r.SkippedSkinned),
            perRule);
    }
}
