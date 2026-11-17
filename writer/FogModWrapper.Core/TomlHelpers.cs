using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Shared TOML field accessors for the catalogue loaders (TextThemeCatalogLoader,
/// PhantomCatalogLoader, HalloweenDecorLoader, RaceItemsCatalog,
/// RaceItemTalismans): each loader owns its own error
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
        return AsInt(v, key, errorPrefix);
    }

    public static int? ToIntOpt(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v))
            return null;
        return AsInt(v, key, errorPrefix);
    }

    /// <summary>A TOML integer (a long in Tomlyn) as an int; one that does not
    /// fit is an invalid catalogue, not an OverflowException.</summary>
    private static int AsInt(object? v, string key, string errorPrefix) => v switch
    {
        long l when l is >= int.MinValue and <= int.MaxValue => (int)l,
        long l => throw new InvalidDataException($"{errorPrefix} field '{key}' must be integer, got out-of-range {l}"),
        int i => i,
        _ => throw new InvalidDataException($"{errorPrefix} field '{key}' must be integer, got {v?.GetType().Name}")
    };

    /// <summary>The tables of the <paramref name="name"/> array, none when
    /// absent. Takes both TOML spellings: [[name]] headers (a TomlTableArray)
    /// and an inline array of inline tables, the empty one included (a
    /// TomlArray), as Python's tomllib does.</summary>
    public static IReadOnlyList<TomlTable> Tables(TomlTable root, string name, string errorPrefix)
    {
        if (!root.TryGetValue(name, out var obj))
            return Array.Empty<TomlTable>();
        if (obj is TomlTableArray tables)
            return tables.ToList();
        if (obj is TomlArray array && array.All(e => e is TomlTable))
            return array.Cast<TomlTable>().ToList();
        throw new InvalidDataException($"{errorPrefix} [[{name}]] must be an array of tables");
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

    /// <summary>Rejects every key of <paramref name="table"/> outside
    /// <paramref name="allowed"/>, so a typo cannot silently drop a field.
    /// <paramref name="where"/> names the table in the message.</summary>
    public static void CheckKeys(TomlTable table, string where, string errorPrefix, params string[] allowed)
    {
        foreach (var key in table.Keys)
        {
            if (!allowed.Contains(key))
                throw new InvalidDataException($"{errorPrefix} {where}: unknown key '{key}'");
        }
    }

    public static string ToString(TomlTable e, string key, string errorPrefix)
    {
        if (!e.TryGetValue(key, out var v) || v is not string s)
            throw new InvalidDataException($"{errorPrefix} missing or non-string field '{key}'");
        return s;
    }
}
