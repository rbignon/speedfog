using FogModWrapper.Models;
using SoulsFormats;
using Xunit;

namespace FogModWrapper.Tests;

public class VanillaWarpRemoverTests
{
    [Fact]
    public void Remove_MatchGroup_RemovesAssetsByEntityGroup()
    {
        using var tmp = new TempDir();
        var mapDir = Path.Combine(tmp.Path, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);

        var msb = new MSBE();
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = "AEG410_900" });
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = "AEG410_901" });

        var keep = new MSBE.Part.Asset { Name = "AEG410_900_2000", ModelName = "AEG410_900" };

        var thorn = new MSBE.Part.Asset { Name = "AEG410_901_2000", ModelName = "AEG410_901" };
        thorn.EntityGroupIDs[0] = 20006662;

        msb.Parts.Assets.Add(keep);
        msb.Parts.Assets.Add(thorn);
        msb.Write(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"), DCX.Type.DCX_DFLT_10000_44_9);

        VanillaWarpRemover.Remove(tmp.Path, new List<RemoveEntity>
        {
            new() { Map = "m20_01_00_00", EntityId = 20006662, MatchGroup = true },
        });

        var reread = MSBE.Read(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"));
        Assert.Contains(reread.Parts.Assets, a => a.Name == "AEG410_900_2000");
        Assert.DoesNotContain(reread.Parts.Assets, a => a.Name == "AEG410_901_2000");
    }

    [Fact]
    public void Remove_WithoutMatchGroup_MatchesByEntityIdOnly()
    {
        using var tmp = new TempDir();
        var mapDir = Path.Combine(tmp.Path, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);

        var msb = new MSBE();
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = "AEG410_901" });

        var grouped = new MSBE.Part.Asset { Name = "AEG410_901_2000", ModelName = "AEG410_901" };
        grouped.EntityGroupIDs[0] = 20006662;
        msb.Parts.Assets.Add(grouped);
        msb.Write(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"), DCX.Type.DCX_DFLT_10000_44_9);

        VanillaWarpRemover.Remove(tmp.Path, new List<RemoveEntity>
        {
            new() { Map = "m20_01_00_00", EntityId = 20006662, MatchGroup = false },
        });

        var reread = MSBE.Read(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"));
        Assert.Contains(reread.Parts.Assets, a => a.Name == "AEG410_901_2000");
    }

    [Fact]
    public void Remove_ByEntityId_RemovesMatchingAsset()
    {
        using var tmp = new TempDir();
        var mapDir = Path.Combine(tmp.Path, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);

        var msb = new MSBE();
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = "AEG099_001" });

        var target = new MSBE.Part.Asset { Name = "AEG099_001_9000", ModelName = "AEG099_001" };
        target.EntityID = 20011234;
        msb.Parts.Assets.Add(target);
        msb.Write(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"), DCX.Type.DCX_DFLT_10000_44_9);

        VanillaWarpRemover.Remove(tmp.Path, new List<RemoveEntity>
        {
            new() { Map = "m20_01_00_00", EntityId = 20011234 },
        });

        var reread = MSBE.Read(Path.Combine(mapDir, "m20_01_00_00.msb.dcx"));
        Assert.DoesNotContain(reread.Parts.Assets, a => a.Name == "AEG099_001_9000");
    }

    [Fact]
    public void Remove_MissingMap_IsANoop()
    {
        using var tmp = new TempDir();
        // No MSB file created — VanillaWarpRemover should return silently
        var ex = Record.Exception(() => VanillaWarpRemover.Remove(tmp.Path, new List<RemoveEntity>
        {
            new() { Map = "m20_01_00_00", EntityId = 20006662, MatchGroup = true },
        }));
        Assert.Null(ex);
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
