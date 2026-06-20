using FogModWrapper;
using Xunit;

public class SummerCatalogLoaderTests
{
    [Fact]
    public void Parse_ValidMixedCatalogue()
    {
        var toml = """
        [[bosses]]
        npc_name_id = 902130000
        name = "Margit"
        en = "Margit, the Sun-Scorched"
        fr = "Margit, le Brûlé par le Soleil"

        [[ui]]
        bnd = "menu_dlc02.msgbnd.dcx"
        fmg = "GR_MenuText"
        id = 331305
        en = "SUNSTROKE"
        """;

        var c = SummerCatalogLoader.Parse(toml);

        Assert.Single(c.Bosses);
        Assert.Equal(902130000, c.Bosses[0].NpcNameId);
        Assert.Equal("Margit, the Sun-Scorched", c.Bosses[0].En);
        Assert.Equal("Margit, le Brûlé par le Soleil", c.Bosses[0].Fr);
        Assert.Single(c.Ui);
        Assert.Equal(331305, c.Ui[0].Id);
        Assert.Null(c.Ui[0].Fr);
    }

    [Fact]
    public void Parse_RejectsBossMissingEn()
    {
        var toml = """
        [[bosses]]
        npc_name_id = 902130000
        name = "Margit"
        en = ""
        """;
        Assert.Throws<InvalidDataException>(() => SummerCatalogLoader.Parse(toml));
    }

    [Fact]
    public void Parse_RejectsDuplicateBossId()
    {
        var toml = """
        [[bosses]]
        npc_name_id = 902130000
        en = "A"
        [[bosses]]
        npc_name_id = 902130000
        en = "B"
        """;
        Assert.Throws<InvalidDataException>(() => SummerCatalogLoader.Parse(toml));
    }

    [Fact]
    public void Parse_RejectsReservedRunCompleteId()
    {
        var toml = """
        [[ui]]
        bnd = "menu_dlc02.msgbnd.dcx"
        fmg = "GR_MenuText"
        id = 331314
        en = "X"
        """;
        Assert.Throws<InvalidDataException>(() => SummerCatalogLoader.Parse(toml));
    }

    [Fact]
    public void Parse_RejectsUiMissingEn()
    {
        var toml = """
        [[ui]]
        bnd = "menu_dlc02.msgbnd.dcx"
        fmg = "GR_MenuText"
        id = 331305
        en = ""
        """;
        Assert.Throws<InvalidDataException>(() => SummerCatalogLoader.Parse(toml));
    }

    [Fact]
    public void Load_MissingFileReturnsEmpty()
    {
        var c = SummerCatalogLoader.Load("/no/such/summer.toml");
        Assert.True(c.IsEmpty);
    }
}
