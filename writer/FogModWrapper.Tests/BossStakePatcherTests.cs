using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class BossStakePatcherTests
{
    private static MSBE MsbWithStake(string areaName)
    {
        // Mirrors FogMod's real output for a BossTrigger-less created stake:
        // always-on flag 6001, UnkT08 left at 0, and a cylinder activation
        // region assigned (the region takes precedence over UnkT08).
        var msb = new MSBE();
        msb.Events.RetryPoints.Add(new MSBE.Event.RetryPoint
        {
            Name = $"{areaName} stake",
            RetryPartName = "AEG099_500_21334",
            RetryRegionName = "c0000_21335 stake",
            EventFlagID = 6001,
            UnkT08 = 0f,
        });
        return msb;
    }

    [Fact]
    public void ApplyToMsb_RescopesFlagAndKeepsRegion()
    {
        // FogMod's created stake for a BossTrigger-less area falls back to
        // flag 6001 (always on): dying anywhere within the activation
        // cylinder offered a respawn inside Edredd's chapel, even before
        // entering the arena. The patch rescopes activation to the boss
        // cluster's zone-tracking entry flag; the cylinder region (whose
        // radius is bounded at annotation time via Area.StakeRadius) and the
        // respawn part must stay untouched.
        var msb = MsbWithStake("boss_room");

        var patched = BossStakePatcher.ApplyToMsb(msb, "boss_room", 1050294004);

        Assert.True(patched);
        var rp = Assert.Single(msb.Events.RetryPoints);
        Assert.Equal(1050294004u, rp.EventFlagID);
        Assert.Equal("c0000_21335 stake", rp.RetryRegionName);
        Assert.Equal("AEG099_500_21334", rp.RetryPartName);
        Assert.Equal(0f, rp.UnkT08);
    }

    [Fact]
    public void ApplyToMsb_NoMatchingStake_ReturnsFalse()
    {
        var msb = MsbWithStake("some_other_area");

        var patched = BossStakePatcher.ApplyToMsb(msb, "boss_room", 1050294004);

        Assert.False(patched);
        Assert.Equal(6001u, msb.Events.RetryPoints[0].EventFlagID);
    }
}
