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
/// window. See docs/untouchable-boss.md.
/// </summary>
public static class UntouchableBossInjector
{
    public const int UNTOUCHABLE_VANILLA_NPC = 52800086;
    private const int WALL_LIFT_TEMPLATE_SPEFFECT = 20011471;

    // Initial values for the in-game tuning session (docs/untouchable-boss.md).
    public const uint BOSS_HP = 3000;
    public const uint BOSS_RUNES = 20000;
    public const float DAMAGE_CUT = 0.35f; // fraction of damage taken (65% cut)

    public static bool IsBossPlaced(Dictionary<string, string> enemyAssignments)
        => enemyAssignments.ContainsValue(
            SpeedFogIds.UntouchableSourceEntity.ToString());

    public static void ApplyParams(RegulationEditor reg)
    {
        var npc = reg.GetParam("NpcParam");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (npc == null || sp == null)
        {
            Console.WriteLine(
                "Untouchable boss: NpcParam/SpEffectParam unavailable, boss keeps vanilla stats");
            return;
        }
        Apply(npc, sp);
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

    /// <summary>MSB phase (post-Write): repoint every placed untouchable
    /// (arena entity ids whose assignment value is the source entity) to
    /// the boss NpcParam clone. ThinkParamID stays vanilla 52800000; AI
    /// tuning is a documented follow-up, not done here.</summary>
    public static void Inject(
        string modDir, Dictionary<string, string> enemyAssignments)
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
        var consoleLock = new object();
        Parallel.ForEach(Directory.GetFiles(msbDir, "*.msb.dcx"), msbPath =>
        {
            var msb = MSBE.Read(msbPath);
            var lines = new List<string>();
            int repointed = ApplyToMsb(msb, arenaIds, lines.Add);
            if (repointed == 0)
                return;
            msb.Write(msbPath);
            lock (consoleLock)
            {
                total += repointed;
                foreach (var e in msb.Parts.Enemies.Where(
                    e => e.NPCParamID == SpeedFogIds.UntouchableBossNpcRow))
                {
                    found.Add(e.EntityID);
                }
                foreach (var line in lines)
                    Console.WriteLine(line);
            }
        });
        Console.WriteLine($"  Repointed {total} untouchable boss part(s)");
        foreach (var missing in arenaIds.Except(found).OrderBy(id => id))
        {
            // Phase-expanded slots may have no MSB part of their own.
            Console.WriteLine(
                $"  Warning: assignment target {missing} not found in any map (phase slot?)");
        }
    }

    internal static int ApplyToMsb(MSBE msb, HashSet<uint> arenaIds, Action<string> log)
    {
        int repointed = 0;
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
            log($"  {enemy.Name} (entity {enemy.EntityID}): NPCParamID -> {SpeedFogIds.UntouchableBossNpcRow}");
            repointed++;
        }
        return repointed;
    }
}
