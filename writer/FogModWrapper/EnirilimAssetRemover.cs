using FogModWrapper.Models;

namespace FogModWrapper;

/// <summary>
/// FogMod-created MSB assets in Enir-Ilim (m20_01_00_00) that block the climb.
/// FogMod creates the Outer Wall thorns barrier as two assets
/// (AEG410_901/AEG410_905) sharing EntityGroup 20006662 and only disables it
/// when flag 330 is ON, which SpeedFog keeps OFF; delete them by group match.
/// Hardcoded-list pattern mirrored from StakeRemover (game IDs live in C#).
/// VanillaWarpRemover skips maps absent from the seed output, so this is a
/// no-op when m20_01 is not part of the run.
/// </summary>
public static class EnirilimAssetRemover
{
    private static readonly (string Map, int EntityId, bool MatchGroup)[] AssetsToRemove =
    {
        ("m20_01_00_00", 20006662, true),  // Outer Wall thorns (EntityGroup)
    };

    public static List<RemoveEntity> GetEntities() => AssetsToRemove
        .Select(a => new RemoveEntity { Map = a.Map, EntityId = a.EntityId, MatchGroup = a.MatchGroup })
        .ToList();
}
