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
    public void ReportPath_AndStagingPath_AreSiblings()
    {
        var sep = Path.DirectorySeparatorChar;
        var dir = $"{sep}x{sep}data{sep}mods{sep}speedfog-winter";
        Assert.Equal(dir + ".report.txt", WinterModBuilder.ReportPath(dir));
        Assert.Equal(dir + ".report.txt", WinterModBuilder.ReportPath(dir + sep));
        Assert.Equal(dir + ".building", WinterModBuilder.StagingPath(dir));
        Assert.Equal(dir + ".building", WinterModBuilder.StagingPath(dir + sep));
    }

    [Fact]
    public void Build_FailsLoudlyAndRemovesTheFolderWhenInputsAreMissing()
    {
        var (gameDir, dataDir, winterDir) = MakeInputs();
        PlantStaleOutputs(winterDir);

        var (ok, stderr) = RunCapturingConsole(() => WinterModBuilder.Build(gameDir, winterDir, dataDir));

        Assert.False(ok);
        Assert.StartsWith("WINTER FAILED: ", stderr);
        AssertNoOutputs(winterDir);
    }

    [Fact]
    public void Build_VerifiesRetargetTargetsBeforeTheScanAndAnyWrite()
    {
        // The empty game folder has no TPF and no binder: the first failure names the step that
        // runs first. The scan would report "layout scan: no *.geombnd.dcx", the material
        // binders "missing material binder".
        var (gameDir, dataDir, winterDir) = MakeInputs();

        var (ok, stderr) = RunCapturingConsole(() => WinterModBuilder.Build(gameDir, winterDir, dataDir));

        Assert.False(ok);
        Assert.StartsWith("WINTER FAILED: texture retarget ", stderr);
        AssertNoOutputs(winterDir);
    }

    [Fact]
    public void Build_MovesTheStagingFolderIntoPlaceAfterWritingTheReport()
    {
        var winterDir = Path.Combine(_root, "mods", "winter");
        PlantStaleOutputs(winterDir);
        string? seenStaging = null;

        var (ok, _) = RunCapturingConsole(() => WinterModBuilder.Build(winterDir, (staging, report) =>
        {
            seenStaging = staging;
            Assert.False(Directory.Exists(winterDir));
            Assert.False(File.Exists(WinterModBuilder.ReportPath(winterDir)));
            Assert.Empty(Directory.EnumerateFileSystemEntries(staging));
            Directory.CreateDirectory(Path.Combine(staging, "material"));
            File.WriteAllText(Path.Combine(staging, "material", "binder.dcx"), "new");
            report.Line("report line");
            return "summary";
        }));

        Assert.True(ok);
        Assert.Equal(WinterModBuilder.StagingPath(winterDir), seenStaging);
        Assert.False(Directory.Exists(WinterModBuilder.StagingPath(winterDir)));
        Assert.Equal("new", File.ReadAllText(Path.Combine(winterDir, "material", "binder.dcx")));
        Assert.False(File.Exists(Path.Combine(winterDir, "stale.txt")));
        Assert.Equal(new[] { "report line" }, File.ReadAllLines(WinterModBuilder.ReportPath(winterDir)));
    }

    [Fact]
    public void Build_FailedRunDeletesTheStaleReportAndLeavesNoFolder()
    {
        var winterDir = Path.Combine(_root, "mods", "winter");
        PlantStaleOutputs(winterDir);

        var (ok, stderr) = RunCapturingConsole(() => WinterModBuilder.Build(winterDir, (staging, report) =>
        {
            File.WriteAllText(Path.Combine(staging, "partial.dcx"), "partial");
            throw new WinterBuildException("listed TPF missing");
        }));

        Assert.False(ok);
        Assert.Equal("WINTER FAILED: listed TPF missing", stderr.Trim());
        AssertNoOutputs(winterDir);
    }

    [Fact]
    public void Build_CleansUpThenRethrowsAnUnexpectedException()
    {
        var winterDir = Path.Combine(_root, "mods", "winter");
        PlantStaleOutputs(winterDir);
        bool stepsRan = false;

        Assert.Throws<InvalidOperationException>(() => RunCapturingConsole(() => WinterModBuilder.Build(winterDir, (staging, report) =>
        {
            File.WriteAllText(Path.Combine(staging, "partial.dcx"), "partial");
            report.Line("never written");
            stepsRan = true;
            throw new InvalidOperationException("a bug");
        })));

        Assert.True(stepsRan);
        AssertNoOutputs(winterDir);
    }

    [Fact]
    public void CheckWinterDir_RefusesAnotherDirectoryArgumentOrAParentOfOne()
    {
        var sep = Path.DirectorySeparatorChar;
        var game = Path.Combine(_root, "Game");
        var mods = Path.Combine(_root, "mods");
        var speedfog = Path.Combine(mods, "speedfog");
        var winter = Path.Combine(mods, "speedfog-winter");
        (string, string?)[] others =
        {
            ("game directory", game), ("mod directory", speedfog), ("Halloween directory", null),
        };

        Assert.Null(WinterModBuilder.CheckWinterDir(winter, others));
        // A shared name prefix is not a parent.
        Assert.Null(WinterModBuilder.CheckWinterDir(Path.Combine(mods, "speed"), others));

        var same = WinterModBuilder.CheckWinterDir(speedfog + sep, others);
        Assert.NotNull(same);
        Assert.Contains("must not be the mod directory", same);
        var parent = WinterModBuilder.CheckWinterDir(mods, others);
        Assert.NotNull(parent);
        Assert.Contains("must not contain the mod directory", parent);
        var root = WinterModBuilder.CheckWinterDir(_root, others);
        Assert.NotNull(root);
        Assert.Contains("game directory", root);
    }

    private (string GameDir, string DataDir, string WinterDir) MakeInputs()
    {
        var gameDir = Path.Combine(_root, "game");
        var dataDir = Path.Combine(_root, "data");
        var winterDir = Path.Combine(_root, "mods", "winter");
        Directory.CreateDirectory(gameDir);
        Directory.CreateDirectory(Path.Combine(dataDir, "plugins"));
        var repoPlugins = FindRepoPlugins();
        foreach (var name in new[] { "winter_materials.toml", "winter_textures.toml" })
        {
            File.Copy(Path.Combine(repoPlugins, name), Path.Combine(dataDir, "plugins", name));
        }
        return (gameDir, dataDir, winterDir);
    }

    /// <summary>A previous build's folder and report, and a staging folder an interrupted build left.</summary>
    private static void PlantStaleOutputs(string winterDir)
    {
        Directory.CreateDirectory(winterDir);
        File.WriteAllText(Path.Combine(winterDir, "stale.txt"), "stale");
        File.WriteAllText(WinterModBuilder.ReportPath(winterDir), "stale report");
        Directory.CreateDirectory(WinterModBuilder.StagingPath(winterDir));
        File.WriteAllText(Path.Combine(WinterModBuilder.StagingPath(winterDir), "stale.txt"), "stale");
    }

    private static void AssertNoOutputs(string winterDir)
    {
        Assert.False(Directory.Exists(winterDir));
        Assert.False(Directory.Exists(WinterModBuilder.StagingPath(winterDir)));
        Assert.False(File.Exists(WinterModBuilder.ReportPath(winterDir)));
    }

    /// <summary>Runs <paramref name="build"/> with stdout and stderr captured; returns its result and stderr.</summary>
    private static (bool Ok, string Stderr) RunCapturingConsole(Func<bool> build)
    {
        var originalOut = Console.Out;
        var originalError = Console.Error;
        var output = new StringWriter();
        var error = new StringWriter();
        Console.SetOut(output);
        Console.SetError(error);
        try
        {
            return (build(), error.ToString());
        }
        finally
        {
            Console.SetOut(originalOut);
            Console.SetError(originalError);
        }
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
