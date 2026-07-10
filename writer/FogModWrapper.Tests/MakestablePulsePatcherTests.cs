using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class MakestablePulsePatcherTests
{
    /// <summary>
    /// WaitFixedTimeFrames instruction: bank 1001, id 1, args [frames(int32)].
    /// </summary>
    private static EMEVD.Instruction MakeWaitFrames(int frames)
    {
        return new EMEVD.Instruction(1001, 1, BitConverter.GetBytes(frames));
    }

    /// <summary>
    /// SetEventFlag instruction (bank 2003, id 66), as compiled from the
    /// common_makestable template. Arg values are irrelevant to the patcher.
    /// </summary>
    private static EMEVD.Instruction MakeSetEventFlag()
    {
        return new EMEVD.Instruction(2003, 66, new byte[12]);
    }

    /// <summary>
    /// Build an EMEVD containing a compiled common_makestable event
    /// (ID 755850000) with the vanilla 10-frame pulse.
    /// </summary>
    private static EMEVD MakeCommonWithMakestable(int pulseFrames = 10)
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(MakestablePulsePatcher.MAKESTABLE_EVENT_ID);
        evt.Instructions.Add(MakeSetEventFlag());          // SetEventFlag(temp, ON)
        evt.Instructions.Add(MakeFiller());                // EndIfEventFlag(...)
        evt.Instructions.Add(MakeWaitFrames(pulseFrames)); // WaitFixedTimeFrames(10)
        evt.Instructions.Add(MakeSetEventFlag());          // SetEventFlag(temp, OFF)
        emevd.Events.Add(evt);
        return emevd;
    }

    private static EMEVD.Instruction MakeFiller()
    {
        return new EMEVD.Instruction(1003, 14, new byte[] { 0, 1, 0, 0 });
    }

    [Fact]
    public void Patch_ExtendsVanillaPulse()
    {
        var emevd = MakeCommonWithMakestable();

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(1, patched);
        var wait = emevd.Events[0].Instructions[2];
        Assert.Equal(1001, wait.Bank);
        Assert.Equal(1, wait.ID);
        Assert.Equal(MakestablePulsePatcher.PULSE_FRAMES, BitConverter.ToInt32(wait.ArgData, 0));
    }

    [Fact]
    public void Patch_ReturnsZero_WhenMakestableEventAbsent()
    {
        var emevd = new EMEVD();
        var other = new EMEVD.Event(915);
        other.Instructions.Add(MakeWaitFrames(10));
        emevd.Events.Add(other);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        // The wait in the unrelated event is untouched.
        Assert.Equal(10, BitConverter.ToInt32(other.Instructions[0].ArgData, 0));
    }

    [Fact]
    public void Patch_IgnoresWaitsWithUnexpectedDuration()
    {
        // If FogRando ever changes the template's pulse length, refuse to
        // patch blindly: only the known vanilla 10-frame wait is rewritten.
        var emevd = MakeCommonWithMakestable(pulseFrames: 30);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Equal(30, BitConverter.ToInt32(emevd.Events[0].Instructions[2].ArgData, 0));
    }

    [Fact]
    public void Patch_OnlyTouchesFrameWaits()
    {
        // A WaitFixedTimeSeconds (bank 1001, id 0) with a bit pattern of 10
        // in the makestable event must not be rewritten.
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(MakestablePulsePatcher.MAKESTABLE_EVENT_ID);
        evt.Instructions.Add(new EMEVD.Instruction(1001, 0, BitConverter.GetBytes(10)));
        emevd.Events.Add(evt);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Equal(0, evt.Instructions[0].ID);
        Assert.Equal(10, BitConverter.ToInt32(evt.Instructions[0].ArgData, 0));
    }
}
