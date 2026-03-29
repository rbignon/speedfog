using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Neutralizes vanilla EMEVD events that set AlternateFlag values (flags 300 and 330)
/// which interfere with SpeedFog's fogwarp patching and zone tracking.
///
/// Flag 300 (Erdtree burning): Set by Event 900 when the Forge bonfire transition fires.
/// If ON before the player reaches an Erdtree fogwarp, the compiled SkipIfEventFlag(300)
/// skips the zone tracking SetEventFlag injected by ZoneTrackingInjector.
/// Fix: NOP SetEventFlag(300, ON) in Event 900, clear flag 300 in Event 0.
///
/// Flag 330 (Sealing Tree burned): Set by Event 915 after Dancing Lion defeat.
/// If ON, fogwarps targeting Romina's area use the post-burning variant where Romina
/// doesn't exist. Fix: NOP SetEventFlag(330, ON) in Event 915, clear flag 330 in Event 0.
///
/// In both cases, the corresponding warp patcher (ErdtreeWarpPatcher for 300,
/// SealingTreeWarpPatcher for 330) handles the flag at the correct moment.
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
    /// Flag 300 = Erdtree burning. Controls m11_00 vs m11_05 map tile loading.
    /// Set by Event 900 when the Forge bonfire transition fires (flag 9116 trigger).
    /// </summary>
    private const int ERDTREE_BURNING_FLAG = 300;

    /// <summary>
    /// Vanilla Event 900 in common.emevd -- Forge bonfire transition that sets flag 300.
    /// FogMod repurposes this event for farumazula_maliketh connections.
    /// </summary>
    private const int EVENT_900_ID = 900;

    /// <summary>
    /// Patch the provided common EMEVD to neutralize Event 915's SetEventFlag(330, ON)
    /// and Event 900's SetEventFlag(300, ON).
    /// Also adds SetEventFlag(flag, OFF) at the start of Event 0 to clear
    /// any leftover flags from a previous save.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    public static void Patch(EMEVD commonEmevd)
    {
        int nop330 = 0, nop300 = 0;
        bool cleared330 = false, cleared300 = false;

        // 1. NOP SetEventFlag(330, ON) in Event 915
        var evt915 = commonEmevd.Events.FirstOrDefault(e => e.ID == EVENT_915_ID);
        if (evt915 != null)
        {
            nop330 = NopSetEventFlag(evt915, SEALING_TREE_FLAG);
        }

        // 2. NOP SetEventFlag(300, ON) in Event 900
        var evt900 = commonEmevd.Events.FirstOrDefault(e => e.ID == EVENT_900_ID);
        if (evt900 != null)
        {
            nop300 = NopSetEventFlag(evt900, ERDTREE_BURNING_FLAG);
        }

        // 3. Clear both flags at game start
        var evt0 = commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (evt0 != null)
        {
            if (nop330 > 0 || evt915 != null)
            {
                InsertClearFlag(evt0, SEALING_TREE_FLAG);
                cleared330 = true;
            }
            if (nop300 > 0 || evt900 != null)
            {
                InsertClearFlag(evt0, ERDTREE_BURNING_FLAG);
                cleared300 = true;
            }
        }

        // Log results
        if (nop330 > 0 || cleared330)
        {
            Console.WriteLine($"AlternateFlag fix: NOP'd {nop330} SetEventFlag(330) in Event 915"
                + (cleared330 ? ", cleared flag 330 in Event 0" : ""));
        }
        if (nop300 > 0 || cleared300)
        {
            Console.WriteLine($"AlternateFlag fix: NOP'd {nop300} SetEventFlag(300) in Event 900"
                + (cleared300 ? ", cleared flag 300 in Event 0" : ""));
        }
        if (nop330 == 0 && nop300 == 0 && !cleared330 && !cleared300)
        {
            Console.WriteLine("AlternateFlag fix: Events 900/915 not found in common.emevd");
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
