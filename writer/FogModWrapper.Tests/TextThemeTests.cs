using FogModWrapper;
using Xunit;

public class TextThemeTests
{
    [Fact]
    public void LocalizedText_FrafrPrefersFrThenFallsBack()
    {
        Assert.Equal("FR", TextTheme.LocalizedText("frafr", "EN", "FR"));
        Assert.Equal("EN", TextTheme.LocalizedText("frafr", "EN", null));
        Assert.Equal("EN", TextTheme.LocalizedText("frafr", "EN", ""));
    }

    [Fact]
    public void LocalizedText_OtherLanguagesAlwaysEn()
    {
        Assert.Equal("EN", TextTheme.LocalizedText("engus", "EN", "FR"));
        Assert.Equal("EN", TextTheme.LocalizedText("deDE", "EN", "FR"));
    }
}
