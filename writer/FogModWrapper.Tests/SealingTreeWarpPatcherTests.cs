using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class SealingTreeWarpPatcherTests
{
    // Romina's arena: primary = m61_44_45_00, alt (burned) = m61_44_45_10
    private const int ALT_REGION = 755890120;     // fictional alt region (m61_44_45_10 side)
    private const int PRIMARY_REGION = 755890110;  // fictional primary region (m61_44_45_00 side)
    private const string PRIMARY_MAP = "m61_44_45_00";

    private static readonly byte[] PrimaryMapBytes = ErdtreeWarpPatcher.ParseMapBytes(PRIMARY_MAP);
    private static readonly int PrimaryMapPacked = ErdtreeWarpPatcher.PackMapId(PRIMARY_MAP);

    private static List<(int altRegion, int primaryRegion, byte[] primaryMapBytes, int primaryMapPacked)>
        MakeTargets(params (int alt, int primary, string map)[] entries)
    {
        return entries.Select(e => (
            e.alt,
            e.primary,
            primaryMapBytes: ErdtreeWarpPatcher.ParseMapBytes(e.map),
            primaryMapPacked: ErdtreeWarpPatcher.PackMapId(e.map)
        )).ToList();
    }

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

    // --- WarpPlayer tests ---

    [Fact]
    public void PatchWarpPlayer_ReplacesAltRegionWithPrimary()
    {
        // Alt warp targeting m61_44_45_10 with ALT_REGION
        var warp = MakeWarpPlayer(61, 44, 45, 10, ALT_REGION);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(1040290400, warp));

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(1, result);
        // Region replaced with primary
        Assert.Equal(PRIMARY_REGION, BitConverter.ToInt32(warp.ArgData, 4));
        // Map bytes replaced with primary map (m61_44_45_00)
        Assert.Equal(61, warp.ArgData[0]);
        Assert.Equal(44, warp.ArgData[1]);
        Assert.Equal(45, warp.ArgData[2]);
        Assert.Equal(0, warp.ArgData[3]);  // sub2: 10 → 0
    }

    [Fact]
    public void PatchWarpPlayer_IgnoresUnrelatedRegions()
    {
        int unrelatedRegion = 755890099;
        var warp = MakeWarpPlayer(60, 41, 37, 0, unrelatedRegion);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290400, warp);
        emevd.Events.Add(evt);

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(0, result);
        Assert.Equal(unrelatedRegion, BitConverter.ToInt32(warp.ArgData, 4));
        Assert.Single(evt.Instructions); // no insertions
    }

    [Fact]
    public void PatchWarpPlayer_DoesNotInsertSetEventFlag()
    {
        // Unlike ErdtreeWarpPatcher, SealingTreeWarpPatcher should NOT insert any instructions
        var warp = MakeWarpPlayer(61, 44, 45, 10, ALT_REGION);
        var emevd = new EMEVD();
        var evt = MakeEvent(1040290400, warp);
        emevd.Events.Add(evt);

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        // Should still be exactly 1 instruction (no SetEventFlag inserted)
        Assert.Single(evt.Instructions);
        Assert.Equal(2003, evt.Instructions[0].Bank);
        Assert.Equal(14, evt.Instructions[0].ID);
    }

    [Fact]
    public void PatchEmevd_HandlesMultipleEntrances()
    {
        // Two different entrance pairs (front + back of Romina's arena)
        int altRegion2 = 755890130;
        int primaryRegion2 = 755890115;

        var warp1 = MakeWarpPlayer(61, 44, 45, 10, ALT_REGION);
        var warp2 = MakeWarpPlayer(61, 44, 45, 10, altRegion2);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(1040290400, warp1));
        emevd.Events.Add(MakeEvent(1040290401, warp2));

        var targets = MakeTargets(
            (ALT_REGION, PRIMARY_REGION, PRIMARY_MAP),
            (altRegion2, primaryRegion2, PRIMARY_MAP)
        );
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(2, result);
        Assert.Equal(PRIMARY_REGION, BitConverter.ToInt32(warp1.ArgData, 4));
        Assert.Equal(primaryRegion2, BitConverter.ToInt32(warp2.ArgData, 4));
    }

    [Fact]
    public void PatchEmevd_CutsceneWarp_ReplacesAltWithPrimary()
    {
        var warp = MakeCutsceneWarp(13000050, 1, ALT_REGION, 61444510);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, warp));

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(1, result);
        Assert.Equal(PRIMARY_REGION, BitConverter.ToInt32(warp.ArgData, 8));
        Assert.Equal(PrimaryMapPacked, BitConverter.ToInt32(warp.ArgData, 12));
    }

    [Fact]
    public void PatchEmevd_CutsceneWarpId12_ReplacesAltWithPrimary()
    {
        // PlayCutsceneToPlayerAndWarp can be bank 2002, id 12 (variant)
        var args = new byte[28];
        BitConverter.GetBytes(13000050).CopyTo(args, 0);
        BitConverter.GetBytes(1).CopyTo(args, 4);
        BitConverter.GetBytes(ALT_REGION).CopyTo(args, 8);
        BitConverter.GetBytes(61444510).CopyTo(args, 12);
        var warp = new EMEVD.Instruction(2002, 12, args);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, warp));

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(1, result);
        Assert.Equal(PRIMARY_REGION, BitConverter.ToInt32(warp.ArgData, 8));
        Assert.Equal(PrimaryMapPacked, BitConverter.ToInt32(warp.ArgData, 12));
    }

    [Fact]
    public void PatchEmevd_CutsceneWarp_IgnoresUnrelatedRegions()
    {
        int unrelatedRegion = 755890099;
        var warp = MakeCutsceneWarp(13000050, 1, unrelatedRegion, 60413700);
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, warp));

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(0, result);
        Assert.Equal(unrelatedRegion, BitConverter.ToInt32(warp.ArgData, 8));
    }

    [Fact]
    public void PatchEmevd_EmptyEmevd_ReturnsZero()
    {
        var emevd = new EMEVD();

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        var result = SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(0, result);
    }

    [Fact]
    public void PatchEmevd_NonWarpInstruction_Untouched()
    {
        // A SetEventFlag instruction should not be touched
        var setFlag = new EMEVD.Instruction(2003, 66, new byte[12]);
        BitConverter.GetBytes(330).CopyTo(setFlag.ArgData, 4);
        var originalArgs = (byte[])setFlag.ArgData.Clone();
        var emevd = new EMEVD();
        emevd.Events.Add(MakeEvent(900, setFlag));

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        Assert.Equal(originalArgs, setFlag.ArgData);
    }

    [Fact]
    public void PatchEmevd_ParameterIndicesUnchanged()
    {
        // Since we don't insert instructions, parameter indices should stay the same
        var warp = MakeWarpPlayer(61, 44, 45, 10, ALT_REGION);
        var evt = new EMEVD.Event(1040290400);
        evt.Instructions.Add(warp);
        evt.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4));
        var emevd = new EMEVD();
        emevd.Events.Add(evt);

        var targets = MakeTargets((ALT_REGION, PRIMARY_REGION, PRIMARY_MAP));
        SealingTreeWarpPatcher.PatchEmevd(emevd, targets);

        // Parameter index should remain 0 (no instruction insertion)
        Assert.Equal(0, evt.Parameters[0].InstructionIndex);
    }
}
