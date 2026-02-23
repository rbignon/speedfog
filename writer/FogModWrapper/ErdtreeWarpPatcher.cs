using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Patches all fogwarp events targeting leyndell_erdtree (m11_00) to warp directly
/// to leyndell2_erdtree (m11_05), removing the dependency on flag 300 (Erdtree burning).
///
/// In vanilla, the fogwarp template (9005777) compiles an alt-warp: primary goes to
/// m11_00, and if flag 300 is ON (set by Maliketh death), it warps to m11_05 instead.
/// This patcher replaces the primary destination with m11_05 coordinates, so the fog
/// gate always reaches leyndell2_erdtree regardless of Maliketh/flag 300.
///
/// Scans ALL EMEVD files in the mod output since the fog gate could be on any map.
/// </summary>
public static class ErdtreeWarpPatcher
{
    /// <summary>
    /// Patch all EMEVD files: replace WarpPlayer/CutsceneWarp instructions targeting
    /// the primary region (m11_00) with the alt region (m11_05).
    /// </summary>
    /// <param name="modDir">Mod output directory containing event/ subdirectory</param>
    /// <param name="primaryRegion">FogMod region for m11_00 entrance (to match)</param>
    /// <param name="altRegion">FogMod region for m11_05 entrance (replacement)</param>
    /// <param name="altMap">Alternate map string, e.g. "m11_05_00_00"</param>
    public static void Patch(string modDir, int primaryRegion, int altRegion, string altMap)
    {
        if (primaryRegion == 0 || altRegion == 0)
        {
            Console.WriteLine("Erdtree warp: skipping (no region data)");
            return;
        }

        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping Erdtree warp patch");
            return;
        }

        byte[] altMapBytes = ParseMapBytes(altMap);
        int altMapPacked = PackMapId(altMap);
        int totalPatched = 0;

        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(file);
            int patched = PatchEmevd(emevd, primaryRegion, altRegion, altMapBytes, altMapPacked);
            if (patched > 0)
            {
                emevd.Write(file);
                Console.WriteLine($"  {Path.GetFileName(file)}: patched {patched} erdtree warp(s)");
                totalPatched += patched;
            }
        }

        if (totalPatched > 0)
        {
            Console.WriteLine($"Erdtree warp fix: patched {totalPatched} warp(s) " +
                $"(region {primaryRegion} -> {altRegion}, map -> {altMap})");
        }
        else
        {
            Console.WriteLine($"Erdtree warp fix: no matching warps found " +
                $"(region {primaryRegion} may not be connected)");
        }
    }

    /// <summary>
    /// Patch all events in an EMEVD. Returns total count of patched instructions.
    /// </summary>
    internal static int PatchEmevd(
        EMEVD emevd, int primaryRegion, int altRegion, byte[] altMapBytes, int altMapPacked)
    {
        int total = 0;
        foreach (var evt in emevd.Events)
        {
            foreach (var instr in evt.Instructions)
            {
                if (TryPatchWarpPlayer(instr, primaryRegion, altRegion, altMapBytes))
                    total++;
                else if (TryPatchCutsceneWarp(instr, primaryRegion, altRegion, altMapPacked))
                    total++;
            }
        }
        return total;
    }

    /// <summary>
    /// WarpPlayer (bank 2003, id 14): [area(1), block(1), sub(1), sub2(1), region(4), ...]
    /// </summary>
    internal static bool TryPatchWarpPlayer(
        EMEVD.Instruction instr, int primaryRegion, int altRegion, byte[] altMapBytes)
    {
        if (instr.Bank != 2003 || instr.ID != 14 || instr.ArgData.Length < 8)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 4);
        if (region != primaryRegion)
            return false;

        altMapBytes.CopyTo(instr.ArgData, 0);
        BitConverter.GetBytes(altRegion).CopyTo(instr.ArgData, 4);
        return true;
    }

    /// <summary>
    /// PlayCutsceneToPlayerAndWarp (bank 2002, id 11/12):
    /// [cutsceneId(4), playback(4), region(4), mapPacked(4), ...]
    /// </summary>
    internal static bool TryPatchCutsceneWarp(
        EMEVD.Instruction instr, int primaryRegion, int altRegion, int altMapPacked)
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

    /// <summary>Parse "m11_05_00_00" -> [11, 5, 0, 0]</summary>
    internal static byte[] ParseMapBytes(string map)
    {
        var parts = map.TrimStart('m').Split('_');
        return [byte.Parse(parts[0]), byte.Parse(parts[1]), byte.Parse(parts[2]), byte.Parse(parts[3])];
    }

    /// <summary>Pack "m11_05_00_00" -> 11050000</summary>
    internal static int PackMapId(string map)
    {
        var parts = map.TrimStart('m').Split('_');
        return int.Parse(parts[0]) * 1000000
             + int.Parse(parts[1]) * 10000
             + int.Parse(parts[2]) * 100
             + int.Parse(parts[3]);
    }
}
