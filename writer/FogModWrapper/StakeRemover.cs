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
    /// Stakes to remove. Each entry specifies the MSB map and the RetryPartName
    /// (the asset name that the RetryPoint event references in the MSB).
    /// </summary>
    private static readonly (string Map, string Name)[] StakesToRemove =
    {
        // caelid_radahn: vanilla stake respawns in caelid_preradahn (outside DAG).
        // RetryPoint in m60_12_09_02, asset m60_51_36_00-AEG099_502_2000.
        ("m60_12_09_02", "m60_51_36_00-AEG099_502_2000"),
    };

    /// <summary>
    /// Build a list of RetryPoints tagged "remove" for injection into
    /// ann.RetryPoints before GameDataWriterE.Write().
    /// </summary>
    public static List<AnnotationData.RetryPoint> GetRetryPointsToRemove()
    {
        var retryPoints = new List<AnnotationData.RetryPoint>();

        foreach (var (map, name) in StakesToRemove)
        {
            var rp = new AnnotationData.RetryPoint
            {
                Map = map,
                Name = name,
                Tags = "remove",
            };
            retryPoints.Add(rp);
        }

        Console.WriteLine($"Tagged {retryPoints.Count} vanilla stakes for removal by FogMod");
        return retryPoints;
    }
}
