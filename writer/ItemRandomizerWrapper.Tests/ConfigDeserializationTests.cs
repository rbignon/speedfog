using System.Text.Json;
using ItemRandomizerWrapper;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

public class ConfigDeserializationTests
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    [Fact]
    public void RandomizerConfig_FullJson_Deserializes()
    {
        var json = """
            {
                "seed": 12345,
                "difficulty": 75,
                "options": {
                    "item": true,
                    "enemy": false,
                    "custom": true
                },
                "preset": "my_preset",
                "helper_options": {
                    "auto_upgrade": true,
                    "remove_requirements": false
                }
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(12345, config.Seed);
        Assert.Equal(75, config.Difficulty);
        Assert.NotNull(config.Options);
        Assert.True(config.Options["item"]);
        Assert.False(config.Options["enemy"]);
        Assert.True(config.Options["custom"]);
        Assert.Equal("my_preset", config.Preset);
        Assert.NotNull(config.HelperOptions);
        Assert.True(config.HelperOptions["auto_upgrade"]);
        Assert.False(config.HelperOptions["remove_requirements"]);
    }

    [Fact]
    public void RandomizerConfig_MinimalJson_UsesDefaults()
    {
        var json = """
            {
                "seed": 999
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(999, config.Seed);
        Assert.Equal(50, config.Difficulty); // Default
        Assert.Null(config.Options);
        Assert.Null(config.Preset);
        Assert.Null(config.HelperOptions);
    }

    [Fact]
    public void RandomizerConfig_EmptyJson_UsesDefaults()
    {
        var json = "{}";

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(0, config.Seed);
        Assert.Equal(50, config.Difficulty);
    }

    [Fact]
    public void RandomizerConfig_RoundTrip_PreservesValues()
    {
        var original = new RandomizerConfig
        {
            Seed = 42,
            Difficulty = 80,
            Options = new Dictionary<string, bool> { ["item"] = true },
            Preset = "test",
            HelperOptions = new Dictionary<string, bool> { ["opt1"] = false }
        };

        var json = JsonSerializer.Serialize(original, JsonOptions);
        var deserialized = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Seed, deserialized.Seed);
        Assert.Equal(original.Difficulty, deserialized.Difficulty);
        Assert.Equal(original.Options!["item"], deserialized.Options!["item"]);
        Assert.Equal(original.Preset, deserialized.Preset);
        Assert.Equal(original.HelperOptions!["opt1"], deserialized.HelperOptions!["opt1"]);
    }

    [Fact]
    public void RandomizerConfig_JsonPropertyNames_UseSnakeCase()
    {
        var config = new RandomizerConfig
        {
            Seed = 1,
            HelperOptions = new Dictionary<string, bool> { ["test"] = true }
        };

        var json = JsonSerializer.Serialize(config);

        Assert.Contains("\"helper_options\"", json);
        Assert.DoesNotContain("\"HelperOptions\"", json);
    }

    [Fact]
    public void CliConfig_DefaultValues_AreEmpty()
    {
        var config = new CliConfig();

        Assert.Equal("", config.ConfigPath);
        Assert.Equal("", config.GameDir);
        Assert.Equal("", config.OutputDir);
        Assert.Null(config.DataDir);
    }

    [Fact]
    public void CliConfig_Properties_AreSettable()
    {
        var config = new CliConfig
        {
            ConfigPath = "/path/to/config.json",
            GameDir = "/path/to/game",
            OutputDir = "/output",
            DataDir = "/data"
        };

        Assert.Equal("/path/to/config.json", config.ConfigPath);
        Assert.Equal("/path/to/game", config.GameDir);
        Assert.Equal("/output", config.OutputDir);
        Assert.Equal("/data", config.DataDir);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(100)]
    public void RandomizerConfig_DifficultyRange_Accepted(int difficulty)
    {
        var json = $"{{\"seed\": 1, \"difficulty\": {difficulty}}}";

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(difficulty, config.Difficulty);
    }

    [Fact]
    public void RandomizerConfig_ExtraFields_Ignored()
    {
        var json = """
            {
                "seed": 123,
                "unknown_field": "should be ignored",
                "another": 456
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(123, config.Seed);
    }

    [Fact]
    public void RandomizerConfig_WithItemPresetPath_Deserializes()
    {
        var json = """
            {
                "seed": 123,
                "item_preset_path": "item_preset.yaml"
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal("item_preset.yaml", config.ItemPresetPath);
    }

    [Fact]
    public void RandomizerConfig_WithoutItemPresetPath_IsNull()
    {
        var json = """
            {
                "seed": 123
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Null(config.ItemPresetPath);
    }

    [Fact]
    public void RandomizerConfig_CaseInsensitive_Works()
    {
        var json = """
            {
                "SEED": 555,
                "Difficulty": 60,
                "Helper_Options": {"test": true}
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Equal(555, config.Seed);
        Assert.Equal(60, config.Difficulty);
        Assert.NotNull(config.HelperOptions);
    }

    [Fact]
    public void RandomizerConfig_WithEnemyOptions_Deserializes()
    {
        var json = """
            {
                "seed": 123,
                "enemy_options": {
                    "randomize_bosses": true,
                    "lock_final_boss": false,
                    "finish_boss_defeat_flag": 1052380800
                }
            }
            """;

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.NotNull(config.EnemyOptions);
        Assert.True(config.EnemyOptions.RandomizeBosses);
        Assert.False(config.EnemyOptions.LockFinalBoss);
        Assert.Equal(1052380800, config.EnemyOptions.FinishBossDefeatFlag);
    }

    [Fact]
    public void RandomizerConfig_WithoutEnemyOptions_IsNull()
    {
        var json = """{ "seed": 123 }""";

        var config = JsonSerializer.Deserialize<RandomizerConfig>(json, JsonOptions);

        Assert.NotNull(config);
        Assert.Null(config.EnemyOptions);
    }
}
