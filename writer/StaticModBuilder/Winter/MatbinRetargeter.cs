using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// Pure MATBIN-level texture repoints: the stem table (Limgrave ground, grass atlases) and the
/// terrain pass sending the albedos of multi-layer terrain shaders to the snow ground texture.
/// </summary>
public static class MatbinRetargeter
{
    private const string MapPresetFolder = "Map_Preset";
    private const string FieldPrefix = "Field_";
    private const string AlbedoEnding = "_a.tif";
    private static readonly string[] AetMarkers = { @"texture\AET", "texture/AET" };

    /// <summary>
    /// Repoints every sampler whose (stem, suffix) matches an entry listing that suffix to the
    /// entry's target stem. Returns the number of samplers rewritten.
    /// </summary>
    public static int ApplyTextureRetargets(MATBIN m, IReadOnlyList<TextureRetarget> retargets)
    {
        int count = 0;
        foreach (MATBIN.Sampler s in m.Samplers)
        {
            (string stem, string suffix) = MatbinNames.TextureStemAndSuffix(s.Path);
            if (suffix.Length == 0)
            {
                continue;
            }
            TextureRetarget? hit = retargets.FirstOrDefault(r =>
                string.Equals(r.Source, stem, StringComparison.OrdinalIgnoreCase)
                && r.Suffixes.Contains(suffix, StringComparer.OrdinalIgnoreCase));
            if (hit is null)
            {
                continue;
            }
            s.Path = MatbinNames.WithTextureStem(s.Path, hit.Target);
            count++;
        }
        return count;
    }

    /// <summary>
    /// Whether a terrain matbin takes the ground albedo: its shader is included (or it is a
    /// Field_ matbin of the Map_Preset folder) and neither the shader nor the matbin name is excluded.
    /// </summary>
    public static bool TerrainApplies(string shaderName, string stem, string folder, TerrainSelector sel)
    {
        bool included = sel.ShaderInclude.IsMatch(shaderName)
            || (folder == MapPresetFolder && stem.StartsWith(FieldPrefix, StringComparison.Ordinal));
        return included && !sel.ShaderExclude.IsMatch(shaderName) && !sel.NameExclude.IsMatch(stem);
    }

    /// <summary>
    /// Repoints to <paramref name="groundAlbedoPath"/> every environment albedo (AET texture,
    /// "_a.tif") that is not on the keep list. Returns the number of samplers rewritten.
    /// </summary>
    public static int RetargetTerrainAlbedos(MATBIN m, TerrainSelector sel, string groundAlbedoPath)
    {
        int count = 0;
        foreach (MATBIN.Sampler s in m.Samplers)
        {
            if (!s.Path.EndsWith(AlbedoEnding, StringComparison.OrdinalIgnoreCase)
                || !AetMarkers.Any(k => s.Path.Contains(k, StringComparison.OrdinalIgnoreCase)))
            {
                continue;
            }
            string fileStem = FlverMaterialTable.MatbinStem(s.Path);
            if (sel.KeepTextures.Contains(fileStem, StringComparer.OrdinalIgnoreCase))
            {
                continue;
            }
            s.Path = groundAlbedoPath;
            count++;
        }
        return count;
    }
}
