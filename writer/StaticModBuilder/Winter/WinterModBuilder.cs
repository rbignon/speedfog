using System.Diagnostics;

namespace StaticModBuilder.Winter;

/// <summary>
/// Runs the winter build steps in order into a staging folder next to the winter mod folder,
/// writes the report, then moves the staging folder into place: the winter folder either holds
/// a complete build or does not exist.
/// </summary>
public static class WinterModBuilder
{
    public const string FailurePrefix = "WINTER FAILED: ";

    /// <summary>Suffix of the sibling staging folder the build writes into.</summary>
    public const string StagingSuffix = ".building";

    /// <summary>Sibling file "&lt;winterDir&gt;.report.txt" (trailing separators ignored).</summary>
    public static string ReportPath(string winterDir) => TrimSeparators(winterDir) + ".report.txt";

    /// <summary>Sibling folder "&lt;winterDir&gt;.building" (trailing separators ignored).</summary>
    public static string StagingPath(string winterDir) => TrimSeparators(winterDir) + StagingSuffix;

    /// <summary>
    /// Error message when the winter folder is, or contains, one of the other folders (labelled
    /// paths, null entries ignored): the build deletes the winter folder, so it must not take the
    /// game, the static mod or the data folder with it. Null when the winter folder is safe.
    /// </summary>
    public static string? CheckWinterDir(string winterDir, params (string Label, string? Dir)[] others)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string winter = TrimSeparators(Path.GetFullPath(winterDir));
        foreach (var (label, dir) in others)
        {
            if (dir == null)
            {
                continue;
            }
            string other = TrimSeparators(Path.GetFullPath(dir));
            if (string.Equals(other, winter, comparison))
            {
                return $"--winter-dir must not be the {label} ({dir}): the winter build deletes it";
            }
            if (other.StartsWith(winter + Path.DirectorySeparatorChar, comparison))
            {
                return $"--winter-dir must not contain the {label} ({dir}): the winter build deletes it";
            }
        }
        return null;
    }

    public static bool Build(string gameDir, string winterDir, string dataDir) =>
        Build(winterDir, (stagingDir, report) => RunSteps(gameDir, stagingDir, dataDir, report));

    /// <summary>
    /// Removes the stale staging folder, winter folder and report, runs <paramref name="steps"/>
    /// into the staging folder (it fills the report and returns the summary line), writes the
    /// report and moves the staging folder into place. On any exception the staging folder, the
    /// winter folder and the report are removed; input failures (<see cref="IsInputFailure"/>)
    /// are reported as "WINTER FAILED: &lt;reason&gt;" and return false, anything else is a bug
    /// and is rethrown.
    /// </summary>
    internal static bool Build(string winterDir, Func<string, WinterReport, string> steps)
    {
        string finalDir = TrimSeparators(winterDir);
        string stagingDir = StagingPath(winterDir);
        string reportPath = ReportPath(winterDir);
        try
        {
            DeleteDirectory(stagingDir);
            DeleteDirectory(finalDir);
            DeleteFile(reportPath);
            Directory.CreateDirectory(stagingDir);

            var report = new WinterReport();
            string summary = steps(stagingDir, report);
            report.Write(reportPath);
            Directory.Move(stagingDir, finalDir);
            Console.WriteLine(summary);
            return true;
        }
        catch (Exception e) when (IsInputFailure(e))
        {
            Console.Error.WriteLine(FailurePrefix + e.Message);
            CleanUp(finalDir, stagingDir, reportPath);
            return false;
        }
        catch
        {
            CleanUp(finalDir, stagingDir, reportPath);
            throw;
        }
    }

    /// <summary>Exceptions a bad game folder or catalogue produces, as opposed to a bug.</summary>
    internal static bool IsInputFailure(Exception e) =>
        e is WinterBuildException or InvalidDataException or IOException or UnauthorizedAccessException;

    private static string RunSteps(string gameDir, string outputDir, string dataDir, WinterReport report)
    {
        var clock = Stopwatch.StartNew();
        var catalog = WinterCatalogLoader.LoadMaterials(Path.Combine(dataDir, "plugins", "winter_materials.toml"));
        var stems = WinterCatalogLoader.LoadTextures(Path.Combine(dataDir, "plugins", "winter_textures.toml"));
        Console.WriteLine("Winter: catalogues loaded");

        // Before the scan and any write: no matbin may point at a texture the game does not ship.
        WinterMaterialBuilder.VerifyRetargetTargets(gameDir, catalog.Retargets);
        Console.WriteLine("Winter: retarget targets verified");

        var skinned = SkinnedMaterialScanner.Scan(gameDir, report, (done, total) =>
        {
            if (done % 1000 == 0)
            {
                Console.WriteLine($"Winter: scanned {done}/{total} binders");
            }
        });
        Console.WriteLine($"Winter: scan done, {skinned.Count} skinned materials, {clock.Elapsed.TotalSeconds:F0} s");

        var materials = WinterMaterialBuilder.Build(gameDir, outputDir, catalog, skinned, report);
        Console.WriteLine($"Winter: binders done, {materials.Converted} conversions, "
            + $"{materials.RetargetedTextures + materials.RetargetedTerrain} retargets, "
            + $"{materials.SkippedSkinned} skipped skinned, {materials.SkippedLayout} skipped layout");

        var tpfs = WinterTextureBuilder.Build(gameDir, outputDir, stems, report);
        Console.WriteLine($"Winter: textures done, {tpfs} TPFs");

        return $"Winter mod: {materials.Converted} conversions, "
            + $"{materials.RetargetedTextures + materials.RetargetedTerrain} retargets, "
            + $"{tpfs} TPFs, {materials.SkippedSkinned} skinned exclusions, {clock.Elapsed.TotalSeconds:F0} s";
    }

    /// <summary>Best-effort removal of everything a failed build may have left.</summary>
    private static void CleanUp(string finalDir, string stagingDir, string reportPath)
    {
        var removals = new Action[] { () => DeleteDirectory(stagingDir), () => DeleteDirectory(finalDir), () => DeleteFile(reportPath) };
        foreach (var remove in removals)
        {
            try
            {
                remove();
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(FailurePrefix + "could not clean up: " + cleanup.Message);
            }
        }
    }

    private static void DeleteDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            Directory.Delete(path, true);
        }
    }

    private static void DeleteFile(string path)
    {
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }

    private static string TrimSeparators(string path) =>
        path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
}
