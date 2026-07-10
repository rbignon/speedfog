using SoulsFormats;

namespace FogModWrapper;

/// <summary>
/// Lengthens the "stable position" pulse in FogMod's compiled
/// common_makestable event (fogevents.txt ID 755850000).
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
/// the gated region and the next quit-out falls back to the last grace.
/// Lengthening the pulse to a few seconds keeps the FogRando semantics
/// (anchor at region entry, no saving deeper into the fight) while making
/// the capture reliable. See docs/quitout-respawn.md.
/// </summary>
public static class MakestablePulsePatcher
{
    /// <summary>FogRando's common_makestable event (fogevents.txt).</summary>
    public const int MAKESTABLE_EVENT_ID = 755850000;

    /// <summary>Pulse length compiled by FogMod from the template.</summary>
    public const int VANILLA_PULSE_FRAMES = 10;

    /// <summary>
    /// Patched pulse length: 150 frames is 5 s at the 30 fps event tick,
    /// comfortably longer than any post-warp fade-in.
    /// </summary>
    public const int PULSE_FRAMES = 150;

    /// <summary>
    /// Rewrite the WaitFixedTimeFrames(10) in event 755850000 to
    /// PULSE_FRAMES. Only the known vanilla duration is rewritten, so a
    /// changed FogRando template is left alone (and logged) rather than
    /// blindly patched.
    /// </summary>
    /// <param name="commonEmevd">In-memory common.emevd to modify</param>
    /// <returns>Number of instructions rewritten</returns>
    public static int Patch(EMEVD commonEmevd)
    {
        var evt = commonEmevd.Events.FirstOrDefault(e => e.ID == MAKESTABLE_EVENT_ID);
        if (evt == null)
        {
            Console.WriteLine("Makestable pulse fix: event 755850000 not found in common.emevd");
            return 0;
        }

        int patched = 0;
        foreach (var instr in evt.Instructions)
        {
            // WaitFixedTimeFrames = bank 1001, id 1, args: [frames(int32)]
            if (instr.Bank == 1001 && instr.ID == 1 && instr.ArgData.Length >= 4
                && BitConverter.ToInt32(instr.ArgData, 0) == VANILLA_PULSE_FRAMES)
            {
                BitConverter.GetBytes(PULSE_FRAMES).CopyTo(instr.ArgData, 0);
                patched++;
            }
        }

        Console.WriteLine(patched > 0
            ? $"Makestable pulse fix: extended pulse to {PULSE_FRAMES} frames ({patched} instruction(s))"
            : "Makestable pulse fix: no WaitFixedTimeFrames(10) found in event 755850000");
        return patched;
    }
}
