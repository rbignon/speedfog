using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace StaticModBuilder.Winter;

/// <summary>
/// Loads and validates the winter catalogues (data/plugins/winter_materials.toml and
/// winter_textures.toml). Every failure is an InvalidDataException starting with Prefix.
/// </summary>
public static class WinterCatalogLoader
{
    public const string Prefix = "winter catalogue:";

    private static readonly HashSet<string> PlanVocabulary =
        new() { "snow_albedo", "snow_normal", "template", "unbound" };
    private static readonly Regex StemPattern = new(@"^aet\d{3}_\d{3}(_billboards)?$");

    public static WinterCatalog ParseMaterials(string toml)
    {
        var root = ParseRoot(toml);

        var snowTable = Table(root, "snow_textures", "root");
        var snow = new SnowTextures(
            Tif(snowTable, "albedo"), Tif(snowTable, "normal"), Tif(snowTable, "ground_albedo"));

        var templates = new List<TwinTemplate>();
        foreach (var t in TableArray(root, "templates", required: true))
            templates.Add(ParseTemplate(t));
        var byShader = new Dictionary<string, TwinTemplate>();
        foreach (var t in templates)
            if (!byShader.TryAdd(t.Shader, t))
                throw Fail($"duplicate template '{t.Shader}'");

        var rules = new List<ConversionRule>();
        var sources = new HashSet<string>();
        foreach (var r in TableArray(root, "rules", required: false))
        {
            var rule = ParseRule(r, byShader);
            if (!sources.Add(rule.Source))
                throw Fail($"duplicate rule source '{rule.Source}'");
            rules.Add(rule);
        }

        var retargets = new List<TextureRetarget>();
        var retargetSources = new HashSet<string>();
        foreach (var r in TableArray(root, "texture_retargets", required: false))
        {
            var source = Str(r, "source", "texture_retargets");
            var suffixes = StrArray(r, "suffixes", source);
            if (suffixes.Count == 0)
                throw Fail($"retarget '{source}': suffixes must not be empty");
            foreach (var s in suffixes)
                if (s != "a" && s != "n")
                    throw Fail($"retarget '{source}': suffix '{s}' must be \"a\" or \"n\"");
            if (!retargetSources.Add(source))
                throw Fail($"duplicate retarget source '{source}'");
            retargets.Add(new TextureRetarget(source, Str(r, "target", source), suffixes));
        }

        var terrainTable = Table(root, "terrain_retarget", "root");
        var terrain = new TerrainSelector(
            Rx(terrainTable, "shader_include", RegexOptions.None),
            Rx(terrainTable, "shader_exclude", RegexOptions.IgnoreCase),
            Rx(terrainTable, "name_exclude", RegexOptions.IgnoreCase),
            StrArray(terrainTable, "keep_textures", "terrain_retarget").ToHashSet());

        return new WinterCatalog(snow, templates, rules, retargets, terrain);
    }

    public static IReadOnlyList<string> ParseTextures(string toml)
    {
        var root = ParseRoot(toml);
        var stems = StrArray(root, "tpfs", "root").Select(s => s.ToLowerInvariant()).ToList();
        if (stems.Count == 0)
            throw Fail("tpfs must not be empty");
        foreach (var s in stems)
            if (!StemPattern.IsMatch(s))
                throw Fail($"tpfs: bad texture stem '{s}'");
        return stems;
    }

    public static WinterCatalog LoadMaterials(string path) => ParseMaterials(ReadFile(path));

    public static IReadOnlyList<string> LoadTextures(string path) => ParseTextures(ReadFile(path));

    private static string ReadFile(string path)
    {
        if (!File.Exists(path))
            throw Fail($"file not found: {path}");
        return File.ReadAllText(path);
    }

    private static TwinTemplate ParseTemplate(TomlTable t)
    {
        var shader = Str(t, "shader", "templates");
        var matbin = Str(t, "matbin", shader);
        var samplers = StrArray(t, "samplers", shader);
        if (samplers.Count == 0)
            throw Fail($"template '{shader}': samplers must not be empty");
        var uvSets = PositiveInt(t, "uv_sets", $"template '{shader}'");

        var ps = new Dictionary<string, float[]>();
        foreach (var (key, value) in Table(t, "params", shader))
            ps[key] = Floats(value, $"template '{shader}' param '{key}'");
        return new TwinTemplate(shader, matbin, samplers, uvSets, ps);
    }

    private static ConversionRule ParseRule(TomlTable r, Dictionary<string, TwinTemplate> templates)
    {
        var source = Str(r, "source", "rules");
        var twin = Str(r, "twin", source);
        if (!templates.TryGetValue(twin, out var template))
            throw Fail($"rule '{source}': twin '{twin}' names no template");
        var sourceSamplers = StrArray(r, "source_samplers", source).ToHashSet();
        if (sourceSamplers.Count == 0)
            throw Fail($"rule '{source}': source_samplers must not be empty");

        var plan = new Dictionary<string, string>();
        foreach (var (key, value) in Table(r, "plan", source))
        {
            if (value is not string v)
                throw Fail($"rule '{source}': plan value of '{key}' must be a string");
            if (!PlanVocabulary.Contains(v) && !sourceSamplers.Contains(v))
                throw Fail($"rule '{source}': plan value '{v}' of '{key}' is neither a plan keyword nor a source sampler");
            plan[key] = v;
        }
        if (!plan.Keys.ToHashSet().SetEquals(template.Samplers))
            throw Fail($"rule '{source}': plan keys do not match the samplers of twin '{twin}'");
        return new ConversionRule(source, twin, sourceSamplers, plan);
    }

    private static TomlTable ParseRoot(string toml)
    {
        try
        {
            return Toml.ToModel(toml);
        }
        catch (TomlException e)
        {
            throw Fail($"invalid TOML: {e.Message}");
        }
    }

    private static InvalidDataException Fail(string message) => new($"{Prefix} {message}");

    private static TomlTable Table(TomlTable parent, string key, string where) =>
        parent.TryGetValue(key, out var o) && o is TomlTable t
            ? t
            : throw Fail($"{where}: missing table [{key}]");

    private static IEnumerable<TomlTable> TableArray(TomlTable parent, string key, bool required)
    {
        if (!parent.TryGetValue(key, out var o))
        {
            if (required) throw Fail($"missing [[{key}]]");
            return Array.Empty<TomlTable>();
        }
        if (o is not TomlTableArray arr || (required && arr.Count == 0))
            throw Fail($"[[{key}]] must be a non-empty array of tables");
        return arr;
    }

    private static string Str(TomlTable t, string key, string where) =>
        t.TryGetValue(key, out var o) && o is string s && s.Length > 0
            ? s
            : throw Fail($"{where}: missing string '{key}'");

    private static int PositiveInt(TomlTable t, string key, string where) =>
        t.TryGetValue(key, out var o) && o is long l && l >= 1 && l <= int.MaxValue
            ? (int)l
            : throw Fail($"{where}: '{key}' must be a positive integer");

    private static string Tif(TomlTable t, string key)
    {
        var s = Str(t, key, "snow_textures");
        if (!s.EndsWith(".tif", StringComparison.OrdinalIgnoreCase))
            throw Fail($"snow_textures: '{key}' must end in .tif");
        return s;
    }

    private static List<string> StrArray(TomlTable t, string key, string where)
    {
        if (!t.TryGetValue(key, out var o) || o is not TomlArray arr)
            throw Fail($"{where}: missing array '{key}'");
        var result = new List<string>();
        foreach (var item in arr)
            result.Add(item as string ?? throw Fail($"{where}: '{key}' must hold strings"));
        return result;
    }

    private static Regex Rx(TomlTable t, string key, RegexOptions options)
    {
        var pattern = Str(t, key, "terrain_retarget");
        try
        {
            return new Regex(pattern, options);
        }
        catch (ArgumentException e)
        {
            throw Fail($"terrain_retarget: '{key}' is not a valid regex: {e.Message}");
        }
    }

    private static float[] Floats(object? value, string where)
    {
        if (value is TomlArray arr)
        {
            if (arr.Count is not (2 or 3))
                throw Fail($"{where}: vector must have 2 or 3 components");
            return arr.Select(v => Scalar(v, where)).ToArray();
        }
        return new[] { Scalar(value, where) };
    }

    private static float Scalar(object? v, string where) => v switch
    {
        double d => (float)d,
        long l => l,
        _ => throw Fail($"{where}: expected a number"),
    };
}
