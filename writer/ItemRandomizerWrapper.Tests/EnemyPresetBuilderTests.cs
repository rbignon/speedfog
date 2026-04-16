using RandomizerCommon;
using Xunit;

namespace ItemRandomizerWrapper.Tests;

/// <summary>
/// Tests for Program.BuildEnemyPreset (the programmatic preset builder).
/// Covers the MinorBoss pool extension, Basic class RemoveSource, and the
/// bosshp option override sourced from ABND_UWYG_Pirl_BossModifiésBETA.
/// </summary>
public class EnemyPresetBuilderTests
{
    [Fact]
    public void None_LocksAllBossClasses_NoExtras()
    {
        var preset = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = "none" });

        foreach (var cls in new[] {
            EnemyAnnotations.EnemyClass.Boss,
            EnemyAnnotations.EnemyClass.Miniboss,
            EnemyAnnotations.EnemyClass.MinorBoss,
            EnemyAnnotations.EnemyClass.NightMiniboss,
            EnemyAnnotations.EnemyClass.DragonMiniboss,
            EnemyAnnotations.EnemyClass.Evergaol,
        })
        {
            Assert.True(preset.Classes[cls].NoRandom, $"{cls} must be NoRandom in 'none' mode");
        }
        Assert.False(preset.Classes.ContainsKey(EnemyAnnotations.EnemyClass.Basic));
        Assert.True(preset["bosshp"], "'none' mode must leave bosshp at its default (true)");
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("all")]
    public void MinorBossPool_ContainsExtraIds(string mode)
    {
        var preset = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = mode });

        var minorBoss = preset.Classes[EnemyAnnotations.EnemyClass.MinorBoss];
        Assert.NotNull(minorBoss.Pools);
        var pool = Assert.Single(minorBoss.Pools);
        Assert.StartsWith("default;", pool.Pool);
        foreach (var id in Program.ExtraMinorBossPoolIds)
        {
            Assert.Contains(id.ToString(), pool.Pool);
        }
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("all")]
    public void MinorBossClass_HasNamedRemoveSource(string mode)
    {
        var preset = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = mode });

        var minorBoss = preset.Classes[EnemyAnnotations.EnemyClass.MinorBoss];
        Assert.NotNull(minorBoss.RemoveSource);
        foreach (var name in Program.MinorBossRemoveSourceNames)
        {
            Assert.Contains(name, minorBoss.RemoveSource);
        }
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("all")]
    public void BasicClass_HasRemoveSource(string mode)
    {
        var preset = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = mode });

        var basic = preset.Classes[EnemyAnnotations.EnemyClass.Basic];
        Assert.NotNull(basic.RemoveSource);
        foreach (var id in Program.BasicRemoveSourceIds)
        {
            Assert.Contains(id.ToString(), basic.RemoveSource);
        }
        // IDs in MinorBoss pool but NOT Basic-classified must be absent from
        // Basic.RemoveSource. Derived to avoid drift if the lists change.
        foreach (var id in Program.ExtraMinorBossPoolIds.Except(Program.BasicRemoveSourceIds))
        {
            Assert.DoesNotContain(id.ToString(), basic.RemoveSource);
        }
    }

    [Theory]
    [InlineData("minor")]
    [InlineData("all")]
    public void BosshpOption_DisabledToPreventDoubleBoost(string mode)
    {
        var preset = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = mode });

        Assert.False(preset["bosshp"],
            "bosshp must be disabled to avoid double-boosting promoted Basic enemies "
            + "(geom-mean HP + tier scaling would make them too tanky)");
        Assert.True(preset["regularhp"],
            "regularhp stays at its default (true); it only affects boss→basic placements "
            + "which don't occur in speedfog's configuration");
    }

    [Fact]
    public void MinorMode_LocksMajorBoss_AllMode_DoesNot()
    {
        var minor = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = "minor" });
        var all = Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = "all" });

        Assert.True(minor.Classes[EnemyAnnotations.EnemyClass.Boss].NoRandom);
        Assert.False(all.Classes.ContainsKey(EnemyAnnotations.EnemyClass.Boss));
    }

    [Fact]
    public void InvalidRandomizeBosses_Throws()
    {
        Assert.Throws<System.ArgumentException>(() =>
            Program.BuildEnemyPreset(new EnemyOptionsConfig { RandomizeBosses = "bogus" }));
    }
}
