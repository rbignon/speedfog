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

        foreach (var msb in applicator.ModifiedMsbs)
            _writeMsbs.Add(msb);
    }

    private void CreateFogGates()
    {
        var fogWriter = new FogGateWriter(_fogData!, _clusterData!, _idAllocator);
        _fogGates = fogWriter.CreateFogGates(_graph);
        Console.WriteLine($"  Created {_fogGates.Count} fog gate definitions");

        var warpWriter = new WarpWriter(_loader!.Msbs, _fogData!);
        foreach (var fogGate in _fogGates)
        {
            warpWriter.CreateSpawnRegion(fogGate);
            _writeMsbs.Add(fogGate.TargetMap);
            _writeEmevds.Add(fogGate.SourceMap);
        }
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
