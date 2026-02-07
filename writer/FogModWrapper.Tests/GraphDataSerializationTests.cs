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
            FinishEvent = 9000001
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
        Assert.Equal(original.FinishEvent, deserialized.FinishEvent);
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
            FinishEvent = 9000000
        };

        var json = JsonSerializer.Serialize(data);

        Assert.Contains("\"event_map\"", json);
        Assert.Contains("\"finish_event\"", json);
    }
}
