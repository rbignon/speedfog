using Xunit;
using StaticModBuilder.Winter;

namespace StaticModBuilder.Tests.Winter;

public class WinterModBuilderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "winter-mod-" + Guid.NewGuid().ToString("N"));

    public WinterModBuilderTests() => Directory.CreateDirectory(_root);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch (IOException) { }
    }

    [Fact]
    public void ReportPath_IsASiblingFile()
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = $"{sep}x{sep}data{sep}mods{sep}speedfog-winter";
        var expected = dir + ".report.txt";
        Assert.Equal(expected, WinterModBuilder.ReportPath(dir));
        Assert.Equal(expected, WinterModBuilder.ReportPath(dir + sep));
    }

    [Fact]
    public void Build_FailsLoudlyAndRemovesTheFolderWhenInputsAreMissing()
    {
        var gameDir = Path.Combine(_root, "game");
        var dataDir = Path.Combine(_root, "data");
        var winterDir = Path.Combine(_root, "mods", "winter");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "plugins"));
        Directory.CreateDirectory(winterDir);
        File.WriteAllText(Path.Combine(winterDir, "stale.txt"), "stale");

        var repoPlugins = FindRepoPlugins();
        foreach (var name in new[] { "winter_materials.toml", "winter_textures.toml" })
        {
            File.Copy(Path.Combine(repoPlugins, name), Path.Combine(dataDir, "plugins", name));
        }

        var originalError = Console.Error;
        var captured = new StringWriter();
        Console.SetError(captured);
        bool ok;
        try
        {
            ok = WinterModBuilder.Build(gameDir, winterDir, dataDir);
        }
        finally
        {
            Console.SetError(originalError);
        }

        Assert.False(ok);
        Assert.StartsWith("WINTER FAILED: ", captured.ToString());
        Assert.False(Directory.Exists(winterDir));
    }

    private static string FindRepoPlugins()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            var candidate = Path.Combine(dir.FullName, "data", "plugins");
            if (File.Exists(Path.Combine(candidate, "winter_materials.toml")))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new InvalidOperationException("data/plugins not found above " + AppContext.BaseDirectory);
    }
}
