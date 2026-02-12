namespace FogModWrapper.Packaging;

/// <summary>
/// Orchestrates the final output packaging: ModEngine download, config generation, and launchers.
/// </summary>
public class PackagingWriter
{
    private readonly string _outputDir;

    public PackagingWriter(string outputDir)
    {
        _outputDir = outputDir;
    }

    /// <summary>
    /// Writes the complete package: downloads ModEngine 2, copies it to output,
    /// and generates config and launcher scripts.
    /// </summary>
    /// <param name="mergeDir">Optional merge directory (Item Randomizer output) - if set, includes RandomizerHelper.dll.</param>
    /// <param name="forceUpdate">Force re-download of ModEngine 2.</param>
    public async Task WritePackageAsync(string? mergeDir = null, bool forceUpdate = false)
    {
        Console.WriteLine();
        Console.WriteLine("=== Packaging SpeedFog Mod ===");

        var itemRandomizerEnabled = !string.IsNullOrEmpty(mergeDir);

        using var downloader = new ModEngineDownloader(CacheHelper.GetCacheDirectory());

        // 1. Ensure ModEngine 2 is downloaded
        var modEngineCachePath = await downloader.EnsureModEngineAsync(forceUpdate);

        // 2. Copy ModEngine to output (runtime files only, skip dev assets)
        var modEngineOutputPath = Path.Combine(_outputDir, "ModEngine");
        CopyModEngine(modEngineCachePath, modEngineOutputPath);
        Console.WriteLine($"Copied ModEngine 2 to {modEngineOutputPath}");

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
        Console.WriteLine("Generated config_speedfog.toml");

        // 6. Generate launcher scripts
        ConfigGenerator.WriteBatchLauncher(_outputDir);
        ConfigGenerator.WriteShellLauncher(_outputDir);
        Console.WriteLine("Generated launcher scripts");

        Console.WriteLine();
        Console.WriteLine("=== SpeedFog mod ready! ===");
        Console.WriteLine($"To play:");
        Console.WriteLine($"  Windows: double-click {Path.Combine(_outputDir, "launch_speedfog.bat")}");
        Console.WriteLine($"  Linux:   run {Path.Combine(_outputDir, "launch_speedfog.sh")}");
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
    /// Copies only the runtime-essential ModEngine 2 files to the output directory.
    /// Skips development files (C++ headers, debug menus, locale data) and configs
    /// for other games. This reduces the output by ~17 MB compared to a full copy.
    /// </summary>
    private static void CopyModEngine(string cacheDir, string destDir)
    {
        Directory.CreateDirectory(destDir);

        // Top-level files: only the launcher and our version marker
        string[] allowedFiles = ["modengine2_launcher.exe", "version.txt"];
        foreach (var name in allowedFiles)
        {
            var src = Path.Combine(cacheDir, name);
            if (File.Exists(src))
                File.Copy(src, Path.Combine(destDir, name), overwrite: true);
        }

        // modengine2/ subdirectories: only runtime essentials
        // Skips: assets/ (debug menu), include/ (C++ headers),
        //        lib/ (.lib linker files), share/ (cmake configs)
        string[] allowedSubDirs = ["bin", "crashpad", "tools"];
        var me2Src = Path.Combine(cacheDir, "modengine2");
        var me2Dst = Path.Combine(destDir, "modengine2");

        if (Directory.Exists(me2Src))
        {
            Directory.CreateDirectory(me2Dst);
            foreach (var name in allowedSubDirs)
            {
                var src = Path.Combine(me2Src, name);
                if (Directory.Exists(src))
                    CopyDirectory(src, Path.Combine(me2Dst, name));
            }
        }
        else
        {
            Console.WriteLine(
                "Warning: modengine2/ directory not found in cache, mod may not launch correctly");
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
