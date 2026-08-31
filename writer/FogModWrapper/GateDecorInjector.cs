using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places data-driven ambient decorations (candelabras, cobwebs, glow
/// anchors, ...) near dungeon entrance gates for the Halloween plugin, from
/// data/plugins/halloween_decorations.toml. The catalogue ships empty
/// (see HalloweenDecorLoader): with no active entries, Inject is a silent
/// no-op.
///
/// Same entrance-gate filter as AmbientSpawnInjector (destination cluster
/// type in mini_dungeon/legacy_dungeon via eventMap, never boss arenas, one
/// spec group per (map, gate) pair) and the same death-marker clone recipe
/// as DeathMarkerInjector (DeepCopy a nearby vanilla asset, detach
/// visibility groups, retarget the model, clear identity fields).
///
/// Two-phase per map, mirroring DeathMarkerInjector:
/// 1. MSB phase: clone a nearby vanilla asset per catalogue entry per gate.
/// 2. EMEVD phase: catalogue entries with SfxId > 0 get one unconditional
///    CreateAssetfollowingSFX event per map (no flag wait, since
///    decorations are always present, unlike death markers).
///
/// Each catalogue entry at a gate seeds GateGeometry.GenerateArcOffsets off
/// the gate EntityID mixed with a decor-specific tag and the entry's index
/// (see ApplyToMsb), not off the raw EntityID: seeding off the raw EntityID
/// would make two entries with equal count/radius bands draw identical
/// positions, and would coincide with AmbientSpawnInjector's greeter
/// sequence at the same gate/arc center.
/// </summary>
public static class GateDecorInjector
{
    private static readonly HashSet<string> DecorClusterTypes =
        new() { "mini_dungeon", "legacy_dungeon" };

    // FogMod's own entity/region allocation floor (DeathMarkerInjector.FOGMOD_ENTITY_MIN);
    // vanilla assets used as clone sources, plus every SpeedFog-managed
    // entity range (death markers, this injector's own decorations), sit at
    // or above it, so a single-sided floor excludes all of them.
    private const uint FOGMOD_ENTITY_MIN = 755890000;

    // Arbitrary fixed tag XORed into the gate EntityID to seed each catalogue
    // entry's arc PRNG (see ApplyToMsb). Any stable constant works; this one
    // just avoids the all-zero/identity case. Must never be 0.
    private const uint DecorSeedTag = 0x44454355u;

    internal readonly record struct GateSpec(string PartName, bool IsASide);

    internal sealed record MapAllocation(string MapId, uint EntityIdBase, int EventOffsetBase);

    /// <summary>
    /// Inject catalogue decorations at every DAG entrance leading into a
    /// mini_dungeon/legacy_dungeon cluster. A no-op (one console line) when
    /// the catalogue is missing or has no active entries. Maps are
    /// processed in parallel; entity and event IDs are pre-partitioned per
    /// map so the output stays deterministic.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        Events events,
        string dataDir)
    {
        var catalog = HalloweenDecorLoader.Load(
            Path.Combine(dataDir, "plugins", "halloween_decorations.toml"));
        if (catalog.IsEmpty)
            return;

        Console.WriteLine("Injecting Halloween gate decorations...");

        var gatesByMap = CollectGatesByMap(connections, eventMap, nodes, gateSides);
        var work = gatesByMap.ToList();
        int perGateCount = catalog.Entries.Sum(e => e.Count);
        bool hasSfxEntries = catalog.Entries.Any(e => e.SfxId > 0);
        var plans = PlanAllocations(
            work.Select(kv => (kv.Key, kv.Value.Count * perGateCount)), hasSfxEntries);

        int totalPlaced = 0;
        int totalMaps = 0;
        var consoleLock = new object();

        Parallel.ForEach(work.Zip(plans), pair =>
        {
            var (mapId, gates) = pair.First;
            var plan = pair.Second;
            var log = new List<string>();
            int count = InjectMap(
                modDir, gameDir, events, mapId, gates, catalog,
                plan.EntityIdBase, plan.EventOffsetBase, log.Add);
            lock (consoleLock)
            {
                foreach (var line in log)
                    Console.WriteLine(line);
            }
            if (count > 0)
            {
                Interlocked.Add(ref totalPlaced, count);
                Interlocked.Increment(ref totalMaps);
            }
        });

        Console.WriteLine($"  Placed {totalPlaced} gate decorations across {totalMaps} maps");
    }

    /// <summary>
    /// Collect entrance gates per map, keyed by the entrance gate's map id.
    /// For each connection, resolves the destination cluster via eventMap
    /// and skips it unless the cluster exists and its type is in
    /// DecorClusterTypes (mini_dungeon/legacy_dungeon; never boss arenas).
    /// Decorations are anchored on the ENTRANCE gate (inside the destination
    /// zone), same as AmbientSpawnInjector. Emits at most one gate per
    /// (map, gate part name) pair.
    /// </summary>
    internal static Dictionary<string, List<GateSpec>> CollectGatesByMap(
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        var result = new Dictionary<string, List<GateSpec>>();
        var seenGates = new HashSet<(string MapId, string PartName)>();

        foreach (var conn in connections)
        {
            if (!eventMap.TryGetValue(conn.FlagId.ToString(), out var clusterId))
                continue;
            if (!nodes.TryGetValue(clusterId, out var node) || !DecorClusterTypes.Contains(node.Type))
                continue;

            var (mapId, partName) = GateGeometry.ParseGateFullName(conn.EntranceGate);
            if (!seenGates.Add((mapId, partName)))
                continue;

            bool isASide = GateGeometry.ResolveIsASide(conn.EntranceGate, conn.EntranceArea, gateSides);

            if (!result.TryGetValue(mapId, out var gates))
            {
                gates = new List<GateSpec>();
                result[mapId] = gates;
            }
            gates.Add(new GateSpec(partName, isASide));
        }

        return result;
    }

    /// <summary>
    /// Partition the entity ID and event ID spaces per map, in map order, so
    /// parallel processing cannot change the generated IDs. Each map gets a
    /// contiguous entity ID block sized for every (gate, catalogue entry)
    /// pair, and, when the catalogue has at least one SfxId &gt; 0 entry,
    /// exactly one event slot (used or not, the same upper-bound tolerance
    /// as DeathMarkerInjector.PlanAllocations). Throws upfront when the
    /// summed event slots exceed the range budget.
    /// </summary>
    internal static List<MapAllocation> PlanAllocations(
        IEnumerable<(string MapId, int SpecCount)> maps, bool hasSfxEntries)
    {
        uint entityBase = SpeedFogIds.HalloweenDecorEntityBase;
        int eventBase = 0;
        var plans = new List<MapAllocation>();
        foreach (var (mapId, specCount) in maps)
        {
            plans.Add(new MapAllocation(mapId, entityBase, hasSfxEntries ? eventBase : -1));
            entityBase += (uint)specCount;
            if (hasSfxEntries)
                eventBase++;
        }
        if (eventBase > SpeedFogIds.HalloweenDecorEvents.Capacity)
        {
            throw new InvalidOperationException(
                $"Halloween decor event budget exceeded: {eventBase} events " +
                $"(max {SpeedFogIds.HalloweenDecorEvents.Capacity}, " +
                $"next range starts at {SpeedFogIds.HalloweenDecorEvents.End})");
        }
        return plans;
    }

    /// <summary>
    /// Apply the catalogue to an already-loaded MSB. For each gate, finds
    /// the gate asset by name (falling back to an EntityID parse, same as
    /// DeathMarkerInjector), then the nearest vanilla asset to clone
    /// (skipping FogMod/SpeedFog-managed entities), shared across every
    /// catalogue entry at that gate. Gates with no gate asset or no vanilla
    /// asset to clone from are logged and skipped. Returns the number of
    /// decorations placed plus the (entityId, sfxDummy, sfxId) work items
    /// for entries with SfxId &gt; 0 (SfxId &lt;= 0 entries produce no work
    /// item: geometry only, no EMEVD event).
    /// </summary>
    internal static (int Placed, List<(uint EntityId, int SfxDummy, int SfxId)> SfxWork) ApplyToMsb(
        MSBE msb, List<GateSpec> gates, DecorCatalog catalog, uint entityIdBase, Action<string> log)
    {
        int placed = 0;
        uint nextEntityId = entityIdBase;
        var sfxWork = new List<(uint, int, int)>();
        var modelsEnsured = new HashSet<string>();

        foreach (var gate in gates)
        {
            var gateAsset = msb.Parts.Assets.Find(a => a.Name == gate.PartName);
            if (gateAsset == null && uint.TryParse(gate.PartName, out uint entityIdLookup))
                gateAsset = msb.Parts.Assets.Find(a => a.EntityID == entityIdLookup);
            if (gateAsset == null)
            {
                log($"  Warning: Gate asset '{gate.PartName}' not found in MSB, skipping gate decorations");
                continue;
            }

            var baseAsset = FindNearestVanillaAsset(msb, gateAsset.Position);
            if (baseAsset == null)
            {
                log($"  Warning: No vanilla asset to clone from, skipping gate decorations for gate '{gate.PartName}'");
                continue;
            }

            // Decorations sit on the interior (entrance-area) side, like
            // AmbientSpawnInjector's greeters: isASide?180:0 places them on
            // the queried (entrance) zone's own player side. See
            // docs/death-markers.md "Position Offsets (ASide/BSide)".
            float arcCenterDeg = gate.IsASide ? 180f : 0f;

            for (int entryIndex = 0; entryIndex < catalog.Entries.Count; entryIndex++)
            {
                var entry = catalog.Entries[entryIndex];
                if (modelsEnsured.Add(entry.Model))
                    MsbHelper.EnsureAssetModel(msb, entry.Model);

                // Mix the gate's own EntityID with a decor-specific tag and
                // the entry index so every catalogue entry at this gate
                // draws its own angle/radius sequence: seeding straight off
                // gateAsset.EntityID (as AmbientSpawnInjector's greeters do
                // for the same arc center) would make two entries with equal
                // count/radius bands land on byte-identical positions, and
                // would make this sequence coincide with the greeter's.
                uint seed = gateAsset.EntityID ^ DecorSeedTag;
                seed += (uint)entryIndex * 7919u;

                var offsets = GateGeometry.GenerateArcOffsets(
                    seed, gateAsset.Rotation.Y, arcCenterDeg,
                    entry.Count, entry.MinRadius, entry.MaxRadius, entry.YOffset);

                for (int i = 0; i < entry.Count; i++)
                {
                    var decor = (MSBE.Part.Asset)baseAsset.DeepCopy();
                    MsbHelper.DetachVisibilityGroups(decor);
                    decor.ModelName = entry.Model;
                    decor.Name = MsbHelper.GeneratePartName(
                        msb.Parts.Assets.Select(a => a.Name), entry.Model);
                    MsbHelper.SetNameIdent(decor);
                    decor.Position = gateAsset.Position + offsets[i];
                    decor.Rotation = Vector3.Zero;
                    decor.EntityID = nextEntityId;
                    decor.AssetSfxParamRelativeID = -1;

                    for (int j = 0; j < decor.UnkPartNames.Length; j++)
                        decor.UnkPartNames[j] = null;
                    decor.UnkT54PartName = null;
                    Array.Clear(decor.EntityGroupIDs);

                    msb.Parts.Assets.Add(decor);

                    if (entry.SfxId > 0)
                        sfxWork.Add((nextEntityId, entry.SfxDummyPoly, entry.SfxId));

                    nextEntityId++;
                    placed++;
                }
            }
        }

        return (placed, sfxWork);
    }

    // --- Helper methods ---

    private static int InjectMap(
        string modDir, string gameDir, Events events,
        string mapId, List<GateSpec> gates, DecorCatalog catalog,
        uint entityIdBase, int eventOffset, Action<string> log)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            log($"  Warning: {msbFileName} not found, skipping gate decorations for {mapId}");
            return 0;
        }

        var msb = MSBE.Read(msbPath);
        var (placed, sfxWork) = ApplyToMsb(msb, gates, catalog, entityIdBase, log);
        if (placed == 0)
            return 0;

        var writePath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        if (sfxWork.Count == 0 || eventOffset < 0)
            return placed;

        var emevdFileName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdFileName);
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
            if (!File.Exists(gameEmevdPath))
            {
                log($"  Warning: {emevdFileName} not found, skipping SFX activation for {mapId}");
                return placed;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            log($"  Warning: Event 0 not found in {emevdFileName}, skipping SFX activation");
            return placed;
        }

        long eventId = SpeedFogIds.HalloweenDecorEvents.Base + eventOffset;
        var evt = new EMEVD.Event(eventId);

        // events is shared across the parallel map workers; serialize
        // instruction building like DeathMarkerInjector does.
        lock (events)
        {
            // Unconditional: decorations are always present, no flag wait
            // (unlike DeathMarkerInjector's per-death-flag events).
            foreach (var (entityId, sfxDummy, sfxId) in sfxWork)
            {
                evt.Instructions.Add(events.ParseAdd(
                    $"ChangeAssetEnableState({entityId}, Enabled)"));
                evt.Instructions.Add(events.ParseAdd(
                    $"CreateAssetfollowingSFX({entityId}, {sfxDummy}, {sfxId})"));
            }
        }

        emevd.Events.Add(evt);
        initEvent.Instructions.Add(EmevdHelper.InitializeEvent((int)eventId));
        emevd.Write(emevdPath);

        return placed;
    }

    private static MSBE.Part.Asset? FindNearestVanillaAsset(MSBE msb, Vector3 targetPos)
    {
        MSBE.Part.Asset? best = null;
        float bestDist = float.MaxValue;

        foreach (var asset in msb.Parts.Assets)
        {
            if (asset.EntityID >= FOGMOD_ENTITY_MIN)
                continue;

            var diff = asset.Position - targetPos;
            float dist = diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = asset;
            }
        }

        return best;
    }
}
