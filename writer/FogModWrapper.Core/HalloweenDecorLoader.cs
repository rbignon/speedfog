using System.Text.RegularExpressions;
using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Loads and validates the Halloween gate decoration catalogue
/// (data/plugins/halloween_decorations.toml). Mirrors TextThemeCatalogLoader's
/// Tomlyn idiom. Returns an empty catalogue when the file is absent or has no
/// active entries, so GateDecorInjector becomes a silent no-op.
/// </summary>
public static class HalloweenDecorLoader
{
    private const string Prefix = "halloween decorations:";
    private static readonly Regex ModelPattern = new(@"^AEG\d{3}_\d{3}$");

    public static DecorCatalog Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException($"{Prefix} top-level TOML must be a table");

        var entries = new List<DecorEntry>();
        if (root.TryGetValue("entries", out var eObj))
        {
            if (eObj is not TomlTableArray eArr)
                throw new InvalidDataException($"{Prefix} [[entries]] must be an array of tables");
            foreach (var entry in eArr)
                entries.Add(ParseEntry(entry));
        }

        return new DecorCatalog(entries);
    }

    public static DecorCatalog Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"{Prefix} no catalogue at {path}, skipping");
            return DecorCatalog.Empty;
        }

        var catalog = Parse(File.ReadAllText(path));
        Console.WriteLine($"{Prefix} loaded {catalog.Entries.Count} entries from {path}");
        return catalog;
    }

    private static DecorEntry ParseEntry(TomlTable e)
    {
        var model = ToString(e, "model");
        if (!ModelPattern.IsMatch(model))
            throw new InvalidDataException($"{Prefix} 'model' must match AEGnnn_nnn, got '{model}'");

        int count = ToInt(e, "count");
        if (count < 1 || count > 5)
            throw new InvalidDataException($"{Prefix} 'count' must be between 1 and 5, got {count}");

        float minRadius = ToFloat(e, "min_radius");
        float maxRadius = ToFloat(e, "max_radius");
        if (!(minRadius > 0f && minRadius <= maxRadius))
            throw new InvalidDataException(
                $"{Prefix} 'min_radius' must be > 0 and <= 'max_radius' " +
                $"(got min_radius={minRadius}, max_radius={maxRadius})");

        float yOffset = ToFloatOpt(e, "y_offset") ?? 0f;
        int sfxDummy = ToIntOpt(e, "sfx_dummy") ?? 100;
        int sfxId = ToIntOpt(e, "sfx_id") ?? 0;

        return new DecorEntry(model, count, minRadius, maxRadius, yOffset, sfxDummy, sfxId);
    }

    private static int ToInt(TomlTable e, string key) => TomlHelpers.ToInt(e, key, Prefix);

    private static int? ToIntOpt(TomlTable e, string key) => TomlHelpers.ToIntOpt(e, key, Prefix);

    private static float ToFloat(TomlTable e, string key) => TomlHelpers.ToFloat(e, key, Prefix);

    private static float? ToFloatOpt(TomlTable e, string key) => TomlHelpers.ToFloatOpt(e, key, Prefix);

    private static string ToString(TomlTable e, string key) => TomlHelpers.ToString(e, key, Prefix);
}

/// <summary>
/// One catalogue entry: a decoration model to place near a dungeon entrance
/// gate. SfxId &lt;= 0 means geometry only, no EMEVD SFX event.
/// </summary>
public sealed record DecorEntry(
    string Model, int Count, float MinRadius, float MaxRadius,
    float YOffset, int SfxDummyPoly, int SfxId);

/// <summary>Parsed Halloween gate decoration catalogue.</summary>
public sealed record DecorCatalog(IReadOnlyList<DecorEntry> Entries)
{
    public bool IsEmpty => Entries.Count == 0;

    public static DecorCatalog Empty { get; } = new(new List<DecorEntry>());
}
