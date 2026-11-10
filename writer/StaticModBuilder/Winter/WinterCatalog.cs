using System.Text.RegularExpressions;

namespace StaticModBuilder.Winter;

/// <summary>Thin-snow textures the converted materials bind (TIF paths as stored in matbins).</summary>
public sealed record SnowTextures(string Albedo, string Normal, string GroundAlbedo);

/// <summary>
/// Template of a snow twin shader: the matbin it is cloned from, its sampler layout
/// and the params inserted on conversion. Params keys keep the catalogue spelling
/// (twin-prefixed params without the prefix, group_* params in full).
/// </summary>
public sealed record TwinTemplate(
    string Shader,
    string Matbin,
    IReadOnlyList<string> Samplers,
    IReadOnlyDictionary<string, float[]> Params);

/// <summary>
/// Conversion of a source shader to a twin. Plan maps each twin sampler to a source
/// sampler suffix, or to snow_albedo, snow_normal, template, unbound.
/// </summary>
public sealed record ConversionRule(
    string Source,
    string Twin,
    IReadOnlySet<string> SourceSamplers,
    IReadOnlyDictionary<string, string> Plan);

public sealed record TextureRetarget(string Source, string Target, IReadOnlyList<string> Suffixes);

public sealed record TerrainSelector(
    Regex ShaderInclude,
    Regex ShaderExclude,
    Regex NameExclude,
    IReadOnlySet<string> KeepTextures);

public sealed record WinterCatalog(
    SnowTextures Snow,
    IReadOnlyList<TwinTemplate> Templates,
    IReadOnlyList<ConversionRule> Rules,
    IReadOnlyList<TextureRetarget> Retargets,
    TerrainSelector Terrain)
{
    /// <summary>The template of a twin shader; throws KeyNotFoundException when absent.</summary>
    public TwinTemplate Template(string shader) =>
        Templates.FirstOrDefault(t => t.Shader == shader)
        ?? throw new KeyNotFoundException($"no winter template for shader '{shader}'");
}
