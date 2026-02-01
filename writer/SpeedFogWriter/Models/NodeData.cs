// writer/SpeedFogWriter/Models/NodeData.cs
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class NodeData
{
    [JsonIgnore]
    public string Id { get; set; } = "";

    [JsonPropertyName("cluster_id")]
    public string ClusterId { get; set; } = "";

    [JsonPropertyName("zones")]
    public List<string> Zones { get; set; } = new();

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("layer")]
    public int Layer { get; set; }

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("entry_fogs")]
    public List<string> EntryFogs { get; set; } = new();

    [JsonPropertyName("exit_fogs")]
    public List<string> ExitFogs { get; set; } = new();

    // Convenience properties for node type checking
    public bool IsStart => Type == "start";
    public bool IsFinalBoss => Type == "final_boss";
    public bool IsLegacyDungeon => Type == "legacy_dungeon";
    public bool IsMiniDungeon => Type == "mini_dungeon";
    public bool IsBossArena => Type == "boss_arena";
    public bool IsMajorBoss => Type == "major_boss";

    /// <summary>
    /// True if this node has multiple entry fogs (merge point in the DAG).
    /// </summary>
    public bool IsMergePoint => EntryFogs.Count > 1;

    public string PrimaryZone => Zones.FirstOrDefault() ?? "";
    public string? PrimaryEntryFog => EntryFogs.FirstOrDefault();
}
