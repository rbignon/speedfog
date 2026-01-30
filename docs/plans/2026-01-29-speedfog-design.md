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
│  generate_clusters.py (one-time)        │
│  - Parse fog.txt (FogRando)             │
│  - Output: clusters.json                │
└──────────────────┬──────────────────────┘
                   │
┌──────────────────▼──────────────────────┐
│  speedfog-core (Python)                 │
│  - Parse config.toml                    │
│  - Parse clusters.json                  │
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
│   │   ├── clusters.py            # Parse clusters.json
│   │   ├── planner.py             # Layer planning
│   │   ├── dag.py                 # DAG data structures
│   │   ├── generator.py           # DAG generation
│   │   ├── balance.py             # Path balancing
│   │   └── output.py              # Generate graph.json
│   ├── data/
│   │   ├── clusters.json          # Pre-computed clusters (generated)
│   │   └── zone_metadata.toml     # Zone weights and overrides
│   ├── config.toml                # User config
│   └── main.py                    # CLI entry point
│
├── tools/
│   └── generate_clusters.py       # Generate clusters.json from fog.txt
│
├── writer/                        # C#
│   ├── data/
│   │   └── speedfog-events.yaml   # Event templates (readable YAML)
│   ├── SpeedFogWriter/
│   │   ├── Program.cs             # Entry point
│   │   ├── GraphReader.cs         # Read graph.json
│   │   ├── EventBuilder.cs        # Build EMEVD from YAML templates
│   │   ├── FogGateWriter.cs       # Adapted from FogRando
│   │   ├── WarpWriter.cs          # Adapted from FogRando
│   │   └── ScalingWriter.cs       # Adapted from EldenScaling.cs
│   └── SpeedFogWriter.csproj
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
# Seed for randomization. Behavior:
# - seed = 0 or omitted: auto-reroll until valid DAG found, display used seed
# - seed = 12345: force this seed, error if DAG generation fails
seed = 0

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

# Optional: merge with Item/Enemy Randomizer output
# If specified, SpeedFog loads EMEVD from this mod as base
# If omitted, uses vanilla game files
# randomizer_dir = "./mods/randomizer"
```

## Zone Data

### Clusters vs Zones

SpeedFog utilise des **clusters** plutôt que des zones individuelles. Un cluster est un groupe de zones connectées par des world connections internes (depuis fog.txt de FogRando). Une fois qu'un joueur entre dans un cluster, il a accès à toutes les zones du cluster.

Voir `docs/plans/generate-clusters-spec.md` pour la spécification complète.

### Categories

| Category | Description | Typical Weight |
|----------|-------------|----------------|
| `legacy_dungeon` | Stormveil, Raya Lucaria, etc. | 10-20 |
| `catacomb` | Catacombs | 4 |
| `cave` | Caves | 4 |
| `tunnel` | Mine tunnels | 4 |
| `gaol` | Evergaols | 4 |
| `boss_arena` | Standalone boss arenas | 2 |

### Data Files

| File | Purpose | Generated by |
|------|---------|--------------|
| `clusters.json` | Pre-computed clusters with entry/exit fogs | `generate_clusters.py` |
| `zone_metadata.toml` | Zone weights (manual overrides) | Human |

### clusters.json Format

```json
{
  "version": "1.0",
  "generated_from": "fog.txt",
  "clusters": [
    {
      "id": "stormveil_start_c1d3",
      "zones": ["stormveil_start", "stormveil"],
      "type": "legacy_dungeon",
      "weight": 20,
      "entry_fogs": [
        {"fog_id": "margit_front", "zone": "stormveil_start"},
        {"fog_id": "godrick_front", "zone": "stormveil"}
      ],
      "exit_fogs": [
        {"fog_id": "margit_front", "zone": "stormveil_start"},
        {"fog_id": "godrick_front", "zone": "stormveil"},
        {"fog_id": "abduction_volcano", "zone": "stormveil", "unique": true}
      ]
    }
  ]
}
```

**entry_fogs** : Fogs par lesquels on peut entrer dans le cluster.
**exit_fogs** : Fogs par lesquels on peut sortir du cluster (inclut les entry_fogs bidirectionnels + les fogs `unique`).

### zone_metadata.toml Format

```toml
# Weights par défaut selon le type
[defaults]
legacy_dungeon = 10
catacomb = 4
cave = 4
tunnel = 4
gaol = 4
boss_arena = 2

# Overrides par zone
[zones.stormveil]
weight = 15

[zones.academy]
weight = 12
```

### Excluded Zones (v1)

Zones exclues automatiquement par `generate_clusters.py` :
- Overworld (tags: `overworld`)
- DLC (tags: `dlc`)
- Zones triviales sans fogs

## DAG Generation Algorithm

### Uniform Layer Design

Each layer has a **uniform cluster type** across all branches. This ensures competitive fairness: all players face the same type of challenge at each step, regardless of which branch they chose.

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
                            Boss_Arena          (merge via 3+ exit cluster)
                               │
Layer N (End)       :      Radagon
```

### Cluster Selection Constraints

**Règle fondamentale** : Une fois qu'un cluster est sélectionné, toutes ses zones sont "consommées" et ne peuvent plus être utilisées.

| Constraint | Description |
|------------|-------------|
| **Inter-layer** | Un cluster ne peut pas inclure de zone déjà utilisée dans un layer précédent |
| **Intra-layer** | Sur un même layer, deux branches ne peuvent pas utiliser des clusters partageant une zone |
| **Entry removal** | L'entry_fog utilisé pour entrer dans un cluster est retiré de ses exit_fogs disponibles |

### Cluster Fog Geometry

Les splits et merges sont déterminés par le nombre d'exits disponibles après sélection :

| Available Exits | Behavior |
|-----------------|----------|
| 1 exit | **Traversal** : passage linéaire (1 branche → 1 branche) |
| 2 exits | **Split** : une branche devient deux (1 → 2) |
| 3+ exits | **Multi-split** ou **Merge point** selon le contexte |

**Calcul des exits disponibles** :
```
exits_disponibles = cluster.exit_fogs - {entry_fog_utilisé}
```

Si l'entry_fog est bidirectionnel, il apparaît dans exit_fogs et doit être retiré.
Si l'entry_fog est `unique` (unidirectionnel), il n'apparaît pas dans exit_fogs.

### Algorithm (Pseudo-code)

```python
def generate_dag(config, clusters):
    dag = DAG()
    rng = Random(config.seed)
    used_zones: set[str] = set()

    # 1. Layer 0: Chapel of Anticipation (single exit)
    start = dag.add_node(layer=0, zone="chapel_of_anticipation")
    current_branches = [Branch(node=start, exit_fog="chapel_exit")]

    # 2. Build layer by layer
    for layer_index in range(1, config.max_layers):
        layer_type = plan_layer_type(layer_index, config, rng)
        next_branches = []

        # Select clusters for each branch (no zone overlap within layer)
        layer_used_zones: set[str] = set()

        for branch in current_branches:
            # Find compatible cluster
            cluster = select_cluster(
                clusters,
                cluster_type=layer_type,
                excluded_zones=used_zones | layer_used_zones,
                min_entries=1,
                rng=rng
            )

            if cluster is None:
                # Fallback or error handling
                continue

            # Pick entry fog and compute available exits
            entry_fog = pick_entry_fog(cluster, rng)
            available_exits = compute_available_exits(cluster, entry_fog)

            # Mark zones as used
            layer_used_zones.update(cluster.zones)

            # Create node and branches
            node = dag.add_node(layer=layer_index, cluster=cluster, entry=entry_fog)
            dag.connect(branch.node, node, fog=branch.exit_fog)

            # Create branches for each available exit
            for exit_fog in available_exits:
                next_branches.append(Branch(node=node, exit_fog=exit_fog))

        # Commit layer zones to global used set
        used_zones.update(layer_used_zones)
        current_branches = next_branches

        # Check for merge opportunities or termination
        if should_merge(current_branches, config):
            current_branches = perform_merge(current_branches, clusters, used_zones)

    # 3. Converge to Radagon
    radagon = dag.add_node(zone="elden_throne")
    for branch in current_branches:
        dag.connect(branch.node, radagon, fog=branch.exit_fog)

    # 4. Validate
    validate_requirements(dag, config)
    validate_balance(dag, config.budget)

    return dag


def compute_available_exits(cluster, entry_fog) -> list[Fog]:
    """Compute exits available after using entry_fog."""
    exits = list(cluster.exit_fogs)

    # Remove entry_fog if it's bidirectional (appears in both entry and exit)
    if entry_fog in exits:
        exits.remove(entry_fog)

    return exits
```

### Layer Planning

The layer sequence emerges from cluster selection:

1. **Layer 0** : Chapel of Anticipation (1 exit → 1 branch)
2. **Layer 1** : First cluster creates N branches (N = exits - 1 if entry is bidirectional)
3. **Layers 2+** : Each branch selects a cluster, potentially splitting further or merging
4. **Final layer** : All branches converge to Radagon

### Merging Branches

Un merge se produit quand plusieurs branches utilisent le **même fog** comme exit vers un cluster commun :

```
Branch A ──(fog_X)──┐
                    ├──→ Cluster C
Branch B ──(fog_X)──┘
```

Cela nécessite que `fog_X` soit un entry_fog de Cluster C et un exit_fog des clusters A et B.

### Path Balancing

With uniform layers, balancing is simpler:
- All branches in a layer have the same cluster type
- Clusters are selected with similar weights (within small tolerance)
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
          "cluster_id": null,
          "zones": ["chapel_of_anticipation"],
          "exits": [
            {"fog_id": "chapel_exit", "target_node": "node_1a"}
          ]
        }
      ]
    },
    {
      "index": 1,
      "tier": 5,
      "nodes": [
        {
          "id": "node_1a",
          "cluster_id": "stormveil_start_c1d3",
          "zones": ["stormveil_start", "stormveil"],
          "entry_fog": "margit_front",
          "exits": [
            {"fog_id": "godrick_front", "target_node": "node_2a"},
            {"fog_id": "divine_tower_gate", "target_node": "node_2b"}
          ]
        }
      ]
    },
    {
      "index": 2,
      "tier": 10,
      "nodes": [
        {
          "id": "node_2a",
          "cluster_id": "murkwater_catacombs_a1b2",
          "zones": ["murkwater_catacombs"],
          "entry_fog": "murkwater_entrance",
          "exits": [
            {"fog_id": "murkwater_boss", "target_node": "node_3"}
          ]
        },
        {
          "id": "node_2b",
          "cluster_id": "tombsward_catacombs_c3d4",
          "zones": ["tombsward_catacombs"],
          "entry_fog": "tombsward_entrance",
          "exits": [
            {"fog_id": "tombsward_boss", "target_node": "node_3"}
          ]
        }
      ]
    }
  ],
  "final": {
    "id": "radagon",
    "cluster_id": null,
    "zones": ["elden_throne"],
    "entry_fogs": ["node_2a_exit", "node_2b_exit"]
  }
}
```

**Notes** :
- `cluster_id` : référence au cluster dans `clusters.json` (null pour start/end)
- `entry_fog` : le fog utilisé pour entrer dans ce cluster
- `exits` : liste des fogs de sortie avec leur destination

## C# Writer

### Responsibilities

1. **Create custom fog gates** between zones (EMEVD events)
2. **Create warps** (teleportation when crossing fog)
3. **Apply enemy scaling** based on layer tier
4. **Give key items** at spawn

### Files Adapted from FogRando

| FogRando Source | SpeedFog Target | Notes |
|-----------------|-----------------|-------|
| `EldenScaling.cs` | `ScalingWriter.cs` | Enemy stat scaling |
| `GameDataWriterE.cs` | `FogGateWriter.cs`, `WarpWriter.cs` | Fog gate creation |
| `fogevents.txt` | `speedfog-events.yaml` | Simplified YAML templates |
| `EventConfig.cs` | `EventBuilder.cs` | Parses YAML, builds EMEVD |
| `AnnotationData.cs` | `GraphReader.cs` | Graph deserialization |

**Note**: Event templates are stored in `speedfog-events.yaml` (not hardcoded in C#) for readability and maintainability.

### Simplified Scaling

```csharp
public static int LayerToTier(int layerIndex, int totalLayers)
{
    // Linear progression from tier 1 to tier 28
    float progress = (float)layerIndex / totalLayers;
    return (int)(1 + progress * 27);
}
```

### Starting Items

All key items are given at game start to prevent softlocks. The complete list:

| Item | Purpose |
|------|---------|
| Academy Glintstone Key | Raya Lucaria access |
| Carian Inverted Statue | Carian Study Hall |
| Cursemark of Death | Ranni quest |
| Dark Moon Ring | Ranni quest |
| Dectus Medallion (Left + Right) | Grand Lift of Dectus |
| Discarded Palace Key | Raya Lucaria locked area |
| Drawing-Room Key | Volcano Manor |
| Gaol Lower/Upper Level Key | Gaol access |
| Haligtree Secret Medallion (Left + Right) | Consecrated Snowfield |
| Hole-Laden Necklace | Quest item |
| Imbued Sword Key | Four Belfries |
| Irina's Letter | Irina quest |
| Larval Tear | Respec |
| Letter from Volcano Manor | Volcano Manor quest |
| Messmer's Kindling | DLC |
| O Mother | Quest item |
| Prayer Room Key | Volcano Manor |
| Pureblood Knight's Medal | Mohgwyn teleport |
| Rold Medallion | Grand Lift of Rold |
| Rusty Key | Stormveil |
| Rya's Necklace | Rya quest |
| Sellian Sealbreaker | Sellia |
| Serpent's Amnion | Quest item |
| Sewer-Gaol Key | Leyndell sewers |
| Stonesword Key (x10) | Imp statues |
| Storeroom Key | Stormveil |
| Volcano Manor Invitation | Volcano Manor |
| Well Depths Key | Quest item |
| Ash of War: Kick | Utility |

### Randomizer Merge (Optional)

SpeedFog can merge with Item/Enemy Randomizer output:

```
Input:
├── Vanilla game files (always loaded)
└── Randomizer mod files (if randomizer_dir specified)
         ↓
    SpeedFog merges EMEVD + adds fog gate events
         ↓
Output: Combined mod files
```

If `randomizer_dir` is not specified, SpeedFog uses vanilla game files as base.

## Key Decisions

| Aspect | Decision |
|--------|----------|
| **Name** | SpeedFog |
| **Architecture** | Python (core) + C# (writer) |
| **Config format** | TOML |
| **Cluster data** | Pre-computed from fog.txt via `generate_clusters.py` |
| **DAG structure** | Uniform layers (same cluster type per layer) |
| **Balancing** | Budget per path with tolerance |
| **Scaling** | Adapted from FogRando (simplified tiers) |
| **One-ways** | Excluded for v1 |
| **Key items** | All 32 items given at start |
| **Seed behavior** | `seed=0`: auto-reroll; `seed=N`: force or error |
| **Enemy Randomizer** | Optional merge via `randomizer_dir` |
| **Target duration** | ~1h (configurable) |
| **Start point** | Chapel of Anticipation |
| **End point** | Radagon/Elden Beast |

## Implementation Roadmap

### Phase 1: Foundations (Python)

- [ ] Create `speedfog/` repo with base structure
- [ ] Script `generate_clusters.py`: generate `clusters.json` from `fog.txt`
- [ ] Define `zone_metadata.toml` with default weights and overrides
- [ ] Parse `config.toml`
- [ ] Parse `clusters.json`

### Phase 2: DAG Generation (Python)

- [ ] Implement `DAG` structure (nodes, edges, layers)
- [ ] Cluster selection with zone exclusion constraints
- [ ] Entry/exit fog management (remove used entry from exits)
- [ ] Split/merge logic based on available exits
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
