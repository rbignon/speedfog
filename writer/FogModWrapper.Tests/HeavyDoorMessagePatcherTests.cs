using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class HeavyDoorMessagePatcherTests
{
    private const int BANK_DISPLAY = 2007;
    private const int ID_GENERIC_DIALOG = 1;
    private const int ID_BLINKING_MESSAGE = 4;
    private const int TEXT_ID_HEAVY_DOOR = 4200;

    /// <summary>
    /// Create a DisplayGenericDialog instruction.
    /// Bank 2007, ID 1, args: [MessageID(4), DialogType(4), NumOptions(4), EntityID(4), Distance(4)]
    /// </summary>
    private static EMEVD.Instruction MakeDisplayGenericDialog(int textId, int entityId = 0)
    {
        var args = new byte[20];
        BitConverter.GetBytes(textId).CopyTo(args, 0);
        BitConverter.GetBytes(entityId).CopyTo(args, 12);
        return new EMEVD.Instruction(BANK_DISPLAY, ID_GENERIC_DIALOG, args);
    }

    /// <summary>
    /// Create a DisplayBlinkingMessage instruction.
    /// Bank 2007, ID 4, args: [MessageID(4)]
    /// </summary>
    private static EMEVD.Instruction MakeDisplayBlinkingMessage(int textId)
    {
        var args = new byte[4];
        BitConverter.GetBytes(textId).CopyTo(args, 0);
        return new EMEVD.Instruction(BANK_DISPLAY, ID_BLINKING_MESSAGE, args);
    }

    private static EMEVD.Instruction MakeFiller()
    {
        return new EMEVD.Instruction(1003, 14, new byte[] { 0, 1, 0, 0 });
    }

    private static void AssertIsNop(EMEVD.Instruction instr)
    {
        Assert.Equal(1001, instr.Bank);
        Assert.Equal(0, instr.ID);
    }

    private static void AssertIsDisplay(EMEVD.Instruction instr)
    {
        Assert.Equal(BANK_DISPLAY, instr.Bank);
    }

    [Fact]
    public void Patch_NopsDisplayGenericDialog4200()
    {
        var evt = new EMEVD.Event(90005650);
        evt.Instructions.Add(MakeFiller());                              // [0]
        evt.Instructions.Add(MakeDisplayGenericDialog(TEXT_ID_HEAVY_DOOR)); // [1]
        evt.Instructions.Add(MakeFiller());                              // [2]

        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(1, count);
        AssertIsNop(evt.Instructions[1]);
        // Other instructions unchanged
        Assert.Equal(1003, evt.Instructions[0].Bank);
        Assert.Equal(1003, evt.Instructions[2].Bank);
    }

    [Fact]
    public void Patch_NopsDisplayBlinkingMessage4200()
    {
        var evt = new EMEVD.Event(90005650);
        evt.Instructions.Add(MakeDisplayBlinkingMessage(TEXT_ID_HEAVY_DOOR)); // [0]

        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(1, count);
        AssertIsNop(evt.Instructions[0]);
    }

    [Fact]
    public void Patch_IgnoresDifferentTextId()
    {
        var evt = new EMEVD.Event(90005650);
        evt.Instructions.Add(MakeDisplayGenericDialog(9999));    // different text ID
        evt.Instructions.Add(MakeDisplayBlinkingMessage(1234));  // different text ID

        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(0, count);
        AssertIsDisplay(evt.Instructions[0]);
        AssertIsDisplay(evt.Instructions[1]);
    }

    [Fact]
    public void Patch_IgnoresDifferentBank()
    {
        // An instruction with text ID 4200 but in a different bank
        var args = new byte[4];
        BitConverter.GetBytes(TEXT_ID_HEAVY_DOOR).CopyTo(args, 0);
        var instr = new EMEVD.Instruction(2003, 1, args); // bank 2003, not 2007

        var evt = new EMEVD.Event(1);
        evt.Instructions.Add(instr);

        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(0, count);
        Assert.Equal(2003, evt.Instructions[0].Bank);
    }

    [Fact]
    public void Patch_RemovesOrphanedParameterEntries()
    {
        var evt = new EMEVD.Event(90005650);
        evt.Instructions.Add(MakeFiller());                                       // [0]
        evt.Instructions.Add(MakeDisplayGenericDialog(TEXT_ID_HEAVY_DOOR, 0));    // [1] - will be NOP'd
        evt.Instructions.Add(MakeFiller());                                       // [2]

        // Parameter targeting instruction [1] (e.g. parameterized EntityID at offset 12)
        evt.Parameters.Add(new EMEVD.Parameter(1, 12, 0, 4));
        // Parameter targeting instruction [0] (should be preserved)
        evt.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        // Parameter targeting instruction [2] (should be preserved)
        evt.Parameters.Add(new EMEVD.Parameter(2, 0, 0, 4));

        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        HeavyDoorMessagePatcher.Patch(emevd);

        // Parameter for instruction [1] removed, others preserved
        Assert.Equal(2, evt.Parameters.Count);
        Assert.DoesNotContain(evt.Parameters, p => p.InstructionIndex == 1);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 0);
        Assert.Contains(evt.Parameters, p => p.InstructionIndex == 2);
    }

    [Fact]
    public void Patch_NopsMultipleInstructionsAcrossEvents()
    {
        // Event with both DisplayGenericDialog and DisplayBlinkingMessage (like 90005650)
        var evt1 = new EMEVD.Event(90005650);
        evt1.Instructions.Add(MakeDisplayGenericDialog(TEXT_ID_HEAVY_DOOR));   // [0]
        evt1.Instructions.Add(MakeDisplayBlinkingMessage(TEXT_ID_HEAVY_DOOR)); // [1]

        // Second event with just DisplayGenericDialog (like 90005652)
        var evt2 = new EMEVD.Event(90005652);
        evt2.Instructions.Add(MakeDisplayGenericDialog(TEXT_ID_HEAVY_DOOR));   // [0]

        var emevd = new EMEVD();
        emevd.Events.Add(evt1);
        emevd.Events.Add(evt2);

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(3, count);
        AssertIsNop(evt1.Instructions[0]);
        AssertIsNop(evt1.Instructions[1]);
        AssertIsNop(evt2.Instructions[0]);
    }

    [Fact]
    public void Patch_ReturnsZeroForEmptyEmevd()
    {
        var emevd = new EMEVD();

        int count = HeavyDoorMessagePatcher.Patch(emevd);

        Assert.Equal(0, count);
    }
}
