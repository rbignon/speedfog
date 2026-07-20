using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class MapSplitsLoaderTests
{
    private const string Sample = """
        [[zones]]
        name = "upper"
        map = "m99_00_00_00"
        display_name = "Upper Half"
        tags = ["dlc"]
        split_from = "lower"
        cols = ["h004400", "h004500"]

        [[fogs]]
        name = "AEG099_002_9900"
        map = "m99_00_00_00"
        id = 990001
        text = "Split gate"
        make_from = "AEG099_002 AEG099_002_9000 1.0 2.0 3.0 90.0"
        aside = { area = "lower", text = "going up" }
        bside = { area = "upper", text = "arriving up" }
        """;

    [Fact]
    public void Parse_ReadsZonesAndFogs()
    {
        var splits = MapSplitsLoader.Parse(Sample);
        var zone = Assert.Single(splits.Zones);
        Assert.Equal("upper", zone.Name);
        Assert.Equal("lower", zone.SplitFrom);
        Assert.Equal(new[] { "h004400", "h004500" }, zone.Cols);
        var fog = Assert.Single(splits.Fogs);
        Assert.Equal(990001, fog.Id);
        Assert.Equal("lower", fog.ASideArea);
        Assert.Equal("arriving up", fog.BSideText);
        Assert.StartsWith("AEG099_002 ", fog.MakeFrom);
    }

    [Fact]
    public void Load_MissingFile_ReturnsEmpty()
    {
        var splits = MapSplitsLoader.Load("/nonexistent/map_splits.toml");
        Assert.Empty(splits.Zones);
        Assert.Empty(splits.Fogs);
    }

    [Fact]
    public void Parse_MissingRequiredKey_Throws()
    {
        Assert.Throws<InvalidDataException>(() => MapSplitsLoader.Parse("""
            [[fogs]]
            name = "X"
            """));
    }
}
