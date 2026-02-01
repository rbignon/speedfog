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
    private SpeedFogEventConfig? _eventConfig;
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

        _eventConfig = SpeedFogEventConfig.Load(Path.Combine(_dataDir, "speedfog-events.yaml"));
        _eventBuilder = new EventBuilder(_eventConfig, _loader!.EventsHelper!);
        Console.WriteLine($"  Loaded {_eventConfig.Templates.Count} event templates");

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
        // Add template events to common_func.emevd
        // These are parameterized events that get instantiated via InitializeEvent
        if (!_loader!.Emevds.TryGetValue("common_func", out var commonFuncEmevd))
        {
            Console.WriteLine("  WARNING: common_func.emevd not loaded, skipping template registration");
            return;
        }

        var count = 0;
        foreach (var templateEvent in _eventBuilder!.GetAllTemplateEvents())
        {
            // Check if event already exists
            if (commonFuncEmevd.Events.Any(e => e.ID == templateEvent.ID))
            {
                Console.WriteLine($"    Template event {templateEvent.ID} already exists, skipping");
                continue;
            }

            commonFuncEmevd.Events.Add(templateEvent);
            Console.WriteLine($"    [DEBUG] Added template event ID {templateEvent.ID} with {templateEvent.Instructions.Count} instructions");
            count++;
        }

        Console.WriteLine($"  Registered {count} template events in common_func");
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
        var buttonParam = _eventBuilder!.GetDefaultInt("button_param", 63000);
        var defaultFogSfx = _eventBuilder.GetDefaultInt("fog_sfx", 3);

        // Cache extracted SFX IDs per fog entity to handle multiple edges using the same fog gate
        // The SFX is only in the vanilla events which get NOPed on first encounter
        var fogSfxCache = new Dictionary<int, int>();

        foreach (var fogGate in _fogGates)
        {
            GenerateFogGateEvents(fogGate, buttonParam, defaultFogSfx, fogSfxCache);
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

    private void GenerateFogGateEvents(FogGateEvent fogGate, int buttonParam, int defaultFogSfx, Dictionary<int, int> fogSfxCache)
    {
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

        // Add InitializeEvent for fogwarp_simple template
        // fogwarp_simple(fog_gate, button_param, warp_region, map_bytes, rotate_target)
        var fogWarpInit = _eventBuilder.BuildInitializeEvent(
            "fogwarp_simple",
            0,
            fogGate.FogEntityId,         // X0_4 = fog_gate
            buttonParam,                  // X4_4 = button_param
            (int)fogGate.WarpRegionId,   // X8_4 = warp_region
            packedMapBytes,              // X12_4 = packed map bytes [m, area, block, sub]
            (int)fogGate.WarpRegionId    // X16_4 = rotate_target (face spawn point)
        );
        event0.Instructions.Add(fogWarpInit);

        // Mark EMEVD for writing
        _writeEmevds.Add(fogGate.SourceMap);
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
