using FogModWrapper.Models;
using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Loads and validates the phantom skins catalog (data/phantom_skins.toml).
/// </summary>
public static class PhantomCatalogLoader
{
    public const int IdRangeStart = 1450700;
    public const int IdRangeEnd = 1450799;

    /// <summary>
    /// Parses a TOML string into a list of PhantomSkin records.
    /// Throws InvalidDataException on malformed content or validation errors.
    /// </summary>
    public static List<PhantomSkin> Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException("phantom_skins: top-level TOML must be a table");

        if (!root.TryGetValue("skins", out var skinsObj) || skinsObj is not TomlTableArray skinsArray)
            throw new InvalidDataException("phantom_skins: missing or invalid [[skins]] array");

        var skins = new List<PhantomSkin>(skinsArray.Count);
        foreach (var entry in skinsArray)
        {
            skins.Add(ParseSkin(entry));
        }

        Validate(skins);
        return skins;
    }

    /// <summary>
    /// Loads the catalog from a file path. Returns an empty list when the file
    /// is absent (skin injection becomes a no-op).
    /// </summary>
    public static List<PhantomSkin> Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Phantom skins: no catalog at {path}, skipping");
            return new List<PhantomSkin>();
        }

        var toml = File.ReadAllText(path);
        var skins = Parse(toml);
        Console.WriteLine($"Phantom skins: loaded {skins.Count} entries from {path}");
        return skins;
    }

    private static PhantomSkin ParseSkin(TomlTable entry)
    {
        int id = ToInt(entry, "id");
        string name = ToString(entry, "name");
        string displayName = ToString(entry, "display_name");
        var (r, g, b) = ParseEdgeColor(entry);
        float edgePower = ToFloat(entry, "edge_power");
        float glowScale = ToFloat(entry, "glow_scale");
        float alpha = ToFloat(entry, "alpha");

        return new PhantomSkin(id, name, displayName, r, g, b, edgePower, glowScale, alpha);
    }

    private static (byte r, byte g, byte b) ParseEdgeColor(TomlTable entry)
    {
        if (!entry.TryGetValue("edge_color", out var raw) || raw is not TomlArray arr)
            throw new InvalidDataException($"phantom_skins: skin missing edge_color array");

        if (arr.Count != 3)
            throw new InvalidDataException($"phantom_skins: edge_color must have exactly 3 elements, got {arr.Count}");

        return (ToByte(arr[0], "edge_color[0]"), ToByte(arr[1], "edge_color[1]"), ToByte(arr[2], "edge_color[2]"));
    }

    private static void Validate(List<PhantomSkin> skins)
    {
        var seenIds = new HashSet<int>();
        var seenNames = new HashSet<string>();

        foreach (var skin in skins)
        {
            if (skin.Id < IdRangeStart || skin.Id > IdRangeEnd)
                throw new InvalidDataException(
                    $"phantom_skins: skin '{skin.Name}' id {skin.Id} outside reserved range {IdRangeStart}-{IdRangeEnd}");

            if (!seenIds.Add(skin.Id))
                throw new InvalidDataException($"phantom_skins: duplicate id {skin.Id}");

            if (!seenNames.Add(skin.Name))
                throw new InvalidDataException($"phantom_skins: duplicate name '{skin.Name}'");

            if (string.IsNullOrWhiteSpace(skin.Name))
                throw new InvalidDataException($"phantom_skins: skin id {skin.Id} has empty name");
        }
    }

    private static int ToInt(TomlTable entry, string key)
    {
        if (!entry.TryGetValue(key, out var v))
            throw new InvalidDataException($"phantom_skins: missing field '{key}'");
        return v switch
        {
            long l => checked((int)l),
            int i => i,
            _ => throw new InvalidDataException($"phantom_skins: field '{key}' must be integer, got {v?.GetType().Name}")
        };
    }

    private static string ToString(TomlTable entry, string key)
    {
        if (!entry.TryGetValue(key, out var v) || v is not string s)
            throw new InvalidDataException($"phantom_skins: missing or non-string field '{key}'");
        return s;
    }

    private static float ToFloat(TomlTable entry, string key)
    {
        if (!entry.TryGetValue(key, out var v))
            throw new InvalidDataException($"phantom_skins: missing field '{key}'");
        return v switch
        {
            double d => (float)d,
            long l => l,
            int i => i,
            _ => throw new InvalidDataException($"phantom_skins: field '{key}' must be number, got {v?.GetType().Name}")
        };
    }

    private static byte ToByte(object? v, string label)
    {
        long n = v switch
        {
            long l => l,
            int i => i,
            _ => throw new InvalidDataException($"phantom_skins: {label} must be integer, got {v?.GetType().Name}")
        };
        if (n < 0 || n > 255)
            throw new InvalidDataException($"phantom_skins: {label} out of range 0-255: {n}");
        return (byte)n;
    }
}
