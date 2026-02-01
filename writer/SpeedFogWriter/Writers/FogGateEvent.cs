// writer/SpeedFogWriter/Writers/FogGateEvent.cs
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

public class FogGateEvent
{
    public uint EventId { get; set; }
    public uint FlagId { get; set; }
    public string EdgeFogId { get; set; } = "";
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public string SourceClusterId { get; set; } = "";
    public string TargetClusterId { get; set; } = "";
    public List<string> TargetZones { get; set; } = new();

    public string SourceMap { get; set; } = "";
    public int FogEntityId { get; set; }
    public string FogModel { get; set; } = "";
    public string? FogAssetName { get; set; }
    public string? FogLookupBy { get; set; }

    /// <summary>
    /// True if this fog gate needs to be created dynamically (makefrom type).
    /// These fogs have position data but don't exist as MSB assets.
    /// </summary>
    public bool IsMakeFrom { get; set; }

    /// <summary>
    /// Position for makefrom fogs (from fog_data.json).
    /// </summary>
    public System.Numerics.Vector3 FogPosition { get; set; }

    /// <summary>
    /// Rotation for makefrom fogs (from fog_data.json).
    /// </summary>
    public System.Numerics.Vector3 FogRotation { get; set; }

    public string TargetMap { get; set; } = "";
    public uint WarpRegionId { get; set; }
    public FogEntryData? EntryFogData { get; set; }

    public int SourceTier { get; set; }
    public int TargetTier { get; set; }

    /// <summary>
    /// The type of fog gate: "entrance" (physical fog gate) or "warp" (item-triggered).
    /// </summary>
    public string FogType { get; set; } = "entrance";

    /// <summary>
    /// For warp-type fogs, the SpEffect that triggers the warp.
    /// For example, Pureblood Knight's Medal triggers SpEffect 502160.
    /// </summary>
    public int? TriggerSpEffect { get; set; }

    public byte[] TargetMapBytes => PathHelper.ParseMapId(TargetMap);

    /// <summary>
    /// Known SpEffects for item-triggered warps.
    /// Key: fog ID (e.g., "12052021"), Value: SpEffect ID
    /// Reference: FogRando fogevents.txt event 922
    /// </summary>
    public static readonly Dictionary<string, int> ItemWarpSpEffects = new()
    {
        { "12052021", 502160 },  // Pureblood Knight's Medal
    };

    /// <summary>
    /// Check if this is an item-triggered warp (vs a physical fog gate).
    /// </summary>
    public bool IsItemWarp => FogType == "warp" && TriggerSpEffect.HasValue;
}
