using System.Numerics;
using FogModWrapper.Common;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper.Plugins.Halloween;

/// <summary>
/// Places ambient enemy spawns at cluster exit gates for the Halloween
/// plugin: unless greeters are disabled, a hostile "greeter" (an Aging
/// Untouchable or a Wormface, GreeterProfile, with vanilla AI, flat
/// GREETER_TIER stats, no runes or drop via a cloned NpcParam row,
/// ApplyHostileGreeter, and a GREETER_AGGRO_DIST aggro radius via a cloned
/// think row, ApplyGreeterThink) standing watch beside every exit gate of a
/// mini_dungeon/legacy_dungeon cluster
/// (HalloweenGateAnchors.SpawnClusterTypes: unlike decorations, no spawns
/// at the start cluster), plus, when ambushes are enabled, a small
/// skeleton or basilisk pack (AmbusherProfile) sharing the gate's arc.
/// Ambushers aggro normally but are decorative: token HP, no runes,
/// near-zero attack via a cloned NpcParam row (ApplyAmbusher).
/// Boss arenas never receive spawns.
///
/// Two-phase injection, mirroring DeathMarkerInjector:
/// 1. MSB phase (ApplyToMsb): clone a nearby vanilla enemy per anchored
///    gate, retarget it to the greeter/ambusher model. Driven per map by
///    HalloweenAmbientPass, which reads/writes the MSB once alongside
///    GateDecorInjector's own MSB phase.
/// 2. Regulation phase (ApplyHostileGreeterRow, ApplyDecorativeAmbusherRows):
///    clone the greeter's NpcParam and NpcThinkParam rows and the
///    ambushers' NpcParam row.
///
/// Must run AFTER GateDecorInjector's placement: the spawns carry EntityID 0
/// and would otherwise pollute the decor injector's vanilla-enemy ground
/// evidence (enforced by HalloweenAmbientPass's per-map ordering).
/// </summary>
public static class AmbientSpawnInjector
{
    /// <summary>
    /// What a greeter of each species is cloned from. Placed greeters always
    /// use SpeedFogIds.HostileGreeterNpcRow / HostileGreeterThinkRow, the
    /// clones of these vanilla templates (one greeter species per seed):
    /// - Untouchable: Aging Untouchable c5280, NpcParam 52800086 (DLC tier
    ///   31), think 52800000 (sees and hears from 30m).
    /// - Wormface: c4570 of Altus Plateau (m60_40_53), NpcParam 45700030
    ///   (tier 8), think 45700100 (already at the 10m greeter cap). Its
    ///   Deathblight (mist, grab) is vanilla and untouched.
    /// </summary>
    internal static SpawnProfile GreeterProfile(HalloweenPluginSettings.GreeterSpecies species) => species switch
    {
        HalloweenPluginSettings.GreeterSpecies.Untouchable => new("c5280", 52800086, 52800000),
        HalloweenPluginSettings.GreeterSpecies.Wormface => new("c4570", 45700030, 45700100),
        _ => throw new ArgumentOutOfRangeException(nameof(species), species, null),
    };

    /// <summary>
    /// What an ambusher of each species is cloned from. Placed ambushers use
    /// SpeedFogIds.DecorativeAmbusherNpcRow (the clone of NpcTemplate) and
    /// the species' vanilla think row (ThinkTemplate, vanilla perception):
    /// - Skeleton: c3500 of Sage's Cave, NpcParam 35000030, think 35000000.
    /// - Basilisk: c4150 of Ainsel River (m12_01), NpcParam 41500060 (tier
    ///   5, no drop in vanilla), think 41500000 (sees from 16m). Its
    ///   Deathblight cloud survives the attack nerf: status buildup is not
    ///   an attack power rate.
    /// </summary>
    internal static SpawnProfile AmbusherProfile(HalloweenPluginSettings.AmbusherSpecies species) => species switch
    {
        HalloweenPluginSettings.AmbusherSpecies.Skeleton => new("c3500", 35000030, 35000000),
        HalloweenPluginSettings.AmbusherSpecies.Basilisk => new("c4150", 41500060, 41500000),
        _ => throw new ArgumentOutOfRangeException(nameof(species), species, null),
    };

    /// <summary>Aggro distance of the greeters, in meters: the cap on every
    /// perception radius of their think row (the vanilla untouchable sees
    /// and hears from 30m; the wormface's row is already within it).
    /// Greeters stand 4-6m from their gate, so they still react to a
    /// player walking up to the exit fog. An integer: four of the five
    /// radii are u16 fields.</summary>
    public const int GREETER_AGGRO_DIST = 10;
    /// <summary>Base-game scaling tier the greeters fight at, everywhere in
    /// the run (they are placed after FogMod's scaling pass): the greeter
    /// row carries scaling SpEffect 7000 + 10 * tier instead of the
    /// template's own area tier, the stats FogMod would give the enemy
    /// moved to an area of that tier (tier 6: untouchable ~730 HP, attack
    /// x1.76 on DLC base attack values vs x3.8 in vanilla's DLC tier 31;
    /// wormface ~1410 HP, from tier 8). Shared by every greeter species and
    /// the jumpscare actors. The in-game tuning knob, 1-20 only: DLC tiers
    /// use another row band (docs/enemy-scaling.md).</summary>
    public const int GREETER_TIER = 6;
    // Tier-1 enemy-scaling row (7000 + 10 * tier, docs/enemy-scaling.md):
    // the SpEffect clone template for the ambushers' attack nerf. A pure
    // rate-multiplier row with no stateInfo side effects.
    internal const int SCALING_TIER1_TEMPLATE_SPEFFECT = 7010;
    /// <summary>Attack power multiplier for decorative ambushers: they are
    /// scenery that swings, not a threat, so damage floors out at ~nothing.</summary>
    public const float AMBUSH_ATTACK_RATE = 0.01f;
    /// <summary>Token HP for decorative ambushers: should fold to any
    /// serious player hit (in-game checklist verifies) while surviving
    /// ambient chip damage. 1 HP self-destructed in-game (the skeletons'
    /// own collapse/assembly mechanics finished them off before the player
    /// ever saw them).</summary>
    public const uint AMBUSH_HP = 100u;
    // Clone sources farther than this from their gate are logged: the
    // inherited collision may belong to another part of the map (Tombsward
    // Cave's nearest collision-bearing candidate stands 96m away, 29m lower),
    // so those gates are the ones to check in-game first.
    private const float FAR_CLONE_SOURCE_DISTANCE = 40f;
    internal const float GREETER_MIN_RADIUS = 4.0f;
    internal const float GREETER_MAX_RADIUS = 6.0f;
    // Spawn arcs are much narrower than the decor's 120-degree default:
    // spawns sit 3-7m out, where a wide arc regularly clipped them into
    // corridor walls in-game (fog gates sit in doorways). Decorations stay
    // wide: their 1.5-4m radii keep them off the walls.
    internal const float GREETER_ARC_SPREAD = 60f;
    private const float AMBUSH_MIN_RADIUS = 3.0f;
    private const float AMBUSH_MAX_RADIUS = 7.0f;
    internal const float AMBUSH_ARC_SPREAD = 80f;

    /// <summary>
    /// Collect spawn specs per map, keyed by the anchored gate's map id.
    /// Anchors (exit gates of mini_dungeon/legacy_dungeon clusters, deduped
    /// per (map, gate part name) pair) come from HalloweenGateAnchors.
    /// Collect with SpawnClusterTypes; each expands to one Greeter when
    /// settings.Greeters, plus, when settings.Ambushes, a pack of 2-3
    /// Ambushers, each spec carrying its configured species' profile. Maps
    /// left without any spec get no entry.
    /// </summary>
    internal static Dictionary<string, List<SpawnSpec>> CollectSpawnSpecsByMap(
        List<Connection> connections,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HalloweenPluginSettings.Settings settings)
    {
        var result = new Dictionary<string, List<SpawnSpec>>();
        var greeter = GreeterProfile(settings.GreeterSpecies);
        var ambusher = AmbusherProfile(settings.AmbusherSpecies);

        foreach (var (mapId, anchors) in HalloweenGateAnchors.Collect(
            connections, nodes, gateSides, HalloweenGateAnchors.SpawnClusterTypes))
        {
            var specs = new List<SpawnSpec>();

            foreach (var anchor in anchors)
            {
                if (settings.Greeters)
                    specs.Add(new SpawnSpec(anchor.PartName, SpawnKind.Greeter, 0, 1, anchor.IsASide, greeter));

                if (settings.Ambushes)
                {
                    int packSize = 2 + new Random(StablePartNameHash(anchor.PartName)).Next(2);
                    for (int i = 0; i < packSize; i++)
                        specs.Add(new SpawnSpec(anchor.PartName, SpawnKind.Ambusher, i, packSize, anchor.IsASide, ambusher));
                }
            }

            if (specs.Count > 0)
                result[mapId] = specs;
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
    /// clone (see CloneSources), logging sources farther than
    /// FAR_CLONE_SOURCE_DISTANCE. Maps with no vanilla enemy to clone from
    /// are logged and skipped. Returns the number of greeters
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
        // just like a real vanilla enemy would. A later gate in the same map
        // must never pick an already-placed spawn as its clone source, or the
        // "nearest enemy stands on a valid collision in the same play space"
        // rationale for the inherited CollisionPartName no longer holds.
        var cloneSources = CloneSources(msb.Parts.Enemies);

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

            var baseEnemy = cloneSources.MinBy(e => Vector3.DistanceSquared(e.Position, gateAsset.Position));
            if (baseEnemy == null)
            {
                log($"  Warning: No vanilla enemy to clone from, skipping ambient spawns for gate '{partName}'");
                continue;
            }
            float sourceDistance = Vector3.Distance(baseEnemy.Position, gateAsset.Position);
            if (sourceDistance > FAR_CLONE_SOURCE_DISTANCE)
            {
                log($"  Gate '{partName}': clone source {baseEnemy.Name} is {sourceDistance:F0}m away " +
                    $"(dy {baseEnemy.Position.Y - gateAsset.Position.Y:F1}m, " +
                    $"collision {baseEnemy.CollisionPartName ?? "none"})");
            }

            foreach (var spec in partSpecs)
            {
                var model = spec.Profile.Model;
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
                    spec.Kind == SpawnKind.Greeter ? GREETER_ARC_SPREAD : AMBUSH_ARC_SPREAD);
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
                    ? SpeedFogIds.HostileGreeterNpcRow
                    : SpeedFogIds.DecorativeAmbusherNpcRow;
                spawn.ThinkParamID = spec.Kind == SpawnKind.Greeter
                    ? SpeedFogIds.HostileGreeterThinkRow
                    : spec.Profile.ThinkTemplate;
                spawn.TalkID = 0;
                spawn.CharaInitID = -1;
                // The nearest-neighbor clone source may patrol a walk route; greeters
                // and ambushers must stand their ground, not inherit it.
                spawn.WalkRouteName = null;
                // CollisionPartName inherited from the cloned neighbor on purpose
                // (see CloneSources for when a collision is required).
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
    /// Clone the species' NpcParam row (GreeterProfile) into the hostile
    /// greeter row: vanilla AI and moveset (the untouchable's madness
    /// lantern, the wormface's Deathblight), but a flat GREETER_TIER scaling
    /// instead of the template's own area tier, no runes and no item drop.
    /// Same clone mechanism as ApplyDecorativeAmbusherRows. Also writes the
    /// greeter think row (ApplyGreeterThink, shorter aggro). Returns false
    /// when the rows could not be written: the MSB phase must then place no
    /// greeter, or the parts would point at missing rows.
    /// </summary>
    public static bool ApplyHostileGreeterRow(RegulationEditor reg, HalloweenPluginSettings.GreeterSpecies species)
    {
        var npc = reg.GetParam("NpcParam");
        var think = reg.GetParam("NpcThinkParam");
        if (npc == null || think == null)
        {
            Console.WriteLine("Halloween spawns: NpcParam/NpcThinkParam unavailable, greeter rows not written");
            return false;
        }
        ApplyHostileGreeter(npc, species);
        ApplyGreeterThink(think, species);
        return true;
    }

    /// <summary>PARAM-level entry point used by tests (same split as ApplyAmbusher).</summary>
    public static void ApplyHostileGreeter(PARAM npc, HalloweenPluginSettings.GreeterSpecies species)
    {
        int template = GreeterProfile(species).NpcTemplate;
        int slot = CloneFlatTier(npc, SpeedFogIds.HostileGreeterNpcRow, template);

        Console.WriteLine(
            $"Halloween spawns: hostile {species} greeter NpcParam {SpeedFogIds.HostileGreeterNpcRow} " +
            $"(clone of {template}, tier {GREETER_TIER} scaling {7000 + 10 * GREETER_TIER} in slot {slot}, runes 0, no drop)");
    }

    /// <summary>
    /// Clone the Aging Untouchable's NpcParam row (52800086) into rowId at
    /// the flat GREETER_TIER scaling, with no runes and no item drop:
    /// HalloweenJumpscareInjector's actor row, an untouchable whatever the
    /// greeter species. Returns the SpEffect slot holding the tier row.
    /// </summary>
    internal static int CloneFlatTierUntouchable(PARAM npc, int rowId)
        => CloneFlatTier(npc, rowId, GreeterProfile(HalloweenPluginSettings.GreeterSpecies.Untouchable).NpcTemplate);

    /// <summary>
    /// Clone a vanilla NpcParam row into rowId at the flat GREETER_TIER
    /// scaling, with no runes and no item drop. Returns the SpEffect slot
    /// holding the tier row.
    /// </summary>
    private static int CloneFlatTier(PARAM npc, int rowId, int templateRow)
    {
        var row = GameEditor.AddRow(npc, rowId, templateRow);
        // The template carries its own area scaling (untouchable 52800086:
        // DLC tier 31, 20007100, hp x13.6, attack x3.8; wormface 45700030:
        // tier 8); FogMod never rescales these clones, so the vanilla tier
        // row of GREETER_TIER takes its place.
        int slot = ClearScalingSlotsAndFindFree(row);
        row[$"spEffectID{slot}"].Value = 7000 + 10 * GREETER_TIER; // s32
        row["getSoul"].Value = 0u;  // u32: no rune farm
        row["itemLotId_enemy"].Value = -1;  // s32: no drop
        return slot;
    }

    /// <summary>PARAM-level entry point used by tests: clone the species'
    /// think row (GreeterProfile) into the greeter think row, same AI, every
    /// perception radius capped at GREETER_AGGRO_DIST. A cap, not a set: a
    /// radius already shorter (the untouchable's 2m smell, every radius of
    /// the wormface's row) stays as it is.</summary>
    public static void ApplyGreeterThink(PARAM think, HalloweenPluginSettings.GreeterSpecies species)
    {
        int template = GreeterProfile(species).ThinkTemplate;
        var row = GameEditor.AddRow(think, SpeedFogIds.HostileGreeterThinkRow, template);
        // Storage types from Defs/NpcThinkParam.xml: ear_dist is f32, the rest u16.
        row["ear_dist"].Value = Math.Min((float)row["ear_dist"].Value, (float)GREETER_AGGRO_DIST);
        foreach (var field in new[] { "eye_dist", "searchEye_dist", "BattleStartDist", "nose_dist" })
        {
            row[field].Value = (ushort)Math.Min((int)(ushort)row[field].Value, GREETER_AGGRO_DIST);
        }

        Console.WriteLine(
            $"Halloween spawns: greeter think row {SpeedFogIds.HostileGreeterThinkRow} " +
            $"(clone of {template}, perception capped at {GREETER_AGGRO_DIST}m)");
    }

    /// <summary>
    /// Clone the species' NpcParam row (AmbusherProfile) into the decorative
    /// ambusher row (token HP, no runes, near-zero attack via a custom
    /// SpEffect): ambushers are scenery that swings, not a threat. Status
    /// buildup is not an attack power rate, so a species' own status attack
    /// (the basilisk's Deathblight cloud) keeps its vanilla strength. Same
    /// clone-plus-SpEffect mechanism as UntouchableBossInjector.Apply.
    /// Returns false when the rows could not be written (see
    /// ApplyHostileGreeterRow).
    /// </summary>
    public static bool ApplyDecorativeAmbusherRows(RegulationEditor reg, HalloweenPluginSettings.AmbusherSpecies species)
    {
        var npc = reg.GetParam("NpcParam");
        var sp = reg.GetParam("SpEffectParam", "SpEffect");
        if (npc == null || sp == null)
        {
            Console.WriteLine("Halloween spawns: NpcParam/SpEffectParam unavailable, ambusher rows not written");
            return false;
        }
        ApplyAmbusher(npc, sp, species);
        return true;
    }

    /// <summary>PARAM-level entry point used by tests (same split as Apply).</summary>
    public static void ApplyAmbusher(PARAM npc, PARAM spEffect, HalloweenPluginSettings.AmbusherSpecies species)
    {
        int template = AmbusherProfile(species).NpcTemplate;
        var spRow = GameEditor.AddRow(
            spEffect, SpeedFogIds.DecorativeAmbusherSpEffectRow, SCALING_TIER1_TEMPLATE_SPEFFECT);
        // Neutralize the template's own tier-1 multipliers (vanilla 7010:
        // maxHpRate 1.141); hp is forced to AMBUSH_HP on the NpcParam row
        // and maxHpRate 1 keeps that value exact. staminaAttackRate is in
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
            npc, SpeedFogIds.DecorativeAmbusherNpcRow, template);
        npcRow["hp"].Value = AMBUSH_HP;  // u32: token HP, dies to any real hit
        npcRow["getSoul"].Value = 0u;  // u32: no rune pinata
        int slot = ClearScalingSlotsAndFindFree(npcRow);
        npcRow[$"spEffectID{slot}"].Value = SpeedFogIds.DecorativeAmbusherSpEffectRow; // s32

        Console.WriteLine(
            $"Halloween spawns: decorative {species} ambusher NpcParam {SpeedFogIds.DecorativeAmbusherNpcRow} " +
            $"(clone of {template}, hp {AMBUSH_HP}, runes 0) + SpEffect " +
            $"{SpeedFogIds.DecorativeAmbusherSpEffectRow} (attack x{AMBUSH_ATTACK_RATE}, slot {slot})");
    }

    // Every clone template carries the game's own area-scaling SpEffect in
    // one of its slots: vanilla skeleton 35000030 has 7080 (tier 8: ~2.7x hp,
    // 2x attack) and basilisk 41500060 has 7050, which would stack onto the
    // ambusher nerf and multiply the AMBUSH_HP / 0.01x numbers; vanilla
    // untouchable 52800086 has 20007100 (DLC tier 31) and wormface 45700030
    // has 7080, which the greeter's GREETER_TIER row replaces. One 0..31 pass
    // clears every slot pointing into the scaling bands
    // (docs/enemy-scaling.md: 7000+10*tier and the DLC 20007xxx band) so the
    // cloned stats are exact, while tracking the first free (-1) slot seen
    // so far. Clearing always runs to slot 31 (no early exit once a free
    // slot is found): a scaling slot past the free one still needs
    // neutralizing. NpcParam carries 32 SpEffect slots (spEffectID0-31);
    // unused ones hold -1 (0 also appears as a placeholder and counts as
    // occupied). Scanned instead of hardcoded because the
    // templates' occupancy is not pinned by any SpeedFog code and may shift
    // with game patches.
    private static int ClearScalingSlotsAndFindFree(PARAM.Row row)
    {
        int freeSlot = -1;
        for (int i = 0; i < 32; i++)
        {
            int value = (int)row[$"spEffectID{i}"].Value;
            if ((value >= 7000 && value <= 7200) || (value >= 20007000 && value <= 20007130))
            {
                row[$"spEffectID{i}"].Value = -1;
                value = -1;
            }
            if (freeSlot == -1 && value == -1)
                freeSlot = i;
        }
        if (freeSlot == -1)
        {
            throw new InvalidOperationException(
                $"No free spEffectID slot on the NpcParam clone {row.ID}");
        }
        return freeSlot;
    }

    // --- Helper methods ---

    /// <summary>
    /// The map's vanilla enemies (outside FogMod/SpeedFog's managed entity
    /// band) eligible as clone sources; each gate clones the nearest one. A
    /// spawn keeps its source's CollisionPartName, so the pool follows the
    /// map's own convention: when at least half of the vanilla enemies stand
    /// on a named collision (dungeon maps, 67-100%), collisionless ones are
    /// dropped, since c1000 dummies near catacomb boss fogs often have none
    /// and a greeter cloned from one never showed up in-game. Open-world
    /// tiles leave nearly every enemy collisionless (at most ~13% name one),
    /// so all their enemies stay eligible and a rare cellar or interior enemy
    /// does not become every spawn's source. Also the clone pool of
    /// HalloweenJumpscareInjector.
    /// </summary>
    internal static List<MSBE.Part.Enemy> CloneSources(IEnumerable<MSBE.Part.Enemy> enemies)
    {
        var vanilla = enemies.Where(e => !SpeedFogIds.IsModManagedEntity(e.EntityID)).ToList();
        var withCollision = vanilla.Where(e => !string.IsNullOrEmpty(e.CollisionPartName)).ToList();
        return withCollision.Count * 2 >= vanilla.Count ? withCollision : vanilla;
    }
}

public enum SpawnKind { Greeter, Ambusher }

public readonly record struct SpawnSpec(
    string GatePartName, SpawnKind Kind, int IndexInPack, int PackSize, bool GateSideIsASide,
    SpawnProfile Profile);

/// <summary>The species a spawn is made of: its chr model and the vanilla
/// NpcParam/NpcThinkParam rows its regulation rows are cloned from
/// (AmbientSpawnInjector.GreeterProfile/AmbusherProfile).</summary>
public readonly record struct SpawnProfile(string Model, int NpcTemplate, int ThinkTemplate);
