using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// Naming conventions of matbin shaders, samplers, params and texture paths. Paths use the
/// game's backslashes; both separators are accepted.
/// </summary>
public static class MatbinNames
{
    private const string SamplerMarker = "_snp_";
    private const int FamilyLength = 6;
    private static readonly char[] Separators = { '\\', '/' };

    /// <summary>File stem of the shader path: @"...\SPX_DLC02\M[Road_Ground].spx" gives "M[Road_Ground]".</summary>
    public static string ShaderName(MATBIN m) => FlverMaterialTable.MatbinStem(m.ShaderPath);

    /// <summary>The part of the sampler type after the last "_snp_" (the whole type when absent).</summary>
    public static string SamplerSuffix(MATBIN.Sampler s)
    {
        int marker = s.Type.LastIndexOf(SamplerMarker, StringComparison.Ordinal);
        return marker < 0 ? s.Type : s.Type[(marker + SamplerMarker.Length)..];
    }

    /// <summary>Prefix of a shader's own params: "M[AMSN_V][Ov_N]" gives "M_AMSN_V__Ov_N__snp_0_".</summary>
    public static string ParamPrefix(string shader) => shader.Replace('[', '_').Replace(']', '_') + "_snp_0_";

    /// <summary>
    /// File name without extension, split at the last underscore: "AET005_070_a.tif" gives
    /// ("AET005_070", "a"). A name without underscore gives (name, "").
    /// </summary>
    public static (string Stem, string Suffix) TextureStemAndSuffix(string path)
    {
        string name = FlverMaterialTable.MatbinStem(path);
        int underscore = name.LastIndexOf('_');
        return underscore < 0 ? (name, "") : (name[..underscore], name[(underscore + 1)..]);
    }

    /// <summary>
    /// The same texture under another stem: the last directory becomes the new stem's family (its
    /// first 6 characters) and the file "&lt;newStem&gt;_&lt;suffix&gt;.tif".
    /// @"...\AET005\AET005_070_a.tif" with "AET000_081" gives @"...\AET000\AET000_081_a.tif".
    /// </summary>
    public static string WithTextureStem(string path, string newStem)
    {
        if (newStem.Length < FamilyLength)
        {
            throw new WinterBuildException($"texture stem '{newStem}' is shorter than its {FamilyLength}-character family");
        }
        string fileName = $"{newStem}_{TextureStemAndSuffix(path).Suffix}.tif";
        int fileSep = path.LastIndexOfAny(Separators);
        if (fileSep < 0)
        {
            return fileName;
        }
        int dirSep = fileSep == 0 ? -1 : path.LastIndexOfAny(Separators, fileSep - 1);
        string parent = path[..(dirSep + 1)];
        return parent + newStem[..FamilyLength] + path[fileSep] + fileName;
    }
}
