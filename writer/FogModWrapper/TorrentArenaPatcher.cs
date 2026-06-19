using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Re-enables Torrent inside boss arenas that vanilla blocks via
/// <c>MSBE.Part.Collision.DisableTorrent</c>. The pattern mirrors the
/// snowfast option in the FogRando randomizer
/// (RandomizerCommon/MiscSetup.cs:1467), which flips the same flag on
/// named collisions in the Haligtree to let the player ride Torrent
/// into the area.
///
/// The collision names per map were found by dumping
/// <c>Parts.Collisions[*].DisableTorrent</c> with
/// <c>tools/game_inspect list-collisions --torrent-only</c>.
/// </summary>
public static class TorrentArenaPatcher
{
    private static readonly Dictionary<string, HashSet<string>> TargetCollisions = new()
    {
        // deeproot_boss (Fia's Champions)
        ["m12_03_00_00"] = new() { "h006000" },

        // ainsel_boss (Astel, Naturalborn of the Void)
        ["m12_04_00_00"] = new() { "h020300", "h020400", "h020500" },

        // siofra_boss (Ancestor Spirit)
        ["m12_08_00_00"] = new() { "h020300", "h020500", "h901000", "h905000" },

        // siofra_nokron_boss (Regal Ancestor Spirit)
        ["m12_09_00_00"] = new() { "h020300", "h020500", "h901000", "h905000" },
    };

    /// <summary>
    /// Map IDs whose arena collisions we re-enable Torrent on. Exposed for
    /// tests so the data contract stays locked.
    /// </summary>
    public static IReadOnlyDictionary<string, HashSet<string>> Targets => TargetCollisions;

    /// <summary>
    /// Patch every targeted arena MSB. If FogMod already wrote the MSB into
    /// <paramref name="modDir"/> we edit it in place; otherwise we read the
    /// vanilla MSB from <paramref name="gameDir"/> and write the patched
    /// version into the mod output.
    /// </summary>
    public static void Patch(string modDir, string gameDir)
    {
        int totalFlipped = 0;
        int patchedMaps = 0;
        foreach (var (mapId, names) in TargetCollisions)
        {
            int flipped = PatchMap(modDir, gameDir, mapId, names);
            if (flipped > 0)
            {
                totalFlipped += flipped;
                patchedMaps++;
            }
        }
        Console.WriteLine(
            $"TorrentArenaPatcher: enabled Torrent on {totalFlipped} collision(s) " +
            $"across {patchedMaps} arena MSB(s)");
        if (patchedMaps == 0)
        {
            Console.Error.WriteLine(
                "  Warning: no arena MSB was patched. Check that gameDir is " +
                "correct and that vanilla MSBs are present.");
        }
    }

    /// <summary>
    /// Flip <c>DisableTorrent=false</c> on every collision whose name is in
    /// <paramref name="targetNames"/>. Returns the number of collisions
    /// actually flipped (already-enabled ones are skipped).
    /// </summary>
    public static int ApplyToMsb(MSBE msb, ISet<string> targetNames)
    {
        int count = 0;
        foreach (var col in msb.Parts.Collisions)
        {
            if (!targetNames.Contains(col.Name))
                continue;
            if (!col.DisableTorrent)
                continue;
            col.DisableTorrent = false;
            count++;
        }
        return count;
    }

    private static int PatchMap(string modDir, string gameDir, string mapId, ISet<string> targetNames)
    {
        var msbFile = $"{mapId}.msb.dcx";
        var modPath = MsbHelper.FindMsbPath(modDir, msbFile);

        string sourcePath;
        if (modPath != null)
        {
            sourcePath = modPath;
        }
        else
        {
            var vanillaPath = MsbHelper.FindMsbPath(gameDir, msbFile);
            if (vanillaPath == null)
            {
                Console.Error.WriteLine(
                    $"  Warning: {msbFile} not found in mod or game dir, skipping torrent patch");
                return 0;
            }
            sourcePath = vanillaPath;
        }

        var msb = MSBE.Read(sourcePath);
        int flipped = ApplyToMsb(msb, targetNames);
        if (flipped == 0)
            return 0;

        var outPath = modPath ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFile);
        Directory.CreateDirectory(Path.GetDirectoryName(outPath)!);
        msb.Write(outPath);
        Console.WriteLine($"  {mapId}: flipped DisableTorrent on {flipped} collision(s)");
        return flipped;
    }
}
