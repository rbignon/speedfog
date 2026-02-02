namespace FogModWrapper.Packaging;

/// <summary>
/// Helper for platform-specific cache directory resolution.
/// </summary>
public static class CacheHelper
{
    /// <summary>
    /// Gets the cache directory for ModEngine 2 storage.
    /// </summary>
    /// <returns>Platform-specific cache path.</returns>
    /// <exception cref="PlatformNotSupportedException">Thrown on unsupported platforms.</exception>
    public static string GetCacheDirectory()
    {
        if (OperatingSystem.IsWindows())
        {
            // Windows: %LOCALAPPDATA%\SpeedFog\modengine2\
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "SpeedFog", "modengine2");
        }

        if (OperatingSystem.IsLinux())
        {
            // Linux: $XDG_CACHE_HOME/speedfog/modengine2/ or ~/.cache/speedfog/modengine2/
            var xdgCache = Environment.GetEnvironmentVariable("XDG_CACHE_HOME");
            var basePath = !string.IsNullOrEmpty(xdgCache)
                ? xdgCache
                : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".cache");
            return Path.Combine(basePath, "speedfog", "modengine2");
        }

        if (OperatingSystem.IsMacOS())
        {
            // macOS: ~/Library/Caches/SpeedFog/modengine2/
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "SpeedFog", "modengine2");
        }

        throw new PlatformNotSupportedException(
            "SpeedFog only supports Windows, Linux, and macOS.");
    }
}
