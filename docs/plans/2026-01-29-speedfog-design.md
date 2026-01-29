# SpeedFog - Design Document

**Date**: 2026-01-29
**Status**: Approved
**Author**: Roger + Claude

## Overview

SpeedFog is an Elden Ring mod that generates short runs (~1h) with a randomized zone structure in the form of a DAG (Directed Acyclic Graph). Unlike FogRando which randomizes the entire world, SpeedFog creates a controlled path with:

- A single starting point (Chapel of Anticipation)
- A single ending point (Radagon/Elden Beast)
- Balanced parallel paths (no disadvantaged branch)
- No dead ends (all paths lead to the end)

### User Workflow

```
1. Run Enemy/Item Randomizer (existing mod)
         ↓
2. Run SpeedFog with config.toml
         ↓
3. SpeedFog generates mod files in output/
         ↓
4. Copy to ModEngine folder → Play
```

## Architecture

```
┌─────────────────────────────────────────┐
│  speedfog-core (Python)                 │
│  - Parse config.toml                    │
│  - Parse zones.toml                     │
│  - Generate DAG                         │
│  - Output: graph.json                   │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  speedfog-writer (C#)                   │
│  - Read graph.json                      │
│  - Use SoulsFormats                     │
│  - Write EMEVD/MSB/PARAM files          │
└─────────────────────────────────────────┘
```

### Project Structure

```
speedfog/
├── core/                          # Python
│   ├── speedfog_core/
│   │   ├── __init__.py
│   │   ├── config.py              # Parse config.toml
│   │   ├── zones.py               # Parse zones.toml
│   │   ├── dag.py                 # DAG generation
│   │   ├── balance.py             # Path balancing
│   │   └── output.py              # Generate graph.json
│   ├── config.toml                # User config
│   ├── zones.toml                 # Zone data
│   └── main.py                    # CLI entry point
│
├── writer/                        # C#
│   ├── SpeedFogWriter/
│   │   ├── Program.cs             # Entry point
│   │   ├── GraphReader.cs         # Read graph.json
│   │   ├── FogGateWriter.cs       # Adapted from FogRando
│   │   ├── WarpWriter.cs          # Adapted from FogRando
│   │   ├── ScalingWriter.cs       # Adapted from EldenScaling.cs
│   │   └── EventBuilder.cs        # Adapted from EventConfig.cs
│   └── SpeedFogWriter.csproj
│
├── tools/
│   └── convert_fogrando.py        # Zone conversion script
│
├── docs/
│   └── plans/
│       └── 2026-01-29-speedfog-design.md
│
└── output/                        # Generated files
    └── mods/speedfog/
```

## Configuration

### config.toml (User Parameters)

```toml
[run]
seed = 12345

[budget]
total_weight = 30              # Target total weight per path
tolerance = 5                  # Max allowed deviation (25-35)

[requirements]
legacy_dungeons = 1            # Minimum legacy dungeons per path
bosses = 5                     # Minimum bosses before Radagon
mini_dungeons = 5              # Minimum mini-dungeons total

[structure]
max_parallel_paths = 3         # Max parallel branches
min_layers = 6                 # Minimum layers
max_layers = 10                # Maximum layers
split_probability = 0.4        # Split probability per layer
merge_probability = 0.3        # Merge probability

[paths]
game_dir = "C:/Program Files/Steam/steamapps/common/ELDEN RING/Game"
output_dir = "./output"
enemy_randomizer_dir = "./mods/randomizer"
```

## Zone Data

### Categories

| Category | Description | Typical Weight |
|----------|-------------|----------------|
| `legacy_dungeon` | Stormveil, Raya Lucaria, etc. | 10-20 |
| `catacomb_short` | Short catacombs (~5 min) | 3-4 |
| `catacomb_medium` | Medium catacombs (~8 min) | 5-7 |
| `catacomb_long` | Long catacombs (~12 min) | 8-10 |
| `cave_short` | Short caves | 3-4 |
| `cave_medium` | Medium caves | 5-7 |
| `cave_long` | Long caves | 8-10 |
| `tunnel` | Mine tunnels | 3-6 |
| `gaol` | Evergaols | 2-4 |
| `boss_arena` | Standalone boss arenas | 2-5 |

### zones.toml Format

```toml
[[zones]]
id = "stormveil_castle"
map = "m10_00_00_00"
name = "Stormveil Castle"
type = "legacy_dungeon"
weight = 15
entrances = ["stormveil_main_gate", "stormveil_cliffside"]
exits = ["stormveil_godrick_arena", "stormveil_to_liurnia"]
boss = "godrick"

[[zones]]
id = "murkwater_catacombs"
map = "m30_00_00_00"
name = "Murkwater Catacombs"
type = "catacomb_short"
weight = 4
entrances = ["murkwater_entrance"]
exits = ["murkwater_boss"]
boss = "grave_warden_duelist"
```

### Excluded Zones (v1)

Zones excluded due to complexity:

```toml
[[excluded]]
reason = "coffin_oneway"
zones = ["ainsel_river", "deeproot_depths", "lake_of_rot"]

[[excluded]]
reason = "complex_internal_structure"
zones = ["subterranean_shunning_grounds", "leyndell_sewers"]
```

## DAG Generation Algorithm

### Layer-Based Construction with Budget

```
Layer 0 (Start)     : Chapel of Anticipation
                              │
Layer 1             :    ┌───┴───┐
                         A       B          (initial split)
                         │       │
Layer 2             :    C   ┌───┤
                         │   D   E          (free splits/merges)
                         └───┼───┘
Layer 3             :        F              (merge)
                             │
Layer N (End)       :    Radagon
```

### Algorithm (Pseudo-code)

```python
def generate_dag(config, zones):
    dag = DAG()
    rng = Random(config.seed)

    # 1. Initialize with starting point
    start = dag.add_node(layer=0, zone=zones.get("chapel_of_anticipation"))

    # 2. Build layer by layer
    current_layer = [start]
    layer_index = 1

    while not should_end(dag, config):
        next_layer = []

        for node in current_layer:
            action = decide_action(rng, dag, config)

            if action == SPLIT:
                child1 = create_node(rng, zones, layer_index)
                child2 = create_node(rng, zones, layer_index)
                dag.connect(node, child1)
                dag.connect(node, child2)
                next_layer.extend([child1, child2])

            elif action == CONTINUE:
                child = create_node(rng, zones, layer_index)
                dag.connect(node, child)
                next_layer.append(child)

            elif action == MERGE:
                node.pending_merge = True
                next_layer.append(node)

        resolve_merges(next_layer)
        current_layer = next_layer
        layer_index += 1

    # 3. Converge to Radagon
    radagon = dag.add_node(zone=zones.get("radagon_arena"))
    for node in current_layer:
        dag.connect(node, radagon)

    # 4. Validate and balance
    validate_requirements(dag, config)
    balance_paths(dag, config.budget)

    return dag
```

### Path Balancing

Each possible path from start to end must have a total weight within `[budget - tolerance, budget + tolerance]`. The algorithm:

1. Enumerate all paths
2. For paths below budget: insert intermediate zones or swap for heavier ones
3. For paths above budget: swap heavy zones for lighter ones
4. Re-validate after balancing

## Intermediate Format (graph.json)

```json
{
  "seed": 12345,
  "layers": [
    {
      "index": 0,
      "tier": 1,
      "nodes": [
        {
          "id": "start",
          "zone": "chapel_of_anticipation",
          "exits": ["node_1a", "node_1b"]
        }
      ]
    },
    {
      "index": 1,
      "tier": 5,
      "nodes": [
        {
          "id": "node_1a",
          "zone": "murkwater_catacombs",
          "entries": ["start"],
          "exits": ["node_2a"]
        },
        {
          "id": "node_1b",
          "zone": "tombsward_catacombs",
          "entries": ["start"],
          "exits": ["node_2b"]
        }
      ]
    }
  ],
  "final": {
    "id": "radagon",
    "zone": "elden_throne",
    "entries": ["node_5a", "node_5b"]
  }
}
```

## C# Writer

### Responsibilities

1. **Create custom fog gates** between zones (EMEVD events)
2. **Create warps** (teleportation when crossing fog)
3. **Apply enemy scaling** based on layer tier
4. **Give key items** at spawn

### Files Adapted from FogRando

| FogRando Source | SpeedFog Target |
|-----------------|-----------------|
| `EldenScaling.cs` | `ScalingWriter.cs` |
| `GameDataWriterE.cs` | `FogGateWriter.cs`, `WarpWriter.cs` |
| `EventConfig.cs` | `EventBuilder.cs` |
| `AnnotationData.cs` | `GraphReader.cs` |

### Simplified Scaling

```csharp
public static int LayerToTier(int layerIndex, int totalLayers)
{
    // Linear progression from tier 1 to tier 28
    float progress = (float)layerIndex / totalLayers;
    return (int)(1 + progress * 27);
}
```

## Key Decisions

| Aspect | Decision |
|--------|----------|
| **Name** | SpeedFog |
| **Architecture** | Python (core) + C# (writer) |
| **Config format** | TOML |
| **Zone data** | Converted once from FogRando |
| **DAG structure** | Layers with free splits/merges |
| **Balancing** | Budget per path with tolerance |
| **Scaling** | Adapted from FogRando (simplified tiers) |
| **One-ways** | Excluded for v1 |
| **Key items** | All given at start |
| **Target duration** | ~1h (configurable) |
| **Start point** | Chapel of Anticipation |
| **End point** | Radagon/Elden Beast |

## Implementation Roadmap

### Phase 1: Foundations (Python)

- [ ] Create `speedfog/` repo with base structure
- [ ] Script `convert_fogrando.py`: extract zones from `fog.txt`
- [ ] Define `zones.toml` with categories and initial weights
- [ ] Parse `config.toml`
- [ ] Parse `zones.toml`

### Phase 2: DAG Generation (Python)

- [ ] Implement `DAG` structure (nodes, edges, layers)
- [ ] Layer-based generation algorithm
- [ ] Split/merge logic with probabilities
- [ ] Path balancing (budget per branch)
- [ ] Constraint validation (min bosses, legacy dungeons, etc.)
- [ ] Export `graph.json`

### Phase 3: C# Writer (Minimal Viable)

- [ ] Setup C# project + SoulsFormats reference
- [ ] Parse `graph.json` → C# structures
- [ ] Adapt `EldenScaling.cs` → `ScalingWriter.cs`
- [ ] Adapt fog gate creation from FogRando
- [ ] Adapt warp events from FogRando
- [ ] Starting items (key items at spawn)

### Phase 4: Integration & Testing

- [ ] End-to-end test: config → generation → mod files
- [ ] In-game test with ModEngine 2
- [ ] Calibrate zone weights (~1h target)
- [ ] Fix bugs found in-game

### Phase 5: Polish (v1.1+)

- [ ] Spoiler log (display generated graph)
- [ ] DLC zone support
- [ ] Additional modes (ultra-short 30min, long 2h)
- [ ] DAG visualization (graphviz export)
