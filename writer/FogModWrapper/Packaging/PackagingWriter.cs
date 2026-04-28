namespace FogModWrapper.Packaging;

/// <summary>
/// Orchestrates the final output packaging: ME3 download, config generation, and launchers.
/// </summary>
public class PackagingWriter
{
    private readonly string _outputDir;

    public PackagingWriter(string outputDir)
    {
        _outputDir = outputDir;
    }

    /// <summary>
    /// Writes the complete package: downloads ME3, copies it to output,
    /// and generates config and launcher scripts.
    /// </summary>
    /// <param name="mergeDir">Optional merge directory (Item Randomizer output) - if set, includes RandomizerHelper.dll.</param>
    /// <param name="forceUpdate">Force re-download of ME3.</param>
    public async Task WritePackageAsync(string? mergeDir = null, bool forceUpdate = false)
    {
        Console.WriteLine();
        Console.WriteLine("=== Packaging SpeedFog Mod ===");

        var itemRandomizerEnabled = !string.IsNullOrEmpty(mergeDir);

        using var downloader = new ModEngineDownloader(CacheHelper.GetCacheDirectory());

        // 1. Ensure ME3 is downloaded
        var modEngineCachePath = await downloader.EnsureModEngineAsync(forceUpdate);

        // 2. Copy ME3 binaries to output (bin/ tree only)
        var me3OutputPath = Path.Combine(_outputDir, "me3");
        CopyModEngine(modEngineCachePath, me3OutputPath);
        Console.WriteLine($"Copied ME3 to {me3OutputPath}");

        // 3. Copy runtime assets (crash fix DLL, etc.)
        CopyAssets();
        Console.WriteLine("Copied runtime assets to lib/");

        // 4. Copy RandomizerHelper config if it exists in merge dir
        if (itemRandomizerEnabled && mergeDir != null)
        {
            var helperConfigSrc = Path.Combine(mergeDir, "RandomizerHelper_config.ini");
            if (File.Exists(helperConfigSrc))
            {
                var libDir = Path.Combine(_outputDir, "lib");
                var helperConfigDst = Path.Combine(libDir, "RandomizerHelper_config.ini");
                File.Copy(helperConfigSrc, helperConfigDst, overwrite: true);
                Console.WriteLine("Copied RandomizerHelper_config.ini to lib/");
            }
        }

        // 5. Generate config file (using path relative to output dir)
        ConfigGenerator.WriteModEngineConfig(_outputDir, "mods/fogmod", itemRandomizerEnabled);
        Console.WriteLine("Generated config_speedfog.me3");

        // 6. Copy launcher, backup, and recovery scripts
        ConfigGenerator.CopyScripts(_outputDir);
        Console.WriteLine("Copied launcher and backup scripts");

        Console.WriteLine();
        Console.WriteLine("=== SpeedFog mod ready! ===");
        Console.WriteLine($"To play: double-click {Path.Combine(_outputDir, "launch_speedfog.bat")}");
    }

    /// <summary>
    /// Copies runtime assets (DLLs, etc.) to the output lib/ folder.
    /// </summary>
    private void CopyAssets()
    {
        var libDir = Path.Combine(_outputDir, "lib");
        Directory.CreateDirectory(libDir);

        // Find assets directory relative to the executable
        var assetsDir = FindAssetsDirectory();
        if (assetsDir == null)
        {
            Console.WriteLine("Warning: assets directory not found, skipping asset copy");
            return;
        }

        foreach (var file in Directory.GetFiles(assetsDir, "*.dll"))
        {
            var destFile = Path.Combine(libDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }
    }

    /// <summary>
    /// Finds the assets directory by searching up from the executable location.
    /// Assets are at writer/assets/, executable may be at:
    /// - writer/FogModWrapper/bin/Debug/net8.0/win-x64/ (6 levels up to writer/)
    /// - writer/FogModWrapper/publish/win-x64/ (3 levels up to writer/)
    /// </summary>
    private static string? FindAssetsDirectory()
    {
        var candidates = new List<string>();

        // Try relative to executable
        var exeDir = AppContext.BaseDirectory;
        for (int i = 0; i <= 7; i++)
        {
            var path = exeDir;
            for (int j = 0; j < i; j++)
                path = Path.Combine(path, "..");
            candidates.Add(Path.Combine(path, "assets"));
        }

        // Also try relative to current working directory
        var cwd = Directory.GetCurrentDirectory();
        candidates.Add(Path.Combine(cwd, "assets"));
        candidates.Add(Path.Combine(cwd, "..", "assets"));
        candidates.Add(Path.Combine(cwd, "..", "..", "assets"));

        foreach (var candidate in candidates)
        {
            var normalized = Path.GetFullPath(candidate);
            if (Directory.Exists(normalized))
                return normalized;
        }

        return null;
    }

    /// <summary>
    /// Copies only the runtime-essential ME3 files (bin/ tree) to the output directory.
    /// The cache contains the full Linux tar.gz contents; we keep just the binaries.
    /// </summary>
    private static void CopyModEngine(string cacheDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        var binSrc = Path.Combine(cacheDir, "bin");
        var binDst = Path.Combine(destDir, "bin");

        if (!Directory.Exists(binSrc))
        {
            Console.WriteLine(
                "Warning: bin/ directory not found in ME3 cache, mod may not launch correctly");
            return;
        }

        CopyDirectory(binSrc, binDst);

        // Persist version marker for visibility
        var srcVersion = Path.Combine(cacheDir, "version.txt");
        if (File.Exists(srcVersion))
            File.Copy(srcVersion, Path.Combine(destDir, "version.txt"), overwrite: true);

        // Make Linux binary executable on Unix host. Tar preserves the +x bit on extraction,
        // but this restores it if the cache was populated on a host that dropped mode bits.
        if (!OperatingSystem.IsWindows())
        {
            var linuxBin = Path.Combine(binDst, "me3");
            if (File.Exists(linuxBin))
            {
                var execMode =
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
                    UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
                    UnixFileMode.OtherRead | UnixFileMode.OtherExecute;
                File.SetUnixFileMode(linuxBin, execMode);
            }
        }
    }

    /// <summary>
    /// Recursively copies a directory.
    /// </summary>
    private static void CopyDirectory(string sourceDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        foreach (var file in Directory.GetFiles(sourceDir))
        {
            var destFile = Path.Combine(destDir, Path.GetFileName(file));
            File.Copy(file, destFile, overwrite: true);
        }

        foreach (var dir in Directory.GetDirectories(sourceDir))
        {
            var destSubDir = Path.Combine(destDir, Path.GetFileName(dir));
            CopyDirectory(dir, destSubDir);
        }
    }
}
