using System.Numerics;

namespace FogModWrapper;

/// <summary>
/// Shared fog gate geometry helpers: FullName parsing, gate-side resolution,
/// and deterministic arc offset generation around a gate. Extracted from
/// <see cref="DeathMarkerInjector"/> so other injectors placing content near
/// fog gates can reuse the same math.
/// </summary>
internal static class GateGeometry
{
    /// <summary>
    /// Parse a gate FullName like "m10_01_00_00_AEG099_001_9000" into (mapId, partName).
    /// </summary>
    internal static (string MapId, string PartName) ParseGateFullName(string fullName)
    {
        var parts = fullName.Split('_');
        if (parts.Length < 5)
            throw new ArgumentException($"Invalid gate FullName (too few segments): {fullName}");

        var mapId = string.Join("_", parts[0], parts[1], parts[2], parts[3]);
        var partName = string.Join("_", parts.Skip(4));
        return (mapId, partName);
    }

    /// <summary>
    /// Determine if the approach area is on the ASide (gate facing direction) of the gate.
    /// ASide = forward direction of the fog gate model (based on Y rotation).
    /// BSide = opposite direction (180 degrees from facing).
    /// Falls back to BSide (current behavior) if the gate or area is not found.
    /// </summary>
    internal static bool ResolveIsASide(
        string gateFullName, string approachArea,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        if (!gateSides.TryGetValue(gateFullName, out var sides))
            return false; // default: BSide (legacy behavior)

        if (sides.ASideArea == approachArea)
            return true;
        if (sides.BSideArea == approachArea)
            return false;

        // Area not found on either side (zone name mismatch). Fall back to BSide.
        Console.WriteLine($"Warning: Area '{approachArea}' not on either side of gate {gateFullName}" +
            $" (A={sides.ASideArea}, B={sides.BSideArea}), defaulting to BSide");
        return false;
    }

    /// <summary>
    /// Generate offsets around a gate, spread across an arc centered on
    /// arcCenterDeg. PRNG seeded on seedEntityId for deterministic placement.
    /// The per-marker draw order (angle then radius) and the sector math must
    /// stay byte-identical across callers so existing placements do not shift.
    /// </summary>
    internal static Vector3[] GenerateArcOffsets(
        uint seedEntityId, float gateRotY, float arcCenterDeg, int count,
        float minRadius, float maxRadius, float yOffset, float arcSpreadDeg = 120f)
    {
        var rng = new Random(seedEntityId.GetHashCode());
        var offsets = new Vector3[count];
        float gateRad = gateRotY * MathF.PI / 180f;

        float sectorSize = arcSpreadDeg / count;
        float arcStart = arcCenterDeg - arcSpreadDeg / 2f;

        for (int i = 0; i < count; i++)
        {
            float sectorStart = arcStart + i * sectorSize;
            float angleDeg = sectorStart + (float)(rng.NextDouble() * sectorSize);
            float angleRad = angleDeg * MathF.PI / 180f;
            float radius = minRadius + (float)(rng.NextDouble() * (maxRadius - minRadius));

            float localX = MathF.Sin(angleRad) * radius;
            float localZ = MathF.Cos(angleRad) * radius;

            float worldX = localX * MathF.Cos(gateRad) + localZ * MathF.Sin(gateRad);
            float worldZ = -localX * MathF.Sin(gateRad) + localZ * MathF.Cos(gateRad);

            offsets[i] = new Vector3(worldX, yOffset, worldZ);
        }

        return offsets;
    }
}
