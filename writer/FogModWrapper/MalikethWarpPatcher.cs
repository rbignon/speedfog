using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Patches Maliketh warp events to target Ashen Leyndell (m11_05) instead of
/// pre-ash Leyndell (m11_00).
///
/// Event 900 (Maliketh cutscene) sets flag 300 (Erdtree burning) BEFORE warping.
/// This causes the game to load m11_05, but EventEditor set the warp destination
/// to the primary FogMod region in m11_00 — which doesn't exist in m11_05.
///
/// The list5 portal event (bonfire portal in m13_00_00_00) has the same issue:
/// warpToSide() generates WarpPlayer targeting the primary m11_00 region.
///
/// Only patches Event 900 (common.emevd.dcx) and events in m13_00_00_00.emevd.dcx.
/// Does NOT touch fogwarp events in m11_00/m11_05 — they have built-in alt-warp
/// logic in the fogwarp template (9005777).
/// </summary>
public static class MalikethWarpPatcher
{
    private const int EVENT_900_ID = 900;
    private const string MALIKETH_MAP = "m13_00_00_00";

    /// <summary>
    /// Patch Event 900 in common.emevd.dcx and list5 portal in m13_00_00_00.emevd.dcx.
    /// </summary>
    /// <param name="modDir">Mod output directory containing event/ subdirectory</param>
    /// <param name="primaryRegion">FogMod region for m11_00 entrance (to match)</param>
    /// <param name="altRegion">FogMod region for m11_05 entrance (replacement)</param>
    /// <param name="altMap">Alternate map string, e.g. "m11_05_00_00"</param>
    public static void Patch(string modDir, int primaryRegion, int altRegion, string altMap)
    {
        if (primaryRegion == 0 || altRegion == 0)
        {
            Console.WriteLine("Maliketh warp: skipping (no region data)");
            return;
        }

        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping Maliketh warp patch");
            return;
        }

        int totalPatched = 0;

        // 1. Patch Event 900 in common.emevd.dcx
        var commonPath = Path.Combine(eventDir, "common.emevd.dcx");
        if (File.Exists(commonPath))
        {
            var emevd = EMEVD.Read(commonPath);
            var evt900 = emevd.Events.Find(e => e.ID == EVENT_900_ID);
            if (evt900 != null)
            {
                int patched = PatchEvent(evt900, primaryRegion, altRegion, altMap);
                if (patched > 0)
                {
                    emevd.Write(commonPath);
                    Console.WriteLine($"  Event 900: patched {patched} warp(s)");
                    totalPatched += patched;
                }
            }
            else
            {
                Console.WriteLine("  Warning: Event 900 not found in common.emevd.dcx");
            }
        }

        // 2. Patch list5 portal event in m13_00_00_00.emevd.dcx
        var m13Path = Path.Combine(eventDir, $"{MALIKETH_MAP}.emevd.dcx");
        if (File.Exists(m13Path))
        {
            var emevd = EMEVD.Read(m13Path);
            int patched = PatchEmevdEvents(emevd, primaryRegion, altRegion, altMap);
            if (patched > 0)
            {
                emevd.Write(m13Path);
                Console.WriteLine($"  {MALIKETH_MAP}: patched {patched} warp(s)");
                totalPatched += patched;
            }
        }

        if (totalPatched > 0)
        {
            Console.WriteLine($"Maliketh warp fix: patched {totalPatched} warp(s) " +
                $"(region {primaryRegion} -> {altRegion}, map -> {altMap})");
        }
        else
        {
            Console.WriteLine($"Maliketh warp fix: no matching warps found " +
                $"(region {primaryRegion} may not be connected)");
        }
    }

    /// <summary>
    /// Patch warp instructions in a single event. Returns count of patched instructions.
    /// </summary>
    internal static int PatchEvent(EMEVD.Event evt, int primaryRegion, int altRegion, string altMap)
    {
        byte[] altMapBytes = ParseMapBytes(altMap);
        int altMapPacked = PackMapId(altMap);
        int patched = 0;

        foreach (var instr in evt.Instructions)
        {
            if (TryPatchWarpPlayer(instr, primaryRegion, altRegion, altMapBytes))
                patched++;
            else if (TryPatchCutsceneWarp(instr, primaryRegion, altRegion, altMapPacked))
                patched++;
        }

        return patched;
    }

    /// <summary>
    /// Patch all events in an EMEVD that contain warp instructions matching the primary region.
    /// Used for m13_00_00_00.emevd.dcx where the list5 portal has a dynamic event ID.
    /// Returns total count of patched instructions.
    /// </summary>
    internal static int PatchEmevdEvents(EMEVD emevd, int primaryRegion, int altRegion, string altMap)
    {
        byte[] altMapBytes = ParseMapBytes(altMap);
        int altMapPacked = PackMapId(altMap);
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
    private static bool TryPatchWarpPlayer(
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
    private static bool TryPatchCutsceneWarp(
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
