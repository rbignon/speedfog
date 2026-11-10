using System.Diagnostics;

namespace StaticModBuilder.Winter;

/// <summary>Runs the winter build steps in order into the winter mod folder.</summary>
public static class WinterModBuilder
{
    public const string FailurePrefix = "WINTER FAILED: ";

    /// <summary>Sibling file "&lt;winterDir&gt;.report.txt" (trailing separators ignored).</summary>
    public static string ReportPath(string winterDir) =>
        winterDir.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + ".report.txt";

    public static bool Build(string gameDir, string winterDir, string dataDir)
    {
        var clock = Stopwatch.StartNew();
        try
        {
            if (Directory.Exists(winterDir))
            {
                Directory.Delete(winterDir, true);
            }
            Directory.CreateDirectory(winterDir);

            var report = new WinterReport();
            var catalog = WinterCatalogLoader.LoadMaterials(Path.Combine(dataDir, "plugins", "winter_materials.toml"));
            var stems = WinterCatalogLoader.LoadTextures(Path.Combine(dataDir, "plugins", "winter_textures.toml"));
            Console.WriteLine("Winter: catalogues loaded");

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

            var materials = WinterMaterialBuilder.Build(gameDir, winterDir, catalog, skinned, report);
            Console.WriteLine($"Winter: binders done, {materials.Converted} conversions, "
                + $"{materials.RetargetedTextures + materials.RetargetedTerrain} retargets, "
                + $"{materials.SkippedSkinned} skipped skinned, {materials.SkippedLayout} skipped layout");

            var tpfs = WinterTextureBuilder.Build(gameDir, winterDir, stems, report);
            Console.WriteLine($"Winter: textures done, {tpfs} TPFs");

            report.Write(ReportPath(winterDir));
            Console.WriteLine($"Winter mod: {materials.Converted} conversions, "
                + $"{materials.RetargetedTextures + materials.RetargetedTerrain} retargets, "
                + $"{tpfs} TPFs, {skinned.Count} skinned exclusions, {clock.Elapsed.TotalSeconds:F0} s");
            return true;
        }
        catch (Exception e) when (e is WinterBuildException or InvalidDataException or IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine(FailurePrefix + e.Message);
            try
            {
                if (Directory.Exists(winterDir))
                {
                    Directory.Delete(winterDir, true);
                }
            }
            catch (Exception cleanup) when (cleanup is IOException or UnauthorizedAccessException)
            {
                Console.Error.WriteLine(FailurePrefix + "could not remove " + winterDir + ": " + cleanup.Message);
            }
            return false;
        }
    }
}
