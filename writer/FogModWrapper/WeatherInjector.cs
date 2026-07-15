using System.Text.Json;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// "weather" plugin: forces a fixed weather and optionally pins the
/// in-game clock to a fixed hour, via a looping event in common.emevd
/// (opt-in via [plugin.weather], see docs/plugins/weather.md).
/// </summary>
public static class WeatherInjector
{
    private static readonly int EVENT_ID = SpeedFogIds.WeatherEvents.Base;

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

    /// <summary>Re-application period. Insurance against vanilla events and
    /// cutscenes that change weather or time, and the time-lock resolution:
    /// the hour is pinned by periodic SetCurrentTime rather than
    /// FreezeTime(true), because a frozen clock keeps engine flag 2200
    /// ("world clock stopped") permanently ON, which breaks external tools
    /// reading it as a loading-screen indicator (the racing mod's zone
    /// reveal). 10 real seconds is ~3-4 in-game minutes of drift before the
    /// snap-back: invisible at noon, and night stays unreachable.
    /// Re-applications are idempotent, hence invisible when nothing
    /// drifted.</summary>
    private const int REAPPLY_INTERVAL_SECONDS = 10;

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

    /// <summary>Instruction lines of the weather event body, in order.
    /// Pure so tests can assert the sequence without an Events parser.</summary>
    public static List<string> BuildEventBody(Settings s)
    {
        // Apply, wait, restart: the same periodic-reapply idiom as FogRando's
        // "scale" template (data/fogevents.txt). EMEVD Goto/Label only jumps
        // FORWARD (every vanilla unconditional goto does; vanilla and FogMod
        // loops use EventEndType.Restart), so a backward Goto would silently
        // end the event instead of looping.
        var lines = new List<string>();
        if (s.FreezeTime)
        {
            // Re-applying the same hour is a visual no-op, so any drift (the
            // running clock, a cutscene, the grace "pass time" menu) is
            // corrected within one interval.
            lines.Add($"SetCurrentTime({s.Hour}, 0, 0, false, false, false, 0, 0, 0)");
        }
        lines.Add($"ChangeWeather(Weather.{s.WeatherToken}, -1, true)");
        lines.Add($"WaitFixedTimeSeconds({REAPPLY_INTERVAL_SECONDS})");
        lines.Add("EndUnconditionally(EventEndType.Restart)");
        return lines;
    }

    /// <summary>Create the looping weather event in common.emevd and
    /// register it in event 0.</summary>
    public static void InjectEmevdEvent(EMEVD commonEmevd, Events events, Settings settings)
    {
        var initEvent = commonEmevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine("Warning: Event 0 not found in common.emevd, skipping weather event");
            return;
        }

        var evt = new EMEVD.Event(EVENT_ID);
        foreach (var line in BuildEventBody(settings))
        {
            evt.Instructions.Add(events.ParseAdd(line));
        }
        commonEmevd.Events.Add(evt);
        initEvent.Instructions.Add(EmevdHelper.InitializeEvent(EVENT_ID));

        Console.WriteLine(
            $"Weather plugin: event {EVENT_ID} (weather={settings.WeatherName}, " +
            $"hour={(settings.FreezeTime ? settings.Hour.ToString() : "not pinned")}, " +
            $"reapply every {REAPPLY_INTERVAL_SECONDS}s)");
    }
}
