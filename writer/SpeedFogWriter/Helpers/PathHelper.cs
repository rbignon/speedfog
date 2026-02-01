// writer/SpeedFogWriter/Helpers/PathHelper.cs
namespace SpeedFogWriter.Helpers;

public static class PathHelper
{
    public static string GetDataDir(string? explicitPath = null)
    {
        if (explicitPath != null && Directory.Exists(explicitPath))
            return explicitPath;

        // Default: ../data relative to executable
        var baseDir = AppContext.BaseDirectory;
        var dataDir = Path.Combine(baseDir, "..", "data");

        if (Directory.Exists(dataDir))
            return Path.GetFullPath(dataDir);

        // Fallback: data/ in current directory
        dataDir = Path.Combine(Directory.GetCurrentDirectory(), "data");
        if (Directory.Exists(dataDir))
            return Path.GetFullPath(dataDir);

        throw new DirectoryNotFoundException("Could not find data directory");
    }

    public static void EnsureDirectory(string path)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
    }

    public static byte[] ParseMapId(string mapId)
    {
        // "m10_01_00_00" -> [10, 1, 0, 0]
        var parts = mapId.TrimStart('m').Split('_');
        if (parts.Length != 4)
            throw new FormatException($"Invalid map ID: {mapId}");

        return new byte[]
        {
            byte.Parse(parts[0]),
            byte.Parse(parts[1]),
            byte.Parse(parts[2]),
            byte.Parse(parts[3])
        };
    }

    public static string FormatMapId(byte m, byte area, byte block, byte sub)
    {
        return $"m{m:D2}_{area:D2}_{block:D2}_{sub:D2}";
    }
}
