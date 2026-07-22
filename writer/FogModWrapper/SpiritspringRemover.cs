using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes spiritspring jump regions (MountJump + MountJumpFall) that let the
/// player bypass a map-splits chokepoint. The jump behaviour lives in
/// dedicated MSB regions with EntityID 0: no flag or EMEVD can disable them
/// (only springs designed as sealable use the LockedMountJump region types),
/// so the regions must be deleted from the MSB. Targets come from the
/// `[[spiritspring_removals]]` entries of data/game_tweaks.toml.
/// Runs post-Write: edits the mod-output MSB if FogMod wrote one, otherwise
/// patches the vanilla MSB into the mod output (TorrentArenaPatcher pattern).
/// Each entry only applies when its RequiredZone is part of the seed's DAG.
/// </summary>
public static class SpiritspringRemover
{
    /// <summary>
    /// Match radius around the recorded region position. Springs are point
    /// regions; 5m tolerates capture imprecision without ever reaching the
    /// next spring (nearest other spring on m61_49_42_00 is ~110m away).
    /// </summary>
    private const float Radius = 5f;

    public static void Patch(
        string modDir, string gameDir,
        IReadOnlyDictionary<string, int> areaTiers,
        IReadOnlyList<SpiritspringRemoval> springs)
    {
        foreach (var target in springs)
        {
            if (!areaTiers.ContainsKey(target.RequiredZone))
                continue;  // zone not in this seed's DAG, leave the map alone
            PatchMap(modDir, gameDir, target);
        }
    }

    /// <summary>
    /// Remove MountJump/MountJumpFall regions within <see cref="Radius"/> of
    /// the target position. Returns the number of regions removed.
    /// </summary>
    public static int ApplyToMsb(MSBE msb, SpiritspringRemoval target)
    {
        var center = new System.Numerics.Vector3(target.X, target.Y, target.Z);
        bool Near(MSBE.Region r) => System.Numerics.Vector3.Distance(r.Position, center) <= Radius;
        int removed = msb.Regions.MountJumps.RemoveAll(r => Near(r));
        removed += msb.Regions.MountJumpFalls.RemoveAll(r => Near(r));
        return removed;
    }

    private static void PatchMap(string modDir, string gameDir, SpiritspringRemoval target)
    {
        var msbFile = $"{target.Map}.msb.dcx";
        var modPath = MsbHelper.FindMsbPath(modDir, msbFile);
        var sourcePath = modPath ?? MsbHelper.FindMsbPath(gameDir, msbFile);
        if (sourcePath == null)
        {
            Console.Error.WriteLine(
                $"  Warning: {msbFile} not found in mod or game dir, spiritspring not removed");
            return;
        }

        var msb = MSBE.Read(sourcePath);
        int removed = ApplyToMsb(msb, target);
        if (removed == 0)
        {
            Console.Error.WriteLine(
                $"  Warning: no spiritspring region found near "
                + $"({target.X:F1}, {target.Y:F1}, {target.Z:F1}) in {target.Map}");
            return;
        }

        var outPath = modPath ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFile);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        msb.Write(outPath);
        Console.WriteLine($"SpiritspringRemover: {target.Map}: removed {removed} jump region(s)");
    }
}
