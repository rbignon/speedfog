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

    public string TargetMap { get; set; } = "";
    public uint WarpRegionId { get; set; }
    public FogEntryData? EntryFogData { get; set; }

    public int SourceTier { get; set; }
    public int TargetTier { get; set; }

    public byte[] TargetMapBytes => PathHelper.ParseMapId(TargetMap);
}
