using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// "weather" plugin: forces a fixed weather and optionally freezes the
/// in-game clock at a fixed hour, via a looping event in common.emevd
/// (opt-in via [plugin.weather], see docs/plugins/weather.md).
/// </summary>
public static class WeatherInjector
{
    /// <summary>Config name (snake_case) -> EMEVD Weather enum token
    /// (er-common.emedf.json, instruction 2003[68] arg 0). The parser strips
    /// non-word chars from emedf value names ("Default 2" -> "Default2").
    /// None (-1) and the Unknown 18-23 entries are not exposed.</summary>
    private static readonly IReadOnlyDictionary<string, string> WeatherTokens =
        new Dictionary<string, string>
        {
            ["default"] = "Default",
            ["rain"] = "Rain",
            ["snow"] = "Snow",
            ["windy_rain"] = "WindyRain",
            ["fog"] = "Fog",
            ["cloudless"] = "Cloudless",
            ["flat_clouds"] = "FlatClouds",
            ["puffy_clouds"] = "PuffyClouds",
            ["rainy_clouds"] = "RainyClouds",
            ["windy_fog"] = "WindyFog",
            ["heavy_snow"] = "HeavySnow",
            ["heavy_fog"] = "HeavyFog",
            ["windy_puffy_clouds"] = "WindyPuffyClouds",
            ["default_2"] = "Default2",
            ["default_3"] = "Default3",
            ["rainy_heavy_fog"] = "RainyHeavyFog",
            ["snowy_heavy_fog"] = "SnowyHeavyFog",
            ["scattered_rain"] = "ScatteredRain",
        };

    /// <summary>Validated [plugin.weather] settings.</summary>
    public sealed record Settings(string WeatherName, string WeatherToken, int Hour, bool FreezeTime);

    /// <summary>Parse and validate [plugin.weather] params. Strict: unknown
    /// keys, unknown weather names, wrong types, or an out-of-range hour
    /// abort the build.</summary>
    public static Settings Parse(PluginConfig config)
    {
        string weatherName = "cloudless";
        int hour = 12;
        bool freezeTime = true;

        foreach (var (key, value) in config.Extra)
        {
            switch (key)
            {
                case "weather":
                    if (value.ValueKind != JsonValueKind.String)
                        throw new InvalidDataException("weather: 'weather' must be a string");
                    weatherName = value.GetString()!.ToLowerInvariant();
                    break;
                case "hour":
                    if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out hour))
                        throw new InvalidDataException("weather: 'hour' must be an integer");
                    break;
                case "freeze_time":
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new InvalidDataException("weather: 'freeze_time' must be a boolean");
                    freezeTime = value.GetBoolean();
                    break;
                default:
                    throw new InvalidDataException($"weather: unknown parameter '{key}'");
            }
        }

        if (!WeatherTokens.TryGetValue(weatherName, out var token))
            throw new InvalidDataException(
                $"weather: unknown weather '{weatherName}'; accepted: "
                + string.Join(", ", WeatherTokens.Keys));
        if (hour is < 0 or > 23)
            throw new InvalidDataException($"weather: 'hour' must be in 0-23, got {hour}");

        return new Settings(weatherName, token, hour, freezeTime);
    }
}
