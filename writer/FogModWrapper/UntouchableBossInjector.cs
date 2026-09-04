using FogMod;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Promotes enemy-randomizer-placed Aging Untouchables (source entity
/// 2049420200, allowlist-only minor boss) to their boss form: a cloned
/// NpcParam row outside the nerflantern-patched 5280 band, carrying a
/// custom SpEffect that lifts the parry wall (stateInfo 121, like
/// nerflantern) but keeps the boss heavily resistant via partial damage
/// cut rates. A successful parry still opens the vanilla full-damage
/// window, and, when the static assets are built, its own battle AI and
/// a frenzy beam (see docs/untouchable-boss.md "Moveset").
/// </summary>
public static class UntouchableBossInjector
{
    public const int UNTOUCHABLE_VANILLA_NPC = 52800086;
    private const int WALL_LIFT_TEMPLATE_SPEFFECT = 20011471;

    // Initial values for the in-game tuning session (docs/untouchable-boss.md).
    public const uint BOSS_HP = 3000;
    public const uint BOSS_RUNES = 20000;
    public const float DAMAGE_CUT = 0.35f; // fraction of damage taken (65% cut)

    // --- Moveset (docs/untouchable-boss.md "Moveset") ---

    public const int UNTOUCHABLE_VANILLA_THINK = 52800000;
    public const int UNTOUCHABLE_VANILLA_VARIATION = 52800;

    /// <summary>Frenzied Burst's bullet: a 100 m/s, 0.5 s hitscan laser
    /// whose SFX (527032/527033) live in the common bundle.</summary>
    public const int BEAM_TEMPLATE_BULLET = 10732000;

    /// <summary>The lantern swing's attack row (magic, no throw): the damage
    /// template for the beam (player spells only have AtkParam_Pc rows).</summary>
    public const int BEAM_TEMPLATE_ATK = 5280115;

    /// <summary>Beam damage (AtkParam_Npc.atkMag, u16). Tuning knob.</summary>
    public const ushort BEAM_MAGIC = 110;

    /// <summary>Vanilla c5280 BehaviorParam judge ids (variation 52800),
    /// re-keyed under the boss variation so every attack/bullet the TAE fires
    /// still resolves. 500 is the non-formula row 1170. Refresh after a game
    /// patch that renumbers c5280's judges.</summary>
    public static readonly int[] VanillaJudges = { 100, 101, 102, 110, 111, 112, 113, 115, 500 };

    // No boss resize: an MSB Part.Scale experiment (1.3x and higher)
    // confirmed in-game that the engine ignores the field for chr parts,
    // and no other offline size mechanism exists (docs/untouchable-boss.md,
    // "Size: settled").

    public static bool IsBossPlaced(Dictionary<string, string> enemyAssignments)
        => enemyAssignments.ContainsValue(
            SpeedFogIds.UntouchableSourceEntity.ToString());

    /// <summary>Outcome of the regulation phase. <c>Core</c>: the boss
    /// NpcParam/SpEffect rows were written (today's boss). <c>Moveset</c>:
    /// the think row, behavior variation, beam bullet and damage row were
    /// written too. Callers must not run the MSB repoint when Core is
    /// false, and must not repoint ThinkParamID when Moveset is false.</summary>
    public readonly record struct ParamResult(bool Core, bool Moveset);

    /// <summary>Static mod files the moveset depends on, relative to the
    /// SpeedFog data dir (built by tools/bootstrap.py: StaticModBuilder's
    /// UntouchableTaePatcher and the WitchyBND repack of
    /// data/mods-src/speedfog/script/). A think row pointing at a missing
    /// luabnd would leave the boss without battle AI, so their absence
    /// disables the whole moveset.</summary>
    public static readonly string[] MovesetStaticAssets =
    {
        Path.Combine("mods", "speedfog", "chr", "c5280.anibnd.dcx"),
        Path.Combine("mods", "speedfog", "script", $"{SpeedFogIds.UntouchableBossBattleGoal}_battle.luabnd.dcx"),
    };

    /// <summary>Regulation phase. Core rows first (as before); then, only if
    /// both static assets exist under <paramref name="dataDir"/> and the
    /// four moveset params are available, the moveset rows
    /// (<see cref="ApplyMoveset"/>). Every skip prints one warning line.</summary>
    public static ParamResult ApplyParams(RegulationEditor reg, string dataDir)
    {
        var npc = reg.GetParam("NpcParam");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (npc == null || sp == null)
        {
            Console.WriteLine(
                "Untouchable boss: NpcParam/SpEffectParam unavailable, boss keeps vanilla stats");
            return new ParamResult(false, false);
        }
        Apply(npc, sp);

        var missingAssets = MovesetStaticAssets
            .Where(rel => !File.Exists(Path.Combine(dataDir, rel)))
            .ToList();
        if (missingAssets.Count > 0)
        {
            Console.WriteLine(
                $"Untouchable boss: moveset skipped (static asset(s) missing: {string.Join(", ", missingAssets)}; run tools/bootstrap.py)");
            return new ParamResult(true, false);
        }

        var think = reg.GetParam("NpcThinkParam");
        var behavior = reg.GetParam("BehaviorParam");
        var bullet = reg.GetParam("Bullet", "BulletParam");
        var atk = reg.GetParam("AtkParam_Npc", "AtkParam");
        if (think == null || behavior == null || bullet == null || atk == null)
        {
            Console.WriteLine(
                "Untouchable boss: moveset skipped (NpcThinkParam/BehaviorParam/Bullet/AtkParam_Npc unavailable)");
            return new ParamResult(true, false);
        }

        return new ParamResult(true, ApplyMoveset(npc, think, behavior, bullet, atk));
    }

    public static void Apply(PARAM npc, PARAM spEffect)
    {
        var spRow = GameEditor.AddRow(
            spEffect, SpeedFogIds.UntouchableBossSpEffectRow, WALL_LIFT_TEMPLATE_SPEFFECT);
        foreach (var field in new[]
        {
            "slashDamageCutRate", "blowDamageCutRate", "thrustDamageCutRate",
            "neutralDamageCutRate", "magicDamageCutRate", "fireDamageCutRate",
            "thunderDamageCutRate", "darkDamageCutRate",
        })
        {
            spRow[field].Value = DAMAGE_CUT;
        }

        var npcRow = GameEditor.AddRow(
            npc, SpeedFogIds.UntouchableBossNpcRow, UNTOUCHABLE_VANILLA_NPC);
        npcRow["hp"].Value = BOSS_HP;          // u32
        npcRow["getSoul"].Value = BOSS_RUNES;  // u32
        // Slot 19 is the first free slot (17 = teleport gate, 18 = parry
        // wall; the custom row's stateInfo 121 lifts the wall permanently).
        npcRow["spEffectID19"].Value = SpeedFogIds.UntouchableBossSpEffectRow; // s32

        Console.WriteLine(
            $"Untouchable boss: NpcParam {SpeedFogIds.UntouchableBossNpcRow} (clone of {UNTOUCHABLE_VANILLA_NPC}, hp {BOSS_HP}, runes {BOSS_RUNES}) + SpEffect {SpeedFogIds.UntouchableBossSpEffectRow} (cut {DAMAGE_CUT})");
    }

    /// <summary>Writes the moveset rows: boss think row (own battle script),
    /// boss behavior variation on the NpcParam clone with vanilla's nine
    /// rows re-keyed plus the beam judge, the beam bullet and its damage
    /// row. All-or-nothing: every input is resolved before the first write,
    /// and false means nothing was written (the boss keeps vanilla AI). The
    /// variation change in particular must never land alone: a variation
    /// with no BehaviorParam rows is a boss with no attacks.</summary>
    public static bool ApplyMoveset(PARAM npc, PARAM think, PARAM behavior, PARAM bullet, PARAM atk)
    {
        var variation = SpeedFogIds.UntouchableBossBehaviorVariation;
        var reasons = new List<string>();

        var bossNpc = npc.Rows.Find(r => r.ID == SpeedFogIds.UntouchableBossNpcRow);
        if (bossNpc == null)
            reasons.Add($"NpcParam {SpeedFogIds.UntouchableBossNpcRow} missing (Apply not run)");

        var vanillaRows = new Dictionary<int, PARAM.Row>();
        foreach (var row in behavior.Rows)
        {
            if ((int)row["variationId"].Value != UNTOUCHABLE_VANILLA_VARIATION)
                continue;
            var judge = (int)row["behaviorJudgeId"].Value;
            if (Array.IndexOf(VanillaJudges, judge) >= 0)
                vanillaRows.TryAdd(judge, row);
        }
        var missingJudges = VanillaJudges.Where(j => !vanillaRows.ContainsKey(j)).ToList();
        if (missingJudges.Count > 0)
            reasons.Add($"BehaviorParam judge(s) {string.Join("/", missingJudges)} missing for variation {UNTOUCHABLE_VANILLA_VARIATION}");

        foreach (var (name, param, id) in new (string, PARAM, int)[]
        {
            ("NpcThinkParam", think, UNTOUCHABLE_VANILLA_THINK),
            ("Bullet", bullet, BEAM_TEMPLATE_BULLET),
            ("AtkParam_Npc", atk, BEAM_TEMPLATE_ATK),
        })
        {
            if (param.Rows.All(r => r.ID != id))
                reasons.Add($"{name} template row {id} missing");
        }

        if (reasons.Count > 0)
        {
            Console.WriteLine($"Untouchable boss: moveset skipped ({string.Join("; ", reasons)})");
            return false;
        }

        bossNpc!["behaviorVariationId"].Value = variation; // s32

        var thinkRow = GameEditor.AddRow(think, SpeedFogIds.UntouchableBossThinkRow, UNTOUCHABLE_VANILLA_THINK);
        thinkRow["battleGoalID"].Value = SpeedFogIds.UntouchableBossBattleGoal; // s32; logicId stays 528000

        foreach (var judge in VanillaJudges)
        {
            var clone = GameEditor.AddRow(behavior, SpeedFogIds.BehaviorRowId(variation, judge), vanillaRows[judge]);
            clone["variationId"].Value = variation;
        }
        var beamBehavior = GameEditor.AddRow(
            behavior, SpeedFogIds.BehaviorRowId(variation, SpeedFogIds.UntouchableBeamJudge), vanillaRows[101]);
        beamBehavior["variationId"].Value = variation;
        beamBehavior["behaviorJudgeId"].Value = SpeedFogIds.UntouchableBeamJudge;
        beamBehavior["refType"].Value = (byte)1; // bullet
        beamBehavior["refId"].Value = SpeedFogIds.UntouchableBeamBulletRow;

        var beamBullet = GameEditor.AddRow(bullet, SpeedFogIds.UntouchableBeamBulletRow, BEAM_TEMPLATE_BULLET);
        beamBullet["atkId_Bullet"].Value = SpeedFogIds.UntouchableBeamAtkRow;
        for (int i = 0; i <= 4; i++)
            beamBullet[$"spEffectId{i}"].Value = -1; // no madness buildup, no rider effects

        var beamAtk = GameEditor.AddRow(atk, SpeedFogIds.UntouchableBeamAtkRow, BEAM_TEMPLATE_ATK);
        beamAtk["atkMag"].Value = BEAM_MAGIC; // u16; throw fields already 0 on 5280115

        Console.WriteLine(
            $"Untouchable boss: moveset rows (think {SpeedFogIds.UntouchableBossThinkRow} -> battle {SpeedFogIds.UntouchableBossBattleGoal}, variation {variation} with {VanillaJudges.Length} vanilla judges + beam judge {SpeedFogIds.UntouchableBeamJudge}, bullet {SpeedFogIds.UntouchableBeamBulletRow} (clone of {BEAM_TEMPLATE_BULLET}), atk {SpeedFogIds.UntouchableBeamAtkRow} magic {BEAM_MAGIC})");
        return true;
    }

    /// <summary>MSB phase (post-Write): repoint every placed untouchable
    /// (arena entity ids whose assignment value is the source entity) to
    /// the boss NpcParam clone. <paramref name="repointThink"/> (the
    /// regulation phase's Moveset flag) also repoints ThinkParamID at the
    /// boss think row; false keeps the vanilla AI.
    ///
    /// <paramref name="mergeDir"/> is the Item Randomizer merge dir (null
    /// or empty disables the fallback, leaving behavior unchanged). When
    /// assignment targets remain unfound after the primary mod-dir scan,
    /// each map in <paramref name="fallbackArenaMaps"/> (data/game_tweaks.toml
    /// [[fallback_arena_maps]]) is read from the merge-dir copy, repointed
    /// the same way, and, only when something was actually repointed,
    /// written into modDir (the higher-priority layer).</summary>
    public static void Inject(
        string modDir, Dictionary<string, string> enemyAssignments, string? mergeDir,
        IReadOnlyList<string> fallbackArenaMaps, bool repointThink)
    {
        var source = SpeedFogIds.UntouchableSourceEntity.ToString();
        var arenaIds = enemyAssignments
            .Where(kv => kv.Value == source)
            .Select(kv => uint.Parse(kv.Key))
            .ToHashSet();
        if (arenaIds.Count == 0)
            return;

        Console.WriteLine(
            $"Untouchable boss: repointing {arenaIds.Count} placed boss slot(s)");
        var msbDir = Path.Combine(modDir, "map", "mapstudio");
        int total = 0;
        var found = new HashSet<uint>();
        // Separate from ForEachWithBufferedLogs' own console lock: that one
        // only serializes log flushing, but `found` (a HashSet) still needs
        // its own lock for concurrent Add calls across worker threads.
        var foundLock = new object();
        MsbHelper.ForEachWithBufferedLogs(Directory.GetFiles(msbDir, "*.msb.dcx"), (msbPath, log) =>
        {
            var msb = MSBE.Read(msbPath);
            // Always surface collected log lines (e.g. "not c5280" warnings),
            // even when nothing was repointed in this map; only the numeric
            // bookkeeping below is gated on the count.
            var (repointed, ids) = ApplyToMsb(msb, arenaIds, log, repointThink);
            if (repointed > 0)
            {
                msb.Write(msbPath);
                Interlocked.Add(ref total, repointed);
                lock (foundLock)
                {
                    foreach (var id in ids)
                        found.Add(id);
                }
            }
        });
        // Merge-dir fallback: arenas whose map FogMod never writes (see
        // fallbackArenaMaps, data/game_tweaks.toml [[fallback_arena_maps]])
        // still have unfound assignment targets at this point. Read the
        // merge-dir copy, repoint it, and ship it into modDir only when
        // something was actually repointed there.
        if (found.Count < arenaIds.Count && !string.IsNullOrEmpty(mergeDir))
        {
            foreach (var name in fallbackArenaMaps)
            {
                var msbFileName = $"{name}.msb.dcx";
                if (MsbHelper.FindMsbPath(modDir, msbFileName) != null)
                    continue; // already scanned in the primary loop above

                var mergePath = MsbHelper.FindMsbPath(mergeDir, msbFileName);
                if (mergePath == null)
                {
                    Console.WriteLine(
                        $"  Warning: fallback map {msbFileName} not found in merge dir");
                    continue;
                }

                var msb = MSBE.Read(mergePath);
                var lines = new List<string>();
                var (repointed, ids) = ApplyToMsb(msb, arenaIds, lines.Add, repointThink);
                foreach (var line in lines)
                    Console.WriteLine(line);
                if (repointed > 0)
                {
                    var writePath = MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
                    Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
                    msb.Write(writePath);
                    total += repointed;
                    foreach (var id in ids)
                        found.Add(id);
                    Console.WriteLine(
                        $"  Fallback: repointed {repointed} part(s) in {name} (merge-dir copy shipped into the mod dir)");
                }
            }
        }

        Console.WriteLine($"  Repointed {total} untouchable boss part(s)");
        foreach (var missing in arenaIds.Except(found).OrderBy(id => id))
        {
            // Phase-expanded slots may have no MSB part of their own.
            Console.WriteLine(
                $"  Warning: assignment target {missing} not found in any map (phase slot?)");
        }
    }

    /// <summary>Repoints every enemy part whose EntityID is an arena id and
    /// whose model is c5280 (the wrong-model case is logged and skipped, not
    /// repointed): NPCParamID always, ThinkParamID only when
    /// <paramref name="repointThink"/> (the moveset rows exist). Returns the
    /// repointed count and the repointed entity ids, so callers can do exact
    /// found/missing bookkeeping without re-scanning the MSB by NPCParamID.</summary>
    internal static (int Repointed, List<uint> Ids) ApplyToMsb(
        MSBE msb, HashSet<uint> arenaIds, Action<string> log, bool repointThink)
    {
        var ids = new List<uint>();
        foreach (var enemy in msb.Parts.Enemies)
        {
            if (!arenaIds.Contains(enemy.EntityID))
                continue;
            if (enemy.ModelName != "c5280")
            {
                log($"  Warning: arena entity {enemy.EntityID} is {enemy.ModelName}, not c5280; leaving it alone");
                continue;
            }
            enemy.NPCParamID = SpeedFogIds.UntouchableBossNpcRow;
            if (repointThink)
            {
                enemy.ThinkParamID = SpeedFogIds.UntouchableBossThinkRow;
                log($"  {enemy.Name} (entity {enemy.EntityID}): NPCParamID -> {SpeedFogIds.UntouchableBossNpcRow}, ThinkParamID -> {SpeedFogIds.UntouchableBossThinkRow}");
            }
            else
            {
                log($"  {enemy.Name} (entity {enemy.EntityID}): NPCParamID -> {SpeedFogIds.UntouchableBossNpcRow}");
            }
            ids.Add(enemy.EntityID);
        }
        return (ids.Count, ids);
    }
}
