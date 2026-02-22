using FogMod;
using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Fixes Maliketh's post-defeat warp when connecting to Ashen Leyndell.
///
/// When SpeedFog connects farumazula_maliketh → leyndell_erdtree, FogMod's
/// EventEditor replaces the warp region in existing events to point to the
/// entrance's primary SpawnPoint in m11_00_00_00. However, the vanilla Event 900
/// (Maliketh defeat cutscene) also sets flag 300 (Erdtree burning) via
/// WarpBonfireFlag, causing the game to load m11_05_00_00 (Ashen Capital).
/// The primary region doesn't exist in the Ashen map → warp fails silently.
///
/// FogMod's AlternateSide mechanism computes SpawnPoints for BOTH map versions.
/// This injector replaces the primary region with the alternate region (and fixes
/// the map bytes where applicable) in all warp instructions across all EMEVDs.
///
/// Key insight: EventEditor only replaces the region in PlayCutsceneToPlayerAndWarp
/// (no MapArg in WarpArgs), so the map bytes may already be m11_05 (vanilla) while
/// the region is the FogMod primary (m11_00). We match solely on region, not map.
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
        byte[] altMapBytes = ParseMapBytes(altWarp.Map);
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
                    if (TryPatchWarpPlayer(instr, primaryRegion, altRegion, altMapBytes))
                        patched++;
                    else if (TryPatchCutsceneWarp(instr, primaryRegion, altRegion, altMapPacked))
                        patched++;
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
            Console.WriteLine($"Ashen Leyndell fix: patched {totalPatched} warp(s) " +
                $"(region {primaryRegion} -> {altRegion}, map -> {altWarp.Map})");
        }
        else
        {
            Console.WriteLine($"Warning: No warp instructions matched for Ashen Leyndell fix " +
                $"(expected region {primaryRegion})");
        }
    }

    /// <summary>
    /// Try to patch a WarpPlayer instruction (bank 2003, id 14).
    /// ArgData layout: [area(1), block(1), sub(1), sub2(1), region(4), ...]
    /// Match solely on region == primaryRegion. Replace region and map bytes.
    /// </summary>
    private static bool TryPatchWarpPlayer(
        EMEVD.Instruction instr,
        int primaryRegion, int altRegion, byte[] altMapBytes)
    {
        if (instr.Bank != 2003 || instr.ID != 14 || instr.ArgData.Length < 8)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 4);
        if (region != primaryRegion)
            return false;

        // Patch map bytes and region
        altMapBytes.CopyTo(instr.ArgData, 0);
        BitConverter.GetBytes(altRegion).CopyTo(instr.ArgData, 4);
        return true;
    }

    /// <summary>
    /// Try to patch a PlayCutsceneToPlayerAndWarp instruction (bank 2002, id 11/12).
    /// ArgData layout: [cutsceneId(4), playback(4), region(4), mapId(4), ...]
    /// Match solely on region == primaryRegion. Replace region and map.
    /// The map may already be m11_05 (vanilla, since EventEditor has no MapArg
    /// for this instruction) or m11_00 (if another injector changed it).
    /// </summary>
    private static bool TryPatchCutsceneWarp(
        EMEVD.Instruction instr,
        int primaryRegion, int altRegion, int altMapPacked)
    {
        if (instr.Bank != 2002 || (instr.ID != 11 && instr.ID != 12))
            return false;
        if (instr.ArgData.Length < 16)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 8);
        if (region != primaryRegion)
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
