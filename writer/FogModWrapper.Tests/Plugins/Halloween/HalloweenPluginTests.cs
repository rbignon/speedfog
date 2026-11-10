using System.Text.Json;
using FogModWrapper.Models;
using FogModWrapper.Plugins.Halloween;
using Xunit;

namespace FogModWrapper.Tests.Plugins.Halloween;

public class HalloweenPluginTests
{
    [Fact]
    public void PlaceableSettings_WithoutRegulationRows_DisablesGreetersAndAmbushes()
    {
        // The regulation hook never ran (regulation.bin unavailable): the
        // greeter and ambusher NpcParam rows do not exist, so the MSB pass
        // must not place parts pointing at them.
        var config = new PluginConfig
        {
            Enabled = true,
            Extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(
                """{"greeters": true, "ambushes": true}""")!,
        };
        var plugin = new HalloweenPlugin(config);

        var settings = plugin.PlaceableSettings();

        Assert.False(settings.Greeters);
        Assert.False(settings.Ambushes);
    }
}
