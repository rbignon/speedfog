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
    public void Parse_EmptyJson_UsesDefaults()
    {
        var json = "{}";

        // Parse succeeds with default values
        var data = GraphLoader.Parse(json);
        Assert.Equal("4.2", data.Version);
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

    [Fact]
    public void Parse_V4_AcceptsVersion()
    {
        var json = """
            {
                "version": "4.2",
                "seed": 42,
                "event_map": {"9000000": "stormveil", "9000001": "radagon"},
                "finish_event": 9000001,
                "connections": [
                    {
                        "exit_area": "stormveil",
                        "exit_gate": "m10_00_00_00_AEG099_001_9000",
                        "entrance_area": "liurnia",
                        "entrance_gate": "m11_00_00_00_AEG099_002_9000",
                        "flag_id": 9000000
                    }
                ]
            }
            """;

        var data = GraphLoader.Parse(json);

        Assert.Equal("4.2", data.Version);
        Assert.Equal(42, data.Seed);
        Assert.Equal(2, data.EventMap.Count);
        Assert.Equal("stormveil", data.EventMap["9000000"]);
        Assert.Equal(9000001, data.FinishEvent);
        Assert.Single(data.Connections);
        Assert.Equal(9000000, data.Connections[0].FlagId);
    }

    [Fact]
    public void GraphData_DeserializesNodeTypes()
    {
        var json = """
        {
          "version": "4.4",
          "seed": 1,
          "nodes": {
            "stormveil": {"type": "legacy_dungeon", "display_name": "Stormveil", "layer": 1},
            "arena_x": {"type": "boss_arena"}
          },
          "connections": [],
          "area_tiers": {}
        }
        """;
        var data = GraphLoader.Parse(json)!;
        Assert.Equal("legacy_dungeon", data.Nodes["stormveil"].Type);
        Assert.Equal("boss_arena", data.Nodes["arena_x"].Type);
    }

    [Fact]
    public void GraphData_DeserializesEnemyAssignments()
    {
        var json = """
        {
          "version": "4.5",
          "seed": 1,
          "enemy_assignments": {"30001800": "2049420200"},
          "connections": [],
          "area_tiers": {}
        }
        """;
        var data = GraphLoader.Parse(json);
        Assert.Equal("2049420200", data.EnemyAssignments["30001800"]);
    }

    [Fact]
    public void GraphData_EnemyAssignmentsDefaultEmpty()
    {
        var json = """
        {"version": "4.5", "seed": 1, "connections": [], "area_tiers": {}}
        """;
        Assert.Empty(GraphLoader.Parse(json).EnemyAssignments);
    }

    [Fact]
    public void GraphData_DeserializesClassLoadoutAndTorrentSkins()
    {
        var json = """
        {
          "version": "4.7",
          "seed": 1,
          "class_loadout": {
            "weapons": [{"id": 3560000, "name": "Leontiel's Greatsword"}],
            "shields": [{"id": 31540000, "name": "Silver Grooved Shield"}],
            "armor_sets": [[100, 200, 300, 400]]
          },
          "torrent_skins": {"unlock": true, "default_flag": 6702},
          "connections": [],
          "area_tiers": {}
        }
        """;
        var data = GraphLoader.Parse(json);
        Assert.Equal(3560000, data.ClassLoadout!.Weapons[0].Id);
        Assert.Equal(31540000, data.ClassLoadout.Shields[0].Id);
        Assert.Equal(4, data.ClassLoadout.ArmorSets[0].Count);
        Assert.Equal(6702, data.TorrentSkins!.DefaultFlag);
    }

    [Fact]
    public void GraphData_DeserializesBossNames()
    {
        var json = """
        {
          "version": "4.8",
          "seed": 1,
          "boss_names": {"30010800": {"name": "Aging Untouchable", "map": "m30_01_00_00"}},
          "connections": [],
          "area_tiers": {}
        }
        """;
        var data = GraphLoader.Parse(json);
        var entry = Assert.Single(data.BossNames);
        Assert.Equal("30010800", entry.Key);
        Assert.Equal("Aging Untouchable", entry.Value.Name);
        Assert.Equal("m30_01_00_00", entry.Value.Map);
    }

    [Fact]
    public void GraphData_BossNamesDefaultEmpty()
    {
        var json = """
        {"version": "4.8", "seed": 1, "connections": [], "area_tiers": {}}
        """;
        Assert.Empty(GraphLoader.Parse(json).BossNames);
    }

    [Fact]
    public void GraphData_ClassLoadoutAndTorrentSkinsDefaultNull()
    {
        var json = """
        {"version": "4.7", "seed": 1, "connections": [], "area_tiers": {}}
        """;
        var data = GraphLoader.Parse(json);
        Assert.Null(data.ClassLoadout);
        Assert.Null(data.TorrentSkins);
    }
}
