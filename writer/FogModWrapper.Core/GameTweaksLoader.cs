using Tomlyn;
using Tomlyn.Model;

namespace FogModWrapper;

/// <summary>
/// Game-knowledge tables loaded from <c>data/game_tweaks.toml</c>:
/// FogMod ConfigVars (logic variables required by Graph.Construct) and the
/// event flags forced ON at map load (open gates).
/// </summary>
public sealed record GameTweaks(
    Dictionary<string, string> ConfigVars,
    List<(string MapId, int FlagId, bool On)> StartupFlags);

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

        return new GameTweaks(configVars, startupFlags);
    }

    public static GameTweaks Load(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException(
                $"game_tweaks.toml not found at {path} (required for FogMod ConfigVars)", path);

        var tweaks = Parse(File.ReadAllText(path));
        Console.WriteLine(
            $"Game tweaks: {tweaks.ConfigVars.Count} config vars, " +
            $"{tweaks.StartupFlags.Count} startup flags from {path}");
        return tweaks;
    }
}
