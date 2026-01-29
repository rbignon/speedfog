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
│   │   ├── planner.py             # Layer planning
│   │   ├── dag.py                 # DAG data structures
│   │   ├── generator.py           # DAG generation
│   │   ├── balance.py             # Path balancing
│   │   └── output.py              # Generate graph.json
│   ├── config.toml                # User config
│   ├── zones.toml                 # Zone gameplay data
│   └── main.py                    # CLI entry point
│
├── data/                          # Static game data
│   └── zone_warps.json            # Fog gate positions (extracted from game)
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
fog_count = 3                # Can be used for split/merge
boss = "godrick"

[[zones]]
id = "murkwater_catacombs"
map = "m30_00_00_00"
name = "Murkwater Catacombs"
type = "catacomb_short"
weight = 4
fog_count = 2                # Linear passage only
boss = "grave_warden_duelist"
```

Key fields:
- `fog_count`: Number of fog gates in the zone (2 = linear, 3 = can split/merge)

### Data Separation: zones.toml vs zone_warps.json

Zone data is split into two files with different purposes:

| File | Purpose | Edited by | Content |
|------|---------|-----------|---------|
| `zones.toml` | Gameplay metadata | Human (manual) | type, weight, fog_count |
| `data/zone_warps.json` | Technical warp data | Script (extracted) | fog positions, entity IDs |

**zones.toml** contains data that requires human judgment:
- Zone type classification
- Weight estimation (gameplay duration/difficulty)
- Fog count (how many fog gates)

**zone_warps.json** contains technical data extracted from game files:
- Fog gate positions (Vector3)
- Entity IDs for EMEVD events
- Warp destination coordinates

This separation ensures:
1. `zones.toml` stays readable and manually editable
2. Technical data can be regenerated from game files
3. Clear ownership: gameplay decisions vs extracted data

**Validation**: A script verifies that every zone in `zones.toml` has corresponding warp data in `zone_warps.json`.

### zone_warps.json Format

```json
{
  "stormveil_castle": {
    "map": "m10_00_00_00",
    "fogs": [
      {
        "id": "stormveil_main_gate",
        "position": [123.4, 56.7, 89.0],
        "rotation": [0, 180, 0],
        "entity_id": 10001800
      },
      {
        "id": "stormveil_to_liurnia",
        "position": [234.5, 67.8, 90.1],
        "rotation": [0, 90, 0],
        "entity_id": 10001801
      }
    ]
  }
}
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

### Uniform Layer Design

Each layer has a **uniform zone type** across all branches. This ensures competitive fairness: all players face the same type of challenge at each step, regardless of which branch they chose.

```
Layer 0 (Start)     : Chapel of Anticipation
                              │
Layer 1 [mini]      :    ┌────┴────┐
                     Catacomb_A  Catacomb_B    (same type, similar weight)
                         │           │
Layer 2 [legacy]    :    ├───────────┤
                      Stormveil   Raya Lucaria  (same type, similar weight)
                         │           │
Layer 3 [boss]      :    └─────┬─────┘
                            Boss_Arena          (merge via 3-fog zone)
                               │
Layer N (End)       :      Radagon
```

### Zone Fog Geometry

Splits and merges are determined by zone geometry (number of fog gates):

| Fog Count | Behavior |
|-----------|----------|
| 2 fogs | Linear passage (1 entrance, 1 exit) |
| 3 fogs | Can be split (1→2) or merge (2→1) |

### Algorithm (Pseudo-code)

```python
def generate_dag(config, zones):
    dag = DAG()
    rng = Random(config.seed)

    # 1. Plan layer sequence (types and structure)
    layer_plan = plan_layers(config, rng)
    # Example: [START, MINI, LEGACY, BOSS, MINI, MERGE, BOSS, END]

    # 2. Initialize with starting point
    start = dag.add_node(layer=0, zone=zones.get("chapel_of_anticipation"))
    current_branches = [start]

    # 3. Build layer by layer
    for layer_index, layer_spec in enumerate(layer_plan[1:-1], start=1):
        zone_type = layer_spec.type
        structure = layer_spec.structure  # CONTINUE, SPLIT, or MERGE

        if structure == SPLIT:
            # Select a 3-fog zone for the split point
            # Then create 2 branches with zones of same type/weight
            ...
        elif structure == MERGE:
            # Select a 3-fog zone as merge destination
            # Connect all current branches to it
            ...
        else:  # CONTINUE
            # For each branch, select a zone of the required type
            # All zones in this layer must have similar weights
            next_branches = []
            target_weight = select_target_weight(zone_type, rng)
            for branch in current_branches:
                zone = select_zone(zones, zone_type, target_weight, tolerance=2)
                node = dag.add_node(layer=layer_index, zone=zone)
                dag.connect(branch, node)
                next_branches.append(node)
            current_branches = next_branches

    # 4. Converge to Radagon
    radagon = dag.add_node(zone=zones.get("radagon_arena"))
    for node in current_branches:
        dag.connect(node, radagon)

    # 5. Validate
    validate_requirements(dag, config)
    validate_balance(dag, config.budget)

    return dag
```

### Layer Planning

The layer sequence is planned upfront to ensure requirements are met:

1. Determine total layers (between `min_layers` and `max_layers`)
2. Distribute required zone types (legacy dungeons, bosses, mini-dungeons)
3. Plan split/merge points based on `max_parallel_paths`
4. Verify total weight budget is achievable

### Path Balancing

With uniform layers, balancing is simpler:
- All branches in a layer have the same zone type
- Zones are selected with similar weights (within small tolerance)
- Total path weights naturally converge

Post-generation validation ensures all paths are within `[budget - tolerance, budget + tolerance]`.

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
