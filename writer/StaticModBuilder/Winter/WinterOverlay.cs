namespace StaticModBuilder.Winter;

/// <summary>
/// Authored files shipped with the winter mod as they are. <c>data/mods-src/speedfog-winter/</c>
/// mirrors the mod layout (<c>chr/</c> holds the Frozen Reindeer Torrent skin) and every file under
/// it is copied verbatim into the build, except entries whose name starts with a dot (.gitkeep,
/// .DS_Store). The folder is optional: a build without it says so and ships none of them.
/// </summary>
public static class WinterOverlay
{
    /// <summary>The overlay folder of a data directory: &lt;dataDir&gt;/mods-src/speedfog-winter.</summary>
    public static string Locate(string dataDir) => Path.Combine(dataDir, "mods-src", "speedfog-winter");

    /// <summary>
    /// Copies every file under <paramref name="overlayDir"/> into <paramref name="outputDir"/> at the
    /// same relative path and lists them in the report, skipping dot-prefixed files and folders.
    /// Returns the relative paths copied ('/' separated, sorted); empty when the folder is absent
    /// or holds no file. An overlay path the build already wrote (file or folder) is an input
    /// failure, and nothing is copied.
    /// </summary>
    public static IReadOnlyList<string> Copy(string overlayDir, string outputDir, WinterReport report)
    {
        var files = Directory.Exists(overlayDir)
            ? Directory.EnumerateFiles(overlayDir, "*", SearchOption.AllDirectories)
                .Select(path => Path.GetRelativePath(overlayDir, path).Replace(Path.DirectorySeparatorChar, '/'))
                .Where(relative => !relative.Split('/').Any(segment => segment.StartsWith('.')))
                .OrderBy(relative => relative, StringComparer.Ordinal)
                .ToList()
            : new List<string>();
        if (files.Count == 0)
        {
            report.Line($"Overlay: none ({overlayDir} absent or empty)");
            return files;
        }

        foreach (var relative in files)
        {
            string built = Path.Combine(outputDir, relative);
            if (File.Exists(built) || Directory.Exists(built))
            {
                throw new WinterBuildException($"overlay file {relative} collides with a built path ({overlayDir})");
            }
        }

        report.Line($"Overlay: {files.Count} files from {overlayDir}");
        foreach (var relative in files)
        {
            string source = Path.Combine(overlayDir, relative);
            string target = Path.Combine(outputDir, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(source, target);
            report.Line($"  {relative} ({new FileInfo(source).Length} bytes)");
        }
        return files;
    }
}
