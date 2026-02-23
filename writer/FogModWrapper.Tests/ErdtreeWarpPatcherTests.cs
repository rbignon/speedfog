using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class ErdtreeWarpPatcherTests
{
    private const int PRIMARY_REGION = 755890068;
    private const int ALT_REGION = 755890086;
    private const string ALT_MAP = "m11_05_00_00";

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
    public void PatchEmevd_MultipleEvents_PatchesAll()
    {
        var warp1 = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var warp2 = MakeWarpPlayer(11, 0, 0, 0, PRIMARY_REGION);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(1040290310, warp1));
        emevd.Events.Add(MakeEvent(900, MakeSetEventFlag(300), warp2));

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(2, result);
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp1.ArgData, 4));
        Assert.Equal(ALT_REGION, BitConverter.ToInt32(warp2.ArgData, 4));
    }

    [Fact]
    public void PatchEmevd_NonMatchingRegion_DoesNotPatch()
    {
        int otherRegion = 755890099;
        var warp = MakeWarpPlayer(11, 0, 0, 0, otherRegion);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(1040290310, warp));

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(0, result);
        Assert.Equal(otherRegion, BitConverter.ToInt32(warp.ArgData, 4));
    }

    [Fact]
    public void PatchEmevd_NonWarpInstruction_Untouched()
    {
        var setFlag = MakeSetEventFlag(300);
        var originalArgs = (byte[])setFlag.ArgData.Clone();
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, setFlag));

        ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(originalArgs, setFlag.ArgData);
    }

    [Fact]
    public void PatchEmevd_EmptyEmevd_ReturnsZero()
    {
        var emevd = new EMEVD();

        var result = ErdtreeWarpPatcher.PatchEmevd(emevd, PRIMARY_REGION, ALT_REGION, AltMapBytes, AltMapPacked);

        Assert.Equal(0, result);
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
