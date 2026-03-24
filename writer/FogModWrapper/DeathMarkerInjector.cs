using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places bloodstain visual markers near each fog gate in the DAG.
///
/// For each fog gate, 3 AEG099_090 anchor assets are placed in a 120-degree arc
/// in front of the gate, with a red glow SFX (DummyPoly 100, SfxID 42) activated
/// in EMEVD event 0. See docs/death-markers.md for the full design.
///
/// In conditional mode (deathFlags non-empty), each bloodstain is controlled by a
/// per-cluster death flag via a dedicated EMEVD event that waits for the flag.
/// </summary>
public static class DeathMarkerInjector
{
    private const string BLOODSTAIN_MODEL = "AEG099_090";
    private const int SFX_DUMMY_POLY = 100;
    private const int SFX_ID = 42;
    private const uint FOGMOD_ENTITY_MIN = 755890000;
    private const uint FOGMOD_ENTITY_MAX = 755900000;

    private const int BLOODSTAINS_PER_GATE = 3;
    private const float MIN_RADIUS = 1.5f;
    private const float MAX_RADIUS = 3.0f;
    private const float Y_OFFSET = 0.10f;

    private const int DEATH_MARKER_EVENT_BASE = 755862100;
    private static int _nextEventOffset = 0;

    private static readonly string[] MsbDirVariants = { "mapstudio", "MapStudio" };

    private readonly struct BloodstainSpec
    {
        public readonly string PartName;
        public readonly int DeathFlag;
        public readonly int TierIndex;  // 0=low, 1=med, 2=high

        public BloodstainSpec(string partName, int deathFlag, int tierIndex)
        {
            PartName = partName;
            DeathFlag = deathFlag;
            TierIndex = tierIndex;
        }
    }

    /// <summary>
    /// Parse a gate FullName like "m10_01_00_00_AEG099_001_9000" into (mapId, partName).
    /// </summary>
    internal static (string MapId, string PartName) ParseGateFullName(string fullName)
    {
        var parts = fullName.Split('_');
        if (parts.Length < 5)
            throw new ArgumentException($"Invalid gate FullName (too few segments): {fullName}");

        var mapId = string.Join("_", parts[0], parts[1], parts[2], parts[3]);
        var partName = string.Join("_", parts.Skip(4));
        return (mapId, partName);
    }

    /// <summary>
    /// Generate 3 offsets in front of a gate (approach side), spread across
    /// a 120-degree arc opposite the gate's facing direction.
    /// PRNG seeded on gateEntityId for deterministic placement.
    /// </summary>
    internal static Vector3[] GenerateOffsets(uint gateEntityId, float gateRotY)
    {
        var rng = new Random(gateEntityId.GetHashCode());
        var offsets = new Vector3[BLOODSTAINS_PER_GATE];
        float gateRad = gateRotY * MathF.PI / 180f;

        const float arcSpread = 120f;
        const float sectorSize = arcSpread / BLOODSTAINS_PER_GATE;
        float arcStart = 180f - arcSpread / 2f;

        for (int i = 0; i < BLOODSTAINS_PER_GATE; i++)
        {
            float sectorStart = arcStart + i * sectorSize;
            float angleDeg = sectorStart + (float)(rng.NextDouble() * sectorSize);
            float angleRad = angleDeg * MathF.PI / 180f;
            float radius = MIN_RADIUS + (float)(rng.NextDouble() * (MAX_RADIUS - MIN_RADIUS));

            float localX = MathF.Sin(angleRad) * radius;
            float localZ = MathF.Cos(angleRad) * radius;

            float worldX = localX * MathF.Cos(gateRad) + localZ * MathF.Sin(gateRad);
            float worldZ = -localX * MathF.Sin(gateRad) + localZ * MathF.Cos(gateRad);

            offsets[i] = new Vector3(worldX, Y_OFFSET, worldZ);
        }

        return offsets;
    }

    /// <summary>
    /// Inject bloodstain visual markers at all fog gates in the DAG.
    /// When deathFlags is non-empty, each bloodstain is controlled by a per-cluster
    /// death flag via a dedicated EMEVD event. When empty, unconditional mode is used.
    /// </summary>
    public static void Inject(
        string modDir, string gameDir,
        List<Connection> connections, Events events,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags)
    {
        Console.WriteLine("Injecting death markers at fog gates...");

        bool conditional = deathFlags.Count > 0;
        uint nextEntityId = FindMaxFogModEntityId(modDir) + 1;
        int totalAssets = 0;
        int totalMaps = 0;
        _nextEventOffset = 0;

        if (conditional)
        {
            var specsByMap = CollectConditionalGatesByMap(connections, eventMap, deathFlags);
            foreach (var (mapId, specs) in specsByMap)
            {
                var (count, nextId) = InjectMapConditional(
                    modDir, gameDir, events, mapId, specs, nextEntityId);
                if (count > 0)
                {
                    totalAssets += count;
                    totalMaps++;
                }
                nextEntityId = nextId;
            }
        }
        else
        {
            var gatesByMap = CollectGatesByMap(connections);
            foreach (var (mapId, partNames) in gatesByMap)
            {
                var (count, nextId) = InjectMap(
                    modDir, gameDir, events, mapId, partNames, nextEntityId);
                if (count > 0)
                {
                    totalAssets += count;
                    totalMaps++;
                }
                nextEntityId = nextId;
            }
        }

        Console.WriteLine($"  Placed {totalAssets} bloodstain markers across {totalMaps} maps" +
            (conditional ? " (conditional)" : " (unconditional)"));
    }

    private static Dictionary<string, HashSet<string>> CollectGatesByMap(List<Connection> connections)
    {
        var gatesByMap = new Dictionary<string, HashSet<string>>();
        var allGates = new HashSet<string>();

        foreach (var conn in connections)
        {
            allGates.Add(conn.ExitGate);
            allGates.Add(conn.EntranceGate);
        }

        foreach (var gate in allGates)
        {
            var (mapId, partName) = ParseGateFullName(gate);
            if (!gatesByMap.TryGetValue(mapId, out var set))
            {
                set = new HashSet<string>();
                gatesByMap[mapId] = set;
            }
            set.Add(partName);
        }

        return gatesByMap;
    }

    private static Dictionary<string, List<BloodstainSpec>> CollectConditionalGatesByMap(
        List<Connection> connections,
        Dictionary<string, string> eventMap,
        Dictionary<string, List<int>> deathFlags)
    {
        var result = new Dictionary<string, List<BloodstainSpec>>();

        foreach (var conn in connections)
        {
            if (!eventMap.TryGetValue(conn.FlagId.ToString(), out var clusterId))
                continue;
            if (!deathFlags.TryGetValue(clusterId, out var flags))
                continue;

            foreach (var gate in new[] { conn.ExitGate, conn.EntranceGate })
            {
                var (mapId, partName) = ParseGateFullName(gate);
                if (!result.TryGetValue(mapId, out var specs))
                {
                    specs = new List<BloodstainSpec>();
                    result[mapId] = specs;
                }

                for (int tier = 0; tier < flags.Count; tier++)
                {
                    if (!specs.Any(s => s.PartName == partName && s.DeathFlag == flags[tier]))
                        specs.Add(new BloodstainSpec(partName, flags[tier], tier));
                }
            }
        }

        return result;
    }

    /// <summary>
    /// Inject conditional bloodstain markers for a single map.
    /// Each bloodstain is activated only when its death flag is set.
    /// </summary>
    private static (int Count, uint NextEntityId) InjectMapConditional(
        string modDir, string gameDir, Events events,
        string mapId, List<BloodstainSpec> specs, uint nextEntityId)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = FindMsbPath(modDir, msbFileName) ?? FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            Console.WriteLine($"  Warning: {msbFileName} not found, skipping death markers for {mapId}");
            return (0, nextEntityId);
        }

        var msb = MSBE.Read(msbPath);

        if (msb.Parts.MapPieces.Count == 0)
            return (0, nextEntityId);

        // Group specs by death flag for EMEVD event creation.
        // Each entry maps deathFlag -> list of entity IDs to activate.
        var entityIdsByFlag = new Dictionary<int, List<uint>>();
        int placedCount = 0;

        EnsureAssetModel(msb, BLOODSTAIN_MODEL);

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
                Console.WriteLine($"  Warning: Gate asset '{partName}' not found in {mapId} MSB, skipping");
                continue;
            }

            var baseAsset = FindNearestVanillaAsset(msb, gateAsset.Position);
            if (baseAsset == null)
            {
                Console.WriteLine($"  Warning: No vanilla asset to clone from in {mapId} MSB, skipping");
                continue;
            }

            var drawGroups = GetDrawGroupsAtPosition(msb, gateAsset.Position);
            var offsets = GenerateOffsets(gateAsset.EntityID, gateAsset.Rotation.Y);

            // WORKAROUND: SoulsFormats' MSBE.Part.DeepCopy() produces shallow copies
            // of internal arrays. Save and restore around the clone batch.
            var savedDrawGroups = baseAsset.Unk1.DrawGroups.ToArray();
            var savedDisplayGroups = baseAsset.Unk1.DisplayGroups.ToArray();
            var savedCollisionMask = baseAsset.Unk1.CollisionMask.ToArray();
            var savedEntityGroupIDs = baseAsset.EntityGroupIDs.ToArray();
            var savedUnkPartNames = baseAsset.UnkPartNames.ToArray();
            var savedUnkT54 = baseAsset.UnkT54PartName;

            foreach (var spec in partSpecs)
            {
                var offset = offsets[spec.TierIndex % 3];

                var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
                bloodstain.ModelName = BLOODSTAIN_MODEL;
                bloodstain.Name = GeneratePartName(
                    msb.Parts.Assets.Select(a => a.Name), BLOODSTAIN_MODEL);
                SetNameIdent(bloodstain);
                bloodstain.Position = gateAsset.Position + offset;
                bloodstain.Rotation = new Vector3(0f, 0f, 0f);
                bloodstain.EntityID = nextEntityId;
                bloodstain.AssetSfxParamRelativeID = -1;

                for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                    bloodstain.UnkPartNames[j] = null;
                bloodstain.UnkT54PartName = null;
                Array.Clear(bloodstain.EntityGroupIDs);

                if (drawGroups != null)
                    ApplyDrawGroups(bloodstain, drawGroups);

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

            // Restore source asset arrays corrupted by DeepCopy shallow references
            Array.Copy(savedDrawGroups, baseAsset.Unk1.DrawGroups, savedDrawGroups.Length);
            Array.Copy(savedDisplayGroups, baseAsset.Unk1.DisplayGroups, savedDisplayGroups.Length);
            Array.Copy(savedCollisionMask, baseAsset.Unk1.CollisionMask, savedCollisionMask.Length);
            Array.Copy(savedEntityGroupIDs, baseAsset.EntityGroupIDs, savedEntityGroupIDs.Length);
            Array.Copy(savedUnkPartNames, baseAsset.UnkPartNames, savedUnkPartNames.Length);
            baseAsset.UnkT54PartName = savedUnkT54;
        }

        if (placedCount == 0)
            return (0, nextEntityId);

        var writePath = FindMsbPath(modDir, msbFileName) ?? FindOrCreateMsbDir(modDir, msbFileName);
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
                Console.WriteLine($"  Warning: {emevdFileName} not found, skipping EMEVD injection for {mapId}");
                return (placedCount, nextEntityId);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine($"  Warning: Event 0 not found in {emevdFileName}, skipping SFX activation");
            return (placedCount, nextEntityId);
        }

        foreach (var (deathFlag, entityIds) in entityIdsByFlag)
        {
            long eventId = DEATH_MARKER_EVENT_BASE + _nextEventOffset;
            _nextEventOffset++;

            var evt = new EMEVD.Event(eventId);

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

            emevd.Events.Add(evt);

            // Register in event 0 via InitializeEvent (bank 2000, id 0)
            var initArgs = new byte[8];
            BitConverter.GetBytes((int)0).CopyTo(initArgs, 0);        // slot = 0
            BitConverter.GetBytes((int)eventId).CopyTo(initArgs, 4);  // event ID
            initEvent.Instructions.Add(new EMEVD.Instruction(2000, 0, initArgs));
        }

        emevd.Write(emevdPath);
        return (placedCount, nextEntityId);
    }

    /// <summary>
    /// Inject bloodstain markers for a single map: MSB assets + EMEVD activation.
    /// </summary>
    private static (int Count, uint NextEntityId) InjectMap(
        string modDir, string gameDir, Events events,
        string mapId, HashSet<string> partNames, uint nextEntityId)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = FindMsbPath(modDir, msbFileName) ?? FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            Console.WriteLine($"  Warning: {msbFileName} not found, skipping death markers for {mapId}");
            return (0, nextEntityId);
        }

        var msb = MSBE.Read(msbPath);

        // Skip maps without MapPieces: we cannot determine correct DrawGroups,
        // and bloodstains will be invisible (known limitation: Roundtable Hold).
        if (msb.Parts.MapPieces.Count == 0)
            return (0, nextEntityId);

        var entityIds = new List<uint>();
        EnsureAssetModel(msb, BLOODSTAIN_MODEL);

        foreach (var partName in partNames)
        {
            var gateAsset = msb.Parts.Assets.Find(a => a.Name == partName);
            if (gateAsset == null && uint.TryParse(partName, out uint entityIdLookup))
                gateAsset = msb.Parts.Assets.Find(a => a.EntityID == entityIdLookup);
            if (gateAsset == null)
            {
                Console.WriteLine($"  Warning: Gate asset '{partName}' not found in {mapId} MSB, skipping");
                continue;
            }

            var baseAsset = FindNearestVanillaAsset(msb, gateAsset.Position);
            if (baseAsset == null)
            {
                Console.WriteLine($"  Warning: No vanilla asset to clone from in {mapId} MSB, skipping");
                continue;
            }

            var drawGroups = GetDrawGroupsAtPosition(msb, gateAsset.Position);
            var offsets = GenerateOffsets(gateAsset.EntityID, gateAsset.Rotation.Y);

            // WORKAROUND: SoulsFormats' MSBE.Part.DeepCopy() produces shallow copies
            // of internal arrays (DrawGroups, DisplayGroups, CollisionMask, EntityGroupIDs,
            // UnkPartNames). Modifying the clone silently corrupts the original.
            // Save and restore all known shared arrays around the clone batch.
            // If SoulsFormats adds new array fields, they may need to be added here too.
            var savedDrawGroups = baseAsset.Unk1.DrawGroups.ToArray();
            var savedDisplayGroups = baseAsset.Unk1.DisplayGroups.ToArray();
            var savedCollisionMask = baseAsset.Unk1.CollisionMask.ToArray();
            var savedEntityGroupIDs = baseAsset.EntityGroupIDs.ToArray();
            var savedUnkPartNames = baseAsset.UnkPartNames.ToArray();
            var savedUnkT54 = baseAsset.UnkT54PartName;

            for (int i = 0; i < offsets.Length; i++)
            {
                var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
                bloodstain.ModelName = BLOODSTAIN_MODEL;
                bloodstain.Name = GeneratePartName(
                    msb.Parts.Assets.Select(a => a.Name), BLOODSTAIN_MODEL);
                SetNameIdent(bloodstain);
                bloodstain.Position = gateAsset.Position + offsets[i];
                bloodstain.Rotation = new Vector3(0f, 0f, 0f);
                bloodstain.EntityID = nextEntityId;
                bloodstain.AssetSfxParamRelativeID = -1;

                for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                    bloodstain.UnkPartNames[j] = null;
                bloodstain.UnkT54PartName = null;
                Array.Clear(bloodstain.EntityGroupIDs);

                if (drawGroups != null)
                    ApplyDrawGroups(bloodstain, drawGroups);

                msb.Parts.Assets.Add(bloodstain);
                entityIds.Add(nextEntityId);
                nextEntityId++;
            }

            // Restore source asset arrays corrupted by DeepCopy shallow references
            Array.Copy(savedDrawGroups, baseAsset.Unk1.DrawGroups, savedDrawGroups.Length);
            Array.Copy(savedDisplayGroups, baseAsset.Unk1.DisplayGroups, savedDisplayGroups.Length);
            Array.Copy(savedCollisionMask, baseAsset.Unk1.CollisionMask, savedCollisionMask.Length);
            Array.Copy(savedEntityGroupIDs, baseAsset.EntityGroupIDs, savedEntityGroupIDs.Length);
            Array.Copy(savedUnkPartNames, baseAsset.UnkPartNames, savedUnkPartNames.Length);
            baseAsset.UnkT54PartName = savedUnkT54;
        }

        if (entityIds.Count == 0)
            return (0, nextEntityId);

        var writePath = FindMsbPath(modDir, msbFileName) ?? FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        // EMEVD: activate bloodstains in event 0
        var emevdFileName = $"{mapId}.emevd.dcx";
        var emevdPath = Path.Combine(modDir, "event", emevdFileName);
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
            if (!File.Exists(gameEmevdPath))
            {
                Console.WriteLine($"  Warning: {emevdFileName} not found, skipping EMEVD injection for {mapId}");
                return (entityIds.Count, nextEntityId);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine($"  Warning: Event 0 not found in {emevdFileName}, skipping SFX activation");
            return (entityIds.Count, nextEntityId);
        }

        foreach (var entityId in entityIds)
        {
            initEvent.Instructions.Add(events.ParseAdd(
                $"ChangeAssetEnableState({entityId}, Enabled)"));
            initEvent.Instructions.Add(events.ParseAdd(
                $"CreateAssetfollowingSFX({entityId}, {SFX_DUMMY_POLY}, {SFX_ID})"));
        }

        emevd.Write(emevdPath);
        return (entityIds.Count, nextEntityId);
    }

    // --- Helper methods ---

    private static MSBE.Part.Asset? FindNearestVanillaAsset(MSBE msb, Vector3 targetPos)
    {
        MSBE.Part.Asset? best = null;
        float bestDist = float.MaxValue;

        foreach (var asset in msb.Parts.Assets)
        {
            if (asset.EntityID >= FOGMOD_ENTITY_MIN && asset.EntityID < FOGMOD_ENTITY_MAX)
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

    /// <summary>
    /// Get DrawGroups for a position from the nearest MapPiece with non-zero DrawGroups.
    /// MapPieces are static level geometry whose DrawGroups reliably represent the
    /// rendering zone at their position.
    /// </summary>
    private static uint[]? GetDrawGroupsAtPosition(MSBE msb, Vector3 targetPos)
    {
        MSBE.Part.MapPiece? best = null;
        float bestDist = float.MaxValue;

        foreach (var piece in msb.Parts.MapPieces)
        {
            if (piece.Unk1.DrawGroups.All(g => g == 0))
                continue;

            var diff = piece.Position - targetPos;
            float dist = diff.X * diff.X + diff.Y * diff.Y + diff.Z * diff.Z;
            if (dist < bestDist)
            {
                bestDist = dist;
                best = piece;
            }
        }

        return best?.Unk1.DrawGroups.ToArray();
    }

    private static void ApplyDrawGroups(MSBE.Part.Asset asset, uint[] drawGroups)
    {
        for (int i = 0; i < asset.Unk1.DrawGroups.Length && i < drawGroups.Length; i++)
            asset.Unk1.DrawGroups[i] = drawGroups[i];
        for (int i = 0; i < asset.Unk1.DisplayGroups.Length && i < drawGroups.Length; i++)
            asset.Unk1.DisplayGroups[i] = drawGroups[i];
    }

    private static void SetNameIdent(MSBE.Part part)
    {
        var segments = part.Name.Split('_');
        if (segments.Length > 0 && int.TryParse(segments[^1], out var ident))
            part.Unk08 = ident;
    }

    private static void EnsureAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = modelName });
    }

    private static string GeneratePartName(IEnumerable<string> existingNames, string modelName)
    {
        var names = new HashSet<string>(existingNames);
        for (int i = 9900; i < 10000; i++)
        {
            var name = $"{modelName}_{i:D4}";
            if (!names.Contains(name))
                return name;
        }
        for (int i = 10000; ; i++)
        {
            var name = $"{modelName}_{i}";
            if (!names.Contains(name))
                return name;
        }
    }

    /// <summary>
    /// Scan all MSBs in modDir to find the highest entity ID in the FogMod range.
    /// FogMod's num4 counter allocates IDs across Assets, Enemies, Players, and Regions.
    /// </summary>
    private static uint FindMaxFogModEntityId(string modDir)
    {
        uint maxId = FOGMOD_ENTITY_MIN;

        var msbFiles = new List<string>();
        foreach (var dirName in MsbDirVariants)
        {
            var msbDir = Path.Combine(modDir, "map", dirName);
            if (Directory.Exists(msbDir))
                msbFiles.AddRange(Directory.GetFiles(msbDir, "*.msb.dcx"));
        }

        foreach (var msbPath in msbFiles)
        {
            var msb = MSBE.Read(msbPath);

            void CheckId(uint id)
            {
                if (id >= FOGMOD_ENTITY_MIN
                    && id < FOGMOD_ENTITY_MAX
                    && id > maxId)
                    maxId = id;
            }

            foreach (var p in msb.Parts.Assets)
                CheckId(p.EntityID);
            foreach (var p in msb.Parts.Enemies)
                CheckId(p.EntityID);
            foreach (var p in msb.Parts.Players)
                CheckId(p.EntityID);
            foreach (var r in msb.Regions.GetEntries())
                CheckId(r.EntityID);
        }

        Console.WriteLine($"  FogMod max entity ID: {maxId}, bloodstain IDs start at {maxId + 1}");
        return maxId;
    }

    private static string? FindMsbPath(string baseDir, string msbFileName)
    {
        foreach (var dirName in MsbDirVariants)
        {
            var path = Path.Combine(baseDir, "map", dirName, msbFileName);
            if (File.Exists(path))
                return path;
        }
        return null;
    }

    private static string FindOrCreateMsbDir(string modDir, string msbFileName)
    {
        var mapDir = Path.Combine(modDir, "map");
        if (Directory.Exists(mapDir))
        {
            foreach (var dirName in MsbDirVariants)
            {
                var dir = Path.Combine(mapDir, dirName);
                if (Directory.Exists(dir))
                    return Path.Combine(dir, msbFileName);
            }
        }
        return Path.Combine(mapDir, "mapstudio", msbFileName);
    }
}
