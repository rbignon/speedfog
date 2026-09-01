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
        Assert.Equal(52800086, msb.Parts.Enemies.Single(e => e.Name == "c5280_9001").NPCParamID);
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
        Assert.Contains(warnings, w => w.Contains("30001800"));
    }

    // Merge-dir fallback (docs/untouchable-boss.md, m60_13_09_02 paragraph):
    // caelid_radahn's boss part lives on an 02-supertile FogMod never
    // writes, so the primary mod-dir scan in Inject never sees it. When the
    // assignment target is still unfound after that scan and a mergeDir is
    // given, Inject reads the merge-dir (Item Randomizer) copy of every map
    // in the fallback list, repoints it, and ships it into modDir.

    [Fact]
    public void Inject_FallbackRepointsMergeDirCopyIntoModDir()
    {
        using var tmp = new TempDir();
        var modDir = Path.Combine(tmp.Path, "mod");
        var mergeDir = Path.Combine(tmp.Path, "merge");
        Directory.CreateDirectory(Path.Combine(modDir, "map", "mapstudio")); // empty: primary scan finds nothing
        var mergeMapDir = Path.Combine(mergeDir, "map", "mapstudio");
        Directory.CreateDirectory(mergeMapDir);

        var msb = new MSBE();
        msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = "c5280" });
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9000", ModelName = "c5280",
            EntityID = 30001800, NPCParamID = 52800140, ThinkParamID = 52800000,
        });
        msb.Write(Path.Combine(mergeMapDir, "m60_13_09_02.msb.dcx"), DCX.Type.DCX_DFLT_10000_44_9);

        var assignments = new Dictionary<string, string>
        {
            ["30001800"] = SpeedFogIds.UntouchableSourceEntity.ToString(),
        };

        UntouchableBossInjector.Inject(modDir, assignments, mergeDir, new[] { "m60_13_09_02" });

        var writtenPath = Path.Combine(modDir, "map", "mapstudio", "m60_13_09_02.msb.dcx");
        Assert.True(File.Exists(writtenPath));
        var reread = MSBE.Read(writtenPath);
        var boss = reread.Parts.Enemies.Single(e => e.EntityID == 30001800);
        Assert.Equal(SpeedFogIds.UntouchableBossNpcRow, boss.NPCParamID);
    }

    [Fact]
    public void Inject_NoMergeDir_DoesNotThrowAndModDirStaysEmpty()
    {
        using var tmp = new TempDir();
        var modDir = Path.Combine(tmp.Path, "mod");
        var mapDir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);

        var assignments = new Dictionary<string, string>
        {
            ["30001800"] = SpeedFogIds.UntouchableSourceEntity.ToString(),
        };

        var captured = new StringWriter();
        var prev = Console.Out;
        Console.SetOut(captured);
        Exception? ex;
        try
        {
            ex = Record.Exception(() => UntouchableBossInjector.Inject(modDir, assignments, null, new[] { "m60_13_09_02" }));
        }
        finally
        {
            Console.SetOut(prev);
        }

        Assert.Null(ex);
        Assert.Empty(Directory.GetFiles(mapDir));
        Assert.Contains("not found in any map (phase slot?)", captured.ToString());
    }

    [Fact]
    public void Inject_FallbackMapAbsentFromMergeDir_LogsWarningAndModDirStaysEmpty()
    {
        using var tmp = new TempDir();
        var modDir = Path.Combine(tmp.Path, "mod");
        var mergeDir = Path.Combine(tmp.Path, "merge");
        var mapDir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);
        Directory.CreateDirectory(mergeDir); // no map/mapstudio/m60_13_09_02.msb.dcx inside

        var assignments = new Dictionary<string, string>
        {
            ["30001800"] = SpeedFogIds.UntouchableSourceEntity.ToString(),
        };

        var captured = new StringWriter();
        var prev = Console.Out;
        Console.SetOut(captured);
        Exception? ex;
        try
        {
            ex = Record.Exception(() => UntouchableBossInjector.Inject(modDir, assignments, mergeDir, new[] { "m60_13_09_02" }));
        }
        finally
        {
            Console.SetOut(prev);
        }

        Assert.Null(ex);
        Assert.Empty(Directory.GetFiles(mapDir));
        var output = captured.ToString();
        Assert.Contains("fallback map m60_13_09_02.msb.dcx not found in merge dir", output);
        Assert.Contains("not found in any map (phase slot?)", output);
    }

}
