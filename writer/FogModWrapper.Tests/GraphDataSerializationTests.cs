using System.Text.Json;
using FogModWrapper.Models;
using Xunit;

namespace FogModWrapper.Tests;

public class GraphDataSerializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = true
    };

    [Fact]
    public void GraphData_RoundTrip_PreservesAllFields()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 12345,
            Options = new Dictionary<string, bool>
            {
                ["scale"] = true,
                ["crawl"] = true
            },
            Connections = new List<Connection>
            {
                new()
                {
                    ExitArea = "stormveil",
                    ExitGate = "gate1",
                    EntranceArea = "liurnia",
                    EntranceGate = "gate2"
                }
            },
            AreaTiers = new Dictionary<string, int>
            {
                ["stormveil"] = 5,
                ["liurnia"] = 10
            },
            StartingItemLots = new List<int> { 1000, 2000 },
            StartingGoods = new List<int> { 8000, 8001 },
            StartingRunes = 100000,
            StartingGoldenSeeds = 5,
            StartingSacredTears = 3,
            EventMap = new Dictionary<string, string>
            {
                ["9000000"] = "stormveil",
                ["9000001"] = "liurnia"
            },
            FinalNodeFlag = 9000001,
            FinishEvent = 9000002,
            FinishBossDefeatFlag = 19000800,
            RunCompleteMessage = "TEST COMPLETE",
            ChapelGrace = false
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Version, deserialized.Version);
        Assert.Equal(original.Seed, deserialized.Seed);
        Assert.Equal(original.Options.Count, deserialized.Options.Count);
        Assert.True(deserialized.Options["scale"]);
        Assert.Equal(original.Connections.Count, deserialized.Connections.Count);
        Assert.Equal(original.AreaTiers.Count, deserialized.AreaTiers.Count);
        Assert.Equal(original.StartingItemLots.Count, deserialized.StartingItemLots.Count);
        Assert.Equal(original.StartingGoods.Count, deserialized.StartingGoods.Count);
        Assert.Equal(original.StartingRunes, deserialized.StartingRunes);
        Assert.Equal(original.StartingGoldenSeeds, deserialized.StartingGoldenSeeds);
        Assert.Equal(original.StartingSacredTears, deserialized.StartingSacredTears);
        Assert.Equal(original.EventMap.Count, deserialized.EventMap.Count);
        Assert.Equal(original.FinalNodeFlag, deserialized.FinalNodeFlag);
        Assert.Equal(original.FinishEvent, deserialized.FinishEvent);
        Assert.Equal(original.FinishBossDefeatFlag, deserialized.FinishBossDefeatFlag);
        Assert.Equal(original.RunCompleteMessage, deserialized.RunCompleteMessage);
        Assert.Equal(original.ChapelGrace, deserialized.ChapelGrace);
    }

    [Fact]
    public void Connection_RoundTrip_PreservesAllFields()
    {
        var original = new Connection
        {
            ExitArea = "zone_a",
            ExitGate = "m10_00_00_00_AEG099_001_9000",
            EntranceArea = "zone_b",
            EntranceGate = "m11_00_00_00_AEG099_002_9000",
            FlagId = 9000042
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Connection>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.ExitArea, deserialized.ExitArea);
        Assert.Equal(original.ExitGate, deserialized.ExitGate);
        Assert.Equal(original.EntranceArea, deserialized.EntranceArea);
        Assert.Equal(original.EntranceGate, deserialized.EntranceGate);
        Assert.Equal(original.FlagId, deserialized.FlagId);
    }

    [Fact]
    public void Connection_ToString_FormatsCorrectly()
    {
        var conn = new Connection
        {
            ExitArea = "stormveil",
            ExitGate = "gate_exit",
            EntranceArea = "liurnia",
            EntranceGate = "gate_entrance"
        };

        var result = conn.ToString();

        Assert.Contains("stormveil", result);
        Assert.Contains("liurnia", result);
        Assert.Contains("gate_exit", result);
        Assert.Contains("gate_entrance", result);
    }

    [Fact]
    public void GraphData_JsonPropertyNames_UseSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            StartingRunes = 5000,
            StartingGoldenSeeds = 2,
            StartingSacredTears = 1
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"starting_runes\"", json);
        Assert.Contains("\"starting_golden_seeds\"", json);
        Assert.Contains("\"starting_sacred_tears\"", json);
        Assert.Contains("\"area_tiers\"", json);
        Assert.Contains("\"starting_goods\"", json);
        Assert.Contains("\"starting_item_lots\"", json);
    }

    [Fact]
    public void Connection_JsonPropertyNames_UseSnakeCase()
    {
        var conn = new Connection
        {
            ExitArea = "a",
            ExitGate = "b",
            EntranceArea = "c",
            EntranceGate = "d"
        };

        var json = JsonSerializer.Serialize(conn);

        Assert.Contains("\"exit_area\"", json);
        Assert.Contains("\"exit_gate\"", json);
        Assert.Contains("\"entrance_area\"", json);
        Assert.Contains("\"entrance_gate\"", json);
    }

    [Fact]
    public void GraphData_V4_RoundTrip_PreservesEventMap()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            EventMap = new Dictionary<string, string>
            {
                ["9000000"] = "stormveil",
                ["9000001"] = "liurnia",
                ["9000002"] = "radagon"
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.EventMap.Count);
        Assert.Equal("stormveil", deserialized.EventMap["9000000"]);
        Assert.Equal("liurnia", deserialized.EventMap["9000001"]);
        Assert.Equal("radagon", deserialized.EventMap["9000002"]);
    }

    [Fact]
    public void GraphData_V4_RoundTrip_PreservesFinalNodeFlag()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            FinalNodeFlag = 1040292817
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(1040292817, deserialized.FinalNodeFlag);
    }

    [Fact]
    public void GraphData_V4_RoundTrip_PreservesFinishEvent()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            FinishEvent = 9000002
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(9000002, deserialized.FinishEvent);
    }

    [Fact]
    public void Connection_V4_RoundTrip_PreservesFlagId()
    {
        var original = new Connection
        {
            ExitArea = "zone_a",
            ExitGate = "m10_00_00_00_AEG099_001_9000",
            EntranceArea = "zone_b",
            EntranceGate = "m11_00_00_00_AEG099_002_9000",
            FlagId = 9000001
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<Connection>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(9000001, deserialized.FlagId);
    }

    [Fact]
    public void GraphData_V4_JsonPropertyNames_UseSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            EventMap = new Dictionary<string, string> { ["9000000"] = "test" },
            FinalNodeFlag = 9000000,
            FinishEvent = 9000001
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"event_map\"", json);
        Assert.Contains("\"final_node_flag\"", json);
        Assert.Contains("\"finish_event\"", json);
    }

    [Fact]
    public void GraphData_CarePackage_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            CarePackage = new List<CarePackageItem>
            {
                new() { Type = 0, Id = 9000008, Name = "Uchigatana +8" },
                new() { Type = 1, Id = 50000, Name = "Kaiden Helm" },
                new() { Type = 2, Id = 1040, Name = "Erdtree's Favor" },
                new() { Type = 3, Id = 4000, Name = "Glintstone Pebble" }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(4, deserialized.CarePackage.Count);
        Assert.Equal(0, deserialized.CarePackage[0].Type);
        Assert.Equal(9000008, deserialized.CarePackage[0].Id);
        Assert.Equal("Uchigatana +8", deserialized.CarePackage[0].Name);
        Assert.Equal(1, deserialized.CarePackage[1].Type);
        Assert.Equal(50000, deserialized.CarePackage[1].Id);
        Assert.Equal(2, deserialized.CarePackage[2].Type);
        Assert.Equal(3, deserialized.CarePackage[3].Type);
    }

    [Fact]
    public void GraphData_CarePackage_EmptyByDefault()
    {
        var data = new GraphData { Version = "4.0", Seed = 1 };

        var json = JsonSerializer.Serialize(data, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.CarePackage);
    }

    [Fact]
    public void GraphData_CarePackage_JsonPropertyNames_UseSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            CarePackage = new List<CarePackageItem>
            {
                new() { Type = 0, Id = 1000000, Name = "Dagger" }
            }
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"care_package\"", json);
        Assert.Contains("\"type\"", json);
        Assert.Contains("\"id\"", json);
        Assert.Contains("\"name\"", json);
    }

    [Fact]
    public void GraphData_FinalNodeFlag_DefaultsToZeroWhenMissing()
    {
        // Simulate old graph.json without final_node_flag field
        var json = """{"version":"4.0","seed":1,"finish_event":9000002}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.FinalNodeFlag);
        Assert.Equal(9000002, deserialized.FinishEvent);
    }

    [Fact]
    public void GraphData_CarePackage_IgnoredWhenMissing()
    {
        // Simulate old graph.json without care_package field
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.CarePackage);
    }

    [Fact]
    public void GraphData_RunCompleteMessage_DefaultsWhenMissing()
    {
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("RUN COMPLETE", deserialized.RunCompleteMessage);
    }

    [Fact]
    public void GraphData_RunCompleteMessage_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            RunCompleteMessage = "GG EZ"
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal("GG EZ", deserialized.RunCompleteMessage);
    }

    [Fact]
    public void GraphData_RunCompleteMessage_JsonPropertyName_UsesSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            RunCompleteMessage = "TEST"
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"run_complete_message\"", json);
    }

    [Fact]
    public void GraphData_ChapelGrace_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            ChapelGrace = false
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.ChapelGrace);
    }

    [Fact]
    public void GraphData_ChapelGrace_DefaultsTrueWhenMissing()
    {
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.ChapelGrace);
    }

    [Fact]
    public void GraphData_ChapelGrace_JsonPropertyName_UsesSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            ChapelGrace = true
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"chapel_grace\"", json);
    }

    [Fact]
    public void GraphData_FinishBossDefeatFlag_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            FinishBossDefeatFlag = 19000800
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(19000800, deserialized.FinishBossDefeatFlag);
    }

    [Fact]
    public void GraphData_FinishBossDefeatFlag_DefaultsToZeroWhenMissing()
    {
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.FinishBossDefeatFlag);
    }

    [Fact]
    public void GraphData_FinishBossDefeatFlag_JsonPropertyName_UsesSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            FinishBossDefeatFlag = 19000800
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"finish_boss_defeat_flag\"", json);
    }

    [Fact]
    public void GraphData_StartingLarvalTears_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            StartingLarvalTears = 15
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(15, deserialized.StartingLarvalTears);
    }

    [Fact]
    public void GraphData_StartingLarvalTears_DefaultsTo10WhenMissing()
    {
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(10, deserialized.StartingLarvalTears);
    }

    [Fact]
    public void GraphData_StartingLarvalTears_JsonPropertyName_UsesSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            StartingLarvalTears = 10
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"starting_larval_tears\"", json);
    }

    [Fact]
    public void GraphData_RemoveEntities_RoundTrip()
    {
        var original = new GraphData
        {
            Version = "4.0",
            Seed = 42,
            RemoveEntities = new List<RemoveEntity>
            {
                new() { Map = "m12_05_00_00", EntityId = 12051500 },
                new() { Map = "m21_00_00_00", EntityId = 21001576 }
            }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(2, deserialized.RemoveEntities.Count);
        Assert.Equal("m12_05_00_00", deserialized.RemoveEntities[0].Map);
        Assert.Equal(12051500, deserialized.RemoveEntities[0].EntityId);
        Assert.Equal("m21_00_00_00", deserialized.RemoveEntities[1].Map);
        Assert.Equal(21001576, deserialized.RemoveEntities[1].EntityId);
    }

    [Fact]
    public void GraphData_RemoveEntities_EmptyByDefault()
    {
        var json = """{"version":"4.0","seed":1}""";
        var deserialized = JsonSerializer.Deserialize<GraphData>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Empty(deserialized.RemoveEntities);
    }

    [Fact]
    public void GraphData_RemoveEntities_JsonPropertyName_UsesSnakeCase()
    {
        var data = new GraphData
        {
            Version = "4.0",
            Seed = 1,
            RemoveEntities = new List<RemoveEntity>
            {
                new() { Map = "m10_00_00_00", EntityId = 1000 }
            }
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"remove_entities\"", json);
        Assert.Contains("\"entity_id\"", json);
    }
}
