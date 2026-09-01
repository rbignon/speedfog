using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Shared TOML field accessors for the catalogue loaders (TextThemeCatalogLoader,
/// PhantomCatalogLoader, HalloweenDecorLoader): each loader owns its own error
/// prefix, so failures still read as "phantom_skins: ..." / "&lt;theme&gt;: ..." /
/// "halloween decorations: ...", but the int/float/string coercion logic (and its
/// long-vs-int Tomlyn quirk) is written once.
/// </summary>
internal static class TomlHelpers
{
    public static int ToInt(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v))
            throw new InvalidDataException($"{errorPrefix} missing field '{key}'");
        return v switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new InvalidDataException($"{errorPrefix} field '{key}' must be integer, got {v?.GetType().Name}")
        };
    }

    public static int? ToIntOpt(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v))
            return null;
        return v switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new InvalidDataException($"{errorPrefix} field '{key}' must be integer, got {v?.GetType().Name}")
        };
    }

    public static float ToFloat(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v))
            throw new InvalidDataException($"{errorPrefix} missing field '{key}'");
        return v switch
        {
            double d => (float)d,
            long l => l,
            int i => i,
            _ => throw new InvalidDataException($"{errorPrefix} field '{key}' must be numeric, got {v?.GetType().Name}")
        };
    }

    public static float? ToFloatOpt(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v))
            return null;
        return v switch
        {
            double d => (float)d,
            long l => l,
            int i => i,
            _ => throw new InvalidDataException($"{errorPrefix} field '{key}' must be numeric, got {v?.GetType().Name}")
        };
    }

    public static string ToString(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v) || v is not string s)
            throw new InvalidDataException($"{errorPrefix} missing or non-string field '{key}'");
        return s;
    }
}
