using FogModWrapper.Models;
using FogModWrapper.Pipeline;

namespace FogModWrapper.Plugins.Weather;

/// <summary>[plugin.weather]: force a fixed weather and pin the clock hour
/// (see docs/plugins/weather.md).</summary>
internal sealed class WeatherPlugin : SpeedFogPlugin
{
    private readonly WeatherInjector.Settings _settings;

    public WeatherPlugin(PluginConfig config) => _settings = WeatherInjector.Parse(config);

    public override string Name => "weather";

    public override void OnCommonEmevd(Context ctx) =>
        WeatherInjector.InjectEmevdEvent(ctx.CommonEmevd, ctx.Events, _settings);
}
