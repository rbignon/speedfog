using System.Text.Json.Serialization;

namespace ItemRandomizerWrapper;

/// <summary>
/// CLI configuration parsed from command-line arguments.
/// </summary>
public class CliConfig
{
    public string ConfigPath { get; set; } = "";
    public string GameDir { get; set; } = "";
    public string OutputDir { get; set; } = "";
    public string? DataDir { get; set; }
}

/// <summary>
/// Randomizer configuration loaded from JSON file.
/// </summary>
public class RandomizerConfig
{
    [JsonPropertyName("seed")]
    public int Seed { get; set; }

    [JsonPropertyName("difficulty")]
    public int Difficulty { get; set; } = 50;

    [JsonPropertyName("options")]
    public Dictionary<string, bool>? Options { get; set; }

    [JsonPropertyName("preset")]
    public string? Preset { get; set; }

    [JsonPropertyName("helper_options")]
    public Dictionary<string, bool>? HelperOptions { get; set; }

    [JsonPropertyName("item_preset_path")]
    public string? ItemPresetPath { get; set; }
}
