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
    public void Parse_DefaultsDarknessOff()
        => Assert.False(HalloweenPluginSettings.Parse(Config()).Darkness);

    [Fact]
    public void Parse_ReadsDarknessTrue()
        => Assert.True(HalloweenPluginSettings.Parse(Config("""{"darkness": true}""")).Darkness);

    [Fact]
    public void Parse_RejectsWrongDarknessType()
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => HalloweenPluginSettings.Parse(Config("""{"darkness": "on"}""")));
        Assert.Contains("'darkness' must be a boolean", ex.Message);
    }

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

    [Fact]
    public void Parse_UnconfiguredMatchesTheRecordDefaults()
        // Code that builds Settings directly (tests, the darkness gate) must
        // see the same jumpscare defaults as an unconfigured seed.
        => Assert.Equal(
            new HalloweenPluginSettings.Settings(Ambushes: false, Greeters: true),
            HalloweenPluginSettings.Parse(Config()));

    [Fact]
    public void Parse_ReadsJumpscareKeys()
    {
        var s = HalloweenPluginSettings.Parse(Config(
            """{"jumpscares": 4, "jumpscare_mode": "strike", "jumpscare_delay": [2.5, 10]}"""));
        Assert.Equal(4, s.Jumpscares);
        Assert.Equal(HalloweenPluginSettings.JumpscareMode.Strike, s.JumpscareMode);
        Assert.Equal(2.5f, s.JumpscareDelayMin);
        Assert.Equal(10f, s.JumpscareDelayMax);
    }

    [Theory]
    [InlineData("""{"jumpscares": 0}""")]
    [InlineData("""{"jumpscares": 10}""")]
    [InlineData("""{"jumpscare_delay": [0, 0]}""")]
    [InlineData("""{"jumpscare_delay": [7, 7]}""")]
    public void Parse_AcceptsJumpscareBounds(string json)
        => HalloweenPluginSettings.Parse(Config(json));

    [Theory]
    [InlineData("""{"jumpscares": -1}""", "'jumpscares'")]
    [InlineData("""{"jumpscares": 11}""", "'jumpscares'")]
    [InlineData("""{"jumpscares": 2.5}""", "'jumpscares'")]
    [InlineData("""{"jumpscares": "3"}""", "'jumpscares'")]
    [InlineData("""{"jumpscare_mode": "boo"}""", "'jumpscare_mode'")]
    [InlineData("""{"jumpscare_mode": 1}""", "'jumpscare_mode'")]
    [InlineData("""{"jumpscare_delay": 5}""", "'jumpscare_delay'")]
    [InlineData("""{"jumpscare_delay": [5]}""", "'jumpscare_delay'")]
    [InlineData("""{"jumpscare_delay": [5, 10, 15]}""", "'jumpscare_delay'")]
    [InlineData("""{"jumpscare_delay": [5, "x"]}""", "'jumpscare_delay'")]
    [InlineData("""{"jumpscare_delay": [-1, 5]}""", "'jumpscare_delay'")]
    [InlineData("""{"jumpscare_delay": [10, 5]}""", "'jumpscare_delay'")]
    public void Parse_RejectsInvalidJumpscareValues(string json, string key)
    {
        var ex = Assert.Throws<InvalidDataException>(
            () => HalloweenPluginSettings.Parse(Config(json)));
        Assert.Contains(key, ex.Message);
    }
}
