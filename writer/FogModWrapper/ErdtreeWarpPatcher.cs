using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Patches all fogwarp events targeting leyndell_erdtree (m11_00) to warp directly
/// to leyndell2_erdtree (m11_05), replacing FogMod's alt-warp mechanism with a direct
/// warp and on-demand flag 300 activation.
///
/// In vanilla, the fogwarp template (9005777) compiles an alt-warp: primary goes to
/// m11_00, and if flag 300 is ON (set by Maliketh death), it warps to m11_05 instead.
/// This patcher replaces the primary destination with m11_05 coordinates and injects
/// SetEventFlag(300, ON) just before the warp, so the engine loads the correct assets.
/// Flag 300 tells the engine which map tile to load at Leyndell coordinates (m11_00
/// vs m11_05). It stays OFF during the run so leyndell_* connections work normally,
/// and is set ON only at the moment of warping to the Erdtree.
///
/// Scans ALL EMEVD files in the mod output since the fog gate could be on any map.
/// </summary>
public static class ErdtreeWarpPatcher
{
    /// <summary>
    /// Flag 300 = Erdtree burning. Set before warping so the engine loads m11_05 assets.
    /// </summary>
    private const int ERDTREE_BURNING_FLAG = 300;

    /// <summary>
    /// Patch all events in an EMEVD. Returns total count of patched warp instructions.
    /// For each patched warp, inserts SetEventFlag(300, ON) just before it.
    /// Also NOPs any SkipIfEventFlag(300, ON) in patched events, since the alt-warp
    /// branch is vestigial after both branches are rewritten to target m11_05.
    /// This prevents flag 300 from skipping zone tracking injections.
    /// </summary>
    public static int PatchEmevd(
        EMEVD emevd, int primaryRegion, int altRegion, byte[] altMapBytes, int altMapPacked)
    {
        int total = 0;
        foreach (var evt in emevd.Events)
        {
            // Collect indices to insert at (reverse order to preserve positions)
            var insertions = new List<int>();
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];
                if (TryPatchWarpPlayer(instr, primaryRegion, altRegion, altMapBytes))
                    insertions.Add(i);
                else if (TryPatchCutsceneWarp(instr, primaryRegion, altRegion, altMapPacked))
                    insertions.Add(i);
            }

            if (insertions.Count == 0)
                continue;

            // NOP SkipIfEventFlag(300, ON) in this event. Both branches now target
            // m11_05, so the alt-warp skip is vestigial. Removing it prevents flag 300
            // (set by Event 900 during Maliketh WarpBonfire) from skipping zone
            // tracking SetEventFlag instructions injected by ZoneTrackingInjector.
            NopSkipIfEventFlag(evt, ERDTREE_BURNING_FLAG);

            // Insert SetEventFlag(300, ON) before each patched warp (reverse to keep indices valid)
            for (int j = insertions.Count - 1; j >= 0; j--)
            {
                evt.Instructions.Insert(insertions[j], MakeSetEventFlag(ERDTREE_BURNING_FLAG));
                // Shift Parameter entries for instructions at or after insertion point
                foreach (var param in evt.Parameters)
                {
                    if (param.InstructionIndex >= insertions[j])
                        param.InstructionIndex++;
                }
            }

            total += insertions.Count;
        }
        return total;
    }

    /// <summary>
    /// Replace SkipIfEventFlag(flagId, ON) instructions with WaitFixedTime(0).
    /// SkipIfEventFlag (bank 1003, id 1): [Skip(byte@0), State(byte@1), FlagType(byte@2), pad(1), FlagID(uint32@4)]
    /// </summary>
    internal static int NopSkipIfEventFlag(EMEVD.Event evt, int flagId)
    {
        int count = 0;
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var instr = evt.Instructions[i];
            if (instr.Bank == 1003 && instr.ID == 1 && instr.ArgData.Length >= 8)
            {
                uint flag = BitConverter.ToUInt32(instr.ArgData, 4);
                byte state = instr.ArgData[1];
                if (flag == (uint)flagId && state == 1) // ON
                {
                    evt.Instructions[i] = MakeWaitFixedTime(0f);
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Create a WaitFixedTime(seconds) instruction: bank 1001, id 0.
    /// Used as a harmless no-op that preserves instruction count and parameter offsets.
    /// </summary>
    private static EMEVD.Instruction MakeWaitFixedTime(float seconds)
    {
        var args = new byte[4];
        BitConverter.GetBytes(seconds).CopyTo(args, 0);
        return new EMEVD.Instruction(1001, 0, args);
    }

    /// <summary>
    /// Create a SetEventFlag instruction: bank 2003, id 66.
    /// Args: [targetType(4)=0, flagId(4), state(1)=1, padding(3)]
    /// </summary>
    internal static EMEVD.Instruction MakeSetEventFlag(int flagId)
    {
        var args = new byte[12];
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        args[8] = 1; // ON
        return new EMEVD.Instruction(2003, 66, args);
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
