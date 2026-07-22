using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Rescopes the activation FLAG of Stakes of Marika that FogMod created for
/// BossTrigger-less boss areas (the map-splits synthetic-arena case, see
/// MapSplitsInjector.EnsureBossStakePos). FogMod's fallback for those is
/// EventFlagID 6001 (always on): combined with the stake's activation
/// cylinder region, dying anywhere within the cylinder offered the chapel
/// respawn, even before ever entering the arena. Real arenas instead gate
/// their stake on the fight flag (BossTrigger).
///
/// The patch reuses the boss cluster's zone-tracking entry flag (set by the
/// fogwarp right before warping into the arena, graph.json `connections`),
/// so the stake only activates once the player has actually entered. The
/// spatial bound is the RetryPoint's region (it takes precedence over
/// UnkT08): by default the StakeRadius cylinder set at annotation time
/// (MapSplitsInjector.SyntheticBossStakeRadius), replaced here by a
/// world-aligned box when the fog declares a stake_region AABB, since a
/// cylinder centered on the stake at the gate necessarily bleeds through
/// the fog wall.
/// </summary>
public static class BossStakePatcher
{
    /// <summary>
    /// Patch the stakes of the given boss areas. Each candidate is the boss
    /// area name plus the map its gate (and therefore its stake) lives in;
    /// the activation flag is the graph connection entering the area. Areas
    /// without a connection this seed have no stake to patch (no connected
    /// main entrance means FogMod created none).
    /// </summary>
    public static void Patch(
        string modDir,
        IReadOnlyList<(string AreaName, string Map, List<float>? StakeRegion)> candidates,
        List<Connection> connections)
    {
        foreach (var (areaName, mapId, stakeRegion) in candidates)
        {
            var conn = connections.FirstOrDefault(c => c.EntranceArea == areaName);
            if (conn == null)
                continue;

            var msbPath = MsbHelper.FindMsbPath(modDir, $"{mapId}.msb.dcx");
            if (msbPath == null)
            {
                Console.Error.WriteLine(
                    $"  Warning: {mapId}.msb.dcx not found, stake for '{areaName}' not rescoped");
                continue;
            }

            var msb = MSBE.Read(msbPath);
            if (!ApplyToMsb(msb, areaName, conn.FlagId, stakeRegion, out bool boxApplied))
            {
                Console.Error.WriteLine(
                    $"  Warning: no '{areaName} stake' RetryPoint in {mapId}, nothing rescoped");
                continue;
            }
            msb.Write(msbPath);
            Console.WriteLine(
                $"BossStakePatcher: '{areaName}' stake activation rescoped to flag {conn.FlagId}"
                + (boxApplied ? ", box region applied" : ""));
        }
    }

    /// <summary>
    /// Set the activation flag on the "&lt;area&gt; stake" RetryPoint
    /// (FogMod's naming for the stakes it creates) and, when
    /// <paramref name="stakeRegion"/> is given ([x1, y1, z1, x2, y2, z2]
    /// AABB from map_splits.toml), reshape the RetryPoint's activation
    /// region into that box. FogMod's cylinder is centered on the stake AT
    /// the gate, so any radius bleeds symmetrically through the fog wall
    /// into the neighbouring zone; a box can sit flush with the wall.
    /// Returns false when the map has no such RetryPoint.
    /// </summary>
    public static bool ApplyToMsb(
        MSBE msb, string areaName, long activationFlag, List<float>? stakeRegion = null)
        => ApplyToMsb(msb, areaName, activationFlag, stakeRegion, out _);

    public static bool ApplyToMsb(
        MSBE msb, string areaName, long activationFlag, List<float>? stakeRegion,
        out bool boxApplied)
    {
        boxApplied = false;
        var rp = msb.Events.RetryPoints.Find(r => r.Name == $"{areaName} stake");
        if (rp == null)
            return false;
        rp.EventFlagID = checked((uint)activationFlag);

        if (stakeRegion != null && rp.RetryRegionName != null)
        {
            var region = msb.Regions.GetEntries()
                .FirstOrDefault(r => r.Name == rp.RetryRegionName);
            if (region == null)
            {
                Console.Error.WriteLine(
                    $"  Warning: stake region '{rp.RetryRegionName}' not found, box not applied");
                return true;
            }
            float x1 = Math.Min(stakeRegion[0], stakeRegion[3]);
            float y1 = Math.Min(stakeRegion[1], stakeRegion[4]);
            float z1 = Math.Min(stakeRegion[2], stakeRegion[5]);
            float x2 = Math.Max(stakeRegion[0], stakeRegion[3]);
            float y2 = Math.Max(stakeRegion[1], stakeRegion[4]);
            float z2 = Math.Max(stakeRegion[2], stakeRegion[5]);
            region.Shape = new MSB.Shape.Box
            {
                Width = x2 - x1,
                Depth = z2 - z1,
                Height = y2 - y1,
            };
            // MSB box shapes anchor at the bottom-center of the volume.
            region.Position = new System.Numerics.Vector3(
                (x1 + x2) / 2f, y1, (z1 + z2) / 2f);
            region.Rotation = default;
            boxApplied = true;
        }
        return true;
    }
}
