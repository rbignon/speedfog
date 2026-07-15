using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Gates the "stable position" pulse in FogMod's compiled common_makestable
/// event (fogevents.txt ID 755850000) on the end of the loading screen.
///
/// FogMod remaps every PlayRegionParam.pcPositionSaveLimitEventFlagId (flag
/// ON = the engine saves the player position, OFF = it does not) to a temp
/// flag that common_makestable turns ON for 10 frames after every loading
/// screen, then OFF until the region's boss defeat flag rises. The pulse
/// anchors the arrival position when warping into a boss arena, so a
/// quit-out mid-fight respawns at the arena entrance instead of a stale
/// pre-warp position.
///
/// 10 frames (~0.3 s) races against the engine grounding the player after
/// the warp fade-in: when the pulse misses, no position is ever saved inside
/// the gated region and the next quit-out falls back to the last grace. A
/// longer fixed window is no better: the engine keeps re-saving the position
/// while the flag is ON, so the anchor drifts to wherever the player is when
/// the window closes.
///
/// Instead, wait for engine flag 2200 (world clock stopped: ON during
/// loading screens and cutscenes, drops at fade-in end) to turn OFF before
/// running FogRando's original 10-frame pulse. The anchor lands where the
/// player gains control, at the arena entrance. A timeout OR'd into the wait
/// keeps the pulse alive if the flag never drops (e.g. an event freezing the
/// world clock with FreezeTime); in that degenerate case the behavior is a
/// fixed window again. See docs/quitout-respawn.md.
/// </summary>
public static class MakestablePulsePatcher
{
    /// <summary>FogRando's common_makestable event (fogevents.txt).</summary>
    public const int MAKESTABLE_EVENT_ID = 755850000;

    /// <summary>Pulse length compiled by FogMod from the template.</summary>
    public const int VANILLA_PULSE_FRAMES = 10;

    /// <summary>Engine flag: world clock stopped (loading screen, cutscene).</summary>
    public const uint CLOCK_STOPPED_FLAG = 2200;

    /// <summary>Timeout before pulsing anyway if flag 2200 never drops.</summary>
    public const float FALLBACK_TIMEOUT_SECONDS = 5f;

    /// <summary>
    /// Insert the load-end gate before the WaitFixedTimeFrames(10) pulse in
    /// event 755850000:
    ///
    ///   IfEventFlag(OR_01, OFF, EventFlag, 2200)   // fade-in finished
    ///   IfElapsedSeconds(OR_01, 5)                 // or safety timeout
    ///   IfConditionGroup(MAIN, ON, OR_01)
    ///
    /// The pulse itself keeps FogRando's 10 frames. Only an event containing
    /// exactly one wait with the known vanilla duration is patched, so a
    /// changed FogRando template is left alone (and logged) rather than
    /// blindly patched.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    /// <returns>1 when the gate was inserted, 0 otherwise</returns>
    public static int Patch(EMEVD commonEmevd)
    {
        var evt = commonEmevd.Events.FirstOrDefault(e => e.ID == MAKESTABLE_EVENT_ID);
        if (evt == null)
        {
            Console.WriteLine("Makestable pulse fix: event 755850000 not found in common.emevd");
            return 0;
        }

        // Idempotence: a gate already waiting on flag 2200 means the event
        // was patched (bank 3, id 0 = IfEventFlag, flag id at byte 4).
        if (evt.Instructions.Any(i => i.Bank == 3 && i.ID == 0
            && i.ArgData.Length >= 8
            && BitConverter.ToUInt32(i.ArgData, 4) == CLOCK_STOPPED_FLAG))
        {
            Console.WriteLine("Makestable pulse fix: event 755850000 already gated on "
                + $"flag {CLOCK_STOPPED_FLAG}, leaving the event alone");
            return 0;
        }

        // WaitFixedTimeFrames = bank 1001, id 1, args: [frames(int32)]
        var waitIndexes = evt.Instructions
            .Select((instr, i) => (instr, i))
            .Where(x => x.instr.Bank == 1001 && x.instr.ID == 1
                && x.instr.ArgData.Length >= 4
                && BitConverter.ToInt32(x.instr.ArgData, 0) == VANILLA_PULSE_FRAMES)
            .Select(x => x.i)
            .ToList();
        if (waitIndexes.Count != 1)
        {
            Console.WriteLine("Makestable pulse fix: expected exactly one "
                + $"WaitFixedTimeFrames({VANILLA_PULSE_FRAMES}) in event 755850000, "
                + $"found {waitIndexes.Count}, leaving the event alone");
            return 0;
        }

        int waitIdx = waitIndexes[0];
        evt.Instructions.InsertRange(waitIdx, new[]
        {
            MakeIfEventFlagOff(CLOCK_STOPPED_FLAG),
            MakeIfElapsedSeconds(FALLBACK_TIMEOUT_SECONDS),
            MakeIfConditionGroup(),
        });

        // The event is parameterized (X0_4/X4_4): entries pointing at
        // instructions past the insertion point must follow them.
        foreach (var param in evt.Parameters)
        {
            if (param.InstructionIndex >= waitIdx)
            {
                param.InstructionIndex += 3;
            }
        }

        Console.WriteLine("Makestable pulse fix: pulse now waits for flag "
            + $"{CLOCK_STOPPED_FLAG} OFF (load end) or {FALLBACK_TIMEOUT_SECONDS}s, "
            + $"then runs {VANILLA_PULSE_FRAMES} frames");
        return 1;
    }

    /// <summary>
    /// IfEventFlag(OR_01, OFF, EventFlag, flagId): bank 3, id 0.
    /// Args: [group(s8), state(u8), flagType(u8), pad, flagId(u32)]
    /// </summary>
    private static EMEVD.Instruction MakeIfEventFlagOff(uint flagId)
    {
        var args = new byte[8];
        args[0] = unchecked((byte)-1); // OR_01
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        return new EMEVD.Instruction(3, 0, args);
    }

    /// <summary>
    /// IfElapsedSeconds(OR_01, seconds): bank 1, id 0.
    /// Args: [group(s8), pad x3, seconds(f32)]
    /// </summary>
    private static EMEVD.Instruction MakeIfElapsedSeconds(float seconds)
    {
        var args = new byte[8];
        args[0] = unchecked((byte)-1); // OR_01
        BitConverter.GetBytes(seconds).CopyTo(args, 4);
        return new EMEVD.Instruction(1, 0, args);
    }

    /// <summary>
    /// IfConditionGroup(MAIN, ON, OR_01): bank 0, id 0.
    /// Args: [result(s8), state(u8), target(s8), pad]
    /// </summary>
    private static EMEVD.Instruction MakeIfConditionGroup()
    {
        var args = new byte[4];
        args[0] = 0;                   // MAIN
        args[1] = 1;                   // ON
        args[2] = unchecked((byte)-1); // OR_01
        return new EMEVD.Instruction(0, 0, args);
    }
}
