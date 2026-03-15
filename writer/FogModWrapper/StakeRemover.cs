using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes vanilla Stake of Marika RetryPoint events from MSBs.
///
/// Some boss zones (e.g. caelid_radahn) have a vanilla RetryPoint that respawns
/// the player in a zone outside the SpeedFog DAG (e.g. caelid_preradahn). FogMod
/// creates its own BossTrigger-controlled stake during the fight, but the vanilla
/// RetryPoint survives and takes over after the boss is defeated, causing a softlock.
///
/// This post-processor removes the vanilla RetryPoint and its associated asset/player
/// parts so the player has no stake after the boss fight (matching FogMod behavior).
/// </summary>
public static class StakeRemover
{
    private static readonly string[] MsbDirVariants = { "mapstudio", "MapStudio" };

    /// <summary>
    /// Stakes to remove. Each entry specifies the MSB map and the RetryPartName
    /// (the asset name that the RetryPoint references).
    /// </summary>
    private static readonly (string Map, string RetryPartName)[] StakesToRemove =
    {
        // caelid_radahn: vanilla stake respawns in caelid_preradahn (outside DAG).
        // RetryPoint in m60_12_09_02, asset m60_51_36_00-AEG099_502_2000.
        ("m60_12_09_02", "m60_51_36_00-AEG099_502_2000"),
    };

    /// <summary>
    /// Remove vanilla stakes from MSBs. Reads from gameDir if the MSB is not
    /// already in modDir, then writes the modified MSB to modDir.
    /// </summary>
    public static void Remove(string modDir, string gameDir)
    {
        Console.WriteLine("Removing vanilla stakes...");
        var totalRemoved = 0;

        foreach (var (mapId, retryPartName) in StakesToRemove)
        {
            var removed = RemoveStake(modDir, gameDir, mapId, retryPartName);
            totalRemoved += removed;
        }

        Console.WriteLine($"  Removed {totalRemoved} vanilla stakes");
    }

    private static int RemoveStake(string modDir, string gameDir, string mapId, string retryPartName)
    {
        var msbFileName = $"{mapId}.msb.dcx";

        // Try mod dir first, then game dir
        var msbPath = FindMsbPath(modDir, msbFileName)
            ?? FindGameMsbPath(gameDir, msbFileName);

        if (msbPath == null)
        {
            Console.WriteLine($"  Warning: MSB not found for {mapId}");
            return 0;
        }

        var msb = MSBE.Read(msbPath);

        var retryPoint = msb.Events.RetryPoints.Find(rp => rp.RetryPartName == retryPartName);
        if (retryPoint == null)
        {
            return 0;
        }

        msb.Events.RetryPoints.Remove(retryPoint);
        Console.WriteLine($"  {mapId}: removed RetryPoint for {retryPartName}");

        // Write to mod dir (may differ from source if read from game dir)
        var outPath = EnsureModMsbPath(modDir, msbFileName);
        msb.Write(outPath);

        return 1;
    }

    private static string? FindMsbPath(string baseDir, string msbFileName)
    {
        foreach (var dirName in MsbDirVariants)
        {
            var path = Path.Combine(baseDir, "map", dirName, msbFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string? FindGameMsbPath(string gameDir, string msbFileName)
    {
        // Game files use PascalCase MapStudio
        var path = Path.Combine(gameDir, "map", "MapStudio", msbFileName);
        return File.Exists(path) ? path : null;
    }

    private static string EnsureModMsbPath(string modDir, string msbFileName)
    {
        // Use lowercase mapstudio for mod output (consistent with FogMod under Wine)
        var dir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, msbFileName);
    }
}
