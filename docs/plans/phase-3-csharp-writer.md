# Phase 3: C# Writer - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 2: DAG Generation](./phase-2-dag-generation.md), [Cluster Generation](./generate-clusters-spec.md)
**Status**: Ready for implementation
**Last updated**: 2026-02-01 (review corrections: entry_fogs as list, zones_data.json, dynamic warp regions, MSB loading, asset creation, fog lookup fix)

## Objective

Create the C# component that reads `graph.json` and generates Elden Ring mod files using SoulsFormats. This involves adapting code from FogRando for fog gate creation, warp events, and enemy scaling.

## Key Architecture Note: Clusters, Not Zones

SpeedFog uses **clusters** as the atomic unit, not individual zones. A cluster is a group of zones connected by world connections. The C# writer must handle:

- Multiple zones per node (e.g., `["stormveil_start", "stormveil"]`)
- Fog IDs that reference specific fogs within cluster zones
- Edge connections via fog gates between clusters

See [generate-clusters-spec.md](./generate-clusters-spec.md) for cluster details.

## Prerequisites

- Phase 2 completed (working `graph.json` output)
- .NET 8.0 SDK
- Libraries from `reference/lib/`:
  - `SoulsFormats.dll` - FromSoft file format I/O
  - `SoulsIds.dll` - Helper library (GameEditor, ParamDictionary)
  - `YamlDotNet.dll`, `Newtonsoft.Json.dll`, `ZstdNet.dll`, `BouncyCastle.Cryptography.dll`
- **Data**: `fog_data.json` with fog gate metadata (see Task 3.2.1)
- **Data**: `zones_data.json` with zone→map mapping (see Task 3.2.2)

## Deliverables

```
speedfog/writer/
├── data/
│   ├── speedfog-events.yaml       # Event templates (readable, not hardcoded)
│   ├── fog_data.json              # Fog gate metadata extracted from fog.txt
│   └── zones_data.json            # Zone→map mapping extracted from zones.toml
│
├── SpeedFogWriter/
│   ├── SpeedFogWriter.csproj
│   ├── Program.cs                 # CLI entry point
│   │
│   ├── Models/
│   │   ├── SpeedFogGraph.cs       # JSON deserialization (graph.json)
│   │   ├── NodeData.cs            # Cluster-based node
│   │   ├── EdgeData.cs            # Edge between nodes
│   │   ├── FogEntryData.cs        # Fog gate metadata (fog_data.json)
│   │   ├── ZoneData.cs            # Zone→map mapping (zones_data.json)
│   │   └── EventTemplate.cs       # YAML event template model
│   │
│   ├── Writers/
│   │   ├── ModWriter.cs           # Main orchestrator
│   │   ├── EventBuilder.cs        # Builds EMEVD from templates
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
    <!-- Libraries from reference/lib/ (extracted from FogRando) -->
    <Reference Include="SoulsFormats">
      <HintPath>../../reference/lib/SoulsFormats.dll</HintPath>
    </Reference>
    <Reference Include="SoulsIds">
      <HintPath>../../reference/lib/SoulsIds.dll</HintPath>
    </Reference>
    <Reference Include="YamlDotNet">
      <HintPath>../../reference/lib/YamlDotNet.dll</HintPath>
    </Reference>
  </ItemGroup>

</Project>
```

### Directory Structure

```
speedfog/
├── reference/
│   └── lib/                  # DLLs extracted from FogRando
│       ├── SoulsFormats.dll  # FromSoft file format I/O
│       ├── SoulsIds.dll      # GameEditor, ParamDictionary helpers
│       ├── YamlDotNet.dll    # YAML parsing
│       ├── Newtonsoft.Json.dll
│       ├── ZstdNet.dll
│       └── BouncyCastle.Cryptography.dll
└── writer/
    └── SpeedFogWriter/
```

**Note**: DLLs already available in `reference/lib/` (extracted from FogRando). For updates, download from [soulsmods/SoulsFormatsNEXT](https://github.com/soulsmods/SoulsFormatsNEXT).

---

## Task 3.1.1: Loading Game Data (MSB + EMEVD + Params)

**Critical**: The C# writer must load **MSB files** in addition to EMEVDs and Params. MSBs contain:
- Fog gate asset positions (for lookup at runtime)
- The ability to create new SpawnPoint regions for warping
- The ability to create new fog wall assets (if needed)

### FogRando Reference (GameDataWriterE.cs:L37-70)

```csharp
// Load MSBs (map data)
CS$<>8__locals1.msbs = CS$<>8__locals1.<Write>g__loadDir|2<MSBE>(
    ..., "map\\mapstudio", (path) => SoulsFile<MSBE>.Read(path), "*.msb.dcx");

// Load EMEVDs (event scripts)
CS$<>8__locals1.emevds = CS$<>8__locals1.<Write>g__loadDir|2<EMEVD>(
    ..., "event", (path) => SoulsFile<EMEVD>.Read(path), "*.emevd.dcx");

// Load Params (game parameters)
CS$<>8__locals1.Params = new ParamDictionary {
    Defs = CS$<>8__locals1.editor.LoadDefs(),
    Inner = CS$<>8__locals1.editor.LoadParams(text5, null)
};
```

### SpeedFog Implementation

```csharp
/// <summary>
/// Loads all game data required for mod generation.
/// </summary>
public class GameDataLoader
{
    private readonly string _gameDir;
    private readonly GameEditor _editor;

    public Dictionary<string, MSBE> Msbs { get; private set; } = new();
    public Dictionary<string, EMEVD> Emevds { get; private set; } = new();
    public ParamDictionary? Params { get; private set; }

    public GameDataLoader(string gameDir)
    {
        _gameDir = gameDir;
        _editor = new GameEditor(GameSpec.FromGame.ER);
    }

    public void LoadAll()
    {
        LoadParams();
        LoadMsbs();
        LoadEmevds();
    }

    private void LoadParams()
    {
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        if (!File.Exists(regulationPath))
            throw new FileNotFoundException($"regulation.bin not found: {regulationPath}");

        Params = new ParamDictionary
        {
            Defs = _editor.LoadDefs(),
            Inner = _editor.LoadParams(regulationPath, null)
        };
        Console.WriteLine($"  Loaded params from {regulationPath}");
    }

    private void LoadMsbs()
    {
        var mapDir = Path.Combine(_gameDir, "map", "mapstudio");
        foreach (var file in Directory.GetFiles(mapDir, "*.msb.dcx"))
        {
            var mapName = Path.GetFileNameWithoutExtension(file).Replace(".msb", "");
            Msbs[mapName] = SoulsFile<MSBE>.Read(file);
        }
        Console.WriteLine($"  Loaded {Msbs.Count} MSB files");
    }

    private void LoadEmevds()
    {
        var eventDir = Path.Combine(_gameDir, "event");
        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var eventName = Path.GetFileNameWithoutExtension(file).Replace(".emevd", "");
            Emevds[eventName] = SoulsFile<EMEVD>.Read(file);
        }
        Console.WriteLine($"  Loaded {Emevds.Count} EMEVD files");
    }
}
```

### Which MSBs to Load

For SpeedFog, we only need MSBs for maps that contain:
1. Fog gates used in the generated graph
2. Target zones where players will warp to

Optimization: Instead of loading all ~200 MSBs, load only the maps referenced by the graph.

```csharp
public void LoadMsbsForGraph(SpeedFogGraph graph, FogDataFile fogData)
{
    var requiredMaps = new HashSet<string>();

    // Maps containing fog gates (from edges)
    foreach (var edge in graph.Edges)
    {
        var fog = fogData.GetFog(edge.FogId);
        if (fog != null)
            requiredMaps.Add(fog.Map);
    }

    // Maps for target zones (from nodes)
    foreach (var node in graph.AllNodes())
    {
        foreach (var zone in node.Zones)
        {
            // Lookup from zones_data.json
            var map = _zoneData.GetMap(zone);
            if (map != null)
                requiredMaps.Add(map);
        }
    }

    // Load only required MSBs
    var mapDir = Path.Combine(_gameDir, "map", "mapstudio");
    foreach (var mapName in requiredMaps)
    {
        var file = Path.Combine(mapDir, $"{mapName}.msb.dcx");
        if (File.Exists(file))
        {
            Msbs[mapName] = SoulsFile<MSBE>.Read(file);
        }
    }
    Console.WriteLine($"  Loaded {Msbs.Count} MSB files (of {requiredMaps.Count} required)");
}
```

---

## Task 3.2: JSON Models (Models/)

### graph.json Format (from Phase 2)

The Python core outputs `graph.json` in this format:

```json
{
  "seed": 123456789,
  "total_layers": 8,
  "total_nodes": 15,
  "total_zones": 20,
  "total_paths": 2,
  "path_weights": [42, 45],
  "nodes": {
    "start": {
      "cluster_id": "chapel_start_a1b2",
      "zones": ["chapel_start"],
      "type": "start",
      "weight": 2,
      "layer": 0,
      "tier": 1,
      "entry_fogs": [],
      "exit_fogs": ["1034432500", "1034432501"]
    },
    "node_1a": {
      "cluster_id": "stormveil_c3d4",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 20,
      "layer": 1,
      "tier": 5,
      "entry_fogs": ["1034432500"],
      "exit_fogs": ["1034432502", "1034432503"]
    },
    "node_merge": {
      "cluster_id": "boss_arena_x1y2",
      "zones": ["some_boss_arena"],
      "type": "boss_arena",
      "weight": 5,
      "layer": 3,
      "tier": 10,
      "entry_fogs": ["1034432502", "1034432510"],
      "exit_fogs": ["1034432520"]
    }
  },
  "edges": [
    {"source": "start", "target": "node_1a", "fog_id": "1034432500"},
    {"source": "start", "target": "node_1b", "fog_id": "1034432501"}
  ],
  "start_id": "start",
  "end_id": "end"
}
```

**Note**: `entry_fogs` is a **list** (not a single string). A node can have multiple entry fogs when it's a merge point in the DAG (multiple paths converge). The start node has an empty list.

### SpeedFogGraph.cs

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// Root structure of graph.json (matches Phase 2 Python output).
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

    [JsonPropertyName("path_weights")]
    public List<int> PathWeights { get; set; } = new();

    [JsonPropertyName("nodes")]
    public Dictionary<string, NodeData> Nodes { get; set; } = new();

    [JsonPropertyName("edges")]
    public List<EdgeData> Edges { get; set; } = new();

    [JsonPropertyName("start_id")]
    public string StartId { get; set; } = "";

    [JsonPropertyName("end_id")]
    public string EndId { get; set; } = "";

    /// <summary>
    /// Load graph from JSON file.
    /// </summary>
    public static SpeedFogGraph Load(string path)
    {
        var json = File.ReadAllText(path);
        var graph = JsonSerializer.Deserialize<SpeedFogGraph>(json)
            ?? throw new InvalidOperationException("Failed to parse graph.json");

        // Set node IDs from dictionary keys
        foreach (var (id, node) in graph.Nodes)
        {
            node.Id = id;
        }

        return graph;
    }

    /// <summary>
    /// Get all nodes in the graph.
    /// </summary>
    public IEnumerable<NodeData> AllNodes() => Nodes.Values;

    /// <summary>
    /// Get node by ID.
    /// </summary>
    public NodeData? GetNode(string id) => Nodes.GetValueOrDefault(id);

    /// <summary>
    /// Get start node.
    /// </summary>
    public NodeData? StartNode => GetNode(StartId);

    /// <summary>
    /// Get end node.
    /// </summary>
    public NodeData? EndNode => GetNode(EndId);

    /// <summary>
    /// Get all edges with resolved node references.
    /// </summary>
    public IEnumerable<(NodeData Source, NodeData Target, string FogId)> AllEdgesResolved()
    {
        foreach (var edge in Edges)
        {
            var source = GetNode(edge.Source);
            var target = GetNode(edge.Target);
            if (source != null && target != null)
            {
                yield return (source, target, edge.FogId);
            }
        }
    }

    /// <summary>
    /// Get outgoing edges from a node.
    /// </summary>
    public IEnumerable<EdgeData> GetOutgoingEdges(string nodeId)
    {
        return Edges.Where(e => e.Source == nodeId);
    }

    /// <summary>
    /// Get incoming edges to a node.
    /// </summary>
    public IEnumerable<EdgeData> GetIncomingEdges(string nodeId)
    {
        return Edges.Where(e => e.Target == nodeId);
    }

    /// <summary>
    /// Group nodes by layer for iteration.
    /// </summary>
    public Dictionary<int, List<NodeData>> NodesByLayer()
    {
        return Nodes.Values
            .GroupBy(n => n.Layer)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}
```

### NodeData.cs

```csharp
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// A node in the DAG representing a cluster (group of zones).
/// </summary>
public class NodeData
{
    /// <summary>
    /// Node ID (e.g., "start", "node_1a", "end").
    /// Set from dictionary key during loading.
    /// </summary>
    [JsonIgnore]
    public string Id { get; set; } = "";

    /// <summary>
    /// Cluster ID from clusters.json (e.g., "stormveil_c3d4").
    /// </summary>
    [JsonPropertyName("cluster_id")]
    public string ClusterId { get; set; } = "";

    /// <summary>
    /// List of zone IDs in this cluster.
    /// A cluster may contain multiple zones connected by world connections.
    /// </summary>
    [JsonPropertyName("zones")]
    public List<string> Zones { get; set; } = new();

    /// <summary>
    /// Cluster type: start, final_boss, legacy_dungeon, mini_dungeon, boss_arena.
    /// </summary>
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>
    /// Total weight of the cluster (sum of zone weights).
    /// </summary>
    [JsonPropertyName("weight")]
    public int Weight { get; set; }

    /// <summary>
    /// Layer index in the DAG (0 = start, N = end).
    /// </summary>
    [JsonPropertyName("layer")]
    public int Layer { get; set; }

    /// <summary>
    /// Difficulty tier for enemy scaling (1-28).
    /// </summary>
    [JsonPropertyName("tier")]
    public int Tier { get; set; }

    /// <summary>
    /// Fog IDs used to enter this cluster (empty list for start node).
    /// A node can have multiple entry fogs when it's a merge point (multiple paths converge).
    /// </summary>
    [JsonPropertyName("entry_fogs")]
    public List<string> EntryFogs { get; set; } = new();

    /// <summary>
    /// Available exit fog IDs from this cluster.
    /// These are fogs that can lead to the next layer.
    /// </summary>
    [JsonPropertyName("exit_fogs")]
    public List<string> ExitFogs { get; set; } = new();

    // Convenience properties
    public bool IsStart => Type == "start";
    public bool IsFinalBoss => Type == "final_boss";
    public bool IsLegacyDungeon => Type == "legacy_dungeon";
    public bool IsMiniDungeon => Type == "mini_dungeon";
    public bool IsBossArena => Type == "boss_arena";
    public bool IsMergePoint => EntryFogs.Count > 1;

    /// <summary>
    /// Get the primary zone (first zone in the cluster).
    /// </summary>
    public string PrimaryZone => Zones.FirstOrDefault() ?? "";

    /// <summary>
    /// Get the primary entry fog (first in the list, or null if none).
    /// </summary>
    public string? PrimaryEntryFog => EntryFogs.FirstOrDefault();
}
```

### EdgeData.cs

```csharp
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// A directed edge between two nodes in the DAG.
/// </summary>
public class EdgeData
{
    /// <summary>
    /// Source node ID.
    /// </summary>
    [JsonPropertyName("source")]
    public string Source { get; set; } = "";

    /// <summary>
    /// Target node ID.
    /// </summary>
    [JsonPropertyName("target")]
    public string Target { get; set; } = "";

    /// <summary>
    /// Fog gate ID used for this connection.
    /// This fog gate should be placed at the exit of the source cluster
    /// and warp to the entrance of the target cluster.
    /// </summary>
    [JsonPropertyName("fog_id")]
    public string FogId { get; set; } = "";
}
```

---

## Task 3.2.1: Fog Data (fog_data.json) - COMPLETE

The C# writer needs fog gate metadata from `fog.txt`. **Positions are NOT stored in fog.txt** for most fogs - they're in MSB (map) files. This task uses a **hybrid approach**:

- **Python** extracts metadata (type, zones, map, entity_id, model, lookup method)
- **C#** resolves positions at runtime from MSB files (which it already loads via SoulsFormats)
- **MakeFrom fogs** are special - they have inline positions which ARE extracted

### fog_data.json Format

```json
{
  "version": "1.0",
  "duplicate_names_handled": 304,
  "fogs": {
    "AEG099_002_9000": {
      "type": "entrance",
      "zones": ["stormveil", "stormveil_godrick"],
      "map": "m10_00_00_00",
      "entity_id": 10001800,
      "model": "AEG099_002",
      "lookup_by": "name",
      "position": null,
      "rotation": null
    },
    "m10_00_00_00_AEG099_002_9000": {
      "type": "entrance",
      "zones": ["stormveil", "stormveil_godrick"],
      "map": "m10_00_00_00",
      "entity_id": 10001800,
      "model": "AEG099_002",
      "lookup_by": "name",
      "position": null,
      "rotation": null
    },
    "755894520": {
      "type": "makefrom",
      "zones": ["peninsula_tombswardcatacombs"],
      "map": "m30_00_00_00",
      "entity_id": 755894520,
      "model": "AEG099_170",
      "lookup_by": null,
      "position": [-63.656, 51.250, 68.100],
      "rotation": [0, -90.0, 0]
    }
  }
}
```

### Key Fields

| Field | Description |
|-------|-------------|
| `type` | "entrance", "warp", or "makefrom" |
| `zones` | List of zones this fog connects (both ASide and BSide) |
| `map` | Map ID where fog is defined (e.g., "m10_00_00_00") |
| `entity_id` | Entity ID for MSB lookup |
| `model` | Fog model name (e.g., "AEG099_002") |
| `asset_name` | **Full asset name in MSB** (e.g., "AEG099_002_9000") - used for name-based lookup |
| `lookup_by` | "name" (AEG fogs) or "entity_id" (numeric fogs), null for makefrom |
| `position` | `[x, y, z]` for makefrom fogs, `null` otherwise |
| `rotation` | `[rx, ry, rz]` for makefrom fogs, `null` otherwise |

**Important**: `model` is the model name shared by many assets. `asset_name` is the unique instance name in the MSB file.

### Duplicate Fog Names

Fog names like `AEG099_002_9000` appear in multiple maps. The script handles this by:
1. First occurrence uses plain name as key: `"AEG099_002_9000"`
2. All occurrences also have map-prefixed key: `"m10_00_00_00_AEG099_002_9000"`

At runtime, the C# writer should:
1. Try plain fog_id first, check if zone matches
2. If zone doesn't match, iterate map-prefixed keys to find matching zone

### FogEntryData.cs

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// Fog metadata loaded from fog_data.json.
/// </summary>
public class FogDataFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("fogs")]
    public Dictionary<string, FogEntryData> Fogs { get; set; } = new();

    public static FogDataFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<FogDataFile>(json)
            ?? throw new InvalidOperationException("Failed to parse fog_data.json");
    }

    /// <summary>
    /// Get fog entry by fog_id and zone.
    /// Tries plain key first, then map-prefixed keys.
    /// </summary>
    public FogEntryData? GetFog(string fogId, string? zone = null)
    {
        // Try plain key first
        if (Fogs.TryGetValue(fogId, out var fog))
        {
            if (zone == null || fog.Zones.Contains(zone))
                return fog;
        }

        // Try all map-prefixed keys
        foreach (var (key, data) in Fogs)
        {
            if (key.EndsWith($"_{fogId}") && (zone == null || data.Zones.Contains(zone)))
                return data;
        }

        return null;
    }
}

/// <summary>
/// Metadata for a single fog gate.
/// </summary>
public class FogEntryData
{
    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    /// <summary>
    /// List of zones this fog connects.
    /// </summary>
    [JsonPropertyName("zones")]
    public List<string> Zones { get; set; } = new();

    /// <summary>
    /// Map ID (e.g., "m10_00_00_00").
    /// </summary>
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    /// <summary>
    /// Entity ID for MSB lookup.
    /// </summary>
    [JsonPropertyName("entity_id")]
    public int EntityId { get; set; }

    /// <summary>
    /// Model name (e.g., "AEG099_231").
    /// </summary>
    [JsonPropertyName("model")]
    public string Model { get; set; } = "";

    /// <summary>
    /// Full asset name in MSB (e.g., "AEG099_231_9000").
    /// This is the unique instance name used for name-based lookup.
    /// </summary>
    [JsonPropertyName("asset_name")]
    public string AssetName { get; set; } = "";

    /// <summary>
    /// How to look up in MSB: "name" or "entity_id", null for makefrom.
    /// </summary>
    [JsonPropertyName("lookup_by")]
    public string? LookupBy { get; set; }

    /// <summary>
    /// Position [x, y, z] - only for makefrom fogs.
    /// </summary>
    [JsonPropertyName("position")]
    public float[]? Position { get; set; }

    /// <summary>
    /// Rotation [x, y, z] in degrees - only for makefrom fogs.
    /// </summary>
    [JsonPropertyName("rotation")]
    public float[]? Rotation { get; set; }

    // Convenience properties
    public bool HasPosition => Position != null && Position.Length == 3;
    public bool IsMakeFrom => Type == "makefrom";

    public System.Numerics.Vector3 PositionVec =>
        Position != null ? new(Position[0], Position[1], Position[2]) : default;

    public System.Numerics.Vector3 RotationVec =>
        Rotation != null ? new(Rotation[0], Rotation[1], Rotation[2]) : default;

    /// <summary>
    /// Parse map ID to bytes (for EMEVD warp instructions).
    /// Example: "m10_01_00_00" -> [10, 1, 0, 0]
    /// </summary>
    public byte[] MapBytes
    {
        get
        {
            var parts = Map.TrimStart('m').Split('_');
            if (parts.Length != 4)
                throw new FormatException($"Invalid map ID: {Map}");

            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3])
            };
        }
    }
}
```

### Resolving Positions at Runtime (C#)

For non-makefrom fogs, the C# writer must resolve positions from MSB files:

```csharp
public Vector3 GetFogPosition(FogEntryData fog, Dictionary<string, MSBE> msbs)
{
    // MakeFrom fogs have inline positions
    if (fog.IsMakeFrom && fog.HasPosition)
        return fog.PositionVec;

    // Ensure MSB is loaded
    if (!msbs.TryGetValue(fog.Map, out var msb))
        throw new Exception($"MSB not loaded for map {fog.Map}");

    // Look up in MSB by asset_name or entity_id
    MSBE.Part.Asset? asset = fog.LookupBy switch
    {
        // IMPORTANT: Use AssetName (e.g., "AEG099_002_9000"), NOT Model (e.g., "AEG099_002")
        "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.AssetName),
        "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == (uint)fog.EntityId),
        _ => null
    };

    if (asset != null)
        return asset.Position;

    // Fallback: try partial match on asset name
    asset = msb.Parts.Assets.FirstOrDefault(a => a.Name.Contains(fog.AssetName));
    if (asset != null)
    {
        Console.WriteLine($"Warning: Used partial match for {fog.AssetName} in {fog.Map}");
        return asset.Position;
    }

    throw new Exception($"Could not find fog asset {fog.AssetName} (entity_id={fog.EntityId}) in {fog.Map}");
}

public (Vector3 Position, Vector3 Rotation) GetFogPositionAndRotation(FogEntryData fog, Dictionary<string, MSBE> msbs)
{
    if (fog.IsMakeFrom && fog.HasPosition)
        return (fog.PositionVec, fog.RotationVec);

    if (!msbs.TryGetValue(fog.Map, out var msb))
        throw new Exception($"MSB not loaded for map {fog.Map}");

    MSBE.Part.Asset? asset = fog.LookupBy switch
    {
        "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.AssetName),
        "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == (uint)fog.EntityId),
        _ => null
    };

    if (asset != null)
        return (asset.Position, asset.Rotation);

    throw new Exception($"Could not find fog asset {fog.AssetName} in {fog.Map}");
}
```

### Extraction Script - NEEDS UPDATE

The Python script `tools/extract_fog_data.py` exists but needs to be updated to include `asset_name`:

```bash
# Generate fog_data.json
python tools/extract_fog_data.py \
    reference/fogrando-data/fog.txt \
    writer/data/fog_data.json \
    --validate-clusters core/data/clusters.json

# Output:
# Parsed 547 fog entries
#   entrance: 351
#   makefrom: 36
#   warp: 160
# All cluster fog_ids found in fog_data!
```

**Required Update**: The script must include `asset_name` field for each fog entry:
- For `entrance` type fogs: `asset_name` is the `Name` field from fog.txt (e.g., "AEG099_002_9000")
- For `makefrom` type fogs: `asset_name` should be generated or derived from the ID
- For `warp` type fogs: similar to entrance

**Example update to extract_fog_data.py**:
```python
# In the extraction loop, add:
fog_entry["asset_name"] = entry.get("Name", "")  # Full asset name in MSB

# The current output only has "model" (e.g., "AEG099_002")
# We need "asset_name" (e.g., "AEG099_002_9000") for MSB lookup
```

**Note**: This is a one-time extraction task. The metadata doesn't change between runs.

---

## Task 3.2.2: Zone Data (zones_data.json) - NEW

The C# writer needs zone→map mapping to determine where to teleport players. This data is extracted from `zones.toml`.

### zones_data.json Format

```json
{
  "version": "1.0",
  "zones": {
    "chapel_start": {
      "map": "m10_01_00_00",
      "name": "Chapel of Anticipation"
    },
    "stormveil": {
      "map": "m10_00_00_00",
      "name": "Stormveil Castle after Gate"
    },
    "limgrave": {
      "map": "m60_42_36_00",
      "name": "Limgrave"
    }
  }
}
```

**Note**: Some zones span multiple maps (e.g., `limgrave` covers many overworld tiles). For these, we use the **primary map** (first in the list from fog.txt). The exact spawn position is determined by the entry fog's location in that map.

### ZoneData.cs

```csharp
using System.Text.Json;
using System.Text.Json.Serialization;

namespace SpeedFogWriter.Models;

/// <summary>
/// Zone metadata loaded from zones_data.json.
/// </summary>
public class ZoneDataFile
{
    [JsonPropertyName("version")]
    public string Version { get; set; } = "1.0";

    [JsonPropertyName("zones")]
    public Dictionary<string, ZoneEntry> Zones { get; set; } = new();

    public static ZoneDataFile Load(string path)
    {
        var json = File.ReadAllText(path);
        return JsonSerializer.Deserialize<ZoneDataFile>(json)
            ?? throw new InvalidOperationException("Failed to parse zones_data.json");
    }

    /// <summary>
    /// Get the map ID for a zone.
    /// </summary>
    public string? GetMap(string zoneId)
    {
        return Zones.TryGetValue(zoneId, out var zone) ? zone.Map : null;
    }

    /// <summary>
    /// Get map for a cluster (uses primary zone).
    /// </summary>
    public string? GetMapForCluster(List<string> zones)
    {
        foreach (var zone in zones)
        {
            var map = GetMap(zone);
            if (map != null) return map;
        }
        return null;
    }
}

/// <summary>
/// Metadata for a single zone.
/// </summary>
public class ZoneEntry
{
    [JsonPropertyName("map")]
    public string Map { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    /// <summary>
    /// Parse map ID to bytes (for EMEVD warp instructions).
    /// Example: "m10_01_00_00" -> [10, 1, 0, 0]
    /// </summary>
    public byte[] MapBytes
    {
        get
        {
            var parts = Map.TrimStart('m').Split('_');
            if (parts.Length != 4)
                throw new FormatException($"Invalid map ID: {Map}");

            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3])
            };
        }
    }
}
```

### Extraction Script

Create `tools/extract_zones_data.py`:

```python
#!/usr/bin/env python3
"""
Extract zone→map mapping from zones.toml for the C# writer.
Outputs zones_data.json with minimal zone metadata.
"""

import argparse
import json
import sys
from pathlib import Path

try:
    import tomllib  # Python 3.11+
except ImportError:
    import tomli as tomllib  # pip install tomli


def extract_zones(zones_toml_path: Path) -> dict:
    """Extract zone data from zones.toml."""
    with open(zones_toml_path, "rb") as f:
        data = tomllib.load(f)

    zones = {}
    for zone in data.get("zones", []):
        zone_id = zone.get("id")
        if not zone_id:
            continue

        zones[zone_id] = {
            "map": zone.get("map", ""),
            "name": zone.get("name", zone_id),
        }

    return zones


def validate_zones(zones: dict, clusters_path: Path | None) -> list[str]:
    """Validate that all zones in clusters.json are present."""
    if clusters_path is None or not clusters_path.exists():
        return []

    with open(clusters_path) as f:
        clusters = json.load(f)

    missing = []
    for cluster in clusters.get("clusters", []):
        for zone_id in cluster.get("zones", []):
            if zone_id not in zones:
                missing.append(zone_id)

    return missing


def main():
    parser = argparse.ArgumentParser(
        description="Extract zone→map mapping from zones.toml"
    )
    parser.add_argument("zones_toml", type=Path, help="Path to zones.toml")
    parser.add_argument("output_json", type=Path, help="Output path for zones_data.json")
    parser.add_argument(
        "--validate-clusters",
        type=Path,
        help="Path to clusters.json for validation",
    )
    args = parser.parse_args()

    if not args.zones_toml.exists():
        print(f"Error: {args.zones_toml} not found", file=sys.stderr)
        return 1

    # Extract zones
    zones = extract_zones(args.zones_toml)
    print(f"Parsed {len(zones)} zones")

    # Validate against clusters if provided
    if args.validate_clusters:
        missing = validate_zones(zones, args.validate_clusters)
        if missing:
            print(f"Warning: {len(missing)} zones missing from zones.toml:")
            for z in missing[:10]:
                print(f"  - {z}")
            if len(missing) > 10:
                print(f"  ... and {len(missing) - 10} more")

    # Write output
    output = {
        "version": "1.0",
        "zones": zones,
    }

    args.output_json.parent.mkdir(parents=True, exist_ok=True)
    with open(args.output_json, "w") as f:
        json.dump(output, f, indent=2)

    print(f"Written {args.output_json}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
```

**Usage:**

```bash
# Generate zones_data.json from zones.toml
python tools/extract_zones_data.py \
    core/zones.toml \
    writer/data/zones_data.json \
    --validate-clusters core/data/clusters.json

# Expected output:
# Parsed 150 zones
# Written writer/data/zones_data.json
```

**Note**: This script requires `tomli` for Python < 3.11:
```bash
pip install tomli
```

---

## Task 3.3: Event Templates (speedfog-events.yaml)

Event scripting is defined in a YAML file rather than hardcoded in C#, for readability and maintainability.

### Why YAML instead of hardcoded C#?

| Aspect | Hardcoded C# | YAML File |
|--------|--------------|-----------|
| Readability | Mixed with code logic | Clear, self-documenting |
| Modification | Requires recompilation | Edit and reload |
| Debugging | Difficult to inspect | Easy to trace |
| Extensibility | Rigid | Add new templates easily |

### speedfog-events.yaml

```yaml
# SpeedFog Event Templates
# Adapted from FogRando's fogevents.txt
#
# Parameter notation: X{offset}_{size}
#   - offset: byte offset in the event parameter block
#   - size: parameter size in bytes (1 or 4)
#   - Example: X0_4 = 4-byte param at offset 0, X12_1 = 1-byte param at offset 12
#
# FogRando Reference: reference/fogrando-data/fogevents.txt

templates:
  # Apply scaling to an enemy when loaded
  # Source: FogRando ID 9005770
  scale:
    id: 79000001
    restart: true
    params:
      entity_id: X0_4
      speffect: X4_4
    commands:
      - IfCharacterBackreadStatus(MAIN, X0_4, true, ComparisonType.Equal, 1)
      - SetSpEffect(X0_4, X4_4)
      - IfCharacterBackreadStatus(MAIN, X0_4, false, ComparisonType.Equal, 1)
      - WaitFixedTimeSeconds(1)
      - EndUnconditionally(EventEndType.Restart)

  # Show fog gate visual effect
  # Source: FogRando ID 9005775
  showsfx:
    id: 79000002
    params:
      fog_gate: X0_4
      sfx_id: X4_4
    commands:
      - ChangeAssetEnableState(X0_4, Enabled)
      - CreateAssetfollowingSFX(X0_4, 101, X4_4)

  # Warp player through fog gate (FULL VERSION with boss flags)
  # Source: FogRando ID 9005777
  # SpeedFog v1: Simplified - no boss defeat checks (all paths are traversable)
  fogwarp:
    id: 79000003
    restart: true
    params:
      fog_gate: X0_4       # Entity ID of the fog gate asset
      button_param: X4_4   # ActionButton param ID (63000 = "Traverse the mist")
      warp_region: X8_4    # SpawnPoint region to warp to
      map_m: X12_1         # Map bytes [m, area, block, sub]
      map_area: X13_1
      map_block: X14_1
      map_sub: X15_1
      rotate_target: X16_4 # Entity to face after warp (usually warp_region)
    commands:
      # Wait for player to interact with fog gate
      - IfActionButtonInArea(AND_05, X4_4, X0_4)
      - IfConditionGroup(MAIN, PASS, AND_05)
      # Check player has "trapped" speffect (can traverse fog)
      - IfCharacterHasSpEffect(AND_06, 10000, 4280, false, ComparisonType.Equal, 1)
      - GotoIfConditionGroupStateUncompiled(Label.Label10, PASS, AND_06)
      # If not trapped, show dialog and restart
      - DisplayGenericDialog(90010, PromptType.OKCANCEL, NumberofOptions.OneButton, X0_4, 3)
      - WaitFixedTimeSeconds(1)
      - EndUnconditionally(EventEndType.Restart)
      # Warping sequence
      - Label10()
      - RotateCharacter(10000, X16_4, 60060, false)
      - WaitFixedTimeSeconds(1)
      - ShowTextOnLoadingScreen(Disabled)
      - WarpPlayer(X12_1, X13_1, X14_1, X15_1, X8_4, 0)
      - WaitFixedTimeFrames(1)
      - EndUnconditionally(EventEndType.Restart)

  # Simplified fog warp for SpeedFog (no trap check, always allowed)
  # Use this for v1 implementation
  fogwarp_simple:
    id: 79000010
    restart: true
    params:
      fog_gate: X0_4
      button_param: X4_4
      warp_region: X8_4
      map_m: X12_1
      map_area: X13_1
      map_block: X14_1
      map_sub: X15_1
      rotate_target: X16_4
    commands:
      - IfActionButtonInArea(AND_01, X4_4, X0_4)
      - IfConditionGroup(MAIN, PASS, AND_01)
      - RotateCharacter(10000, X16_4, 60060, false)
      - WaitFixedTimeSeconds(0.5)
      - ShowTextOnLoadingScreen(Disabled)
      - WarpPlayer(X12_1, X13_1, X14_1, X15_1, X8_4, 0)
      - WaitFixedTimeFrames(1)
      - EndUnconditionally(EventEndType.Restart)

  # Start boss fight when player enters region (if boss not defeated)
  # Source: FogRando ID 9005776
  startboss:
    id: 79000004
    restart: true
    params:
      defeat_flag: X0_4    # Boss defeat event flag
      trigger_region: X4_4 # Region that triggers fight start
      start_flag: X8_4     # Flag to set when fight starts
    commands:
      - EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, X0_4)
      - IfInoutsideArea(MAIN, InsideOutsideState.Inside, 10000, X4_4, 1)
      - WaitFixedTimeFrames(1)
      - SetEventFlag(TargetEventFlagType.EventFlag, X8_4, ON)

  # Disable fog gate (for multiplayer gates we want hidden)
  # Source: FogRando ID 9005778
  disable:
    id: 79000005
    params:
      fog_gate: X0_4
    commands:
      - ChangeAssetEnableState(X0_4, OFF)

  # Give items directly to player (for starting items)
  # SpeedFog-specific event
  give_items:
    id: 79000006
    params:
      check_flag: X0_4     # Flag to check (give once only)
      item_type: X4_4      # ItemType enum (3 = Goods)
      item_id: X8_4        # Item ID
      quantity: X12_4      # Amount to give
    commands:
      - EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, X0_4)
      - DirectlyGivePlayerItem(X4_4, X8_4, 6001, X12_4)
      - SetEventFlag(TargetEventFlagType.EventFlag, X0_4, ON)

  # Initialize common event (called from common.emevd event 0)
  # Used to run persistent events
  common_init:
    id: 79000007
    params:
      event_id: X0_4       # Event ID to initialize
      slot: X4_4           # Event slot (usually 0)
    commands:
      # This template is used to generate initialization calls
      # Actual instruction: InitializeEvent(0, event_id, slot, ...)
      - InitializeEvent(0, X0_4, X4_4, 0)

defaults:
  button_param: 63000      # "Traverse the mist" (action button)
  fog_sfx: 8011            # Standard fog visual effect SFX ID
  trigger_flag: 79900000   # SpeedFog game start flag
  player_entity: 10000     # Player character entity ID

# Event ID allocation plan:
# 79000001-79000099: Template event definitions (in common_func)
# 79000100-79099999: Per-fog-gate event instances
# 79100000-79199999: SpawnPoint region IDs
# 79200000-79299999: Event flags
# 79900000-79999999: Reserved for special flags
```

### EventTemplate.cs (Model)

```csharp
using YamlDotNet.Serialization;

namespace SpeedFogWriter.Models;

public class EventTemplate
{
    public int Id { get; set; }
    public bool Restart { get; set; }
    public Dictionary<string, string> Params { get; set; } = new();
    public List<string> Commands { get; set; } = new();
}

public class SpeedFogEventConfig
{
    public Dictionary<string, EventTemplate> Templates { get; set; } = new();
    public Dictionary<string, object> Defaults { get; set; } = new();

    public static SpeedFogEventConfig Load(string path)
    {
        var yaml = File.ReadAllText(path);
        var deserializer = new DeserializerBuilder().Build();
        return deserializer.Deserialize<SpeedFogEventConfig>(yaml);
    }
}
```

### EventBuilder.cs

Converts YAML templates to EMEVD instructions using SoulsIds' `Events.ParseAddArg()`.

```csharp
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Builds EMEVD events from YAML templates.
/// Uses SoulsIds Events class for instruction parsing.
/// </summary>
public class EventBuilder
{
    private readonly SpeedFogEventConfig _config;
    private readonly Events _events;

    public EventBuilder(SpeedFogEventConfig config, Events events)
    {
        _config = config;
        _events = events;
    }

    /// <summary>
    /// Create an EMEVD event from a template with substituted parameters.
    /// </summary>
    public EMEVD.Event BuildEvent(string templateName, int eventId, Dictionary<string, object> args)
    {
        if (!_config.Templates.TryGetValue(templateName, out var template))
            throw new ArgumentException($"Unknown template: {templateName}");

        var restartType = template.Restart
            ? EMEVD.Event.RestBehaviorType.Restart
            : EMEVD.Event.RestBehaviorType.Default;

        var evt = new EMEVD.Event(eventId, restartType);

        foreach (var commandStr in template.Commands)
        {
            // Substitute $param placeholders with actual values
            var resolved = ResolveCommand(commandStr, template.Params, args);

            // Parse using SoulsIds
            var (instruction, parameters) = _events.ParseAddArg(resolved, evt.Instructions.Count);
            evt.Instructions.Add(instruction);
            evt.Parameters.AddRange(parameters);
        }

        return evt;
    }

    private string ResolveCommand(string command, Dictionary<string, string> paramDefs, Dictionary<string, object> args)
    {
        var result = command;
        foreach (var (paramName, paramPos) in paramDefs)
        {
            if (args.TryGetValue(paramName, out var value))
            {
                result = result.Replace($"${paramName}", value.ToString());
            }
            else
            {
                // Keep X0_4 style for EMEVD parameter substitution
                result = result.Replace($"${paramName}", paramPos);
            }
        }
        return result;
    }
}
```

---

## Task 3.4: ScalingWriter.cs

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

## Task 3.5: FogGateWriter.cs

Creates fog wall events using EMEVD. Adapted from FogRando's event creation logic.

### Key Concepts

- **EMEVD**: Event script format used by FromSoft games
- **Fog Wall**: Visual barrier that triggers warp on contact
- **Event Flag**: Persistent game state (used to track fog traversal)
- **Cluster**: A node contains multiple zones; fogs are identified by fog_id

### Architecture Note

The DAG uses edges with `fog_id` to connect clusters. Each edge represents:
1. **Source**: A cluster (may contain multiple zones)
2. **Target**: Another cluster
3. **fog_id**: The specific fog gate used for this connection

**Important**: The fog_id identifies which fog gate VISUAL to use, but the DESTINATION is determined by the target cluster, not the fog's original connection. This is how fog randomization works.

The `fog_id` is looked up in `fog_data.json` to get:
- Model name and entity_id for the fog wall asset
- Source map ID where the fog is located
- Position lookup method (via MSB at runtime)

The **destination** is determined by:
- Target cluster's zones → `zones_data.json` → destination map
- SpawnPoint region created dynamically near entry fog position

### FogGateWriter.cs

```csharp
using SoulsFormats;
using SpeedFogWriter.Models;
using System.Numerics;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates fog gate events between clusters.
/// Adapted from FogRando's GameDataWriterE.cs
/// </summary>
public class FogGateWriter
{
    // Custom event ID range
    private const int CustomEventBase = 79000000;

    // Custom flag ID range
    private const int CustomFlagBase = 79000000;

    // Custom region ID range (for dynamically created spawn points)
    private const int CustomRegionBase = 79000000;

    private readonly FogDataFile _fogData;
    private readonly ZoneDataFile _zoneData;
    private int _nextEventId;
    private int _nextFlagId;
    private int _nextRegionId;

    public FogGateWriter(FogDataFile fogData, ZoneDataFile zoneData)
    {
        _fogData = fogData;
        _zoneData = zoneData;
        _nextEventId = CustomEventBase;
        _nextFlagId = CustomFlagBase;
        _nextRegionId = CustomRegionBase;
    }

    /// <summary>
    /// Create fog gate events for all edges in the graph.
    /// </summary>
    public List<FogGateEvent> CreateFogGates(SpeedFogGraph graph)
    {
        var events = new List<FogGateEvent>();

        foreach (var edge in graph.Edges)
        {
            var source = graph.GetNode(edge.Source);
            var target = graph.GetNode(edge.Target);

            if (source == null || target == null)
            {
                Console.WriteLine($"Warning: Invalid edge {edge.Source} -> {edge.Target}");
                continue;
            }

            // Look up fog metadata for the edge's fog gate
            var exitFogData = _fogData.GetFog(edge.FogId);
            if (exitFogData == null)
            {
                Console.WriteLine($"Warning: Missing fog data for {edge.FogId}");
                continue;
            }

            // Determine target map from zones_data
            var targetMap = _zoneData.GetMapForCluster(target.Zones);
            if (targetMap == null)
            {
                Console.WriteLine($"Warning: Cannot determine map for target cluster {target.ClusterId}");
                continue;
            }

            // Get entry fog for spawn position (use first entry fog if available)
            var primaryEntryFog = target.PrimaryEntryFog;
            var entryFogData = primaryEntryFog != null ? _fogData.GetFog(primaryEntryFog) : null;

            var fogEvent = CreateFogGate(source, target, edge, exitFogData, targetMap, entryFogData);
            events.Add(fogEvent);
        }

        return events;
    }

    private FogGateEvent CreateFogGate(
        NodeData source,
        NodeData target,
        EdgeData edge,
        FogEntryData exitFog,
        string targetMap,
        FogEntryData? entryFog)
    {
        var eventId = _nextEventId++;
        var flagId = _nextFlagId++;
        var warpRegionId = _nextRegionId++;  // Dynamic region to be created

        return new FogGateEvent
        {
            EventId = eventId,
            FlagId = flagId,
            EdgeFogId = edge.FogId,
            SourceNodeId = source.Id,
            TargetNodeId = target.Id,
            SourceClusterId = source.ClusterId,
            TargetClusterId = target.ClusterId,
            TargetZones = target.Zones,

            // Fog gate source (where the fog wall is displayed)
            SourceMap = exitFog.Map,
            FogEntityId = exitFog.EntityId,
            FogModel = exitFog.Model,
            FogLookupBy = exitFog.LookupBy,

            // Warp destination (dynamically determined)
            TargetMap = targetMap,
            WarpRegionId = warpRegionId,  // Will be created as SpawnPoint in target map
            EntryFogData = entryFog,      // Used to determine spawn position

            // Scaling
            SourceTier = source.Tier,
            TargetTier = target.Tier,
        };
    }

    /// <summary>
    /// Generate EMEVD instructions for a fog gate.
    /// </summary>
    public void WriteToEmevd(EMEVD emevd, FogGateEvent fogGate, int scalingSpEffect)
    {
        // Key instructions needed:
        // 1. Spawn fog wall asset at FogEntry (if not already present)
        // 2. Create button interaction region
        // 3. On player pressing button near fog:
        //    a. Set event flag (for tracking)
        //    b. Play warp animation
        //    c. WarpPlayer to TargetMap at WarpRegion
        //    d. Apply scaling SpEffect to enemies in new area

        // The actual EMEVD instruction format is complex and requires
        // understanding of the event scripting system.
        // See speedfog-events.yaml for template-based approach.

        throw new NotImplementedException(
            "EMEVD generation requires adapting FogRando's EventConfig system. " +
            "See FogRando source for implementation details."
        );
    }
}

/// <summary>
/// Data for a single fog gate event.
/// Contains all information needed to create the fog wall and warp event.
/// </summary>
public class FogGateEvent
{
    // Identification
    public int EventId { get; set; }
    public int FlagId { get; set; }
    public string EdgeFogId { get; set; } = "";  // fog_id from edge
    public string SourceNodeId { get; set; } = "";
    public string TargetNodeId { get; set; } = "";
    public string SourceClusterId { get; set; } = "";
    public string TargetClusterId { get; set; } = "";
    public List<string> TargetZones { get; set; } = new();

    // Fog gate source (where the fog wall is displayed)
    public string SourceMap { get; set; } = "";
    public int FogEntityId { get; set; }
    public string FogModel { get; set; } = "";
    public string? FogLookupBy { get; set; }  // "name" or "entity_id"

    // Warp destination
    public string TargetMap { get; set; } = "";
    public int WarpRegionId { get; set; }  // Dynamically created SpawnPoint region
    public FogEntryData? EntryFogData { get; set; }  // For position lookup

    // Scaling
    public int SourceTier { get; set; }
    public int TargetTier { get; set; }

    /// <summary>
    /// Parse target map to bytes for EMEVD warp instruction.
    /// Example: "m10_00_00_00" -> [10, 0, 0, 0]
    /// </summary>
    public byte[] TargetMapBytes
    {
        get
        {
            var parts = TargetMap.TrimStart('m').Split('_');
            if (parts.Length != 4)
                throw new FormatException($"Invalid map ID: {TargetMap}");

            return new byte[]
            {
                byte.Parse(parts[0]),
                byte.Parse(parts[1]),
                byte.Parse(parts[2]),
                byte.Parse(parts[3])
            };
        }
    }
}
```

---

## Task 3.5.1: Creating Fog Wall Assets (FogAssetHelper)

**Critical Implementation Detail**: Creating new fog wall assets in MSB files.

### FogRando Approach

FogRando uses a closure function `addFakeGate` that:
1. Deep-copies an existing asset as a template (`overworldAssetBase`)
2. Sets the model name and position
3. Adds the model to the MSB if not present
4. Generates a unique asset name
5. Configures SFX parameters

**Key reference** (GameDataWriterE.cs:L154, L262, L2674):
```csharp
// L154: Base asset for cloning (existing asset in the game)
CS$<>8__locals1.overworldAssetBase = CS$<>8__locals1.msbs["m60_46_38_00"]
    .Parts.Assets.Find(e => e.Name == "AEG007_310_2000");

// L2674: Clone and configure
asset16 = (MSBE.Part.Asset)CS$<>8__locals1.overworldAssetBase.DeepCopy();
asset16.ModelName = "AEG099_065";
GameDataWriterE.<Write>g__addAssetModel|1_21(msb, asset16.ModelName);
GameDataWriterE.<Write>g__setAssetName|1_24(asset16, newPartName);
asset16.Position = position;
asset16.Rotation = rotation;
asset16.EntityID = entityId;
msb.Parts.Assets.Add(asset16);
```

### SpeedFog Implementation

```csharp
using SoulsFormats;
using System.Numerics;

namespace SpeedFogWriter.Helpers;

/// <summary>
/// Helper for creating fog wall assets in MSB files.
/// Adapted from FogRando's addFakeGate closure.
/// </summary>
public class FogAssetHelper
{
    private readonly Dictionary<string, MSBE> _msbs;
    private MSBE.Part.Asset? _templateAsset;
    private int _nextPartIndex = 20000;  // Starting index for new part names

    // Standard fog wall models
    public static readonly HashSet<string> FogWallModels = new()
    {
        "AEG099_230",  // Standard fog wall
        "AEG099_231",  // Standard fog wall (alternate)
        "AEG099_232"   // Standard fog wall (alternate)
    };

    // Boss fog models
    public static readonly HashSet<string> BossFogModels = new()
    {
        "AEG099_001", "AEG099_002", "AEG099_003", "AEG099_239"
    };

    public FogAssetHelper(Dictionary<string, MSBE> msbs)
    {
        _msbs = msbs;
        InitializeTemplate();
    }

    private void InitializeTemplate()
    {
        // Use an existing asset as template (like FogRando does)
        // This asset exists in the vanilla game and has correct default properties
        const string templateMap = "m60_46_38_00";
        const string templateAssetName = "AEG007_310_2000";

        if (_msbs.TryGetValue(templateMap, out var msb))
        {
            _templateAsset = msb.Parts.Assets.Find(a => a.Name == templateAssetName);
        }

        if (_templateAsset == null)
        {
            throw new Exception($"Cannot initialize fog asset helper: template asset " +
                $"{templateAssetName} not found in {templateMap}. Ensure MSBs are loaded.");
        }
    }

    /// <summary>
    /// Create a new fog wall asset in the specified map.
    /// </summary>
    /// <param name="mapId">Map to add the asset to (e.g., "m10_00_00_00")</param>
    /// <param name="modelName">Fog model (e.g., "AEG099_231")</param>
    /// <param name="position">World position</param>
    /// <param name="rotation">World rotation (degrees)</param>
    /// <param name="entityId">Entity ID for EMEVD reference</param>
    /// <param name="enableSfx">Whether to enable fog SFX (default true)</param>
    /// <returns>The created asset</returns>
    public MSBE.Part.Asset CreateFogGate(
        string mapId,
        string modelName,
        Vector3 position,
        Vector3 rotation,
        uint entityId,
        bool enableSfx = true)
    {
        if (!_msbs.TryGetValue(mapId, out var msb))
            throw new ArgumentException($"MSB not loaded for map {mapId}");

        if (_templateAsset == null)
            throw new InvalidOperationException("Template asset not initialized");

        // 1. Deep copy the template asset
        var newAsset = (MSBE.Part.Asset)_templateAsset.DeepCopy();

        // 2. Set model name
        newAsset.ModelName = modelName;

        // 3. Ensure model is registered in MSB
        AddAssetModel(msb, modelName);

        // 4. Generate unique asset name
        var assetName = GenerateAssetName(mapId, modelName);
        SetAssetName(newAsset, assetName);

        // 5. Set position and rotation
        newAsset.Position = position;
        newAsset.Rotation = rotation;

        // 6. Set entity ID
        newAsset.EntityID = entityId;

        // 7. Configure SFX
        if (FogWallModels.Contains(modelName))
        {
            // 0 = enable SFX, -1 = disable SFX
            newAsset.AssetSfxParamRelativeID = enableSfx ? 0 : -1;
        }

        // 8. Add to MSB
        msb.Parts.Assets.Add(newAsset);

        return newAsset;
    }

    /// <summary>
    /// Add asset model to MSB if not already present.
    /// Adapted from FogRando's addAssetModel helper (L5221).
    /// </summary>
    private void AddAssetModel(MSBE msb, string modelName)
    {
        if (msb.Models.Assets.Any(m => m.Name == modelName))
            return;

        msb.Models.Assets.Add(new MSBE.Model.Asset
        {
            Name = modelName,
            SibPath = $@"N:\GR\data\Asset\Environment\geometry\{modelName[..6]}\{modelName}\sib\{modelName}.sib"
        });
    }

    /// <summary>
    /// Generate unique asset name for the new part.
    /// </summary>
    private string GenerateAssetName(string mapId, string modelName)
    {
        var index = _nextPartIndex++;
        return $"{modelName}_{index}";
    }

    /// <summary>
    /// Set asset name and update internal references.
    /// Adapted from FogRando's setAssetName helper (L5274).
    /// </summary>
    private void SetAssetName(MSBE.Part.Asset asset, string newName)
    {
        var oldName = asset.Name;
        asset.Name = newName;

        // Set Unk08 from name suffix (FogRando: setNameIdent L5263)
        if (int.TryParse(newName.Split('_').Last(), out var unk))
        {
            asset.Unk08 = unk;
        }

        // Update self-references in UnkPartNames
        for (int i = 0; i < asset.UnkPartNames.Length; i++)
        {
            if (asset.UnkPartNames[i] == oldName)
            {
                asset.UnkPartNames[i] = newName;
            }
        }

        // Update UnkT54PartName if self-referencing
        if (asset.UnkT54PartName == oldName)
        {
            asset.UnkT54PartName = newName;
        }
    }
}
```

### Entity ID Allocation

SpeedFog needs to allocate unique entity IDs for:
- New fog wall assets
- New SpawnPoint regions
- Event flags

**Strategy** (following FogRando pattern, L124-135):

```csharp
public class EntityIdAllocator
{
    // SpeedFog reserved ID ranges (must not conflict with vanilla or other mods)
    // FogRando uses 755890000+ for entities, 1040290000+ for flags
    // SpeedFog uses 79000000+ to avoid conflicts
    private uint _nextEntityId = 79000000;
    private uint _nextRegionId = 79100000;
    private uint _nextFlagId = 79200000;

    public uint AllocateEntityId() => _nextEntityId++;
    public uint AllocateRegionId() => _nextRegionId++;
    public uint AllocateFlagId() => _nextFlagId++;

    /// <summary>
    /// Reserve a block of IDs for a specific purpose.
    /// </summary>
    public (uint Start, uint End) ReserveBlock(int count, IdType type)
    {
        return type switch
        {
            IdType.Entity => ReserveFrom(ref _nextEntityId, count),
            IdType.Region => ReserveFrom(ref _nextRegionId, count),
            IdType.Flag => ReserveFrom(ref _nextFlagId, count),
            _ => throw new ArgumentException($"Unknown ID type: {type}")
        };
    }

    private (uint Start, uint End) ReserveFrom(ref uint current, int count)
    {
        var start = current;
        current += (uint)count;
        return (start, current - 1);
    }

    public enum IdType { Entity, Region, Flag }
}
```

---

## Task 3.6: WarpWriter.cs

Handles warp teleportation and **dynamic spawn region creation**.

### Key Insight: Dynamic Spawn Regions

Unlike FogRando which uses existing warp regions, SpeedFog creates **new SpawnPoint regions** for each fog gate destination. This is necessary because:
1. The fog's original destination doesn't match our randomized target
2. We need consistent spawn points near entry fogs

The process:
1. Get entry fog position from MSB (or fog_data for makefrom)
2. Create a new `MSBE.Region.SpawnPoint` at that position
3. Add it to the target map's MSB
4. Use its entity ID in the WarpPlayer instruction

### WarpWriter.cs

```csharp
using SoulsFormats;
using SpeedFogWriter.Models;
using System.Numerics;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates warp teleportation events and spawn regions.
/// </summary>
public class WarpWriter
{
    private readonly Dictionary<string, MSBE> _msbs;
    private readonly FogDataFile _fogData;

    public WarpWriter(Dictionary<string, MSBE> msbs, FogDataFile fogData)
    {
        _msbs = msbs;
        _fogData = fogData;
    }

    /// <summary>
    /// Create a SpawnPoint region in the target map for warping.
    /// </summary>
    public void CreateSpawnRegion(FogGateEvent fogGate)
    {
        if (!_msbs.TryGetValue(fogGate.TargetMap, out var msb))
        {
            Console.WriteLine($"Warning: MSB not loaded for {fogGate.TargetMap}");
            return;
        }

        // Determine spawn position from entry fog
        Vector3 position;
        Vector3 rotation = Vector3.Zero;

        if (fogGate.EntryFogData != null)
        {
            if (fogGate.EntryFogData.HasPosition)
            {
                // MakeFrom fog: use inline position
                position = fogGate.EntryFogData.PositionVec;
                rotation = fogGate.EntryFogData.RotationVec;
            }
            else
            {
                // Lookup position from MSB asset
                position = GetFogPositionFromMsb(fogGate.EntryFogData, msb);
            }
        }
        else
        {
            // Fallback: use origin (shouldn't happen in practice)
            Console.WriteLine($"Warning: No entry fog for {fogGate.TargetClusterId}, using origin");
            position = Vector3.Zero;
        }

        // Create SpawnPoint region
        var spawnRegion = new MSBE.Region.SpawnPoint
        {
            Name = $"SpeedFog_Spawn_{fogGate.WarpRegionId}",
            EntityID = (uint)fogGate.WarpRegionId,
            Position = position,
            Rotation = rotation
        };

        msb.Regions.Add(spawnRegion);
    }

    /// <summary>
    /// Look up fog asset position from MSB.
    /// IMPORTANT: Uses AssetName (e.g., "AEG099_002_9000"), NOT Model (e.g., "AEG099_002").
    /// </summary>
    private Vector3 GetFogPositionFromMsb(FogEntryData fog, MSBE msb)
    {
        // Find asset by asset_name or entity_id
        MSBE.Part.Asset? asset = fog.LookupBy switch
        {
            // Use AssetName for name-based lookup (e.g., "AEG099_002_9000")
            "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.AssetName),
            // Use EntityId for ID-based lookup
            "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == (uint)fog.EntityId),
            _ => null
        };

        if (asset != null)
            return asset.Position;

        // Fallback: try partial match on asset name
        asset = msb.Parts.Assets.FirstOrDefault(a =>
            a.Name.StartsWith(fog.Model) && a.Name.Contains(fog.AssetName.Split('_').Last()));

        if (asset != null)
        {
            Console.WriteLine($"Warning: Used partial match for fog {fog.AssetName} in {fog.Map}");
            return asset.Position;
        }

        Console.WriteLine($"Warning: Could not find fog asset {fog.AssetName} (id={fog.EntityId}) in {fog.Map}");
        return Vector3.Zero;
    }

    /// <summary>
    /// Get rotation for spawn point (face away from fog gate).
    /// FogRando uses oppositeRotation helper (GameDataWriterE.cs:L5334).
    /// </summary>
    private Vector3 GetSpawnRotation(FogEntryData fog, MSBE msb)
    {
        if (fog.IsMakeFrom && fog.Rotation != null)
            return fog.RotationVec;

        var asset = fog.LookupBy switch
        {
            "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.AssetName),
            "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == (uint)fog.EntityId),
            _ => null
        };

        if (asset != null)
        {
            // Return opposite rotation (player faces away from fog)
            return OppositeRotation(asset.Rotation);
        }

        return Vector3.Zero;
    }

    /// <summary>
    /// Flip rotation 180 degrees.
    /// Adapted from FogRando's oppositeRotation helper.
    /// </summary>
    private static Vector3 OppositeRotation(Vector3 rotation)
    {
        var y = rotation.Y + 180f;
        y = y >= 180f ? y - 360f : y;
        return new Vector3(rotation.X, y, rotation.Z);
    }

    /// <summary>
    /// Move position in the direction of rotation.
    /// Adapted from FogRando's moveInDirection helper (L5326).
    /// </summary>
    private static Vector3 MoveInDirection(Vector3 position, Vector3 rotation, float distance)
    {
        var rad = rotation.Y * MathF.PI / 180f;
        return new Vector3(
            position.X + MathF.Sin(rad) * distance,
            position.Y,
            position.Z + MathF.Cos(rad) * distance
        );
    }
}
```

---

## Task 3.7: StartingItemsWriter.cs

Gives key items to player at game start to prevent softlocks.

### Implementation Strategy

**Chosen approach**: EMEVD events with `DirectlyGivePlayerItem`

FogRando uses this instruction (GameDataWriterE.cs:L4964-4966):
```csharp
DirectlyGivePlayerItem(ItemType.Goods, {itemId}, 6001, 1)
```

This is preferable to modifying ItemLotParam because:
1. Works regardless of starting area
2. Fires once on game start
3. Easy to add conditionally (only items needed for the generated path)

### Key Item IDs

```csharp
// Item type constants (for DirectlyGivePlayerItem)
public enum ItemType
{
    Weapon = 0,
    Protector = 1,  // Armor
    Accessory = 2,
    Goods = 3,      // Key items, consumables
    Gem = 4         // Ashes of War
}
```

| Item | Type | ID | Purpose |
|------|------|-----|---------|
| Academy Glintstone Key | Goods | 8109 | Raya Lucaria access |
| Rusty Key | Goods | 8010 | Stormveil shortcut |
| Discarded Palace Key | Goods | 8199 | Raya Lucaria locked area |
| Drawing-Room Key | Goods | 8134 | Volcano Manor |
| Dectus Medallion (Left) | Goods | 8105 | Grand Lift of Dectus |
| Dectus Medallion (Right) | Goods | 8106 | Grand Lift of Dectus |
| Rold Medallion | Goods | 8107 | Grand Lift of Rold |
| Haligtree Medallion (Left) | Goods | 8175 | Consecrated Snowfield |
| Haligtree Medallion (Right) | Goods | 8176 | Consecrated Snowfield |
| Carian Inverted Statue | Goods | 8111 | Carian Study Hall |
| Imbued Sword Key | Goods | 8186 | Four Belfries (x3 needed) |
| Pureblood Knight's Medal | Goods | 2160 | Mohgwyn teleport |
| Stonesword Key | Goods | 8000 | Imp statue seals |

### StartingItemsWriter.cs

```csharp
using SoulsFormats;
using SoulsIds;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Creates EMEVD events to give key items at game start.
/// Prevents softlocks by ensuring all progression items are available.
/// </summary>
public class StartingItemsWriter
{
    // Item type for DirectlyGivePlayerItem instruction
    private const int ItemTypeGoods = 3;

    // Key items that should always be given
    private static readonly List<(int ItemId, int Quantity, string Name)> CoreKeyItems = new()
    {
        (8109, 1, "Academy Glintstone Key"),
        (8010, 1, "Rusty Key"),
        (8105, 1, "Dectus Medallion (Left)"),
        (8106, 1, "Dectus Medallion (Right)"),
        (8107, 1, "Rold Medallion"),
        (8000, 10, "Stonesword Key"),  // Give 10 for imp seals
    };

    // Additional key items based on zones in the graph
    private static readonly Dictionary<string, (int ItemId, int Quantity)> ZoneSpecificItems = new()
    {
        ["academy"] = (8109, 1),           // Academy Glintstone Key
        ["academy_entrance"] = (8109, 1),
        ["volcano_manor"] = (8134, 1),     // Drawing-Room Key
        ["carian_study_hall"] = (8111, 1), // Carian Inverted Statue
        ["haligtree"] = (8175, 1),         // Haligtree Left (also needs right)
        ["consecrated_snowfield"] = (8175, 1),
    };

    private readonly EMEVD _commonEmevd;
    private readonly Events _events;
    private readonly EntityIdAllocator _idAllocator;

    // Flags to track which items have been given (give once only)
    private uint _nextGiveItemFlag;

    public StartingItemsWriter(EMEVD commonEmevd, Events events, EntityIdAllocator idAllocator)
    {
        _commonEmevd = commonEmevd;
        _events = events;
        _idAllocator = idAllocator;
        _nextGiveItemFlag = 79900100;  // Reserved range for item give flags
    }

    /// <summary>
    /// Add core key items that are always needed.
    /// </summary>
    public void AddCoreItems()
    {
        foreach (var (itemId, quantity, name) in CoreKeyItems)
        {
            AddItemGiveEvent(itemId, quantity, name);
        }
    }

    /// <summary>
    /// Add items based on zones present in the graph.
    /// Only gives items needed for the generated path.
    /// </summary>
    public void AddItemsForZones(IEnumerable<string> zones)
    {
        var addedItems = new HashSet<int>();

        foreach (var zone in zones)
        {
            // Check if zone has specific item requirements
            foreach (var (zonePrefix, item) in ZoneSpecificItems)
            {
                if (zone.StartsWith(zonePrefix) && !addedItems.Contains(item.ItemId))
                {
                    AddItemGiveEvent(item.ItemId, item.Quantity, zonePrefix);
                    addedItems.Add(item.ItemId);
                }
            }
        }

        // Haligtree needs both medallion halves
        if (addedItems.Contains(8175) && !addedItems.Contains(8176))
        {
            AddItemGiveEvent(8176, 1, "Haligtree Medallion (Right)");
        }
    }

    /// <summary>
    /// Create an EMEVD event that gives an item once.
    /// </summary>
    private void AddItemGiveEvent(int itemId, int quantity, string debugName)
    {
        var giveFlag = _nextGiveItemFlag++;
        var eventId = _idAllocator.AllocateEntityId();

        // Create event: check flag, give item, set flag
        var evt = new EMEVD.Event(eventId, EMEVD.Event.RestBehaviorType.Default);

        // EndIfEventFlag(End, ON, EventFlag, giveFlag)
        evt.Instructions.Add(_events.ParseAdd(
            $"EndIfEventFlag(EventEndType.End, ON, TargetEventFlagType.EventFlag, {giveFlag})"));

        // DirectlyGivePlayerItem(ItemType.Goods, itemId, 6001, quantity)
        // 6001 = acquisition flag param (standard)
        evt.Instructions.Add(_events.ParseAdd(
            $"DirectlyGivePlayerItem({ItemTypeGoods}, {itemId}, 6001, {quantity})"));

        // SetEventFlag(EventFlag, giveFlag, ON)
        evt.Instructions.Add(_events.ParseAdd(
            $"SetEventFlag(TargetEventFlagType.EventFlag, {giveFlag}, ON)"));

        _commonEmevd.Events.Add(evt);

        // Initialize event from common event 0
        AddEventInitialization(eventId);

        Console.WriteLine($"  Added item give event: {debugName} (id={itemId}, qty={quantity})");
    }

    /// <summary>
    /// Add event initialization to common.emevd event 0.
    /// </summary>
    private void AddEventInitialization(long eventId)
    {
        // Find event 0 (common initialization event)
        var event0 = _commonEmevd.Events.FirstOrDefault(e => e.ID == 0);
        if (event0 == null)
        {
            Console.WriteLine("Warning: common.emevd event 0 not found, creating new");
            event0 = new EMEVD.Event(0, EMEVD.Event.RestBehaviorType.Default);
            _commonEmevd.Events.Insert(0, event0);
        }

        // Add: InitializeEvent(0, eventId, 0)
        event0.Instructions.Add(new EMEVD.Instruction(2000, 0, new List<object>
        {
            0,           // slot
            (int)eventId,
            0            // param (unused)
        }));
    }
}
```

### Usage in ModWriter

```csharp
private void AddStartingItems()
{
    if (_emevds == null || !_emevds.TryGetValue("common", out var commonEmevd))
        throw new InvalidOperationException("common.emevd not loaded");

    var itemWriter = new StartingItemsWriter(commonEmevd, _events, _idAllocator);

    // Add core items (always needed)
    itemWriter.AddCoreItems();

    // Add zone-specific items based on the graph
    var allZones = _graph.AllNodes().SelectMany(n => n.Zones);
    itemWriter.AddItemsForZones(allZones);

    Console.WriteLine($"  Added starting item events");
}
```

---

## Task 3.8: ModWriter.cs (Orchestrator)

Main class that coordinates all writers.

```csharp
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Main orchestrator for mod file generation.
/// Coordinates loading game data, generating mod content, and writing output.
/// </summary>
public class ModWriter
{
    private readonly string _gameDir;
    private readonly string _outputDir;
    private readonly string _dataDir;
    private readonly SpeedFogGraph _graph;

    // Game data
    private GameEditor _editor = null!;
    private ParamDictionary? _params;
    private Dictionary<string, MSBE> _msbs = new();
    private Dictionary<string, EMEVD> _emevds = new();
    private Events _events = null!;

    // SpeedFog data files
    private FogDataFile? _fogData;
    private ZoneDataFile? _zoneData;

    // Writers
    private EntityIdAllocator _idAllocator = null!;
    private ScalingWriter? _scalingWriter;
    private FogAssetHelper? _fogAssetHelper;
    private List<FogGateEvent>? _fogGates;

    // Track which files need to be written
    private HashSet<string> _writeMsbs = new();
    private HashSet<string> _writeEmevds = new() { "common", "common_func" };

    public ModWriter(string gameDir, string outputDir, string dataDir, SpeedFogGraph graph)
    {
        _gameDir = gameDir;
        _outputDir = outputDir;
        _dataDir = dataDir;
        _graph = graph;
    }

    /// <summary>
    /// Generate all mod files.
    /// </summary>
    public void Generate()
    {
        Console.WriteLine("Initializing...");
        Initialize();

        Console.WriteLine("Loading game data...");
        LoadGameData();

        Console.WriteLine("Loading SpeedFog data files...");
        LoadSpeedFogData();

        Console.WriteLine("Generating scaling effects...");
        GenerateScaling();

        Console.WriteLine("Generating fog gates and spawn regions...");
        GenerateFogGates();

        Console.WriteLine("Adding starting items...");
        AddStartingItems();

        Console.WriteLine("Writing output files...");
        WriteOutput();

        Console.WriteLine("Done!");
        PrintSummary();
    }

    private void Initialize()
    {
        _editor = new GameEditor(GameSpec.FromGame.ER);
        _editor.Spec.GameDir = _gameDir;
        _editor.Spec.DefDir = Path.Combine(_gameDir, "..", "Defs");  // Adjust path as needed

        _idAllocator = new EntityIdAllocator();

        // Initialize Events helper for EMEVD parsing
        var emedfPath = Path.Combine(_dataDir, "..", "reference", "fogrando-data", "er-common.emedf.json");
        _events = Events.FromSpec(emedfPath, _editor);
    }

    private void LoadGameData()
    {
        // Load params
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        if (!File.Exists(regulationPath))
            throw new FileNotFoundException($"regulation.bin not found: {regulationPath}");

        _params = new ParamDictionary
        {
            Defs = _editor.LoadDefs(),
            Inner = _editor.LoadParams(regulationPath, null)
        };
        Console.WriteLine($"  Loaded params");

        // Load EMEVDs (always load common and common_func)
        var eventDir = Path.Combine(_gameDir, "event");
        foreach (var file in Directory.GetFiles(eventDir, "*.emevd.dcx"))
        {
            var name = Path.GetFileNameWithoutExtension(file).Replace(".emevd", "");
            _emevds[name] = SoulsFile<EMEVD>.Read(file);
        }
        Console.WriteLine($"  Loaded {_emevds.Count} EMEVD files");

        // Note: MSBs loaded after SpeedFog data (to load only required maps)
    }

    private void LoadSpeedFogData()
    {
        // Load fog_data.json
        var fogDataPath = Path.Combine(_dataDir, "fog_data.json");
        if (!File.Exists(fogDataPath))
            throw new FileNotFoundException($"fog_data.json not found: {fogDataPath}");
        _fogData = FogDataFile.Load(fogDataPath);
        Console.WriteLine($"  Loaded {_fogData.Fogs.Count} fog entries");

        // Load zones_data.json
        var zonesDataPath = Path.Combine(_dataDir, "zones_data.json");
        if (!File.Exists(zonesDataPath))
            throw new FileNotFoundException($"zones_data.json not found: {zonesDataPath}");
        _zoneData = ZoneDataFile.Load(zonesDataPath);
        Console.WriteLine($"  Loaded {_zoneData.Zones.Count} zone entries");

        // Now load only required MSBs
        LoadRequiredMsbs();
    }

    private void LoadRequiredMsbs()
    {
        var requiredMaps = new HashSet<string>();

        // Maps containing fog gates (from edges)
        foreach (var edge in _graph.Edges)
        {
            var fog = _fogData!.GetFog(edge.FogId);
            if (fog != null)
                requiredMaps.Add(fog.Map);
        }

        // Maps for target zones (from nodes)
        foreach (var node in _graph.AllNodes())
        {
            var map = _zoneData!.GetMapForCluster(node.Zones);
            if (map != null)
                requiredMaps.Add(map);
        }

        // Load MSBs
        var mapDir = Path.Combine(_gameDir, "map", "mapstudio");
        foreach (var mapName in requiredMaps)
        {
            var file = Path.Combine(mapDir, $"{mapName}.msb.dcx");
            if (File.Exists(file))
            {
                _msbs[mapName] = SoulsFile<MSBE>.Read(file);
            }
            else
            {
                Console.WriteLine($"  Warning: MSB not found for {mapName}");
            }
        }
        Console.WriteLine($"  Loaded {_msbs.Count} MSB files (of {requiredMaps.Count} required)");

        // Initialize fog asset helper (needs MSBs)
        _fogAssetHelper = new FogAssetHelper(_msbs);
    }

    private void GenerateScaling()
    {
        if (_params == null) throw new InvalidOperationException("Params not loaded");

        _scalingWriter = new ScalingWriter(_params);
        _scalingWriter.GenerateScalingEffects();

        Console.WriteLine($"  Generated {_scalingWriter.TierTransitions.Count} scaling effects");
    }

    private void GenerateFogGates()
    {
        if (_fogData == null || _zoneData == null)
            throw new InvalidOperationException("SpeedFog data not loaded");

        var fogWriter = new FogGateWriter(_fogData, _zoneData);
        _fogGates = fogWriter.CreateFogGates(_graph);
        Console.WriteLine($"  Created {_fogGates.Count} fog gate definitions");

        // Create spawn regions in target MSBs
        var warpWriter = new WarpWriter(_msbs, _fogData);
        foreach (var fogGate in _fogGates)
        {
            warpWriter.CreateSpawnRegion(fogGate);
            _writeMsbs.Add(fogGate.TargetMap);  // Mark MSB as modified
        }
        Console.WriteLine($"  Created {_fogGates.Count} spawn regions");

        // Generate EMEVD events
        // (Implementation uses EventBuilder with templates from speedfog-events.yaml)
        // For now, mark source map EMEVDs as needing writes
        foreach (var fogGate in _fogGates)
        {
            _writeEmevds.Add(fogGate.SourceMap);
        }
    }

    private void AddStartingItems()
    {
        if (!_emevds.TryGetValue("common", out var commonEmevd))
            throw new InvalidOperationException("common.emevd not loaded");

        var itemWriter = new StartingItemsWriter(commonEmevd, _events, _idAllocator);
        itemWriter.AddCoreItems();

        // Add zone-specific items
        var allZones = _graph.AllNodes().SelectMany(n => n.Zones);
        itemWriter.AddItemsForZones(allZones);
    }

    private void WriteOutput()
    {
        if (_params == null)
            throw new InvalidOperationException("Data not loaded");

        var modDir = Path.Combine(_outputDir, "mods", "speedfog");

        // Write params (regulation.bin)
        var paramDir = Path.Combine(modDir, "param", "gameparam");
        Directory.CreateDirectory(paramDir);
        var regulationOut = Path.Combine(paramDir, "regulation.bin");
        _editor.OverrideBndRel<PARAM>(
            Path.Combine(_gameDir, "regulation.bin"),
            regulationOut,
            _params.Inner,
            f => f.Write(),
            null,
            DCX.Type.DCX_DFLT_11000_44_9
        );
        Console.WriteLine($"  Written: regulation.bin");

        // Write modified EMEVDs
        var eventDir = Path.Combine(modDir, "event");
        Directory.CreateDirectory(eventDir);
        foreach (var name in _writeEmevds)
        {
            if (_emevds.TryGetValue(name, out var emevd))
            {
                var path = Path.Combine(eventDir, $"{name}.emevd.dcx");
                emevd.Write(path, DCX.Type.DCX_DFLT_11000_44_9);
            }
        }
        Console.WriteLine($"  Written: {_writeEmevds.Count} EMEVD files");

        // Write modified MSBs
        var mapDir = Path.Combine(modDir, "map", "mapstudio");
        Directory.CreateDirectory(mapDir);
        foreach (var name in _writeMsbs)
        {
            if (_msbs.TryGetValue(name, out var msb))
            {
                var path = Path.Combine(mapDir, $"{name}.msb.dcx");
                msb.Write(path, DCX.Type.DCX_DFLT_11000_44_9);
            }
        }
        Console.WriteLine($"  Written: {_writeMsbs.Count} MSB files");

        Console.WriteLine($"\nOutput written to: {modDir}");
    }

    private void PrintSummary()
    {
        Console.WriteLine("\n=== SpeedFog Mod Summary ===");
        Console.WriteLine($"Seed: {_graph.Seed}");
        Console.WriteLine($"Layers: {_graph.TotalLayers}");
        Console.WriteLine($"Nodes: {_graph.TotalNodes}");
        Console.WriteLine($"Paths: {_graph.TotalPaths}");
        Console.WriteLine($"Path weights: [{string.Join(", ", _graph.PathWeights)}]");
        Console.WriteLine($"Fog gates: {_fogGates?.Count ?? 0}");
        Console.WriteLine($"Modified MSBs: {_writeMsbs.Count}");
        Console.WriteLine($"Modified EMEVDs: {_writeEmevds.Count}");
    }
}
```

---

## Task 3.9: Program.cs (CLI)

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
            Console.WriteLine("Usage: SpeedFogWriter <graph.json> <game_dir> <output_dir> [--data-dir <path>]");
            Console.WriteLine();
            Console.WriteLine("Arguments:");
            Console.WriteLine("  graph.json  - Path to generated graph from speedfog-core");
            Console.WriteLine("  game_dir    - Path to Elden Ring Game folder");
            Console.WriteLine("  output_dir  - Output directory for mod files");
            Console.WriteLine();
            Console.WriteLine("Options:");
            Console.WriteLine("  --data-dir  - Path to data directory (default: ../data relative to exe)");
            return 1;
        }

        var graphPath = args[0];
        var gameDir = args[1];
        var outputDir = args[2];

        // Parse optional data-dir
        var dataDir = Path.Combine(AppContext.BaseDirectory, "..", "data");
        for (int i = 3; i < args.Length - 1; i++)
        {
            if (args[i] == "--data-dir")
            {
                dataDir = args[i + 1];
            }
        }

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

        if (!Directory.Exists(dataDir))
        {
            Console.Error.WriteLine($"Error: Data directory not found: {dataDir}");
            Console.Error.WriteLine("  Expected to find fog_data.json and speedfog-events.yaml");
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
            Console.WriteLine($"  Path weights: [{string.Join(", ", graph.PathWeights)}]");

            // Validate graph structure
            if (graph.StartNode == null)
            {
                Console.Error.WriteLine("Error: Graph has no start node");
                return 1;
            }
            if (graph.EndNode == null)
            {
                Console.Error.WriteLine("Error: Graph has no end node");
                return 1;
            }

            // Generate mod
            Console.WriteLine();
            var writer = new ModWriter(gameDir, outputDir, dataDir, graph);
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

## FogRando Reference: GameDataWriterE.cs

The decompiled `GameDataWriterE.cs` (5639 lines) is the main reference for mod generation. Below are key sections relevant to SpeedFog.

### File Structure Overview

```
GameDataWriterE.cs
├── L1-19:     Imports and class declaration
├── L20-4974:  Main Write() method (single massive function)
│   ├── L37-70:      "Reading game data" - loads MSBs, EMEVDs, params
│   ├── L109+:       "Modifying game data" - main logic
│   ├── L190-202:    Fog model definitions
│   ├── L262-4736:   Fog gate creation (multiple call sites)
│   ├── L1781-1852:  EMEVD event processing and creation
│   ├── L1964-1966:  EldenScaling integration
│   └── L4977-5030:  "Writing game data" - output files
├── L5205-5570:  Helper functions (local/nested)
│   ├── L5213:   parseFloats
│   ├── L5221:   addAssetModel
│   ├── L5243:   addEnemyModel
│   ├── L5263:   setNameIdent
│   ├── L5274:   setAssetName
│   ├── L5311:   setBoxRegion
│   ├── L5326:   moveInDirection
│   └── L5343+:  parseMap, formatMap, parentMap, etc.
└── L5570-5637:  Static data (dupeMsbs list)
```

### Key Sections

#### 1. Loading Game Data (L37-70)

```csharp
// L37-44: Load MSBs and EMEVDs
notify("Reading game data");
CS$<>8__locals1.msbs = CS$<>8__locals1.<Write>g__loadDir|2<MSBE>(
    ..., "map\\mapstudio", (path) => SoulsFile<MSBE>.Read(path), "*.msb.dcx");
CS$<>8__locals1.emevds = CS$<>8__locals1.<Write>g__loadDir|2<EMEVD>(
    ..., "event", (path) => SoulsFile<EMEVD>.Read(path), "*.emevd.dcx");

// L65-68: Load params
CS$<>8__locals1.Params = new ParamDictionary {
    Defs = CS$<>8__locals1.editor.LoadDefs(),
    Inner = CS$<>8__locals1.editor.LoadParams(text5, null)
};
```

#### 2. Fog Model IDs (L190-194)

```csharp
CS$<>8__locals1.mfogModels = new HashSet<string> {
    "AEG099_230",  // Standard fog wall
    "AEG099_231",  // Standard fog wall (alternate)
    "AEG099_232"   // Standard fog wall (alternate)
};
```

Other fog/door models used throughout the file:
- `AEG099_001`, `AEG099_002`, `AEG099_003` - Boss fog
- `AEG099_060`, `AEG099_065`, `AEG099_090` - Special gates
- `AEG099_630` - Overworld barrier
- `AEG250_007`, `AEG030_925` - Door assets

#### 3. Fog Gate Creation (L262, L363, L640, etc.)

The `addFakeGate` helper is a closure within Write(). Usage pattern:

```csharp
// L262: Create fog gate with position and name
MSBE.Part.Asset asset3 = CS$<>8__locals1.<Write>g__addFakeGate|25(
    e.Area,           // Map ID (e.g., "m10_01_00_00")
    array3[0],        // Model name (e.g., "AEG099_231")
    array3[1],        // Base asset name (for cloning)
    pos,              // Vector3 position
    rot,              // Vector3 rotation
    e.Name            // Optional entity name
);
asset3.EntityID = (uint)e.ID;
```

Key call sites:
- **L262**: Standard fog gate creation
- **L363, L367**: Multi-height fog walls (stacked vertically)
- **L640**: Exit fog gates with regions
- **L1420**: Dungeon destination fog
- **L2643, L2717**: Boss fog gates
- **L4230-4244**: Custom barrier fog walls (triple-stacked)
- **L4736**: Barrier fog with entity ID

#### 4. EMEVD Event Creation (L1781-1852)

```csharp
// L1781-1803: Process existing events
foreach (KeyValuePair<string, EMEVD> keyValuePair2 in CS$<>8__locals1.emevds) {
    foreach (EMEVD.Event @event in keyValuePair2.Value.Events) {
        foreach (EMEVD.Instruction instruction in @event.Instructions) {
            // Check instruction bank/ID and modify as needed
        }
    }
}

// L1804-1828: Create new events from EventConfig
foreach (EventConfig.NewEvent newEvent in eventConfig.NewEvents) {
    event2 = new EMEVD.Event((long)newEvent.ID, flag11 ? 1 : 0);
    // Parse commands and add instructions
    for (int m2 = 0; m2 < list19.Count; m2++) {
        var valueTuple2 = CS$<>8__locals1.events.ParseAddArg(list19[m2], m2);
        event2.Instructions.Add(valueTuple2.Item1);
        event2.Parameters.AddRange(valueTuple2.Item2);
    }
    CS$<>8__locals1.emevds["common"].Events.Add(event2);
}
```

#### 5. Scaling Integration (L1964-1966)

```csharp
EldenScaling eldenScaling = new EldenScaling(CS$<>8__locals1.Params);
Dictionary<int, int> dictionary14 = eldenScaling.InitializeEldenScaling();
EldenScaling.SpEffectValues spEffectValues = eldenScaling.EditScalingSpEffects();
```

#### 6. Warp Point Structure (L462-493, L767-773)

```csharp
// L462-477: Create warp point from entrance data
side11.Warp = new Graph.WarpPoint {
    Region = (int)region.EntityID,
    Retry = (int)region.EntityID  // Optional
};

// L767-773: Create return warp
e.ASide.Warp = new Graph.WarpPoint {
    Region = (int)num11++,
    SitFlag = ...,   // Site of Grace flag
    WarpFlag = ...   // Warp activation flag
};
```

#### 7. Writing Output (L4977-5030)

```csharp
// L4977
notify("Writing game data");

// L4980-4990: Write params
Console.WriteLine("Writing params");
string text38 = CS$<>8__locals1.outDir + "\\regulation.bin";
CS$<>8__locals1.editor.OverrideBndRel<PARAM>(text5, text38,
    CS$<>8__locals1.Params.Inner, (f) => f.Write(), null, type);

// L4996-5029: Write EMEVDs
Console.WriteLine($"Writing {CS$<>8__locals1.writeEmevds.Count} emevds");
foreach (KeyValuePair<string, EMEVD> entry in CS$<>8__locals1.emevds) {
    // Process and write each EMEVD file
}
```

### Helper Functions

| Helper | Line | Purpose |
|--------|------|---------|
| `parseFloats` | L5213 | Parse space-separated floats |
| `addAssetModel` | L5221 | Add asset model to MSB |
| `addEnemyModel` | L5243 | Add enemy model to MSB |
| `setNameIdent` | L5263 | Set entity name identifier |
| `setAssetName` | L5274 | Rename asset and update references |
| `setBoxRegion` | L5311 | Create box-shaped region from spec |
| `moveInDirection` | L5326 | Move point in rotation direction |
| `oppositeRotation` | L5334 | Flip rotation 180° |
| `parseMap` | L5343 | Parse map ID to bytes |
| `formatMap` | L5350 | Format map bytes to string |

---

## FogRando Reference: EldenScaling.cs

The `EldenScaling.cs` (269 lines) handles enemy stat scaling.

### Structure Overview

```
EldenScaling.cs
├── L11-26:    ScalingData class
├── L28-42:    SpEffectValues/AreaScalingValue classes
├── L46-63:    eldenScaling config
│   ├── ScalingBase = 7000
│   ├── NewScalingBase = 7800000
│   └── MaxTier = 34
├── L53-62:    ScalingFields (health, damage, defense, etc.)
├── L65-71:    eldenExps (34 tier XP values)
├── L73:       EldenSoulScaling computed from eldenExps
├── L80-179:   InitializeEldenScaling()
└── L181-269:  EditScalingSpEffects()
```

### Key Constants (L46-71)

```csharp
private readonly ScalingData eldenScaling = new ScalingData {
    ScalingBase = 7000,        // Vanilla scaling SpEffect base
    NewScalingBase = 7800000,  // Custom SpEffect ID range
    MaxTier = 34,              // Maximum scaling tier
    ScalingFields = new Dictionary<string, List<string>> {
        ["health"] = new List<string> { "maxHpRate" },
        ["stamina"] = new List<string> { "maxStaminaRate" },
        ["staminadamage"] = new List<string> { "staminaAttackRate" },
        ["damage"] = new List<string> {
            "physicsAttackPowerRate", "magicAttackPowerRate",
            "fireAttackPowerRate", "thunderAttackPowerRate",
            "darkAttackPowerRate"
        },
        ["defense"] = new List<string> {
            "physicsDiffenceRate", "magicDiffenceRate",
            "fireDiffenceRate", "thunderDiffenceRate",
            "darkDiffenceRate"
        },
        ["buildup"] = new List<string> { /* status effect rates */ },
        ["xp"] = new List<string> { "haveSoulRate" }
    }
};

// L65-71: XP scaling exponents (powers of 10)
private static readonly List<double> eldenExps = new List<double> {
    0.0, 23.0, 43.0, 188.0, 233.0, 285.0, 487.0, 743.0, 769.0, 925.0,
    970.0, 1091.0, 1107.0, 1192.0, 1277.0, 1430.0, 1438.0, 1458.0, ...
};
```

### Scaling Matrix Generation (L165-179)

```csharp
// Creates ratio matrix for tier transitions
Dictionary<string, List<double>> makeScalingMatrix(
    Dictionary<string, List<double>> scalingMult) {
    foreach (var item2 in scalingMult) {
        List<double> list = new List<double>();
        foreach (var (fromTier, toTier) in eldenScaling.SectionPairs) {
            list.Add(value[toTier - 1] / value[fromTier - 1]);
        }
        dictionary4[item2.Key] = list;
    }
    return dictionary4;
}
```

---

## Key Challenges & Notes

### 1. EMEVD Generation

SpeedFog uses a **YAML-based template approach** (Task 3.3) for EMEVD generation, inspired by FogRando but simplified:

- Event templates are defined in `writer/data/speedfog-events.yaml`
- `EventBuilder.cs` parses templates and builds EMEVD using SoulsIds' `Events.ParseAddArg()`
- This keeps event logic readable and separate from C# code

**Key templates needed** (from FogRando's `fogevents.txt`):
| Template | FogRando ID | Purpose |
|----------|-------------|---------|
| `scale` | 9005770 | Apply scaling SpEffect to enemies |
| `showsfx` | 9005775 | Display fog gate visual effect |
| `fogwarp` | 9005777 | Warp on fog gate interaction (simplified) |

**Testing approach**:
- Start with a single hardcoded fog gate to verify EMEVD format
- Then validate the YAML template system works correctly

### 2. Fog Position Data (NEW)

**Critical dependency**: The C# writer needs `fog_data.json` with fog gate coordinates.

This data must be extracted from `fog.txt` before Phase 3 implementation begins. Create a Python script `tools/extract_fog_data.py` that:

1. Parses `fog.txt` Entrances and Warps sections
2. Extracts position, rotation, entity_id, map, model for each fog
3. Outputs `writer/data/fog_data.json`

**Key sections in fog.txt to parse**:
```yaml
Entrances:
  - Name: stormveil_front
    ID: 1034432500
    Area: stormveil_start
    ...
    Pos: "123.5 45.2 -78.9"
    Rot: "0 180 0"
```

**Note**: The fog_id in clusters.json matches the ID or Name from fog.txt Entrances section.

### 3. Cluster-Based Architecture

The Phase 3 spec was updated to match the cluster-based architecture from Phase 1-2:

| Old (zone-based) | New (cluster-based) |
|------------------|---------------------|
| `zone_id` (single) | `zones` (list) |
| `ZoneMap` | Lookup via `zones_data.json` |
| `entries`/`exits` (node IDs) | `entry_fogs`/`exit_fogs` (fog ID lists) |
| No explicit edges | `edges` array with fog_id |

The C# writer uses edges, fog_data, and zones_data to determine:
- Where to place fog walls (source cluster exit fog position from fog_data)
- Where to warp (target cluster map from zones_data, position from entry fog)

### 4. Fog Gate Asset Creation

The `addFakeGate` helper in FogRando (closure, not standalone) creates fog wall assets by:
1. Cloning an existing asset as template
2. Setting model name (e.g., `AEG099_231`)
3. Setting position and rotation
4. Optionally setting EntityID and SfxParamRelativeID

**Key references**:
- `GameDataWriterE.cs:L262` - Basic usage
- `GameDataWriterE.cs:L363-367` - Multi-height stacking
- `GameDataWriterE.cs:L5221-5243` - `addAssetModel` helper

### 4. SoulsFormats Compatibility

Ensure you're using a SoulsFormats version that supports Elden Ring. SoulsFormatsNEXT is recommended.

---

## Acceptance Criteria

### Task 3.2.1 (fog_data.json) - COMPLETE
- [x] Python script `tools/extract_fog_data.py` exists
- [x] Script parses fog.txt Entrances and Warps sections
- [x] Script outputs `writer/data/fog_data.json`
- [x] All fog_ids from clusters.json are present in fog_data.json
- [ ] **NEW**: Script includes `asset_name` field (not just `model`)

### Task 3.2.2 (zones_data.json) - TODO
- [ ] Python script `tools/extract_zones_data.py` exists
- [ ] Script parses `core/zones.toml`
- [ ] Script outputs `writer/data/zones_data.json` with zone→map mapping
- [ ] All zones from clusters are present in zones_data.json

### Task 3.1 (Setup)
- [ ] Project builds with `dotnet build`
- [ ] SoulsFormats loads correctly
- [ ] SoulsIds Events class can parse EMEVD commands

### Task 3.1.1 (Game Data Loading) - NEW
- [ ] `GameDataLoader` can load regulation.bin (params)
- [ ] `GameDataLoader` can load MSB files
- [ ] `GameDataLoader` can load EMEVD files
- [ ] Only required MSBs are loaded (based on graph)

### Task 3.2 (Models)
- [ ] `SpeedFogGraph.Load()` parses graph.json correctly
- [ ] All nodes and edges accessible
- [ ] `FogDataFile.Load()` parses fog_data.json correctly
- [ ] `FogDataFile` includes `AssetName` property (not just `Model`)
- [ ] `ZoneDataFile.Load()` parses zones_data.json correctly
- [ ] Nodes contain correct cluster data (zones list, **entry_fogs** list, exit_fogs)

### Task 3.3 (Event Templates)
- [ ] `speedfog-events.yaml` loads correctly
- [ ] `EventBuilder` can parse template commands
- [ ] `fogwarp_simple` template generates valid EMEVD
- [ ] Generated EMEVD instructions are valid

### Task 3.4 (Scaling)
- [ ] SpEffect entries created for tier transitions
- [ ] Scaling factors are reasonable (no 100x damage)

### Task 3.5 (Fog Gates)
- [ ] Fog gate events created for all edges in the graph
- [ ] Each edge's fog_id is found in fog_data.json
- [ ] `FogAssetHelper` can create new fog wall assets
- [ ] `EntityIdAllocator` generates unique IDs

### Task 3.5.1 (Asset Creation) - NEW
- [ ] `FogAssetHelper.CreateFogGate()` creates valid MSB assets
- [ ] Asset models are registered in MSB
- [ ] Asset names are unique
- [ ] SFX configuration is correct for fog walls

### Task 3.6 (Warps)
- [ ] Target map determined via zones_data.json
- [ ] SpawnPoint regions created in target MSBs
- [ ] Spawn positions use **AssetName** for lookup (not Model)
- [ ] Spawn rotations face away from fog gate

### Task 3.7 (Starting Items)
- [ ] `DirectlyGivePlayerItem` EMEVD instructions generated
- [ ] Items given only once (flag-gated)
- [ ] Core items always given
- [ ] Zone-specific items based on graph

### Task 3.8-3.9 (Integration)
- [ ] Full pipeline works: graph.json + fog_data.json + zones_data.json → mod files
- [ ] MSB files written for modified maps
- [ ] EMEVD files written for modified events
- [ ] Output directory structure matches ModEngine 2 expectations
- [ ] Summary output shows correct seed, nodes, paths, weights

---

## Testing

### Unit Tests

Test JSON parsing, scaling calculations, etc.

```csharp
// Example test cases
[Test]
public void SpeedFogGraph_Load_ParsesNodesCorrectly()
{
    var graph = SpeedFogGraph.Load("test/sample_graph.json");
    Assert.That(graph.Nodes.Count, Is.EqualTo(15));
    Assert.That(graph.StartNode?.Type, Is.EqualTo("start"));
    Assert.That(graph.EndNode?.Type, Is.EqualTo("final_boss"));
}

[Test]
public void NodeData_HasMultipleZones()
{
    var graph = SpeedFogGraph.Load("test/sample_graph.json");
    var stormveil = graph.AllNodes().First(n => n.ClusterId.Contains("stormveil"));
    Assert.That(stormveil.Zones, Has.Count.GreaterThan(1));
}

[Test]
public void FogData_AllEdgeFogsExist()
{
    var graph = SpeedFogGraph.Load("test/sample_graph.json");
    var fogPos = FogDataFile.Load("data/fog_data.json");

    foreach (var edge in graph.Edges)
    {
        Assert.That(fogPos.GetFog(edge.FogId), Is.Not.Null,
            $"Missing fog position for {edge.FogId}");
    }
}
```

### Integration Test

```bash
# 1. Extract fog positions (prerequisite)
cd speedfog
python tools/extract_fog_data.py \
    reference/fogrando-data/fog.txt \
    writer/data/fog_data.json

# 2. Generate graph (Phase 2)
cd core
python -m speedfog_core.main config.toml -o ../writer/test/graph.json -v

# 3. Generate mod files (Phase 3)
cd ../writer
dotnet run -- test/graph.json "C:/Games/ELDEN RING/Game" ./output --data-dir ./data

# 4. Verify output structure
ls -la output/mods/speedfog/
# Should contain:
#   param/gameparam/regulation.bin
#   event/common.emevd.dcx
#   event/m10_*.emevd.dcx (map-specific events)
```

### In-Game Test

1. Copy output to ModEngine mod folder
2. Launch game with ModEngine
3. Start new game, verify:
   - Starting items present (keys, medallions)
   - First fog gate appears at Chapel exit
   - Warp works (player teleports to first cluster)
   - Enemy scaling feels appropriate (tier-based)
   - Both paths are playable to final boss

---

## Next Phase

After completing Phase 3, proceed to Phase 4: Integration & Testing (spec to be created).
