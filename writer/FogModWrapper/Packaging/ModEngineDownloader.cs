using System.Formats.Tar;
using System.IO.Compression;
using System.Text.Json;
using FogModWrapper.Models;

namespace FogModWrapper.Packaging;

/// <summary>
/// Downloads ME3 from GitHub releases and manages caching.
/// Uses the Linux tar.gz asset, which contains both the Linux native binary
/// (bin/me3) and the Windows runtime binaries (bin/win64/me3.exe + DLLs).
/// </summary>
public class ModEngineDownloader : IDisposable
{
    private const string GitHubApiUrl =
        "https://api.github.com/repos/soulsmods/me3/releases/latest";

    private readonly HttpClient _httpClient;
    private readonly string _cacheDir;

    public ModEngineDownloader(string cacheDir)
    {
        _cacheDir = cacheDir;
        _httpClient = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(10)
        };
        _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("SpeedFog/1.0");
    }

    /// <summary>
    /// Ensures ME3 is available in the cache. Downloads if not present or if forceUpdate is true.
    /// </summary>
    /// <returns>Path to the cached ME3 directory (containing bin/me3 and bin/win64/).</returns>
    public async Task<string> EnsureModEngineAsync(bool forceUpdate = false)
    {
        var sentinel = Path.Combine(_cacheDir, "bin", "win64", "me3.exe");

        if (!forceUpdate && File.Exists(sentinel))
        {
            Console.WriteLine("ME3 found in cache.");
            return _cacheDir;
        }

        Console.WriteLine("Downloading ME3 from GitHub...");

        var releaseJson = await _httpClient.GetStringAsync(GitHubApiUrl);
        var release = JsonSerializer.Deserialize<GitHubRelease>(releaseJson)
            ?? throw new InvalidOperationException("Failed to parse GitHub release response");

        // The Linux tar.gz contains both Linux native (bin/me3) and Windows binaries (bin/win64/).
        var asset = release.Assets.FirstOrDefault(a =>
            a.Name == "me3-linux-amd64.tar.gz");

        if (asset == null)
            throw new InvalidOperationException(
                "Could not find me3-linux-amd64.tar.gz release asset");

        Console.WriteLine($"Downloading {asset.Name} ({asset.Size / 1024 / 1024} MB)...");

        var archivePath = Path.Combine(Path.GetTempPath(), asset.Name);
        await DownloadFileAsync(asset.BrowserDownloadUrl, archivePath);

        // Reset cache directory
        if (Directory.Exists(_cacheDir))
        {
            foreach (var file in Directory.GetFiles(_cacheDir))
                File.Delete(file);
            foreach (var dir in Directory.GetDirectories(_cacheDir))
                Directory.Delete(dir, recursive: true);
        }
        Directory.CreateDirectory(_cacheDir);

        // Extract tar.gz: gunzip into a temp tar, then untar to cache dir.
        await using (var fileStream = File.OpenRead(archivePath))
        await using (var gzStream = new GZipStream(fileStream, CompressionMode.Decompress))
        {
            await TarFile.ExtractToDirectoryAsync(gzStream, _cacheDir, overwriteFiles: true);
        }

        // Persist version
        var versionFile = Path.Combine(_cacheDir, "version.txt");
        await File.WriteAllTextAsync(versionFile, release.TagName);

        File.Delete(archivePath);

        Console.WriteLine($"ME3 {release.TagName} installed to {_cacheDir}");
        return _cacheDir;
    }

    /// <summary>
    /// Checks if a newer version is available on GitHub.
    /// </summary>
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
            if (read == 0)
                break;

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
