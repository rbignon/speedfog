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

    /// <summary>EndIfEventFlag instruction (bank 1003, id 2).</summary>
    private static EMEVD.Instruction MakeEndIfEventFlag()
    {
        return new EMEVD.Instruction(1003, 2, new byte[8]);
    }

    /// <summary>IfEventFlag instruction (bank 3, id 0).</summary>
    private static EMEVD.Instruction MakeIfEventFlag()
    {
        return new EMEVD.Instruction(3, 0, new byte[8]);
    }

    /// <summary>EndUnconditionally(Restart) instruction (bank 1000, id 4).</summary>
    private static EMEVD.Instruction MakeEndRestart()
    {
        return new EMEVD.Instruction(1000, 4, new byte[] { 1, 0, 0, 0 });
    }

    /// <summary>
    /// Build an EMEVD containing a compiled common_makestable event
    /// (ID 755850000) mirroring FogMod's output: the 6 template instructions
    /// plus the 4 Parameter entries substituting X0_4 (temp flag) and X4_4
    /// (boss defeat flag) at byte 4 of their instructions.
    /// </summary>
    private static EMEVD MakeCommonWithMakestable(int pulseFrames = 10)
    {
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(MakestablePulsePatcher.MAKESTABLE_EVENT_ID);
        evt.Instructions.Add(MakeSetEventFlag());          // 0: SetEventFlag(X0_4, ON)
        evt.Instructions.Add(MakeEndIfEventFlag());        // 1: EndIfEventFlag(End, ON, X4_4)
        evt.Instructions.Add(MakeWaitFrames(pulseFrames)); // 2: WaitFixedTimeFrames(10)
        evt.Instructions.Add(MakeSetEventFlag());          // 3: SetEventFlag(X0_4, OFF)
        evt.Instructions.Add(MakeIfEventFlag());           // 4: IfEventFlag(MAIN, ON, X4_4)
        evt.Instructions.Add(MakeEndRestart());            // 5: EndUnconditionally(Restart)
        evt.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4)); // X0_4 -> instr 0
        evt.Parameters.Add(new EMEVD.Parameter(1, 4, 4, 4)); // X4_4 -> instr 1
        evt.Parameters.Add(new EMEVD.Parameter(3, 4, 0, 4)); // X0_4 -> instr 3
        evt.Parameters.Add(new EMEVD.Parameter(4, 4, 4, 4)); // X4_4 -> instr 4
        emevd.Events.Add(evt);
        return emevd;
    }

    [Fact]
    public void Patch_InsertsLoadEndGateBeforePulse()
    {
        var emevd = MakeCommonWithMakestable();

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(1, patched);
        var instrs = emevd.Events[0].Instructions;
        Assert.Equal(9, instrs.Count);

        // 2: IfEventFlag(OR_01, OFF, EventFlag, 2200) — bank 3, id 0,
        //    args [group(s8), state(u8), flagType(u8), pad, flagId(u32)]
        Assert.Equal(3, instrs[2].Bank);
        Assert.Equal(0, instrs[2].ID);
        Assert.Equal(8, instrs[2].ArgData.Length);
        Assert.Equal(0xFF, instrs[2].ArgData[0]);              // OR_01 = -1
        Assert.Equal(0, instrs[2].ArgData[1]);                 // OFF
        Assert.Equal(0, instrs[2].ArgData[2]);                 // EventFlag
        Assert.Equal(2200u, BitConverter.ToUInt32(instrs[2].ArgData, 4));

        // 3: IfElapsedSeconds(OR_01, 5) — bank 1, id 0,
        //    args [group(s8), pad x3, seconds(f32)]
        Assert.Equal(1, instrs[3].Bank);
        Assert.Equal(0, instrs[3].ID);
        Assert.Equal(8, instrs[3].ArgData.Length);
        Assert.Equal(0xFF, instrs[3].ArgData[0]);              // OR_01 = -1
        Assert.Equal(5f, BitConverter.ToSingle(instrs[3].ArgData, 4));

        // 4: IfConditionGroup(MAIN, ON, OR_01) — bank 0, id 0,
        //    args [result(s8), state(u8), target(s8), pad]
        Assert.Equal(0, instrs[4].Bank);
        Assert.Equal(0, instrs[4].ID);
        Assert.Equal(4, instrs[4].ArgData.Length);
        Assert.Equal(0, instrs[4].ArgData[0]);                 // MAIN
        Assert.Equal(1, instrs[4].ArgData[1]);                 // ON
        Assert.Equal(0xFF, instrs[4].ArgData[2]);              // OR_01 = -1

        // 5: the pulse keeps FogRando's original 10 frames.
        Assert.Equal(1001, instrs[5].Bank);
        Assert.Equal(1, instrs[5].ID);
        Assert.Equal(10, BitConverter.ToInt32(instrs[5].ArgData, 0));

        // The surrounding template instructions are untouched.
        Assert.Equal((2003, 66), (instrs[0].Bank, instrs[0].ID)); // SetEventFlag ON
        Assert.Equal((1003, 2), (instrs[1].Bank, instrs[1].ID));  // EndIfEventFlag
        Assert.Equal((2003, 66), (instrs[6].Bank, instrs[6].ID)); // SetEventFlag OFF
        Assert.Equal((3, 0), (instrs[7].Bank, instrs[7].ID));     // IfEventFlag X4_4
        Assert.Equal((1000, 4), (instrs[8].Bank, instrs[8].ID));  // End(Restart)
    }

    [Fact]
    public void Patch_ShiftsParametersAfterInsertionPoint()
    {
        var emevd = MakeCommonWithMakestable();

        MakestablePulsePatcher.Patch(emevd);

        var parameters = emevd.Events[0].Parameters;
        Assert.Equal(4, parameters.Count);
        // Instructions before the insertion point keep their indices.
        Assert.Equal(0, parameters[0].InstructionIndex);
        Assert.Equal(1, parameters[1].InstructionIndex);
        // Instructions after the insertion point shift by the 3 inserted ones.
        Assert.Equal(6, parameters[2].InstructionIndex);
        Assert.Equal(7, parameters[3].InstructionIndex);
        // Byte-level fields are untouched.
        Assert.All(parameters, p =>
        {
            Assert.Equal(4, p.TargetStartByte);
            Assert.Equal(4, p.ByteCount);
        });
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
        // The unrelated event is untouched.
        Assert.Single(other.Instructions);
        Assert.Equal(10, BitConverter.ToInt32(other.Instructions[0].ArgData, 0));
    }

    [Fact]
    public void Patch_IgnoresWaitsWithUnexpectedDuration()
    {
        // If FogRando ever changes the template's pulse length, refuse to
        // patch blindly: only the known vanilla 10-frame wait is gated.
        var emevd = MakeCommonWithMakestable(pulseFrames: 30);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Equal(6, emevd.Events[0].Instructions.Count);
        Assert.Equal(30, BitConverter.ToInt32(emevd.Events[0].Instructions[2].ArgData, 0));
    }

    [Fact]
    public void Patch_OnlyTouchesFrameWaits()
    {
        // A WaitFixedTimeSeconds (bank 1001, id 0) with a bit pattern of 10
        // in the makestable event must not be treated as the pulse.
        var emevd = new EMEVD();
        var evt = new EMEVD.Event(MakestablePulsePatcher.MAKESTABLE_EVENT_ID);
        evt.Instructions.Add(new EMEVD.Instruction(1001, 0, BitConverter.GetBytes(10)));
        emevd.Events.Add(evt);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Single(evt.Instructions);
        Assert.Equal(0, evt.Instructions[0].ID);
        Assert.Equal(10, BitConverter.ToInt32(evt.Instructions[0].ArgData, 0));
    }

    [Fact]
    public void Patch_IsIdempotent()
    {
        // Running twice must not stack a second gate: the wait is still
        // 10 frames after the first pass, but the 2200 gate is already there.
        var emevd = MakeCommonWithMakestable();
        MakestablePulsePatcher.Patch(emevd);

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Equal(9, emevd.Events[0].Instructions.Count);
    }

    [Fact]
    public void Patch_RefusesWhenMultipleVanillaWaits()
    {
        // Two 10-frame waits mean the template changed shape: leave it alone
        // rather than guessing which one is the pulse.
        var emevd = MakeCommonWithMakestable();
        emevd.Events[0].Instructions.Add(MakeWaitFrames(10));

        int patched = MakestablePulsePatcher.Patch(emevd);

        Assert.Equal(0, patched);
        Assert.Equal(7, emevd.Events[0].Instructions.Count);
    }
}
