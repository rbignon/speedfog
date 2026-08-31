using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// Parses [plugin.halloween] parameters from graph.json's verbatim plugin
/// table. Strict like WeatherInjector.Parse: unknown keys or wrong types
/// abort the build. The text theme (TextTheme) only reads `enabled`; the
/// ambient injectors read the parameters here.
/// </summary>
public static class HalloweenPluginSettings
{
    public sealed record Settings(bool Ambushes);

    public static Settings Parse(PluginConfig config)
    {
        bool ambushes = false;
        foreach (var (key, value) in config.Extra)
        {
            switch (key)
            {
                case "ambushes":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("halloween: 'ambushes' must be a boolean");
                    ambushes = value.GetBoolean();
                    break;
                default:
                    throw new InvalidDataException($"halloween: unknown parameter '{key}'");
            }
        }
        return new Settings(ambushes);
    }
}
