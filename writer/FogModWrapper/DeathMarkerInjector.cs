using System.Numerics;
using FogModWrapper.Models;
using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Places bloodstain visual markers near each fog gate in the DAG.
///
/// Each fog gate gets 3 bloodstain assets (AEG099_090 invisible anchor) spread
/// around it in 120-degree sectors, with a red glow SFX (CreateAssetfollowingSFX
/// with DummyPolyID 100, SfxID 42) activated in EMEVD event 0.
///
/// Two phases:
/// 1. MSB: clone AEG099_090 assets at random offsets around each gate
/// 2. EMEVD: enable each asset and attach the bloodstain SFX in event 0
/// </summary>
public static class DeathMarkerInjector
{
    private const string BLOODSTAIN_MODEL = "AEG099_090";
    private const int SFX_DUMMY_POLY = 100;
    private const int SFX_ID = 42;
    private const uint ENTITY_ID_BASE = 755895000;

    // Bloodstain placement parameters
    private const int BLOODSTAINS_PER_GATE = 3;
    private const float MIN_RADIUS = 1.5f;
    private const float MAX_RADIUS = 3.0f;
    private const float SECTOR_DEGREES = 120f;

    // MSB directory name variants (vanilla=PascalCase, FogMod under Wine=lowercase)
    private static readonly string[] MsbDirVariants = { "mapstudio", "MapStudio" };

    /// <summary>
    /// Parse a gate FullName like "m10_01_00_00_AEG099_001_9000" into (mapId, partName).
    /// The map ID is always the first 4 underscore-separated segments.
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
    /// Generate 3 Vector3 offsets around a gate, one per 120-degree sector.
    /// PRNG is seeded on gateEntityId.GetHashCode() for deterministic placement.
    /// Each bloodstain gets a sector (0-120, 120-240, 240-360) with a random angle
    /// within it and a random radius between 1.5m and 3.0m.
    /// Returns offsets as (sin*radius, 0, cos*radius).
    /// </summary>
    internal static Vector3[] GenerateOffsets(uint gateEntityId)
    {
        var rng = new Random(gateEntityId.GetHashCode());
        var offsets = new Vector3[BLOODSTAINS_PER_GATE];

        for (int i = 0; i < BLOODSTAINS_PER_GATE; i++)
        {
            float sectorStart = i * SECTOR_DEGREES;
            float angleDeg = sectorStart + (float)(rng.NextDouble() * SECTOR_DEGREES);
            float angleRad = angleDeg * MathF.PI / 180f;
            float radius = MIN_RADIUS + (float)(rng.NextDouble() * (MAX_RADIUS - MIN_RADIUS));

            offsets[i] = new Vector3(
                MathF.Sin(angleRad) * radius,
                0f,
                MathF.Cos(angleRad) * radius);
        }

        return offsets;
    }

    /// <summary>
    /// Inject bloodstain visual markers at all fog gates in the DAG.
    /// Phase 1: place AEG099_090 assets in MSBs near each gate.
    /// Phase 2: activate assets and attach SFX in EMEVD event 0.
    /// </summary>
    public static void Inject(string modDir, string gameDir, List<Connection> connections, Events events)
    {
        Console.WriteLine("Injecting death markers at fog gates...");

        // Collect all unique gate FullNames from connections, grouped by map
        var gatesByMap = CollectGatesByMap(connections);

        // Phase 1: MSB - place bloodstain assets
        var (bloodstainsByMap, totalAssets, totalMaps) = InjectMsb(modDir, gameDir, gatesByMap);

        Console.WriteLine($"  MSB: placed {totalAssets} bloodstain assets across {totalMaps} maps");

        // Phase 2: EMEVD - activate bloodstains
        var (emevdAssets, emevdMaps) = InjectEmevd(modDir, gameDir, events, bloodstainsByMap);

        Console.WriteLine($"  EMEVD: activated {emevdAssets} bloodstains across {emevdMaps} maps");
    }

    /// <summary>
    /// Collect all unique gate FullNames from connections (both exit and entrance),
    /// grouped by map ID.
    /// </summary>
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
            if (!gatesByMap.ContainsKey(mapId))
                gatesByMap[mapId] = new HashSet<string>();
            gatesByMap[mapId].Add(partName);
        }

        return gatesByMap;
    }

    /// <summary>
    /// Phase 1: For each map, read the MSB, find gate assets, place bloodstains.
    /// Returns mapping of map ID to list of bloodstain entity IDs for EMEVD injection.
    /// </summary>
    private static (Dictionary<string, List<uint>> BloodstainsByMap, int TotalAssets, int TotalMaps) InjectMsb(
        string modDir, string gameDir, Dictionary<string, HashSet<string>> gatesByMap)
    {
        var bloodstainsByMap = new Dictionary<string, List<uint>>();
        uint nextEntityId = ENTITY_ID_BASE;
        int totalAssets = 0;
        int totalMaps = 0;

        foreach (var (mapId, partNames) in gatesByMap)
        {
            var msbFileName = $"{mapId}.msb.dcx";

            // Try modDir first, then gameDir
            var msbPath = FindMsbPath(modDir, msbFileName) ?? FindMsbPath(gameDir, msbFileName);
            if (msbPath == null)
            {
                Console.WriteLine($"  Warning: {msbFileName} not found, skipping death markers for {mapId}");
                continue;
            }

            var msb = MSBE.Read(msbPath);
            var entityIds = new List<uint>();

            // Find a base asset to clone from (any existing asset in the MSB)
            var baseAsset = msb.Parts.Assets.FirstOrDefault();
            if (baseAsset == null)
            {
                Console.WriteLine($"  Warning: No asset parts in {mapId} MSB to clone from, skipping");
                continue;
            }

            // Ensure AEG099_090 model definition exists
            EnsureAssetModel(msb, BLOODSTAIN_MODEL);

            foreach (var partName in partNames)
            {
                // Find the gate asset: by name first, then by entity ID if numeric
                var gateAsset = msb.Parts.Assets.Find(a => a.Name == partName);
                if (gateAsset == null && uint.TryParse(partName, out uint entityIdLookup))
                {
                    gateAsset = msb.Parts.Assets.Find(a => a.EntityID == entityIdLookup);
                }
                if (gateAsset == null)
                {
                    Console.WriteLine($"  Warning: Gate asset '{partName}' not found in {mapId} MSB, skipping");
                    continue;
                }

                var offsets = GenerateOffsets(gateAsset.EntityID);

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

                    // Clear inherited references (FogRando's setAssetName pattern)
                    for (int j = 0; j < bloodstain.UnkPartNames.Length; j++)
                        bloodstain.UnkPartNames[j] = null;
                    bloodstain.UnkT54PartName = null;
                    Array.Clear(bloodstain.EntityGroupIDs);

                    msb.Parts.Assets.Add(bloodstain);
                    entityIds.Add(nextEntityId);
                    nextEntityId++;
                }
            }

            if (entityIds.Count > 0)
            {
                // Write MSB to modDir
                var writePath = FindMsbPath(modDir, msbFileName) ?? FindOrCreateMsbDir(modDir, msbFileName);
                Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
                msb.Write(writePath);

                bloodstainsByMap[mapId] = entityIds;
                totalAssets += entityIds.Count;
                totalMaps++;
            }
        }

        return (bloodstainsByMap, totalAssets, totalMaps);
    }

    /// <summary>
    /// Phase 2: For each map with bloodstains, add ChangeAssetEnableState + CreateAssetfollowingSFX
    /// to EMEVD event 0.
    /// </summary>
    private static (int TotalAssets, int TotalMaps) InjectEmevd(
        string modDir, string gameDir, Events events,
        Dictionary<string, List<uint>> bloodstainsByMap)
    {
        int totalAssets = 0;
        int totalMaps = 0;

        foreach (var (mapId, entityIds) in bloodstainsByMap)
        {
            var emevdFileName = $"{mapId}.emevd.dcx";
            var emevdPath = Path.Combine(modDir, "event", emevdFileName);

            // If not in modDir, copy from gameDir
            if (!File.Exists(emevdPath))
            {
                var gameEmevdPath = Path.Combine(gameDir, "event", emevdFileName);
                if (!File.Exists(gameEmevdPath))
                {
                    Console.WriteLine($"  Warning: {emevdFileName} not found, skipping EMEVD injection for {mapId}");
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
                File.Copy(gameEmevdPath, emevdPath);
            }

            var emevd = EMEVD.Read(emevdPath);
            var initEvent = emevd.Events.Find(e => e.ID == 0);
            if (initEvent == null)
            {
                Console.WriteLine($"  Warning: Event 0 not found in {emevdFileName}, skipping");
                continue;
            }

            foreach (var entityId in entityIds)
            {
                initEvent.Instructions.Add(events.ParseAdd(
                    $"ChangeAssetEnableState({entityId}, Enabled)"));
                initEvent.Instructions.Add(events.ParseAdd(
                    $"CreateAssetfollowingSFX({entityId}, {SFX_DUMMY_POLY}, {SFX_ID})"));
            }

            emevd.Write(emevdPath);
            totalAssets += entityIds.Count;
            totalMaps++;
        }

        return (totalAssets, totalMaps);
    }

    // --- Helper methods ---

    /// <summary>
    /// Set Unk08 from the numeric suffix of a part's Name.
    /// Replicates FogRando's setNameIdent (GameDataWriterE.cs:5263-5268).
    /// </summary>
    private static void SetNameIdent(MSBE.Part part)
    {
        var segments = part.Name.Split('_');
        if (segments.Length > 0 && int.TryParse(segments[^1], out var ident))
        {
            part.Unk08 = ident;
        }
    }

    /// <summary>
    /// Ensure an asset model definition exists in the MSB models list.
    /// </summary>
    private static void EnsureAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;
        msb.Models.Assets.Add(new MSBE.Model.Asset { Name = modelName });
    }

    /// <summary>
    /// Generate a unique MSB part name with incrementing suffix.
    /// Uses the 9900+ range to avoid conflicts with vanilla and FogMod parts.
    /// </summary>
    private static string GeneratePartName(IEnumerable<string> existingNames, string modelName)
    {
        var names = new HashSet<string>(existingNames);
        for (int i = 9900; i < 10000; i++)
        {
            var name = $"{modelName}_{i:D4}";
            if (!names.Contains(name))
                return name;
        }
        // Overflow: continue beyond 9999
        for (int i = 10000; ; i++)
        {
            var name = $"{modelName}_{i}";
            if (!names.Contains(name))
                return name;
        }
    }

    /// <summary>
    /// Find an MSB file under a base directory, trying both "MapStudio" (vanilla)
    /// and "mapstudio" (FogMod on Linux via Wine) directory names.
    /// </summary>
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

    /// <summary>
    /// Find the existing mapstudio directory in modDir, or create one
    /// matching the convention FogMod used (defaults to "mapstudio").
    /// </summary>
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
        // Default to lowercase (FogMod convention under Wine)
        return Path.Combine(mapDir, "mapstudio", msbFileName);
    }
}
