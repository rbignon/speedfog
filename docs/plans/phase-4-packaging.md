# Phase 4: Packaging & Launcher

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 3: C# Writer](./phase-3-csharp-writer.md)
**Status**: Implemented
**Last updated**: 2026-02-01

## Objective

Create a self-contained output folder that allows users to launch Elden Ring with the SpeedFog mod in a single double-click, without any manual ModEngine configuration.

## Design Goals

1. **Zero configuration** - User runs the writer, gets a ready-to-play folder
2. **No bundled binaries** - Download ModEngine 2 from official GitHub releases
3. **Offline support** - Cache downloaded files for subsequent runs
4. **Cross-platform cache** - Use appropriate cache location per OS

## Output Structure

After running `SpeedFogWriter`, the output folder contains everything needed:

```
output/
├── ModEngine/
│   ├── modengine2_launcher.exe      # Downloaded from GitHub
│   └── modengine2/
│       ├── modengine2.dll
│       └── ...
│
├── mod/
│   ├── regulation.bin               # Modified game params
│   ├── event/
│   │   └── *.emevd.dcx              # Modified events
│   └── msg/
│       └── *.msgbnd.dcx             # Modified messages (optional)
│
├── config_speedfog.toml             # ModEngine configuration
├── launch_speedfog.bat              # Windows launcher (double-click to play)
├── launch_speedfog.sh               # Linux launcher (for Proton users)
└── spoiler.txt                      # Human-readable path description
```

## User Workflow

```bash
# 1. Generate the graph and spoiler (Python)
speedfog config.toml --spoiler -o /tmp/speedfog
# Creates /tmp/speedfog/<seed>/graph.json and spoiler.txt

# 2. Generate the complete mod (C#)
dotnet run --project writer/SpeedFogWriter -- /tmp/speedfog/<seed> "/path/to/ELDEN RING/Game" ./output

# 3. Play!
# Windows: Double-click output/launch_speedfog.bat
# Linux:   ./output/launch_speedfog.sh
```

---

## Task 4.1: ModEngine 2 Downloader

### 4.1.1: Cache Location

Store downloaded ModEngine 2 in a user-specific cache directory:

| OS | Cache Path |
|----|------------|
| Windows | `%LOCALAPPDATA%\SpeedFog\modengine2\` |
| Linux | `~/.cache/speedfog/modengine2/` |
| macOS | `~/Library/Caches/SpeedFog/modengine2/` |

```csharp
public static class CacheHelper
{
    public static string GetCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpeedFog", "modengine2");

        if (OperatingSystem.IsLinux())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".cache", "speedfog", "modengine2");

        if (OperatingSystem.IsMacOS())
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "SpeedFog", "modengine2");

        throw new PlatformNotSupportedException();
    }
}
```

### 4.1.2: GitHub Release Fetcher

Download the latest ModEngine 2 release from GitHub:

```csharp
public class ModEngineDownloader
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/soulsmods/ModEngine-2/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public ModEngineDownloader(string cacheDir)
    {
        _cacheDir = cacheDir;
        _httpClient = new HttpClient();
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpeedFog/1.0");
    }

    /// <summary>
    /// Ensures ModEngine 2 is available in the cache.
    /// Downloads if not present or if forceUpdate is true.
    /// </summary>
    public async Task<string> EnsureModEngineAsync(bool forceUpdate = false)
    {
        var launcherPath = Path.Combine(_cacheDir, "modengine2_launcher.exe");

        if (!forceUpdate && File.Exists(launcherPath))
        {
            Console.WriteLine("ModEngine 2 found in cache.");
            return _cacheDir;
        }

        Console.WriteLine("Downloading ModEngine 2 from GitHub...");

        // Fetch release info
        var releaseJson = await _httpClient.GetStringAsync(GitHubApiUrl);
        var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson);

        // Find the Windows x64 zip asset
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Contains("win64") && a.Name.EndsWith(".zip"));

        if (asset == null)
            throw new Exception("Could not find ModEngine 2 Windows release asset");

        Console.WriteLine($"Downloading {asset.Name} ({asset.Size / 1024 / 1024} MB)...");

        // Download zip
        var zipPath = Path.Combine(Path.GetTempPath(), asset.Name);
        await DownloadFileAsync(asset.BrowserDownloadUrl, zipPath);

        // Extract to cache
        Directory.CreateDirectory(_cacheDir);
        ZipFile.ExtractToDirectory(zipPath, _cacheDir, overwriteFiles: true);

        // Clean up
        File.Delete(zipPath);

        Console.WriteLine($"ModEngine 2 installed to {_cacheDir}");
        return _cacheDir;
    }

    private async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        var totalBytes = response.Content.Headers.ContentLength ?? -1;
        var buffer = new byte[8192];
        var totalRead = 0L;

        using var contentStream = await response.Content.ReadAsStreamAsync();
        using var fileStream = new FileStream(destinationPath, FileMode.Create);

        while (true)
        {
            var read = await contentStream.ReadAsync(buffer);
            if (read == 0) break;

            await fileStream.WriteAsync(buffer.AsMemory(0, read));
            totalRead += read;

            if (totalBytes > 0)
            {
                var progress = (int)(totalRead * 100 / totalBytes);
                Console.Write($"\rProgress: {progress}%  ");
            }
        }
        Console.WriteLine();
    }
}

// Models for GitHub API response
public class GitHubRelease
{
    [JsonPropertyName("tag_name")]
    public string TagName { get; set; }

    [JsonPropertyName("assets")]
    public List<GitHubAsset> Assets { get; set; }
}

public class GitHubAsset
{
    [JsonPropertyName("name")]
    public string Name { get; set; }

    [JsonPropertyName("size")]
    public long Size { get; set; }

    [JsonPropertyName("browser_download_url")]
    public string BrowserDownloadUrl { get; set; }
}
```

### 4.1.3: Version Tracking

Store the downloaded version for update checks:

```csharp
// Cache structure:
// ~/.cache/speedfog/modengine2/
// ├── version.txt              # Contains "2.1.0" or similar
// ├── modengine2_launcher.exe
// └── modengine2/
//     └── ...

public async Task<bool> IsUpdateAvailable()
{
    var versionFile = Path.Combine(_cacheDir, "version.txt");
    if (!File.Exists(versionFile)) return true;

    var cachedVersion = await File.ReadAllTextAsync(versionFile);

    var releaseJson = await _httpClient.GetStringAsync(GitHubApiUrl);
    var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson);

    return release.TagName != cachedVersion.Trim();
}
```

---

## Task 4.2: Config File Generator

### 4.2.1: ModEngine TOML Configuration

Generate `config_speedfog.toml`:

```csharp
public class ConfigGenerator
{
    public static void WriteModEngineConfig(string outputDir)
    {
        var configPath = Path.Combine(outputDir, "config_speedfog.toml");

        var config = @"# SpeedFog ModEngine 2 Configuration
# Auto-generated - do not edit manually

[modengine]
debug = false
external_dlls = []

[extension.mod_loader]
enabled = true
loose_params = false
mods = [
    { enabled = true, name = ""speedfog"", path = ""mod"" }
]
";

        File.WriteAllText(configPath, config);
    }
}
```

### 4.2.2: Launcher Scripts

Generate platform-specific launcher scripts:

**Windows (`launch_speedfog.bat`):**

```csharp
public static void WriteBatchLauncher(string outputDir)
{
    var batPath = Path.Combine(outputDir, "launch_speedfog.bat");

    var script = @"@echo off
REM SpeedFog Launcher for Elden Ring
REM Auto-generated - do not edit manually

setlocal

REM Get the directory where this script is located
set SCRIPT_DIR=%~dp0

REM Launch ModEngine with our config
""%SCRIPT_DIR%ModEngine\modengine2_launcher.exe"" -t er -c ""%SCRIPT_DIR%config_speedfog.toml""

endlocal
";

    File.WriteAllText(batPath, script);
}
```

**Linux (`launch_speedfog.sh`):**

```csharp
public static void WriteShellLauncher(string outputDir)
{
    var shPath = Path.Combine(outputDir, "launch_speedfog.sh");

    var script = @"#!/bin/bash
# SpeedFog Launcher for Elden Ring (Linux/Proton)
# Auto-generated - do not edit manually

SCRIPT_DIR=""$(cd ""$(dirname ""${BASH_SOURCE[0]}"")"" && pwd)""

# Note: For Proton/Wine users, you may need to configure
# Steam to use this script as a custom launch command
# or run through protontricks

wine ""$SCRIPT_DIR/ModEngine/modengine2_launcher.exe"" -t er -c ""$SCRIPT_DIR/config_speedfog.toml""
";

    File.WriteAllText(shPath, script);

    // Make executable on Unix systems
    if (!OperatingSystem.IsWindows())
    {
        File.SetUnixFileMode(shPath,
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute |
            UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
    }
}
```

---

## Task 4.3: Output Assembly

### 4.3.1: PackagingWriter

Orchestrates the final output assembly:

```csharp
public class PackagingWriter
{
    private readonly ModEngineDownloader _downloader;
    private readonly string _outputDir;

    public PackagingWriter(string outputDir)
    {
        _outputDir = outputDir;
        _downloader = new ModEngineDownloader(CacheHelper.GetCacheDirectory());
    }

    public async Task WritePackageAsync()
    {
        Console.WriteLine("=== Packaging SpeedFog Mod ===");

        // 1. Ensure ModEngine 2 is downloaded
        var modEngineCachePath = await _downloader.EnsureModEngineAsync();

        // 2. Copy ModEngine to output
        var modEngineOutputPath = Path.Combine(_outputDir, "ModEngine");
        CopyDirectory(modEngineCachePath, modEngineOutputPath);
        Console.WriteLine($"Copied ModEngine 2 to {modEngineOutputPath}");

        // 3. Generate config file
        ConfigGenerator.WriteModEngineConfig(_outputDir);
        Console.WriteLine("Generated config_speedfog.toml");

        // 4. Generate launcher scripts
        ConfigGenerator.WriteBatchLauncher(_outputDir);
        ConfigGenerator.WriteShellLauncher(_outputDir);
        Console.WriteLine("Generated launcher scripts");

        Console.WriteLine();
        Console.WriteLine("=== SpeedFog mod ready! ===");
        Console.WriteLine($"To play: double-click {Path.Combine(_outputDir, "launch_speedfog.bat")}");
    }

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
```

### 4.3.2: Integration with ModWriter

Update the main `ModWriter` to include packaging at the end:

```csharp
// In ModWriter.cs, at the end of WriteAll():
public async Task WriteAllAsync(SpeedFogGraph graph)
{
    // ... existing Phase 3 code (params, events, fog gates, etc.) ...

    // Phase 4: Packaging
    var packager = new PackagingWriter(_outputDir);
    await packager.WritePackageAsync();
}
```

---

## Task 4.4: Spoiler Log

The spoiler log is generated by the Python core (`speedfog --spoiler`) and includes:
- ASCII graph visualization
- Path summary with weights
- Node details with zones, tiers, layers, and fog gate IDs

The C# writer simply copies `spoiler.txt` from the seed directory to the output:

```csharp
// In Program.cs, after mod generation:
var spoilerSource = Path.Combine(options.SeedDir, "spoiler.txt");
if (File.Exists(spoilerSource))
{
    var spoilerDest = Path.Combine(options.OutputDir, "spoiler.txt");
    File.Copy(spoilerSource, spoilerDest, overwrite: true);
    Console.WriteLine("Copied spoiler.txt from seed directory");
}
```

This centralizes spoiler generation in Python where all graph information is available.

---

## Task 4.5: CLI Arguments

### 4.5.1: Extended CLI Options

Update `Program.cs` to support packaging options:

```csharp
// Usage:
// dotnet run -- <seed_dir> <game_path> <output_dir> [options]
//
// Arguments:
//   seed_dir    - Path to seed directory (contains graph.json, spoiler.txt)
//   game_path   - Path to Elden Ring Game folder
//   output_dir  - Output directory for mod files
//
// Options:
//   --data-dir <path>   Path to data directory (default: ../data)
//   --no-package        Skip ModEngine packaging (output mod files only)
//   --update-modengine  Force re-download of ModEngine 2

public class Options
{
    public string SeedDir { get; set; }
    public string GamePath { get; set; }
    public string OutputDir { get; set; }
    public string DataDir { get; set; }

    public bool Package { get; set; } = true;
    public bool UpdateModEngine { get; set; } = false;
}
```

---

## Deliverables Summary

```
speedfog/writer/SpeedFogWriter/
├── Packaging/
│   ├── CacheHelper.cs           # Cache directory resolution
│   ├── ModEngineDownloader.cs   # GitHub release downloader
│   ├── ConfigGenerator.cs       # TOML + launcher script generation
│   └── PackagingWriter.cs       # Orchestrator
│
└── Models/
    └── GitHubModels.cs          # GitHub API response models
```

Note: Spoiler log generation is handled by Python (`core/speedfog_core/output.py`).

---

## Testing Checklist

- [ ] ModEngine 2 downloads correctly from GitHub
- [ ] Cached version is reused on subsequent runs
- [ ] `config_speedfog.toml` has correct paths
- [ ] `launch_speedfog.bat` launches the game correctly
- [ ] Output folder is fully self-contained (can be moved/copied)
- [ ] Works with Steam running
- [ ] Spoiler log accurately describes the generated path

---

## Future Enhancements (v2)

- **GUI wrapper** - Simple WPF/Avalonia UI for non-CLI users
- **Mod merging** - Allow additional mods in the config
- **Steam integration** - Add as non-Steam game automatically
- **Auto-update check** - Notify when new ModEngine version available
