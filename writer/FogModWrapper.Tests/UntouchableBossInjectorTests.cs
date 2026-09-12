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
        var (npc, sp) = BuildCoreParams();

        UntouchableBossInjector.Apply(npc, sp);

        var row = npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        Assert.Equal(UntouchableBossInjector.BOSS_HP, (uint)row["hp"].Value);
        Assert.Equal(UntouchableBossInjector.BOSS_RUNES, (uint)row["getSoul"].Value);
        Assert.Equal(UntouchableBossInjector.BOSS_TOUGHNESS, (uint)row["toughness"].Value);
        Assert.Equal(UntouchableBossInjector.BOSS_SUPER_ARMOR, (float)row["superArmorDurability"].Value);
        Assert.Equal(UntouchableBossInjector.BOSS_SUPER_ARMOR_RECOVER, (float)row["superArmorRecoverCorrection"].Value);
        // The partial wall is applied by the copied wall event, never resident
        // (a resident copy could not be cleared on the first parry).
        for (int i = 0; i < 32; i++)
            Assert.NotEqual(SpeedFogIds.UntouchableBossSpEffectRow, (int)row[$"spEffectID{i}"].Value);
    }

    [Fact]
    public void Apply_CustomSpEffectRowHasPartialCutRates()
    {
        var (npc, sp) = BuildCoreParams();

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
        var (npc, sp) = BuildCoreParams();
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
    public void ApplyParams_SkipsMoveset_WhenMovesetParamsUnavailable()
    {
        // Static assets present, but the regulation carries only the two
        // core params: core rows written, no moveset row, no think row.
        var (npc, sp) = BuildCoreParams();
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
        AddRowFromTemplate(sp, UntouchableBossInjector.BREAK_VFX_SPEFFECT);
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
            msb, new HashSet<uint> { 30001800 }, _ => { }, repointThink: false);

        Assert.Equal(1, repointed);
        Assert.Equal(new List<uint> { 30001800 }, ids);
        var boss = msb.Parts.Enemies.Single(e => e.EntityID == 30001800);
        Assert.Equal(SpeedFogIds.UntouchableBossNpcRow, boss.NPCParamID);
        Assert.Equal(52800000, boss.ThinkParamID); // moveset off: AI stays vanilla
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
            msb, new HashSet<uint> { 30001800 }, warnings.Add, repointThink: false);

        Assert.Equal(0, repointed);
        Assert.Empty(ids);
        Assert.Equal(35000030, msb.Parts.Enemies[0].NPCParamID);
        Assert.Contains(warnings, w => w.Contains("30001800"));
    }

    [Fact]
    public void ApplyToMsb_RepointsThinkParam_OnlyWhenMovesetApplied()
    {
        var msb = new MSBE();
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9000", ModelName = "c5280",
            EntityID = 30001800, NPCParamID = 52800086, ThinkParamID = 52800000,
        });
        // A halloween greeter (EntityID 0): keeps its own think row.
        msb.Parts.Enemies.Add(new MSBE.Part.Enemy
        {
            Name = "c5280_9001", ModelName = "c5280",
            EntityID = 0, NPCParamID = 52800086, ThinkParamID = SpeedFogIds.PassiveGreeterThinkRow,
        });
        var log = new List<string>();

        var (repointed, _) = UntouchableBossInjector.ApplyToMsb(
            msb, new HashSet<uint> { 30001800 }, log.Add, repointThink: true);

        Assert.Equal(1, repointed);
        var boss = msb.Parts.Enemies.Single(e => e.EntityID == 30001800);
        Assert.Equal(SpeedFogIds.UntouchableBossNpcRow, boss.NPCParamID);
        Assert.Equal(SpeedFogIds.UntouchableBossThinkRow, boss.ThinkParamID);
        Assert.Equal(SpeedFogIds.PassiveGreeterThinkRow,
            msb.Parts.Enemies.Single(e => e.Name == "c5280_9001").ThinkParamID);
        Assert.Contains(log, l => l.Contains("ThinkParamID"));
    }

    // Merge-dir fallback (docs/untouchable-boss.md "Two-phase injector", fallback arena maps):
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

        UntouchableBossInjector.Inject(modDir, assignments, mergeDir, new[] { "m60_13_09_02" }, repointThink: false);

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
            ex = Record.Exception(() => UntouchableBossInjector.Inject(modDir, assignments, null, new[] { "m60_13_09_02" }, repointThink: false));
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
            ex = Record.Exception(() => UntouchableBossInjector.Inject(modDir, assignments, mergeDir, new[] { "m60_13_09_02" }, repointThink: false));
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
        var (npc, sp) = BuildCoreParams();
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
        bullet.Rows[0]["spEffectIDForShooter"].Value = 1732002;

        // The lantern's ambient pulse bullets (judges 100-102): a VFX rider in
        // slot 0 and the madness buildup (26000) in slot 2, as in 1.17.
        foreach (var pulseId in new[] { 205280000, 205280001, 205280002 })
        {
            var pulse = AddRowFromTemplate(bullet, pulseId);
            pulse["atkId_Bullet"].Value = 5280000;
            pulse["spEffectId0"].Value = 20011456;
            pulse["spEffectId1"].Value = -1;
            pulse["spEffectId2"].Value = UntouchableBossInjector.LANTERN_MADNESS_SPEFFECT;
        }

        // Midra's Flame of Frenzy chain (root -> segment -> terminal ball),
        // every link carrying the madness rider 21730000, as in 1.17.
        foreach (var (id, child, life, radius) in new[]
        {
            (210730000, 210730005, 0.05f, 0.1f), (210730005, 210730006, 0.5f, 0.1f), (210730006, -1, 0.1f, 1.5f),
        })
        {
            var link = AddRowFromTemplate(bullet, id);
            link["atkId_Bullet"].Value = 210730000;
            link["sfxId_Bullet"].Value = 527062;
            link["sfxId_Hit"].Value = 527063;
            link["life"].Value = life;
            link["hitRadius"].Value = radius;
            link["HitBulletID"].Value = child;
            link["spEffectId0"].Value = 21730000;
            link["spEffectIDForShooter"].Value = -1;
            link["numShoot"].Value = (ushort)1;
            link["shootAngleInterval"].Value = (short)0;
        }

        var atk = BuildParamFromDef("AtkParam", templateId: 5280115, paramName: "AtkParam_Npc");
        atk.Rows[0]["atkMag"].Value = (ushort)100;
        atk.Rows[0]["throwTypeId"].Value = (ushort)0;
        return (npc, think, behavior, bullet, atk);
    }

    [Fact]
    public void ApplyMoveset_WithoutBossNpcClone_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        npc.Rows.RemoveAll(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(bullet.Rows, r => r.ID == SpeedFogIds.UntouchableBeamBulletRow);
        Assert.DoesNotContain(atk.Rows, r => r.ID == SpeedFogIds.UntouchableBeamAtkRow);
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
            (100, 1, SpeedFogIds.UntouchablePulseBulletBase), (101, 1, SpeedFogIds.UntouchablePulseBulletBase + 1),
            (102, 1, SpeedFogIds.UntouchablePulseBulletBase + 2),
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
        Assert.Equal(-1, (int)beam["spEffectIDForShooter"].Value);  // no caster-side madness rider
        Assert.Equal(73200, (int)bullet.Rows.Single(r => r.ID == 10732000)["atkId_Bullet"].Value);

        var dmg = atk.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBeamAtkRow);
        Assert.Equal(UntouchableBossInjector.BEAM_MAGIC, (ushort)dmg["atkMag"].Value);
        Assert.Equal((ushort)0, (ushort)dmg["throwTypeId"].Value);
        Assert.Equal((ushort)100, (ushort)atk.Rows.Single(r => r.ID == 5280115)["atkMag"].Value);
    }

    [Fact]
    public void ApplyMoveset_ClonesTheFlameChainWithoutMadnessAndFansTheRootOut()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();

        Assert.True(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        var variation = SpeedFogIds.UntouchableBossBehaviorVariation;
        var flame = behavior.Rows.Single(r => r.ID == SpeedFogIds.BehaviorRowId(variation, SpeedFogIds.UntouchableFlameJudge));
        Assert.Equal(variation, (int)flame["variationId"].Value);
        Assert.Equal(SpeedFogIds.UntouchableFlameJudge, (int)flame["behaviorJudgeId"].Value);
        Assert.Equal((byte)1, (byte)flame["refType"].Value);
        Assert.Equal(SpeedFogIds.UntouchableFlameBulletBase, (int)flame["refId"].Value);

        int root = SpeedFogIds.UntouchableFlameBulletBase;
        var links = new[] { root, root + 1, root + 2 }
            .Select(id => bullet.Rows.Single(r => r.ID == id)).ToList();
        // Re-chained onto the clones, the terminal ball still ends the chain.
        Assert.Equal(root + 1, (int)links[0]["HitBulletID"].Value);
        Assert.Equal(root + 2, (int)links[1]["HitBulletID"].Value);
        Assert.Equal(-1, (int)links[2]["HitBulletID"].Value);
        foreach (var link in links)
        {
            Assert.Equal(SpeedFogIds.UntouchableFlameAtkRow, (int)link["atkId_Bullet"].Value);
            Assert.Equal(527062, (int)link["sfxId_Bullet"].Value);      // visual kept
            for (int i = 0; i <= 4; i++)
                Assert.Equal(-1, (int)link[$"spEffectId{i}"].Value);   // madness gone
            Assert.Equal(-1, (int)link["spEffectIDForShooter"].Value);
        }
        Assert.Equal(1.5f, (float)links[2]["hitRadius"].Value);         // kinematics kept
        // The root fans out around the lantern; the links it spawns do not.
        Assert.Equal(UntouchableBossInjector.FLAME_DIRECTIONS, (ushort)links[0]["numShoot"].Value);
        Assert.Equal((short)90, (short)links[0]["shootAngleInterval"].Value); // a full circle over four directions
        Assert.Equal((ushort)1, (ushort)links[1]["numShoot"].Value);
        Assert.Equal((ushort)1, (ushort)links[2]["numShoot"].Value);
        // Midra's own rows untouched.
        var vanillaRoot = bullet.Rows.Single(r => r.ID == 210730000);
        Assert.Equal(21730000, (int)vanillaRoot["spEffectId0"].Value);
        Assert.Equal(210730005, (int)vanillaRoot["HitBulletID"].Value);
        Assert.Equal((ushort)1, (ushort)vanillaRoot["numShoot"].Value);

        var dmg = atk.Rows.Single(r => r.ID == SpeedFogIds.UntouchableFlameAtkRow);
        Assert.Equal(UntouchableBossInjector.FLAME_MAGIC, (ushort)dmg["atkMag"].Value);
        Assert.Equal((ushort)0, (ushort)dmg["throwTypeId"].Value);
    }

    [Theory]
    // A fourth link, and a cycle back to the root: both are chains the clone
    // band cannot hold, and a truncated clone would be a partial moveset.
    [InlineData(755000000)]
    [InlineData(210730000)]
    public void ApplyMoveset_FlameChainLongerThanTheCloneBand_WritesNothing(int fourthLink)
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        bullet.Rows.Single(r => r.ID == 210730006)["HitBulletID"].Value = fourthLink;
        if (fourthLink != 210730000)
            AddRowFromTemplate(bullet, fourthLink)["HitBulletID"].Value = -1;

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(bullet.Rows, r => r.ID == SpeedFogIds.UntouchableFlameBulletBase);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(atk.Rows, r => r.ID == SpeedFogIds.UntouchableFlameAtkRow);
    }

    [Fact]
    public void ApplyMoveset_MissingFlameChainLink_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        bullet.Rows.RemoveAll(r => r.ID == 210730005); // the chain's middle link gone

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(bullet.Rows, r => r.ID == SpeedFogIds.UntouchableFlameBulletBase);
        Assert.DoesNotContain(atk.Rows, r => r.ID == SpeedFogIds.UntouchableFlameAtkRow);
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
    public void ApplyMoveset_ExtraVanillaJudge_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        SetBehavior(AddRowFromTemplate(behavior, 252800120), 52800, 120, 0, 5280120);

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
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

    // --- Partial wall, nerflantern slot, hit reactions, lantern madness ---

    /// <summary>SpEffectParam's twelve damage-level replacement fields.</summary>
    private static readonly string[] DamageLevelFields =
    {
        "dmgLv_None", "dmgLv_S", "dmgLv_M", "dmgLv_L", "dmgLv_BlowM", "dmgLv_Push",
        "dmgLv_Strike", "dmgLv_BlowS", "dmgLv_Min", "dmgLv_Uppercut", "dmgLv_BlowLL", "dmgLv_Breath",
    };

    /// <summary>The two core params as Apply reads them: the vanilla NpcParam
    /// row, the parry-window SpEffect template, the break VFX template and
    /// vanilla's boss damage-level table.</summary>
    private static (PARAM npc, PARAM sp) BuildCoreParams()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        AddRowFromTemplate(sp, UntouchableBossInjector.BREAK_VFX_SPEFFECT);
        // Vanilla's boss damage-level table (Jori, Godrick, Margit...).
        var table = AddRowFromTemplate(sp, UntouchableBossInjector.NO_FLINCH_TEMPLATE_SPEFFECT);
        foreach (var field in DamageLevelFields)
            table[field].Value = (sbyte)1;
        table["spCategory"].Value = (ushort)1001;
        table["categoryPriority"].Value = (byte)200;
        table["effectEndurance"].Value = -1f;
        return (npc, sp);
    }

    [Fact]
    public void Apply_KeepsTheCutInTheParryWindowCategory()
    {
        var (npc, sp) = BuildCoreParams();
        sp.Rows[0]["spCategory"].Value = (ushort)1001; // vanilla 20011471's category

        UntouchableBossInjector.Apply(npc, sp);

        // Same category as the wall it replaces and as the parry-window
        // effect: the window overrides it during a parry, as in vanilla.
        Assert.Equal((ushort)1001,
            (ushort)sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossSpEffectRow)["spCategory"].Value);
    }

    [Fact]
    public void Apply_WritesThePermanentBrokenRowFromTheBreakVfx()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        var vfx = AddRowFromTemplate(sp, UntouchableBossInjector.BREAK_VFX_SPEFFECT);
        vfx["effectEndurance"].Value = 1f;
        vfx["vfxId"].Value = 20050460;
        vfx["spCategory"].Value = (ushort)0;

        UntouchableBossInjector.Apply(npc, sp);

        var cut = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossSpEffectRow);
        var broken = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBrokenSpEffectRow);
        foreach (var field in new[] { "slashDamageCutRate", "magicDamageCutRate", "darkDamageCutRate" })
        {
            // x4 between the two written rows, as requested.
            Assert.Equal(4f, (float)broken[field].Value / (float)cut[field].Value);
        }
        Assert.Equal((ushort)0, (ushort)broken["spCategory"].Value); // coexists with later parry windows
        Assert.Equal(-1f, (float)broken["effectEndurance"].Value);
        Assert.Equal(-1, (int)broken["vfxId"].Value);
        Assert.Equal(1f, (float)vfx["effectEndurance"].Value); // vanilla flash untouched
    }

    [Fact]
    public void Apply_DropsNerflanternsWallLiftFromTheBossClone()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        // What the merged regulation looks like: nerflantern wrote 20011471
        // into the last free slot of the vanilla row.
        npc.Rows[0]["spEffectID18"].Value = 20011473;
        npc.Rows[0]["spEffectID31"].Value = UntouchableBossInjector.PARRY_WINDOW_SPEFFECT;
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        AddRowFromTemplate(sp, UntouchableBossInjector.BREAK_VFX_SPEFFECT);

        UntouchableBossInjector.Apply(npc, sp);

        var boss = npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        for (int i = 0; i < 32; i++)
            Assert.NotEqual(UntouchableBossInjector.PARRY_WINDOW_SPEFFECT, (int)boss[$"spEffectID{i}"].Value);
        Assert.Equal(-1, (int)boss["spEffectID31"].Value);
        Assert.Equal(20011473, (int)boss["spEffectID18"].Value); // stateInfo 420 row stays
        // The vanilla row (ambient untouchables) keeps nerflantern's slot.
        Assert.Equal(UntouchableBossInjector.PARRY_WINDOW_SPEFFECT,
            (int)npc.Rows.Single(r => r.ID == 52800086)["spEffectID31"].Value);
    }

    [Fact]
    public void Apply_FoldsVanillasBossDamageLevelTableIntoTheCutRow()
    {
        var (npc, sp) = BuildCoreParams();
        npc.Rows[0]["spEffectID1"].Value = 0; // vanilla 52800086: row 0, the no-op effect
        var table = sp.Rows.Single(r => r.ID == UntouchableBossInjector.NO_FLINCH_TEMPLATE_SPEFFECT);
        table["dmgLv_Breath"].Value = (sbyte)2; // copied, not hardcoded

        UntouchableBossInjector.Apply(npc, sp);

        // The cut row takes the vanilla wall's hit-reaction shape (category
        // 1001, priority 0, every incoming damage level replaced by None),
        // with the cut rates raised: what keeps an immune untouchable from
        // flinching keeps the boss from flinching until the wall breaks.
        var cut = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossSpEffectRow);
        Assert.Equal((sbyte)1, (sbyte)cut["dmgLv_S"].Value);
        foreach (var field in DamageLevelFields)
            Assert.Equal((sbyte)table[field].Value, (sbyte)cut[field].Value);
        // The broken boss flinches again, as a vanilla untouchable does once
        // its wall is gone (the broken row must stay in category 0).
        var broken = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBrokenSpEffectRow);
        foreach (var field in DamageLevelFields)
            Assert.Equal((sbyte)0, (sbyte)broken[field].Value);
        // No resident row: a category-0 table went unheeded in game (2026-09-07).
        Assert.Equal(
            new[] { 20011471, UntouchableBossInjector.BREAK_VFX_SPEFFECT, UntouchableBossInjector.NO_FLINCH_TEMPLATE_SPEFFECT,
                    SpeedFogIds.UntouchableBossSpEffectRow, SpeedFogIds.UntouchableBrokenSpEffectRow }.OrderBy(id => id),
            sp.Rows.Select(r => r.ID).OrderBy(id => id));
        var boss = npc.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        Assert.Equal(0, (int)boss["spEffectID1"].Value);
        // Vanilla's own table row is untouched (ambient untouchables and every 5300 carrier).
        Assert.Equal((ushort)1001, (ushort)table["spCategory"].Value);
        Assert.Equal((sbyte)2, (sbyte)table["dmgLv_Breath"].Value);
    }

    [Fact]
    public void Apply_WithoutTheBossDamageLevelTable_LeavesTheCutRowWithoutIt()
    {
        var npc = BuildParamFromDef("NpcParam", templateId: 52800086);
        var sp = BuildParamFromDef("SpEffect", templateId: 20011471, paramName: "SpEffectParam");
        AddRowFromTemplate(sp, UntouchableBossInjector.BREAK_VFX_SPEFFECT);

        UntouchableBossInjector.Apply(npc, sp);

        var cut = sp.Rows.Single(r => r.ID == SpeedFogIds.UntouchableBossSpEffectRow);
        foreach (var field in DamageLevelFields)
            Assert.Equal((sbyte)0, (sbyte)cut[field].Value); // 20011471's own zeros
        Assert.Equal(UntouchableBossInjector.DAMAGE_CUT, (float)cut["slashDamageCutRate"].Value);
        Assert.Contains(npc.Rows, r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
    }

    [Fact]
    public void ApplyMoveset_LanternPulsesLoseTheirMadnessForTheBoss()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();

        Assert.True(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        var variation = SpeedFogIds.UntouchableBossBehaviorVariation;
        foreach (var (judge, vanillaBullet, i) in new[] { (100, 205280000, 0), (101, 205280001, 1), (102, 205280002, 2) })
        {
            var cloneId = SpeedFogIds.UntouchablePulseBulletBase + i;
            var row = behavior.Rows.Single(r => r.ID == SpeedFogIds.BehaviorRowId(variation, judge));
            Assert.Equal(cloneId, (int)row["refId"].Value);
            var clone = bullet.Rows.Single(r => r.ID == cloneId);
            Assert.Equal(5280000, (int)clone["atkId_Bullet"].Value);
            Assert.Equal(20011456, (int)clone["spEffectId0"].Value); // lantern VFX kept
            Assert.Equal(-1, (int)clone["spEffectId2"].Value);       // madness gone
            // The vanilla bullet (ambient untouchables) keeps its madness.
            Assert.Equal(UntouchableBossInjector.LANTERN_MADNESS_SPEFFECT,
                (int)bullet.Rows.Single(r => r.ID == vanillaBullet)["spEffectId2"].Value);
        }
        Assert.Equal(5280110, (int)behavior.Rows.Single(r => r.ID == SpeedFogIds.BehaviorRowId(variation, 110))["refId"].Value);
    }

    [Fact]
    public void ApplyMoveset_MissingPulseBulletTemplate_WritesNothing()
    {
        var (npc, think, behavior, bullet, atk) = BuildMovesetParams();
        bullet.Rows.RemoveAll(r => r.ID == 205280001);

        Assert.False(UntouchableBossInjector.ApplyMoveset(npc, think, behavior, bullet, atk));

        Assert.DoesNotContain(think.Rows, r => r.ID == SpeedFogIds.UntouchableBossThinkRow);
        Assert.DoesNotContain(bullet.Rows, r => r.ID >= SpeedFogIds.UntouchablePulseBulletBase && r.ID < SpeedFogIds.UntouchablePulseBulletBase + 3);
        Assert.DoesNotContain(behavior.Rows, r => (int)r["variationId"].Value == SpeedFogIds.UntouchableBossBehaviorVariation);
    }

    // The wall event the enemy randomizer copies next to a placed boss, as
    // seen in 1.17 (vanilla ids 1700783 etc.): entity parameterized at
    // offset 0 of each instruction, SpEffect/enable literals at offset 4.
    private static EMEVD.Instruction Instr(int bank, int id, int a, int b)
    {
        var bytes = new byte[8];
        BitConverter.GetBytes(a).CopyTo(bytes, 0);
        BitConverter.GetBytes(b).CopyTo(bytes, 4);
        return new EMEVD.Instruction(bank, id, bytes);
    }

    private static EMEVD MakeWallEmevd(uint boss, long wallEventId = 1700783)
    {
        var emevd = new EMEVD();
        var init = new EMEVD.Event(0);
        init.Instructions.Add(EmevdHelper.InitializeEvent((int)wallEventId, (int)boss));
        init.Instructions.Add(EmevdHelper.InitializeEvent(1700764, (int)boss, 4000396)); // teleport sibling
        init.Instructions.Add(EmevdHelper.InitializeEvent(1700999, 99999999)); // another enemy's copy
        init.Instructions.Add(EmevdHelper.InitializeEvent(1700555, (int)boss)); // SpEffect slot parameterized
        emevd.Events.Add(init);
        // Teleport sibling: hides the bar mid-warp, no wall.
        var teleport = new EMEVD.Event(1700764);
        teleport.Instructions.Add(Instr(2004, 30, 0, 0));
        teleport.Instructions.Add(Instr(2004, 30, 0, 1));
        teleport.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        teleport.Parameters.Add(new EMEVD.Parameter(1, 0, 0, 4));
        emevd.Events.Add(teleport);
        // A boss event whose SpEffect id itself is a parameter: not ours to rewrite.
        var parameterized = new EMEVD.Event(1700555);
        parameterized.Instructions.Add(Instr(2004, 8, 0, UntouchableBossInjector.VANILLA_WALL_SPEFFECT));
        parameterized.Parameters.Add(new EMEVD.Parameter(0, 4, 0, 4));
        emevd.Events.Add(parameterized);
        foreach (var id in new[] { wallEventId, 1700999L })
        {
            var evt = new EMEVD.Event(id);
            evt.Instructions.Add(Instr(2004, 8, 0, UntouchableBossInjector.VANILLA_WALL_SPEFFECT));  // wall at spawn
            evt.Instructions.Add(Instr(2004, 8, 0, 19690));
            evt.Instructions.Add(Instr(2004, 30, 0, 0));                                           // HP bar hidden
            evt.Instructions.Add(new EMEVD.Instruction(4, 5, new byte[20]));                       // IfCharacterHasSpEffect
            evt.Instructions.Add(Instr(2004, 21, 0, UntouchableBossInjector.VANILLA_WALL_SPEFFECT)); // wall cleared on parry
            evt.Instructions.Add(Instr(2004, 30, 0, 1));                                           // HP bar shown
            evt.Instructions.Add(Instr(2004, 8, 0, 20011472));
            // Entity slots are parameterized (offset 0), SpEffect ids are literals.
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                if (evt.Instructions[i].Bank == 2004)
                    evt.Parameters.Add(new EMEVD.Parameter(i, 0, 0, 4));
            }
            emevd.Events.Add(evt);
        }
        return emevd;
    }

    private static int SpEffectOf(EMEVD.Instruction ins) => BitConverter.ToInt32(ins.ArgData, 4);

    [Fact]
    public void PatchWallEvents_SwapsTheWallForTheCutAndShowsTheBarOnlyInTheBossEvents()
    {
        var emevd = MakeWallEmevd(31100800);
        var log = new List<string>();

        var rewritten = UntouchableBossInjector.PatchWallEvents(emevd, 31100800, log.Add);

        Assert.Equal(5, rewritten); // 2 wall swaps + the spawn-time bar + the teleport sibling's bar + the broken rider
        var bossEvent = emevd.Events.Single(e => e.ID == 1700783);
        var boss = bossEvent.Instructions;
        Assert.Equal(8, boss.Count);
        Assert.Equal(SpeedFogIds.UntouchableBossSpEffectRow, SpEffectOf(boss[0]));
        Assert.Equal(19690, SpEffectOf(boss[1]));
        Assert.Equal(1, boss[2].ArgData[4]);
        Assert.Equal(SpeedFogIds.UntouchableBossSpEffectRow, SpEffectOf(boss[4]));
        Assert.Equal(1, boss[5].ArgData[4]);
        Assert.Equal(UntouchableBossInjector.BREAK_VFX_SPEFFECT, SpEffectOf(boss[6]));
        // The broken rider follows the break VFX, entity slot parameterized like it.
        Assert.Equal((2004, 8), (boss[7].Bank, boss[7].ID));
        Assert.Equal(SpeedFogIds.UntouchableBrokenSpEffectRow, SpEffectOf(boss[7]));
        Assert.Contains(bossEvent.Parameters, prm => prm.InstructionIndex == 7 && prm.TargetStartByte == 0 && prm.ByteCount == 4);
        Assert.Equal(7, bossEvent.Parameters.Count(prm => prm.TargetStartByte == 0)); // 6 vanilla 2004 slots + the rider
        // The teleport sibling no longer hides the bar; its re-enable stays.
        var teleport = emevd.Events.Single(e => e.ID == 1700764).Instructions;
        Assert.Equal(1, teleport[0].ArgData[4]);
        Assert.Equal(1, teleport[1].ArgData[4]);
        // A parameterized SpEffect slot is the runtime's, left alone.
        Assert.Equal(UntouchableBossInjector.VANILLA_WALL_SPEFFECT,
            SpEffectOf(emevd.Events.Single(e => e.ID == 1700555).Instructions[0]));
        // Another placed enemy's copy of the same event shape stays vanilla.
        var other = emevd.Events.Single(e => e.ID == 1700999).Instructions;
        Assert.Equal(UntouchableBossInjector.VANILLA_WALL_SPEFFECT, SpEffectOf(other[0]));
        Assert.Equal(0, other[2].ArgData[4]);
        Assert.Contains(log, l => l.Contains("31100800") && l.Contains("2 wall swap") && l.Contains("2 HP bar flip") && l.Contains("1 broken rider"));
    }

    [Fact]
    public void PatchWallEvents_NoEventForTheBoss_WarnsAndChangesNothing()
    {
        var emevd = MakeWallEmevd(31100800);
        var log = new List<string>();

        Assert.Equal(0, UntouchableBossInjector.PatchWallEvents(emevd, 30001800, log.Add));

        Assert.Equal(UntouchableBossInjector.VANILLA_WALL_SPEFFECT,
            SpEffectOf(emevd.Events.Single(e => e.ID == 1700783).Instructions[0]));
        Assert.Contains(log, l => l.Contains("Warning") && l.Contains("30001800"));
    }

    [Fact]
    public void PatchWallEvents_NoWallSwap_LeavesTheBossEventsUntouched()
    {
        // A boss whose only copied event hides the bar (no wall event): the
        // flip must not survive, since another boss of the same map may still
        // write the EMEVD.
        var emevd = MakeWallEmevd(31100800);
        var init = emevd.Events.Single(e => e.ID == 0);
        init.Instructions.Add(EmevdHelper.InitializeEvent(1700888, 30001800));
        var barOnly = new EMEVD.Event(1700888);
        barOnly.Instructions.Add(Instr(2004, 30, 0, 0));
        barOnly.Instructions.Add(Instr(2004, 8, 0, UntouchableBossInjector.BREAK_VFX_SPEFFECT));
        barOnly.Parameters.Add(new EMEVD.Parameter(0, 0, 0, 4));
        barOnly.Parameters.Add(new EMEVD.Parameter(1, 0, 0, 4));
        emevd.Events.Add(barOnly);

        Assert.Equal(0, UntouchableBossInjector.PatchWallEvents(emevd, 30001800, _ => { }));
        Assert.Equal(0, barOnly.Instructions[0].ArgData[4]);
        Assert.Equal(2, barOnly.Instructions.Count); // no rider inserted
        Assert.Equal(2, barOnly.Parameters.Count);
        Assert.Equal(5, UntouchableBossInjector.PatchWallEvents(emevd, 31100800, _ => { })); // the other boss is unaffected
    }

    [Fact]
    public void InjectWallPatch_RewritesTheMapEmevdAndSkipsMapsWithoutOne()
    {
        using var mod = new TempDir();
        Directory.CreateDirectory(Path.Combine(mod.Path, "event"));
        var path = Path.Combine(mod.Path, "event", "m31_10_00_00.emevd.dcx");
        MakeWallEmevd(31100800).Write(path);
        var log = new List<string>();

        var patched = UntouchableBossInjector.InjectWallPatch(mod.Path, "m31_10_00_00.msb.dcx", new[] { 31100800u }, log.Add);
        var skipped = UntouchableBossInjector.InjectWallPatch(mod.Path, "m60_13_09_02.msb.dcx", new[] { 30001800u }, log.Add);

        Assert.Equal(5, patched);
        Assert.Equal(0, skipped);
        var written = EMEVD.Read(path);
        Assert.Equal(SpeedFogIds.UntouchableBossSpEffectRow,
            SpEffectOf(written.Events.Single(e => e.ID == 1700783).Instructions[0]));
        Assert.Contains(log, l => l.Contains("m60_13_09_02.emevd.dcx not in the mod dir"));
    }

    [Fact]
    public void InjectWallPatch_CopiesTheFallbackArenaEmevdFromTheMergeDir()
    {
        using var mod = new TempDir();
        using var merge = new TempDir();
        Directory.CreateDirectory(Path.Combine(merge.Path, "event"));
        MakeWallEmevd(30001800).Write(Path.Combine(merge.Path, "event", "m60_13_09_02.emevd.dcx"));
        var log = new List<string>();

        var patched = UntouchableBossInjector.InjectWallPatch(
            mod.Path, "m60_13_09_02.msb.dcx", new[] { 30001800u }, log.Add, merge.Path);

        Assert.Equal(5, patched);
        var shipped = Path.Combine(mod.Path, "event", "m60_13_09_02.emevd.dcx");
        Assert.True(File.Exists(shipped));
        Assert.Equal(SpeedFogIds.UntouchableBossSpEffectRow,
            SpEffectOf(EMEVD.Read(shipped).Events.Single(e => e.ID == 1700783).Instructions[0]));
        Assert.Contains(log, l => l.Contains("copied from the merge dir"));
    }
}
