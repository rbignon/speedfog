using FogModWrapper;
using Xunit;

public class SummerThemeTests
{
    [Fact]
    public void LocalizedText_FrafrPrefersFrThenFallsBack()
    {
        Assert.Equal("FR", SummerTheme.LocalizedText("frafr", "EN", "FR"));
        Assert.Equal("EN", SummerTheme.LocalizedText("frafr", "EN", null));
        Assert.Equal("EN", SummerTheme.LocalizedText("frafr", "EN", ""));
    }

    [Fact]
    public void LocalizedText_OtherLanguagesAlwaysEn()
    {
        Assert.Equal("EN", SummerTheme.LocalizedText("engus", "EN", "FR"));
        Assert.Equal("EN", SummerTheme.LocalizedText("deDE", "EN", "FR"));
    }
}
