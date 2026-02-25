using FogModWrapper;
using Xunit;

namespace FogModWrapper.Tests;

public class ZoneTrackingTests
{
    [Theory]
    [InlineData("m34_12_00_00_34122840", 34122840)]
    [InlineData("m30_14_00_00_30142840", 30142840)]
    [InlineData("m35_00_00_00_35002840", 35002840)]
    [InlineData("m30_18_00_00_30182840", 30182840)]
    public void ParseGateActionEntity_NumericGate_ReturnsEntityId(string gateName, int expected)
    {
        Assert.Equal(expected, ZoneTrackingInjector.ParseGateActionEntity(gateName));
    }

    [Theory]
    [InlineData("m10_01_00_00_AEG099_001_9000")]
    [InlineData("m31_05_00_00_AEG099_230_9001")]
    [InlineData("m11_10_00_00_AEG099_231_9000")]
    [InlineData("m34_12_00_00_AEG099_003_9000")]
    public void ParseGateActionEntity_AEG099Gate_ReturnsZero(string gateName)
    {
        Assert.Equal(0, ZoneTrackingInjector.ParseGateActionEntity(gateName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("m10_01_00")]
    public void ParseGateActionEntity_InvalidInput_ReturnsZero(string gateName)
    {
        Assert.Equal(0, ZoneTrackingInjector.ParseGateActionEntity(gateName));
    }

    [Fact]
    public void ParseGateActionEntity_CrossTileGate_ReturnsZero()
    {
        // Cross-tile gates have a second map prefix at parts[4], not a numeric entity
        Assert.Equal(0, ZoneTrackingInjector.ParseGateActionEntity(
            "m60_13_13_02_m60_52_53_00-AEG099_003_9001"));
    }

    [Fact]
    public void ResolveEntityCandidate_SingleCandidate_ReturnsFlag()
    {
        var candidates = new List<ZoneTrackingInjector.EntityCandidate>
        {
            new(flagId: 100, destMaps: new HashSet<(byte, byte, byte, byte)> { (31, 5, 0, 0) })
        };
        // Even with a non-matching dest map, single candidate returns directly
        var result = ZoneTrackingInjector.ResolveEntityCandidate(candidates, (10, 1, 0, 0));
        Assert.Equal(100, result);
    }

    [Fact]
    public void ResolveEntityCandidate_TwoCandidates_DisambiguatesByDestMap()
    {
        var candidates = new List<ZoneTrackingInjector.EntityCandidate>
        {
            new(flagId: 100, destMaps: new HashSet<(byte, byte, byte, byte)> { (31, 5, 0, 0) }),
            new(flagId: 200, destMaps: new HashSet<(byte, byte, byte, byte)> { (10, 1, 0, 0) })
        };
        // Warp targets m10_01 → should pick flag 200
        var result = ZoneTrackingInjector.ResolveEntityCandidate(candidates, (10, 1, 0, 0));
        Assert.Equal(200, result);

        // Warp targets m31_05 → should pick flag 100
        result = ZoneTrackingInjector.ResolveEntityCandidate(candidates, (31, 5, 0, 0));
        Assert.Equal(100, result);
    }

    [Fact]
    public void ResolveEntityCandidate_TwoCandidates_BothMatch_ReturnsNull()
    {
        // When both candidates share the same dest map, disambiguation is impossible
        var sharedMap = ((byte)31, (byte)5, (byte)0, (byte)0);
        var candidates = new List<ZoneTrackingInjector.EntityCandidate>
        {
            new(flagId: 100, destMaps: new HashSet<(byte, byte, byte, byte)> { sharedMap }),
            new(flagId: 200, destMaps: new HashSet<(byte, byte, byte, byte)> { sharedMap })
        };
        var result = ZoneTrackingInjector.ResolveEntityCandidate(candidates, sharedMap);
        Assert.Null(result);
    }

    [Fact]
    public void ResolveEntityCandidate_TwoCandidates_NoMatch_ReturnsNull()
    {
        var candidates = new List<ZoneTrackingInjector.EntityCandidate>
        {
            new(flagId: 100, destMaps: new HashSet<(byte, byte, byte, byte)> { (31, 5, 0, 0) }),
            new(flagId: 200, destMaps: new HashSet<(byte, byte, byte, byte)> { (10, 1, 0, 0) })
        };
        // Warp targets a map not in either candidate's DestMaps
        var result = ZoneTrackingInjector.ResolveEntityCandidate(candidates, (60, 44, 34, 0));
        Assert.Null(result);
    }

    [Fact]
    public void CommonEventLookup_ResolvesWhenDestOnlyCollides()
    {
        // Simulate: Fire Giant exit (has_common_event) + Margit exit both target m10
        var destMap = ((byte)10, (byte)0, (byte)0, (byte)0);

        // Build common event lookup from connections with HasCommonEvent
        var commonEventLookup = new Dictionary<(byte, byte, byte, byte), int>();
        var commonEventCollisions = new HashSet<(byte, byte, byte, byte)>();

        // Only the Fire Giant connection has HasCommonEvent
        ZoneTrackingInjector.RegisterCommonEventKeys(
            new byte[] { 10, 0, 0, 0 }, 1040292829, commonEventLookup, commonEventCollisions);

        Assert.True(commonEventLookup.ContainsKey(destMap));
        Assert.Equal(1040292829, commonEventLookup[destMap]);
        Assert.Empty(commonEventCollisions);
    }

    [Fact]
    public void CommonEventLookup_TracksCollisions()
    {
        var commonEventLookup = new Dictionary<(byte, byte, byte, byte), int>();
        var commonEventCollisions = new HashSet<(byte, byte, byte, byte)>();

        ZoneTrackingInjector.RegisterCommonEventKeys(
            new byte[] { 10, 0, 0, 0 }, 100, commonEventLookup, commonEventCollisions);
        ZoneTrackingInjector.RegisterCommonEventKeys(
            new byte[] { 10, 0, 0, 0 }, 200, commonEventLookup, commonEventCollisions);

        Assert.Contains(((byte)10, (byte)0, (byte)0, (byte)0), commonEventCollisions);
    }

    [Fact]
    public void RegisterEntity_SharedEntity_CreatesTwoCandidates()
    {
        var entityToFlag = new Dictionary<int, List<ZoneTrackingInjector.EntityCandidate>>();

        // Register two connections that share entity 12345
        ZoneTrackingInjector.RegisterEntity(
            entityToFlag, 12345, 100,
            new HashSet<(byte, byte, byte, byte)> { (31, 5, 0, 0) });
        ZoneTrackingInjector.RegisterEntity(
            entityToFlag, 12345, 200,
            new HashSet<(byte, byte, byte, byte)> { (10, 1, 0, 0) });

        Assert.True(entityToFlag.ContainsKey(12345));
        var candidates = entityToFlag[12345];
        Assert.Equal(2, candidates.Count);
        Assert.Equal(100, candidates[0].FlagId);
        Assert.Equal(200, candidates[1].FlagId);
    }
}
