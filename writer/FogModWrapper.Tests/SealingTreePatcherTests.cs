using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class SealingTreePatcherTests
{
    /// <summary>
    /// Helper to create a SetEventFlag instruction matching Event 915's format.
    /// Bank 2003, ID 66, args: [targetType(4)=0, flagId(4), state(1), padding(3)]
    /// </summary>
    private static EMEVD.Instruction MakeSetEventFlag(int flagId, bool on)
    {
        var args = new byte[12];
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        args[8] = (byte)(on ? 1 : 0);
        return new EMEVD.Instruction(2003, 66, args);
    }

    /// <summary>
    /// Helper to create a filler instruction (EndIfPlayerInWorldType).
    /// </summary>
    private static EMEVD.Instruction MakeFiller()
    {
        return new EMEVD.Instruction(1003, 14, new byte[] { 0, 1, 0, 0 });
    }

    [Fact]
    public void NopSetEventFlag_ReplacesFlag330On_WithWaitFixedTime()
    {
        var evt = new EMEVD.Event(915);
        evt.Instructions.Add(MakeFiller());                     // [0]
        evt.Instructions.Add(MakeSetEventFlag(330, true));      // [1] — target
        evt.Instructions.Add(MakeSetEventFlag(300, true));      // [2] — different flag, untouched
        evt.Instructions.Add(MakeSetEventFlag(330, false));     // [3] — OFF, untouched

        int count = SealingTreePatcher.NopSetEventFlag(evt, 330);

        Assert.Equal(1, count);

        // [1] should be replaced with WaitFixedTime(0) = bank 1001, id 0
        Assert.Equal(1001, evt.Instructions[1].Bank);
        Assert.Equal(0, evt.Instructions[1].ID);

        // [2] and [3] should be unchanged
        Assert.Equal(2003, evt.Instructions[2].Bank);
        Assert.Equal(66, evt.Instructions[2].ID);
        Assert.Equal(2003, evt.Instructions[3].Bank);
        Assert.Equal(66, evt.Instructions[3].ID);
    }

    [Fact]
    public void NopSetEventFlag_NoMatchingFlag_ReturnsZero()
    {
        var evt = new EMEVD.Event(915);
        evt.Instructions.Add(MakeSetEventFlag(300, true));
        evt.Instructions.Add(MakeSetEventFlag(9140, true));

        int count = SealingTreePatcher.NopSetEventFlag(evt, 330);

        Assert.Equal(0, count);
        // All instructions unchanged
        Assert.All(evt.Instructions, i =>
        {
            Assert.Equal(2003, i.Bank);
            Assert.Equal(66, i.ID);
        });
    }

    [Fact]
    public void NopSetEventFlag_MultipleMatches_ReplacesAll()
    {
        var evt = new EMEVD.Event(915);
        evt.Instructions.Add(MakeSetEventFlag(330, true));   // [0] — target
        evt.Instructions.Add(MakeFiller());                   // [1]
        evt.Instructions.Add(MakeSetEventFlag(330, true));   // [2] — target

        int count = SealingTreePatcher.NopSetEventFlag(evt, 330);

        Assert.Equal(2, count);
        Assert.Equal(1001, evt.Instructions[0].Bank);
        Assert.Equal(1001, evt.Instructions[2].Bank);
    }

    [Fact]
    public void NopSetEventFlag_PreservesInstructionCount()
    {
        var evt = new EMEVD.Event(915);
        evt.Instructions.Add(MakeFiller());
        evt.Instructions.Add(MakeSetEventFlag(330, true));
        evt.Instructions.Add(MakeFiller());

        int originalCount = evt.Instructions.Count;
        SealingTreePatcher.NopSetEventFlag(evt, 330);

        Assert.Equal(originalCount, evt.Instructions.Count);
    }

    [Fact]
    public void InsertClearFlag_InsertsSetEventFlagOffAtIndex0()
    {
        var evt = new EMEVD.Event(0);
        evt.Instructions.Add(MakeFiller());  // [0] — will become [1]

        SealingTreePatcher.InsertClearFlag(evt, 330);

        Assert.Equal(2, evt.Instructions.Count);
        // Inserted instruction at [0]: SetEventFlag(330, OFF)
        var inserted = evt.Instructions[0];
        Assert.Equal(2003, inserted.Bank);
        Assert.Equal(66, inserted.ID);
        Assert.Equal(330, BitConverter.ToInt32(inserted.ArgData, 4)); // flag ID
        Assert.Equal(0, inserted.ArgData[8]); // state = OFF
    }

    [Fact]
    public void InsertClearFlag_ShiftsParameterIndices()
    {
        var evt = new EMEVD.Event(0);
        evt.Instructions.Add(MakeFiller());  // [0]
        evt.Instructions.Add(MakeFiller());  // [1]

        // Add parameters referencing instructions [0] and [1]
        // EMEVD.Parameter(instrIndex, srcOffset, tgtOffset, len)
        evt.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        evt.Parameters.Add(new EMEVD.Parameter(1, 0, 0, 4));

        SealingTreePatcher.InsertClearFlag(evt, 330);

        // Both parameters should be shifted by +1
        Assert.Equal(1, evt.Parameters[0].InstructionIndex);
        Assert.Equal(2, evt.Parameters[1].InstructionIndex);
    }

    [Fact]
    public void InsertClearFlag_EmptyEvent_InsertsSuccessfully()
    {
        var evt = new EMEVD.Event(0);

        SealingTreePatcher.InsertClearFlag(evt, 330);

        Assert.Single(evt.Instructions);
        Assert.Equal(2003, evt.Instructions[0].Bank);
        Assert.Equal(66, evt.Instructions[0].ID);
    }
}
