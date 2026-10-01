using System.Text.Json;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class HalloweenPluginSettingsTests
{
    private static PluginConfig Config(string extraJson = "{}")
    {
        var extra = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(extraJson)!;
        return new PluginConfig { Enabled = true, Extra = extra };
    }

    [Fact]
    public void Parse_DefaultsAmbushesOff()
        => Assert.False(HalloweenPluginSettings.Parse(Config()).Ambushes);

    [Fact]
    public void Parse_ReadsAmbushesTrue()
        => Assert.True(HalloweenPluginSettings.Parse(Config("""{"ambushes": true}""")).Ambushes);

    [Fact]
    public void Parse_DefaultsGreetersOn()
        => Assert.True(HalloweenPluginSettings.Parse(Config()).Greeters);

    [Fact]
    public void Parse_ReadsGreetersFalse()
        => Assert.False(HalloweenPluginSettings.Parse(Config("""{"greeters": false}""")).Greeters);

    [Fact]
    public void Parse_RejectsUnknownKey()
        => Assert.Throws<InvalidDataException>(
            () => HalloweenPluginSettings.Parse(Config("""{"embushes": true}""")));

    [Fact]
    public void Parse_RejectsWrongType()
        => Assert.Throws<InvalidDataException>(
            () => HalloweenPluginSettings.Parse(Config("""{"ambushes": "yes"}""")));

    [Fact]
    public void Parse_RejectsWrongGreetersType()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => HalloweenPluginSettings.Parse(Config("""{"greeters": 0}""")));
        Assert.Contains("'greeters' must be a boolean", ex.Message);
    }
}
