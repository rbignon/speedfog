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

    /// <summary>
    /// Estimate the ground Y at a gate from hand-placed vanilla parts nearby.
    /// The gate origin's own Y is often not exactly at floor level (survey
    /// over every m30/m31/m32 fog gate and dungeon door: ~85% within 0.3m of
    /// the neighborhood median, outliers up to 1.7m). Takes the median Y of
    /// candidates within horizontalRadius meters of the gate; falls back to
    /// the gate's own Y with fewer than two candidates, and clamps the
    /// correction to maxCorrection.
    ///
    /// The vertical acceptance window is ASYMMETRIC ([-maxBelow, +maxAbove]):
    /// floor evidence sitting well above the gate origin is almost always
    /// wall-mounted decor, not floor (Shadow Keep gate AEG099_230_9500: two
    /// wall props at +2.3m outvoted the single floor asset and pulled
    /// decorations to mid-gate height under the old symmetric window), and
    /// the failure costs are asymmetric too: a floating prop is glaring, a
    /// slightly sunken one reads as settled. Downward corrections (the
    /// "floating bloodstain" case) stay fully allowed.
    ///
    /// The correction is applied only when the selected candidates agree
    /// within consensusSpread meters (max - min); a mixed-level neighborhood
    /// (stairs, ledges) falls back to the gate Y instead of trusting an
    /// arbitrary median between levels.
    /// </summary>
    internal static float EstimateGroundY(
        Vector3 gatePos, IEnumerable<Vector3> candidates,
        float horizontalRadius = 6f, float maxBelow = 2.5f, float maxAbove = 0.5f,
        float consensusSpread = 0.75f, float maxCorrection = 2f)
    {
        var ys = new List<float>();
        foreach (var pos in candidates)
        {
            float dx = pos.X - gatePos.X;
            float dz = pos.Z - gatePos.Z;
            if (dx * dx + dz * dz > horizontalRadius * horizontalRadius)
                continue;
            float dy = pos.Y - gatePos.Y;
            if (dy < -maxBelow || dy > maxAbove)
                continue;
            ys.Add(pos.Y);
        }
        if (ys.Count < 2)
            return gatePos.Y;

        ys.Sort();
        if (ys[^1] - ys[0] > consensusSpread)
            return gatePos.Y;

        float median = ys.Count % 2 == 1
            ? ys[ys.Count / 2]
            : (ys[ys.Count / 2 - 1] + ys[ys.Count / 2]) / 2f;
        return gatePos.Y + Math.Clamp(median - gatePos.Y, -maxCorrection, maxCorrection);
    }

    /// <summary>
    /// Deterministic yaw angles (degrees, [0, 360)) for parts placed around
    /// a gate. Seeded like GenerateArcOffsets but XORed with a fixed
    /// constant so the yaw stream never replays the arc angle/radius stream
    /// (which would correlate a part's facing with its position).
    /// </summary>
    internal static float[] GenerateYaws(uint seedEntityId, int count)
    {
        var rng = new Random(seedEntityId.GetHashCode() ^ 0x5F375A86);
        var yaws = new float[count];
        for (int i = 0; i < count; i++)
            yaws[i] = (float)(rng.NextDouble() * 360.0);
        return yaws;
    }
}
