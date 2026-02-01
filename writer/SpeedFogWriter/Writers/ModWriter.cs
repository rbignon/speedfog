// writer/SpeedFogWriter/Writers/ModWriter.cs
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

public class ModWriter
{
    private readonly string _gameDir;
    private readonly string _outputDir;
    private readonly string _dataDir;
    private readonly SpeedFogGraph _graph;
    private readonly string? _vanillaDir;

    private GameDataLoader? _loader;
    private FogDataFile? _fogData;
    private ClusterFile? _clusterData;
    private FogLocations? _fogLocations;
    private EventTemplateRegistry? _eventRegistry;
    private EventBuilder? _eventBuilder;
    private EntityIdAllocator _idAllocator = new();
    private ScalingWriter? _scalingWriter;
    private FogAssetHelper? _fogAssetHelper;
    private List<FogGateEvent>? _fogGates;

    private readonly HashSet<string> _writeMsbs = new();
    private readonly HashSet<string> _writeEmevds = new() { "common", "common_func" };

    public ModWriter(string gameDir, string outputDir, string dataDir, SpeedFogGraph graph, string? vanillaDir = null)
    {
        _gameDir = gameDir;
        _outputDir = outputDir;
        _dataDir = dataDir;
        _graph = graph;
        _vanillaDir = vanillaDir;
    }

    public void Generate()
    {
        Console.WriteLine("Loading game data...");
        LoadGameData();

        Console.WriteLine("Loading SpeedFog data...");
        LoadSpeedFogData();

        Console.WriteLine("Generating scaling effects...");
        GenerateScaling();

        Console.WriteLine("Registering event templates...");
        RegisterTemplateEvents();

        Console.WriteLine("Initializing common events...");
        RegisterCommonEvents();

        Console.WriteLine("Creating fog gates...");
        CreateFogGates();

        Console.WriteLine("Adding starting items...");
        AddStartingItems();

        Console.WriteLine("Writing output...");
        WriteOutput();

        PrintSummary();
    }

    private void LoadGameData()
    {
        _loader = new GameDataLoader(_gameDir, _vanillaDir);
        _loader.LoadParams();
        _loader.LoadEmevds();
        _loader.InitializeEvents(Path.Combine(_dataDir, "er-common.emedf.json"));
    }

    private void LoadSpeedFogData()
    {
        _fogData = FogDataFile.Load(Path.Combine(_dataDir, "fog_data.json"));
        Console.WriteLine($"  Loaded {_fogData.Fogs.Count} fog entries");

        _clusterData = ClusterFile.Load(Path.Combine(_dataDir, "clusters.json"));
        Console.WriteLine($"  Loaded {_clusterData.ZoneMaps.Count} zone mappings");

        _fogLocations = FogLocations.Load(Path.Combine(_dataDir, "foglocations2.txt"));
        Console.WriteLine($"  Loaded {_fogLocations.EnemyAreas.Count} enemy areas");

        // Load FogRando event templates from fogevents.txt
        var fogEventsPath = Path.Combine(_dataDir, "fogevents.txt");
        _eventRegistry = EventTemplateRegistry.Load(fogEventsPath);
        _eventBuilder = new EventBuilder(_eventRegistry, _loader!.EventsHelper!);
        Console.WriteLine($"  Loaded {_eventRegistry.GetAllTemplates().Count()} event templates from fogevents.txt");

        ValidateFogReferences();
        LoadRequiredMsbs();
    }

    private void ValidateFogReferences()
    {
        var missing = new List<string>();
        foreach (var edge in _graph.Edges)
        {
            if (_fogData!.GetFog(edge.FogId) == null)
                missing.Add(edge.FogId);
        }

        if (missing.Any())
        {
            Console.WriteLine($"  WARNING: {missing.Count} fog_ids not found:");
            foreach (var fog in missing.Take(5))
                Console.WriteLine($"    - {fog}");
        }
    }

    private void LoadRequiredMsbs()
    {
        const string TemplateMsb = "m60_46_38_00";
        var requiredMaps = new HashSet<string> { TemplateMsb };

        foreach (var edge in _graph.Edges)
        {
            var fog = _fogData!.GetFog(edge.FogId);
            if (fog != null) requiredMaps.Add(fog.Map);
        }

        foreach (var node in _graph.AllNodes())
        {
            var map = _clusterData!.GetMapForCluster(node.Zones);
            if (map != null) requiredMaps.Add(map);
        }

        _loader!.LoadMsbs(requiredMaps);
        _fogAssetHelper = new FogAssetHelper(_loader.Msbs);
    }

    private void GenerateScaling()
    {
        _scalingWriter = new ScalingWriter(_loader!.Params!);
        _scalingWriter.GenerateScalingEffects();

        var applicator = new EnemyScalingApplicator(
            _loader.Msbs,
            _loader.Emevds,
            _graph,
            _fogLocations!,
            _scalingWriter);
        applicator.ApplyScaling();

        // Track modified files for writing
        foreach (var msb in applicator.ModifiedMsbs)
            _writeMsbs.Add(msb);
        foreach (var emevd in applicator.ModifiedEmevds)
            _writeEmevds.Add(emevd);
    }

    private void RegisterTemplateEvents()
    {
        // Register non-common templates in common_func.emevd
        // These are parameterized events like scale, showsfx, fogwarp
        if (!_loader!.Emevds.TryGetValue("common_func", out var commonFuncEmevd))
        {
            Console.WriteLine("  WARNING: common_func.emevd not loaded, skipping template registration");
            return;
        }

        var funcCount = 0;
        foreach (var templateEvent in _eventBuilder!.GetAllTemplateEvents())
        {
            // Check if event already exists in common_func (FogRando already has them)
            if (commonFuncEmevd.Events.Any(e => e.ID == templateEvent.ID))
            {
                Console.WriteLine($"    Template event {templateEvent.ID} already exists in common_func, skipping");
                continue;
            }

            commonFuncEmevd.Events.Add(templateEvent);
            funcCount++;
        }
        Console.WriteLine($"  Registered {funcCount} template events in common_func");

        // Register common_* templates in common.emevd
        // These are events like common_fingerstart, common_gracetable, etc.
        if (!_loader.Emevds.TryGetValue("common", out var commonEmevd))
        {
            Console.WriteLine("  WARNING: common.emevd not loaded, skipping common template registration");
            return;
        }

        var commonCount = 0;
        foreach (var templateEvent in _eventBuilder.GetCommonTemplateEvents())
        {
            // Check if event already exists in common
            if (commonEmevd.Events.Any(e => e.ID == templateEvent.ID))
            {
                Console.WriteLine($"    Template event {templateEvent.ID} already exists in common, skipping");
                continue;
            }

            commonEmevd.Events.Add(templateEvent);
            commonCount++;
        }
        Console.WriteLine($"  Registered {commonCount} template events in common");
    }

    // FogRando event IDs for events without names in fogevents.txt
    private const int AbductionEventId = 755850220;  // Iron Virgin grab immortality

    /// <summary>
    /// Initialize common events required for SpeedFog.
    /// Equivalent to FogRando settings: roundtable=true, scale=true, ChapelInit=false.
    /// Note: We use ChapelInit=false mode because common_fingerstart/fingerdoor work together,
    /// and common_roundtable waits for the flag set by common_fingerstart (1040292051).
    /// </summary>
    private void RegisterCommonEvents()
    {
        if (!_loader!.Emevds.TryGetValue("common", out var commonEmevd))
        {
            Console.WriteLine("  WARNING: common.emevd not loaded, skipping common event initialization");
            return;
        }

        // Find event 0 (common initialization event)
        var event0 = commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (event0 == null)
        {
            Console.WriteLine("  WARNING: Event 0 not found in common.emevd");
            return;
        }

        // NOTE: Do NOT set SpEffect 4280 (trapped) here!
        // SpEffect 4280 PREVENTS fog traversal. The fogwarp template checks:
        // "IfCharacterHasSpEffect(AND_06, 10000, 4280, false, ...)" - player must NOT have 4280
        // 4280 is set during certain events (Iron Virgin grab) and cleared afterward.

        // Initialize common events based on FogRando forced settings
        var slot = 0;

        // common_fingerstart (755850280) - Start after picking up finger
        // Sets flag 1040292051 when flag 60210 (finger picked up) is set
        if (_eventBuilder!.HasTemplate("common_fingerstart"))
        {
            var init = _eventBuilder.BuildInitializeEvent("common_fingerstart", slot++);
            event0.Instructions.Add(init);
            Console.WriteLine("  Initialized common_fingerstart");
        }

        // common_fingerdoor (755850282) - Auto-open Chapel door
        // Opens door when in Chapel and game has started
        if (_eventBuilder.HasTemplate("common_fingerdoor"))
        {
            var init = _eventBuilder.BuildInitializeEvent("common_fingerdoor", slot++);
            event0.Instructions.Add(init);
            Console.WriteLine("  Initialized common_fingerdoor");
        }

        // common_autostart (755850204) - Award starting items (flasks, etc.)
        // Awards ItemLot 10010000 if flag 60210 is not set, for moved Chapel starts
        if (_eventBuilder.HasTemplate("common_autostart"))
        {
            var init = _eventBuilder.BuildInitializeEvent("common_autostart", slot++);
            event0.Instructions.Add(init);
            Console.WriteLine("  Initialized common_autostart (starting items)");
        }

        // common_roundtable (755850202) - Roundtable access after game start
        // Makes Roundtable available after flag 1040292051 (set by common_fingerstart)
        // Note: We use common_roundtable (not common_gracetable) because fingerstart sets 1040292051
        if (_eventBuilder.HasTemplate("common_roundtable"))
        {
            var init = _eventBuilder.BuildInitializeEvent("common_roundtable", slot++);
            event0.Instructions.Add(init);
            Console.WriteLine("  Initialized common_roundtable (roundtable access)");
        }

        // common_bellofreturn (755850250) - Return to Chapel with Bell of Return
        // Allows using Bell of Return to warp back to Chapel
        if (_eventBuilder.HasTemplate("common_bellofreturn"))
        {
            var init = _eventBuilder.BuildInitializeEvent("common_bellofreturn", slot++);
            event0.Instructions.Add(init);
            Console.WriteLine("  Initialized common_bellofreturn");
        }

        // Abduction immortality (755850220) - Iron Virgin grab protection
        // This event has no Name in fogevents.txt, so we use BuildInitializeEventById
        // Prevents death from Iron Virgin abduction grab in Raya Lucaria
        {
            var init = _eventBuilder.BuildInitializeEventById(AbductionEventId, slot++);
            event0.Instructions.Add(init);
            Console.WriteLine($"  Initialized abduction immortality (event {AbductionEventId})");
        }

        Console.WriteLine($"  Initialized {slot} common events");
    }

    private void CreateFogGates()
    {
        // Modify ActionButtonParam for fog gates (same as FogRando)
        // This makes the action prompt appear at the correct height on fog gates
        var actionButtonParamTable = _loader!.Params!["ActionButtonParam"];
        if (actionButtonParamTable[10000] != null)
        {
            actionButtonParamTable[10000]["height"].Value = 2f;
            actionButtonParamTable[10000]["baseHeightOffset"].Value = -1f;
        }

        var fogWriter = new FogGateWriter(_fogData!, _clusterData!, _idAllocator);
        _fogGates = fogWriter.CreateFogGates(_graph);
        Console.WriteLine($"  Created {_fogGates.Count} fog gate definitions");

        // Create makefrom fog assets (these don't exist in vanilla MSBs)
        // Also enable existing fog gates that need to be visible
        var makeFromCount = 0;
        var enabledCount = 0;
        foreach (var fogGate in _fogGates)
        {
            if (fogGate.IsMakeFrom)
            {
                var asset = _fogAssetHelper!.CreateFogGate(
                    fogGate.SourceMap,
                    fogGate.FogModel,
                    fogGate.FogPosition,
                    fogGate.FogRotation,
                    (uint)fogGate.FogEntityId);

                if (asset != null)
                {
                    _writeMsbs.Add(fogGate.SourceMap);
                    makeFromCount++;
                }
            }
            else
            {
                // Enable existing fog gate in MSB (may be disabled by default)
                if (_fogAssetHelper!.EnableExistingFogGate(
                    fogGate.SourceMap,
                    (uint)fogGate.FogEntityId,
                    fogGate.FogAssetName))
                {
                    _writeMsbs.Add(fogGate.SourceMap);
                    enabledCount++;
                }
            }
        }
        if (makeFromCount > 0)
            Console.WriteLine($"  Created {makeFromCount} makefrom fog assets");
        if (enabledCount > 0)
            Console.WriteLine($"  Enabled {enabledCount} existing fog assets");

        // Create spawn regions in target MSBs
        var warpWriter = new WarpWriter(_loader!.Msbs, _fogData!);
        foreach (var fogGate in _fogGates)
        {
            warpWriter.CreateSpawnRegion(fogGate);
            _writeMsbs.Add(fogGate.TargetMap);
        }

        // Generate EMEVD events for each fog gate
        // Cache extracted SFX IDs per fog entity to handle multiple edges using the same fog gate
        // The SFX is only in the vanilla events which get NOPed on first encounter
        var fogSfxCache = new Dictionary<int, int>();

        foreach (var fogGate in _fogGates)
        {
            GenerateFogGateEvents(fogGate, DefaultButtonParam, DefaultFogSfx, fogSfxCache);
        }

        Console.WriteLine($"  Generated EMEVD events for {_fogGates.Count} fog gates");
    }

    // Vanilla fog gate visibility event IDs that need to be NOPed
    // These events control fog gate appearance based on boss defeat flags, etc.
    // Reference: FogRando fogevents.txt events 9005800, 9005801, 9005811
    private static readonly HashSet<int> VanillaFogVisibilityEvents = new() { 9005800, 9005801, 9005811 };

    // Event ID for the fog visibility event that contains the SFX parameter
    // 9005811 has format: (slot, eventId, X0_4=defeatFlag, X4_4=fogEntity, X8_4=sfxId, X12_4=secondFlag)
    private const int VanillaFogSfxEvent = 9005811;

    // Default parameters for fog gate events
    // These match FogRando behavior
    private const int DefaultButtonParam = 10000;  // ActionButton param for fog gate interaction
    private const int DefaultFogSfx = 3;           // Default fog visual effect ID

    private void GenerateFogGateEvents(FogGateEvent fogGate, int buttonParam, int defaultFogSfx, Dictionary<int, int> fogSfxCache)
    {
        // Handle item-triggered warps (e.g., Pureblood Knight's Medal) differently
        // They need to go in common.emevd and use a different template
        if (fogGate.IsItemWarp)
        {
            GenerateItemWarpEvent(fogGate);
            return;
        }

        // Get source map EMEVD
        if (!_loader!.Emevds.TryGetValue(fogGate.SourceMap, out var emevd))
        {
            Console.WriteLine($"    WARNING: EMEVD not loaded for {fogGate.SourceMap}");
            return;
        }

        // Find event 0 (initialization event)
        var event0 = emevd.Events.FirstOrDefault(e => e.ID == 0);
        if (event0 == null)
        {
            Console.WriteLine($"    WARNING: Event 0 not found in {fogGate.SourceMap}");
            return;
        }

        // Check cache first for SFX ID (handles multiple edges using the same fog gate)
        int fogSfx;
        if (fogSfxCache.TryGetValue(fogGate.FogEntityId, out var cachedSfx))
        {
            fogSfx = cachedSfx;
        }
        else
        {
            // NOP vanilla fog gate visibility events that reference this fog entity
            // This prevents the vanilla game from controlling the fog gate visibility
            // which would conflict with our showsfx event
            // Also extracts the SFX ID from vanilla 9005811 events to use for our showsfx call
            var (nopCount, extractedSfxId) = NopVanillaFogEvents(emevd, fogGate.FogEntityId, fogGate.SourceMap);
            if (nopCount > 0)
            {
                Console.WriteLine($"    [DEBUG] {fogGate.SourceMap}: NOPed {nopCount} vanilla fog visibility events for entity {fogGate.FogEntityId}");
            }

            // Use the extracted SFX ID from vanilla events, or fall back to default
            // Different fog gates have different SFX IDs (e.g., Chapel uses 16, Stormveil uses 3)
            fogSfx = extractedSfxId ?? defaultFogSfx;
            fogSfxCache[fogGate.FogEntityId] = fogSfx;

            if (extractedSfxId != null && extractedSfxId != defaultFogSfx)
            {
                Console.WriteLine($"    [DEBUG] {fogGate.SourceMap}: Using extracted SFX ID {fogSfx} for entity {fogGate.FogEntityId}");
            }
        }

        // Pack target map bytes into a single int for warp instruction
        // WarpPlayer reads bytes at X12_1, X13_1, X14_1, X15_1 from a 4-byte value at X12_4
        // Little-endian: mapBytes[0] is at lowest address (X12_1)
        var mapBytes = fogGate.TargetMapBytes;
        int packedMapBytes = mapBytes[0] | (mapBytes[1] << 8) | (mapBytes[2] << 16) | (mapBytes[3] << 24);

        // Add InitializeEvent for showsfx template
        // showsfx(fog_gate, sfx_id)
        var showSfxInit = _eventBuilder!.BuildInitializeEvent(
            "showsfx",
            0,
            fogGate.FogEntityId,    // X0_4 = fog_gate
            fogSfx                   // X4_4 = sfx_id
        );
        event0.Instructions.Add(showSfxInit);
        Console.WriteLine($"    [DEBUG] {fogGate.SourceMap} event0: InitializeCommonEvent showsfx({fogGate.FogEntityId}, {fogSfx})");

        // Add InitializeEvent for fogwarp template (9005777)
        // Uses FogRando's fogwarp with speffect 4280 check (init in RegisterCommonEvents)
        // fogwarp parameters: X0_4=fog_gate, X4_4=button_param, X8_4=warp_target, X12_4=map_bytes,
        //                     X16_4=boss_defeat_flag, X20_4=boss_trap_flag, X24_4=alt_flag,
        //                     X28_4=alt_warp_target, X32_4=alt_map_bytes, X36_4=rotate_target
        var fogWarpInit = _eventBuilder.BuildInitializeEvent(
            "fogwarp",
            0,
            fogGate.FogEntityId,         // X0_4 = fog gate entity
            buttonParam,                  // X4_4 = button param (10000)
            (int)fogGate.WarpRegionId,   // X8_4 = warp target region
            packedMapBytes,              // X12_4 = packed map bytes [m, area, block, sub]
            0,                           // X16_4 = boss defeat flag (0 = no restriction)
            0,                           // X20_4 = boss trap flag (0 = no trap)
            0,                           // X24_4 = alternative flag (0 = no alt warp)
            0,                           // X28_4 = alternative warp target (unused)
            0,                           // X32_4 = alternate map bytes (unused)
            (int)fogGate.WarpRegionId    // X36_4 = rotate character target (face spawn point)
        );
        event0.Instructions.Add(fogWarpInit);

        // Mark EMEVD for writing
        _writeEmevds.Add(fogGate.SourceMap);
    }

    /// <summary>
    /// Generate events for item-triggered warps (e.g., Pureblood Knight's Medal).
    /// Following FogRando's approach: modify the vanilla WarpPlayer destination instead of
    /// creating a new event. This keeps all the vanilla logic (SpEffect check, world type check)
    /// and just changes where the player warps to.
    /// Reference: FogRando fogevents.txt event 922 with Template WarpID
    /// </summary>
    private void GenerateItemWarpEvent(FogGateEvent fogGate)
    {
        // Get common.emevd for global events
        if (!_loader!.Emevds.TryGetValue("common", out var commonEmevd))
        {
            Console.WriteLine($"    WARNING: common.emevd not loaded, cannot create item warp for {fogGate.EdgeFogId}");
            return;
        }

        // The vanilla warp destination region ID is the same as the fog ID for item warps
        // e.g., fog ID "12052021" = region 12052021 in Mohgwyn Palace
        if (!int.TryParse(fogGate.EdgeFogId, out var vanillaRegionId))
        {
            Console.WriteLine($"    WARNING: Cannot parse fog ID {fogGate.EdgeFogId} as region ID");
            return;
        }

        // Modify the vanilla WarpPlayer instruction to point to our new destination
        // This keeps all the vanilla event logic (SpEffect detection, world type check, etc.)
        var mapBytes = fogGate.TargetMapBytes;
        var modifyCount = ModifyVanillaItemWarp(
            commonEmevd,
            vanillaRegionId,
            (int)fogGate.WarpRegionId,
            mapBytes
        );

        if (modifyCount > 0)
        {
            Console.WriteLine($"    [DEBUG] common: Modified {modifyCount} WarpPlayer from region {vanillaRegionId} to {fogGate.WarpRegionId} in {fogGate.TargetMap}");
            if (modifyCount > 1)
            {
                Console.WriteLine($"    WARNING: Multiple WarpPlayer instructions found for region {vanillaRegionId}, this is unexpected");
            }
        }
        else
        {
            Console.WriteLine($"    WARNING: Could not find vanilla WarpPlayer to region {vanillaRegionId} in common.emevd");
        }

        // Mark common.emevd for writing
        _writeEmevds.Add("common");
    }

    /// <summary>
    /// Find and MODIFY vanilla WarpPlayer instructions that warp to a specific region.
    /// FogRando approach: instead of NOPing and creating a new event, we modify the
    /// existing WarpPlayer to point to our new destination.
    /// WarpPlayer is Bank 2003, ID 14.
    /// Arguments: AreaID(1), BlockID(1), Sub1(1), Sub2(1), SpawnPoint(4), Unknown(4)
    /// </summary>
    /// <param name="emevd">The common.emevd file</param>
    /// <param name="vanillaRegionId">The vanilla spawn point region ID to find</param>
    /// <param name="newRegionId">The new spawn point region ID</param>
    /// <param name="newMapBytes">The new map bytes [m, area, block, sub]</param>
    /// <returns>Number of instructions modified</returns>
    private int ModifyVanillaItemWarp(EMEVD emevd, int vanillaRegionId, int newRegionId, byte[] newMapBytes)
    {
        const int WarpPlayerBank = 2003;
        const int WarpPlayerId = 14;
        // WarpPlayer args layout: Area(1), Block(1), Sub1(1), Sub2(1), SpawnPoint(4), Unknown(4)
        const int SpawnPointOffset = 4;

        int modifyCount = 0;

        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];

                // Check if this is WarpPlayer (2003[14])
                if (instr.Bank != WarpPlayerBank || instr.ID != WarpPlayerId)
                    continue;

                var args = instr.ArgData;
                // WarpPlayer needs at least 12 bytes: 4 bytes for area/block/sub, 4 for spawnpoint, 4 for unknown
                if (args.Length < 12)
                    continue;

                // Read SpawnPoint (int32 at offset 4)
                int spawnPoint = BitConverter.ToInt32(args, SpawnPointOffset);

                if (spawnPoint == vanillaRegionId)
                {
                    // Modify the instruction's arguments to point to new destination
                    // Create new args array with modified values
                    var newArgs = new byte[args.Length];
                    Array.Copy(args, newArgs, args.Length);

                    // Update map bytes (first 4 bytes): Area, Block, Sub1, Sub2
                    // Note: WarpPlayer uses Area, Block, Sub1, Sub2 in that order
                    // But our mapBytes are [m, area, block, sub]
                    // Based on FogRando fogevents.txt: WarpPlayer(12, 5, 0, 0, 12052021*, 0)
                    // This means Area=12, Block=5, Sub1=0, Sub2=0 for m12_05_00_00
                    newArgs[0] = newMapBytes[0];  // m -> Area
                    newArgs[1] = newMapBytes[1];  // area -> Block
                    newArgs[2] = newMapBytes[2];  // block -> Sub1
                    newArgs[3] = newMapBytes[3];  // sub -> Sub2

                    // Update SpawnPoint (int32 at offset 4)
                    BitConverter.GetBytes(newRegionId).CopyTo(newArgs, SpawnPointOffset);

                    // Create new instruction with modified args
                    evt.Instructions[i] = new EMEVD.Instruction(WarpPlayerBank, WarpPlayerId, newArgs);
                    modifyCount++;
                    Console.WriteLine($"    [DEBUG] Modified WarpPlayer in event {evt.ID}: region {spawnPoint} -> {newRegionId}, map {newMapBytes[0]}_{newMapBytes[1]}_{newMapBytes[2]}_{newMapBytes[3]}");
                }
            }
        }

        return modifyCount;
    }

    /// <summary>
    /// Find and NOP vanilla InitializeCommonEvent instructions that control fog gate visibility.
    /// These events (9005800, 9005801, 9005811) disable the fog gate and wait for conditions
    /// before enabling it. We need to remove them so our showsfx event can control visibility.
    /// Also extracts the SFX ID from 9005811 events for use in our showsfx calls.
    /// </summary>
    /// <param name="emevd">The map's EMEVD file (searches ALL events, not just event 0)</param>
    /// <param name="fogEntityId">The fog gate entity ID to look for</param>
    /// <param name="mapName">Map name for logging</param>
    /// <returns>Tuple of (number of instructions NOPed, extracted SFX ID or null if not found)</returns>
    private (int nopCount, int? sfxId) NopVanillaFogEvents(EMEVD emevd, int fogEntityId, string mapName)
    {
        int nopCount = 0;
        int? extractedSfxId = null;

        // Search ALL events in the EMEVD, not just event 0
        // The vanilla InitializeCommonEvent calls may be in various events (e.g., event 10012849 for Chapel)
        foreach (var evt in emevd.Events)
        {
            for (int i = 0; i < evt.Instructions.Count; i++)
            {
                var instr = evt.Instructions[i];

                // Check if this is InitializeCommonEvent (2000[6])
                if (instr.Bank != 2000 || instr.ID != 6)
                    continue;

                // Parse the instruction arguments
                // InitializeCommonEvent format: (slot, eventId, args...)
                // Slot is typically 0, eventId identifies which common_func event to call
                var args = instr.ArgData;
                if (args.Length < 8) // Need at least slot(4) + eventId(4)
                    continue;

                // Read eventId (bytes 4-7, little-endian int32)
                // Note: slot is bytes 0-3, eventId is bytes 4-7
                int eventId = BitConverter.ToInt32(args, 4);

                if (!VanillaFogVisibilityEvents.Contains(eventId))
                    continue;

                // Read the fog entity ID argument
                // For 9005800/9005801/9005811:
                // Format is typically (slot, eventId, X0_4, X4_4, ...)
                // X0_4 = boss defeat flag, X4_4 = fog gate entity
                // So fog entity is at byte offset 12 (after slot=0-3, eventId=4-7, X0_4=8-11)
                if (args.Length < 16)
                    continue;

                int fogArg = BitConverter.ToInt32(args, 12);

                if (fogArg == fogEntityId)
                {
                    // Extract SFX ID from 9005811 events before NOPing
                    // 9005811 format: (slot, eventId, X0_4=defeatFlag, X4_4=fogEntity, X8_4=sfxId, X12_4=secondFlag)
                    // X8_4 is at byte offset 16 (slot=0-3, eventId=4-7, X0_4=8-11, X4_4=12-15, X8_4=16-19)
                    if (eventId == VanillaFogSfxEvent && args.Length >= 20 && extractedSfxId == null)
                    {
                        extractedSfxId = BitConverter.ToInt32(args, 16);
                    }

                    // Replace with NOP instruction (1014, 69) - same as FogRando
                    evt.Instructions[i] = new EMEVD.Instruction(1014, 69);
                    nopCount++;
                }
            }
        }

        return (nopCount, extractedSfxId);
    }

    private void AddStartingItems()
    {
        if (!_loader!.Emevds.TryGetValue("common", out var commonEmevd))
            throw new InvalidOperationException("common.emevd not loaded");

        var itemWriter = new StartingItemsWriter(commonEmevd, _loader.EventsHelper!, _idAllocator);
        itemWriter.AddCoreItems();

        var allZones = _graph.AllNodes().SelectMany(n => n.Zones);
        itemWriter.AddItemsForZones(allZones);
    }

    private void WriteOutput()
    {
        var modDir = Path.Combine(_outputDir, "mods", "speedfog");

        // DCX compression types for Elden Ring (numeric values used for compatibility)
        // 13 = DCX_KRAK (Kraken compression, used for regulation.bin params)
        // 9 = DCX_DFLT_10000_24_9 (used for EMEVD and MSB files)
        // These match FogRando's GameDataWriterE.cs L4982
        const DCX.Type ParamDcx = (DCX.Type)13;
        const DCX.Type EmevdDcx = (DCX.Type)9;
        const DCX.Type MsbDcx = (DCX.Type)9;

        // Write params
        var paramDir = Path.Combine(modDir, "param", "gameparam");
        Directory.CreateDirectory(paramDir);
        var regulationOut = Path.Combine(paramDir, "regulation.bin");
        _loader!.Editor.OverrideBndRel<PARAM>(
            Path.Combine(_gameDir, "regulation.bin"),
            regulationOut,
            _loader.Params!.Inner,
            f => f.Write(),
            null,
            ParamDcx);
        Console.WriteLine($"  Written: regulation.bin");

        // Write EMEVDs
        var eventDir = Path.Combine(modDir, "event");
        Directory.CreateDirectory(eventDir);
        foreach (var name in _writeEmevds)
        {
            if (_loader.Emevds.TryGetValue(name, out var emevd))
            {
                emevd.Write(Path.Combine(eventDir, $"{name}.emevd.dcx"), EmevdDcx);
            }
        }
        Console.WriteLine($"  Written: {_writeEmevds.Count} EMEVD files");

        // Write MSBs
        var mapDir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);
        foreach (var name in _writeMsbs)
        {
            if (_loader.Msbs.TryGetValue(name, out var msb))
            {
                msb.Write(Path.Combine(mapDir, $"{name}.msb.dcx"), MsbDcx);
            }
        }
        Console.WriteLine($"  Written: {_writeMsbs.Count} MSB files");

        Console.WriteLine($"\nOutput: {modDir}");
    }

    private void PrintSummary()
    {
        Console.WriteLine("\n=== SpeedFog Summary ===");
        Console.WriteLine($"Seed: {_graph.Seed}");
        Console.WriteLine($"Nodes: {_graph.TotalNodes}");
        Console.WriteLine($"Paths: {_graph.TotalPaths}");
        Console.WriteLine($"Path weights: [{string.Join(", ", _graph.PathWeights)}]");
        Console.WriteLine($"Fog gates: {_fogGates?.Count ?? 0}");
    }
}
