using Xunit;

namespace FogModWrapper.Tests;

public class VanillaMapPinnerTests
{
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    [Fact]
    public void Pin_CopiesMsbAndEmevdFromVanillaWhenModHasNeither()
    {
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        Write(Path.Combine(vanilla, "m60_52_39_00.msb.dcx"), "msb-snapshot");
        Write(Path.Combine(vanilla, "m60_52_39_00.emevd.dcx"), "emevd-snapshot");
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" });

        Assert.Equal(2, pinned);
        Assert.Equal("msb-snapshot", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
        Assert.Equal("emevd-snapshot", File.ReadAllText(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
    }

    [Fact]
    public void Pin_NeverOverwritesAFileFogModWrote()
    {
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        Write(Path.Combine(vanilla, "m60_52_39_00.msb.dcx"), "msb-snapshot");
        Write(Path.Combine(vanilla, "m60_52_39_00.emevd.dcx"), "emevd-snapshot");
        Write(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx"), "emevd-with-fog-edits");

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" });

        Assert.Equal(1, pinned);
        Assert.Equal("emevd-with-fog-edits", File.ReadAllText(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
        Assert.Equal("msb-snapshot", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
    }

    [Fact]
    public void Pin_CopiesFilesTheItemRandomizerWroteIntoTheModOutput()
    {
        // mods/fogmod wins the ModEngine order, and EventDisabler /
        // VanillaWarpRemover only patch the FogMod output: a pinned map the
        // Item Randomizer wrote must be copied there (identical content, then
        // patched), not left merge-dir-only where injectors cannot reach it.
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        var merge = Path.Combine(tmp.Path, "merge");
        Write(Path.Combine(vanilla, "m60_52_39_00.msb.dcx"), "msb-1.17-snapshot");
        Write(Path.Combine(vanilla, "m60_52_39_00.emevd.dcx"), "emevd-1.17-snapshot");
        Write(Path.Combine(merge, "map", "MapStudio", "m60_52_39_00.msb.dcx"), "msb-with-item-edits");
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" }, merge);

        Assert.Equal(2, pinned);
        Assert.Equal("msb-with-item-edits", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
        Assert.Equal("emevd-1.17-snapshot", File.ReadAllText(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
    }

    [Fact]
    public void Pin_ShipsMergeDirFileEvenWhenSnapshotLacksIt()
    {
        // The presence guarantee must not depend on the snapshot: a map the
        // Item Randomizer wrote is shipped from the merge dir even when the
        // snapshot has no copy of that file at all.
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        var merge = Path.Combine(tmp.Path, "merge");
        Directory.CreateDirectory(vanilla);
        Write(Path.Combine(merge, "map", "MapStudio", "m60_52_39_00.msb.dcx"), "msb-with-item-edits");
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" }, merge);

        Assert.Equal(1, pinned);
        Assert.Equal("msb-with-item-edits", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
        Assert.False(File.Exists(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
    }

    [Fact]
    public void Pin_MissingVanillaFile_IsReportedNotCreated()
    {
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        Directory.CreateDirectory(vanilla);
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m99_00_00_00" });

        Assert.Equal(0, pinned);
        Assert.False(File.Exists(Path.Combine(mod, "event", "m99_00_00_00.emevd.dcx")));
    }
}
