using System.Numerics;
using SoulsFormats;

namespace StaticModBuilder.Winter;

/// <summary>
/// A twin shader's template matbin, checked against the catalogue: its shader path, the UV sets
/// its vertex shaders read (from the catalogue), its samplers in layout order and the twin params
/// the catalogue lists, in the matbin's order, with the catalogue values. The objects are never
/// put in a matbin; conversions copy them.
/// </summary>
public sealed record ResolvedTemplate(
    string Shader,
    string ShaderPath,
    int UvSets,
    IReadOnlyList<MATBIN.Sampler> Samplers,
    IReadOnlyList<MATBIN.Param> Params);

/// <summary>
/// Converts one matbin to the snow twin of its shader (pure: no file I/O). The skinned and
/// UV-set exclusions belong to the caller.
/// </summary>
public sealed class MatbinConverter
{
    public const string NoRule = "no rule";
    public const string Layout = "layout";

    private const string GroupPrefix = "group_";
    private const string CameraFadeParam = "g_GX_CameraFadeParamId";

    private readonly WinterCatalog _catalog;
    private readonly IReadOnlyDictionary<string, ResolvedTemplate> _templates;
    private readonly Dictionary<string, ConversionRule> _rules;

    public MatbinConverter(WinterCatalog catalog, IReadOnlyDictionary<string, ResolvedTemplate> templates)
    {
        foreach (var rule in catalog.Rules)
        {
            if (!templates.ContainsKey(rule.Twin))
            {
                throw new WinterBuildException($"matbin conversion: twin '{rule.Twin}' of rule '{rule.Source}' has no resolved template");
            }
        }
        _catalog = catalog;
        _templates = templates;
        _rules = catalog.Rules.ToDictionary(r => r.Source);
    }

    /// <summary>
    /// Checks the template matbin's shader and sampler suffix sequence against the catalogue and
    /// copies its samplers and the params the catalogue lists (order, Key and Type from the
    /// matbin, value from the catalogue). A catalogue param the matbin lacks is an error.
    /// </summary>
    public static ResolvedTemplate Resolve(TwinTemplate template, MATBIN templateMatbin)
    {
        string where = $"template matbin '{template.Matbin}'";
        string shader = MatbinNames.ShaderName(templateMatbin);
        if (shader != template.Shader)
        {
            throw new WinterBuildException($"{where}: shader '{shader}' is not '{template.Shader}'");
        }
        var layout = templateMatbin.Samplers.Select(MatbinNames.SamplerSuffix).ToList();
        if (!layout.SequenceEqual(template.Samplers))
        {
            throw new WinterBuildException(
                $"{where}: sampler layout [{string.Join(", ", layout)}] is not [{string.Join(", ", template.Samplers)}]");
        }

        // Template matbin order, not catalogue order: the validated spike binders follow it.
        string prefix = MatbinNames.ParamPrefix(shader);
        var parameters = new List<MATBIN.Param>();
        foreach (var param in templateMatbin.Params)
        {
            string? key = IsGroup(param.Name) ? param.Name
                : param.Name.StartsWith(prefix, StringComparison.Ordinal) ? param.Name[prefix.Length..]
                : null;
            if (key == null || !template.Params.TryGetValue(key, out var values) || parameters.Any(p => p.Name == param.Name))
            {
                continue;
            }
            parameters.Add(new MATBIN.Param
            {
                Name = param.Name,
                Key = param.Key,
                Type = param.Type,
                Value = CatalogueValue(param.Type, values, $"{where}: param '{param.Name}'"),
            });
        }
        foreach (string key in template.Params.Keys)
        {
            string name = IsGroup(key) ? key : prefix + key;
            if (!parameters.Any(p => p.Name == name))
            {
                throw new WinterBuildException($"{where}: no param '{name}'");
            }
        }

        return new ResolvedTemplate(
            shader,
            templateMatbin.ShaderPath,
            template.UvSets,
            templateMatbin.Samplers.Select(CopySampler).ToList(),
            parameters);
    }

    /// <summary>Whether a conversion rule exists for the shader name (exact match).</summary>
    public bool HasRule(string shader) => _rules.ContainsKey(shader);

    /// <summary>The twin of the rule for the shader, which must have one (<see cref="HasRule"/>).</summary>
    public string TwinOf(string shader) => _rules[shader].Twin;

    /// <summary>The UV sets the vertex shaders of the shader's twin read, per the catalogue.</summary>
    public int RequiredUvSets(string shader) => _templates[TwinOf(shader)].UvSets;

    /// <summary>
    /// Rewrites the matbin to the twin of its shader. Returns false, the matbin untouched, with
    /// "no rule" when its shader has no rule and "layout" when its sampler suffix set is not the
    /// rule's.
    /// </summary>
    public bool TryConvert(MATBIN m, out string? skipReason)
    {
        string shader = MatbinNames.ShaderName(m);
        if (!_rules.TryGetValue(shader, out var rule))
        {
            skipReason = NoRule;
            return false;
        }
        var sources = new Dictionary<string, MATBIN.Sampler>();
        foreach (var sampler in m.Samplers)
        {
            if (!sources.TryAdd(MatbinNames.SamplerSuffix(sampler), sampler))
            {
                skipReason = Layout;
                return false;
            }
        }
        if (!rule.SourceSamplers.SetEquals(sources.Keys))
        {
            skipReason = Layout;
            return false;
        }

        var template = _templates[rule.Twin];
        string sourcePrefix = MatbinNames.ParamPrefix(shader);
        var samplers = template.Samplers.Select(t => PlannedSampler(t, rule.Plan[MatbinNames.SamplerSuffix(t)], sources)).ToList();
        var parameters = ConvertedParams(m.Params, sourcePrefix, template);

        string twinFloat3 = MatbinNames.ParamPrefix(template.Shader) + "float3_0";
        if (!parameters.Any(p => p.Name == twinFloat3))
        {
            throw new WinterBuildException($"matbin conversion {shader} -> {template.Shader}: no param '{twinFloat3}'");
        }
        var leftover = parameters.FirstOrDefault(p => p.Name.StartsWith(sourcePrefix, StringComparison.Ordinal));
        if (leftover != null)
        {
            throw new WinterBuildException($"matbin conversion {shader} -> {template.Shader}: source param '{leftover.Name}' left");
        }

        m.ShaderPath = template.ShaderPath;
        m.Samplers = samplers;
        m.Params = parameters;
        skipReason = null;
        return true;
    }

    private MATBIN.Sampler PlannedSampler(MATBIN.Sampler twin, string plan, IReadOnlyDictionary<string, MATBIN.Sampler> sources)
    {
        var sampler = CopySampler(twin);
        switch (plan)
        {
            case "snow_albedo":
                sampler.Path = _catalog.Snow.Albedo;
                sampler.Unk14 = Vector2.Zero;
                break;
            case "snow_normal":
                sampler.Path = _catalog.Snow.Normal;
                sampler.Unk14 = Vector2.Zero;
                break;
            case "template":
                break;
            case "unbound":
                sampler.Path = "";
                sampler.Unk14 = Vector2.One;
                break;
            default:
                var source = sources[plan];
                sampler.Path = source.Path;
                sampler.Unk14 = source.Unk14;
                break;
        }
        return sampler;
    }

    /// <summary>
    /// The source params in order, minus the source-prefixed ones and the group_* ones the twin
    /// does not list, with the twin's params (prefixed ones always, group_* ones the source lacks)
    /// inserted before g_GX_CameraFadeParamId, or appended when it is absent.
    /// </summary>
    private static List<MATBIN.Param> ConvertedParams(List<MATBIN.Param> source, string sourcePrefix, ResolvedTemplate template)
    {
        var twinGroups = template.Params.Where(p => IsGroup(p.Name)).Select(p => p.Name).ToHashSet();
        var kept = source
            .Where(p => !p.Name.StartsWith(sourcePrefix, StringComparison.Ordinal))
            .Where(p => !IsGroup(p.Name) || twinGroups.Contains(p.Name))
            .ToList();
        var sourceNames = source.Select(p => p.Name).ToHashSet();
        var inserted = template.Params
            .Where(p => !IsGroup(p.Name) || !sourceNames.Contains(p.Name))
            .Select(CopyParam);

        int cameraFade = kept.FindIndex(p => p.Name == CameraFadeParam);
        kept.InsertRange(cameraFade < 0 ? kept.Count : cameraFade, inserted);
        return kept;
    }

    private static bool IsGroup(string name) => name.StartsWith(GroupPrefix, StringComparison.Ordinal);

    private static object CatalogueValue(MATBIN.ParamType type, float[] values, string where)
    {
        int expected = type switch
        {
            MATBIN.ParamType.Float => 1,
            MATBIN.ParamType.Float2 => 2,
            MATBIN.ParamType.Float3 => 3,
            _ => throw new WinterBuildException($"{where}: type {type} is not a float type"),
        };
        if (values.Length != expected)
        {
            throw new WinterBuildException($"{where}: {values.Length} catalogue values for type {type}");
        }
        return type == MATBIN.ParamType.Float ? values[0] : values.ToArray();
    }

    private static MATBIN.Sampler CopySampler(MATBIN.Sampler s) =>
        new() { Type = s.Type, Path = s.Path, Key = s.Key, Unk14 = s.Unk14 };

    /// <summary>Copies a resolved param; array values are cloned so no two matbins share one.</summary>
    private static MATBIN.Param CopyParam(MATBIN.Param p) => new()
    {
        Name = p.Name,
        Key = p.Key,
        Type = p.Type,
        Value = p.Value is float[] values ? values.ToArray() : p.Value,
    };
}
