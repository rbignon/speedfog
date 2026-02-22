using FogMod;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Fixes Maliketh's post-defeat warp when connecting to Ashen Leyndell.
///
/// When SpeedFog connects farumazula_maliketh → leyndell_erdtree, FogMod replaces
/// the warp destination with the entrance's primary Side (m11_00_00_00). But the
/// Maliketh warp also sets flag 300 (Erdtree burning) via WarpBonfireFlag, which
/// causes the game to load m11_05_00_00 (Ashen Capital). The warp region doesn't
/// exist in the Ashen map, so the warp fails silently.
///
/// FogMod's AlternateSide mechanism computes warp points for BOTH map versions.
/// This injector patches warp instructions to use the alternate (Ashen) destination.
///
/// Scans all EMEVD files for both instruction families:
/// - WarpPlayer (2003:14): [area(1), block(1), sub(1), sub2(1), region(4)]
/// - PlayCutsceneToPlayerAndWarp (2002:11/12): [cutscene(4), playback(4), region(4), packedMap(4)]
/// </summary>
public static class AshenLeyndellWarpInjector
{
    /// <summary>
    /// FogMod allocates warp target region entity IDs starting from this base.
    /// Only patch FogMod-generated warps, not vanilla ones.
    /// </summary>
    private const int FOGMOD_ENTITY_BASE = 755890000;

    public static void Inject(
        string modDir,
        Graph.WarpPoint primaryWarp,
        Graph.WarpPoint altWarp)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping Ashen Leyndell warp fix");
            return;
        }

        int primaryRegion = primaryWarp.Region;
        int altRegion = altWarp.Region;

        // Primary map bytes for WarpPlayer matching (4 individual bytes)
        byte[] primaryMapBytes = ParseMapBytes(primaryWarp.Map);
        byte[] altMapBytes = ParseMapBytes(altWarp.Map);

        // Packed map ints for PlayCutsceneToPlayerAndWarp matching
        int primaryMapPacked = PackMapId(primaryWarp.Map);
        int altMapPacked = PackMapId(altWarp.Map);

        int totalPatched = 0;

        foreach (var emevdPath in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(emevdPath);
            int patched = 0;

            foreach (var evt in emevd.Events)
            {
                foreach (var instr in evt.Instructions)
                {
                    if (TryPatchWarpPlayer(instr, primaryRegion, primaryMapBytes, altRegion, altMapBytes))
                    {
                        patched++;
                    }
                    else if (TryPatchCutsceneWarp(instr, primaryRegion, primaryMapPacked, altRegion, altMapPacked))
                    {
                        patched++;
                    }
                }
            }

            if (patched > 0)
            {
                emevd.Write(emevdPath);
                var fileName = Path.GetFileName(emevdPath);
                Console.WriteLine($"  Patched {patched} warp(s) in {fileName}");
                totalPatched += patched;
            }
        }

        if (totalPatched > 0)
        {
            Console.WriteLine($"Ashen Leyndell fix: patched {totalPatched} warp(s) from {primaryWarp.Map} to {altWarp.Map}");
        }
        else
        {
            Console.WriteLine($"Warning: No warp instructions matched for Ashen Leyndell fix " +
                $"(primary region {primaryRegion}, alt region {altRegion})");
        }
    }

    /// <summary>
    /// Try to patch a WarpPlayer instruction (bank 2003, id 14).
    /// ArgData layout: [area(1), block(1), sub(1), sub2(1), region(4), ...]
    /// Match on region (must be FogMod-generated) AND map bytes.
    /// </summary>
    private static bool TryPatchWarpPlayer(
        EMEVD.Instruction instr,
        int primaryRegion, byte[] primaryMapBytes,
        int altRegion, byte[] altMapBytes)
    {
        if (instr.Bank != 2003 || instr.ID != 14 || instr.ArgData.Length < 8)
            return false;

        var a = instr.ArgData;
        int region = BitConverter.ToInt32(a, 4);

        if (region < FOGMOD_ENTITY_BASE)
            return false;

        // Match map bytes at offset 0-3
        if (a[0] != primaryMapBytes[0] || a[1] != primaryMapBytes[1] ||
            a[2] != primaryMapBytes[2] || a[3] != primaryMapBytes[3])
            return false;

        // Match region
        if (region != primaryRegion)
            return false;

        // Patch map bytes
        altMapBytes.CopyTo(a, 0);
        // Patch region
        BitConverter.GetBytes(altRegion).CopyTo(a, 4);
        return true;
    }

    /// <summary>
    /// Try to patch a PlayCutsceneToPlayerAndWarp instruction (bank 2002, id 11/12).
    /// ArgData layout: [cutsceneId(4), playback(4), region(4), mapId(4), ...]
    /// mapId is packed decimal: area*1000000 + block*10000 + sub*100 + sub2.
    /// </summary>
    private static bool TryPatchCutsceneWarp(
        EMEVD.Instruction instr,
        int primaryRegion, int primaryMapPacked,
        int altRegion, int altMapPacked)
    {
        if (instr.Bank != 2002 || (instr.ID != 11 && instr.ID != 12))
            return false;
        if (instr.ArgData.Length < 16)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 8);
        int mapInt = BitConverter.ToInt32(instr.ArgData, 12);

        if (region != primaryRegion || mapInt != primaryMapPacked)
            return false;

        BitConverter.GetBytes(altRegion).CopyTo(instr.ArgData, 8);
        BitConverter.GetBytes(altMapPacked).CopyTo(instr.ArgData, 12);
        return true;
    }

    /// <summary>
    /// Parse map name to 4 bytes: "m11_00_00_00" → [11, 0, 0, 0]
    /// </summary>
    private static byte[] ParseMapBytes(string map)
    {
        var parts = map.TrimStart('m').Split('_');
        return new byte[]
        {
            byte.Parse(parts[0]),
            byte.Parse(parts[1]),
            byte.Parse(parts[2]),
            byte.Parse(parts[3]),
        };
    }

    /// <summary>
    /// Pack a map name string into a decimal int.
    /// "m11_05_00_00" → 11050000
    /// Format: area*1000000 + block*10000 + sub*100 + sub2
    /// </summary>
    private static int PackMapId(string map)
    {
        var parts = map.TrimStart('m').Split('_');
        return int.Parse(parts[0]) * 1000000
             + int.Parse(parts[1]) * 10000
             + int.Parse(parts[2]) * 100
             + int.Parse(parts[3]);
    }
}
