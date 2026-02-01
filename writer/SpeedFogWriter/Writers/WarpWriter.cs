// writer/SpeedFogWriter/Writers/WarpWriter.cs
using SoulsFormats;
using SpeedFogWriter.Models;
using System.Numerics;

namespace SpeedFogWriter.Writers;

public class WarpWriter
{
    private readonly Dictionary<string, MSBE> _msbs;
    private readonly FogDataFile _fogData;

    public WarpWriter(Dictionary<string, MSBE> msbs, FogDataFile fogData)
    {
        _msbs = msbs;
        _fogData = fogData;
    }

    public void CreateSpawnRegion(FogGateEvent fogGate)
    {
        if (!_msbs.TryGetValue(fogGate.TargetMap, out var msb))
        {
            Console.WriteLine($"  Warning: MSB not loaded for {fogGate.TargetMap}");
            return;
        }

        Vector3 position;
        Vector3 rotation = Vector3.Zero;

        if (fogGate.EntryFogData != null)
        {
            if (fogGate.EntryFogData.HasPosition)
            {
                position = fogGate.EntryFogData.PositionVec;
                rotation = fogGate.EntryFogData.RotationVec;
            }
            else
            {
                (position, rotation) = GetFogPositionFromMsb(fogGate.EntryFogData, msb);
            }
        }
        else
        {
            Console.WriteLine($"  Warning: No entry fog for {fogGate.TargetClusterId}, using origin");
            position = Vector3.Zero;
        }

        var spawnRegion = new MSBE.Region.SpawnPoint
        {
            Name = $"SpeedFog_Spawn_{fogGate.WarpRegionId}",
            EntityID = fogGate.WarpRegionId,
            Position = position,
            Rotation = OppositeRotation(rotation)
        };

        msb.Regions.SpawnPoints.Add(spawnRegion);
    }

    private (Vector3 Position, Vector3 Rotation) GetFogPositionFromMsb(FogEntryData fog, MSBE msb)
    {
        MSBE.Part.Asset? asset = fog.LookupBy switch
        {
            "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.AssetName),
            "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == (uint)fog.EntityId),
            _ => null
        };

        if (asset != null)
            return (asset.Position, asset.Rotation);

        // Fallback: partial match
        asset = msb.Parts.Assets.FirstOrDefault(a =>
            a.Name.StartsWith(fog.Model) && !string.IsNullOrEmpty(fog.AssetName) &&
            a.Name.Contains(fog.AssetName.Split('_').LastOrDefault() ?? ""));

        if (asset != null)
        {
            Console.WriteLine($"  Warning: Used partial match for fog {fog.AssetName} in {fog.Map}");
            return (asset.Position, asset.Rotation);
        }

        Console.WriteLine($"  Warning: Could not find fog asset {fog.AssetName} in {fog.Map}");
        return (Vector3.Zero, Vector3.Zero);
    }

    private static Vector3 OppositeRotation(Vector3 rotation)
    {
        var y = rotation.Y + 180f;
        y = y >= 180f ? y - 360f : y;
        return new Vector3(rotation.X, y, rotation.Z);
    }
}
