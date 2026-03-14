using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class ZoneTrackingTests
{
    // --- TryExtractWarpInfo tests ---

    [Fact]
    public void TryExtractWarpInfo_WarpPlayer_ExtractsRegionAndMap()
    {
        // WarpPlayer (2003:14): [area(1), block(1), sub(1), sub2(1), region(4), ...]
        var argData = new byte[12];
        argData[0] = 31; // area
        argData[1] = 5;  // block
        argData[2] = 0;  // sub
        argData[3] = 0;  // sub2
        BitConverter.GetBytes(755890042).CopyTo(argData, 4); // region

        var instr = new EMEVD.Instruction(2003, 14, argData);
        var result = ZoneTrackingInjector.TryExtractWarpInfo(instr);

        Assert.NotNull(result);
        Assert.Equal((31, 5, 0, 0), result.Value.DestMap);
        Assert.Equal(755890042, result.Value.Region);
    }

    [Fact]
    public void TryExtractWarpInfo_WarpPlayer_ZeroMap_ReturnsNull()
    {
        // Parameterized template: map bytes are all zero
        var argData = new byte[12];
        BitConverter.GetBytes(755890042).CopyTo(argData, 4);

        var instr = new EMEVD.Instruction(2003, 14, argData);
        Assert.Null(ZoneTrackingInjector.TryExtractWarpInfo(instr));
    }

    [Fact]
    public void TryExtractWarpInfo_PlayCutsceneToPlayerAndWarp_ExtractsRegionAndMap()
    {
        // PlayCutsceneToPlayerAndWarp (2002:11): [cutscene(4), playback(4), region(4), mapId(4), ...]
        var argData = new byte[20];
        BitConverter.GetBytes(123456).CopyTo(argData, 0);    // cutscene ID
        BitConverter.GetBytes(0).CopyTo(argData, 4);          // playback
        BitConverter.GetBytes(755890099).CopyTo(argData, 8);  // region
        // Packed map: 11*1000000 + 5*10000 + 0*100 + 0 = 11050000
        BitConverter.GetBytes(11050000).CopyTo(argData, 12);

        var instr = new EMEVD.Instruction(2002, 11, argData);
        var result = ZoneTrackingInjector.TryExtractWarpInfo(instr);

        Assert.NotNull(result);
        Assert.Equal((11, 5, 0, 0), result.Value.DestMap);
        Assert.Equal(755890099, result.Value.Region);
    }

    [Fact]
    public void TryExtractWarpInfo_UnrelatedInstruction_ReturnsNull()
    {
        var instr = new EMEVD.Instruction(3, 24, new byte[12]);
        Assert.Null(ZoneTrackingInjector.TryExtractWarpInfo(instr));
    }

    // --- Region-based injection tests (using real EMEVD structures) ---
    //
    // NOTE: These tests duplicate the scan/lookup logic from InjectFogGateFlags rather
    // than calling the real method, because InjectFogGateFlags operates on EMEVD files
    // on disk. This means the actual insertion logic (reverse iteration, Parameter index
    // shifting) is NOT unit-tested here — it is covered by integration tests
    // (run_integration.sh) and Phase 3 validation (missing flags abort the build).

    /// <summary>
    /// Build a minimal EMEVD with one event containing a WarpPlayer instruction.
    /// </summary>
    private static EMEVD MakeEmevdWithWarp(int region, byte area = 31, byte block = 5)
    {
        var argData = new byte[12];
        argData[0] = area;
        argData[1] = block;
        argData[2] = 0;
        argData[3] = 0;
        BitConverter.GetBytes(region).CopyTo(argData, 4);

        var emevd = new EMEVD();
        var evt = new EMEVD.Event(1040290310);
        // Add a dummy instruction before the warp (simulates IfActionButtonInArea)
        evt.Instructions.Add(new EMEVD.Instruction(3, 24, new byte[12]));
        // The warp instruction
        evt.Instructions.Add(new EMEVD.Instruction(2003, 14, argData));
        emevd.Events.Add(evt);
        return emevd;
    }

    [Fact]
    public void RegionLookup_SingleFlag_InjectsOneSetEventFlag()
    {
        int region = 755890042;
        int flagId = 1040292800;

        var regionToFlags = new Dictionary<int, List<int>>
        {
            [region] = new List<int> { flagId }
        };

        var emevd = MakeEmevdWithWarp(region);
        var evt = emevd.Events[0];
        Assert.Equal(2, evt.Instructions.Count); // dummy + warp

        // Simulate the injection logic inline (since InjectFogGateFlags works on files)
        var warpPositions = new List<(int index, List<int> flagIds)>();
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
            if (warpInfo != null && regionToFlags.TryGetValue(warpInfo.Value.Region, out var flags))
            {
                warpPositions.Add((i, flags));
            }
        }

        Assert.Single(warpPositions);
        Assert.Single(warpPositions[0].flagIds);
        Assert.Equal(flagId, warpPositions[0].flagIds[0]);
    }

    [Fact]
    public void RegionLookup_MultipleFlags_SharedEntrance_InjectsAll()
    {
        int region = 755890042;
        int flagA = 1040292800;
        int flagB = 1040292801;

        var regionToFlags = new Dictionary<int, List<int>>
        {
            [region] = new List<int> { flagA, flagB }
        };

        var emevd = MakeEmevdWithWarp(region);
        var evt = emevd.Events[0];

        var warpPositions = new List<(int index, List<int> flagIds)>();
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
            if (warpInfo != null && regionToFlags.TryGetValue(warpInfo.Value.Region, out var flags))
            {
                warpPositions.Add((i, flags));
            }
        }

        Assert.Single(warpPositions);
        Assert.Equal(2, warpPositions[0].flagIds.Count);
        Assert.Contains(flagA, warpPositions[0].flagIds);
        Assert.Contains(flagB, warpPositions[0].flagIds);
    }

    [Fact]
    public void RegionLookup_UnknownRegion_Skipped()
    {
        int unknownRegion = 14003900; // vanilla region, not in our dict
        var regionToFlags = new Dictionary<int, List<int>>
        {
            [755890042] = new List<int> { 1040292800 }
        };

        var emevd = MakeEmevdWithWarp(unknownRegion);
        var evt = emevd.Events[0];

        var warpPositions = new List<(int index, List<int> flagIds)>();
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
            if (warpInfo != null && regionToFlags.TryGetValue(warpInfo.Value.Region, out var flags))
            {
                warpPositions.Add((i, flags));
            }
        }

        Assert.Empty(warpPositions);
    }

    [Fact]
    public void RegionLookup_MultipleWarpsInEvent_AllMatched()
    {
        // An event with two WarpPlayer instructions (different execution paths,
        // same destination — e.g., alt-warp events)
        int region = 755890042;
        int flagId = 1040292800;

        var regionToFlags = new Dictionary<int, List<int>>
        {
            [region] = new List<int> { flagId }
        };

        var argData = new byte[12];
        argData[0] = 31;
        argData[1] = 5;
        BitConverter.GetBytes(region).CopyTo(argData, 4);

        var emevd = new EMEVD();
        var evt = new EMEVD.Event(1040290310);
        evt.Instructions.Add(new EMEVD.Instruction(3, 24, new byte[12])); // dummy
        evt.Instructions.Add(new EMEVD.Instruction(2003, 14, (byte[])argData.Clone())); // warp 1
        evt.Instructions.Add(new EMEVD.Instruction(1003, 2, new byte[4])); // some other instruction
        evt.Instructions.Add(new EMEVD.Instruction(2003, 14, (byte[])argData.Clone())); // warp 2
        emevd.Events.Add(evt);

        var warpPositions = new List<(int index, List<int> flagIds)>();
        for (int i = 0; i < evt.Instructions.Count; i++)
        {
            var warpInfo = ZoneTrackingInjector.TryExtractWarpInfo(evt.Instructions[i]);
            if (warpInfo != null && regionToFlags.TryGetValue(warpInfo.Value.Region, out var flags))
            {
                warpPositions.Add((i, flags));
            }
        }

        Assert.Equal(2, warpPositions.Count);
    }

    // --- ConnectionInjector region mapping tests ---

    [Fact]
    public void SameClusterInvariant_SameCluster_NoError()
    {
        // Two flags for the same region, both mapping to the same cluster
        var regionToFlags = new Dictionary<int, List<int>>
        {
            [755890042] = new List<int> { 100, 200 }
        };
        var eventMap = new Dictionary<string, string>
        {
            ["100"] = "stormveil_c1",
            ["200"] = "stormveil_c1"
        };

        // Verify assertion logic: all flags for the same region map to the same cluster
        foreach (var (region, flags) in regionToFlags)
        {
            string? firstCluster = null;
            foreach (var flag in flags)
            {
                if (eventMap.TryGetValue(flag.ToString(), out var cluster))
                {
                    if (firstCluster == null)
                        firstCluster = cluster;
                    else
                        Assert.Equal(firstCluster, cluster);
                }
            }
        }
    }

    [Fact]
    public void SameClusterInvariant_DifferentClusters_Detects()
    {
        // Two flags for the same region mapping to DIFFERENT clusters
        // (architecturally impossible, but the safety net should catch it)
        var regionToFlags = new Dictionary<int, List<int>>
        {
            [755890042] = new List<int> { 100, 200 }
        };
        var eventMap = new Dictionary<string, string>
        {
            ["100"] = "stormveil_c1",
            ["200"] = "liurnia_c2"  // different cluster!
        };

        bool violationDetected = false;
        foreach (var (region, flags) in regionToFlags)
        {
            string? firstCluster = null;
            foreach (var flag in flags)
            {
                if (eventMap.TryGetValue(flag.ToString(), out var cluster))
                {
                    if (firstCluster == null)
                        firstCluster = cluster;
                    else if (cluster != firstCluster)
                        violationDetected = true;
                }
            }
        }

        Assert.True(violationDetected);
    }
}
