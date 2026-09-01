using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places bloodstain visual markers near fog gates that lead to dangerous zones.
///
/// Only exit gates (the fog the player sees before entering a zone) receive
/// bloodstains, not entrance gates inside the destination zone. This gives a
/// clear signal: "deaths have occurred beyond this fog gate".
///
/// Requires death flags (from racing mod). When deathFlags is empty, no
/// bloodstains are placed. Each bloodstain is controlled by a per-cluster
/// death flag via a dedicated EMEVD event that waits for the flag.
///
/// For each fog gate, up to 3 AEG099_090 anchor assets are placed in a 120-degree
/// arc in front of the gate, with a red glow SFX (DummyPoly 100, SfxID 42).
/// See docs/death-markers.md for the full design.
/// </summary>
public static class DeathMarkerInjector
{
    private const string BLOODSTAIN_MODEL = "AEG099_090";
    private const int SFX_DUMMY_POLY = 100;
    private const int SFX_ID = 42;

    private const int BLOODSTAINS_PER_GATE = 3;
    private const float MIN_RADIUS = 1.5f;
    private const float MAX_RADIUS = 3.0f;
    private const float Y_OFFSET = 0.13f;

    private static readonly int DEATH_MARKER_EVENT_BASE = SpeedFogIds.DeathMarkerEvents.Base;

    private readonly struct BloodstainSpec
    {
        public readonly string PartName;
        public readonly int DeathFlag;
        public readonly int TierIndex;  // 0=low, 1=med, 2=high
        public readonly bool IsASide;   // true = approach from ASide (gate facing direction)

        public BloodstainSpec(string partName, int deathFlag, int tierIndex, bool isASide)
        {
            PartName = partName;
            DeathFlag = deathFlag;
            TierIndex = tierIndex;
            IsASide = isASide;
        }
    }

    /// <summary>
    /// Inject bloodstain visual markers at exit fog gates in the DAG.
    /// Requires deathFlags to be non-empty; returns immediately otherwise.
    /// Each bloodstain is controlled by a per-cluster death flag via a dedicated
    /// EMEVD event. gateSides maps gate FullName to (ASideArea, BSideArea) from
    /// fog.txt, used to determine which side of the gate the bloodstains are on.
    /// Maps are processed in parallel (independent MSB/EMEVD files); entity and
    /// event IDs are pre-partitioned per map so the output stays deterministic.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections, Events events,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        if (deathFlags.Count == 0)
        {
            Console.WriteLine("No death flags provided, skipping death markers.");
            return;
        }

        Console.WriteLine("Injecting death markers at fog gates...");

        var specsByMap = CollectExitGatesByMap(connections, eventMap, deathFlags, gateSides);
        var work = specsByMap.ToList();
        var plans = PlanAllocations(work.Select(kv =>
            (kv.Key, kv.Value.Count, kv.Value.Select(s => s.DeathFlag).Distinct().Count())));

        int totalAssets = 0;
        int totalMaps = 0;

        MsbHelper.ForEachWithBufferedLogs(work.Zip(plans), (pair, log) =>
        {
            var (mapId, specs) = pair.First;
            var plan = pair.Second;
            int count = InjectMap(
                modDir, gameDir, events, mapId, specs,
                plan.EntityIdBase, plan.EventOffsetBase, log);
            if (count > 0)
            {
                Interlocked.Add(ref totalAssets, count);
                Interlocked.Increment(ref totalMaps);
            }
        });

        Console.WriteLine($"  Placed {totalAssets} bloodstain markers across {totalMaps} maps");
    }

    internal sealed record MapAllocation(string MapId, uint EntityIdBase, int EventOffsetBase);

    /// <summary>
    /// Partition the entity ID and event ID spaces per map, in map order,
    /// so parallel processing cannot change the generated IDs. Each map gets
    /// one entity ID per spec and one event slot per distinct death flag
    /// (upper bounds: specs skipped for missing gates leave unused gaps).
    /// Throws upfront when the summed event slots exceed the range budget.
    /// </summary>
    internal static List<MapAllocation> PlanAllocations(
        IEnumerable<(string MapId, int SpecCount, int DistinctFlagCount)> maps)
    {
        // Entity IDs start above FogMod's range to avoid collisions without
        // scanning MSBs. FogMod allocates from SpeedFogIds.FogModEntityMin
        // (755890000) and uses far fewer than the 10000 available IDs in a
        // typical DAG.
        uint entityBase = SpeedFogIds.DeathMarkerEntityBase;
        int eventBase = 0;
        var plans = new List<MapAllocation>();
        foreach (var (mapId, specCount, distinctFlagCount) in maps)
        {
            plans.Add(new MapAllocation(mapId, entityBase, eventBase));
            entityBase += (uint)specCount;
            eventBase += distinctFlagCount;
        }
        if (eventBase > SpeedFogIds.DeathMarkerEvents.Capacity)
        {
            throw new InvalidOperationException(
                $"Death marker event budget exceeded: {eventBase} events " +
                $"(max {SpeedFogIds.DeathMarkerEvents.Capacity}, " +
                $"next range starts at {SpeedFogIds.DeathMarkerEvents.End})");
        }
        return plans;
    }

    private static Dictionary<string, List<BloodstainSpec>> CollectExitGatesByMap(
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        var result = new Dictionary<string, List<BloodstainSpec>>();

        foreach (var conn in connections)
        {
            if (!eventMap.TryGetValue(conn.FlagId.ToString(), out var clusterId))
                continue;
            if (!deathFlags.TryGetValue(clusterId, out var flags))
                continue;

            // Only place bloodstains at the exit gate (the fog the player sees
            // before entering the dangerous zone), not at the entrance gate inside
            // the destination zone.
            var (mapId, partName) = GateGeometry.ParseGateFullName(conn.ExitGate);
            bool isASide = GateGeometry.ResolveIsASide(conn.ExitGate, conn.ExitArea, gateSides);

            if (!result.TryGetValue(mapId, out var specs))
            {
                specs = new List<BloodstainSpec>();
                result[mapId] = specs;
            }

            for (int tier = 0; tier < flags.Count; tier++)
            {
                if (!specs.Any(s => s.PartName == partName && s.DeathFlag == flags[tier]))
                    specs.Add(new BloodstainSpec(partName, flags[tier], tier, isASide));
            }
        }

        return result;
    }

    /// <summary>
    /// Inject bloodstain markers for a single map.
    /// Each bloodstain is activated only when its death flag is set.
    /// Entity IDs grouped by death flag produce one EMEVD event per (flag, map) pair.
    /// Runs on a worker thread: log via <paramref name="log"/> (flushed grouped
    /// per map), IDs come from the map's pre-allocated block.
    /// </summary>
    private static int InjectMap(
        string modDir, string gameDir, Events events,
        string mapId, List<BloodstainSpec> specs, uint nextEntityId, int eventOffset,
        Action<string> log)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            log($"  Warning: {msbFileName} not found, skipping death markers for {mapId}");
            return 0;
        }

        var msb = MSBE.Read(msbPath);

        // Group specs by death flag for EMEVD event creation.
        // Each entry maps deathFlag -> list of entity IDs to activate.
        var entityIdsByFlag = new Dictionary<int, List<uint>>();
        int placedCount = 0;

        MsbHelper.EnsureAssetModel(msb, BLOODSTAIN_MODEL);

        // Group specs by part name to share the DeepCopy workaround per gate asset
        var specsByPart = specs.GroupBy(s => s.PartName);

        foreach (var group in specsByPart)
        {
            var partName = group.Key;
            var partSpecs = group.ToList();

            var gateAsset = msb.Parts.Assets.Find(a => a.Name == partName);
            if (gateAsset == null && uint.TryParse(partName, out uint entityIdLookup))
                gateAsset = msb.Parts.Assets.Find(a => a.EntityID == entityIdLookup);
            if (gateAsset == null)
            {
                log($"  Warning: Gate asset '{partName}' not found in {mapId} MSB, skipping");
                continue;
            }

            var baseAsset = FindNearestVanillaAsset(msb, gateAsset.Position);
            if (baseAsset == null)
            {
                log($"  Warning: No vanilla asset to clone from in {mapId} MSB, skipping");
                continue;
            }

            // Precompute offsets for both sides. A gate used as both entrance and exit
            // in different connections may need bloodstains on different sides: the exit
            // connection approaches from one zone, the entrance connection from another.
            var offsetsASide = GateGeometry.GenerateArcOffsets(
                gateAsset.EntityID, gateAsset.Rotation.Y, 180f,
                BLOODSTAINS_PER_GATE, MIN_RADIUS, MAX_RADIUS, Y_OFFSET);
            var offsetsBSide = GateGeometry.GenerateArcOffsets(
                gateAsset.EntityID, gateAsset.Rotation.Y, 0f,
                BLOODSTAINS_PER_GATE, MIN_RADIUS, MAX_RADIUS, Y_OFFSET);

            // No save/restore needed around the clone batch: Part.DeepCopy
            // clones EntityGroupIDs, Asset.DeepCopyTo reassigns the SOURCE's
            // UnkPartNames to a fresh clone (an unqualified `UnkPartNames =
            // Clone()`, so each clone ends up holding an array no longer
            // referenced by the base), UnkT54PartName is an immutable string
            // reference, and the Unk1 group arrays are detached per clone by
            // DetachVisibilityGroups.
            foreach (var spec in partSpecs)
            {
                var offsets = spec.IsASide ? offsetsASide : offsetsBSide;
                var offset = offsets[spec.TierIndex % 3];

                var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
                MsbHelper.DetachVisibilityGroups(bloodstain);
                bloodstain.ModelName = BLOODSTAIN_MODEL;
                bloodstain.Name = MsbHelper.GeneratePartName(
                    msb.Parts.Assets.Select(a => a.Name), BLOODSTAIN_MODEL);
                MsbHelper.SetNameIdent(bloodstain);
                bloodstain.Position = gateAsset.Position + offset;
                bloodstain.Rotation = new Vector3(0f, 0f, 0f);
                bloodstain.EntityID = nextEntityId;
                bloodstain.AssetSfxParamRelativeID = -1;

                for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                    bloodstain.UnkPartNames[j] = null;
                bloodstain.UnkT54PartName = null;
                Array.Clear(bloodstain.EntityGroupIDs);

                msb.Parts.Assets.Add(bloodstain);

                if (!entityIdsByFlag.TryGetValue(spec.DeathFlag, out var flagEntities))
                {
                    flagEntities = new List<uint>();
                    entityIdsByFlag[spec.DeathFlag] = flagEntities;
                }
                flagEntities.Add(nextEntityId);

                nextEntityId++;
                placedCount++;
            }
        }

        if (placedCount == 0)
            return 0;

        var writePath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        // EMEVD: create conditional events for each death flag
        var emevdFileName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdFileName);
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
            if (!File.Exists(gameEmevdPath))
            {
                log($"  Warning: {emevdFileName} not found, skipping EMEVD injection for {mapId}");
                return placedCount;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            log($"  Warning: Event 0 not found in {emevdFileName}, skipping SFX activation");
            return placedCount;
        }

        // The event budget was checked upfront by PlanAllocations;
        // entityIdsByFlag.Count never exceeds the map's DistinctFlagCount.
        foreach (var (deathFlag, entityIds) in entityIdsByFlag)
        {
            long eventId = DEATH_MARKER_EVENT_BASE + eventOffset;
            eventOffset++;

            var evt = new EMEVD.Event(eventId);

            // events is shared across the parallel map workers and its parse
            // caches are not known to be thread-safe; instruction building is
            // cheap next to MSB/DCX work, so serialize it.
            lock (events)
            {
                // IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, deathFlag)
                evt.Instructions.Add(events.ParseAdd(
                    $"IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, {deathFlag})"));

                foreach (var entityId in entityIds)
                {
                    evt.Instructions.Add(events.ParseAdd(
                        $"ChangeAssetEnableState({entityId}, Enabled)"));
                    evt.Instructions.Add(events.ParseAdd(
                        $"CreateAssetfollowingSFX({entityId}, {SFX_DUMMY_POLY}, {SFX_ID})"));
                }
            }

            emevd.Events.Add(evt);

            // Register in event 0 via InitializeEvent (bank 2000, id 0)
            initEvent.Instructions.Add(EmevdHelper.InitializeEvent((int)eventId));
        }

        emevd.Write(emevdPath);
        return placedCount;
    }

    // --- Helper methods ---

    private static MSBE.Part.Asset? FindNearestVanillaAsset(MSBE msb, Vector3 targetPos) =>
        MsbHelper.FindNearestVanilla(
            msb.Parts.Assets, a => a.EntityID, a => a.Position, targetPos,
            SpeedFogIds.FogModEntityMin, SpeedFogIds.DeathMarkerEntityBase);

}
