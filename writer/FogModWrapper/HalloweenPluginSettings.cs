using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Parses [plugin.halloween] parameters from graph.json's verbatim plugin
/// table. Strict like WeatherInjector.Parse: unknown keys or wrong types
/// abort the build. `greeters` and `ambushes` take a boolean or a species
/// name (true = the default species). The text theme (TextTheme) only reads `enabled`; the
/// ambient injectors, the jumpscare layer and the darkness gate in
/// Program.cs read the parameters here.
/// </summary>
public static class HalloweenPluginSettings
{
    public enum JumpscareMode { Scare, Strike }

    /// <summary>Enemy standing watch at the anchored exit gates
    /// (AmbientSpawnInjector.GreeterProfile).</summary>
    public enum GreeterSpecies { Untouchable, Wormface }

    /// <summary>Enemy of the decorative ambush packs
    /// (AmbientSpawnInjector.AmbusherProfile).</summary>
    public enum AmbusherSpecies { Skeleton, Basilisk }

    // Config names of each flavor's species. A boolean `true` stands for the
    // first (default) species, so pools written before species existed keep
    // their enemies: the first entry must stay the Settings default
    // (Parse_BooleanSpawnKeysKeepTheDefaultSpecies guards the pairing).
    private static readonly (string Name, GreeterSpecies Species)[] GreeterNames =
    {
        ("untouchable", GreeterSpecies.Untouchable),
        ("wormface", GreeterSpecies.Wormface),
    };
    private static readonly (string Name, AmbusherSpecies Species)[] AmbusherNames =
    {
        ("skeleton", AmbusherSpecies.Skeleton),
        ("basilisk", AmbusherSpecies.Basilisk),
    };

    // Shared by Parse and the Settings record, so a Settings built in code
    // matches an unconfigured [plugin.halloween]. Jumpscares are opt-in:
    // configs enable the plugin for its theme alone.
    public const int DefaultJumpscares = 0;
    public const float DefaultJumpscareDelayMin = 5f;
    public const float DefaultJumpscareDelayMax = 25f;

    public sealed record Settings(
        bool Ambushes, bool Greeters, bool Darkness = false,
        int Jumpscares = DefaultJumpscares,
        JumpscareMode JumpscareMode = JumpscareMode.Scare,
        float JumpscareDelayMin = DefaultJumpscareDelayMin,
        float JumpscareDelayMax = DefaultJumpscareDelayMax,
        GreeterSpecies GreeterSpecies = GreeterSpecies.Untouchable,
        AmbusherSpecies AmbusherSpecies = AmbusherSpecies.Skeleton);

    public static Settings Parse(PluginConfig config)
    {
        bool ambushes = false;
        bool greeters = true;
        var greeterSpecies = GreeterSpecies.Untouchable;
        var ambusherSpecies = AmbusherSpecies.Skeleton;
        bool darkness = false;
        int jumpscares = DefaultJumpscares;
        var jumpscareMode = JumpscareMode.Scare;
        float delayMin = DefaultJumpscareDelayMin;
        float delayMax = DefaultJumpscareDelayMax;
        foreach (var (key, value) in config.Extra)
        {
            switch (key)
            {
                case "ambushes":
                    (ambushes, ambusherSpecies) = ParseFlavor(key, value, AmbusherNames);
                    break;
                case "greeters":
                    (greeters, greeterSpecies) = ParseFlavor(key, value, GreeterNames);
                    break;
                case "darkness":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("halloween: 'darkness' must be a boolean");
                    darkness = value.GetBoolean();
                    break;
                case "jumpscares":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out jumpscares)
                        || jumpscares < 0 || jumpscares > SpeedFogIds.HalloweenJumpscareLayerFlagCount)
                    {
                        throw new InvalidDataException(
                            "halloween: 'jumpscares' must be an integer from 0 to " +
                            SpeedFogIds.HalloweenJumpscareLayerFlagCount);
                    }
                    break;
                case "jumpscare_mode":
                    jumpscareMode = (value.ValueKind == JsonValueKind.String ? value.GetString() : null) switch
                    {
                        "scare" => JumpscareMode.Scare,
                        "strike" => JumpscareMode.Strike,
                        _ => throw new InvalidDataException(
                            "halloween: 'jumpscare_mode' must be \"scare\" or \"strike\""),
                    };
                    break;
                case "jumpscare_delay":
                    (delayMin, delayMax) = ParseDelay(value);
                    break;
                default:
                    throw new InvalidDataException($"halloween: unknown parameter '{key}'");
            }
        }
        return new Settings(
            ambushes, greeters, darkness, jumpscares, jumpscareMode, delayMin, delayMax,
            greeterSpecies, ambusherSpecies);
    }

    /// <summary>A spawn flavor key: a boolean (on with the default species,
    /// or off) or the name of the species to spawn.</summary>
    private static (bool Enabled, T Species) ParseFlavor<T>(
        string key, JsonElement value, (string Name, T Species)[] names)
    {
        if (value.ValueKind is JsonValueKind.True or JsonValueKind.False)
            return (value.GetBoolean(), names[0].Species);
        if (value.ValueKind == JsonValueKind.String)
        {
            var name = value.GetString();
            foreach (var (n, species) in names)
            {
                if (n == name)
                    return (true, species);
            }
        }
        throw new InvalidDataException(
            $"halloween: '{key}' must be a boolean or one of " +
            string.Join(", ", names.Select(n => $"\"{n.Name}\"")));
    }

    private static (float Min, float Max) ParseDelay(JsonElement value)
    {
        const string error = "halloween: 'jumpscare_delay' must be [min, max] seconds with 0 <= min <= max";
        if (value.ValueKind != JsonValueKind.Array || value.GetArrayLength() != 2)
            throw new InvalidDataException(error);
        var bounds = value.EnumerateArray()
            .Select(e => e.ValueKind == JsonValueKind.Number ? e.GetSingle() : throw new InvalidDataException(error))
            .ToArray();
        if (bounds[0] < 0 || bounds[0] > bounds[1])
            throw new InvalidDataException(error);
        return (bounds[0], bounds[1]);
    }
}
