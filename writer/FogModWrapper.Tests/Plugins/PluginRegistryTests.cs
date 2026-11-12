using System.Text.Json;
using FogModWrapper.Models;
using FogModWrapper.Plugins;
using Xunit;

namespace FogModWrapper.Tests.Plugins;

public class PluginRegistryTests
{
    private static PluginConfig Enabled(string extraJson = "{}") => new()
    {
        Enabled = true,
        Extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extraJson)!,
    };

    [Fact]
    public void CreateEnabled_ReturnsEnabledKnownPluginsInExecutionOrder()
    {
        var graph = new GraphData
        {
            Plugins = new()
            {
                ["halloween"] = Enabled(),
                ["winter"] = Enabled(),
                ["summer"] = Enabled(),
                ["weather"] = new PluginConfig { Enabled = false },
            },
        };

        Assert.Equal(new[] { "summer", "halloween" },
            PluginRegistry.CreateEnabled(graph).Select(p => p.Name));
    }

    [Fact]
    public void CreateEnabled_AllEnabled_RunsSummerWeatherHalloween()
    {
        var graph = new GraphData
        {
            Plugins = new()
            {
                ["halloween"] = Enabled(),
                ["weather"] = Enabled(),
                ["summer"] = Enabled(),
            },
        };

        Assert.Equal(new[] { "summer", "weather", "halloween" },
            PluginRegistry.CreateEnabled(graph).Select(p => p.Name));
    }

    [Fact]
    public void CreateEnabled_NullPlugins_ReturnsEmpty()
    {
        var graph = new GraphData { Plugins = null! };

        Assert.Empty(PluginRegistry.CreateEnabled(graph));
    }

    [Fact]
    public void CreateEnabled_InvalidHalloweenConfig_Throws()
    {
        var graph = new GraphData
        {
            Plugins = new() { ["halloween"] = Enabled("""{"jumpscare_mode": "foo"}""") },
        };

        Assert.Throws<InvalidDataException>(() => PluginRegistry.CreateEnabled(graph));
    }

    [Fact]
    public void CreateEnabled_InvalidWeatherConfig_Throws()
    {
        var graph = new GraphData
        {
            Plugins = new() { ["weather"] = Enabled("""{"hour": "noon"}""") },
        };

        Assert.Throws<InvalidDataException>(() => PluginRegistry.CreateEnabled(graph));
    }

    [Fact]
    public void Creates_race_items_when_enabled()
    {
        var graph = new GraphData { Plugins = new() { ["race_items"] = Enabled() } };

        var plugins = PluginRegistry.CreateEnabled(graph);

        Assert.Equal(new[] { "race_items" }, plugins.Select(p => p.Name));
        Assert.Null(plugins[0].TextThemeName);
    }

    [Fact]
    public void Ignores_race_items_when_disabled()
    {
        var graph = new GraphData
        {
            Plugins = new() { ["race_items"] = new PluginConfig { Enabled = false } },
        };

        Assert.Empty(PluginRegistry.CreateEnabled(graph));
    }

    [Fact]
    public void RaceItemsPlugin_rejects_unknown_setting()
    {
        var graph = new GraphData
        {
            Plugins = new() { ["race_items"] = Enabled("""{"hammer": 1}""") },
        };

        Assert.Throws<InvalidDataException>(() => PluginRegistry.CreateEnabled(graph));
    }

    [Fact]
    public void CreateEnabled_RaceItemsRunsAfterHalloween()
    {
        var graph = new GraphData
        {
            Plugins = new() { ["race_items"] = Enabled(), ["halloween"] = Enabled() },
        };

        Assert.Equal(new[] { "halloween", "race_items" },
            PluginRegistry.CreateEnabled(graph).Select(p => p.Name));
    }
}
