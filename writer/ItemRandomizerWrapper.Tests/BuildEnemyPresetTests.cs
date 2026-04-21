using ItemRandomizerWrapper;
using RandomizerCommon;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

public class BuildEnemyPresetTests
{
    [Fact]
    public void BuildEnemyPreset_NoAssignments_LeavesEnemiesNull()
    {
        var options = new EnemyOptionsConfig { RandomizeBosses = "all" };
        var preset = Program.BuildEnemyPreset(options, null);
        Assert.Null(preset.Enemies);
    }

    [Fact]
    public void BuildEnemyPreset_WithAssignments_CopiesIntoPresetEnemies()
    {
        var options = new EnemyOptionsConfig { RandomizeBosses = "all" };
        var assignments = new Dictionary<string, string>
        {
            ["18000850"] = "10000850",
            ["1042360800"] = "1043360800",
        };
        var preset = Program.BuildEnemyPreset(options, assignments);
        Assert.NotNull(preset.Enemies);
        Assert.Equal(2, preset.Enemies!.Count);
        Assert.Equal("10000850", preset.Enemies["18000850"]);
    }

    [Fact]
    public void BuildEnemyPreset_RandomizeNone_IgnoresAssignments()
    {
        var options = new EnemyOptionsConfig { RandomizeBosses = "none" };
        var assignments = new Dictionary<string, string> { ["x"] = "y" };
        var preset = Program.BuildEnemyPreset(options, assignments);
        Assert.Null(preset.Enemies);
        Assert.True(preset.Classes[EnemyAnnotations.EnemyClass.MinorBoss].NoRandom);
    }

    [Fact]
    public void BuildEnemyPreset_MinorMode_MergesClassesWithDefaultPool()
    {
        var options = new EnemyOptionsConfig { RandomizeBosses = "minor" };
        var preset = Program.BuildEnemyPreset(options, null);

        // MinorBoss pool is "default" only (no promoted IDs, no RemoveSource).
        var minorPool = preset.Classes[EnemyAnnotations.EnemyClass.MinorBoss];
        Assert.Equal("default", minorPool.Pools![0].Pool);
        Assert.Null(minorPool.RemoveSource);

        // Boss class locked in minor mode.
        Assert.True(preset.Classes[EnemyAnnotations.EnemyClass.Boss].NoRandom);
    }
}
