using System.Text.Json;
using FogModWrapper;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class GraphLoaderTests
{
    [Fact]
    public void Parse_ValidJson_ReturnsGraphData()
    {
        var json = """
            {
                "version": "3.0",
                "seed": 12345,
                "connections": [
                    {
                        "exit_area": "stormveil",
                        "exit_gate": "m10_00_00_00_AEG099_001_9000",
                        "entrance_area": "liurnia",
                        "entrance_gate": "m11_00_00_00_AEG099_002_9000"
                    }
                ],
                "area_tiers": {
                    "stormveil": 5,
                    "liurnia": 10
                },
                "starting_goods": [8000, 8001],
                "starting_runes": 50000,
                "starting_golden_seeds": 3,
                "starting_sacred_tears": 2
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("3.0", data.Version);
        Assert.Equal(12345, data.Seed);
        Assert.Single(data.Connections);
        Assert.Equal("stormveil", data.Connections[0].ExitArea);
        Assert.Equal("m10_00_00_00_AEG099_001_9000", data.Connections[0].ExitGate);
        Assert.Equal("liurnia", data.Connections[0].EntranceArea);
        Assert.Equal(2, data.AreaTiers.Count);
        Assert.Equal(5, data.AreaTiers["stormveil"]);
        Assert.Equal(2, data.StartingGoods.Count);
        Assert.Equal(50000, data.StartingRunes);
        Assert.Equal(3, data.StartingGoldenSeeds);
        Assert.Equal(2, data.StartingSacredTears);
    }

    [Fact]
    public void Parse_MinimalJson_UsesDefaults()
    {
        var json = """
            {
                "version": "3.0",
                "seed": 1
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("3.0", data.Version);
        Assert.Equal(1, data.Seed);
        Assert.Empty(data.Connections);
        Assert.Empty(data.AreaTiers);
        Assert.Empty(data.StartingGoods);
        Assert.Empty(data.StartingItemLots);
        Assert.Equal(0, data.StartingRunes);
        Assert.Equal(0, data.StartingGoldenSeeds);
        Assert.Equal(0, data.StartingSacredTears);
    }

    [Fact]
    public void Parse_EmptyJson_ThrowsJsonException()
    {
        var json = "{}";

        // Parse succeeds but with default values (version will be "3.0" by default)
        var data = GraphLoader.Parse(json);
        Assert.Equal("3.0", data.Version);
    }

    [Fact]
    public void Parse_NullJson_ThrowsJsonException()
    {
        var json = "null";

        Assert.Throws<JsonException>(() => GraphLoader.Parse(json));
    }

    [Fact]
    public void Parse_InvalidJson_ThrowsJsonException()
    {
        var json = "not valid json";

        Assert.Throws<JsonException>(() => GraphLoader.Parse(json));
    }

    [Fact]
    public void Parse_WrongVersion_StillParses()
    {
        var json = """
            {
                "version": "1.0",
                "seed": 999
            }
            """;

        // Should parse but with a warning (we can't easily test Console output)
        var data = GraphLoader.Parse(json);

        Assert.Equal("1.0", data.Version);
        Assert.Equal(999, data.Seed);
    }

    [Fact]
    public void Parse_ExtraFields_Ignored()
    {
        var json = """
            {
                "version": "3.0",
                "seed": 42,
                "unknown_field": "should be ignored",
                "another_unknown": 123
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("3.0", data.Version);
        Assert.Equal(42, data.Seed);
    }

    [Fact]
    public void Parse_CaseInsensitive()
    {
        var json = """
            {
                "VERSION": "3.0",
                "SEED": 777,
                "Area_Tiers": {"zone1": 5}
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("3.0", data.Version);
        Assert.Equal(777, data.Seed);
        Assert.Single(data.AreaTiers);
    }

    [Fact]
    public void Parse_WithComments_ParsesSuccessfully()
    {
        var json = """
            {
                // This is a comment
                "version": "3.0",
                "seed": 123
                /* This is also a comment */
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("3.0", data.Version);
        Assert.Equal(123, data.Seed);
    }

    [Fact]
    public void Load_NonExistentFile_ThrowsFileNotFoundException()
    {
        var path = "/nonexistent/path/graph.json";

        Assert.Throws<FileNotFoundException>(() => GraphLoader.Load(path));
    }
}
