using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

public sealed record SplitZone(
    string Name, string Map, string DisplayName, List<string> Tags,
    string SplitFrom, List<string> Cols, List<string> Enemies);

public sealed record SplitFog(
    string Name, string Map, int Id, string Text, string MakeFrom,
    string ASideArea, string ASideText, string BSideArea, string BSideText);

public sealed record MapSplits(List<SplitZone> Zones, List<SplitFog> Fogs)
{
    public static MapSplits Empty { get; } = new(new List<SplitZone>(), new List<SplitFog>());
}

/// <summary>
/// Loads data/map_splits.toml: synthetic zones and fog gates splitting
/// oversized maps. Consumed by MapSplitsInjector before Graph.Construct.
/// Python's tools/generate_clusters.py reads the same file for cluster
/// generation; both sides must stay in sync (docs/map-splits.md).
/// </summary>
public static class MapSplitsLoader
{
    public static MapSplits Load(string path)
    {
        if (!File.Exists(path))
        {
            Console.WriteLine($"Map splits: no map_splits.toml at {path}, skipping");
            return MapSplits.Empty;
        }
        return Parse(File.ReadAllText(path));
    }

    public static MapSplits Parse(string toml)
    {
        var root = Toml.ToModel(toml);
        var zones = new List<SplitZone>();
        if (root.TryGetValue("zones", out var zonesObj))
        {
            if (zonesObj is not TomlTableArray zoneEntries)
                throw new InvalidDataException("map_splits.toml: zones must be an array of tables");
            foreach (var entry in zoneEntries)
            {
                zones.Add(new SplitZone(
                    RequireString(entry, "name"),
                    RequireString(entry, "map"),
                    RequireString(entry, "display_name"),
                    ReadStringList(entry, "tags"),
                    RequireString(entry, "split_from"),
                    ReadStringList(entry, "cols"),
                    ReadStringList(entry, "enemies")));
            }
        }
        var fogs = new List<SplitFog>();
        if (root.TryGetValue("fogs", out var fogsObj))
        {
            if (fogsObj is not TomlTableArray fogEntries)
                throw new InvalidDataException("map_splits.toml: fogs must be an array of tables");
            foreach (var entry in fogEntries)
            {
                var (asideArea, asideText) = ReadSide(entry, "aside");
                var (bsideArea, bsideText) = ReadSide(entry, "bside");
                if (!entry.TryGetValue("id", out var idObj) || idObj is not long id)
                    throw new InvalidDataException("map_splits.toml: [[fogs]] entry missing integer 'id'");
                fogs.Add(new SplitFog(
                    RequireString(entry, "name"),
                    RequireString(entry, "map"),
                    checked((int)id),
                    RequireString(entry, "text"),
                    RequireString(entry, "make_from"),
                    asideArea, asideText, bsideArea, bsideText));
            }
        }
        return new MapSplits(zones, fogs);
    }

    private static string RequireString(TomlTable entry, string key)
    {
        if (!entry.TryGetValue(key, out var obj) || obj is not string s || string.IsNullOrEmpty(s))
            throw new InvalidDataException($"map_splits.toml: entry missing or non-string '{key}'");
        return s;
    }

    private static List<string> ReadStringList(TomlTable entry, string key)
    {
        var result = new List<string>();
        if (!entry.TryGetValue(key, out var obj))
            return result;
        if (obj is not TomlArray arr)
            throw new InvalidDataException($"map_splits.toml: '{key}' must be an array");
        foreach (var item in arr)
        {
            if (item is not string s)
                throw new InvalidDataException($"map_splits.toml: '{key}' must contain strings");
            result.Add(s);
        }
        return result;
    }

    private static (string Area, string Text) ReadSide(TomlTable entry, string key)
    {
        if (!entry.TryGetValue(key, out var obj) || obj is not TomlTable side)
            throw new InvalidDataException($"map_splits.toml: [[fogs]] entry missing '{key}' table");
        return (RequireString(side, "area"), RequireString(side, "text"));
    }
}
