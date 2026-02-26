using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Patches vanilla Event 915 in common.emevd to prevent it from setting flag 330
/// (Sealing Tree burned / DLC Erdtree gate).
///
/// In vanilla, Event 915 is a ContinueOnRest event that waits for flag 9140
/// (Dancing Lion defeated), then sets flag 330 and warps the player to the
/// post-burning DLC area. If a player loads SpeedFog on a save where flag 9140
/// is already ON from a previous DLC playthrough, Event 915 immediately fires
/// and sets flag 330. This causes fogwarps targeting Romina's area (m61_44_45_00)
/// to use the AlternateOf destination (m61_44_45_10 — the post-burning variant),
/// where Romina doesn't exist.
///
/// The fix: replace SetEventFlag(330, ON) in Event 915 with a no-op (WaitFixedTime(0)).
/// This is safe because SpeedFog never needs the Sealing Tree to burn — the
/// AlternateOf fogwarp mechanism handles map variant selection via flag 330 checks,
/// and we always want the primary (non-burned) variant.
///
/// Analogous to ErdtreeWarpPatcher which handles flag 300 (base game Erdtree).
/// </summary>
public static class SealingTreePatcher
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
    /// Patch common.emevd to neutralize Event 915's SetEventFlag(330, ON).
    /// Also adds SetEventFlag(330, OFF) at the start of common Event 0 to clear
    /// any leftover flag from a previous save.
    /// </summary>
    public static void Patch(string modDir)
    {
        var commonPath = Path.Combine(modDir, "event", "common.emevd.dcx");
        if (!File.Exists(commonPath))
        {
            Console.WriteLine("Warning: common.emevd.dcx not found, skipping Sealing Tree patch");
            return;
        }

        var emevd = EMEVD.Read(commonPath);
        int nopCount = 0;
        bool cleared = false;

        // 1. Find Event 915 and NOP out SetEventFlag(330, ON)
        var evt915 = emevd.Events.FirstOrDefault(e => e.ID == EVENT_915_ID);
        if (evt915 != null)
        {
            nopCount = NopSetEventFlag(evt915, SEALING_TREE_FLAG);
        }

        // 2. Clear flag 330 at game start to handle saves where it's already ON.
        var evt0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (evt0 != null)
        {
            InsertClearFlag(evt0, SEALING_TREE_FLAG);
            cleared = true;
        }

        if (nopCount > 0 || cleared)
        {
            emevd.Write(commonPath);
            Console.WriteLine($"Sealing Tree fix: NOP'd {nopCount} SetEventFlag(330) in Event 915"
                + (cleared ? ", cleared flag 330 in Event 0" : ""));
        }
        else
        {
            Console.WriteLine("Sealing Tree fix: Event 915 not found in common.emevd");
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
    private static EMEVD.Instruction MakeWaitFixedTime(float seconds)
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
