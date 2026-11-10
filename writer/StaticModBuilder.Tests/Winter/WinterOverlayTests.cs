using Xunit;
using StaticModBuilder.Winter;

namespace StaticModBuilder.Tests.Winter;

public class WinterOverlayTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winter-overlay-" + Guid.NewGuid().ToString("N"));

    public WinterOverlayTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void Copy_ShipsEveryOverlayFileAtItsRelativePathAndListsThem()
    {
        var dataDir = Path.Combine(_root, "data");
        var overlay = WinterOverlay.Locate(dataDir);
        Assert.Equal(Path.Combine(dataDir, "mods-src", "speedfog-winter"), overlay);
        var binder = new byte[] { 0x44, 0x43, 0x58, 0x00, 0xFF, 0x10 };
        WriteFile(Path.Combine(overlay, "chr", "c8002.chrbnd.dcx"), binder);
        WriteFile(Path.Combine(overlay, "chr", "sub", "b.bin"), new byte[] { 1 });
        WriteFile(Path.Combine(overlay, "top.txt"), new byte[] { 2, 3 });
        WriteFile(Path.Combine(overlay, ".gitkeep"), new byte[] { 4 });
        WriteFile(Path.Combine(overlay, ".hidden", "x.bin"), new byte[] { 5 });
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(output, "material"));
        var report = new WinterReport();

        var copied = WinterOverlay.Copy(overlay, output, report);

        Assert.Equal(new[] { "chr/c8002.chrbnd.dcx", "chr/sub/b.bin", "top.txt" }, copied);
        Assert.False(File.Exists(Path.Combine(output, ".gitkeep")));
        Assert.False(Directory.Exists(Path.Combine(output, ".hidden")));
        Assert.Equal(binder, File.ReadAllBytes(Path.Combine(output, "chr", "c8002.chrbnd.dcx")));
        Assert.Equal(new byte[] { 1 }, File.ReadAllBytes(Path.Combine(output, "chr", "sub", "b.bin")));
        Assert.Equal(new byte[] { 2, 3 }, File.ReadAllBytes(Path.Combine(output, "top.txt")));
        Assert.StartsWith("Overlay: 3 files", report.Lines[0]);
        Assert.Contains(report.Lines, line => line.Contains("chr/c8002.chrbnd.dcx") && line.Contains("6 bytes"));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Copy_ShipsNothingAndSaysSoWhenTheOverlayIsAbsentOrEmpty(bool folderExists)
    {
        var overlay = Path.Combine(_root, "overlay");
        if (folderExists)
        {
            Directory.CreateDirectory(Path.Combine(overlay, "chr"));
        }
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(output);
        var report = new WinterReport();

        var copied = WinterOverlay.Copy(overlay, output, report);

        Assert.Empty(copied);
        Assert.Empty(Directory.EnumerateFileSystemEntries(output));
        var line = Assert.Single(report.Lines);
        Assert.StartsWith("Overlay: none", line);
    }

    [Fact]
    public void Copy_RefusesAnOverlayFileTheBuildAlreadyWroteAndCopiesNothing()
    {
        var overlay = Path.Combine(_root, "overlay");
        WriteFile(Path.Combine(overlay, "chr", "a.dcx"), new byte[] { 1 });
        WriteFile(Path.Combine(overlay, "material", "allmaterial.matbinbnd.dcx"), new byte[] { 2 });
        var output = Path.Combine(_root, "out");
        WriteFile(Path.Combine(output, "material", "allmaterial.matbinbnd.dcx"), new byte[] { 9 });

        var error = Assert.Throws<WinterBuildException>(() => WinterOverlay.Copy(overlay, output, new WinterReport()));

        Assert.Contains("material/allmaterial.matbinbnd.dcx", error.Message);
        Assert.False(File.Exists(Path.Combine(output, "chr", "a.dcx")));
        Assert.Equal(new byte[] { 9 }, File.ReadAllBytes(Path.Combine(output, "material", "allmaterial.matbinbnd.dcx")));
    }

    [Fact]
    public void Copy_RefusesAnOverlayFileNamedLikeABuiltFolder()
    {
        var overlay = Path.Combine(_root, "overlay");
        WriteFile(Path.Combine(overlay, "asset"), new byte[] { 1 });
        var output = Path.Combine(_root, "out");
        Directory.CreateDirectory(Path.Combine(output, "asset", "aet"));

        var error = Assert.Throws<WinterBuildException>(() => WinterOverlay.Copy(overlay, output, new WinterReport()));

        Assert.Contains("overlay file asset collides", error.Message);
        Assert.True(Directory.Exists(Path.Combine(output, "asset", "aet")));
    }

    private static void WriteFile(string path, byte[] bytes)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
    }
}
