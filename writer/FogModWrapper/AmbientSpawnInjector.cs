using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places ambient enemy spawns just inside dungeon entrance gates for the
/// Halloween plugin: a passive "greeter" (Aging Untouchable model, perception
/// zeroed so it never aggros) at every mini_dungeon/legacy_dungeon entrance,
/// plus, when ambushes are enabled, a small skeleton pack sharing the
/// entrance's arc. Boss arenas never receive spawns (see SpawnClusterTypes).
///
/// Two-phase injection, mirroring DeathMarkerInjector:
/// 1. MSB phase (this class, Inject/ApplyToMsb): clone a nearby vanilla enemy
///    per entrance gate, retarget it to the greeter/ambusher model.
/// 2. Regulation phase (ApplyPassiveThinkRow): clone the greeter's
///    NpcThinkParam row with all perception fields zeroed.
/// </summary>
public static class AmbientSpawnInjector
{
    private const string GREETER_MODEL = "c5280";
    private const int GREETER_NPC_PARAM = 52800086;
    private const string AMBUSH_MODEL = "c3500";
    private const int AMBUSH_NPC_PARAM = 35000030;
    private const int AMBUSH_THINK_PARAM = 35000000;
    private const float GREETER_MIN_RADIUS = 4.0f;
    private const float GREETER_MAX_RADIUS = 6.0f;
    private const float AMBUSH_MIN_RADIUS = 3.0f;
    private const float AMBUSH_MAX_RADIUS = 7.0f;
    private const float AMBUSH_ARC_SPREAD = 140f;
    private static readonly HashSet<string> SpawnClusterTypes =
        new() { "mini_dungeon", "legacy_dungeon" };

    // FogMod's own entity/region allocation floor (DeathMarkerInjector.FOGMOD_ENTITY_MIN);
    // vanilla enemies used as clone sources must sit below it.
    private const uint FOGMOD_ENTITY_MIN = 755890000;

    /// <summary>
    /// Inject passive greeters (and optional ambush packs) at every DAG entrance
    /// leading into a mini_dungeon/legacy_dungeon cluster. Maps are processed in
    /// parallel (independent MSB files); no entity or event IDs are allocated,
    /// so no pre-partitioning is needed.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HalloweenPluginSettings.Settings settings)
    {
        Console.WriteLine("Injecting Halloween ambient spawns at dungeon entrances...");

        var specsByMap = CollectSpawnSpecsByMap(connections, eventMap, nodes, gateSides, settings);
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
    /// Collect spawn specs per map, keyed by the entrance gate's map id. For
    /// each connection, resolves the destination cluster via eventMap and
    /// skips it unless the cluster exists and its type is in
    /// SpawnClusterTypes (mini_dungeon/legacy_dungeon; never boss arenas).
    /// Spawns are anchored on the ENTRANCE gate (inside the destination zone)
    /// since they greet the player as they arrive. Emits at most one spec
    /// group per (map, gate part name) pair: one Greeter, plus, when
    /// settings.Ambushes, a pack of 2-3 Ambushers.
    /// </summary>
    internal static Dictionary<string, List<SpawnSpec>> CollectSpawnSpecsByMap(
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, GraphNode> nodes,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides,
        HalloweenPluginSettings.Settings settings)
    {
        var result = new Dictionary<string, List<SpawnSpec>>();
        var seenGates = new HashSet<(string MapId, string PartName)>();

        foreach (var conn in connections)
        {
            if (!eventMap.TryGetValue(conn.FlagId.ToString(), out var clusterId))
                continue;
            if (!nodes.TryGetValue(clusterId, out var node) || !SpawnClusterTypes.Contains(node.Type))
                continue;

            var (mapId, partName) = GateGeometry.ParseGateFullName(conn.EntranceGate);
            if (!seenGates.Add((mapId, partName)))
                continue;

            bool isASide = GateGeometry.ResolveIsASide(conn.EntranceGate, conn.EntranceArea, gateSides);

            if (!result.TryGetValue(mapId, out var specs))
            {
                specs = new List<SpawnSpec>();
                result[mapId] = specs;
            }

            specs.Add(new SpawnSpec(partName, SpawnKind.Greeter, 0, 1, isASide));

            if (settings.Ambushes)
            {
                int packSize = 2 + new Random(StablePartNameHash(partName)).Next(2);
                for (int i = 0; i < packSize; i++)
                    specs.Add(new SpawnSpec(partName, SpawnKind.Ambusher, i, packSize, isASide));
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
    /// to clone from are logged and skipped. Returns the number of spawns
    /// placed.
    /// </summary>
    internal static int ApplyToMsb(MSBE msb, List<SpawnSpec> specs, Action<string> log)
    {
        int placed = 0;
        var modelsEnsured = new HashSet<string>();

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

            var baseEnemy = FindNearestVanillaEnemy(msb, gateAsset.Position);
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
                    // resolved against the entrance area, so this lands spawns on
                    // the entrance area's own side, i.e. where the arriving player
                    // stands inside the destination zone (see docs/death-markers.md
                    // "Position Offsets (ASide/BSide)").
                    spec.GateSideIsASide ? 180f : 0f,
                    spec.PackSize,
                    spec.Kind == SpawnKind.Greeter ? GREETER_MIN_RADIUS : AMBUSH_MIN_RADIUS,
                    spec.Kind == SpawnKind.Greeter ? GREETER_MAX_RADIUS : AMBUSH_MAX_RADIUS,
                    0f,
                    spec.Kind == SpawnKind.Greeter ? 120f : AMBUSH_ARC_SPREAD);
                var offset = offsets[spec.IndexInPack];

                var spawn = (MSBE.Part.Enemy)baseEnemy.DeepCopy();
                MsbHelper.DetachVisibilityGroups(spawn);
                spawn.ModelName = model;
                spawn.Name = MsbHelper.GeneratePartName(msb.Parts.Enemies.Select(e => e.Name), spawn.ModelName);
                MsbHelper.SetNameIdent(spawn);
                spawn.Position = gateAsset.Position + offset;
                // Greeters face the gate (the arriving player); ambushers keep pack scatter.
                float yawToGate = MathF.Atan2(-offset.X, -offset.Z) * 180f / MathF.PI;
                spawn.Rotation = new Vector3(0f, spec.Kind == SpawnKind.Greeter ? yawToGate : (spec.IndexInPack * 137f) % 360f, 0f);
                spawn.EntityID = 0;
                Array.Clear(spawn.EntityGroupIDs);
                spawn.NPCParamID = spec.Kind == SpawnKind.Greeter ? GREETER_NPC_PARAM : AMBUSH_NPC_PARAM;
                spawn.ThinkParamID = spec.Kind == SpawnKind.Greeter ? SpeedFogIds.PassiveGreeterThinkRow : AMBUSH_THINK_PARAM;
                spawn.TalkID = 0;
                spawn.CharaInitID = -1;
                // CollisionPartName inherited from the cloned neighbor on purpose: the
                // nearest enemy stands on a valid collision in the same play space.
                msb.Parts.Enemies.Add(spawn);

                placed++;
            }
        }

        return placed;
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
        var row = GameEditor.AddRow(think, SpeedFogIds.PassiveGreeterThinkRow, 52800000);
        // Storage types from Defs/NpcThinkParam.xml: ear_dist is f32, the rest u16.
        row["ear_dist"].Value = 0f;
        row["eye_dist"].Value = (ushort)0;
        row["nose_dist"].Value = (ushort)0;
        row["searchEye_dist"].Value = (ushort)0;
        row["BattleStartDist"].Value = (ushort)0;
        Console.WriteLine($"Halloween spawns: passive think row {SpeedFogIds.PassiveGreeterThinkRow} (clone of 52800000, perception zeroed)");
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
        int placed = ApplyToMsb(msb, specs, log);
        if (placed == 0)
            return (0, 0);

        int greeters = msb.Parts.Enemies.Count(e => e.ModelName == GREETER_MODEL);
        int ambushers = msb.Parts.Enemies.Count(e => e.ModelName == AMBUSH_MODEL);

        var writePath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        return (greeters, ambushers);
    }

    private static MSBE.Part.Enemy? FindNearestVanillaEnemy(MSBE msb, Vector3 targetPos)
    {
        MSBE.Part.Enemy? best = null;
        float bestDist = float.MaxValue;

        foreach (var enemy in msb.Parts.Enemies)
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
