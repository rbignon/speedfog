using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class WeatherInjectorTests
{
    /// <summary>Build a PluginConfig the same way production does: through
    /// GraphLoader, so Extra contains real JsonElements.</summary>
    private static PluginConfig LoadWeatherConfig(string extraParamsJson = "")
    {
        var json = "{\"version\": \"4.4\", \"seed\": 1, \"plugins\": {\"weather\": {\"enabled\": true"
                   + extraParamsJson + "}}}";
        return GraphLoader.Parse(json).Plugins["weather"];
    }

    // --- Parse ---

    [Fact]
    public void Parse_NoParams_AppliesDefaults()
    {
        var s = WeatherInjector.Parse(LoadWeatherConfig());

        Assert.Equal("cloudless", s.WeatherName);
        Assert.Equal("Cloudless", s.WeatherToken);
        Assert.Equal(12, s.Hour);
        Assert.True(s.FreezeTime);
    }

    [Fact]
    public void Parse_ExplicitParams_Applied()
    {
        var s = WeatherInjector.Parse(LoadWeatherConfig(
            ", \"weather\": \"puffy_clouds\", \"hour\": 18, \"freeze_time\": false"));

        Assert.Equal("puffy_clouds", s.WeatherName);
        Assert.Equal("PuffyClouds", s.WeatherToken);
        Assert.Equal(18, s.Hour);
        Assert.False(s.FreezeTime);
    }

    [Fact]
    public void Parse_WeatherName_CaseInsensitive()
    {
        var s = WeatherInjector.Parse(LoadWeatherConfig(", \"weather\": \"Heavy_Snow\""));
        Assert.Equal("HeavySnow", s.WeatherToken);
    }

    [Fact]
    public void Parse_UnknownWeather_ThrowsAndListsAcceptedNames()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"weather\": \"sunny\"")));

        Assert.Contains("unknown weather", ex.Message);
        Assert.Contains("cloudless", ex.Message);       // the error lists accepted names
        Assert.Contains("scattered_rain", ex.Message);
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(24)]
    public void Parse_HourOutOfRange_Throws(int hour)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig($", \"hour\": {hour}")));

        Assert.Contains("hour", ex.Message);
    }

    [Fact]
    public void Parse_UnknownKey_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"wheather\": \"rain\"")));

        Assert.Contains("unknown parameter", ex.Message);
    }

    [Fact]
    public void Parse_WrongTypes_Throw()
    {
        Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"weather\": 5")));
        Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"hour\": \"noon\"")));
        Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"hour\": 12.5")));
        Assert.Throws<InvalidDataException>(() =>
            WeatherInjector.Parse(LoadWeatherConfig(", \"freeze_time\": 1")));
    }
}
