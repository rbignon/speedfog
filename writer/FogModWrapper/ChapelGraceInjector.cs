using SoulsFormats;
using SoulsIds;

namespace FogModWrapper;

/// <summary>
/// Injects a Site of Grace at Chapel of Anticipation (m10_01_00_00).
/// Replicates the grace creation logic from FogMod's custom bonfire system
/// as a post-processing step after FogMod writes.
///
/// Three modifications:
/// 1. MSB: Grace visual (AEG099_060), grace NPC (c1000), player warp target
/// 2. BonfireWarpParam: Fast travel entry in regulation.bin
/// 3. EMEVD: RegisterBonfire instruction in map event 0
///
/// Based on FogMod's CustomBonfires "chapel" handling in GameDataWriterE.cs:4693-4818
/// and the fog.txt CustomBonfires entry for Chapel of Anticipation.
/// </summary>
public static class ChapelGraceInjector
{
    private const string MAP_ID = "m10_01_00_00";

    // Grace position (from fog.txt CustomBonfires chapel entry, verified by FogRando)
    private const float POS_X = -32.574062f;
    private const float POS_Y = 21.330698f;
    private const float POS_Z = -91.523232f;
    private const float ROT_Y = 166.234589f;

    // Entity ID base for the bonfire asset (FogRando chapel convention: 10011952)
    // chrEntity = bonfireEntity - 1000, playerEntity = bonfireEntity - 970
    private const uint BONFIRE_ENTITY_BASE = 10011952;

    // BonfireWarpParam row ID base (FogRando chapel convention: 100102)
    private const int BONFIRE_ROW_BASE = 100102;

    // Template bonfire entity to copy cosmetic fields from (existing Chapel grace)
    private const uint TEMPLATE_BONFIRE_ENTITY = 10001950;

    // FMG text ID for "Chapel of Anticipation" (vanilla PlaceName entry)
    private const int TEXT_ID = 10010;

    // Grace NPC parameters (c1000 = invisible bonfire controller NPC)
    private const string GRACE_NPC_MODEL = "c1000";
    private const int NPC_THINK_PARAM = 1;
    private const int NPC_NPC_PARAM = 10000000;
    private const int NPC_TALK_ID = 1000;

    // Chapel-specific collision part (from FogRando chapel handling)
    private const string COLLISION_PART = "h002000";

    // Grace asset model (the visual flame)
    private const string GRACE_ASSET_MODEL = "AEG099_060";

    // MSB directory name variants (vanilla=PascalCase, FogMod under Wine=lowercase)
    private static readonly string[] MSB_DIR_VARIANTS = { "mapstudio", "MapStudio" };

    // Preferred source entities to clone from (fog.txt CustomBonfires)
    private const string PREFERRED_ASSET = "AEG217_237_0501";
    private const string PREFERRED_ENEMY = "c4690_9000";
    private const string PREFERRED_PLAYER = "c0000_0000";

    /// <summary>
    /// Inject a Site of Grace at Chapel of Anticipation.
    /// Skips gracefully if the grace already exists (e.g., added by Item Randomizer).
    /// </summary>
    public static void Inject(string modDir, string gameDir)
    {
        Console.WriteLine("Injecting Chapel of Anticipation grace...");

        // Step 1: MSB - add grace asset, NPC, and player spawn
        var msbResult = InjectMsb(modDir, gameDir);
        if (msbResult == null)
            return;

        // Step 2: BonfireWarpParam - add fast travel entry
        var bonfireFlag = InjectBonfireWarpParam(modDir, msbResult.Value.BonfireEntityId);
        if (bonfireFlag == null)
            return;

        // Step 3: EMEVD - add RegisterBonfire instruction
        InjectEmevd(modDir, gameDir, bonfireFlag.Value, msbResult.Value.BonfireEntityId);

        Console.WriteLine("Chapel of Anticipation grace injected successfully");
    }

    private struct MsbResult
    {
        public uint BonfireEntityId;
    }

    /// <summary>
    /// Add grace asset, NPC, and player spawn to the chapel MSB.
    /// </summary>
    private static MsbResult? InjectMsb(string modDir, string gameDir)
    {
        var msbFileName = $"{MAP_ID}.msb.dcx";

        // FogMod writes "mapstudio" (lowercase) but vanilla game uses "MapStudio" (PascalCase).
        // On Linux (case-sensitive fs), we must check both.
        var modMsbPath = FindMsbPath(modDir, msbFileName);
        var gameMsbPath = FindMsbPath(gameDir, msbFileName);

        string msbPath;
        if (modMsbPath != null)
        {
            msbPath = modMsbPath;
        }
        else if (gameMsbPath != null)
        {
            msbPath = gameMsbPath;
        }
        else
        {
            Console.WriteLine($"Warning: {msbFileName} not found, skipping chapel grace");
            return null;
        }

        var msb = MSBE.Read(msbPath);

        // Check if grace already exists (e.g., Item Randomizer or FogMod already created it)
        var existingGrace = msb.Parts.Assets.Find(a =>
            a.ModelName == GRACE_ASSET_MODEL &&
            a.EntityID >= BONFIRE_ENTITY_BASE && a.EntityID < BONFIRE_ENTITY_BASE + 100);
        if (existingGrace != null)
        {
            Console.WriteLine($"  Chapel grace already exists (entity {existingGrace.EntityID}), skipping MSB");
            return new MsbResult { BonfireEntityId = existingGrace.EntityID };
        }

        // Collect existing entity IDs for conflict avoidance
        var existingEntities = new HashSet<uint>();
        foreach (var p in msb.Parts.Assets)
            existingEntities.Add(p.EntityID);
        foreach (var p in msb.Parts.Enemies)
            existingEntities.Add(p.EntityID);
        foreach (var p in msb.Parts.Players)
            existingEntities.Add(p.EntityID);

        // Allocate bonfire entity ID (increment if conflicts)
        uint bonfireEntity = BONFIRE_ENTITY_BASE;
        while (existingEntities.Contains(bonfireEntity))
            bonfireEntity++;
        existingEntities.Add(bonfireEntity);

        // Derived entity IDs (FogRando convention: GameDataWriterE.cs:4696-4697)
        uint chrEntity = bonfireEntity - 1000;
        uint playerEntity = bonfireEntity - 970;

        // Ensure model definitions exist in MSB
        EnsureAssetModel(msb, GRACE_ASSET_MODEL);
        EnsureEnemyModel(msb, GRACE_NPC_MODEL);

        // 1. Grace asset (AEG099_060 - the visual flame model)
        var baseAsset = msb.Parts.Assets.Find(a => a.Name == PREFERRED_ASSET)
                        ?? msb.Parts.Assets.FirstOrDefault();
        if (baseAsset == null)
        {
            Console.WriteLine("Warning: No asset parts in MSB to clone from");
            return null;
        }

        var graceAsset = (MSBE.Part.Asset)baseAsset.DeepCopy();
        graceAsset.ModelName = GRACE_ASSET_MODEL;
        graceAsset.Name = GeneratePartName(msb.Parts.Assets.Select(a => a.Name), GRACE_ASSET_MODEL);
        graceAsset.Position = new System.Numerics.Vector3(POS_X, POS_Y, POS_Z);
        graceAsset.Rotation = new System.Numerics.Vector3(0f, ROT_Y, 0f);
        graceAsset.EntityID = bonfireEntity;
        msb.Parts.Assets.Add(graceAsset);

        // 2. Grace NPC (c1000 - invisible bonfire controller)
        var baseEnemy = msb.Parts.Enemies.Find(e => e.Name == PREFERRED_ENEMY)
                        ?? msb.Parts.Enemies.FirstOrDefault();
        if (baseEnemy == null)
        {
            Console.WriteLine("Warning: No enemy parts in MSB to clone from");
            return null;
        }

        var graceNpc = (MSBE.Part.Enemy)baseEnemy.DeepCopy();
        graceNpc.ModelName = GRACE_NPC_MODEL;
        graceNpc.Name = GeneratePartName(msb.Parts.Enemies.Select(e => e.Name), GRACE_NPC_MODEL);
        graceNpc.Position = new System.Numerics.Vector3(POS_X, POS_Y, POS_Z);
        graceNpc.Rotation = new System.Numerics.Vector3(0f, ROT_Y, 0f);
        graceNpc.EntityID = chrEntity;
        graceNpc.ThinkParamID = NPC_THINK_PARAM;
        graceNpc.NPCParamID = NPC_NPC_PARAM;
        graceNpc.TalkID = NPC_TALK_ID;
        graceNpc.CharaInitID = -1;
        graceNpc.CollisionPartName = COLLISION_PART;
        msb.Parts.Enemies.Add(graceNpc);

        // 3. Player warp target (2m forward from grace, matching FogRando moveInDirection)
        var basePlayer = msb.Parts.Players.Find(p => p.Name == PREFERRED_PLAYER)
                         ?? msb.Parts.Players.FirstOrDefault();
        if (basePlayer == null)
        {
            Console.WriteLine("Warning: No player parts in MSB to clone from");
            return null;
        }

        var playerPos = MoveInDirection(POS_X, POS_Y, POS_Z, ROT_Y, 2f);
        var gracePlayer = (MSBE.Part.Player)basePlayer.DeepCopy();
        gracePlayer.Name = GeneratePartName(msb.Parts.Players.Select(p => p.Name), "c0000");
        gracePlayer.Position = playerPos;
        gracePlayer.Rotation = new System.Numerics.Vector3(0f, ROT_Y, 0f);
        gracePlayer.EntityID = playerEntity;
        msb.Parts.Players.Add(gracePlayer);

        // Write to modDir (always write to mod output, not game dir).
        // Use the same directory case that FogMod used (mapstudio vs MapStudio).
        var writePath = modMsbPath ?? FindOrCreateMsbDir(modDir, msbFileName);
        Directory.CreateDirectory(Path.GetDirectoryName(writePath)!);
        msb.Write(writePath);

        Console.WriteLine($"  MSB: asset {graceAsset.Name} (entity {bonfireEntity}), " +
                          $"NPC {graceNpc.Name} (entity {chrEntity}), " +
                          $"player {gracePlayer.Name} (entity {playerEntity})");

        return new MsbResult { BonfireEntityId = bonfireEntity };
    }

    /// <summary>
    /// Add bonfire warp entry to regulation.bin for fast travel support.
    /// Returns the allocated event flag ID, or null on failure.
    /// </summary>
    private static uint? InjectBonfireWarpParam(string modDir, uint bonfireEntityId)
    {
        var regulationPath = Path.Combine(modDir, "regulation.bin");
        if (!File.Exists(regulationPath))
        {
            Console.WriteLine("Warning: regulation.bin not found, skipping bonfire param injection");
            return null;
        }

        var defPath = Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "eldendata", "Defs", "BonfireWarpParam.xml");
        if (!File.Exists(defPath))
        {
            Console.WriteLine($"Warning: BonfireWarpParam.xml not found at {defPath}");
            return null;
        }

        var paramdef = PARAMDEF.XmlDeserialize(defPath);

        BND4 regulation;
        try
        {
            regulation = SFUtil.DecryptERRegulation(regulationPath);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Warning: Failed to decrypt regulation.bin: {ex.Message}");
            return null;
        }

        var bonfireFile = regulation.Files.Find(f => f.Name.EndsWith("BonfireWarpParam.param"));
        if (bonfireFile == null)
        {
            Console.WriteLine("Warning: BonfireWarpParam.param not found in regulation.bin");
            return null;
        }

        var bonfireParam = PARAM.Read(bonfireFile.Bytes);
        bonfireParam.ApplyParamdef(paramdef);

        // Check if a row already exists for this bonfire entity
        var existingRow = bonfireParam.Rows.Find(r =>
            (uint)r["bonfireEntityId"].Value == bonfireEntityId);
        if (existingRow != null)
        {
            var existingFlag = (uint)existingRow["eventflagId"].Value;
            Console.WriteLine($"  BonfireWarpParam: row already exists for entity {bonfireEntityId} " +
                              $"(flag {existingFlag})");
            return existingFlag;
        }

        // Find template bonfire row to copy cosmetic fields from
        var templateRow = bonfireParam.Rows.Find(r =>
            (uint)r["bonfireEntityId"].Value == TEMPLATE_BONFIRE_ENTITY);
        if (templateRow == null)
        {
            Console.WriteLine("Warning: Template bonfire row (entity 10001950) not found");
            return null;
        }

        // Allocate unique row ID (increment from base if conflicts)
        var existingIds = new HashSet<int>(bonfireParam.Rows.Select(r => r.ID));
        int rowId = BONFIRE_ROW_BASE;
        while (existingIds.Contains(rowId))
            rowId++;

        // Allocate unique event flag ID (increment from template's flag if conflicts)
        var existingFlags = new HashSet<uint>(
            bonfireParam.Rows.Select(r => (uint)r["eventflagId"].Value));
        uint flagId = (uint)templateRow["eventflagId"].Value;
        while (existingFlags.Contains(flagId))
            flagId++;

        // Parse map coordinates from MAP_ID (m10_01_00_00)
        var mapParts = MAP_ID.Split('_');
        byte areaNo = byte.Parse(mapParts[0].Substring(1)); // "m10" -> 10
        byte gridX = byte.Parse(mapParts[1]);                // "01"
        byte gridZ = byte.Parse(mapParts[2]);                // "00"

        // Create new BonfireWarpParam row
        var newRow = new PARAM.Row(rowId, "", bonfireParam.AppliedParamdef);
        newRow["eventflagId"].Value = flagId;
        newRow["bonfireEntityId"].Value = bonfireEntityId;
        newRow["areaNo"].Value = areaNo;
        newRow["gridXNo"].Value = gridX;
        newRow["gridZNo"].Value = gridZ;
        newRow["posX"].Value = POS_X;
        newRow["posY"].Value = POS_Y;
        newRow["posZ"].Value = POS_Z;
        newRow["textId1"].Value = TEXT_ID;
        newRow["bonfireSubCategorySortId"].Value = (ushort)9999;

        // Copy cosmetic/display fields from template row (GameDataWriterE.cs:4796-4809)
        foreach (var field in new[]
        {
            "forbiddenIconId", "bonfireSubCategoryId", "iconId",
            "dispMask00", "dispMask01", "dispMask02",
            "noIgnitionSfxDmypolyId_0", "noIgnitionSfxId_0"
        })
        {
            newRow[field].Value = templateRow[field].Value;
        }

        bonfireParam.Rows.Add(newRow);
        bonfireParam.Rows = bonfireParam.Rows.OrderBy(r => r.ID).ToList();

        bonfireFile.Bytes = bonfireParam.Write();
        SFUtil.EncryptERRegulation(regulationPath, regulation);

        Console.WriteLine($"  BonfireWarpParam: row {rowId}, flag {flagId}, entity {bonfireEntityId}");
        return flagId;
    }

    /// <summary>
    /// Add RegisterBonfire instruction to the chapel map EMEVD event 0.
    /// </summary>
    private static void InjectEmevd(string modDir, string gameDir, uint bonfireFlag, uint bonfireEntity)
    {
        var emevdPath = Path.Combine(modDir, "event", $"{MAP_ID}.emevd.dcx");

        // If FogMod didn't write this EMEVD, copy from game dir
        if (!File.Exists(emevdPath))
        {
            var gameEmevdPath = Path.Combine(gameDir, "event", $"{MAP_ID}.emevd.dcx");
            if (!File.Exists(gameEmevdPath))
            {
                Console.WriteLine($"Warning: {MAP_ID}.emevd.dcx not found, skipping EMEVD injection");
                return;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(emevdPath)!);
            File.Copy(gameEmevdPath, emevdPath);
        }

        var emevd = EMEVD.Read(emevdPath);
        var initEvent = emevd.Events.Find(e => e.ID == 0);
        if (initEvent == null)
        {
            Console.WriteLine($"Warning: Event 0 not found in {MAP_ID}.emevd, skipping");
            return;
        }

        // RegisterBonfire / SpawnBonfire (bank 2009, id 3)
        // Args: flagId (uint4), entityId (uint4), unk1 (float4=0), unk2 (float4=0),
        //       unk3 (int4=0), unk4 (float4=5)
        // Using raw bytes for reliability (avoids EMEDF name dependency)
        var args = new byte[24];
        BitConverter.GetBytes(bonfireFlag).CopyTo(args, 0);
        BitConverter.GetBytes(bonfireEntity).CopyTo(args, 4);
        BitConverter.GetBytes(0f).CopyTo(args, 8);
        BitConverter.GetBytes(0f).CopyTo(args, 12);
        BitConverter.GetBytes(0).CopyTo(args, 16);
        BitConverter.GetBytes(5f).CopyTo(args, 20);
        initEvent.Instructions.Add(new EMEVD.Instruction(2009, 3, args));

        emevd.Write(emevdPath);
        Console.WriteLine($"  EMEVD: RegisterBonfire(flag={bonfireFlag}, entity={bonfireEntity})");
    }

    // --- Helper methods ---

    /// <summary>
    /// Move a position forward by 'dist' meters in the direction of Y-axis rotation.
    /// Replicates FogRando's moveInDirection (GameDataWriterE.cs:5326-5330).
    /// </summary>
    private static System.Numerics.Vector3 MoveInDirection(
        float x, float y, float z, float rotY, float dist)
    {
        float rad = rotY * MathF.PI / 180f;
        return new System.Numerics.Vector3(
            x + MathF.Sin(rad) * dist,
            y,
            z + MathF.Cos(rad) * dist);
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
    /// Ensure an enemy model definition exists in the MSB models list.
    /// </summary>
    private static void EnsureEnemyModel(MSBE msb, string modelName)
    {
        if (msb.Models.Enemies.Any(m => m.Name == modelName))
            return;
        msb.Models.Enemies.Add(new MSBE.Model.Enemy { Name = modelName });
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
    /// Returns the full path if found, null otherwise.
    /// </summary>
    private static string? FindMsbPath(string baseDir, string msbFileName)
    {
        foreach (var dirName in MSB_DIR_VARIANTS)
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
            foreach (var dirName in MSB_DIR_VARIANTS)
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
