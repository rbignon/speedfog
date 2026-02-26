using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Patches compiled fogwarp events to eliminate flag 330 (Sealing Tree burned) dependency.
///
/// FogMod's fogwarp template compiles an AlternateSide branch: when flag 330 is ON,
/// the warp goes to m61_44_45_10 (post-burning variant) instead of m61_44_45_00
/// (primary, where Romina lives). Something outside EMEVD sets flag 330 on saves
/// with prior DLC progress, causing the wrong map variant to load.
///
/// Rather than chasing the flag source, this patcher replaces the alt warp destinations
/// with primary destinations in all compiled fogwarp events. This is the reverse of
/// ErdtreeWarpPatcher (which replaces primary m11_00 with alt m11_05).
///
/// No SetEventFlag insertion is needed (unlike ErdtreeWarpPatcher) because we want
/// flag 330 to stay OFF — the primary (non-burned) map variant is always correct.
/// </summary>
public static class SealingTreeWarpPatcher
{
    /// <summary>
    /// Patch all EMEVD files: replace alt warp destinations with primary destinations
    /// for all entrances that use AlternateFlag 330.
    /// </summary>
    /// <param name="modDir">Mod output directory containing event/ subdirectory</param>
    /// <param name="entrances">
    /// List of (altRegion, primaryRegion, primaryMap) tuples.
    /// altRegion = the AlternateSide warp region to match (m61_44_45_10 side).
    /// primaryRegion = the primary warp region to replace with (m61_44_45_00 side).
    /// primaryMap = the primary map string, e.g. "m61_44_45_00".
    /// </param>
    public static void Patch(string modDir, List<(int altRegion, int primaryRegion, string primaryMap)> entrances)
    {
        var eventDir = Path.Combine(modDir, "event");
        if (!Directory.Exists(eventDir))
        {
            Console.WriteLine("Warning: event directory not found, skipping Sealing Tree warp patch");
            return;
        }

        // Pre-compute map bytes/packed for each entrance
        var patchTargets = entrances
            .Where(e => e.altRegion != 0 && e.primaryRegion != 0)
            .Select(e => (
                e.altRegion,
                e.primaryRegion,
                primaryMapBytes: ErdtreeWarpPatcher.ParseMapBytes(e.primaryMap),
                primaryMapPacked: ErdtreeWarpPatcher.PackMapId(e.primaryMap)
            ))
            .ToList();

        if (patchTargets.Count == 0)
        {
            Console.WriteLine("Sealing Tree warp fix: skipping (no valid entrances)");
            return;
        }

        int totalPatched = 0;

        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var emevd = EMEVD.Read(file);
            int patched = PatchEmevd(emevd, patchTargets);
            if (patched > 0)
            {
                emevd.Write(file);
                Console.WriteLine($"  {Path.GetFileName(file)}: patched {patched} sealing tree warp(s)");
                totalPatched += patched;
            }
        }

        if (totalPatched > 0)
        {
            Console.WriteLine($"Sealing Tree warp fix: patched {totalPatched} warp(s) across {patchTargets.Count} entrance(s)");
        }
        else
        {
            Console.WriteLine("Sealing Tree warp fix: no matching warps found (entrances may not be connected)");
        }
    }

    /// <summary>
    /// Patch all events in an EMEVD. Returns total count of patched instructions.
    /// Unlike ErdtreeWarpPatcher, does NOT insert SetEventFlag — we want flag 330 OFF.
    /// </summary>
    internal static int PatchEmevd(
        EMEVD emevd,
        List<(int altRegion, int primaryRegion, byte[] primaryMapBytes, int primaryMapPacked)> targets)
    {
        int total = 0;
        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                foreach (var target in targets)
                {
                    if (TryPatchWarpPlayer(instr, target.altRegion, target.primaryRegion, target.primaryMapBytes))
                    {
                        total++;
                        break; // instruction matched, no need to check other targets
                    }
                    if (TryPatchCutsceneWarp(instr, target.altRegion, target.primaryRegion, target.primaryMapPacked))
                    {
                        total++;
                        break;
                    }
                }
            }
        }
        return total;
    }

    /// <summary>
    /// WarpPlayer (bank 2003, id 14): [area(1), block(1), sub(1), sub2(1), region(4), ...]
    /// Replace alt region with primary region and primary map bytes.
    /// </summary>
    internal static bool TryPatchWarpPlayer(
        EMEVD.Instruction instr, int altRegion, int primaryRegion, byte[] primaryMapBytes)
    {
        if (instr.Bank != 2003 || instr.ID != 14 || instr.ArgData.Length < 8)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 4);
        if (region != altRegion)
            return false;

        primaryMapBytes.CopyTo(instr.ArgData, 0);
        BitConverter.GetBytes(primaryRegion).CopyTo(instr.ArgData, 4);
        return true;
    }

    /// <summary>
    /// PlayCutsceneToPlayerAndWarp (bank 2002, id 11/12):
    /// [cutsceneId(4), playback(4), region(4), mapPacked(4), ...]
    /// Replace alt region with primary region and primary map packed.
    /// </summary>
    internal static bool TryPatchCutsceneWarp(
        EMEVD.Instruction instr, int altRegion, int primaryRegion, int primaryMapPacked)
    {
        if (instr.Bank != 2002 || (instr.ID != 11 && instr.ID != 12))
            return false;
        if (instr.ArgData.Length < 16)
            return false;

        int region = BitConverter.ToInt32(instr.ArgData, 8);
        if (region != altRegion)
            return false;

        BitConverter.GetBytes(primaryRegion).CopyTo(instr.ArgData, 8);
        BitConverter.GetBytes(primaryMapPacked).CopyTo(instr.ArgData, 12);
        return true;
    }
}
