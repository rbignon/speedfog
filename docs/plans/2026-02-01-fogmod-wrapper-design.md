# FogMod Wrapper Design

## Overview

Replace SpeedFogWriter with a thin wrapper that calls FogMod.dll directly, injecting our Python-generated graph connections instead of FogMod's randomization logic.

## Motivation

- Reuse 100% of FogMod's writer (EMEVD, params, MSB, scaling)
- Less C# code to maintain
- Automatic compatibility with FogMod's game data handling

## Architecture

```
Python (speedfog)                    C# (FogModWrapper)
─────────────────                    ──────────────────
config.toml
    │
speedfog_core
    │
graph.json ─────────────────────────► Load graph.json
                                      Load FogMod.dll
                                      FogMod loads fog.txt → Graph
                                      Inject our connections
                                      GameDataWriterE.Write()
                                          │
                                      output/mod/
```

**Principle**: Reuse 100% of FogMod's writer, replace only the graph connection logic.

## graph.json Format (v2)

```json
{
  "version": "2.0",
  "seed": 212559448,

  "options": {
    "scale": true,
    "newgraces": true,
    "sombermode": true,
    "physick": true
  },

  "connections": [
    {
      "exit_area": "chapel_start",
      "exit_gate": "m10_01_00_00_AEG099_001_9000",
      "entrance_area": "gelmir_wyndhamcatacombs",
      "entrance_gate": "m31_05_00_00_AEG099_230_9001"
    },
    {
      "exit_area": "chapel_start",
      "exit_gate": "m12_05_00_00_12052021",
      "entrance_area": "mohgwyn",
      "entrance_gate": "m12_05_00_00_12052021"
    }
  ],

  "area_tiers": {
    "chapel_start": 1,
    "gelmir_wyndhamcatacombs": 3,
    "mohgwyn": 15
  }
}
```

### Fields

| Field | Description |
|-------|-------------|
| `version` | Format version for compatibility |
| `seed` | Generation seed for reproducibility |
| `options` | FogMod options to enable |
| `connections` | List of exit→entrance connections |
| `area_tiers` | Zone→tier mapping for enemy scaling |

### Gate Naming Convention

Gates use FogMod's FullName format: `{map}_{name}`

Examples:
- Fog gate: `m10_01_00_00_AEG099_001_9000`
- Warp: `m12_05_00_00_12052021`
- Medal warp: `m12_05_00_00_12052021` (ASide.Area = chapel_start)

## C# Wrapper Structure

```
writer/
├── FogModWrapper/
│   ├── FogModWrapper.csproj
│   ├── Program.cs              # CLI entry point
│   ├── GraphLoader.cs          # Load and parse graph.json
│   └── ConnectionInjector.cs   # Inject connections into Graph
└── lib/
    ├── FogMod.dll
    ├── SoulsFormats.dll
    ├── SoulsIds.dll
    ├── YamlDotNet.dll
    └── Newtonsoft.Json.dll
```

### Usage

```bash
FogModWrapper.exe <graph.json> --game-dir <path> --data-dir <path> -o <output>
```

### Main Flow (Program.cs)

```csharp
public static void Main(string[] args)
{
    // 1. Parse arguments
    var graphPath = args[0];
    var gameDir = GetArg(args, "--game-dir");
    var dataDir = GetArg(args, "--data-dir");
    var outDir = GetArg(args, "-o");

    // 2. Load our graph.json
    var graphData = GraphLoader.Load(graphPath);

    // 3. Build FogMod options
    var opt = new RandomizerOptions(GameSpec.FromGame.ER);
    opt.Seed = graphData.Seed;
    foreach (var (key, value) in graphData.Options)
        opt[key] = value;

    // 4. Load FogMod data (fog.txt, fogevents.txt, etc.)
    var ann = LoadAnnotationData(dataDir);
    var events = new Events(dataDir + "/er-common.emedf.json", true, true);
    var eventConfig = LoadEventConfig(dataDir, events);

    // 5. Build FogMod Graph (unconnected nodes/edges)
    var graph = new Graph();
    graph.Construct(opt, ann);

    // 6. Inject OUR connections (replaces GraphConnector.Connect())
    ConnectionInjector.Inject(graph, graphData.Connections);

    // 7. Apply tiers for scaling
    ApplyAreaTiers(graph, graphData.AreaTiers);

    // 8. Call FogMod writer
    var writer = new GameDataWriterE();
    writer.Write(opt, ann, graph, null, outDir, events, eventConfig, Console.WriteLine);
}
```

### ConnectionInjector

```csharp
public static class ConnectionInjector
{
    public static void Inject(Graph graph, List<Connection> connections)
    {
        foreach (var conn in connections)
        {
            // Find exit edge
            var exitNode = graph.Nodes[conn.ExitArea];
            var exitEdge = exitNode.To.Find(e => e.Name == conn.ExitGate);

            if (exitEdge == null)
                throw new Exception($"Exit edge not found: {conn.ExitArea} / {conn.ExitGate}");

            // Find entrance edge (via Pair of destination's exit)
            var entranceNode = graph.Nodes[conn.EntranceArea];
            var destExitEdge = entranceNode.To.Find(e => e.Name == conn.EntranceGate);

            if (destExitEdge == null)
                throw new Exception($"Entrance edge not found: {conn.EntranceArea} / {conn.EntranceGate}");

            var entranceEdge = destExitEdge.Pair;

            // Connect
            graph.Connect(exitEdge, entranceEdge);

            Console.WriteLine($"Connected: {conn.ExitArea} --[{conn.ExitGate}]--> {conn.EntranceArea}");
        }
    }
}
```

**Important**: Each fog gate has 2 edges (Exit in `node.To`, Entrance in `node.From`). We find the Exit on the destination side, then use `.Pair` to get the Entrance.

## Python Modifications

### Files to Modify

```
core/speedfog_core/
├── graph_serializer.py   # New output format
└── fog_registry.py       # Add FullNames
```

### fog_registry.py

```python
def get_fullname(fog_id: str, zone: str, fog_data: dict) -> str:
    """Convert a fog_id to FogMod FullName."""
    fog = fog_data.get(fog_id)
    if fog:
        return f"{fog['map']}_{fog_id}"
    # Fallback for numeric IDs (warps)
    return f"{get_warp_map(fog_id)}_{fog_id}"
```

### graph_serializer.py

```python
def serialize_graph_v2(dag, options: dict) -> dict:
    """Serialize DAG to v2 format for FogModWrapper."""
    connections = []
    area_tiers = {}

    for edge in dag.edges:
        connections.append({
            "exit_area": edge.source_zone,
            "exit_gate": get_fullname(edge.exit_fog, ...),
            "entrance_area": edge.target_zone,
            "entrance_gate": get_fullname(edge.entry_fog, ...)
        })

    for node in dag.nodes:
        for zone in node.zones:
            area_tiers[zone] = node.tier

    return {
        "version": "2.0",
        "seed": dag.seed,
        "options": options,
        "connections": connections,
        "area_tiers": area_tiers
    }
```

## Implementation Steps

| # | Task | Effort |
|---|------|--------|
| 1 | Create FogModWrapper project + dependencies | Low |
| 2 | Implement CLI + GraphLoader | Low |
| 3 | Implement ConnectionInjector | Medium |
| 4 | Handle area_tiers for scaling | Medium |
| 5 | Modify Python: FullNames + v2 format | Medium |
| 6 | Integration tests | Medium |

## Identified Risks

1. **Graph.AreaTiers**: Verify how FogMod uses it for scaling
2. **Conditional warps (Medal)**: Verify FogMod handles them correctly
3. **Unsupported options**: Identify which are relevant for SpeedFog

## References

- FogMod source: `~/src/games/ER/fog/src-decompiled/FogMod/`
- FogMod DLLs: `~/src/games/ER/fog/extracted/`
- FogMod data: `data/fog.txt`, `data/fogevents.txt`
- Current SpeedFogWriter: `writer/SpeedFogWriter/` (to be archived)
