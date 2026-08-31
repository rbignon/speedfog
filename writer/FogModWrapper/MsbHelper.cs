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
    /// SibPath follows the enemy randomizer's registration convention
    /// (N:\GR\data\Model\chr\{model}\sib\{model}.sib). The existing
    /// ChapelGrace caller only registers the invisible c1000 and is
    /// unaffected by the addition.
    /// </summary>
    public static void EnsureEnemyModel(MSBE msb, string modelName)
    {
        if (msb.Models.Enemies.Any(m => m.Name == modelName))
            return;
        msb.Models.Enemies.Add(new MSBE.Model.Enemy
        {
            Name = modelName,
            SibPath = $"N:\\GR\\data\\Model\\chr\\{modelName}\\sib\\{modelName}.sib",
        });
    }

    /// <summary>
    /// Gives a cloned asset its own Unk1 so it stops aliasing the base
    /// part's group arrays: MSBE's UnkStruct1.DeepCopy only clones
    /// CollisionMask, sharing DisplayGroups/DrawGroups between base and
    /// clone. The fresh arrays stay all-zero, the profile every working
    /// map's bloodstains ship with (the visible part is a following SFX,
    /// not the asset model); a restrictive inherited DisplayGroups (e.g. an
    /// interior prop's display cell, hit at Fort of Reprimand's chapel)
    /// display-culls the marker and its SFX. Scalar display-condition
    /// fields and CollisionMask values are preserved from the clone.
    /// See docs/death-markers.md for the aliasing details.
    /// </summary>
    public static void DetachVisibilityGroups(MSBE.Part.Asset part)
    {
        var src = part.Unk1;
        var own = new MSBE.Part.UnkStruct1
        {
            Condition1 = src.Condition1,
            Condition2 = src.Condition2,
            UnkC2 = src.UnkC2,
            UnkC3 = src.UnkC3,
            UnkC4 = src.UnkC4,
            UnkC6 = src.UnkC6,
        };
        Array.Copy(src.CollisionMask, own.CollisionMask, own.CollisionMask.Length);
        part.Unk1 = own;
    }

    /// <summary>
    /// Same detachment as the Asset overload, for cloned Enemy parts.
    /// See docs/death-markers.md for the aliasing details.
    /// </summary>
    public static void DetachVisibilityGroups(MSBE.Part.Enemy part)
    {
        var src = part.Unk1;
        var own = new MSBE.Part.UnkStruct1
        {
            Condition1 = src.Condition1,
            Condition2 = src.Condition2,
            UnkC2 = src.UnkC2,
            UnkC3 = src.UnkC3,
            UnkC4 = src.UnkC4,
            UnkC6 = src.UnkC6,
        };
        Array.Copy(src.CollisionMask, own.CollisionMask, own.CollisionMask.Length);
        part.Unk1 = own;
    }
}
