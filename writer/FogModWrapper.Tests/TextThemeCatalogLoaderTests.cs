using FogModWrapper;
using Xunit;

public class TextThemeCatalogLoaderTests
{
    [Fact]
    public void Parse_ValidMixedCatalogue()
    {
        var toml = """
        [[bosses]]
        npc_name_id = 902130000
        name = "Margit"
        en = "Margit, the Sun-Scorched"
        fr = "Margit, le Brûlé par le soleil"

        [[ui]]
        bnd = "menu_dlc02.msgbnd.dcx"
        fmg = "GR_MenuText"
        id = 331305
        en = "SUNSTROKE"
        """;

        var c = TextThemeCatalogLoader.Parse(toml, "summer");

        Assert.Single(c.Bosses);
        Assert.Equal(902130000, c.Bosses[0].NpcNameId);
        Assert.Equal("Margit, the Sun-Scorched", c.Bosses[0].En);
        Assert.Equal("Margit, le Brûlé par le soleil", c.Bosses[0].Fr);
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
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "summer"));
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
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "summer"));
    }

    [Fact]
    public void Parse_AcceptsBossKeyedByName()
    {
        var toml = """
        [[bosses]]
        boss_name = "Devonia"
        en = "Demonia"
        fr = "Démonia"
        """;

        var c = TextThemeCatalogLoader.Parse(toml, "halloween");

        Assert.Single(c.Bosses);
        Assert.Equal("Devonia", c.Bosses[0].BossName);
        Assert.Null(c.Bosses[0].NpcNameId);
        Assert.Equal("Demonia", c.Bosses[0].En);
        Assert.Equal("Démonia", c.Bosses[0].Fr);
    }

    [Theory]
    [InlineData("npc_name_id = 902130000\nboss_name = \"Devonia\"")]
    [InlineData("name = \"Devonia\"")]
    public void Parse_RejectsBossWithoutExactlyOneKey(string keys)
    {
        var toml = $"[[bosses]]\n{keys}\nen = \"X\"\n";
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "halloween"));
    }

    // BossNameInjector allocates trimmed, non-empty names without a trailing
    // parenthetical ("Hornsent (Leda Fight)" becomes "Hornsent"): these keys
    // could never match.
    [Theory]
    [InlineData("")]
    [InlineData(" Devonia")]
    [InlineData("Hornsent (Leda Fight)")]
    public void Parse_RejectsBossNameTheInjectorNeverAllocates(string bossName)
    {
        var toml = $"[[bosses]]\nboss_name = \"{bossName}\"\nen = \"X\"\n";
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "halloween"));
    }

    [Fact]
    public void Parse_RejectsNpcNameIdInTheSeedDependentRange()
    {
        var toml = $"[[bosses]]\nnpc_name_id = {SpeedFogIds.BossNameFmgIds.Base}\nen = \"X\"\n";
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "halloween"));
    }

    [Fact]
    public void Parse_RejectsDuplicateBossName()
    {
        var toml = """
        [[bosses]]
        boss_name = "Devonia"
        en = "A"
        [[bosses]]
        boss_name = "Devonia"
        en = "B"
        """;
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "halloween"));
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
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "summer"));
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
        Assert.Throws<InvalidDataException>(() => TextThemeCatalogLoader.Parse(toml, "summer"));
    }

    [Fact]
    public void Load_MissingFileReturnsEmpty()
    {
        var c = TextThemeCatalogLoader.Load("/no/such/summer.toml", "summer");
        Assert.True(c.IsEmpty);
    }

    [Fact]
    public void Parse_ErrorsCarryThemeName()
    {
        var toml = """
        [[bosses]]
        npc_name_id = 902130000
        en = ""
        """;
        var ex = Assert.Throws<InvalidDataException>(
            () => TextThemeCatalogLoader.Parse(toml, "halloween"));
        Assert.StartsWith("halloween:", ex.Message);
    }

    [Theory]
    [InlineData("summer")]
    [InlineData("halloween")]
    public void Load_RealCatalogueValidates(string theme)
    {
        var path = Path.Combine(RepoPluginsDir(), $"{theme}.toml");
        var c = TextThemeCatalogLoader.Load(path, theme);
        Assert.False(c.IsEmpty);
    }

    // Walks up from the test bin dir to the repo root (the dir holding data/plugins).
    private static string RepoPluginsDir()
    {
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "data", "plugins")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        return Path.Combine(dir!, "data", "plugins");
    }
}
