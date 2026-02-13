using FogModWrapper.Models;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes vanilla warp entities from MSBs that FogMod couldn't properly remove.
///
/// FogMod tags unique warps (coffins, DLC transitions) with "remove" but its removal
/// logic uses o.Name == e.Name where e.Name is a region entity ID string (e.g., "2046402020"),
/// not an MSB Part.Asset name. The comparison is always false, so vanilla warps persist.
///
/// This post-processor removes the actual MSB Part.Asset entries by EntityID.
/// </summary>
public static class VanillaWarpRemover
{
    // MSB directory name variants (vanilla=PascalCase, FogMod under Wine=lowercase)
    private static readonly string[] MsbDirVariants = { "mapstudio", "MapStudio" };

    /// <summary>
    /// Remove vanilla warp assets from MSBs for the given entities.
    /// </summary>
    public static void Remove(string modDir, List<RemoveEntity> entities)
    {
        Console.WriteLine($"Removing {entities.Count} vanilla warp entities...");

        // Group by map to avoid reading/writing the same MSB multiple times
        var byMap = new Dictionary<string, List<RemoveEntity>>();
        foreach (var entity in entities)
        {
            if (!byMap.ContainsKey(entity.Map))
                byMap[entity.Map] = new List<RemoveEntity>();
            byMap[entity.Map].Add(entity);
        }

        var totalRemoved = 0;
        foreach (var (mapId, mapEntities) in byMap)
        {
            var removed = RemoveFromMap(modDir, mapId, mapEntities);
            totalRemoved += removed;
        }

        Console.WriteLine($"  Removed {totalRemoved} vanilla warp assets from MSBs");
    }

    private static int RemoveFromMap(string modDir, string mapId, List<RemoveEntity> entities)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = FindMsbPath(modDir, msbFileName);

        if (msbPath == null)
        {
            // Map not in mod output (not part of this seed's graph) — skip
            return 0;
        }

        var msb = MSBE.Read(msbPath);
        var entityIds = new HashSet<uint>(
            entities.Where(e => e.EntityId > 0).Select(e => (uint)e.EntityId));

        var removed = msb.Parts.Assets.RemoveAll(a => entityIds.Contains(a.EntityID));

        if (removed > 0)
        {
            msb.Write(msbPath);
            Console.WriteLine($"  {mapId}: removed {removed} warp assets");
        }

        return removed;
    }

    private static string? FindMsbPath(string baseDir, string msbFileName)
    {
        foreach (var dirName in MsbDirVariants)
        {
            var path = Path.Combine(baseDir, "map", dirName, msbFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }
}
