// writer/SpeedFogWriter/Packaging/ModEngineDownloader.cs
using System.IO.Compression;
using System.Text.Json;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Packaging;

/// <summary>
/// Downloads ModEngine 2 from GitHub releases and manages caching.
/// </summary>
public class ModEngineDownloader : IDisposable
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/soulsmods/ModEngine2/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public ModEngineDownloader(string cacheDir)
    {
        _cacheDir = cacheDir;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10) // Large download timeout
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpeedFog/1.0");
    }

    /// <summary>
    /// Ensures ModEngine 2 is available in the cache.
    /// Downloads if not present or if forceUpdate is true.
    /// </summary>
    /// <param name="forceUpdate">Force re-download even if cached.</param>
    /// <returns>Path to the cached ModEngine 2 directory.</returns>
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
        var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson)
            ?? throw new InvalidOperationException("Failed to parse GitHub release response");

        // Find the Windows x64 zip asset
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name.Contains("win64") && a.Name.EndsWith(".zip"));

        if (asset == null)
            throw new InvalidOperationException(
                "Could not find ModEngine 2 Windows release asset");

        Console.WriteLine($"Downloading {asset.Name} ({asset.Size / 1024 / 1024} MB)...");

        // Download zip
        var zipPath = Path.Combine(Path.GetTempPath(), asset.Name);
        await DownloadFileAsync(asset.BrowserDownloadUrl, zipPath);

        // Extract to cache
        Directory.CreateDirectory(_cacheDir);

        // Clear existing cache if updating
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(_cacheDir))
                Directory.Delete(dir, recursive: true);
        }

        ZipFile.ExtractToDirectory(zipPath, _cacheDir, overwriteFiles: true);

        // ModEngine zip extracts to a subdirectory; flatten if needed
        var subdirs = Directory.GetDirectories(_cacheDir);
        if (subdirs.Length == 1 && !File.Exists(launcherPath))
        {
            var extractedDir = subdirs[0];
            foreach (var file in Directory.GetFiles(extractedDir))
            {
                var destFile = Path.Combine(_cacheDir, Path.GetFileName(file));
                File.Move(file, destFile, overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(extractedDir))
            {
                var destDir = Path.Combine(_cacheDir, Path.GetFileName(dir));
                if (Directory.Exists(destDir))
                    Directory.Delete(destDir, recursive: true);
                Directory.Move(dir, destDir);
            }
            Directory.Delete(extractedDir);
        }

        // Store version for update checks
        var versionFile = Path.Combine(_cacheDir, "version.txt");
        await File.WriteAllTextAsync(versionFile, release.TagName);

        // Clean up
        File.Delete(zipPath);

        Console.WriteLine($"ModEngine 2 {release.TagName} installed to {_cacheDir}");
        return _cacheDir;
    }

    /// <summary>
    /// Checks if a newer version is available on GitHub.
    /// </summary>
    /// <returns>True if an update is available.</returns>
    public async Task<bool> IsUpdateAvailable()
    {
        var versionFile = Path.Combine(_cacheDir, "version.txt");
        if (!File.Exists(versionFile))
            return true;

        var cachedVersion = await File.ReadAllTextAsync(versionFile);

        var releaseJson = await _httpClient.GetStringAsync(GitHubApiUrl);
        var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson);

        return release?.TagName != cachedVersion.Trim();
    }

    /// <summary>
    /// Downloads a file with progress reporting.
    /// </summary>
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

    public void Dispose()
    {
        _httpClient.Dispose();
    }
}
