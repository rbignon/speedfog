using Tomlyn;
using Tomlyn.Model;
using FogModWrapper.Models;

namespace FogModWrapper;

public sealed record TorrentArena(string Map, List<string> Collisions);
public sealed record SpiritspringRemoval(
    string Map, float X, float Y, float Z, string RequiredZone);
public sealed record StakeRemoval(string Map, string Name);

/// <summary>
/// Game-knowledge tables loaded from <c>data/game_tweaks.toml</c>:
/// FogMod ConfigVars (logic variables required by Graph.Construct) and the
/// event flags forced ON at map load (open gates).
/// </summary>
public sealed record GameTweaks(
    Dictionary<string, string> ConfigVars,
    List<(string MapId, int FlagId, bool On)> StartupFlags,
    List<TorrentArena> TorrentArenas,
    List<SpiritspringRemoval> SpiritspringRemovals,
    List<RemoveEntity> RemoveEntities,
    List<StakeRemoval> StakeRemovals);

/// <summary>
/// Loads and validates <c>data/game_tweaks.toml</c>. Unlike optional
/// overrides (opensplit), this file is required: FogMod's Graph.Construct
/// fails without the ConfigVars.
/// </summary>
public static class GameTweaksLoader
{
    public static GameTweaks Parse(string toml)
    {
        var model = Toml.ToModel(toml);
        if (model is not TomlTable root)
            throw new InvalidDataException("game_tweaks.toml: not a TOML table");

        // FogMod's AnnotationData.ConfigVars wants "TRUE"/"FALSE" strings
        var configVars = new Dictionary<string, string>();
        if (root.TryGetValue("config_vars", out var varsObj))
        {
            if (varsObj is not TomlTable vars)
                throw new InvalidDataException("game_tweaks.toml: [config_vars] must be a table");
            foreach (var (name, value) in vars)
            {
                if (value is not bool b)
                    throw new InvalidDataException(
                        $"game_tweaks.toml: config_vars.{name} must be a boolean");
                configVars[name] = b ? "TRUE" : "FALSE";
            }
        }
        if (configVars.Count == 0)
            throw new InvalidDataException(
                "game_tweaks.toml: [config_vars] is missing or empty; " +
                "Graph.Construct requires the FogMod logic variables");

        var startupFlags = new List<(string, int, bool)>();
        if (root.TryGetValue("startup_flags", out var flagsObj))
        {
            if (flagsObj is not TomlTableArray entries)
                throw new InvalidDataException(
                    "game_tweaks.toml: startup_flags must be an array of tables");
            foreach (var entry in entries)
            {
                if (!entry.TryGetValue("map", out var mapObj)
                    || mapObj is not string map || string.IsNullOrEmpty(map))
                    throw new InvalidDataException(
                        "game_tweaks.toml: [[startup_flags]] entry missing 'map'");
                if (!entry.TryGetValue("flag", out var flagObj) || flagObj is not long flag)
                    throw new InvalidDataException(
                        $"game_tweaks.toml: startup_flags for {map} missing integer 'flag'");
                bool on = true;
                if (entry.TryGetValue("on", out var onObj))
                {
                    if (onObj is not bool b)
                        throw new InvalidDataException(
                            $"game_tweaks.toml: startup_flags for {map}: 'on' must be a boolean");
                    on = b;
                }
                startupFlags.Add((map, checked((int)flag), on));
            }
        }

        return new GameTweaks(
            configVars, startupFlags,
            ParseTorrentArenas(root),
            ParseSpiritspringRemovals(root),
            ParseRemoveEntities(root),
            ParseStakeRemovals(root));
    }

    public static GameTweaks Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"game_tweaks.toml not found at {path} (required for FogMod ConfigVars)", path);

        var tweaks = Parse(File.ReadAllText(path));
        Console.WriteLine(
            $"Game tweaks: {tweaks.ConfigVars.Count} config vars, " +
            $"{tweaks.StartupFlags.Count} startup flags, " +
            $"{tweaks.TorrentArenas.Count} torrent arenas, " +
            $"{tweaks.SpiritspringRemovals.Count} spiritspring removals, " +
            $"{tweaks.RemoveEntities.Count} remove entities, " +
            $"{tweaks.StakeRemovals.Count} stake removals from {path}");
        return tweaks;
    }

    private static string RequireString(TomlTable entry, string section, string key)
    {
        if (!entry.TryGetValue(key, out var obj) || obj is not string s || string.IsNullOrEmpty(s))
            throw new InvalidDataException(
                $"game_tweaks.toml: [[{section}]] entry missing '{key}'");
        return s;
    }

    private static TomlTableArray? Section(TomlTable root, string section)
    {
        if (!root.TryGetValue(section, out var obj))
            return null;
        if (obj is not TomlTableArray entries)
            throw new InvalidDataException(
                $"game_tweaks.toml: {section} must be an array of tables");
        return entries;
    }

    private static List<TorrentArena> ParseTorrentArenas(TomlTable root)
    {
        var result = new List<TorrentArena>();
        var entries = Section(root, "torrent_arenas");
        if (entries == null)
            return result;
        foreach (var entry in entries)
        {
            var map = RequireString(entry, "torrent_arenas", "map");
            if (!entry.TryGetValue("collisions", out var colsObj)
                || colsObj is not TomlArray cols || cols.Count == 0)
                throw new InvalidDataException(
                    $"game_tweaks.toml: torrent_arenas for {map}: "
                    + "'collisions' must be a non-empty array");
            var names = new List<string>();
            foreach (var c in cols)
            {
                if (c is not string s || string.IsNullOrEmpty(s))
                    throw new InvalidDataException(
                        $"game_tweaks.toml: torrent_arenas for {map}: "
                        + "'collisions' must contain strings");
                names.Add(s);
            }
            result.Add(new TorrentArena(map, names));
        }
        return result;
    }

    private static List<SpiritspringRemoval> ParseSpiritspringRemovals(TomlTable root)
    {
        var result = new List<SpiritspringRemoval>();
        var entries = Section(root, "spiritspring_removals");
        if (entries == null)
            return result;
        foreach (var entry in entries)
        {
            var map = RequireString(entry, "spiritspring_removals", "map");
            if (!entry.TryGetValue("position", out var posObj)
                || posObj is not TomlArray pos || pos.Count != 3)
                throw new InvalidDataException(
                    $"game_tweaks.toml: spiritspring_removals for {map}: "
                    + "'position' must be an array of 3 numbers");
            var xyz = new float[3];
            for (int i = 0; i < 3; i++)
            {
                xyz[i] = pos[i] switch
                {
                    double d => (float)d,
                    long l => l,
                    _ => throw new InvalidDataException(
                        $"game_tweaks.toml: spiritspring_removals for {map}: "
                        + "'position' must contain numbers"),
                };
            }
            result.Add(new SpiritspringRemoval(
                map, xyz[0], xyz[1], xyz[2],
                RequireString(entry, "spiritspring_removals", "required_zone")));
        }
        return result;
    }

    private static List<RemoveEntity> ParseRemoveEntities(TomlTable root)
    {
        var result = new List<RemoveEntity>();
        var entries = Section(root, "remove_entities");
        if (entries == null)
            return result;
        foreach (var entry in entries)
        {
            var map = RequireString(entry, "remove_entities", "map");
            if (!entry.TryGetValue("entity_id", out var idObj) || idObj is not long id)
                throw new InvalidDataException(
                    $"game_tweaks.toml: remove_entities for {map}: missing integer 'entity_id'");
            bool matchGroup = false;
            if (entry.TryGetValue("match_group", out var mgObj))
            {
                if (mgObj is not bool b)
                    throw new InvalidDataException(
                        $"game_tweaks.toml: remove_entities for {map}: 'match_group' must be a boolean");
                matchGroup = b;
            }
            result.Add(new RemoveEntity
            {
                Map = map,
                EntityId = checked((int)id),
                MatchGroup = matchGroup,
            });
        }
        return result;
    }

    private static List<StakeRemoval> ParseStakeRemovals(TomlTable root)
    {
        var result = new List<StakeRemoval>();
        var entries = Section(root, "stake_removals");
        if (entries == null)
            return result;
        foreach (var entry in entries)
        {
            result.Add(new StakeRemoval(
                RequireString(entry, "stake_removals", "map"),
                RequireString(entry, "stake_removals", "name")));
        }
        return result;
    }
}
