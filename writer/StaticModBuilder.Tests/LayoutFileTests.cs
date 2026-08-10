using Xunit;

namespace StaticModBuilder.Tests;

public class LayoutFileTests
{
    private const string LAYOUT =
        "<TextureAtlas imagePath=\"SB_Title_01.png\">\n"
        + "\t<SubTexture name=\"MENU_Bar_Loss.png\" x=\"0\" y=\"30\" width=\"2892\" height=\"26\" half=\"0\"/>\n"
        + "\t<SubTexture name=\"MENU_Title_EldenRing_01.png\" x=\"0\" y=\"60\" width=\"2532\" height=\"1532\" half=\"0\"/>\n"
        + "\t<SubTexture name=\"MENU_Title_EldenRing.png\" x=\"100\" y=\"200\" width=\"1266\" height=\"766\" half=\"0\"/>\n"
        + "</TextureAtlas>\n";

    [Fact]
    public void FindSubTexture_ReturnsRectOfExactName()
    {
        var rect = LayoutFile.FindSubTexture(LAYOUT, "MENU_Title_EldenRing_01");

        Assert.Equal((0, 60, 2532, 1532), rect);
    }

    [Fact]
    public void FindSubTexture_DoesNotMatchNamePrefix()
    {
        // "MENU_Title_EldenRing" must resolve its own entry, not the _01 one
        var rect = LayoutFile.FindSubTexture(LAYOUT, "MENU_Title_EldenRing");

        Assert.Equal((100, 200, 1266, 766), rect);
    }

    [Fact]
    public void FindSubTexture_ThrowsWhenAbsent()
    {
        Assert.Throws<InvalidDataException>(() => LayoutFile.FindSubTexture(LAYOUT, "MENU_Nope"));
    }

    [Fact]
    public void RemoveSubTexture_RemovesOnlyThatLine()
    {
        var result = LayoutFile.RemoveSubTexture(LAYOUT, "MENU_Title_EldenRing_01");

        Assert.DoesNotContain("MENU_Title_EldenRing_01", result);
        Assert.Contains("MENU_Bar_Loss.png", result);
        Assert.Contains("MENU_Title_EldenRing.png", result);
        // everything else is preserved byte for byte
        Assert.Equal(LAYOUT.Replace(
            "\t<SubTexture name=\"MENU_Title_EldenRing_01.png\" x=\"0\" y=\"60\" width=\"2532\" height=\"1532\" half=\"0\"/>\n",
            ""), result);
    }

    [Fact]
    public void RemoveSubTexture_ThrowsWhenAbsent()
    {
        Assert.Throws<InvalidDataException>(() => LayoutFile.RemoveSubTexture(LAYOUT, "MENU_Nope"));
    }

    [Fact]
    public void RemoveSubTexture_HandlesCrlfLineEndings()
    {
        var crlf = LAYOUT.Replace("\n", "\r\n");

        var result = LayoutFile.RemoveSubTexture(crlf, "MENU_Title_EldenRing_01");

        Assert.DoesNotContain("MENU_Title_EldenRing_01", result);
        Assert.Equal(crlf.Replace(
            "\t<SubTexture name=\"MENU_Title_EldenRing_01.png\" x=\"0\" y=\"60\" width=\"2532\" height=\"1532\" half=\"0\"/>\r\n",
            ""), result);
    }

    [Fact]
    public void RemoveSubTexture_RemovesOnlyFirstOccurrence()
    {
        var duplicated = LAYOUT.Replace("</TextureAtlas>",
            "\t<SubTexture name=\"MENU_Title_EldenRing_01.png\" x=\"9\" y=\"9\" width=\"4\" height=\"4\" half=\"0\"/>\n</TextureAtlas>");

        var result = LayoutFile.RemoveSubTexture(duplicated, "MENU_Title_EldenRing_01");

        Assert.Contains("x=\"9\"", result);
        Assert.DoesNotContain("x=\"0\" y=\"60\"", result);
    }
}
