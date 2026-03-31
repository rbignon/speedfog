using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Neutralizes vanilla EMEVD events that set AlternateFlag values and clears
/// stale flags at game start.
///
/// Flag 300 (Erdtree burning): NOT cleared in Event 0 and NOT neutralized in Event 900.
/// Event 900's SetEventFlag(300, ON) is needed by FogMod's "Repeat warp" grace menu,
/// whose condition checks WarpBonfireFlag(300). Clearing flag 300 in Event 0 breaks
/// this because Event 0 re-executes after the WarpBonfire warp, resetting flag 300
/// before the player reaches a grace. ErdtreeWarpPatcher handles flag 300 at the
/// correct moment (just before each Erdtree warp) and NOPs SkipIfEventFlag(300) in
/// fogwarp events to prevent zone tracking skips.
///
/// Flag 330 (Sealing Tree burned): Set by Event 915 after Dancing Lion defeat.
/// If ON, fogwarps targeting Romina's area use the post-burning variant where Romina
/// doesn't exist. Fix: NOP SetEventFlag(330, ON) in Event 915, clear flag 330 in Event 0.
/// SealingTreeWarpPatcher handles the warp destination patching.
/// </summary>
public static class AlternateFlagPatcher
{
    /// <summary>
    /// Flag 330 = DLC Sealing Tree burned. Controls m61_44_45_00 vs m61_44_45_10.
    /// </summary>
    private const int SEALING_TREE_FLAG = 330;

    /// <summary>
    /// Vanilla Event 915 in common.emevd — the only automatic event that sets flag 330.
    /// </summary>
    private const int EVENT_915_ID = 915;

    /// <summary>
    /// Patch the provided common EMEVD to neutralize Event 915's SetEventFlag(330, ON)
    /// and clear flag 330 in Event 0 for stale saves.
    ///
    /// Event 900's SetEventFlag(300, ON) is NOT neutralized: it is needed by FogMod's
    /// "Repeat warp" grace menu, whose condition checks WarpBonfireFlag(300).
    /// The zone tracking skip caused by flag 300 is handled by ErdtreeWarpPatcher,
    /// which NOPs the SkipIfEventFlag(300) in compiled fogwarp events.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    public static void Patch(EMEVD commonEmevd)
    {
        int nop330 = 0;
        bool cleared330 = false;

        // 1. NOP SetEventFlag(330, ON) in Event 915
        var evt915 = commonEmevd.Events.FirstOrDefault(e => e.ID == EVENT_915_ID);
        if (evt915 != null)
        {
            nop330 = NopSetEventFlag(evt915, SEALING_TREE_FLAG);
        }

        // 2. Clear flag 330 at game start for stale saves.
        // Flag 300 is NOT cleared here: clearing it in Event 0 breaks the
        // "Repeat warp" grace menu for WarpBonfire transitions (Maliketh),
        // because Event 0 re-executes after the WarpBonfire warp and resets
        // flag 300 before the player reaches a grace. ErdtreeWarpPatcher
        // handles flag 300 at the correct moment (just before each warp).
        var evt0 = commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (evt0 != null)
        {
            if (nop330 > 0 || evt915 != null)
            {
                InsertClearFlag(evt0, SEALING_TREE_FLAG);
                cleared330 = true;
            }
        }

        // Log results
        if (nop330 > 0 || cleared330)
        {
            Console.WriteLine($"AlternateFlag fix: NOP'd {nop330} SetEventFlag(330) in Event 915"
                + (cleared330 ? ", cleared flag 330 in Event 0" : ""));
        }
        if (nop330 == 0 && !cleared330)
        {
            Console.WriteLine("AlternateFlag fix: Event 915 not found in common.emevd");
        }
    }

    /// <summary>
    /// Insert SetEventFlag(flagId, OFF) at the beginning of an event.
    /// Shifts all Parameter instruction indices by 1 to preserve offsets.
    /// </summary>
    internal static void InsertClearFlag(EMEVD.Event evt, int flagId)
    {
        evt.Instructions.Insert(0, MakeSetEventFlag(flagId, false));
        foreach (var param in evt.Parameters)
        {
            param.InstructionIndex++;
        }
    }

    /// <summary>
    /// Replace all SetEventFlag instructions targeting the given flag with WaitFixedTime(0)
    /// (a harmless no-op that preserves instruction count and parameter offsets).
    /// </summary>
    internal static int NopSetEventFlag(EMEVD.Event evt, int flagId)
    {
        int count = 0;
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var instr = evt.Instructions[i];
            // SetEventFlag = bank 2003, id 66
            // Args: [targetType(4), flagId(4), state(1), padding(3)]
            if (instr.Bank == 2003 && instr.ID == 66 && instr.ArgData.Length >= 12)
            {
                int flag = BitConverter.ToInt32(instr.ArgData, 4);
                byte state = instr.ArgData[8];
                if (flag == flagId && state == 1) // Only patch ON, not OFF
                {
                    // Replace with WaitFixedTime(0) — bank 1001, id 0, arg=0.0f
                    // This is a no-op: wait 0 seconds, then continue
                    evt.Instructions[i] = MakeWaitFixedTime(0f);
                    count++;
                }
            }
        }
        return count;
    }

    /// <summary>
    /// Create a WaitFixedTime(seconds) instruction: bank 1001, id 0.
    /// Args: [seconds(4 float)]
    /// </summary>
    internal static EMEVD.Instruction MakeWaitFixedTime(float seconds)
    {
        var args = new byte[4];
        BitConverter.GetBytes(seconds).CopyTo(args, 0);
        return new EMEVD.Instruction(1001, 0, args);
    }

    /// <summary>
    /// Create a SetEventFlag instruction: bank 2003, id 66.
    /// Args: [targetType(4)=0, flagId(4), state(1), padding(3)]
    /// </summary>
    private static EMEVD.Instruction MakeSetEventFlag(int flagId, bool on)
    {
        var args = new byte[12];
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        args[8] = (byte)(on ? 1 : 0);
        return new EMEVD.Instruction(2003, 66, args);
    }
}
