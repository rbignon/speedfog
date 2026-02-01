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

    public ModWriter(string gameDir, string outputDir, string dataDir, SpeedFogGraph graph)
    {
        _gameDir = gameDir;
        _outputDir = outputDir;
        _dataDir = dataDir;
        _graph = graph;
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
        _loader = new GameDataLoader(_gameDir);
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
            count++;
        }

        Console.WriteLine($"  Registered {count} template events in common_func");
    }

    private void CreateFogGates()
    {
        var fogWriter = new FogGateWriter(_fogData!, _clusterData!, _idAllocator);
        _fogGates = fogWriter.CreateFogGates(_graph);
        Console.WriteLine($"  Created {_fogGates.Count} fog gate definitions");

        // Create makefrom fog assets (these don't exist in vanilla MSBs)
        var makeFromCount = 0;
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
        }
        if (makeFromCount > 0)
            Console.WriteLine($"  Created {makeFromCount} makefrom fog assets");

        // Create spawn regions in target MSBs
        var warpWriter = new WarpWriter(_loader!.Msbs, _fogData!);
        foreach (var fogGate in _fogGates)
        {
            warpWriter.CreateSpawnRegion(fogGate);
            _writeMsbs.Add(fogGate.TargetMap);
        }

        // Generate EMEVD events for each fog gate
        var buttonParam = _eventBuilder!.GetDefaultInt("button_param", 63000);
        var fogSfx = _eventBuilder.GetDefaultInt("fog_sfx", 8011);

        foreach (var fogGate in _fogGates)
        {
            GenerateFogGateEvents(fogGate, buttonParam, fogSfx);
        }

        Console.WriteLine($"  Generated EMEVD events for {_fogGates.Count} fog gates");
    }

    private void GenerateFogGateEvents(FogGateEvent fogGate, int buttonParam, int fogSfx)
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

        // Parse target map bytes for warp instruction
        var mapBytes = fogGate.TargetMapBytes;

        // Add InitializeEvent for showsfx template
        // showsfx(fog_gate, sfx_id)
        var showSfxInit = _eventBuilder!.BuildInitializeEvent(
            "showsfx",
            0,
            fogGate.FogEntityId,    // X0_4 = fog_gate
            fogSfx                   // X4_4 = sfx_id
        );
        event0.Instructions.Add(showSfxInit);

        // Add InitializeEvent for fogwarp_simple template
        // fogwarp_simple(fog_gate, button_param, warp_region, map_m, map_area, map_block, map_sub, rotate_target)
        var fogWarpInit = _eventBuilder.BuildInitializeEvent(
            "fogwarp_simple",
            0,
            fogGate.FogEntityId,         // X0_4 = fog_gate
            buttonParam,                  // X4_4 = button_param
            (int)fogGate.WarpRegionId,   // X8_4 = warp_region
            (int)mapBytes[0],            // X12_1 = map_m
            (int)mapBytes[1],            // X13_1 = map_area
            (int)mapBytes[2],            // X14_1 = map_block
            (int)mapBytes[3],            // X15_1 = map_sub
            (int)fogGate.WarpRegionId    // X16_4 = rotate_target (face spawn point)
        );
        event0.Instructions.Add(fogWarpInit);

        // Mark EMEVD for writing
        _writeEmevds.Add(fogGate.SourceMap);
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
