using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class ErdtreeWarpPatcherTests
{
    private const int PRIMARY_REGION = 755890068;
    private const int ALT_REGION = 755890086;
    private const string ALT_MAP = "m11_05_00_00";
    private const int ERDTREE_BURNING_FLAG = 300;

    private static readonly byte[] AltMapBytes = ErdtreeWarpPatcher.ParseMapBytes(ALT_MAP);
    private static readonly int AltMapPacked = ErdtreeWarpPatcher.PackMapId(ALT_MAP);

    private static EMEVD.Event MakeEvent(long id, params EMEVD.Instruction[] instructions)
    {
        var evt = new EMEVD.Event(id);
        evt.Instructions.AddRange(instructions);
        return evt;
    }

    /// <summary>
    /// WarpPlayer (bank 2003, id 14): [area(1), block(1), sub(1), sub2(1), region(4), padding(4)]
    /// </summary>
    private static EMEVD.Instruction MakeWarpPlayer(byte area, byte block, byte sub, byte sub2, int region)
    {
        var args = new byte[12];
        args[0] = area;
        args[1] = block;
        args[2] = sub;
        args[3] = sub2;
        BitConverter.GetBytes(region).CopyTo(args, 4);
        return new EMEVD.Instruction(2003, 14, args);
    }

    /// <summary>
    /// PlayCutsceneToPlayerAndWarp (bank 2002, id 11):
    /// [cutsceneId(4), playback(4), region(4), mapPacked(4), ...]
    /// </summary>
    private static EMEVD.Instruction MakeCutsceneWarp(int cutsceneId, int playback, int region, int mapPacked)
    {
        var args = new byte[28];
        BitConverter.GetBytes(cutsceneId).CopyTo(args, 0);
        BitConverter.GetBytes(playback).CopyTo(args, 4);
        BitConverter.GetBytes(region).CopyTo(args, 8);
        BitConverter.GetBytes(mapPacked).CopyTo(args, 12);
        return new EMEVD.Instruction(2002, 11, args);
    }

    private static EMEVD.Instruction MakeSetEventFlag(int flag)
    {
        var args = new byte[12];
        BitConverter.GetBytes(flag).CopyTo(args, 4);
        args[8] = 1;
        return new EMEVD.Instruction(2003, 66, args);
    }

    private static void AssertIsSetEventFlag(EMEVD.Instruction instr, int expectedFlag)
    {
        Assert.Equal(2003, instr.Bank);
        Assert.Equal(66, instr.ID);
        Assert.Equal(expectedFlag, BitConverter.ToInt32(instr.ArgData, 4));
        Assert.Equal(1, instr.ArgData[8]); // ON
    }

    // --- PatchEmevd tests ---

    [Fact]
    public void PatchEmevd_WarpPlayer_PatchesRegionAndMap()
    {
        var warp = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(1040290310, warp));

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(1, result);
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp.ArgData, 4));
        Assert.Equal(11, warp.ArgData[0]);
        Assert.Equal(5, warp.ArgData[1]);
        Assert.Equal(0, warp.ArgData[2]);
        Assert.Equal(0, warp.ArgData[3]);
    }

    [Fact]
    public void PatchEmevd_WarpPlayer_InsertsSetEventFlagBefore()
    {
        var warp = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290310, warp);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(2, evt.Instructions.Count);
        AssertIsSetEventFlag(evt.Instructions[0], ERDTREE_BURNING_FLAG);
        // warp is now at index 1
        Assert.Equal(2003, evt.Instructions[1].Bank);
        Assert.Equal(14, evt.Instructions[1].ID);
    }

    [Fact]
    public void PatchEmevd_CutsceneWarp_PatchesRegionAndMap()
    {
        var warp = MakeCutsceneWarp(13000050, 1, PRIMARY_REGION, 11000000);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, warp));

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(1, result);
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp.ArgData, 8));
        Assert.Equal(11050000, BitConverter.ToInt32(warp.ArgData, 12));
    }

    [Fact]
    public void PatchEmevd_CutsceneWarp_InsertsSetEventFlagBefore()
    {
        var warp = MakeCutsceneWarp(13000050, 1, PRIMARY_REGION, 11000000);
        var emevd = new EMEVD();
        var evt = MakeEvent(900, warp);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(2, evt.Instructions.Count);
        AssertIsSetEventFlag(evt.Instructions[0], ERDTREE_BURNING_FLAG);
        Assert.Equal(2002, evt.Instructions[1].Bank);
        Assert.Equal(11, evt.Instructions[1].ID);
    }

    [Fact]
    public void PatchEmevd_MultipleEvents_PatchesAll()
    {
        var warp1 = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var warp2 = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        var evt1 = MakeEvent(1040290310, warp1);
        var evt2 = MakeEvent(900, MakeSetEventFlag(999), warp2);
        emevd.Events.Add(evt1);
        emevd.Events.Add(evt2);

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(2, result);
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp1.ArgData, 4));
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp2.ArgData, 4));
        // evt1: [SetEventFlag(300), WarpPlayer]
        Assert.Equal(2, evt1.Instructions.Count);
        AssertIsSetEventFlag(evt1.Instructions[0], ERDTREE_BURNING_FLAG);
        // evt2: [SetEventFlag(999), SetEventFlag(300), WarpPlayer]
        Assert.Equal(3, evt2.Instructions.Count);
        Assert.Equal(2003, evt2.Instructions[0].Bank); // original SetEventFlag(999)
        Assert.Equal(66, evt2.Instructions[0].ID);
        AssertIsSetEventFlag(evt2.Instructions[1], ERDTREE_BURNING_FLAG);
    }

    [Fact]
    public void PatchEmevd_NonMatchingRegion_DoesNotPatch()
    {
        int otherRegion = 755890099;
        var warp = MakeWarpPlayer(11, 0, 0, 0, otherRegion);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290310, warp);
        emevd.Events.Add(evt);

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(0, result);
        Assert.Equal(otherRegion, BitConverter.ToInt32(warp.ArgData, 4));
        Assert.Single(evt.Instructions); // no insertion
    }

    [Fact]
    public void PatchEmevd_NonWarpInstruction_Untouched()
    {
        var setFlag = MakeSetEventFlag(300);
        var originalArgs = (byte[])setFlag.ArgData.Clone();
        var emevd = new EMEVD();
        var evt = MakeEvent(900, setFlag);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(originalArgs, setFlag.ArgData);
        Assert.Single(evt.Instructions); // no insertion
    }

    [Fact]
    public void PatchEmevd_EmptyEmevd_ReturnsZero()
    {
        var emevd = new EMEVD();

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(0, result);
    }

    [Fact]
    public void PatchEmevd_WarpWithPrecedingInstructions_InsertsAtCorrectPosition()
    {
        // Event with: [IfCondition, SetFlag(999), WarpPlayer]
        var ifInstr = new EMEVD.Instruction(3, 0, new byte[8]); // IfEventFlag
        var setFlag = MakeSetEventFlag(999);
        var warp = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290310, ifInstr, setFlag, warp);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        // Should become: [IfCondition, SetFlag(999), SetEventFlag(300), WarpPlayer]
        Assert.Equal(4, evt.Instructions.Count);
        Assert.Equal(3, evt.Instructions[0].Bank); // IfEventFlag
        Assert.Equal(2003, evt.Instructions[1].Bank); // SetFlag(999)
        Assert.Equal(66, evt.Instructions[1].ID);
        AssertIsSetEventFlag(evt.Instructions[2], ERDTREE_BURNING_FLAG); // injected
        Assert.Equal(2003, evt.Instructions[3].Bank); // WarpPlayer
        Assert.Equal(14, evt.Instructions[3].ID);
    }

    [Fact]
    public void PatchEmevd_ShiftsParameterIndices()
    {
        // Event with a Parameter referencing instruction 0 (the warp)
        var warp = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var evt = new EMEVD.Event(1040290310);
        evt.Instructions.Add(warp);
        // Add a Parameter that points to instruction index 0
        evt.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4));
        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        // SetEventFlag inserted at index 0, warp pushed to index 1
        Assert.Equal(2, evt.Instructions.Count);
        // Parameter should now point to instruction index 1 (shifted by 1)
        Assert.Equal(1, evt.Parameters[0].InstructionIndex);
    }

    // --- NopSkipIfEventFlag tests ---

    /// <summary>
    /// SkipIfEventFlag (bank 1003, id 1): [Skip(byte@0), State(byte@1), FlagType(byte@2), pad(1), FlagID(uint32@4)]
    /// </summary>
    private static EMEVD.Instruction MakeSkipIfEventFlag(byte skip, byte state, int flagId)
    {
        var args = new byte[8];
        args[0] = skip;
        args[1] = state;
        args[2] = 0; // FlagType = EventFlag
        BitConverter.GetBytes(flagId).CopyTo(args, 4);
        return new EMEVD.Instruction(1003, 1, args);
    }

    private static void AssertIsWaitFixedTime(EMEVD.Instruction instr)
    {
        Assert.Equal(1001, instr.Bank);
        Assert.Equal(0, instr.ID);
    }

    [Fact]
    public void PatchEmevd_NopsSkipIfEventFlag300InPatchedEvent()
    {
        // Simulates compiled fogwarp with alt-warp branch:
        // [0] SkipIfEventFlag(skip=1, ON, flag=300)
        // [1] WarpPlayer(m11_00, primaryRegion)  -- primary branch
        // [2] WarpPlayer(m11_05, altRegion)       -- alt branch (already m11_05)
        var skip = MakeSkipIfEventFlag(1, 1, 300);
        var warpPrimary = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var warpAlt = MakeWarpPlayer(11, 5, 0, 0, ALT_REGION);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290310, skip, warpPrimary, warpAlt);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        // SkipIfEventFlag should be NOP'd to WaitFixedTime(0)
        AssertIsWaitFixedTime(evt.Instructions[0]);
    }

    [Fact]
    public void PatchEmevd_DoesNotNopSkipIfEventFlagInUnpatchedEvent()
    {
        // Event with SkipIfEventFlag(300) but no matching WarpPlayer
        var skip = MakeSkipIfEventFlag(1, 1, 300);
        var otherWarp = MakeWarpPlayer(31, 6, 0, 0, 755890099); // non-matching region
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290311, skip, otherWarp);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        // SkipIfEventFlag should be untouched (event had no matching warps)
        Assert.Equal(1003, evt.Instructions[0].Bank);
        Assert.Equal(1, evt.Instructions[0].ID);
    }

    [Fact]
    public void PatchEmevd_DoesNotNopSkipIfEventFlagForOtherFlags()
    {
        // SkipIfEventFlag(330) in an event with matching WarpPlayer
        var skip = MakeSkipIfEventFlag(1, 1, 330); // flag 330, not 300
        var warp = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290310, skip, warp);
        emevd.Events.Add(evt);

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        // SkipIfEventFlag(330) should be untouched
        Assert.Equal(1003, evt.Instructions[0].Bank);
        Assert.Equal(1, evt.Instructions[0].ID);
        Assert.Equal(330, (int)BitConverter.ToUInt32(evt.Instructions[0].ArgData, 4));
    }

    [Fact]
    public void NopSkipIfEventFlag_ReplacesFlag300On_WithWaitFixedTime()
    {
        var evt = new EMEVD.Event(100);
        evt.Instructions.Add(MakeSkipIfEventFlag(2, 1, 300));  // [0] ON, target
        evt.Instructions.Add(MakeSkipIfEventFlag(1, 0, 300));  // [1] OFF, untouched
        evt.Instructions.Add(MakeSkipIfEventFlag(1, 1, 330));  // [2] different flag, untouched

        int count = ErdtreeWarpPatcher.NopSkipIfEventFlag(evt, 300);

        Assert.Equal(1, count);
        AssertIsWaitFixedTime(evt.Instructions[0]);
        Assert.Equal(1003, evt.Instructions[1].Bank); // OFF untouched
        Assert.Equal(1003, evt.Instructions[2].Bank); // flag 330 untouched
    }

    [Fact]
    public void NopSkipIfEventFlag_PreservesInstructionCount()
    {
        var evt = new EMEVD.Event(100);
        evt.Instructions.Add(MakeSkipIfEventFlag(2, 1, 300));
        evt.Instructions.Add(MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION));

        int originalCount = evt.Instructions.Count;
        ErdtreeWarpPatcher.NopSkipIfEventFlag(evt, 300);

        Assert.Equal(originalCount, evt.Instructions.Count);
    }

    // --- MakeSetEventFlag tests ---

    [Fact]
    public void MakeSetEventFlag_ProducesCorrectInstruction()
    {
        var instr = ErdtreeWarpPatcher.MakeSetEventFlag(300);

        Assert.Equal(2003, instr.Bank);
        Assert.Equal(66, instr.ID);
        Assert.Equal(12, instr.ArgData.Length);
        Assert.Equal(0, BitConverter.ToInt32(instr.ArgData, 0)); // targetType = 0
        Assert.Equal(300, BitConverter.ToInt32(instr.ArgData, 4)); // flagId
        Assert.Equal(1, instr.ArgData[8]); // state = ON
    }

    // --- Helper tests ---

    [Theory]
    [InlineData("m11_05_00_00", new byte[] { 11, 5, 0, 0 })]
    [InlineData("m11_00_00_00", new byte[] { 11, 0, 0, 0 })]
    [InlineData("m13_00_00_00", new byte[] { 13, 0, 0, 0 })]
    public void ParseMapBytes_ValidMap_ReturnsCorrectBytes(string map, byte[] expected)
    {
        Assert.Equal(expected, ErdtreeWarpPatcher.ParseMapBytes(map));
    }

    [Theory]
    [InlineData("m11_05_00_00", 11050000)]
    [InlineData("m11_00_00_00", 11000000)]
    [InlineData("m13_00_00_00", 13000000)]
    public void PackMapId_ValidMap_ReturnsCorrectId(string map, int expected)
    {
        Assert.Equal(expected, ErdtreeWarpPatcher.PackMapId(map));
    }
}
