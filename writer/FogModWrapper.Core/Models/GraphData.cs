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
    /// Larval Tears to give at game start for rebirth at Sites of Grace.
    /// When > 0, also injects the rebirth menu option into the grace ESD.
    /// </summary>
    [JsonPropertyName("starting_larval_tears")]
    public int StartingLarvalTears { get; set; } = 10;

    /// <summary>
    /// Mapping of event flag ID (as string) to cluster ID.
    /// Used by racing mod to detect zone transitions via EMEVD flags.
    /// </summary>
    [JsonPropertyName("event_map")]
    public Dictionary<string, string> EventMap { get; set; } = new();

    /// <summary>
    /// Zone-tracking flag for the final boss node.
    /// Used to identify connections leading to the final boss area
    /// and extract its DefeatFlag from FogMod's Graph.
    /// </summary>
    [JsonPropertyName("final_node_flag")]
    public int FinalNodeFlag { get; set; } = 0;

    /// <summary>
    /// Event flag ID set when the final boss is defeated.
    /// Separate from FinalNodeFlag: this fires on boss death, not fog gate traversal.
    /// </summary>
    [JsonPropertyName("finish_event")]
    public int FinishEvent { get; set; } = 0;

    /// <summary>
    /// DefeatFlag for the final boss, propagated from fog.txt through clusters.json.
    /// Primary source for boss death detection. When > 0, takes priority over
    /// the DefeatFlag extracted from FogMod's Graph (which may be missing for
    /// zones like leyndell_erdtree where the boss is in a linked zone).
    /// </summary>
    [JsonPropertyName("finish_boss_defeat_flag")]
    public int FinishBossDefeatFlag { get; set; } = 0;

    /// <summary>
    /// Text displayed as a golden banner after the final boss is defeated.
    /// Configurable via config.toml [run] run_complete_message.
    /// </summary>
    [JsonPropertyName("run_complete_message")]
    public string RunCompleteMessage { get; set; } = "RUN COMPLETE";

    /// <summary>
    /// Whether to add a Site of Grace at Chapel of Anticipation (starting location).
    /// </summary>
    [JsonPropertyName("chapel_grace")]
    public bool ChapelGrace { get; set; } = true;

    /// <summary>
    /// Randomized starting build items (care package).
    /// Each item has a type (Weapon/Protector/Accessory/Goods), param row ID, and display name.
    /// </summary>
    [JsonPropertyName("care_package")]
    public List<CarePackageItem> CarePackage { get; set; } = new();

    /// <summary>
    /// Vanilla warp entities to remove from MSBs.
    /// These are one-way teleporters (coffins, DLC warps) that FogMod marks for removal
    /// but can't actually delete due to a name mismatch in its removal logic.
    /// </summary>
    [JsonPropertyName("remove_entities")]
    public List<RemoveEntity> RemoveEntities { get; set; } = new();
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

/// <summary>
/// A vanilla warp entity to remove from an MSB.
/// </summary>
public class RemoveEntity
{
    /// <summary>
    /// Map ID where the entity exists (e.g., "m12_05_00_00").
    /// </summary>
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    /// <summary>
    /// MSB entity ID to remove (matches Parts.Assets.EntityID).
    /// </summary>
    [JsonPropertyName("entity_id")]
    public int EntityId { get; set; }
}
