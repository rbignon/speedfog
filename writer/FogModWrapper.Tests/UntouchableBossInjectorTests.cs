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
    public void ApplyParams_ReturnsFalse_WhenParamsUnavailable()
    {
        // Empty BND4: GetParam("NpcParam") warn-returns null before ever
        // touching the defs dir (RegulationEditorTests' fixture pattern).
        // Program.cs must see Core == false to skip the MSB repoint phase
        // instead of pointing placed parts at a row that was never written
        // (see docs/untouchable-boss.md "Two-phase injector").
        using var data = new TempDir();
        var editor = new RegulationEditor(new BND4(), Path.GetTempPath());

        var result = UntouchableBossInjector.ApplyParams(editor, data.Path);

        Assert.False(result.Core);
        Assert.False(result.Moveset);
    }

    [Fact]
    public void ApplyParams_ReturnsTrue_WhenBothParamsAvailable()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/NpcParam.param", npc.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/SpEffectParam.param", sp.Write()));
        var editor = new RegulationEditor(bnd, DefsDir());
        using var data = new TempDir(); // no static assets: moveset off, core on

        var result = UntouchableBossInjector.ApplyParams(editor, data.Path);

        Assert.True(result.Core);
        Assert.False(result.Moveset);
        var row = editor.GetParam("NpcParam")!.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        Assert.Equal(UntouchableBossInjector.BOSS_HP, (uint)row["hp"].Value);
    }

    private static void TouchStaticAssets(string dataDir)
    {
        foreach (var rel in UntouchableBossInjector.MovesetStaticAssets)
        {
            var path = Path.Combine(dataDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, new byte[] { 0 });
        }
    }

    [Fact]
    public void MovesetStaticAssets_AreTheAnibndAndTheBattleScript()
    {
        Assert.Contains(Path.Combine("mods", "speedfog", "chr", "c5280.anibnd.dcx"),
            UntouchableBossInjector.MovesetStaticAssets);
        Assert.Contains(Path.Combine("mods", "speedfog", "script", "755890_battle.luabnd.dcx"),
            UntouchableBossInjector.MovesetStaticAssets);
    }

    [Fact]
    public void ApplyParams_SkipsMoveset_WhenMovesetParamsUnavailable()
    {
        // Static assets present, but the regulation carries only the two
        // core params: core rows written, no moveset row, no think row.
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/NpcParam.param", npc.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/SpEffectParam.param", sp.Write()));
        var editor = new RegulationEditor(bnd, DefsDir());
        using var data = new TempDir();
        TouchStaticAssets(data.Path);

        var result = UntouchableBossInjector.ApplyParams(editor, data.Path);

        Assert.True(result.Core);
        Assert.False(result.Moveset);
        Assert.Equal(0, (int)editor.GetParam("NpcParam")!.Rows
            .Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow)["behaviorVariationId"].Value);
    }

    [Fact]
    public void ApplyParams_AppliesMoveset_WhenAssetsAndParamsArePresent()
    {
        var (_, think, behavior, bullet, atk) = BuildMovesetParams();
        // BuildMovesetParams already ran Apply on npc; feed ApplyParams a
        // fresh NpcParam so the core clone is written by ApplyParams itself.
        var freshNpc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        var bnd = new BND4();
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/NpcParam.param", freshNpc.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/SpEffectParam.param", sp.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/NpcThinkParam.param", think.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/BehaviorParam.param", behavior.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/Bullet.param", bullet.Write()));
        bnd.Files.Add(new BinderFile(Binder.FileFlags.Flag1, 0, "N:/GR/data/Param/GameParam/AtkParam_Npc.param", atk.Write()));
        var editor = new RegulationEditor(bnd, DefsDir());
        using var data = new TempDir();
        TouchStaticAssets(data.Path);

        var result = UntouchableBossInjector.ApplyParams(editor, data.Path);

        Assert.True(result.Core);
        Assert.True(result.Moveset);
        Assert.Contains(editor.GetParam("NpcThinkParam")!.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.Contains(editor.GetParam("Bullet", "BulletParam")!.Rows, r => r.ID == SpeedFogIds.UntouchableBeamBulletRow);
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

    // --- Moveset (docs/untouchable-boss.md "Moveset") ---

    private static void SetBehavior(PARAM.Row row, int variation, int judge, int refType, int refId)
    {
        row["variationId"].Value = variation;       // s32
        row["behaviorJudgeId"].Value = judge;       // s32
        row["refType"].Value = (byte)refType;       // u8
        row["refId"].Value = refId;                 // s32
    }

    /// <summary>The five PARAMs ApplyMoveset reads, shaped like 1.17: the
    /// boss NpcParam clone already written by Apply, vanilla c5280's think
    /// row, its nine BehaviorParam rows (including the non-formula row 1170
    /// for judge 500), Frenzied Burst's bullet and the lantern swing's
    /// attack row.</summary>
    private static (PARAM npc, PARAM think, PARAM behavior, PARAM bullet, PARAM atk) BuildMovesetParams()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        UntouchableBossInjector.Apply(npc, sp);
        npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow)["behaviorVariationId"].Value = 52800;

        var think = BuildParamFromDef("NpcThinkParam", templateId: 52800000);
        think.Rows[0]["logicId"].Value = 528000;
        think.Rows[0]["battleGoalID"].Value = 528000;

        var behavior = BuildParamFromDef("BehaviorParam", templateId: 252800100);
        SetBehavior(behavior.Rows[0], 52800, 100, 1, 205280000);
        foreach (var (id, judge, refType, refId) in new[]
        {
            (252800101, 101, 1, 205280001), (252800102, 102, 1, 205280002),
            (252800110, 110, 0, 5280110), (252800111, 111, 0, 5280111),
            (252800112, 112, 1, 205280005), (252800113, 113, 0, 5280113),
            (252800115, 115, 0, 5280115), (1170, 500, 0, 5280001),
        })
        {
            SetBehavior(AddRowFromTemplate(behavior, id), 52800, judge, refType, refId);
        }

        var bullet = BuildParamFromDef("BulletParam", templateId: 10732000, paramName: "Bullet");
        bullet.Rows[0]["atkId_Bullet"].Value = 73200;
        bullet.Rows[0]["sfxId_Bullet"].Value = 527032;
        bullet.Rows[0]["sfxId_Hit"].Value = 527033;
        bullet.Rows[0]["life"].Value = 0.5f;
        bullet.Rows[0]["initVellocity"].Value = 100f;
        bullet.Rows[0]["spEffectId0"].Value = 12345;

        var atk = BuildParamFromDef("AtkParam", templateId: 5280115, paramName: "AtkParam_Npc");
        atk.Rows[0]["atkMag"].Value = (ushort)100;
        atk.Rows[0]["throwTypeId"].Value = (ushort)0;
        return (npc, think, behavior, bullet, atk);
    }

    [Fact]
    public void ApplyMoveset_ThinkRowSelectsTheBossBattleScript()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();

        Assert.True(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        var row = think.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.Equal(SpeedFogIds.UntouchableBossBattleGoal, (int)row["battleGoalID"].Value);
        Assert.Equal(528000, (int)row["logicId"].Value); // shared logic script stays vanilla
        Assert.Equal(528000, (int)think.Rows.Single(r => r.ID == 52800000)["battleGoalID"].Value);
    }

    [Fact]
    public void ApplyMoveset_ReKeysVanillaBehaviorRowsUnderTheBossVariation()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();

        UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk);

        var variation = SpeedFogIds.UntouchableBossBehaviorVariation;
        Assert.Equal(variation,
            (int)npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow)["behaviorVariationId"].Value);
        foreach (var (judge, refType, refId) in new[]
        {
            (100, 1, 205280000), (101, 1, 205280001), (102, 1, 205280002),
            (110, 0, 5280110), (111, 0, 5280111), (112, 1, 205280005),
            (113, 0, 5280113), (115, 0, 5280115), (500, 0, 5280001),
        })
        {
            var clone = behavior.Rows.Single(r => r.ID == SpeedFogIds.BehaviorRowId(variation, judge));
            Assert.Equal(variation, (int)clone["variationId"].Value);
            Assert.Equal(judge, (int)clone["behaviorJudgeId"].Value);
            Assert.Equal((byte)refType, (byte)clone["refType"].Value);
            Assert.Equal(refId, (int)clone["refId"].Value);
        }
        var beam = behavior.Rows.Single(r => r.ID == SpeedFogIds.BehaviorRowId(variation, SpeedFogIds.UntouchableBeamJudge));
        Assert.Equal(SpeedFogIds.UntouchableBeamJudge, (int)beam["behaviorJudgeId"].Value);
        Assert.Equal((byte)1, (byte)beam["refType"].Value);
        Assert.Equal(SpeedFogIds.UntouchableBeamBulletRow, (int)beam["refId"].Value);
        // Vanilla rows untouched: still variation 52800, still nine of them.
        Assert.Equal(9, behavior.Rows.Count(r => (int)r["variationId"].Value == 52800));
    }

    [Fact]
    public void ApplyMoveset_ClonesTheBeamBulletAndItsDamageRow()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();

        UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk);

        var beam = bullet.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBeamBulletRow);
        Assert.Equal(SpeedFogIds.UntouchableBeamAtkRow, (int)beam["atkId_Bullet"].Value);
        Assert.Equal(527032, (int)beam["sfxId_Bullet"].Value);   // visual kept
        Assert.Equal(0.5f, (float)beam["life"].Value);           // kinematics kept
        Assert.Equal(100f, (float)beam["initVellocity"].Value);
        for (int i = 0; i <= 4; i++)
            Assert.Equal(-1, (int)beam[$"spEffectId{i}"].Value);  // no madness, nothing
        Assert.Equal(73200, (int)bullet.Rows.Single(r => r.ID == 10732000)["atkId_Bullet"].Value);

        var dmg = atk.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBeamAtkRow);
        Assert.Equal(UntouchableBossInjector.BEAM_MAGIC, (ushort)dmg["atkMag"].Value);
        Assert.Equal((ushort)0, (ushort)dmg["throwTypeId"].Value);
        Assert.Equal((ushort)100, (ushort)atk.Rows.Single(r => r.ID == 5280115)["atkMag"].Value);
    }

    [Fact]
    public void ApplyMoveset_MissingVanillaJudge_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        behavior.Rows.RemoveAll(r => r.ID == 1170); // judge 500 gone (game patch drift)

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.Equal(52800,
            (int)npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow)["behaviorVariationId"].Value);
        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(bullet.Rows, r => r.ID == SpeedFogIds.UntouchableBeamBulletRow);
        Assert.DoesNotContain(atk.Rows, r => r.ID == SpeedFogIds.UntouchableBeamAtkRow);
    }

    [Fact]
    public void ApplyMoveset_MissingTemplateRow_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        bullet.Rows.RemoveAll(r => r.ID == 10732000); // Frenzied Burst gone

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(atk.Rows, r => r.ID == SpeedFogIds.UntouchableBeamAtkRow);
    }
}
