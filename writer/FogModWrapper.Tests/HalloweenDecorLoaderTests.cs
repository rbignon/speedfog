using FogModWrapper;
using Xunit;

public class HalloweenDecorLoaderTests
{
    [Fact]
    public void Load_MissingFileIsEmpty()
        => Assert.True(HalloweenDecorLoader.Load("/no/such/file.toml").IsEmpty);

    [Fact]
    public void Parse_ReadsEntryWithDefaults()
    {
        var catalog = HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "AEG099_090"
            count = 2
            min_radius = 2.0
            max_radius = 4.0
            """);
        var e = Assert.Single(catalog.Entries);
        Assert.Equal("AEG099_090", e.Model);
        Assert.Equal(0, e.SfxId);
    }

    [Fact]
    public void Parse_DefaultsSfxDummyTo100()
    {
        var catalog = HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "AEG099_090"
            count = 1
            min_radius = 1.0
            max_radius = 2.0
            """);
        var e = Assert.Single(catalog.Entries);
        Assert.Equal(100, e.SfxDummyPoly);
    }

    [Fact]
    public void Parse_RejectsBadModelName()
        => Assert.Throws<InvalidDataException>(() => HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "not_a_model"
            count = 1
            min_radius = 1.0
            max_radius = 2.0
            """));

    [Fact]
    public void Parse_RejectsCountOutOfRange()
        => Assert.Throws<InvalidDataException>(() => HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "AEG099_090"
            count = 9
            min_radius = 1.0
            max_radius = 2.0
            """));

    [Fact]
    public void Parse_RejectsMinRadiusGreaterThanMaxRadius()
        => Assert.Throws<InvalidDataException>(() => HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "AEG099_090"
            count = 1
            min_radius = 5.0
            max_radius = 2.0
            """));

    [Fact]
    public void Parse_RejectsZeroMinRadius()
        => Assert.Throws<InvalidDataException>(() => HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "AEG099_090"
            count = 1
            min_radius = 0.0
            max_radius = 2.0
            """));

    [Fact]
    public void Parse_ErrorsCarryHalloweenDecorationsPrefix()
    {
        var ex = Assert.Throws<InvalidDataException>(() => HalloweenDecorLoader.Parse("""
            [[entries]]
            model = "not_a_model"
            count = 1
            min_radius = 1.0
            max_radius = 2.0
            """));
        Assert.StartsWith("halloween decorations:", ex.Message);
    }

    [Fact]
    public void Load_RealCatalogueValidates()
    {
        // Walk up from the test bin dir like TextThemeCatalogLoaderTests does.
        var dir = AppContext.BaseDirectory;
        while (dir != null && !Directory.Exists(Path.Combine(dir, "data", "plugins")))
            dir = Path.GetDirectoryName(dir);
        Assert.NotNull(dir);
        // Ships with the scouted starter set; loading must succeed, and the
        // summed per-gate count must stay modest (the file header asks for
        // it: every entry applies at every eligible entrance gate).
        var catalog = HalloweenDecorLoader.Load(
            Path.Combine(dir!, "data", "plugins", "halloween_decorations.toml"));
        Assert.False(catalog.IsEmpty);
        Assert.InRange(catalog.Entries.Sum(e => e.Count), 1, 10);
    }
}
