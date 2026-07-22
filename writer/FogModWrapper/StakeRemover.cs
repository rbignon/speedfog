using FogMod;

namespace FogModWrapper;

/// <summary>
/// Provides RetryPoint entries tagged "remove" for vanilla Stakes of Marika
/// that respawn the player outside the SpeedFog DAG.
///
/// Game MSBs are stored in BHD/BDT archives, not as loose files. FogMod's
/// GameDataWriterE reads from these archives and handles RetryPoint removal
/// for entries tagged "remove". We inject these into ann.RetryPoints before
/// Write() so FogMod handles the extraction, removal, and writing.
///
/// See: GameDataWriterE.cs lines 4452-4458 for FogMod's "remove" tag logic.
/// </summary>
public static class StakeRemover
{
    /// <summary>
    /// Build a list of RetryPoints tagged "remove" for injection into
    /// ann.RetryPoints before GameDataWriterE.Write(), from the
    /// [[stake_removals]] entries of data/game_tweaks.toml.
    /// </summary>
    public static List<AnnotationData.RetryPoint> GetRetryPointsToRemove(
        IReadOnlyList<StakeRemoval> stakes)
    {
        var retryPoints = new List<AnnotationData.RetryPoint>();

        foreach (var stake in stakes)
        {
            retryPoints.Add(new AnnotationData.RetryPoint
            {
                Map = stake.Map,
                Name = stake.Name,
                Tags = "remove",
            });
        }

        Console.WriteLine($"Tagged {retryPoints.Count} vanilla stakes for removal by FogMod");
        return retryPoints;
    }
}
