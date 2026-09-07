namespace FogModWrapper;

/// <summary>
/// Central registry of every EMEVD event ID range and auxiliary flag that
/// SpeedFog allocates itself. Injectors reference these instead of local
/// constants so the ranges are deconflicted in one place, and
/// SpeedFogIdsTests asserts they never overlap.
///
/// Bands:
/// - Event IDs live in the 7558600xx-7558650xx band, safely below FogMod's
///   entity/region base (see FogModEntityMin).
/// - Auxiliary flags live at the top of FogMod's 104029xxxx flag band
///   (9000+), which FogMod's own allocator does not reach.
/// - Persistent flags come from Python's PERSISTENT_FLAG_BASE (1050290000,
///   see speedfog/constants.py); offset 1 is reserved here for the banner.
/// </summary>
public static class SpeedFogIds
{
    /// <summary>A named, capacity-bounded event ID range owned by one injector.</summary>
    public sealed record IdRange(string Owner, int Base, int Capacity)
    {
        /// <summary>Exclusive upper bound.</summary>
        public int End => Base + Capacity;
    }

    /// <summary>FogMod's entity/region allocation floor: FogMod-managed MSB
    /// parts and regions sit at or above this value. Injectors use it to
    /// tell vanilla parts (below) from FogMod-managed ones (at or above)
    /// when picking clone sources or ground evidence.</summary>
    public const uint FogModEntityMin = 755890000;

    // --- EMEVD event ID ranges ---

    public static readonly IdRange StartingItemEvents =
        new("StartingItemInjector", 755860000, 100);

    public static readonly IdRange RoundtableUnlockEvents =
        new("RoundtableUnlockInjector", 755860100, 100);

    public static readonly IdRange StartingResourceEvents =
        new("StartingResourcesInjector", 755861000, 1000);

    public static readonly IdRange BossDeathMonitorEvents =
        new("ZoneTrackingInjector", 755862000, 100);

    /// <summary>One event per (death flag, map) pair; slots are pre-partitioned
    /// per map by DeathMarkerInjector.PlanAllocations (maps run in parallel).</summary>
    public static readonly IdRange DeathMarkerEvents =
        new("DeathMarkerInjector", 755862100, 900);

    public static readonly IdRange RunCompleteEvents =
        new("RunCompleteInjector", 755863000, 1000);

    public static readonly IdRange ChapelGraceEvents =
        new("ChapelGraceInjector", 755864000, 1000);

    public static readonly IdRange WeatherEvents =
        new("WeatherInjector", 755865000, 100);

    /// <summary>One unconditional CreateAssetfollowingSFX event per map (no
    /// flag wait, unlike DeathMarkerEvents); slots are pre-partitioned per
    /// map by GateDecorInjector.PlanAllocations.</summary>
    public static readonly IdRange HalloweenDecorEvents =
        new("GateDecorInjector", 755865100, 400);

    /// <summary>All event ranges, for the disjointness test.</summary>
    public static readonly IReadOnlyList<IdRange> EventRanges = new[]
    {
        StartingItemEvents,
        RoundtableUnlockEvents,
        StartingResourceEvents,
        BossDeathMonitorEvents,
        DeathMarkerEvents,
        RunCompleteEvents,
        ChapelGraceEvents,
        WeatherEvents,
        HalloweenDecorEvents,
    };

    // --- Auxiliary one-shot flags (FogMod band 104029xxxx, top slice) ---

    public const int ResourcesGivenFlag = 1040299000;
    public const int ItemsGivenFlag = 1040299001;
    public const int ChapelSpawnDoneFlag = 1040299002;

    /// <summary>All auxiliary flags SpeedFog claims, for the uniqueness test.</summary>
    public static readonly IReadOnlyList<int> AuxiliaryFlags = new[]
    {
        ResourcesGivenFlag,
        ItemsGivenFlag,
        ChapelSpawnDoneFlag,
    };

    // --- Shared flags (not allocated by SpeedFog) ---

    /// <summary>FogMod's Roundtable finger-pickup flag; SpeedFog sets it at
    /// start to bypass the pickup, and gates item events on it.</summary>
    public const int FingerPickupFlag = 1040292051;

    /// <summary>Persistent saved flag (PERSISTENT_FLAG_BASE + 1); guards the
    /// RUN COMPLETE banner one-shot. Offset 0 (items_spawned_flag) is
    /// allocated Python-side and arrives via graph.json.</summary>
    public const int BannerShownFlag = 1050290001;

    // --- MSB entity ID bases (not EMEVD event ids; a separate id space) ---

    /// <summary>DeathMarkerInjector's per-map entity ID allocation floor:
    /// bloodstain markers are placed at or above this value. Also doubles as
    /// the exclusive upper bound of FogMod's own entity range (see
    /// FogModEntityMin), the boundary between the two bands.</summary>
    public const uint DeathMarkerEntityBase = 755900000;

    /// <summary>MSB entity IDs for Halloween gate decorations. Disjoint from
    /// FogMod (<see cref="FogModEntityMin"/>, 755890000+) and the death
    /// markers (<see cref="DeathMarkerEntityBase"/>, 755900000+).</summary>
    public const uint HalloweenDecorEntityBase = 755910000;

    // --- Param row IDs (not entity IDs; separate namespace per PARAM) ---

    // Param row namespaces are per-PARAM (NpcThinkParam, NpcParam,
    // SpEffectParam, Bullet, AtkParam_Npc never share ids), so the same three
    // numeric values below are reused across the nine rows with no collision
    // (Bullet additionally owns the pulse band 755890003-005).
    // They sit in FogMod's entity band only by convention (params and entities
    // are unrelated id spaces); no vanilla row in any of the five params
    // comes anywhere near them, and phantom skins (1450700-1450799) /
    // FogMod scaling (7800000-7804487) live in disjoint SpEffectParam
    // territory.
    private const int ParamRowBase0 = 755890000;
    private const int ParamRowBase1 = 755890001;
    private const int ParamRowBase2 = 755890002;

    /// <summary>NpcThinkParam row for the passive Halloween greeters:
    /// a clone of the Aging Untouchable's think row (52800000) with all
    /// perception zeroed.</summary>
    public const int PassiveGreeterThinkRow = ParamRowBase0;

    /// <summary>NpcParam row for the Aging Untouchable minor boss: a clone
    /// of vanilla 52800086 with boss-level HP/runes and nerflantern's
    /// resident 20011471 scrubbed from its slots. Deliberately outside the 5280xxxx
    /// band: the item randomizer's always-on nerflantern option patches
    /// every 5280-band NpcParam row, and the boss's vulnerability must
    /// stay under SpeedFog's control.</summary>
    public const int UntouchableBossNpcRow = ParamRowBase0;

    /// <summary>SpEffectParam row for the boss's partial wall: a clone of
    /// the vanilla parry-window effect 20011471 (category 1001) with the
    /// eight damage-type cut rates lowered and vanilla's boss damage-level
    /// table (5300) folded in, i.e. the vanilla wall's shape with damage
    /// let through. Not resident on the NpcParam
    /// row: it replaces the vanilla full wall (20011470) inside the wall
    /// event the enemy randomizer copies for each placed boss, so the
    /// vanilla flow clears it on the first parry.</summary>
    public const int UntouchableBossSpEffectRow = ParamRowBase0;

    /// <summary>Vanilla MSB entity id of the allowlist source part
    /// (c5280_9000 in m61_49_42); enemy_assignments values equal to it
    /// mark enemy-randomizer-placed untouchable bosses.</summary>
    public const uint UntouchableSourceEntity = 2049420200;

    /// <summary>NpcParam row for the decorative Halloween ambushers: a
    /// clone of the Sage's Cave skeleton (35000030) with token HP, no runes,
    /// and the near-zero attack SpEffect attached.</summary>
    public const int DecorativeAmbusherNpcRow = ParamRowBase1;

    /// <summary>SpEffectParam row multiplying the ambushers' attack power
    /// rates down to near zero: a clone of the tier-1 scaling row (7010,
    /// see docs/enemy-scaling.md).</summary>
    public const int DecorativeAmbusherSpEffectRow = ParamRowBase1;

    /// <summary>NpcThinkParam row for the Aging Untouchable boss: a clone
    /// of 52800000 whose battleGoalID selects SpeedFog's own battle script
    /// (<see cref="UntouchableBossBattleGoal"/>), leaving the ambient
    /// untouchables on the vanilla bytecode script. ParamRowBase0 in this
    /// namespace is <see cref="PassiveGreeterThinkRow"/>.</summary>
    public const int UntouchableBossThinkRow = ParamRowBase1;

    /// <summary>Bullet row for the boss's frenzy beam: a clone of Frenzied
    /// Burst (10732000, SFX in the common bundle) whose atkId points at
    /// <see cref="UntouchableBeamAtkRow"/>.</summary>
    public const int UntouchableBeamBulletRow = ParamRowBase0;

    /// <summary>AtkParam_Npc row carrying the beam's damage: a clone of the
    /// lantern swing (5280115, magic, no throw). Player spells only have
    /// AtkParam_Pc rows, so an NPC-fired Frenzied Burst needs this one.</summary>
    public const int UntouchableBeamAtkRow = ParamRowBase0;

    /// <summary>SpEffectParam row applied by the copied wall event right
    /// after the break VFX, once the partial wall is cleared: a permanent
    /// clone of the break VFX effect 20011472 (category 0, no VFX) whose
    /// eight cut rates make the boss take twice the vanilla damage, four
    /// times what it took behind the partial wall.</summary>
    public const int UntouchableBrokenSpEffectRow = ParamRowBase2;

    /// <summary>First of three consecutive Bullet rows (755890003-005): the
    /// lantern's ambient pulse bullets (vanilla 205280000-002, judges
    /// 100-102) cloned without their madness SpEffect, referenced by the boss
    /// behavior variation only.</summary>
    public const int UntouchablePulseBulletBase = 755890003;

    // --- Icon ids (not entity ids, not param row ids; a third id namespace) ---

    /// <summary>Icon id for the Halloween Golden Seed pumpkin icon. The
    /// texture MENU_ItemIcon_60383 ships in the speedfog-halloween
    /// overlay's 05_dummy.tpf.dcx superset; the id is u16-safe and far
    /// above every vanilla icon id (max ~8490). Vanilla iconId: 383.</summary>
    public const int HalloweenGoldenSeedIcon = 60383;

    /// <summary>Icon id for the Halloween Sacred Tear icon, a corrupted
    /// burning-chalice artwork (MENU_ItemIcon_60384, same overlay).
    /// Vanilla iconId: 384 (EquipParamGoods row 10020, renamed
    /// "Profane Tear" by the text catalogue).</summary>
    public const int HalloweenSacredTearIcon = 60384;

    // --- FMG text ids (NpcName.fmg entry ids: a separate namespace from
    //     entity ids, event ids and param rows) ---

    /// <summary>NpcName entries BossNameInjector adds for promoted mobs whose
    /// display name has no vanilla NpcName entry (graph.json boss_names):
    /// one id per distinct name, engus + frafr. Vanilla ids sit in the
    /// 9xxxxxxxx band and the Item Randomizer's editnames allocator (off in
    /// SpeedFog) starts at 907770000, so the 75589xxxx SpeedFog band is free
    /// here.</summary>
    public static readonly IdRange BossNameFmgIds =
        new("BossNameInjector", 755890000, 100);

    // --- Behavior ids (a fourth id kind: the battle-script id selected by
    //     NpcThinkParam.battleGoalID, NpcParam.behaviorVariationId, and a
    //     TAE bullet judge id; none of them is a param row id) ---

    /// <summary>NpcThinkParam.battleGoalID of the boss: the game loads
    /// script/755890_battle.luabnd.dcx (source under
    /// data/mods-src/speedfog/script/, repacked at bootstrap).</summary>
    public const int UntouchableBossBattleGoal = 755890;

    /// <summary>NpcParam.behaviorVariationId of the boss clone. BehaviorParam
    /// rows are keyed by <see cref="BehaviorRowId"/>, so the boss's rows live
    /// at 275589xxx, disjoint from vanilla c5280's 252800xxx.</summary>
    public const int UntouchableBossBehaviorVariation = 75589;

    /// <summary>TAE bullet judge id written on animation 3004's retargeted
    /// events by StaticModBuilder's UntouchableTaePatcher (which references
    /// this project for it).
    /// Vanilla c5280 uses judges 100-115 and 500; 150 resolves to nothing
    /// under variation 52800, so the patched animation stays inert for
    /// ambient untouchables.</summary>
    public const int UntouchableBeamJudge = 150;

    /// <summary>BehaviorParam row id for a (variation, judge) pair: the
    /// game's own composite key (vanilla c5280 judge 100 is 252800100).</summary>
    public static int BehaviorRowId(int variationId, int judgeId)
        => 200000000 + variationId * 1000 + judgeId;
}
