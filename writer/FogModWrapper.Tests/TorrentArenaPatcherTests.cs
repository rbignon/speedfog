using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class TorrentArenaPatcherTests
{
    private static MSBE.Part.Collision MakeCollision(string name, bool disableTorrent)
    {
        return new MSBE.Part.Collision
        {
            Name = name,
            DisableTorrent = disableTorrent,
        };
    }

    [Fact]
    public void ApplyToMsb_FlipsOnlyMatchingNames()
    {
        var msb = new MSBE();
        msb.Parts.Collisions.Add(MakeCollision("h020300", disableTorrent: true));
        msb.Parts.Collisions.Add(MakeCollision("h020500", disableTorrent: true));
        msb.Parts.Collisions.Add(MakeCollision("h999999", disableTorrent: true));

        var targets = new HashSet<string> { "h020300", "h020500" };

        var flipped = TorrentArenaPatcher.ApplyToMsb(msb, targets);

        Assert.Equal(2, flipped);
        Assert.False(msb.Parts.Collisions.Single(c => c.Name == "h020300").DisableTorrent);
        Assert.False(msb.Parts.Collisions.Single(c => c.Name == "h020500").DisableTorrent);
        // Unrelated collision is left alone even when DisableTorrent was true
        Assert.True(msb.Parts.Collisions.Single(c => c.Name == "h999999").DisableTorrent);
    }

    [Fact]
    public void ApplyToMsb_SkipsAlreadyEnabled()
    {
        var msb = new MSBE();
        msb.Parts.Collisions.Add(MakeCollision("h006000", disableTorrent: false));
        msb.Parts.Collisions.Add(MakeCollision("h006100", disableTorrent: true));

        var targets = new HashSet<string> { "h006000", "h006100" };

        var flipped = TorrentArenaPatcher.ApplyToMsb(msb, targets);

        Assert.Equal(1, flipped);
        Assert.False(msb.Parts.Collisions.Single(c => c.Name == "h006000").DisableTorrent);
        Assert.False(msb.Parts.Collisions.Single(c => c.Name == "h006100").DisableTorrent);
    }

    [Fact]
    public void Targets_LocksDownArenaContract()
    {
        // Encodes the design contract from docs/torrent-arena-patcher.md:
        // exactly the four boss arenas where Torrent is intentionally re-enabled.
        // Mohgwyn (m12_05) is deliberately excluded; new entries should not be
        // added here without a docs update + an explicit test change.
        Assert.Equal(
            new[] { "m12_03_00_00", "m12_04_00_00", "m12_08_00_00", "m12_09_00_00" },
            TorrentArenaPatcher.Targets.Keys.OrderBy(k => k).ToArray());
        Assert.DoesNotContain("m12_05_00_00", TorrentArenaPatcher.Targets.Keys);
    }

    [Fact]
    public void ApplyToMsb_NoMatches_ReturnsZero()
    {
        var msb = new MSBE();
        msb.Parts.Collisions.Add(MakeCollision("h000000", disableTorrent: true));

        var flipped = TorrentArenaPatcher.ApplyToMsb(msb, new HashSet<string> { "h020300" });

        Assert.Equal(0, flipped);
        Assert.True(msb.Parts.Collisions[0].DisableTorrent);
    }
}
