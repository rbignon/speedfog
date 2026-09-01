using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using Xunit;
using static FogModWrapper.Tests.ParamTestHelper;

namespace FogModWrapper.Tests;

public class UntouchableBossInjectorTests
{
    [Fact]
    public void Apply_ClonesNpcRowOutOfBandWithBossStats()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");

        UntouchableBossInjector.Apply(npc, sp);

        var row = npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        Assert.Equal(UntouchableBossInjector.BOSS_HP, (uint)row["hp"].Value);
        Assert.Equal(UntouchableBossInjector.BOSS_RUNES, (uint)row["getSoul"].Value);
        Assert.Equal(SpeedFogIds.UntouchableBossSpEffectRow, (int)row["spEffectID19"].Value);
    }

    [Fact]
    public void Apply_CustomSpEffectRowHasPartialCutRates()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");

        UntouchableBossInjector.Apply(npc, sp);

        var row = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossSpEffectRow);
        foreach (var field in new[]
        {
            "slashDamageCutRate", "blowDamageCutRate", "thrustDamageCutRate",
            "neutralDamageCutRate", "magicDamageCutRate", "fireDamageCutRate",
            "thunderDamageCutRate", "darkDamageCutRate",
        })
        {
            Assert.Equal(UntouchableBossInjector.DAMAGE_CUT, (float)row[field].Value);
        }
    }

    [Fact]
    public void IsBossPlaced_MatchesSourceEntityValue()
    {
        Assert.True(UntouchableBossInjector.IsBossPlaced(
            new Dictionary<string, string> { ["30001800"] = "2049420200" }));
        Assert.False(UntouchableBossInjector.IsBossPlaced(
            new Dictionary<string, string> { ["30001800"] = "11000295" }));
        Assert.False(UntouchableBossInjector.IsBossPlaced(new Dictionary<string, string>()));
    }

    [Fact]
    public void ApplyToMsb_RepointsPlacedBossAndLeavesOthersAlone()
    {
        var msb = new MSBE();
        // The randomizer-placed boss: arena entity id, source model + npc.
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9000", ModelName = "c5280",
            EntityID = 30001800, NPCParamID = 52800086, ThinkParamID = 52800000,
        });
        // A halloween greeter (same model/npc, EntityID 0): must stay vanilla.
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9001", ModelName = "c5280",
            EntityID = 0, NPCParamID = 52800086, ThinkParamID = 755890000,
        });
        // An unrelated boss in the same map: must stay untouched.
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3500_9000", ModelName = "c3500",
            EntityID = 30001850, NPCParamID = 35000030, ThinkParamID = 35000000,
        });

        var (repointed, ids) = UntouchableBossInjector.ApplyToMsb(
            msb, new HashSet<uint> { 30001800 }, _ => { });

        Assert.Equal(1, repointed);
        Assert.Equal(new List<uint> { 30001800 }, ids);
        var boss = msb.Parts.Enemies.Single(e => e.EntityID == 30001800);
        Assert.Equal(SpeedFogIds.UntouchableBossNpcRow, boss.NPCParamID);
        Assert.Equal(52800000, boss.ThinkParamID); // AI stays vanilla in this plan
        // MSB-scale experiment: only the promoted part grows.
        Assert.Equal(new Vector3(UntouchableBossInjector.BOSS_SCALE), boss.Scale);
        var greeter = msb.Parts.Enemies.Single(e => e.Name == "c5280_9001");
        Assert.Equal(52800086, greeter.NPCParamID);
        Assert.Equal(Vector3.One, greeter.Scale);
        Assert.Equal(35000030, msb.Parts.Enemies.Single(e => e.ModelName == "c3500").NPCParamID);
    }

    [Fact]
    public void ApplyToMsb_WarnsAndSkipsWrongModelAtArenaId()
    {
        var msb = new MSBE();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c3500_9000", ModelName = "c3500",
            EntityID = 30001800, NPCParamID = 35000030, ThinkParamID = 35000000,
        });
        var warnings = new List<string>();

        var (repointed, ids) = UntouchableBossInjector.ApplyToMsb(
            msb, new HashSet<uint> { 30001800 }, warnings.Add);

        Assert.Equal(0, repointed);
        Assert.Empty(ids);
        Assert.Equal(35000030, msb.Parts.Enemies[0].NPCParamID);
        // The scale experiment must never leak onto a wrong-model part.
        Assert.Equal(Vector3.One, msb.Parts.Enemies[0].Scale);
        Assert.Contains(warnings, w => w.Contains("30001800"));
    }
}
