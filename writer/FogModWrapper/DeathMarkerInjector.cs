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
    private const uint FOGMOD_ENTITY_MIN = 755890000;
    private const uint FOGMOD_ENTITY_MAX = 755900000;

    private const int BLOODSTAINS_PER_GATE = 3;
    private const float MIN_RADIUS = 1.5f;
    private const float MAX_RADIUS = 3.0f;
    private const float Y_OFFSET = 0.12f;

    private const int DEATH_MARKER_EVENT_BASE = 755862100;

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
    /// Generate 3 offsets on the approach side of a gate, spread across a 120-degree arc.
    /// FogMod places the ASide warp region in the gate's facing direction (0 degrees).
    /// The player stands on the opposite side of the warp region to trigger it, so:
    /// - isASide=true: arc at 180 degrees (opposite the facing direction)
    /// - isASide=false: arc at 0 degrees (the facing direction)
    /// PRNG seeded on gateEntityId for deterministic placement.
    /// </summary>
    internal static Vector3[] GenerateOffsets(uint gateEntityId, float gateRotY, bool isASide)
    {
        var rng = new Random(gateEntityId.GetHashCode());
        var offsets = new Vector3[BLOODSTAINS_PER_GATE];
        float gateRad = gateRotY * MathF.PI / 180f;

        const float arcSpread = 120f;
        const float sectorSize = arcSpread / BLOODSTAINS_PER_GATE;
        float arcCenter = isASide ? 180f : 0f;
        float arcStart = arcCenter - arcSpread / 2f;

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
    /// Inject bloodstain visual markers at exit fog gates in the DAG.
    /// Requires deathFlags to be non-empty; returns immediately otherwise.
    /// Each bloodstain is controlled by a per-cluster death flag via a dedicated
    /// EMEVD event. gateSides maps gate FullName to (ASideArea, BSideArea) from
    /// fog.txt, used to determine which side of the gate the bloodstains are on.
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

        // Start above FogMod's entity range to avoid collisions without scanning MSBs.
        // FogMod allocates from FOGMOD_ENTITY_MIN (755890000) and uses far fewer than
        // the 10000 available IDs in a typical SpeedFog DAG.
        uint nextEntityId = FOGMOD_ENTITY_MAX;
        int totalAssets = 0;
        int totalMaps = 0;
        int nextEventOffset = 0;

        var specsByMap = CollectExitGatesByMap(connections, eventMap, deathFlags, gateSides);
        foreach (var (mapId, specs) in specsByMap)
        {
            var (count, nextId, nextEvt) = InjectMap(
                modDir, gameDir, events, mapId, specs, nextEntityId, nextEventOffset);
            if (count > 0)
            {
                totalAssets += count;
                totalMaps++;
            }
            nextEntityId = nextId;
            nextEventOffset = nextEvt;
        }

        Console.WriteLine($"  Placed {totalAssets} bloodstain markers across {totalMaps} maps");
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
            var (mapId, partName) = ParseGateFullName(conn.ExitGate);
            bool isASide = ResolveIsASide(conn.ExitGate, conn.ExitArea, gateSides);

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
    /// </summary>
    private static (int Count, uint NextEntityId, int NextEventOffset) InjectMap(
        string modDir, string gameDir, Events events,
        string mapId, List<BloodstainSpec> specs, uint nextEntityId, int eventOffset)
    {
        var msbFileName = $"{mapId}.msb.dcx";
        var msbPath = MsbHelper.FindMsbPath(modDir, msbFileName) ?? MsbHelper.FindMsbPath(gameDir, msbFileName);
        if (msbPath == null)
        {
            Console.WriteLine($"  Warning: {msbFileName} not found, skipping death markers for {mapId}");
            return (0, nextEntityId, eventOffset);
        }

        var msb = MSBE.Read(msbPath);

        if (msb.Parts.MapPieces.Count == 0)
            return (0, nextEntityId, eventOffset);

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

            // Precompute offsets for both sides. A gate used as both entrance and exit
            // in different connections may need bloodstains on different sides: the exit
            // connection approaches from one zone, the entrance connection from another.
            var offsetsASide = GenerateOffsets(gateAsset.EntityID, gateAsset.Rotation.Y, isASide: true);
            var offsetsBSide = GenerateOffsets(gateAsset.EntityID, gateAsset.Rotation.Y, isASide: false);

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
                var offsets = spec.IsASide ? offsetsASide : offsetsBSide;
                var offset = offsets[spec.TierIndex % 3];

                var bloodstain = (MSBE.Part.Asset)baseAsset.DeepCopy();
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
            return (0, nextEntityId, eventOffset);

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
                Console.WriteLine($"  Warning: {emevdFileName} not found, skipping EMEVD injection for {mapId}");
                return (placedCount, nextEntityId, eventOffset);
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine($"  Warning: Event 0 not found in {emevdFileName}, skipping SFX activation");
            return (placedCount, nextEntityId, eventOffset);
        }

        foreach (var (deathFlag, entityIds) in entityIdsByFlag)
        {
            long eventId = DEATH_MARKER_EVENT_BASE + eventOffset;
            eventOffset++;

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
        return (placedCount, nextEntityId, eventOffset);
    }

    // --- Helper methods ---

    /// <summary>
    /// Determine if the approach area is on the ASide (gate facing direction) of the gate.
    /// ASide = forward direction of the fog gate model (based on Y rotation).
    /// BSide = opposite direction (180 degrees from facing).
    /// Falls back to BSide (current behavior) if the gate or area is not found.
    /// </summary>
    private static bool ResolveIsASide(
        string gateFullName, string approachArea,
        Dictionary<string, (string ASideArea, string BSideArea)> gateSides)
    {
        if (!gateSides.TryGetValue(gateFullName, out var sides))
            return false; // default: BSide (legacy behavior)

        if (sides.ASideArea == approachArea)
            return true;
        if (sides.BSideArea == approachArea)
            return false;

        // Area not found on either side (zone name mismatch). Fall back to BSide.
        Console.WriteLine($"  Warning: Area '{approachArea}' not on either side of gate {gateFullName}" +
            $" (A={sides.ASideArea}, B={sides.BSideArea}), defaulting to BSide");
        return false;
    }

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

}
