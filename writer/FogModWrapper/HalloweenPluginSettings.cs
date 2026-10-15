using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Parses [plugin.halloween] parameters from graph.json's verbatim plugin
/// table. Strict like WeatherInjector.Parse: unknown keys or wrong types
/// abort the build. The text theme (TextTheme) only reads `enabled`; the
/// ambient injectors, the jumpscare layer and the darkness gate in
/// Program.cs read the parameters here.
/// </summary>
public static class HalloweenPluginSettings
{
    public enum JumpscareMode { Scare, Strike }

    // Shared by Parse and the Settings record, so a Settings built in code
    // matches an unconfigured [plugin.halloween].
    public const int DefaultJumpscares = 3;
    public const float DefaultJumpscareDelayMin = 5f;
    public const float DefaultJumpscareDelayMax = 25f;

    public sealed record Settings(
        bool Ambushes, bool Greeters, bool Darkness = false,
        int Jumpscares = DefaultJumpscares,
        JumpscareMode JumpscareMode = JumpscareMode.Scare,
        float JumpscareDelayMin = DefaultJumpscareDelayMin,
        float JumpscareDelayMax = DefaultJumpscareDelayMax);

    public static Settings Parse(PluginConfig config)
    {
        bool ambushes = false;
        bool greeters = true;
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
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("halloween: 'ambushes' must be a boolean");
                    ambushes = value.GetBoolean();
                    break;
                case "greeters":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("halloween: 'greeters' must be a boolean");
                    greeters = value.GetBoolean();
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
        return new Settings(ambushes, greeters, darkness, jumpscares, jumpscareMode, delayMin, delayMax);
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
