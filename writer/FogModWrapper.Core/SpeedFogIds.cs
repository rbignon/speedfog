namespace FogModWrapper;

/// <summary>
/// Central registry of every EMEVD event ID range and auxiliary flag that
/// SpeedFog allocates itself. Injectors reference these instead of local
/// constants so the ranges are deconflicted in one place, and
/// SpeedFogIdsTests asserts they never overlap.
///
/// Bands:
/// - Event IDs live in the 7558600xx-7558649xx band, safely below FogMod's
///   entity/region base (755890000, see DeathMarkerInjector.FOGMOD_ENTITY_MIN).
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
}
