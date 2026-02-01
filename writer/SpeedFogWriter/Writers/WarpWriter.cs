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
                // MakeFrom fogs have inline positions
                position = fogGate.EntryFogData.PositionVec;
                rotation = fogGate.EntryFogData.RotationVec;
            }
            else
            {
                // IMPORTANT: Entry fog is in its own map, not necessarily the target map
                // Look up in the correct MSB based on the fog's map
                var entryFogMap = fogGate.EntryFogData.Map;
                if (_msbs.TryGetValue(entryFogMap, out var entryMsb))
                {
                    (position, rotation) = GetFogPositionFromMsb(fogGate.EntryFogData, entryMsb);
                }
                else
                {
                    Console.WriteLine($"  Warning: MSB not loaded for entry fog map {entryFogMap}");
                    position = Vector3.Zero;
                    rotation = Vector3.Zero;
                }
            }
        }
        else
        {
            Console.WriteLine($"  Warning: No entry fog for {fogGate.TargetClusterId}, using origin");
            position = Vector3.Zero;
        }

        // Offset spawn position by 1 unit away from the fog gate
        // This prevents the player from spawning directly on the fog and getting stuck
        // FogRando uses dist=1f (GameDataWriterE.cs L373)
        var spawnPosition = MoveInDirection(position, rotation, 1f);

        var spawnRegion = new MSBE.Region.SpawnPoint
        {
            Name = $"SpeedFog_Spawn_{fogGate.WarpRegionId}",
            EntityID = fogGate.WarpRegionId,
            Position = spawnPosition,
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

    /// <summary>
    /// Move a position in the direction specified by rotation.
    /// Same as FogRando's moveInDirection helper (GameDataWriterE.cs L5326).
    /// </summary>
    /// <param name="position">Starting position</param>
    /// <param name="rotation">Rotation (Y component is the heading in degrees)</param>
    /// <param name="distance">Distance to move (positive = forward)</param>
    private static Vector3 MoveInDirection(Vector3 position, Vector3 rotation, float distance)
    {
        float radians = rotation.Y * MathF.PI / 180f;
        return new Vector3(
            position.X + MathF.Sin(radians) * distance,
            position.Y,
            position.Z + MathF.Cos(radians) * distance
        );
    }

    private static Vector3 OppositeRotation(Vector3 rotation)
    {
        var y = rotation.Y + 180f;
        y = y >= 180f ? y - 360f : y;
        return new Vector3(rotation.X, y, rotation.Z);
    }
}
