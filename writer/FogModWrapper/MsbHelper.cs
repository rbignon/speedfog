using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Shared utilities for MSB file operations across injectors.
/// </summary>
internal static class MsbHelper
{
    // MSB directory name variants (vanilla=PascalCase, FogMod under Wine=lowercase)
    private static readonly string[] MsbDirVariants = { "mapstudio", "MapStudio" };

    /// <summary>
    /// Find an MSB file under a base directory, trying both "MapStudio" (vanilla)
    /// and "mapstudio" (FogMod on Linux via Wine) directory names.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    public static string? FindMsbPath(string baseDir, string msbFileName)
    {
        foreach (var dirName in MsbDirVariants)
        {
            var path = Path.Combine(baseDir, "map", dirName, msbFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    /// <summary>
    /// Find the existing mapstudio directory in modDir, or create one
    /// matching the convention FogMod used (defaults to "mapstudio").
    /// </summary>
    public static string FindOrCreateMsbDir(string modDir, string msbFileName)
    {
        var mapDir = Path.Combine(modDir, "map");
        if (Directory.Exists(mapDir))
        {
            foreach (var dirName in MsbDirVariants)
            {
                var dir = Path.Combine(mapDir, dirName);
                if (Directory.Exists(dir))
                    return Path.Combine(dir, msbFileName);
            }
        }
        // Default to lowercase (FogMod convention under Wine)
        return Path.Combine(mapDir, "mapstudio", msbFileName);
    }

    /// <summary>
    /// Generate a unique MSB part name with incrementing suffix.
    /// Uses the 9900+ range to avoid conflicts with vanilla and FogMod parts.
    /// </summary>
    public static string GeneratePartName(IEnumerable<string> existingNames, string modelName)
    {
        var names = new HashSet<string>(existingNames);
        for (int i = 9900; i < 10000; i++)
        {
            var name = $"{modelName}_{i:D4}";
            if (!names.Contains(name))
                return name;
        }
        // Overflow: continue beyond 9999
        for (int i = 10000; ; i++)
        {
            var name = $"{modelName}_{i}";
            if (!names.Contains(name))
                return name;
        }
    }

    /// <summary>
    /// Set the Unk08 field from the numeric suffix of the part name.
    /// For a part named "AEG099_090_9901", sets Unk08 = 9901.
    /// </summary>
    public static void SetNameIdent(MSBE.Part part)
    {
        var segments = part.Name.Split('_');
        if (segments.Length > 0 && int.TryParse(segments[^1], out var ident))
            part.Unk08 = ident;
    }

    /// <summary>
    /// Ensure an asset model definition exists in the MSB models list.
    /// </summary>
    public static void EnsureAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = modelName });
    }

    /// <summary>
    /// Ensure an enemy model definition exists in the MSB models list.
    /// </summary>
    public static void EnsureEnemyModel(MSBE msb, string modelName)
    {
        if (msb.Models.Enemies.Any(m => m.Name == modelName))
            return;
        msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = modelName });
    }
}
