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

    [Fact]
    public void Parse_TorrentArenas_ParsesEntries()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[torrent_arenas]]
            map = "m12_03_00_00"
            collisions = ["h006000", "h006100"]
            """);
        var arena = Assert.Single(tweaks.TorrentArenas);
        Assert.Equal("m12_03_00_00", arena.Map);
        Assert.Equal(new List<string> { "h006000", "h006100" }, arena.Collisions);
    }

    [Fact]
    public void Parse_TorrentArenas_EmptyCollisions_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[torrent_arenas]]
            map = "m12_03_00_00"
            collisions = []
            """));
        Assert.Contains("collisions", ex.Message);
    }

    [Fact]
    public void Parse_SpiritspringRemovals_ParsesEntry()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[spiritspring_removals]]
            map = "m61_49_42_00"
            position = [76.7, 303.2, 127.2]
            required_zone = "reprimand"
            """);
        var spring = Assert.Single(tweaks.SpiritspringRemovals);
        Assert.Equal("m61_49_42_00", spring.Map);
        Assert.Equal(76.7f, spring.X, precision: 3);
        Assert.Equal(303.2f, spring.Y, precision: 3);
        Assert.Equal(127.2f, spring.Z, precision: 3);
        Assert.Equal("reprimand", spring.RequiredZone);
    }

    [Fact]
    public void Parse_SpiritspringRemovals_IntegerCoordinates_Parse()
    {
        // TOML "303" is a long, not a double; the loader must accept both.
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[spiritspring_removals]]
            map = "m61_49_42_00"
            position = [76, 303, 127]
            required_zone = "reprimand"
            """);
        Assert.Equal(303f, tweaks.SpiritspringRemovals[0].Y);
    }

    [Fact]
    public void Parse_SpiritspringRemovals_WrongPositionArity_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[spiritspring_removals]]
            map = "m61_49_42_00"
            position = [76.7, 303.2]
            required_zone = "reprimand"
            """));
        Assert.Contains("position", ex.Message);
    }

    [Fact]
    public void Parse_RemoveEntities_ParsesEntryWithMatchGroupDefault()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[remove_entities]]
            map = "m20_01_00_00"
            entity_id = 20006662
            match_group = true

            [[remove_entities]]
            map = "m12_05_00_00"
            entity_id = 12345
            """);
        Assert.Equal(2, tweaks.RemoveEntities.Count);
        Assert.Equal("m20_01_00_00", tweaks.RemoveEntities[0].Map);
        Assert.Equal(20006662, tweaks.RemoveEntities[0].EntityId);
        Assert.True(tweaks.RemoveEntities[0].MatchGroup);
        Assert.False(tweaks.RemoveEntities[1].MatchGroup);
    }

    [Fact]
    public void Parse_RemoveEntities_MissingEntityId_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[remove_entities]]
            map = "m20_01_00_00"
            """));
        Assert.Contains("entity_id", ex.Message);
    }

    [Fact]
    public void Parse_StakeRemovals_ParsesEntries()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[stake_removals]]
            map = "m60_12_09_02"
            name = "m60_51_36_00-AEG099_502_2000"
            """);
        var stake = Assert.Single(tweaks.StakeRemovals);
        Assert.Equal("m60_12_09_02", stake.Map);
        Assert.Equal("m60_51_36_00-AEG099_502_2000", stake.Name);
    }

    [Fact]
    public void Parse_StakeRemovals_MissingName_Throws()
    {
        var ex = Assert.Throws<InvalidDataException>(() => GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true

            [[stake_removals]]
            map = "m60_12_09_02"
            """));
        Assert.Contains("name", ex.Message);
    }

    [Fact]
    public void Parse_AbsentTargetSections_DefaultEmpty()
    {
        var tweaks = GameTweaksLoader.Parse("""
            [config_vars]
            logicpass = true
            """);
        Assert.Empty(tweaks.TorrentArenas);
        Assert.Empty(tweaks.SpiritspringRemovals);
        Assert.Empty(tweaks.RemoveEntities);
        Assert.Empty(tweaks.StakeRemovals);
    }
}
