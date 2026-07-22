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
/// spatial bound is NOT handled here: the RetryPoint's cylinder region
/// takes precedence over UnkT08, and its radius is already set at
/// annotation time via Area.StakeRadius
/// (MapSplitsInjector.SyntheticBossStakeRadius).
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
        IReadOnlyList<(string AreaName, string Map)> candidates,
        List<Connection> connections)
    {
        foreach (var (areaName, mapId) in candidates)
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
            if (!ApplyToMsb(msb, areaName, conn.FlagId))
            {
                Console.Error.WriteLine(
                    $"  Warning: no '{areaName} stake' RetryPoint in {mapId}, nothing rescoped");
                continue;
            }
            msb.Write(msbPath);
            Console.WriteLine(
                $"BossStakePatcher: '{areaName}' stake activation rescoped to flag {conn.FlagId}");
        }
    }

    /// <summary>
    /// Set the activation flag on the "&lt;area&gt; stake" RetryPoint
    /// (FogMod's naming for the stakes it creates), leaving its activation
    /// region and respawn part untouched. Returns false when the map has no
    /// such RetryPoint.
    /// </summary>
    public static bool ApplyToMsb(MSBE msb, string areaName, long activationFlag)
    {
        var rp = msb.Events.RetryPoints.Find(r => r.Name == $"{areaName} stake");
        if (rp == null)
            return false;
        rp.EventFlagID = checked((uint)activationFlag);
        return true;
    }
}
