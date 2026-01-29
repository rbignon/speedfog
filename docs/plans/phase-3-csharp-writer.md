# Phase 3: C# Writer - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 2: DAG Generation](./phase-2-dag-generation.md)
**Status**: Ready for implementation

## Objective

Create the C# component that reads `graph.json` and generates Elden Ring mod files using SoulsFormats. This involves adapting code from FogRando for fog gate creation, warp events, and enemy scaling.

## Prerequisites

- Phase 2 completed (working `graph.json` output)
- .NET 8.0 SDK
- SoulsFormats library (via NuGet or local reference)
- Access to FogRando decompiled source at `/home/rom1/src/games/ER/fog/src-decompiled/`

## Deliverables

```
speedfog/writer/
├── SpeedFogWriter/
│   ├── SpeedFogWriter.csproj
│   ├── Program.cs                 # CLI entry point
│   │
│   ├── Models/
│   │   ├── SpeedFogGraph.cs       # JSON deserialization
│   │   ├── NodeData.cs
│   │   └── LayerData.cs
│   │
│   ├── Writers/
│   │   ├── ModWriter.cs           # Main orchestrator
│   │   ├── FogGateWriter.cs       # Creates fog wall events
│   │   ├── WarpWriter.cs          # Creates warp teleportations
│   │   ├── ScalingWriter.cs       # Enemy stat scaling
│   │   └── StartingItemsWriter.cs # Key items at spawn
│   │
│   └── Helpers/
│       ├── EmevdHelper.cs         # EMEVD utilities
│       ├── ParamHelper.cs         # PARAM utilities
│       └── PathHelper.cs          # File path utilities
│
└── SpeedFogWriter.sln
```

---

## Task 3.1: Project Setup

### SpeedFogWriter.csproj

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net8.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="System.Text.Json" Version="8.0.0" />
  </ItemGroup>

  <ItemGroup>
    <!-- Reference SoulsFormats - adjust path as needed -->
    <Reference Include="SoulsFormats">
      <HintPath>../lib/SoulsFormats.dll</HintPath>
    </Reference>
    <Reference Include="SoulsIds">
      <HintPath>../lib/SoulsIds.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

### Directory Structure for Libraries

```
speedfog/writer/
├── lib/
│   ├── SoulsFormats.dll      # From SoulsFormatsNEXT
│   ├── SoulsIds.dll          # Game constants
│   └── ... (other dependencies)
└── SpeedFogWriter/
```

**Note**: Download SoulsFormats from [soulsmods/SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT) releases.

---

## Task 3.2: JSON Models (Models/)

### SpeedFogGraph.cs

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// Root structure of graph.json
/// </summary>
public class SpeedFogGraph
{
    [JsonPropertyName("seed")]
    public int Seed { get; set; }

    [JsonPropertyName("total_layers")]
    public int TotalLayers { get; set; }

    [JsonPropertyName("total_nodes")]
    public int TotalNodes { get; set; }

    [JsonPropertyName("total_paths")]
    public int TotalPaths { get; set; }

    [JsonPropertyName("layers")]
    public List<LayerData> Layers { get; set; } = new();

    [JsonPropertyName("start")]
    public string? StartNodeId { get; set; }

    [JsonPropertyName("end")]
    public string? EndNodeId { get; set; }

    /// <summary>
    /// Load graph from JSON file.
    /// </summary>
    public static SpeedFogGraph Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<SpeedFogGraph>(json)
            ?? throw new InvalidOperationException("Failed to parse graph.json");
    }

    /// <summary>
    /// Get all nodes in the graph.
    /// </summary>
    public IEnumerable<NodeData> AllNodes()
    {
        return Layers.SelectMany(l => l.Nodes);
    }

    /// <summary>
    /// Get node by ID.
    /// </summary>
    public NodeData? GetNode(string id)
    {
        return AllNodes().FirstOrDefault(n => n.Id == id);
    }

    /// <summary>
    /// Get all edges (connections between nodes).
    /// </summary>
    public IEnumerable<(NodeData Source, NodeData Target)> AllEdges()
    {
        foreach (var node in AllNodes())
        {
            foreach (var exitId in node.Exits)
            {
                var target = GetNode(exitId);
                if (target != null)
                {
                    yield return (node, target);
                }
            }
        }
    }
}
```

### LayerData.cs

```csharp
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class LayerData
{
    [JsonPropertyName("index")]
    public int Index { get; set; }

    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    [JsonPropertyName("nodes")]
    public List<NodeData> Nodes { get; set; } = new();
}
```

### NodeData.cs

```csharp
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

public class NodeData
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("zone")]
    public string ZoneId { get; set; } = "";

    [JsonPropertyName("zone_name")]
    public string ZoneName { get; set; } = "";

    [JsonPropertyName("zone_map")]
    public string ZoneMap { get; set; } = "";

    [JsonPropertyName("zone_type")]
    public string ZoneType { get; set; } = "";

    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    [JsonPropertyName("boss")]
    public string? Boss { get; set; }

    [JsonPropertyName("entries")]
    public List<string> Entries { get; set; } = new();

    [JsonPropertyName("exits")]
    public List<string> Exits { get; set; } = new();

    /// <summary>
    /// The layer this node belongs to (set during processing).
    /// </summary>
    [JsonIgnore]
    public int Layer { get; set; }

    /// <summary>
    /// The tier for scaling (set during processing).
    /// </summary>
    [JsonIgnore]
    public int Tier { get; set; }

    public bool HasBoss => !string.IsNullOrEmpty(Boss);
    public bool IsStart => ZoneType == "start";
    public bool IsFinalBoss => ZoneType == "final_boss";
}
```

---

## Task 3.3: ScalingWriter.cs

Adapted from FogRando's `EldenScaling.cs`. This creates SpEffect entries for enemy stat scaling.

### Key Concepts

- **Tier**: Difficulty level (1-34 in vanilla, we use 1-28)
- **SpEffect**: Special effect that modifies entity stats
- **Scaling fields**: HP, damage, defense, stamina, etc.

### ScalingWriter.cs

```csharp
using SoulsFormats;
using SoulsIds;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates SpEffect entries for enemy scaling based on zone tier.
/// Adapted from FogRando's EldenScaling.cs
/// </summary>
public class ScalingWriter
{
    // Base SpEffect IDs used by the game
    private const int VanillaScalingBase = 7000;

    // Our custom SpEffect range (must not conflict with game or other mods)
    private const int CustomScalingBase = 7900000;

    // Scaling multipliers per tier (simplified from FogRando)
    // Index 0 = tier 1, index 27 = tier 28
    private static readonly double[] HealthMultipliers = GenerateMultipliers(1.0, 4.5, 28);
    private static readonly double[] DamageMultipliers = GenerateMultipliers(1.0, 3.5, 28);
    private static readonly double[] DefenseMultipliers = GenerateMultipliers(1.0, 2.0, 28);
    private static readonly double[] SoulMultipliers = GenerateMultipliers(1.0, 10.0, 28);

    private readonly ParamDictionary _params;
    private int _nextSpEffectId;

    /// <summary>
    /// Maps (sourceTier, targetTier) to SpEffect ID.
    /// </summary>
    public Dictionary<(int, int), int> TierTransitions { get; } = new();

    public ScalingWriter(ParamDictionary gameParams)
    {
        _params = gameParams;
        _nextSpEffectId = CustomScalingBase;
    }

    /// <summary>
    /// Generate all scaling SpEffects for tier transitions.
    /// </summary>
    public void GenerateScalingEffects()
    {
        var spEffectParam = _params["SpEffectParam"];
        var templateRow = spEffectParam[VanillaScalingBase];

        // Generate SpEffects for all tier transitions
        for (int fromTier = 1; fromTier <= 28; fromTier++)
        {
            for (int toTier = 1; toTier <= 28; toTier++)
            {
                if (fromTier == toTier) continue;

                var spEffectId = CreateScalingEffect(spEffectParam, templateRow, fromTier, toTier);
                TierTransitions[(fromTier, toTier)] = spEffectId;
            }
        }

        // Sort param rows by ID
        spEffectParam.Rows.Sort((a, b) => a.ID.CompareTo(b.ID));
    }

    private int CreateScalingEffect(PARAM spEffectParam, PARAM.Row template, int fromTier, int toTier)
    {
        var id = _nextSpEffectId++;

        // Create new row
        var row = new PARAM.Row(id, "", spEffectParam.AppliedParamdef);

        // Copy template values
        foreach (var cell in template.Cells)
        {
            row[cell.Def.InternalName].Value = cell.Value;
        }

        // Calculate scaling factors
        double healthFactor = HealthMultipliers[toTier - 1] / HealthMultipliers[fromTier - 1];
        double damageFactor = DamageMultipliers[toTier - 1] / DamageMultipliers[fromTier - 1];
        double defenseFactor = DefenseMultipliers[toTier - 1] / DefenseMultipliers[fromTier - 1];
        double soulFactor = SoulMultipliers[toTier - 1] / SoulMultipliers[fromTier - 1];

        // Apply scaling factors
        row["maxHpRate"].Value = (float)healthFactor;
        row["maxStaminaRate"].Value = (float)healthFactor;

        row["physicsAttackPowerRate"].Value = (float)damageFactor;
        row["magicAttackPowerRate"].Value = (float)damageFactor;
        row["fireAttackPowerRate"].Value = (float)damageFactor;
        row["thunderAttackPowerRate"].Value = (float)damageFactor;
        row["darkAttackPowerRate"].Value = (float)damageFactor;

        row["physicsDiffenceRate"].Value = (float)defenseFactor;
        row["magicDiffenceRate"].Value = (float)defenseFactor;
        row["fireDiffenceRate"].Value = (float)defenseFactor;
        row["thunderDiffenceRate"].Value = (float)defenseFactor;
        row["darkDiffenceRate"].Value = (float)defenseFactor;

        row["haveSoulRate"].Value = (float)soulFactor;

        spEffectParam.Rows.Add(row);
        return id;
    }

    /// <summary>
    /// Get the SpEffect ID for transitioning between tiers.
    /// </summary>
    public int GetTransitionEffect(int fromTier, int toTier)
    {
        if (fromTier == toTier)
            return -1; // No scaling needed

        return TierTransitions.GetValueOrDefault((fromTier, toTier), -1);
    }

    private static double[] GenerateMultipliers(double min, double max, int count)
    {
        var result = new double[count];
        for (int i = 0; i < count; i++)
        {
            // Exponential curve for smoother progression
            double t = (double)i / (count - 1);
            result[i] = min * Math.Pow(max / min, t);
        }
        return result;
    }
}
```

---

## Task 3.4: FogGateWriter.cs

Creates fog wall events using EMEVD. Adapted from FogRando's event creation logic.

### Key Concepts

- **EMEVD**: Event script format used by FromSoft games
- **Fog Wall**: Visual barrier that triggers warp on contact
- **Event Flag**: Persistent game state (used to track fog traversal)

### FogGateWriter.cs

```csharp
using SoulsFormats;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates fog gate events between zones.
/// Adapted from FogRando's GameDataWriterE.cs
/// </summary>
public class FogGateWriter
{
    // Custom event ID range
    private const int CustomEventBase = 79000000;

    // Custom flag ID range
    private const int CustomFlagBase = 79000000;

    private int _nextEventId;
    private int _nextFlagId;

    public FogGateWriter()
    {
        _nextEventId = CustomEventBase;
        _nextFlagId = CustomFlagBase;
    }

    /// <summary>
    /// Create fog gate events for all edges in the graph.
    /// </summary>
    public List<FogGateEvent> CreateFogGates(SpeedFogGraph graph, Dictionary<string, ZoneWarpData> zoneWarps)
    {
        var events = new List<FogGateEvent>();

        foreach (var (source, target) in graph.AllEdges())
        {
            // Skip if we don't have warp data for these zones
            if (!zoneWarps.TryGetValue(source.ZoneId, out var sourceWarp) ||
                !zoneWarps.TryGetValue(target.ZoneId, out var targetWarp))
            {
                Console.WriteLine($"Warning: Missing warp data for {source.ZoneId} -> {target.ZoneId}");
                continue;
            }

            var fogEvent = CreateFogGate(source, target, sourceWarp, targetWarp);
            events.Add(fogEvent);
        }

        return events;
    }

    private FogGateEvent CreateFogGate(
        NodeData source,
        NodeData target,
        ZoneWarpData sourceWarp,
        ZoneWarpData targetWarp)
    {
        var eventId = _nextEventId++;
        var flagId = _nextFlagId++;

        return new FogGateEvent
        {
            EventId = eventId,
            FlagId = flagId,
            SourceNodeId = source.Id,
            TargetNodeId = target.Id,
            SourceMap = source.ZoneMap,
            TargetMap = target.ZoneMap,
            // Position where fog wall spawns (exit of source zone)
            FogPosition = sourceWarp.ExitPosition,
            // Position where player arrives (entrance of target zone)
            WarpDestination = targetWarp.EntrancePosition,
            // Scaling tier of target zone
            TargetTier = target.Tier,
        };
    }

    /// <summary>
    /// Generate EMEVD instructions for a fog gate.
    /// </summary>
    public void WriteToEmevd(EMEVD emevd, FogGateEvent fogGate, int scalingSpEffect)
    {
        // This is a simplified version - actual implementation needs
        // proper EMEVD instruction building based on FogRando's approach

        // Key instructions needed:
        // 1. Spawn fog wall asset at FogPosition
        // 2. Create region trigger around fog wall
        // 3. On player entering region:
        //    a. Set event flag (for tracking)
        //    b. Apply scaling SpEffect
        //    c. Warp player to WarpDestination
        //    d. Despawn fog wall

        // The actual EMEVD instruction format is complex and requires
        // understanding of the event scripting system.
        // See FogRando's EventConfig.cs for template-based approach.

        throw new NotImplementedException(
            "EMEVD generation requires adapting FogRando's EventConfig system. " +
            "See FogRando source for implementation details."
        );
    }
}

/// <summary>
/// Data for a single fog gate event.
/// </summary>
public class FogGateEvent
{
    public int EventId { get; set; }
    public int FlagId { get; set; }
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public string SourceMap { get; set; } = "";
    public string TargetMap { get; set; } = "";
    public Vector3 FogPosition { get; set; }
    public Vector3 WarpDestination { get; set; }
    public int TargetTier { get; set; }
}

/// <summary>
/// Warp position data for a zone.
/// </summary>
public class ZoneWarpData
{
    public string ZoneId { get; set; } = "";
    public string MapId { get; set; } = "";
    public Vector3 EntrancePosition { get; set; }
    public Vector3 ExitPosition { get; set; }
    public int EntranceRegion { get; set; }
    public int ExitRegion { get; set; }
}

/// <summary>
/// Simple 3D vector (matches System.Numerics.Vector3)
/// </summary>
public struct Vector3
{
    public float X, Y, Z;

    public Vector3(float x, float y, float z)
    {
        X = x; Y = y; Z = z;
    }
}
```

---

## Task 3.5: WarpWriter.cs

Handles the actual warp teleportation logic.

```csharp
using SoulsFormats;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates warp teleportation events.
/// Works in conjunction with FogGateWriter.
/// </summary>
public class WarpWriter
{
    /// <summary>
    /// Generate warp instructions for EMEVD.
    /// </summary>
    public void WriteWarpEvent(
        EMEVD emevd,
        FogGateEvent fogGate,
        int scalingSpEffect)
    {
        // Warp event structure (pseudocode):
        //
        // Event {eventId}:
        //   IF PlayerInRegion(fogGate.FogRegion)
        //   THEN
        //     SetEventFlag(fogGate.FlagId, ON)
        //     ApplySpEffect(Player, scalingSpEffect)  // Scaling
        //     WarpPlayer(fogGate.TargetMap, fogGate.WarpDestination)
        //     EndEvent()
        //
        // See FogRando's EMEVD templates for actual instruction format.

        throw new NotImplementedException(
            "Warp event generation requires EMEVD instruction building. " +
            "Adapt from FogRando's event templates."
        );
    }
}
```

---

## Task 3.6: StartingItemsWriter.cs

Gives key items to player at game start.

```csharp
using SoulsFormats;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Adds key items to player's starting inventory.
/// </summary>
public class StartingItemsWriter
{
    // Item IDs for key items (from SoulsIds or manual research)
    private static readonly Dictionary<string, int> KeyItems = new()
    {
        ["rusty_key"] = 8109,           // Rusty Key (Stormveil)
        ["stonesword_key"] = 8100,      // Stonesword Key
        ["academy_glintstone_key"] = 8110,  // Academy key
        ["discarded_palace_key"] = 8102,    // Raya Lucaria locked area
        ["carian_inverted_statue"] = 8115,  // Carian Study Hall
        ["haligtree_secret_medallion_l"] = 8186,
        ["haligtree_secret_medallion_r"] = 8187,
        // Add more as needed
    };

    private readonly ParamDictionary _params;

    public StartingItemsWriter(ParamDictionary gameParams)
    {
        _params = gameParams;
    }

    /// <summary>
    /// Add all key items to starting inventory.
    /// </summary>
    public void AddKeyItemsToStart()
    {
        // Elden Ring stores starting items in ItemLotParam_map
        // for the starting area, or can use event scripts.

        // Approach 1: Modify starting gift lot
        // Approach 2: Add event that gives items on game start

        // For SpeedFog, we'll use a simple approach:
        // Create an event that fires once at game start and gives all keys.

        throw new NotImplementedException(
            "Starting items can be added via ItemLotParam or event scripts. " +
            "Research the exact approach used by item randomizers."
        );
    }

    /// <summary>
    /// Add specific items to starting inventory.
    /// </summary>
    public void AddItems(IEnumerable<string> itemNames)
    {
        foreach (var name in itemNames)
        {
            if (KeyItems.TryGetValue(name, out var itemId))
            {
                AddStartingItem(itemId, quantity: 1);
            }
        }
    }

    /// <summary>
    /// Add Stonesword Keys (commonly needed).
    /// </summary>
    public void AddStoneswordKeys(int quantity = 10)
    {
        AddStartingItem(KeyItems["stonesword_key"], quantity);
    }

    private void AddStartingItem(int itemId, int quantity)
    {
        // Implementation depends on chosen approach
        throw new NotImplementedException();
    }
}
```

---

## Task 3.7: ModWriter.cs (Orchestrator)

Main class that coordinates all writers.

```csharp
using SoulsFormats;
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Main orchestrator for mod file generation.
/// </summary>
public class ModWriter
{
    private readonly string _gameDir;
    private readonly string _outputDir;
    private readonly SpeedFogGraph _graph;

    private ParamDictionary? _params;
    private Dictionary<string, EMEVD>? _emevds;
    private Dictionary<string, ZoneWarpData>? _zoneWarps;

    public ModWriter(string gameDir, string outputDir, SpeedFogGraph graph)
    {
        _gameDir = gameDir;
        _outputDir = outputDir;
        _graph = graph;
    }

    /// <summary>
    /// Generate all mod files.
    /// </summary>
    public void Generate()
    {
        Console.WriteLine("Loading game data...");
        LoadGameData();

        Console.WriteLine("Loading zone warp data...");
        LoadZoneWarps();

        Console.WriteLine("Generating scaling effects...");
        GenerateScaling();

        Console.WriteLine("Generating fog gates...");
        GenerateFogGates();

        Console.WriteLine("Adding starting items...");
        AddStartingItems();

        Console.WriteLine("Writing output files...");
        WriteOutput();

        Console.WriteLine("Done!");
    }

    private void LoadGameData()
    {
        // Load game params
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        _params = new ParamDictionary();
        // ... load params using SoulsFormats

        // Load EMEVD files
        _emevds = new Dictionary<string, EMEVD>();
        // ... load common.emevd and relevant map emevds
    }

    private void LoadZoneWarps()
    {
        // Load warp position data for each zone
        // This data needs to be extracted from FogRando's fog.txt
        // or created manually

        _zoneWarps = new Dictionary<string, ZoneWarpData>();

        // TODO: Load from zones_warps.json or similar
        // This requires mapping each zone to its entrance/exit positions
    }

    private void GenerateScaling()
    {
        if (_params == null) throw new InvalidOperationException("Params not loaded");

        var scalingWriter = new ScalingWriter(_params);
        scalingWriter.GenerateScalingEffects();

        // Store for use in fog gate generation
        // _scalingEffects = scalingWriter.TierTransitions;
    }

    private void GenerateFogGates()
    {
        if (_zoneWarps == null) throw new InvalidOperationException("Zone warps not loaded");

        var fogWriter = new FogGateWriter();
        var fogGates = fogWriter.CreateFogGates(_graph, _zoneWarps);

        // TODO: Write fog gates to EMEVD
    }

    private void AddStartingItems()
    {
        if (_params == null) throw new InvalidOperationException("Params not loaded");

        var itemWriter = new StartingItemsWriter(_params);
        itemWriter.AddStoneswordKeys(10);
        // Add other key items as needed
    }

    private void WriteOutput()
    {
        var modDir = Path.Combine(_outputDir, "mods", "speedfog");
        Directory.CreateDirectory(modDir);

        // Write params
        var paramDir = Path.Combine(modDir, "param", "gameparam");
        Directory.CreateDirectory(paramDir);
        // ... write modified params

        // Write EMEVDs
        var eventDir = Path.Combine(modDir, "event");
        Directory.CreateDirectory(eventDir);
        // ... write modified EMEVDs

        Console.WriteLine($"Output written to: {modDir}");
    }
}
```

---

## Task 3.8: Program.cs (CLI)

```csharp
using SpeedFogWriter.Models;
using SpeedFogWriter.Writers;

namespace SpeedFogWriter;

class Program
{
    static int Main(string[] args)
    {
        if (args.Length < 3)
        {
            Console.WriteLine("Usage: SpeedFogWriter <graph.json> <game_dir> <output_dir>");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  graph.json  - Path to generated graph from speedfog-core");
            Console.WriteLine("  game_dir    - Path to Elden Ring Game folder");
            Console.WriteLine("  output_dir  - Output directory for mod files");
            return 1;
        }

        var graphPath = args[0];
        var gameDir = args[1];
        var outputDir = args[2];

        // Validate paths
        if (!File.Exists(graphPath))
        {
            Console.Error.WriteLine($"Error: Graph file not found: {graphPath}");
            return 1;
        }

        if (!Directory.Exists(gameDir))
        {
            Console.Error.WriteLine($"Error: Game directory not found: {gameDir}");
            return 1;
        }

        try
        {
            // Load graph
            Console.WriteLine($"Loading graph: {graphPath}");
            var graph = SpeedFogGraph.Load(graphPath);
            Console.WriteLine($"  Seed: {graph.Seed}");
            Console.WriteLine($"  Nodes: {graph.TotalNodes}");
            Console.WriteLine($"  Paths: {graph.TotalPaths}");

            // Generate mod
            var writer = new ModWriter(gameDir, outputDir, graph);
            writer.Generate();

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: {ex.Message}");
            if (Environment.GetEnvironmentVariable("SPEEDFOG_DEBUG") != null)
            {
                Console.Error.WriteLine(ex.StackTrace);
            }
            return 1;
        }
    }
}
```

---

## Key Challenges & Notes

### 1. EMEVD Generation

The hardest part is generating valid EMEVD instructions. FogRando uses a template-based approach (`EventConfig.cs`) where event "shapes" are defined and filled in with specific values.

**Recommendation**:
- Study FogRando's `EventConfig.cs` and `fogevents.txt` carefully
- Start with a single hardcoded fog gate to verify the approach works
- Then generalize to template-based generation

### 2. Zone Warp Data

We need entrance/exit positions for every zone. This data exists in FogRando's YAML files but needs to be extracted and mapped to our zone IDs.

**Recommendation**:
- Create a `zone_warps.json` file that maps zone IDs to warp coordinates
- Extract this data from FogRando's `fog.txt` Entrances/Warps sections

### 3. SoulsFormats Compatibility

Ensure you're using a SoulsFormats version that supports Elden Ring. SoulsFormatsNEXT is recommended.

---

## Acceptance Criteria

### Task 3.1 (Setup)
- [ ] Project builds with `dotnet build`
- [ ] SoulsFormats loads correctly

### Task 3.2 (Models)
- [ ] `SpeedFogGraph.Load()` parses graph.json correctly
- [ ] All nodes and edges accessible

### Task 3.3 (Scaling)
- [ ] SpEffect entries created for tier transitions
- [ ] Scaling factors are reasonable (no 100x damage)

### Task 3.4-3.5 (Fog Gates & Warps)
- [ ] Fog gate events created for all edges
- [ ] Events compile without EMEVD errors

### Task 3.6 (Starting Items)
- [ ] Key items added to starting inventory
- [ ] Player doesn't get softlocked by missing keys

### Task 3.7-3.8 (Integration)
- [ ] Full pipeline works: graph.json → mod files
- [ ] Output directory structure matches ModEngine 2 expectations

---

## Testing

### Unit Tests

Test JSON parsing, scaling calculations, etc.

### Integration Test

```bash
# Generate graph (Phase 2)
cd speedfog/core
speedfog config.toml -o ../writer/test/graph.json

# Generate mod files (Phase 3)
cd ../writer
dotnet run -- test/graph.json "C:/Games/ELDEN RING/Game" ./output

# Verify output structure
ls -la output/mods/speedfog/
```

### In-Game Test

1. Copy output to ModEngine mod folder
2. Launch game with ModEngine
3. Start new game, verify:
   - Starting items present
   - First fog gate appears
   - Warp works
   - Enemy scaling feels appropriate

---

## Next Phase

After completing Phase 3, proceed to [Phase 4: Integration & Testing](./phase-4-integration.md).
