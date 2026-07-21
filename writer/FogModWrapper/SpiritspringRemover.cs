using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Removes spiritspring jump regions (MountJump + MountJumpFall) that let the
/// player bypass a map-splits chokepoint. The jump behaviour lives in
/// dedicated MSB regions with EntityID 0: no flag or EMEVD can disable them
/// (only springs designed as sealable use the LockedMountJump region types),
/// so the regions must be deleted from the MSB. Hardcoded list, same policy
/// as EnirilimAssetRemover; positions located with `game_inspect near`.
/// Runs post-Write: edits the mod-output MSB if FogMod wrote one, otherwise
/// patches the vanilla MSB into the mod output (TorrentArenaPatcher pattern).
/// Each entry only applies when its RequiredZone is part of the seed's DAG.
/// </summary>
public static class SpiritspringRemover
{
    public sealed record SpringTarget(string Map, float X, float Y, float Z, string RequiredZone);

    // Fort of Reprimand back-ravine spring (m61_49_42_00): jumps the player
    // from the ravine below the fort up behind Edredd's chapel, bypassing
    // both synthetic gates (docs/map-splits.md).
    private static readonly List<SpringTarget> TargetSprings = new()
    {
        new("m61_49_42_00", 76.7f, 303.2f, 127.2f, RequiredZone: "reprimand"),
    };

    /// <summary>
    /// Match radius around the recorded region position. Springs are point
    /// regions; 5m tolerates capture imprecision without ever reaching the
    /// next spring (nearest other spring on m61_49_42_00 is ~110m away).
    /// </summary>
    private const float Radius = 5f;

    public static void Patch(string modDir, string gameDir, IReadOnlyDictionary<string, int> areaTiers)
    {
        foreach (var target in TargetSprings)
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
    public static int ApplyToMsb(MSBE msb, SpringTarget target)
    {
        var center = new System.Numerics.Vector3(target.X, target.Y, target.Z);
        bool Near(MSBE.Region r) => System.Numerics.Vector3.Distance(r.Position, center) <= Radius;
        int removed = msb.Regions.MountJumps.RemoveAll(r => Near(r));
        removed += msb.Regions.MountJumpFalls.RemoveAll(r => Near(r));
        return removed;
    }

    private static void PatchMap(string modDir, string gameDir, SpringTarget target)
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
