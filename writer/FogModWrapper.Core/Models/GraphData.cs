using System.Text.Json.Serialization;

namespace FogModWrapper.Models;

/// <summary>
/// Root structure for graph.json format.
/// </summary>
public class GraphData
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "4.0";

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

    /// <summary>
    /// Good IDs to award at game start using DirectlyGivePlayerItem.
    /// Unlike ItemLots, these are not affected by Item Randomizer.
    /// </summary>
    [JsonPropertyName("starting_goods")]
    public List<int> StartingGoods { get; set; } = new();

    [JsonPropertyName("starting_runes")]
    public int StartingRunes { get; set; } = 0;

    [JsonPropertyName("starting_golden_seeds")]
    public int StartingGoldenSeeds { get; set; } = 0;

    [JsonPropertyName("starting_sacred_tears")]
    public int StartingSacredTears { get; set; } = 0;

    /// <summary>
    /// Mapping of event flag ID (as string) to cluster ID.
    /// Used by racing mod to detect zone transitions via EMEVD flags.
    /// </summary>
    [JsonPropertyName("event_map")]
    public Dictionary<string, string> EventMap { get; set; } = new();

    /// <summary>
    /// Event flag ID set when the final boss is defeated.
    /// </summary>
    [JsonPropertyName("finish_event")]
    public int FinishEvent { get; set; } = 0;

    /// <summary>
    /// Randomized starting build items (care package).
    /// Each item has a type (Weapon/Protector/Accessory/Goods), param row ID, and display name.
    /// </summary>
    [JsonPropertyName("care_package")]
    public List<CarePackageItem> CarePackage { get; set; } = new();
}

/// <summary>
/// A single care package item to give the player at game start.
/// </summary>
public class CarePackageItem
{
    /// <summary>
    /// Item type: 0=Weapon, 1=Protector, 2=Accessory, 3=Goods.
    /// Maps to EMEDF ItemType enum for DirectlyGivePlayerItem.
    /// </summary>
    [JsonPropertyName("type")]
    public int Type { get; set; }

    /// <summary>
    /// Param row ID (with upgrade level encoded for weapons).
    /// </summary>
    [JsonPropertyName("id")]
    public int Id { get; set; }

    /// <summary>
    /// Display name for logging/spoiler.
    /// </summary>
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
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

    /// <summary>
    /// Event flag ID to set when this connection's fog gate is traversed.
    /// Maps to the destination node's flag in event_map.
    /// </summary>
    [JsonPropertyName("flag_id")]
    public int FlagId { get; set; } = 0;

    public override string ToString()
    {
        return $"{ExitArea} --[{ExitGate}]--> {EntranceArea} via [{EntranceGate}]";
    }
}
