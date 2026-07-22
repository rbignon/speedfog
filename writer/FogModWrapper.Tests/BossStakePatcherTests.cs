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
    public void ApplyToMsb_StakeRegion_ReplacesCylinderWithBox()
    {
        // A cylinder centered on the stake AT the gate bleeds symmetrically
        // through the fog wall into the neighbouring zone (2026-07-22 second
        // playtest: deaths in the fort near the gate still offered the
        // chapel respawn). The AABB from map_splits.toml becomes a box whose
        // edge can sit flush with the wall.
        var msb = MsbWithStake("boss_room");
        msb.Regions.Others.Add(new MSBE.Region.Other
        {
            Name = "c0000_21335 stake",
            Shape = new MSB.Shape.Cylinder { Radius = 40f, Height = 100f },
            Position = new System.Numerics.Vector3(55.1f, 343.5f, -86.9f),
        });

        var patched = BossStakePatcher.ApplyToMsb(
            msb, "boss_room", 1050294004,
            new List<float> { 30f, 370f, -120f, 100f, 412f, -86.5f });

        Assert.True(patched);
        var region = msb.Regions.Others.Single(r => r.Name == "c0000_21335 stake");
        var box = Assert.IsType<MSB.Shape.Box>(region.Shape);
        Assert.Equal(70f, box.Width);
        Assert.Equal(33.5f, box.Depth);
        Assert.Equal(42f, box.Height);
        // Bottom-center anchor of the AABB
        Assert.Equal(65f, region.Position.X);
        Assert.Equal(370f, region.Position.Y);
        Assert.Equal(-103.25f, region.Position.Z);
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
