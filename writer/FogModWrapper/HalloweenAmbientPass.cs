using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Single per-map driver for the Halloween ambient layer at cluster exit
/// gates. GateDecorInjector (data-driven decorations) and AmbientSpawnInjector
/// (passive greeters + optional ambush packs) each used to run their own
/// parallel MSB read/ApplyToMsb/write pass over their own map set, even
/// though those sets mostly overlap. This driver reads each map's MSB once,
/// applies GateDecorInjector.ApplyToMsb first (ground-evidence invariant:
/// the decor ground estimate treats vanilla enemies as floor evidence, and
/// AmbientSpawnInjector's spawns carry EntityID 0, which would otherwise
/// pollute that evidence), then AmbientSpawnInjector.ApplyToMsb, then writes
/// the MSB once. GateDecorInjector's EMEVD/SFX phase (WriteSfxEvents) is
/// unaffected: still per map, decor-only, run after the shared MSB write.
///
/// The two features anchor on different cluster-type sets
/// (HalloweenGateAnchors.DecorClusterTypes includes "start",
/// SpawnClusterTypes does not), so this driver unions the two maps of
/// per-map anchors/specs and hands each feature only its own.
/// </summary>
public static class HalloweenAmbientPass
{
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        Events events,
        string dataDir,
        HalloweenPluginSettings.Settings settings)
    {
        var catalog = HalloweenDecorLoader.Load(
            Path.Combine(dataDir, "plugins", "halloween_decorations.toml"));

        // Only collect decor anchors when the catalogue has active entries:
        // matches GateDecorInjector's old silent no-op when the catalogue is
        // missing or empty.
        var decorGatesByMap = catalog.IsEmpty
            ? new Dictionary<string, List<HalloweenGateAnchors.GateAnchor>>()
            : HalloweenGateAnchors.Collect(
                connections, nodes, gateSides, HalloweenGateAnchors.DecorClusterTypes);

        var spawnSpecsByMap = AmbientSpawnInjector.CollectSpawnSpecsByMap(
            connections, nodes, gateSides, settings);

        var mapIds = new HashSet<string>(decorGatesByMap.Keys);
        mapIds.UnionWith(spawnSpecsByMap.Keys);

        // Decor entity/event id allocation is planned over exactly the maps
        // decorGatesByMap produced, in that dictionary's own enumeration
        // order (same map set and order GateDecorInjector.Inject used to
        // plan over) so IDs stay identical to before this driver existed;
        // planning over the wider mapIds union would hand spawn-only maps an
        // unused decor event slot and shift every later map's allocation.
        int perGateCount = catalog.Entries.Sum(e => e.Count);
        bool hasSfxEntries = catalog.Entries.Any(e => e.SfxId > 0);
        var decorPlans = catalog.IsEmpty
            ? new Dictionary<string, GateDecorInjector.MapAllocation>()
            : GateDecorInjector.PlanAllocations(
                decorGatesByMap.Select(kv => (kv.Key, kv.Value.Count * perGateCount)),
                hasSfxEntries).ToDictionary(p => p.MapId);

        if (!catalog.IsEmpty)
            Console.WriteLine("Injecting Halloween gate decorations...");
        Console.WriteLine("Injecting Halloween ambient spawns at cluster exit gates...");

        int totalDecor = 0, totalDecorMaps = 0;
        int totalGreeters = 0, totalAmbushers = 0, totalSpawnMaps = 0;

        MsbHelper.ForEachWithBufferedLogs(mapIds, (mapId, log) =>
        {
            var decorGates = decorGatesByMap.TryGetValue(mapId, out var dg)
                ? dg : new List<HalloweenGateAnchors.GateAnchor>();
            var spawnSpecs = spawnSpecsByMap.TryGetValue(mapId, out var ss)
                ? ss : new List<SpawnSpec>();

            var msbFileName = $"{mapId}.msb.dcx";
            var msbPath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindMsbPath(gameDir, msbFileName);
            if (msbPath == null)
            {
                if (decorGates.Count > 0)
                    log($"  Warning: {msbFileName} not found, skipping gate decorations for {mapId}");
                if (spawnSpecs.Count > 0)
                    log($"  Warning: {msbFileName} not found, skipping ambient spawns for {mapId}");
                return;
            }

            var msb = MSBE.Read(msbPath);

            int decorPlaced = 0;
            List<(uint EntityId, int SfxDummy, int SfxId)> sfxWork = new();
            if (decorGates.Count > 0)
            {
                var plan = decorPlans[mapId];
                (decorPlaced, sfxWork) = GateDecorInjector.ApplyToMsb(msb, decorGates, catalog, plan.EntityIdBase, log);
            }

            // Decor placement runs first: see class doc comment
            // (ground-evidence invariant).
            var (greeters, ambushers) = spawnSpecs.Count > 0
                ? AmbientSpawnInjector.ApplyToMsb(msb, spawnSpecs, log)
                : (0, 0);

            if (decorPlaced + greeters + ambushers > 0)
            {
                var writePath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
                msb.Write(writePath);
            }

            if (decorPlaced > 0)
            {
                Interlocked.Add(ref totalDecor, decorPlaced);
                Interlocked.Increment(ref totalDecorMaps);
            }
            if (greeters + ambushers > 0)
            {
                Interlocked.Add(ref totalGreeters, greeters);
                Interlocked.Add(ref totalAmbushers, ambushers);
                Interlocked.Increment(ref totalSpawnMaps);
            }

            if (sfxWork.Count > 0)
            {
                var plan = decorPlans[mapId];
                GateDecorInjector.WriteSfxEvents(modDir, gameDir, events, mapId, sfxWork, plan.EventOffsetBase, log);
            }
        });

        if (!catalog.IsEmpty)
            Console.WriteLine($"  Placed {totalDecor} gate decorations across {totalDecorMaps} maps");
        Console.WriteLine($"  Placed {totalGreeters} greeters + {totalAmbushers} ambushers across {totalSpawnMaps} maps");
    }
}
