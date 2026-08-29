using Xunit;

namespace FogModWrapper.Tests;

public class VanillaMapPinnerTests
{
    private static void Write(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    private sealed class TempDir : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "sf-pin-" + Guid.NewGuid().ToString("N"));
        public TempDir() => Directory.CreateDirectory(Path);
        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    [Fact]
    public void Pin_CopiesMsbAndEmevdFromVanillaWhenModHasNeither()
    {
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        Write(Path.Combine(vanilla, "m60_52_39_00.msb.dcx"), "msb-1.16");
        Write(Path.Combine(vanilla, "m60_52_39_00.emevd.dcx"), "emevd-1.16");
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" });

        Assert.Equal(2, pinned);
        Assert.Equal("msb-1.16", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
        Assert.Equal("emevd-1.16", File.ReadAllText(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
    }

    [Fact]
    public void Pin_NeverOverwritesAFileFogModWrote()
    {
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        Write(Path.Combine(vanilla, "m60_52_39_00.msb.dcx"), "msb-1.16");
        Write(Path.Combine(vanilla, "m60_52_39_00.emevd.dcx"), "emevd-1.16");
        Write(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx"), "emevd-with-fog-edits");

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_52_39_00" });

        Assert.Equal(1, pinned);
        Assert.Equal("emevd-with-fog-edits", File.ReadAllText(Path.Combine(mod, "event", "m60_52_39_00.emevd.dcx")));
        Assert.Equal("msb-1.16", File.ReadAllText(Path.Combine(mod, "map", "mapstudio", "m60_52_39_00.msb.dcx")));
    }

    [Fact]
    public void Pin_SkipsFilesTheItemRandomizerWrote()
    {
        // ModEngine loads mods/itemrando after mods/fogmod, so a pinned copy in
        // fogmod would shadow a file the Item Randomizer wrote for the same map.
        using var tmp = new TempDir();
        var vanilla = Path.Combine(tmp.Path, "Vanilla");
        var mod = Path.Combine(tmp.Path, "mod");
        var merge = Path.Combine(tmp.Path, "merge");
        Write(Path.Combine(vanilla, "m60_13_09_02.msb.dcx"), "msb-1.16");
        Write(Path.Combine(vanilla, "m60_13_09_02.emevd.dcx"), "emevd-1.16");
        Write(Path.Combine(merge, "map", "MapStudio", "m60_13_09_02.msb.dcx"), "msb-with-item-edits");
        Directory.CreateDirectory(mod);

        int pinned = VanillaMapPinner.Pin(mod, vanilla, new[] { "m60_13_09_02" }, merge);

        Assert.Equal(1, pinned);
        Assert.False(File.Exists(Path.Combine(mod, "map", "mapstudio", "m60_13_09_02.msb.dcx")));
        Assert.Equal("emevd-1.16", File.ReadAllText(Path.Combine(mod, "event", "m60_13_09_02.emevd.dcx")));
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
