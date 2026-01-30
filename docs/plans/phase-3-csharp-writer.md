# Phase 3: C# Writer - Detailed Implementation Spec

**Parent document**: [SpeedFog Design](./2026-01-29-speedfog-design.md)
**Prerequisite**: [Phase 2: DAG Generation](./phase-2-dag-generation.md), [Cluster Generation](./generate-clusters-spec.md)
**Status**: Ready for implementation
**Last updated**: 2026-01-30 (aligned with cluster-based architecture from Phase 1-2)

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
- **New**: `fog_data.json` with fog gate coordinates (see Task 3.2.1)

## Deliverables

```
speedfog/writer/
├── data/
│   ├── speedfog-events.yaml       # Event templates (readable, not hardcoded)
│   └── fog_data.json         # Fog gate positions extracted from fog.txt
│
├── SpeedFogWriter/
│   ├── SpeedFogWriter.csproj
│   ├── Program.cs                 # CLI entry point
│   │
│   ├── Models/
│   │   ├── SpeedFogGraph.cs       # JSON deserialization (graph.json)
│   │   ├── NodeData.cs            # Cluster-based node
│   │   ├── EdgeData.cs            # Edge between nodes
│   │   ├── FogEntryData.cs     # Fog gate positions (fog_data.json)
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

## Task 3.2: JSON Models (Models/)

### graph.json Format (from Phase 2)

The Python core outputs `graph.json` in this format:

```json
{
  "seed": 123456789,
  "total_layers": 8,
  "total_nodes": 15,
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
      "entry_fog": null,
      "exit_fogs": ["1034432500", "1034432501"]
    },
    "node_1a": {
      "cluster_id": "stormveil_c3d4",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 20,
      "layer": 1,
      "tier": 5,
      "entry_fog": "1034432500",
      "exit_fogs": ["1034432502", "1034432503"]
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
    /// Fog ID used to enter this cluster (null for start node).
    /// This is the fog gate the player passed through to reach this cluster.
    /// </summary>
    [JsonPropertyName("entry_fog")]
    public string? EntryFog { get; set; }

    /// <summary>
    /// Available exit fog IDs from this cluster.
    /// These are fogs that can lead to the next layer.
    /// Note: The entry_fog is excluded if it was bidirectional.
    /// </summary>
    [JsonPropertyName("exit_fogs")]
    public List<string> ExitFogs { get; set; } = new();

    // Convenience properties
    public bool IsStart => Type == "start";
    public bool IsFinalBoss => Type == "final_boss";
    public bool IsLegacyDungeon => Type == "legacy_dungeon";
    public bool IsMiniDungeon => Type == "mini_dungeon";
    public bool IsBossArena => Type == "boss_arena";

    /// <summary>
    /// Get the primary zone (first zone in the cluster).
    /// </summary>
    public string PrimaryZone => Zones.FirstOrDefault() ?? "";
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
| `lookup_by` | "name" (AEG fogs) or "entity_id" (numeric fogs), null for makefrom |
| `position` | `[x, y, z]` for makefrom fogs, `null` otherwise |
| `rotation` | `[rx, ry, rz]` for makefrom fogs, `null` otherwise |

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

    // Look up in MSB
    var msb = msbs[fog.Map];
    MSBE.Part.Asset? asset = fog.LookupBy switch
    {
        "name" => msb.Parts.Assets.FirstOrDefault(a => a.Name == fog.Model),
        "entity_id" => msb.Parts.Assets.FirstOrDefault(a => a.EntityID == fog.EntityId),
        _ => null
    };

    if (asset != null)
        return asset.Position;

    throw new Exception($"Could not find fog asset in {fog.Map}");
}
```

### Extraction Script - COMPLETE

The Python script `tools/extract_fog_data.py` has been implemented:

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

**Note**: This is a one-time extraction task. The metadata doesn't change between runs.

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
# Simplified from FogRando's fogevents.txt
#
# Parameter notation: X{offset}_{size}
#   - offset: byte offset in the event parameter block
#   - size: parameter size in bytes (1 or 4)
#   - Example: X0_4 = 4-byte param at offset 0, X12_1 = 1-byte param at offset 12

templates:
  # Apply scaling to an enemy when loaded
  scale:
    id: 79000001
    restart: true
    params:
      entity_id: X0_4
      speffect: X4_4
    commands:
      - IfCharacterBackreadStatus(MAIN, $entity_id, true, ComparisonType.Equal, 1)
      - SetSpEffect($entity_id, $speffect)
      - IfCharacterBackreadStatus(MAIN, $entity_id, false, ComparisonType.Equal, 1)
      - WaitFixedTimeSeconds(1)
      - EndUnconditionally(EventEndType.Restart)

  # Show fog gate visual effect
  show_fog:
    id: 79000002
    params:
      fog_gate: X0_4
      sfx_id: X4_4
    commands:
      - ChangeAssetEnableState($fog_gate, Enabled)
      - CreateAssetFollowingSFX($fog_gate, 101, $sfx_id)

  # Warp player through fog gate (simplified from FogRando's fogwarp)
  # Note: Map bytes are 4 separate 1-byte params (m, area, block, sub) for mAA_BB_CC_00
  fog_warp:
    id: 79000003
    restart: true
    params:
      fog_gate: X0_4
      button_param: X4_4
      warp_region: X8_4
      map_m: X12_1          # Map type (e.g., 10 for m10_xx_xx_xx)
      map_area: X13_1       # Area (e.g., 01)
      map_block: X14_1      # Block (e.g., 00)
      map_sub: X15_1        # Sub (e.g., 00)
      rotate_target: X16_4
    commands:
      - IfActionButtonInArea(AND_01, $button_param, $fog_gate)
      - IfConditionGroup(MAIN, PASS, AND_01)
      - RotateCharacter(10000, $rotate_target, 60060, false)
      - WaitFixedTimeSeconds(0.5)
      - ShowTextOnLoadingScreen(Disabled)
      - WarpPlayer($map_m, $map_area, $map_block, $map_sub, $warp_region, 0)
      - WaitFixedTimeFrames(1)
      - EndUnconditionally(EventEndType.Restart)

  # Give starting items on trigger flag
  starting_items:
    id: 79000004
    params:
      trigger_flag: X0_4
      item_lot: X4_4
    commands:
      - IfEventFlag(MAIN, ON, TargetEventFlagType.EventFlag, $trigger_flag)
      - AwardItemLot($item_lot)

defaults:
  button_param: 63000      # "Traverse the mist"
  fog_sfx: 8011            # Standard fog visual effect
  trigger_flag: 79900000   # SpeedFog start flag
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

The `fog_id` is looked up in `fog_data.json` to get:
- Position and rotation for spawning the fog wall
- Map ID for the EMEVD
- Warp region for teleportation

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

    private readonly FogDataFile _fogPositions;
    private int _nextEventId;
    private int _nextFlagId;

    public FogGateWriter(FogDataFile fogPositions)
    {
        _fogPositions = fogPositions;
        _nextEventId = CustomEventBase;
        _nextFlagId = CustomFlagBase;
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

            // Look up fog position data
            var exitFogData = _fogPositions.GetFog(edge.FogId);
            if (exitFogData == null)
            {
                Console.WriteLine($"Warning: Missing fog position for {edge.FogId}");
                continue;
            }

            // Find the entry fog of the target cluster
            var entryFogData = target.EntryFog != null
                ? _fogPositions.GetFog(target.EntryFog)
                : null;

            if (entryFogData == null && target.EntryFog != null)
            {
                Console.WriteLine($"Warning: Missing fog position for target entry {target.EntryFog}");
            }

            var fogEvent = CreateFogGate(source, target, edge, exitFogData, entryFogData);
            events.Add(fogEvent);
        }

        return events;
    }

    private FogGateEvent CreateFogGate(
        NodeData source,
        NodeData target,
        EdgeData edge,
        FogEntryData exitFog,
        FogEntryData? entryFog)
    {
        var eventId = _nextEventId++;
        var flagId = _nextFlagId++;

        return new FogGateEvent
        {
            EventId = eventId,
            FlagId = flagId,
            EdgeFogId = edge.FogId,
            SourceNodeId = source.Id,
            TargetNodeId = target.Id,
            SourceClusterId = source.ClusterId,
            TargetClusterId = target.ClusterId,

            // Fog gate position (exit of source cluster)
            SourceMap = exitFog.Map,
            FogEntry = exitFog.PositionVec,
            FogRotation = exitFog.RotationVec,
            FogEntityId = exitFog.EntityId,
            FogModel = exitFog.Model,

            // Warp destination (entrance of target cluster)
            TargetMap = entryFog?.Map ?? exitFog.Map,
            WarpRegion = entryFog?.WarpRegion ?? exitFog.WarpRegion,
            WarpPosition = entryFog?.PositionVec ?? exitFog.PositionVec,

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

    // Fog gate spawn (exit of source cluster)
    public string SourceMap { get; set; } = "";
    public Vector3 FogEntry { get; set; }
    public Vector3 FogRotation { get; set; }
    public int FogEntityId { get; set; }
    public string FogModel { get; set; } = "";

    // Warp destination (entrance of target cluster)
    public string TargetMap { get; set; } = "";
    public int WarpRegion { get; set; }
    public Vector3 WarpPosition { get; set; }

    // Scaling
    public int SourceTier { get; set; }
    public int TargetTier { get; set; }

    /// <summary>
    /// Parse target map to bytes for EMEVD warp instruction.
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

## Task 3.6: WarpWriter.cs

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

## Task 3.7: StartingItemsWriter.cs

Gives key items to player at game start.

```csharp
using SoulsFormats;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Adds key items to player's starting inventory.
/// All items given at start to prevent softlocks.
/// </summary>
public class StartingItemsWriter
{
    // Item IDs from FogRando fog.txt (format: type 3 = goods/key items)
    // Source: reference/fogrando-data/fog.txt lines 3258-3358
    private static readonly Dictionary<string, int> KeyItems = new()
    {
        // Dungeon access keys
        ["academy_glintstone_key"] = 8109,      // Raya Lucaria
        ["rusty_key"] = 8010,                   // Stormveil
        ["discarded_palace_key"] = 8199,        // Raya Lucaria locked area
        ["drawing_room_key"] = 8134,            // Volcano Manor

        // Medallions for lifts
        ["dectus_medallion_left"] = 8105,       // Grand Lift of Dectus
        ["dectus_medallion_right"] = 8106,      // Grand Lift of Dectus
        ["rold_medallion"] = 8107,              // Grand Lift of Rold
        ["haligtree_secret_medallion_left"] = 8175,   // Consecrated Snowfield
        ["haligtree_secret_medallion_right"] = 8176,  // Consecrated Snowfield

        // Quest/progression items
        ["carian_inverted_statue"] = 8111,      // Carian Study Hall
        ["cursemark_of_death"] = 8191,          // Ranni quest
        ["dark_moon_ring"] = 8121,              // Ranni quest
        ["imbued_sword_key"] = 8186,            // Four Belfries
        ["pureblood_knights_medal"] = 2160,     // Mohgwyn teleport

        // DLC keys (v2)
        ["messmers_kindling"] = 2008021,        // DLC
        ["o_mother"] = 2009004,                 // DLC
        ["well_depths_key"] = 2008004,          // DLC - Belurat
        ["gaol_upper_level_key"] = 2008005,     // DLC - Charo's Gaol
        ["gaol_lower_level_key"] = 2008006,     // DLC - Charo's Gaol
        ["hole_laden_necklace"] = 2008008,      // DLC
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

## Task 3.8: ModWriter.cs (Orchestrator)

Main class that coordinates all writers.

```csharp
using SoulsFormats;
using SoulsIds;
using SpeedFogWriter.Models;
using SpeedFogWriter.Helpers;
using System.Text.Json;

namespace SpeedFogWriter.Writers;

/// <summary>
/// Main orchestrator for mod file generation.
/// </summary>
public class ModWriter
{
    private readonly string _gameDir;
    private readonly string _outputDir;
    private readonly string _dataDir;
    private readonly SpeedFogGraph _graph;

    private ParamDictionary? _params;
    private Dictionary<string, EMEVD>? _emevds;
    private FogDataFile? _fogPositions;
    private ScalingWriter? _scalingWriter;
    private List<FogGateEvent>? _fogGates;

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
        Console.WriteLine("Loading game data...");
        LoadGameData();

        Console.WriteLine("Loading fog position data...");
        LoadFogData();

        Console.WriteLine("Generating scaling effects...");
        GenerateScaling();

        Console.WriteLine("Generating fog gates...");
        GenerateFogGates();

        Console.WriteLine("Adding starting items...");
        AddStartingItems();

        Console.WriteLine("Writing output files...");
        WriteOutput();

        Console.WriteLine("Done!");
        PrintSummary();
    }

    private void LoadGameData()
    {
        // Load game params using SoulsIds GameEditor
        var regulationPath = Path.Combine(_gameDir, "regulation.bin");
        if (!File.Exists(regulationPath))
            throw new FileNotFoundException($"regulation.bin not found: {regulationPath}");

        var editor = new GameEditor(GameSpec.FromGame.ER);
        _params = new ParamDictionary
        {
            Defs = editor.LoadDefs(),
            Inner = editor.LoadParams(regulationPath, null)
        };

        // Load EMEVD files
        _emevds = new Dictionary<string, EMEVD>();
        var eventDir = Path.Combine(_gameDir, "event");

        // Load common.emevd (always needed)
        var commonPath = Path.Combine(eventDir, "common.emevd.dcx");
        if (File.Exists(commonPath))
        {
            _emevds["common"] = SoulsFile<EMEVD>.Read(commonPath);
        }

        // Load map EMEVDs for each unique map in the graph
        var maps = _graph.AllNodes()
            .SelectMany(n => n.Zones)
            .Select(z => GetMapForZone(z))
            .Where(m => m != null)
            .Distinct()
            .ToList();

        foreach (var map in maps)
        {
            var mapEventPath = Path.Combine(eventDir, $"{map}.emevd.dcx");
            if (File.Exists(mapEventPath))
            {
                _emevds[map!] = SoulsFile<EMEVD>.Read(mapEventPath);
            }
        }

        Console.WriteLine($"  Loaded {_emevds.Count} EMEVD files");
    }

    private string? GetMapForZone(string zoneId)
    {
        // This needs zone→map mapping
        // For now, lookup in fog_data.json when loaded
        return _fogPositions?.Fogs.Values
            .FirstOrDefault(f => f.Zone == zoneId)?.Map;
    }

    private void LoadFogData()
    {
        var fogPosPath = Path.Combine(_dataDir, "fog_data.json");
        if (!File.Exists(fogPosPath))
            throw new FileNotFoundException($"fog_data.json not found: {fogPosPath}");

        _fogPositions = FogDataFile.Load(fogPosPath);
        Console.WriteLine($"  Loaded {_fogPositions.Fogs.Count} fog positions");
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
        if (_fogPositions == null) throw new InvalidOperationException("Fog positions not loaded");

        var fogWriter = new FogGateWriter(_fogPositions);
        _fogGates = fogWriter.CreateFogGates(_graph);

        Console.WriteLine($"  Created {_fogGates.Count} fog gate events");

        // Write to EMEVD
        if (_emevds == null || _scalingWriter == null)
            throw new InvalidOperationException("EMEVD or scaling not initialized");

        foreach (var fogGate in _fogGates)
        {
            var scalingEffect = _scalingWriter.GetTransitionEffect(
                fogGate.SourceTier,
                fogGate.TargetTier
            );

            // Add event to appropriate map EMEVD
            var mapEmevd = _emevds.GetValueOrDefault(fogGate.SourceMap)
                ?? _emevds.GetValueOrDefault("common");

            if (mapEmevd != null)
            {
                fogWriter.WriteToEmevd(mapEmevd, fogGate, scalingEffect);
            }
        }
    }

    private void AddStartingItems()
    {
        if (_params == null) throw new InvalidOperationException("Params not loaded");

        var itemWriter = new StartingItemsWriter(_params);
        itemWriter.AddStoneswordKeys(10);
        // Add other key items as needed based on which clusters are in the graph
    }

    private void WriteOutput()
    {
        if (_params == null || _emevds == null)
            throw new InvalidOperationException("Data not loaded");

        var modDir = Path.Combine(_outputDir, "mods", "speedfog");
        Directory.CreateDirectory(modDir);

        // Write params (regulation.bin)
        var paramDir = Path.Combine(modDir, "param", "gameparam");
        Directory.CreateDirectory(paramDir);

        var editor = new GameEditor(GameSpec.FromGame.ER);
        var regulationOut = Path.Combine(paramDir, "regulation.bin");
        editor.OverrideBndRel<PARAM>(
            Path.Combine(_gameDir, "regulation.bin"),
            regulationOut,
            _params.Inner,
            f => f.Write(),
            null,
            DCX.Type.DCX_DFLT_11000_44_9
        );
        Console.WriteLine($"  Written: {regulationOut}");

        // Write EMEVDs
        var eventDir = Path.Combine(modDir, "event");
        Directory.CreateDirectory(eventDir);

        foreach (var (name, emevd) in _emevds)
        {
            var emevdOut = Path.Combine(eventDir, $"{name}.emevd.dcx");
            emevd.Write(emevdOut, DCX.Type.DCX_DFLT_11000_44_9);
            Console.WriteLine($"  Written: {emevdOut}");
        }

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
| `ZoneMap` | Lookup in `fog_data.json` |
| `entries`/`exits` (node IDs) | `entry_fog`/`exit_fogs` (fog IDs) |
| No explicit edges | `edges` array with fog_id |

The C# writer uses edges and fog_data to determine:
- Where to place fog walls (source cluster exit fog position)
- Where to warp (target cluster entry fog position)

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

### Task 3.0 (Prerequisite: fog_data.json)
- [ ] Python script `tools/extract_fog_data.py` exists
- [ ] Script parses fog.txt Entrances section
- [ ] Script outputs `writer/data/fog_data.json`
- [ ] All fog_ids from clusters.json are present in fog_data.json

### Task 3.1 (Setup)
- [ ] Project builds with `dotnet build`
- [ ] SoulsFormats loads correctly

### Task 3.2 (Models)
- [ ] `SpeedFogGraph.Load()` parses graph.json correctly
- [ ] All nodes and edges accessible
- [ ] `FogDataFile.Load()` parses fog_data.json correctly
- [ ] Nodes contain correct cluster data (zones list, entry_fog, exit_fogs)

### Task 3.3 (Event Templates)
- [ ] `speedfog-events.yaml` loads correctly
- [ ] `EventBuilder` can parse template commands
- [ ] Generated EMEVD instructions are valid

### Task 3.4 (Scaling)
- [ ] SpEffect entries created for tier transitions
- [ ] Scaling factors are reasonable (no 100x damage)

### Task 3.5-3.6 (Fog Gates & Warps)
- [ ] Fog gate events created for all edges in the graph
- [ ] Each edge's fog_id is found in fog_data.json
- [ ] Events compile without EMEVD errors

### Task 3.7 (Starting Items)
- [ ] Key items added to starting inventory
- [ ] Player doesn't get softlocked by missing keys

### Task 3.8-3.9 (Integration)
- [ ] Full pipeline works: graph.json + fog_data.json → mod files
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
