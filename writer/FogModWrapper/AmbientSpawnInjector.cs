using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places ambient enemy spawns at cluster exit gates for the Halloween
/// plugin: a passive "greeter" (Aging Untouchable model, perception zeroed
/// so it never aggros) standing watch beside every exit gate of a
/// mini_dungeon/legacy_dungeon cluster (HalloweenGateAnchors.
/// SpawnClusterTypes: unlike decorations, no spawns at the start cluster),
/// plus, when ambushes are enabled, a small skeleton pack sharing the
/// gate's arc. Ambushers aggro normally but are decorative: 1 HP, no
/// runes, near-zero attack via a cloned NpcParam row (ApplyAmbusher).
/// Boss arenas never receive spawns.
///
/// Two-phase injection, mirroring DeathMarkerInjector:
/// 1. MSB phase (this class, Inject/ApplyToMsb): clone a nearby vanilla enemy
///    per anchored gate, retarget it to the greeter/ambusher model.
/// 2. Regulation phase (ApplyPassiveThinkRow): clone the greeter's
///    NpcThinkParam row with all perception fields zeroed.
///
/// Must run AFTER GateDecorInjector: the spawns carry EntityID 0 and would
/// otherwise pollute the decor injector's vanilla-enemy ground evidence
/// (enforced by the call order in Program.cs).
/// </summary>
public static class AmbientSpawnInjector
{
    private const string GREETER_MODEL = "c5280";
    private const int GREETER_NPC_PARAM = 52800086;
    private const string AMBUSH_MODEL = "c3500";
    // Vanilla Sage's Cave skeleton: the clone TEMPLATE for the decorative
    // ambusher row; placed ambushers use SpeedFogIds.DecorativeAmbusherNpcRow.
    private const int AMBUSH_NPC_PARAM = 35000030;
    private const int AMBUSH_THINK_PARAM = 35000000;
    // Tier-1 enemy-scaling row (7000 + 10 * tier, docs/enemy-scaling.md):
    // the SpEffect clone template for the ambushers' attack nerf. A pure
    // rate-multiplier row with no stateInfo side effects.
    private const int SCALING_TIER1_TEMPLATE_SPEFFECT = 7010;
    /// <summary>Attack power multiplier for decorative ambushers: they are
    /// scenery that swings, not a threat, so damage floors out at ~nothing.</summary>
    public const float AMBUSH_ATTACK_RATE = 0.01f;
    private const float GREETER_MIN_RADIUS = 4.0f;
    private const float GREETER_MAX_RADIUS = 6.0f;
    private const float AMBUSH_MIN_RADIUS = 3.0f;
    private const float AMBUSH_MAX_RADIUS = 7.0f;
    private const float AMBUSH_ARC_SPREAD = 140f;

    // FogMod's own entity/region allocation floor (DeathMarkerInjector.FOGMOD_ENTITY_MIN);
    // vanilla enemies used as clone sources must sit below it.
    private const uint FOGMOD_ENTITY_MIN = 755890000;

    /// <summary>
    /// Inject passive greeters (and optional ambush packs) at every exit
    /// gate of a mini_dungeon/legacy_dungeon cluster. Maps are processed
    /// in parallel (independent MSB files); no entity or event IDs are
    /// allocated, so no pre-partitioning is needed.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HalloweenPluginSettings.Settings settings)
    {
        Console.WriteLine("Injecting Halloween ambient spawns at cluster exit gates...");

        var specsByMap = CollectSpawnSpecsByMap(connections, nodes, gateSides, settings);
        var work = specsByMap.ToList();

        int totalGreeters = 0;
        int totalAmbushers = 0;
        int totalMaps = 0;
        var consoleLock = new object();

        Parallel.ForEach(work, kv =>
        {
            var (mapId, specs) = kv;
            var log = new List<string>();
            var (greeters, ambushers) = InjectMap(modDir, gameDir, mapId, specs, log.Add);
            lock (consoleLock)
            {
                foreach (var line in log)
                    Console.WriteLine(line);
            }
            if (greeters + ambushers > 0)
            {
                Interlocked.Add(ref totalGreeters, greeters);
                Interlocked.Add(ref totalAmbushers, ambushers);
                Interlocked.Increment(ref totalMaps);
            }
        });

        Console.WriteLine($"  Placed {totalGreeters} greeters + {totalAmbushers} ambushers across {totalMaps} maps");
    }

    /// <summary>
    /// Collect spawn specs per map, keyed by the anchored gate's map id.
    /// Anchors (exit gates of mini_dungeon/legacy_dungeon clusters, deduped
    /// per (map, gate part name) pair) come from HalloweenGateAnchors.
    /// Collect with SpawnClusterTypes; each expands to one Greeter, plus,
    /// when settings.Ambushes, a pack of 2-3 Ambushers.
    /// </summary>
    internal static Dictionary<string, List<SpawnSpec>> CollectSpawnSpecsByMap(
        List<Connection> connections,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HalloweenPluginSettings.Settings settings)
    {
        var result = new Dictionary<string, List<SpawnSpec>>();

        foreach (var (mapId, anchors) in HalloweenGateAnchors.Collect(
            connections, nodes, gateSides, HalloweenGateAnchors.SpawnClusterTypes))
        {
            var specs = new List<SpawnSpec>();
            result[mapId] = specs;

            foreach (var anchor in anchors)
            {
                specs.Add(new SpawnSpec(anchor.PartName, SpawnKind.Greeter, 0, 1, anchor.IsASide));

                if (settings.Ambushes)
                {
                    int packSize = 2 + new Random(StablePartNameHash(anchor.PartName)).Next(2);
                    for (int i = 0; i < packSize; i++)
                        specs.Add(new SpawnSpec(anchor.PartName, SpawnKind.Ambusher, i, packSize, anchor.IsASide));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Process-stable string hash for seeding pack-size randomness.
    /// string.GetHashCode() is randomized per process on .NET (hash-flooding
    /// mitigation), which would make ambush pack sizes differ between
    /// separate builds of the same seed. This is a plain 31-accumulator
    /// rolling hash, deterministic across processes and .NET versions.
    /// </summary>
    private static int StablePartNameHash(string s)
    {
        unchecked
        {
            int hash = 17;
            foreach (char c in s)
                hash = hash * 31 + c;
            return hash;
        }
    }

    /// <summary>
    /// Apply spawn specs to an already-loaded MSB. Groups by gate part name;
    /// finds the gate asset by name (falling back to an EntityID parse, same
    /// as DeathMarkerInjector.InjectMap), then the nearest vanilla enemy to
    /// clone (skipping FogMod-allocated entities). Maps with no vanilla enemy
    /// to clone from are logged and skipped. Returns the number of greeters
    /// and ambushers actually placed by this call (not a count of matching
    /// models in the MSB: vanilla catacomb maps often already contain
    /// skeleton enemies sharing the ambusher's chr model).
    /// </summary>
    internal static (int Greeters, int Ambushers) ApplyToMsb(MSBE msb, List<SpawnSpec> specs, Action<string> log)
    {
        int greeters = 0;
        int ambushers = 0;
        var modelsEnsured = new HashSet<string>();
        // Snapshot before placing anything: a spawn added for an earlier gate
        // carries EntityID 0 (see below), which passes the "vanilla" filter
        // in FindNearestVanillaEnemy just like a real vanilla enemy would. A
        // later gate in the same map must never pick an already-placed spawn
        // as its clone source, or the "nearest enemy stands on a valid
        // collision in the same play space" rationale for the inherited
        // CollisionPartName no longer holds.
        var vanillaEnemies = msb.Parts.Enemies.ToList();

        foreach (var group in specs.GroupBy(s => s.GatePartName))
        {
            var partName = group.Key;
            var partSpecs = group.ToList();

            var gateAsset = msb.Parts.Assets.Find(a => a.Name == partName);
            if (gateAsset == null && uint.TryParse(partName, out uint entityIdLookup))
                gateAsset = msb.Parts.Assets.Find(a => a.EntityID == entityIdLookup);
            if (gateAsset == null)
            {
                log($"  Warning: Gate asset '{partName}' not found in MSB, skipping ambient spawns");
                continue;
            }

            var baseEnemy = FindNearestVanillaEnemy(vanillaEnemies, gateAsset.Position);
            if (baseEnemy == null)
            {
                log($"  Warning: No vanilla enemy to clone from, skipping ambient spawns for gate '{partName}'");
                continue;
            }

            foreach (var spec in partSpecs)
            {
                var model = spec.Kind == SpawnKind.Greeter ? GREETER_MODEL : AMBUSH_MODEL;
                if (modelsEnsured.Add(model))
                    MsbHelper.EnsureEnemyModel(msb, model);

                var offsets = GateGeometry.GenerateArcOffsets(
                    gateAsset.EntityID, gateAsset.Rotation.Y,
                    // Same mapping as DeathMarkerInjector: isASide?180:0 places an
                    // object on the QUERIED zone's player side. GateSideIsASide was
                    // resolved against the exit area, so this lands spawns on the
                    // approach side of the exit gate, where the player walks up to
                    // it (see docs/death-markers.md "Position Offsets
                    // (ASide/BSide)").
                    spec.GateSideIsASide ? 180f : 0f,
                    spec.PackSize,
                    spec.Kind == SpawnKind.Greeter ? GREETER_MIN_RADIUS : AMBUSH_MIN_RADIUS,
                    spec.Kind == SpawnKind.Greeter ? GREETER_MAX_RADIUS : AMBUSH_MAX_RADIUS,
                    0f,
                    spec.Kind == SpawnKind.Greeter ? 120f : AMBUSH_ARC_SPREAD);
                var offset = offsets[spec.IndexInPack];

                var spawn = (MSBE.Part.Enemy)baseEnemy.DeepCopy();
                MsbHelper.CopyVisibilityGroups(spawn);
                spawn.ModelName = model;
                spawn.Name = MsbHelper.GeneratePartName(msb.Parts.Enemies.Select(e => e.Name), spawn.ModelName);
                MsbHelper.SetNameIdent(spawn);
                spawn.Position = gateAsset.Position + offset;
                // Greeters stand watch facing AWAY from the gate, toward the
                // approaching player (at an exit gate the player walks up
                // from the zone interior); ambushers keep pack scatter.
                float yawAwayFromGate = MathF.Atan2(offset.X, offset.Z) * 180f / MathF.PI;
                spawn.Rotation = new Vector3(0f, spec.Kind == SpawnKind.Greeter ? yawAwayFromGate : (spec.IndexInPack * 137f) % 360f, 0f);
                spawn.EntityID = 0;
                Array.Clear(spawn.EntityGroupIDs);
                spawn.NPCParamID = spec.Kind == SpawnKind.Greeter
                    ? GREETER_NPC_PARAM
                    : SpeedFogIds.DecorativeAmbusherNpcRow;
                spawn.ThinkParamID = spec.Kind == SpawnKind.Greeter ? SpeedFogIds.PassiveGreeterThinkRow : AMBUSH_THINK_PARAM;
                spawn.TalkID = 0;
                spawn.CharaInitID = -1;
                // CollisionPartName inherited from the cloned neighbor on purpose: the
                // nearest enemy stands on a valid collision in the same play space.
                msb.Parts.Enemies.Add(spawn);

                if (spec.Kind == SpawnKind.Greeter)
                    greeters++;
                else
                    ambushers++;
            }
        }

        return (greeters, ambushers);
    }

    /// <summary>
    /// Clone the Aging Untouchable's NpcThinkParam row (52800000) with every
    /// perception field zeroed, so the greeter never aggros on the player.
    /// </summary>
    public static void ApplyPassiveThinkRow(RegulationEditor reg)
    {
        var think = reg.GetParam("NpcThinkParam");
        if (think == null)
        {
            Console.WriteLine("Halloween spawns: NpcThinkParam unavailable, greeters stay vanilla-aggro");
            return;
        }
        Apply(think);
    }

    /// <summary>
    /// Lower-level entry point used by tests. Operates on an already-loaded
    /// PARAM (same split as PhantomCatalogInjector.ApplyTo/Apply), so the
    /// row-writing logic can be exercised against a real NpcThinkParam.xml
    /// paramdef without needing a RegulationEditor/regulation.bin. This is
    /// the codepath that shipped without its paramdef in a real publish
    /// (see e5a3212); PARAM-level coverage regression-guards that.
    /// </summary>
    public static void Apply(PARAM think)
    {
        var row = GameEditor.AddRow(think, SpeedFogIds.PassiveGreeterThinkRow, 52800000);
        // Storage types from Defs/NpcThinkParam.xml: ear_dist is f32, the rest u16.
        row["ear_dist"].Value = 0f;
        row["eye_dist"].Value = (ushort)0;
        row["nose_dist"].Value = (ushort)0;
        row["searchEye_dist"].Value = (ushort)0;
        row["BattleStartDist"].Value = (ushort)0;
        Console.WriteLine($"Halloween spawns: passive think row {SpeedFogIds.PassiveGreeterThinkRow} (clone of 52800000, perception zeroed)");
    }

    /// <summary>
    /// Clone the Sage's Cave skeleton NpcParam row into the decorative
    /// ambusher row (1 HP, no runes, near-zero attack via a custom
    /// SpEffect): ambushers are scenery that swings, not a threat. Same
    /// clone-plus-SpEffect mechanism as UntouchableBossInjector.Apply.
    /// </summary>
    public static void ApplyDecorativeAmbusherRows(RegulationEditor reg)
    {
        var npc = reg.GetParam("NpcParam");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (npc == null || sp == null)
        {
            Console.WriteLine("Halloween spawns: NpcParam/SpEffectParam unavailable, ambushers keep vanilla stats");
            return;
        }
        ApplyAmbusher(npc, sp);
    }

    /// <summary>PARAM-level entry point used by tests (same split as Apply).</summary>
    public static void ApplyAmbusher(PARAM npc, PARAM spEffect)
    {
        var spRow = GameEditor.AddRow(
            spEffect, SpeedFogIds.DecorativeAmbusherSpEffectRow, SCALING_TIER1_TEMPLATE_SPEFFECT);
        // Neutralize the template's own tier-1 multipliers (vanilla 7010:
        // maxHpRate 1.141); only the attack rates matter, since hp is
        // forced to 1 on the NpcParam row anyway. staminaAttackRate is in
        // the nerf list so blocked hits do not drain stamina either.
        spRow["maxHpRate"].Value = 1f;
        spRow["haveSoulRate"].Value = 1f;
        foreach (var field in new[]
        {
            "physicsAttackPowerRate", "magicAttackPowerRate", "fireAttackPowerRate",
            "thunderAttackPowerRate", "darkAttackPowerRate", "staminaAttackRate",
        })
        {
            spRow[field].Value = AMBUSH_ATTACK_RATE;
        }

        var npcRow = GameEditor.AddRow(
            npc, SpeedFogIds.DecorativeAmbusherNpcRow, AMBUSH_NPC_PARAM);
        npcRow["hp"].Value = 1u;       // u32: dies to any hit
        npcRow["getSoul"].Value = 0u;  // u32: no rune pinata
        ClearInheritedScalingSlots(npcRow);
        int slot = FirstFreeSpEffectSlot(npcRow);
        npcRow[$"spEffectID{slot}"].Value = SpeedFogIds.DecorativeAmbusherSpEffectRow; // s32

        Console.WriteLine(
            $"Halloween spawns: decorative ambusher NpcParam {SpeedFogIds.DecorativeAmbusherNpcRow} " +
            $"(clone of {AMBUSH_NPC_PARAM}, hp 1, runes 0) + SpEffect " +
            $"{SpeedFogIds.DecorativeAmbusherSpEffectRow} (attack x{AMBUSH_ATTACK_RATE}, slot {slot})");
    }

    // Vanilla 35000030 carries the game's own area-scaling SpEffect in one
    // of its slots (7080, tier 8: ~2.7x hp, 2x attack), which the clone
    // would inherit and stack onto the nerf, roughly doubling the "1 HP /
    // 0.01x" numbers. Clear every slot pointing into the scaling bands
    // (docs/enemy-scaling.md: 7000+10*tier and the DLC 20007xxx band) so
    // the decorative stats are exact.
    private static void ClearInheritedScalingSlots(PARAM.Row row)
    {
        for (int i = 0; i < 32; i++)
        {
            int value = (int)row[$"spEffectID{i}"].Value;
            if ((value >= 7000 && value <= 7200) || (value >= 20007000 && value <= 20007130))
                row[$"spEffectID{i}"].Value = -1;
        }
    }

    // NpcParam carries 32 SpEffect slots (spEffectID0-31); unused ones hold
    // -1 (0 also appears as a placeholder and counts as occupied). Scanned
    // instead of hardcoded (unlike UntouchableBossInjector's slot 19)
    // because the skeleton template's occupancy is not pinned by any
    // SpeedFog code and may shift with game patches.
    private static int FirstFreeSpEffectSlot(PARAM.Row row)
    {
        for (int i = 0; i < 32; i++)
        {
            if ((int)row[$"spEffectID{i}"].Value == -1)
                return i;
        }
        throw new InvalidOperationException(
            "No free spEffectID slot on the decorative ambusher NpcParam clone");
    }

    // --- Helper methods ---

    private static (int Greeters, int Ambushers) InjectMap(
        string modDir, string gameDir, string mapId, List<SpawnSpec> specs, Action<string> log)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            log($"  Warning: {msbFileName} not found, skipping ambient spawns for {mapId}");
            return (0, 0);
        }

        var msb = MSBE.Read(msbPath);
        var (greeters, ambushers) = ApplyToMsb(msb, specs, log);
        if (greeters + ambushers == 0)
            return (0, 0);

        var writePath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        return (greeters, ambushers);
    }

    private static MSBE.Part.Enemy? FindNearestVanillaEnemy(IReadOnlyList<MSBE.Part.Enemy> enemies, Vector3 targetPos)
    {
        MSBE.Part.Enemy? best = null;
        float bestDist = float.MaxValue;

        foreach (var enemy in enemies)
        {
            if (enemy.EntityID >= FOGMOD_ENTITY_MIN)
                continue;

            var diff = enemy.Position - targetPos;
            float dist = diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = enemy;
            }
        }

        return best;
    }
}

public enum SpawnKind { Greeter, Ambusher }

public readonly record struct SpawnSpec(
    string GatePartName, SpawnKind Kind, int IndexInPack, int PackSize, bool GateSideIsASide);
