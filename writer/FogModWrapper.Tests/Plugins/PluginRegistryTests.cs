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
}
