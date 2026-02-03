using System.Text.Json.Serialization;

namespace FogModWrapper.Models;

/// <summary>
/// Root structure for graph.json v2 format.
/// </summary>
public class GraphData
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "2.0";

    [JsonPropertyName("seed")]
    public int Seed { get; set; }

    [JsonPropertyName("options")]
    public Dictionary<string, bool> Options { get; set; } = new();

    [JsonPropertyName("connections")]
    public List<Connection> Connections { get; set; } = new();

    [JsonPropertyName("area_tiers")]
    public Dictionary<string, int> AreaTiers { get; set; } = new();

    [JsonPropertyName("starting_item_lots")]
    public List<int> StartingItemLots { get; set; } = new();
}

/// <summary>
/// A connection between two fog gates.
/// </summary>
public class Connection
{
    /// <summary>
    /// The area containing the exit edge.
    /// </summary>
    [JsonPropertyName("exit_area")]
    public string ExitArea { get; set; } = "";

    /// <summary>
    /// The FullName of the exit gate (e.g., "m10_01_00_00_AEG099_001_9000").
    /// </summary>
    [JsonPropertyName("exit_gate")]
    public string ExitGate { get; set; } = "";

    /// <summary>
    /// The area containing the entrance edge.
    /// </summary>
    [JsonPropertyName("entrance_area")]
    public string EntranceArea { get; set; } = "";

    /// <summary>
    /// The FullName of the entrance gate (e.g., "m31_05_00_00_AEG099_230_9001").
    /// </summary>
    [JsonPropertyName("entrance_gate")]
    public string EntranceGate { get; set; } = "";

    public override string ToString()
    {
        return $"{ExitArea} --[{ExitGate}]--> {EntranceArea} via [{EntranceGate}]";
    }
}
