using System.Numerics;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class SpiritspringRemoverTests
{
    private static readonly SpiritspringRemoval Target =
        new("m61_49_42_00", 76.7f, 303.2f, 127.2f, "reprimand");

    [Fact]
    public void ApplyToMsb_RemovesBothRegionTypesWithinRadius()
    {
        var msb = new MSBE();
        msb.Regions.MountJumps.Add(new MSBE.Region.MountJump { Position = new Vector3(76.7f, 303.2f, 127.2f) });
        msb.Regions.MountJumpFalls.Add(new MSBE.Region.MountJumpFall { Position = new Vector3(76.7f, 303.2f, 127.2f) });

        var removed = SpiritspringRemover.ApplyToMsb(msb, Target);

        Assert.Equal(2, removed);
        Assert.Empty(msb.Regions.MountJumps);
        Assert.Empty(msb.Regions.MountJumpFalls);
    }

    [Fact]
    public void ApplyToMsb_LeavesDistantAndLockedRegions()
    {
        var msb = new MSBE();
        // Another spring on the same map, 100m away: must survive
        msb.Regions.MountJumps.Add(new MSBE.Region.MountJump { Position = new Vector3(20.1f, 206.2f, 116.2f) });
        msb.Regions.LockedMountJumps.Add(new MSBE.Region.LockedMountJump { Position = new Vector3(20.1f, 206.2f, 116.2f) });
        msb.Regions.LockedMountJumps.Add(new MSBE.Region.LockedMountJump { Position = new Vector3(76.7f, 303.2f, 127.2f) });

        var removed = SpiritspringRemover.ApplyToMsb(msb, Target);

        Assert.Equal(0, removed);
        Assert.Single(msb.Regions.MountJumps);
        Assert.Equal(2, msb.Regions.LockedMountJumps.Count);
    }

    [Fact]
    public void Patch_SkipsMapWhenRequiredZoneNotInDag()
    {
        using var tmp = new TempDir();
        var msbPath = WriteMsbWithSpring(tmp.Path);

        SpiritspringRemover.Patch(tmp.Path, tmp.Path,
            new Dictionary<string, int>(), new[] { Target });

        var reread = MSBE.Read(msbPath);
        Assert.Single(reread.Regions.MountJumps);
    }

    [Fact]
    public void Patch_RemovesSpringWhenRequiredZoneInDag()
    {
        using var tmp = new TempDir();
        var msbPath = WriteMsbWithSpring(tmp.Path);

        SpiritspringRemover.Patch(tmp.Path, tmp.Path,
            new Dictionary<string, int> { [Target.RequiredZone] = 3 }, new[] { Target });

        var reread = MSBE.Read(msbPath);
        Assert.Empty(reread.Regions.MountJumps);
    }

    private static string WriteMsbWithSpring(string modDir)
    {
        var mapDir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);
        var msb = new MSBE();
        msb.Regions.MountJumps.Add(new MSBE.Region.MountJump { Position = new Vector3(Target.X, Target.Y, Target.Z) });
        var path = Path.Combine(mapDir, $"{Target.Map}.msb.dcx");
        msb.Write(path, DCX.Type.DCX_DFLT_10000_44_9);
        return path;
    }

    /// <summary>
    /// Disposable temp directory helper (mirrors StartupFlagInjectorTests).
    /// </summary>
    private sealed class TempDir : IDisposable
    {
        public string Path { get; }

        public TempDir()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"sftest_{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, true);
        }
    }
}
