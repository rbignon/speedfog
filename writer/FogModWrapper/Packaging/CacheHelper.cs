namespace FogModWrapper.Packaging;

/// <summary>
/// Helper for platform-specific cache directory resolution.
/// </summary>
public static class CacheHelper
{
    /// <summary>
    /// Gets the cache directory for ME3 storage.
    /// </summary>
    /// <returns>Platform-specific cache path.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown on unsupported platforms.</exception>
    public static string GetCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: %LOCALAPPDATA%\SpeedFog\me3\
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpeedFog", "me3");
        }

        if (OperatingSystem.IsLinux())
        {
            // Linux: $XDG_CACHE_HOME/speedfog/me3/ or ~/.cache/speedfog/me3/
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var basePath = !string.IsNullOrEmpty(xdgCache)
                ? xdgCache
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return Path.Combine(basePath, "speedfog", "me3");
        }

        if (OperatingSystem.IsMacOS())
        {
            // macOS: ~/Library/Caches/SpeedFog/me3/
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "SpeedFog", "me3");
        }

        throw new PlatformNotSupportedException(
            "SpeedFog only supports Windows, Linux, and macOS.");
    }
}
