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
}
