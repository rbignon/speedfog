using Xunit;

namespace FogModWrapper.Tests;

public class GameTweaksLoaderTests
{
    [Fact]
    public void Parse_ConfigVars_MapsBoolsToFogModStrings()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            scalepass = false
            logicpass = true
            """);

        Assert.Equal("FALSE", tweaks.ConfigVars["scalepass"]);
        Assert.Equal("TRUE", tweaks.ConfigVars["logicpass"]);
        Assert.Empty(tweaks.StartupFlags);
    }

    [Fact]
    public void Parse_MissingConfigVars_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [[startup_flags]]
            map = "m10_00_00_00"
            flag = 10000500
            """));
        Assert.Contains("config_vars", ex.Message);
    }

    [Fact]
    public void Parse_NonBoolConfigVar_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            scalepass = "FALSE"
            """));
        Assert.Contains("scalepass", ex.Message);
    }

    [Fact]
    public void Parse_StartupFlags_ParsesEntriesWithDefaultOn()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[startup_flags]]
            map = "m35_00_00_00"
            flag = 35000565
            on = false

            [[startup_flags]]
            map = "common"
            flag = 12345
            """);

        Assert.Equal(2, tweaks.StartupFlags.Count);
        Assert.Equal(("m35_00_00_00", 35000565, false), tweaks.StartupFlags[0]);
        // 'on' defaults to true when omitted
        Assert.Equal(("common", 12345, true), tweaks.StartupFlags[1]);
    }

    [Fact]
    public void Parse_StartupFlagMissingMap_Throws()
    {
        Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[startup_flags]]
            flag = 35000565
            """));
    }

    [Fact]
    public void Load_MissingFile_Throws()
    {
        Assert.Throws<FileNotFoundException>(
            () => GameTweaksLoader.Load("/nonexistent/game_tweaks.toml"));
    }

    [Fact]
    public void RealDataFile_LoadsWithCriticalVarsAndGateFlags()
    {
        var baseDir = AppDomain.CurrentDomain.BaseDirectory;
        var path = Path.GetFullPath(
            Path.Combine(baseDir, "../../../../..", "data", "game_tweaks.toml"));
        Assert.True(File.Exists(path), $"tracked data file missing: {path}");

        var tweaks = GameTweaksLoader.Load(path);

        // Graph.Construct dies without these; a broken edit must fail here
        Assert.Equal("FALSE", tweaks.ConfigVars["scalepass"]);
        Assert.Equal("TRUE", tweaks.ConfigVars["logicpass"]);
        Assert.Equal("TRUE", tweaks.ConfigVars["academyglintstonekey"]);
        Assert.True(tweaks.ConfigVars.Count >= 50,
            $"expected the full ConfigVars table, got {tweaks.ConfigVars.Count}");
        Assert.True(tweaks.StartupFlags.Count >= 3);
        Assert.Contains(("m10_00_00_00", 10000500, true), tweaks.StartupFlags);
    }
}
