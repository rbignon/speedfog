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
}
