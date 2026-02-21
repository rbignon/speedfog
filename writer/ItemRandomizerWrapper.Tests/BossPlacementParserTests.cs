using System.Text.Json;
using ItemRandomizerWrapper;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

public class BossPlacementParserTests
{
    [Fact]
    public void Parse_BossLine_ExtractsPlacement()
    {
        var lines = new[]
        {
            "-- Boss placements",
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Single(result);
        Assert.True(result.ContainsKey("14000850"));
        Assert.Equal("Rennala Queen of the Full Moon", result["14000850"].Name);
        Assert.Equal(14000800, result["14000850"].EntityId);
    }

    [Fact]
    public void Parse_WithScaling_ExtractsPlacement()
    {
        var lines = new[]
        {
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria (scaling 5->3)",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Single(result);
        Assert.Equal("Rennala Queen of the Full Moon", result["14000850"].Name);
    }

    [Fact]
    public void Parse_MultipleBosses_ExtractsAll()
    {
        var lines = new[]
        {
            "Replacing Godrick the Grafted (#14000850) in Stormveil Castle: Rennala Queen of the Full Moon (#14000800) from Raya Lucaria",
            "Replacing Rennala Queen of the Full Moon (#14000800) in Raya Lucaria: Godrick the Grafted (#14000850) from Stormveil Castle",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Equal(2, result.Count);
    }

    [Fact]
    public void Parse_NonReplacingLines_Ignored()
    {
        var lines = new[]
        {
            "-- Boss placements",
            "Some other output",
            "(not randomized)",
            "",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Empty(result);
    }

    [Fact]
    public void Parse_CategoryPrefix_IncludedInName()
    {
        // Some bosses get a category prefix like "Black Phantom" from the randomizer
        var lines = new[]
        {
            "Replacing Crucible Knight (#10000850) in Evergaol: Night Black Phantom Crucible Knight (#10000860) from Night Arena",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Equal("Night Black Phantom Crucible Knight", result["10000850"].Name);
    }

    [Fact]
    public void Parse_LargeEntityId_DoesNotOverflow()
    {
        // DLC entity IDs can exceed Int32.MaxValue (2,147,483,647)
        var lines = new[]
        {
            "Replacing DLC Boss (#2300000800) in DLC Arena: Another Boss (#2400000850) from Other Arena",
        };

        var result = BossPlacementParser.Parse(lines);

        Assert.Single(result);
        Assert.Equal("Another Boss", result["2300000800"].Name);
        Assert.Equal(2400000850L, result["2300000800"].EntityId);
    }

    [Fact]
    public void Serialize_ProducesExpectedJson()
    {
        var placements = new Dictionary<string, BossPlacement>
        {
            ["14000850"] = new BossPlacement { Name = "Rennala", EntityId = 14000800 },
        };

        var json = BossPlacementParser.Serialize(placements);
        var deserialized = JsonSerializer.Deserialize<Dictionary<string, BossPlacement>>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("Rennala", deserialized["14000850"].Name);
    }
}
