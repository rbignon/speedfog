using FogModWrapper.Models;
using FogModWrapper.Plugins.Halloween;
using FogModWrapper.Plugins.Summer;
using FogModWrapper.Plugins.Weather;

namespace FogModWrapper.Plugins;

/// <summary>The plugins FogModWrapper knows, in execution order.</summary>
internal static class PluginRegistry
{
    // Execution order at every hook. Weather before halloween keeps their
    // common.emevd events in their historical order (event order is part of
    // the file bytes); summer before halloween lets the halloween text theme
    // overwrite colliding summer entries.
    private static readonly (string Name, Func<PluginConfig, SpeedFogPlugin> Create)[] Known =
    {
        ("summer", _ => new SummerPlugin()),
        ("weather", config => new WeatherPlugin(config)),
        ("halloween", config => new HalloweenPlugin(config)),
    };

    /// <summary>
    /// One plugin object per enabled, known [plugin.*] table of graph.json,
    /// in execution order, each named after its table. Unknown names are
    /// ignored. Plugins parse their settings on creation, so an invalid table
    /// fails here.
    /// </summary>
    public static List<SpeedFogPlugin> CreateEnabled(GraphData graph) =>
        Known
            .Where(k => graph.IsPluginEnabled(k.Name))
            .Select(k =>
            {
                var plugin = k.Create(graph.Plugins[k.Name]);
                plugin.Name = k.Name;
                return plugin;
            })
            .ToList();
}
