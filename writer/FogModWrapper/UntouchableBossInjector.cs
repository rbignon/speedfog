using FogMod;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Promotes enemy-randomizer-placed Aging Untouchables (source entity
/// 2049420200, allowlist-only minor boss) to their boss form: a cloned
/// NpcParam row outside the nerflantern-patched 5280 band, a partial wall
/// (the vanilla full-immunity wall swapped for a damage cut in the wall
/// event the randomizer copies next to each placed boss, so the first parry
/// clears it the vanilla way) and, when the static assets are built, its
/// own battle AI and a frenzy beam. See docs/untouchable-boss.md.
/// </summary>
public static class UntouchableBossInjector
{
    public const int UNTOUCHABLE_VANILLA_NPC = 52800086;

    // Initial values for the in-game tuning session (docs/untouchable-boss.md).
    public const uint BOSS_HP = 2000;
    public const uint BOSS_RUNES = 20000;
    /// <summary>Hit reactions. The stagger meter follows Jori's profile
    /// (superArmorDurability 80, recovery 3/13; vanilla 65 and 0) and
    /// toughness is Jori's 0 (35 on vanilla 52800086). Neither keeps
    /// ordinary hits from interrupting the boss (2026-09-05/06 runs): that
    /// is the resident damage-level table, see
    /// <see cref="NO_FLINCH_TEMPLATE_SPEFFECT"/>.</summary>
    public const uint BOSS_TOUGHNESS = 0;
    public const float BOSS_SUPER_ARMOR = 80f;
    public const float BOSS_SUPER_ARMOR_RECOVER = 0.23076923f; // Jori's exact value, 3/13
    public const float DAMAGE_CUT = 0.5f; // fraction of damage taken (50% cut)

    /// <summary>Fraction of damage taken once the wall is broken: twice the
    /// vanilla damage, i.e. a x4 ratio against the partial wall (2026-09-05
    /// session request). Tuning knob.</summary>
    public const float BROKEN_DAMAGE_TAKEN = 2f;

    /// <summary>The one-second break VFX effect the copied wall event applies
    /// once the wall is cleared; the template of the broken row and the
    /// anchor after which that row's SetSpEffect is inserted.</summary>
    public const int BREAK_VFX_SPEFFECT = 20011472;

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

    /// <summary>Judges of the lantern's ambient pulses (vanilla bullets
    /// 205280000-002, fired by idle, walk and most attacks): re-pointed at
    /// madness-free clones under the boss variation.</summary>
    public static readonly int[] PulseJudges = { 100, 101, 102 };

    /// <summary>Madness buildup rider on the pulse bullets (SpEffect 26000,
    /// stateInfo 437, madnessAttackPower 10): the reason the status rose even
    /// while the boss stood still.</summary>
    public const int LANTERN_MADNESS_SPEFFECT = 26000;

    // --- Parry break (docs/untouchable-boss.md "Parry break") ---

    /// <summary>The parry-window SpEffect (category 1001, stateInfo 121):
    /// the parried animation (8500) applies it through a TAE event, it
    /// overrides the wall (same category) during the parry, and the
    /// untouchable's own wall event waits for it to clear the wall for good.
    /// nerflantern makes it resident on every 5280-band row to defeat the
    /// wall; <see cref="Apply"/> scrubs that inherited copy from the boss
    /// clone so the partial wall and the parry detection work.</summary>
    public const int PARRY_WINDOW_SPEFFECT = 20011471;

    /// <summary>The vanilla wall: full immunity (every damage cut rate 0,
    /// category 1001) applied by the untouchable's wall event at spawn and
    /// cleared by that event on the first parry. Swapped for the partial cut
    /// row by <see cref="PatchWallEvents"/>.</summary>
    public const int VANILLA_WALL_SPEFFECT = 20011470;

    /// <summary>Vanilla's boss damage-level table: a permanent SpEffect
    /// (category 1001, priority 200) whose twelve <c>dmgLv_*</c> fields
    /// replace every incoming damage level by None, resident in slot 1 of
    /// Jori (53120000), Godrick, Margit and 690 NpcParam rows in all; the
    /// vanilla immunity wall 20011470 carries the same table, which is why
    /// an immune untouchable never flinches. Vanilla 52800086 has no such
    /// row: whatever its super armor, every hit interrupted the boss.</summary>
    public const int NO_FLINCH_TEMPLATE_SPEFFECT = 5300;

    /// <summary>NpcParam carries spEffectID0..31.</summary>
    private const int NPC_SPEFFECT_SLOTS = 32;

    private static readonly string[] CutFields =
    {
        "slashDamageCutRate", "blowDamageCutRate", "thrustDamageCutRate",
        "neutralDamageCutRate", "magicDamageCutRate", "fireDamageCutRate",
        "thunderDamageCutRate", "darkDamageCutRate",
    };

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
        // The partial wall: a clone of the parry-window effect (category
        // 1001, stateInfo 121) with the eight cut rates lowered. It is not
        // resident on the NpcParam row: the enemy randomizer copies the
        // untouchable's own wall event next to every placed boss, and
        // PatchWallEvents swaps that event's full-immunity wall (20011470)
        // for this row, so the vanilla flow applies it at spawn, lets the
        // parry-window effect override it during a parry (same category)
        // and clears it for good on the first parry.
        var spRow = GameEditor.AddRow(
            spEffect, SpeedFogIds.UntouchableBossSpEffectRow, PARRY_WINDOW_SPEFFECT);
        foreach (var field in CutFields)
            spRow[field].Value = DAMAGE_CUT;

        // Broken state: applied by the copied wall event right after the
        // break VFX (PatchWallEvents inserts the SetSpEffect). A permanent,
        // VFX-less clone of that effect (category 0: coexists with the
        // parry-window effect of later parries) raising the damage taken.
        var brokenRow = GameEditor.AddRow(spEffect, SpeedFogIds.UntouchableBrokenSpEffectRow, BREAK_VFX_SPEFFECT);
        foreach (var field in CutFields)
            brokenRow[field].Value = BROKEN_DAMAGE_TAKEN;
        brokenRow["effectEndurance"].Value = -1f; // f32: permanent
        brokenRow["vfxId"].Value = -1;            // s32: the flash stays on the vanilla row

        var npcRow = GameEditor.AddRow(
            npc, SpeedFogIds.UntouchableBossNpcRow, UNTOUCHABLE_VANILLA_NPC);
        npcRow["hp"].Value = BOSS_HP;          // u32
        npcRow["getSoul"].Value = BOSS_RUNES;  // u32
        npcRow["toughness"].Value = BOSS_TOUGHNESS;                            // u32
        npcRow["superArmorDurability"].Value = BOSS_SUPER_ARMOR;               // f32
        npcRow["superArmorRecoverCorrection"].Value = BOSS_SUPER_ARMOR_RECOVER; // f32
        // The clone is taken from the merged regulation, where the Item
        // Randomizer's always-on nerflantern option has already written the
        // parry-window effect (20011471) into a free slot of every 5280-band
        // row (slot 31 on 1.17) to defeat the wall for ambient untouchables.
        // Resident, it would defeat the partial wall too (same category) and
        // keep the copied event's parry detector permanently true (banner
        // looping from game start, 2026-09-05 diagnostic); drop it from
        // every slot. Slot 17 (20011450, vanilla's teleport gate, unread by
        // the boss script) and slot 18
        // (20011473, stateInfo 420) stay as vanilla.
        for (int i = 0; i < NPC_SPEFFECT_SLOTS; i++)
        {
            if ((int)npcRow[$"spEffectID{i}"].Value == PARRY_WINDOW_SPEFFECT)
                npcRow[$"spEffectID{i}"].Value = -1;
        }

        // Hit reactions: vanilla's boss damage-level table, cloned out of
        // the wall's category and made resident in the first free slot
        // (slot 1 on 52800086, where 0 is the no-op row). Within a category
        // the lower categoryPriority wins (paramdef description), so the
        // partial wall and the parry-window effect (1001/0) outrank the
        // table (1001/200): resident as vanilla's 5300, it would be evicted
        // for good by the wall applied at spawn. Category 0 keeps it out of
        // that contest; whether the engine honours a category-0 table is
        // the in-game check (no vanilla table lives outside 1001). The
        // super armor meter still staggers the boss (Godrick carries the
        // same table). Skipped with a warning rather than failing the seed:
        // the boss then flinches as before.
        string noFlinch = "no no-flinch table";
        int slot = FirstFreeSpEffectSlot(npcRow);
        if (spEffect[NO_FLINCH_TEMPLATE_SPEFFECT] == null)
            Console.WriteLine($"Untouchable boss: warning, SpEffect {NO_FLINCH_TEMPLATE_SPEFFECT} (boss damage-level table) not found, ordinary hits keep interrupting the boss");
        else if (slot < 0)
            Console.WriteLine("Untouchable boss: warning, no free spEffectID slot on the NpcParam clone, ordinary hits keep interrupting the boss");
        else
        {
            var table = GameEditor.AddRow(
                spEffect, SpeedFogIds.UntouchableNoFlinchSpEffectRow, NO_FLINCH_TEMPLATE_SPEFFECT);
            table["spCategory"].Value = (ushort)0;
            table["categoryPriority"].Value = (byte)0; // meaningless without a category; as the other category-0 rows
            npcRow[$"spEffectID{slot}"].Value = SpeedFogIds.UntouchableNoFlinchSpEffectRow;
            noFlinch = $"no-flinch table {SpeedFogIds.UntouchableNoFlinchSpEffectRow} in slot {slot}";
        }

        Console.WriteLine(
            $"Untouchable boss: NpcParam {SpeedFogIds.UntouchableBossNpcRow} (clone of {UNTOUCHABLE_VANILLA_NPC}, hp {BOSS_HP}, runes {BOSS_RUNES}, toughness {BOSS_TOUGHNESS}, super armor {BOSS_SUPER_ARMOR}/{BOSS_SUPER_ARMOR_RECOVER}, nerflantern slot scrubbed, {noFlinch}) + partial wall SpEffect {SpeedFogIds.UntouchableBossSpEffectRow} (cut {DAMAGE_CUT}) + broken SpEffect {SpeedFogIds.UntouchableBrokenSpEffectRow} (x{BROKEN_DAMAGE_TAKEN}), both applied by the copied wall event");
    }

    /// <summary>Lowest spEffectIDn slot holding -1 or 0 (row 0 is the
    /// engine's no-op effect, what vanilla rows carry in unused slots; the
    /// same "&lt;= 0 is free" test as RandomizerCommon's
    /// GameData.AddNpcSpEffect, which scans from slot 31 down, hence
    /// nerflantern's slot 31), or -1 when all 32 are taken.</summary>
    private static int FirstFreeSpEffectSlot(PARAM.Row npcRow)
    {
        for (int i = 0; i < NPC_SPEFFECT_SLOTS; i++)
        {
            if ((int)npcRow[$"spEffectID{i}"].Value <= 0)
                return i;
        }
        return -1;
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
        int vanillaCount = 0;
        foreach (var row in behavior.Rows)
        {
            if ((int)row["variationId"].Value != UNTOUCHABLE_VANILLA_VARIATION)
                continue;
            vanillaCount++;
            var judge = (int)row["behaviorJudgeId"].Value;
            if (Array.IndexOf(VanillaJudges, judge) >= 0)
                vanillaRows.TryAdd(judge, row);
        }
        var missingJudges = VanillaJudges.Where(j => !vanillaRows.ContainsKey(j)).ToList();
        if (missingJudges.Count > 0)
            reasons.Add($"BehaviorParam judge(s) {string.Join("/", missingJudges)} missing for variation {UNTOUCHABLE_VANILLA_VARIATION}");

        if (vanillaCount != VanillaJudges.Length)
            reasons.Add($"BehaviorParam variation {UNTOUCHABLE_VANILLA_VARIATION} has {vanillaCount} row(s), expected {VanillaJudges.Length} (refresh VanillaJudges after a game patch)");

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

        // The pulse judges must resolve to bullets (refType 1) that exist:
        // their madness-free clones are what the boss variation points at.
        var pulseTemplates = new Dictionary<int, PARAM.Row>();
        foreach (var judge in PulseJudges)
        {
            if (!vanillaRows.TryGetValue(judge, out var row))
                continue; // already reported above
            var refId = (int)row["refId"].Value;
            var template = (byte)row["refType"].Value == 1 ? bullet.Rows.Find(r => r.ID == refId) : null;
            if (template == null)
                reasons.Add($"pulse judge {judge} does not resolve to a Bullet row ({refId})");
            else
                pulseTemplates[judge] = template;
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
            int pulseIndex = Array.IndexOf(PulseJudges, judge);
            if (pulseIndex < 0)
                continue;
            // Boss-only pulse: same bullet minus the madness rider (the VFX
            // rider in another slot stays); ambient untouchables keep vanilla.
            int pulseId = SpeedFogIds.UntouchablePulseBulletBase + pulseIndex;
            var pulse = GameEditor.AddRow(bullet, pulseId, pulseTemplates[judge]);
            for (int i = 0; i <= 4; i++)
            {
                if ((int)pulse[$"spEffectId{i}"].Value == LANTERN_MADNESS_SPEFFECT)
                    pulse[$"spEffectId{i}"].Value = -1;
            }
            clone["refId"].Value = pulseId;
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
        beamBullet["spEffectIDForShooter"].Value = -1; // s32: Frenzied Burst's caster-side madness rider

        var beamAtk = GameEditor.AddRow(atk, SpeedFogIds.UntouchableBeamAtkRow, BEAM_TEMPLATE_ATK);
        beamAtk["atkMag"].Value = BEAM_MAGIC; // u16; throw fields already 0 on 5280115

        Console.WriteLine(
            $"Untouchable boss: moveset rows (think {SpeedFogIds.UntouchableBossThinkRow} -> battle {SpeedFogIds.UntouchableBossBattleGoal}, variation {variation} with {VanillaJudges.Length} vanilla judges + beam judge {SpeedFogIds.UntouchableBeamJudge}, bullet {SpeedFogIds.UntouchableBeamBulletRow} (clone of {BEAM_TEMPLATE_BULLET}), atk {SpeedFogIds.UntouchableBeamAtkRow} magic {BEAM_MAGIC}, pulses {SpeedFogIds.UntouchablePulseBulletBase}-{SpeedFogIds.UntouchablePulseBulletBase + PulseJudges.Length - 1} without madness)");
        return true;
    }

    /// <summary>Arena entity ids whose enemy assignment is the Aging
    /// Untouchable source.</summary>
    public static HashSet<uint> ArenaIds(Dictionary<string, string> enemyAssignments)
    {
        var source = SpeedFogIds.UntouchableSourceEntity.ToString();
        return enemyAssignments
            .Where(kv => kv.Value == source)
            .Select(kv => uint.Parse(kv.Key))
            .ToHashSet();
    }

    /// <summary>Rewrites the events the enemy randomizer copied for one
    /// placed boss (every event whose InitializeEvent carries the boss
    /// entity): each SetSpEffect/ClearSpEffect of the full-immunity wall
    /// (<see cref="VANILLA_WALL_SPEFFECT"/>) now names the partial cut row,
    /// the first SetCharacterHPBarDisplay(disabled) of each of those events
    /// becomes enabled (the boss takes damage from the start, so its bar
    /// shows from the start, and the teleport sibling event must not hide
    /// it again), and a SetSpEffect of the broken row
    /// (<see cref="SpeedFogIds.UntouchableBrokenSpEffectRow"/>) is inserted
    /// right after each SetSpEffect of the break VFX
    /// (<see cref="BREAK_VFX_SPEFFECT"/>), carrying the same entity
    /// parameter. Parameterized slots are left alone. Returns the number of
    /// instructions rewritten or inserted, or 0 (with a warning, nothing
    /// touched) when none of the boss's events carries the wall: the boss
    /// then has no damage cut at all.</summary>
    public static int PatchWallEvents(EMEVD emevd, uint boss, Action<string> log)
    {
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            log($"  Warning: Event 0 not found, wall patch skipped for entity {boss}");
            return 0;
        }

        // Events whose InitializeEvent (slot, event id, args...) carries the
        // boss entity among its args.
        var eventIds = new HashSet<long>();
        foreach (var ins in initEvent.Instructions)
        {
            if (ins.Bank != 2000 || ins.ID != 0 || ins.ArgData.Length < 12)
                continue;
            for (int off = 8; off + 4 <= ins.ArgData.Length; off += 4)
            {
                if (BitConverter.ToUInt32(ins.ArgData, off) == boss)
                {
                    eventIds.Add(BitConverter.ToInt32(ins.ArgData, 4));
                    break;
                }
            }
        }

        var bossEvents = emevd.Events.Where(e => eventIds.Contains(e.ID)).ToList();

        // Byte 4 holds the SpEffect id or the enable flag; a parameterized
        // slot there is the runtime's, not ours.
        static HashSet<int> ParameterizedAtByte4(EMEVD.Event evt) =>
            evt.Parameters.Where(prm => prm.TargetStartByte == 4).Select(prm => (int)prm.InstructionIndex).ToHashSet();
        static bool IsWallInstruction(EMEVD.Instruction ins) =>
            ins.Bank == 2004 && (ins.ID == 8 || ins.ID == 21) && ins.ArgData.Length >= 8
            && BitConverter.ToInt32(ins.ArgData, 4) == VANILLA_WALL_SPEFFECT;

        // Decide before touching anything: without a wall to soften, an
        // always-visible bar on an immune boss would mislead.
        bool hasWall = bossEvents.Any(evt =>
        {
            var skip = ParameterizedAtByte4(evt);
            return evt.Instructions.Where((ins, i) => !skip.Contains(i)).Any(IsWallInstruction);
        });
        if (!hasWall)
        {
            log($"  Warning: no wall event found for entity {boss}: the boss has no damage cut (full damage from the start)");
            return 0;
        }

        int swaps = 0;
        int barFlips = 0;
        int riders = 0;
        foreach (var evt in bossEvents)
        {
            var skip = ParameterizedAtByte4(evt);
            bool hpBarDone = false;
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var ins = evt.Instructions[i];
                if (ins.Bank != 2004 || ins.ArgData.Length < 8 || skip.Contains(i))
                    continue;
                if (IsWallInstruction(ins))
                {
                    BitConverter.GetBytes(SpeedFogIds.UntouchableBossSpEffectRow).CopyTo(ins.ArgData, 4);
                    swaps++;
                }
                else if (ins.ID == 8 && BitConverter.ToInt32(ins.ArgData, 4) == BREAK_VFX_SPEFFECT)
                {
                    // Broken state: same entity slot (copy of the vanilla
                    // instruction's parameter), our row id, inserted right
                    // after the break VFX. Later parameters shift by one.
                    var entityParam = evt.Parameters.FirstOrDefault(prm => prm.InstructionIndex == i && prm.TargetStartByte == 0);
                    var bytes = (byte[])ins.ArgData.Clone();
                    BitConverter.GetBytes(SpeedFogIds.UntouchableBrokenSpEffectRow).CopyTo(bytes, 4);
                    foreach (var prm in evt.Parameters)
                    {
                        if (prm.InstructionIndex > i)
                            prm.InstructionIndex++;
                    }
                    evt.Instructions.Insert(i + 1, new EMEVD.Instruction(2004, 8, bytes));
                    if (entityParam != null)
                        evt.Parameters.Add(new EMEVD.Parameter(i + 1, 0, entityParam.SourceStartByte, entityParam.ByteCount));
                    skip = ParameterizedAtByte4(evt);
                    riders++;
                    i++; // skip the instruction just inserted
                }
                else if (ins.ID == 30 && !hpBarDone && ins.ArgData[4] == 0)
                {
                    ins.ArgData[4] = 1; // spawn-time or teleport "disabled" -> enabled
                    hpBarDone = true;
                    barFlips++;
                }
            }
        }

        log($"  wall event patched for entity {boss}: {swaps} wall swap(s) ({VANILLA_WALL_SPEFFECT} -> {SpeedFogIds.UntouchableBossSpEffectRow}) + {barFlips} HP bar flip(s) + {riders} broken rider(s) ({SpeedFogIds.UntouchableBrokenSpEffectRow})");
        return swaps + barFlips + riders;
    }

    /// <summary>Reads the map's EMEVD from the mod dir (or copies it there
    /// from <paramref name="sourceDir"/>, the Item Randomizer's merge dir,
    /// for the fallback arena FogMod never writes), patches the wall events
    /// of the given bosses and writes it back. Missing everywhere: logged
    /// and skipped, that boss keeps the vanilla full wall until parried.</summary>
    internal static int InjectWallPatch(
        string modDir, string msbFileName, IEnumerable<uint> bossIds, Action<string> log, string? sourceDir = null)
    {
        var ids = bossIds.OrderBy(id => id).ToList();
        if (ids.Count == 0)
            return 0;
        var mapId = msbFileName.Replace(".msb.dcx", "", StringComparison.OrdinalIgnoreCase);
        var emevdName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdName);
        if (!File.Exists(emevdPath))
        {
            var sourcePath = sourceDir == null ? null : Path.Combine(sourceDir, "event", emevdName);
            if (sourcePath == null || !File.Exists(sourcePath))
            {
                log($"  Warning: {emevdName} not in the mod dir, wall patch skipped for entity {string.Join("/", ids)} (vanilla full wall until parried)");
                return 0;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(sourcePath, emevdPath);
            log($"  {emevdName} copied from the merge dir for the wall patch");
        }
        var emevd = EMEVD.Read(emevdPath);
        int rewritten = 0;
        foreach (var boss in ids)
            rewritten += PatchWallEvents(emevd, boss, log);
        if (rewritten > 0)
            emevd.Write(emevdPath);
        return rewritten;
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
    /// written into modDir (the higher-priority layer).
    ///
    /// Every repointed boss also gets its copied wall event patched in the
    /// same map's EMEVD (<see cref="InjectWallPatch"/>).</summary>
    public static void Inject(
        string modDir, Dictionary<string, string> enemyAssignments, string? mergeDir,
        IReadOnlyList<string> fallbackArenaMaps, bool repointThink)
    {
        var arenaIds = ArenaIds(enemyAssignments);
        if (arenaIds.Count == 0)
            return;

        int wallInstructions = 0;
        int wallMaps = 0;

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
                int patched = InjectWallPatch(modDir, Path.GetFileName(msbPath), ids, log);
                if (patched > 0)
                {
                    Interlocked.Add(ref wallInstructions, patched);
                    Interlocked.Increment(ref wallMaps);
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
                    // FogMod never writes this map's EMEVD: the merge-dir copy
                    // (which carries the randomizer's wall events) is shipped
                    // into the mod dir, patched.
                    int patched = InjectWallPatch(modDir, msbFileName, ids, Console.WriteLine, mergeDir);
                    if (patched > 0)
                    {
                        wallInstructions += patched;
                        wallMaps++;
                    }
                }
            }
        }

        Console.WriteLine($"  Repointed {total} untouchable boss part(s)");
        Console.WriteLine($"  Wall patch: {wallInstructions} instruction(s) in {wallMaps} map(s)");
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
