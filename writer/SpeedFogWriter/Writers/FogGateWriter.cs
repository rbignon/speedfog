// writer/SpeedFogWriter/Writers/FogGateWriter.cs
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

public class FogGateWriter
{
    private readonly FogDataFile _fogData;
    private readonly ClusterFile _clusterData;
    private readonly EntityIdAllocator _idAllocator;

    public FogGateWriter(FogDataFile fogData, ClusterFile clusterData, EntityIdAllocator idAllocator)
    {
        _fogData = fogData;
        _clusterData = clusterData;
        _idAllocator = idAllocator;
    }

    public List<FogGateEvent> CreateFogGates(SpeedFogGraph graph)
    {
        var events = new List<FogGateEvent>();

        foreach (var edge in graph.Edges)
        {
            var source = graph.GetNode(edge.Source);
            var target = graph.GetNode(edge.Target);

            if (source == null || target == null)
            {
                Console.WriteLine($"  Warning: Invalid edge {edge.Source} -> {edge.Target}");
                continue;
            }

            // Use source node's zones to disambiguate fog lookup
            // Many fogs share the same asset name (e.g., AEG099_001_9000) across different maps
            // The zone context ensures we get the fog from the correct map
            // Try each zone until we find a match
            FogEntryData? exitFogData = null;
            foreach (var zone in source.Zones)
            {
                exitFogData = _fogData.GetFog(edge.FogId, zone);
                if (exitFogData != null) break;
            }
            // Fallback: try without zone context
            exitFogData ??= _fogData.GetFog(edge.FogId);
            if (exitFogData == null)
            {
                Console.WriteLine($"  Warning: Missing fog data for {edge.FogId} (zones: {string.Join(", ", source.Zones)})");
                continue;
            }

            var targetMap = _clusterData.GetMapForCluster(target.Zones);
            if (targetMap == null)
            {
                Console.WriteLine($"  Warning: Cannot determine map for cluster {target.ClusterId}");
                continue;
            }

            var primaryEntryFog = target.PrimaryEntryFog;
            var entryFogData = primaryEntryFog != null ? _fogData.GetFog(primaryEntryFog) : null;

            var fogEvent = new FogGateEvent
            {
                EventId = _idAllocator.AllocateEventId(),
                FlagId = _idAllocator.AllocateFlagId(),
                EdgeFogId = edge.FogId,
                SourceNodeId = source.Id,
                TargetNodeId = target.Id,
                SourceClusterId = source.ClusterId,
                TargetClusterId = target.ClusterId,
                TargetZones = target.Zones,

                SourceMap = exitFogData.Map,
                FogEntityId = exitFogData.EntityId,
                FogModel = exitFogData.Model,
                FogAssetName = exitFogData.AssetName,
                FogLookupBy = exitFogData.LookupBy,

                // MakeFrom fog data (for dynamic asset creation)
                IsMakeFrom = exitFogData.IsMakeFrom,
                FogPosition = exitFogData.PositionVec,
                FogRotation = exitFogData.RotationVec,

                TargetMap = targetMap,
                WarpRegionId = _idAllocator.AllocateRegionId(),
                EntryFogData = entryFogData,

                SourceTier = source.Tier,
                TargetTier = target.Tier,
            };

            events.Add(fogEvent);
        }

        return events;
    }
}
