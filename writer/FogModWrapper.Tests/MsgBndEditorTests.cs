using FogModWrapper;
using Xunit;

public class MsgBndEditorTests
{
    [Fact]
    public void LocalizedText_FrafrPrefersFrThenFallsBack()
    {
        Assert.Equal("FR", MsgBndEditor.LocalizedText("frafr", "EN", "FR"));
        Assert.Equal("EN", MsgBndEditor.LocalizedText("frafr", "EN", null));
        Assert.Equal("EN", MsgBndEditor.LocalizedText("frafr", "EN", ""));
    }

    [Fact]
    public void LocalizedText_OtherLanguagesAlwaysEn()
    {
        Assert.Equal("EN", MsgBndEditor.LocalizedText("engus", "EN", "FR"));
        Assert.Equal("EN", MsgBndEditor.LocalizedText("deDE", "EN", "FR"));
    }
}
