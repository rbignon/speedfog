# DAG Generation Algorithm

How SpeedFog generates balanced, randomized DAGs from zone clusters.

## Overview

The generator (`speedfog/generator.py`) builds a directed acyclic graph layer by layer, using three topology operations (split, merge, passant) to create parallel branches that converge before the final boss. The result is a graph where all paths have similar total weight (duration), ensuring fair races.

## Data Structures

### DagNode (`dag.py`)

A cluster instance placed at a specific layer and tier in the DAG.

| Field | Type | Description |
|-------|------|-------------|
| `id` | str | Unique node ID (usually `cluster.id`) |
| `cluster` | ClusterData | Zone cluster with entry/exit fogs |
| `layer` | int | Vertical position (0 = start) |
| `tier` | int | Enemy difficulty scaling (1-28) |
| `entry_fogs` | list | Fog gates consumed to enter (empty for start) |
| `exit_fogs` | list | Available exits after consuming entries |

### DagEdge (`dag.py`)

A connection between two nodes via specific fog gates.

| Field | Type | Description |
|-------|------|-------------|
| `source_id` | str | Node the player leaves |
| `target_id` | str | Node the player enters |
| `exit_fog` | str | Fog gate used to exit source |
| `entry_fog` | str | Fog gate used to enter target |

### Branch (`dag.py`)

Tracks parallel path state during generation.

| Field | Type | Description |
|-------|------|-------------|
| `id` | str | Branch identifier (e.g., `"0"`, `"0_1"`, `"merged_3"`) |
| `current_node_id` | str | Where this branch is now |
| `available_exit` | str | Fog gate to use for next connection |

## Topology Operations

The algorithm decides between three operations at each layer:

### Passant (1→1 per branch)

Each branch independently advances through its own node. The simplest operation.

**Filter**: `can_be_passant_node(cluster)` — cluster must have exactly 1 net exit after consuming 1 entry fog.

**Process**:
1. For each branch, find a compatible cluster
2. Pick entry fog that maximizes remaining exits (prefer non-bidirectional)
3. Create node, connect edge from branch's current node
4. Update branch to point to new node

### Split (1→N branches)

One branch fans out into N parallel branches. Creates divergence in the DAG.

**Filter**: `can_be_split_node(cluster, N)` — cluster must have exactly N net exits after consuming 1 entry fog.

**Process**:
1. Pick a branch to split (random selection)
2. Find a cluster with N available exits
3. Create one node with N outgoing edges
4. Replace the single branch with N new branches (named `{parent}_{0..N-1}`)
5. Non-split branches execute passant in the same layer

**Constraints**:
- `N` ranges from 2 to `max_branches` (config, default 3)
- Total branches after split must not exceed `max_parallel_paths` (config, default 3)
- Room calculation: `max_parallel_paths - len(branches) + 1`
- Tries max fan-out first, falls back to smaller N (greedy)

### Merge (N→1 branches)

N branches converge into a single node. Creates convergence in the DAG.

**Filter**: `can_be_merge_node(cluster, N)` — cluster must have N+ entry fogs and exactly 1 net exit after consuming N entries.

**Process**:
1. Select N branches to merge (random subset)
2. Find a cluster with enough entries and 1 net exit
3. Create one node with N incoming edges
4. Replace N branches with 1 new branch (named `merged_{layer}`)
5. Non-merged branches execute passant in the same layer

**Anti-micro-merge**: Selected branches must have at least 2 different parent nodes. This prevents trivial split→immediate-merge patterns (Y-shapes) that add no meaningful divergence.

**Entry selection**: `select_entries_for_merge()` prefers non-bidirectional entries (preserves exit count for future operations), with main-tagged entries as a soft preference within each group.

## Operation Decision

```
decide_operation(num_branches, config, rng):
    if num_branches >= max_parallel_paths:
        → MERGE (merge_prob) or PASSANT (1 - merge_prob)
    elif num_branches == 1:
        → SPLIT (split_prob) or PASSANT (1 - split_prob)
    else:  # 1 < branches < max
        → SPLIT (split_prob) | MERGE (merge_prob) | PASSANT (remainder)
```

Default probabilities: split=0.3, merge=0.3, passant=0.4 (implicit).

When at max parallel paths, only merge or passant are available. When at 1 branch, only split or passant.

## Cluster Compatibility

### Bidirectional Fogs

A fog gate is **bidirectional** if the same `(fog_id, zone)` pair appears in both a cluster's `entry_fogs` and `exit_fogs`. This means using it as an entry consumes a potential exit (and vice versa).

### Net Exits

`count_net_exits(cluster, N)` calculates the minimum exits remaining after consuming N entry fogs:

1. Classify entries as bidirectional or non-bidirectional
2. Greedily consume non-bidirectional entries first (they don't reduce exits)
3. If more entries needed, consume bidirectional (each reduces exits by 1)
4. Return `total_exits - bidirectional_consumed`

This determines which topology operations are compatible:

| Operation | Compatibility Rule |
|-----------|-------------------|
| Passant | `count_net_exits(cluster, 1) == 1` |
| Split(N) | `count_net_exits(cluster, 1) == N` |
| Merge(N) | `len(entry_fogs) >= N` AND `count_net_exits(cluster, N) == 1` |

## Generation Flow

### 1. Start Node (layer 0)

- Select start cluster (type `"start"`, Chapel of Anticipation)
- No entry consumed (player spawns here)
- All exits available
- Initialize branches from exits (limited by `max_parallel_paths` and `max_branches`)

### 2. First Layer (optional forced type)

If `first_layer_type` is configured (e.g., `"legacy_dungeon"`), execute a passant layer with only clusters of that type. Ensures a consistent opening experience.

### 3. Plan Layer Types

```python
num_layers = random(min_layers, max_layers)
layer_types = plan_layer_types(requirements, num_layers, rng, major_boss_ratio)
```

`plan_layer_types()` builds a list from:
- Required legacy dungeons, bosses, mini dungeons (from config)
- Pad with mini_dungeons or trim to fit `num_layers`
- Replace some with `major_boss` based on `major_boss_ratio`
- Shuffle for randomness

### 4. Execute Layers

For each planned layer:
1. Compute tier via interpolation: `tier = 1 + (layer / (total - 1)) * (final_tier - 1)`
2. Near-end check: if last 2 layers and multiple branches → force merge
3. Decide operation (split/merge/passant) based on probabilities
4. Execute operation with compatible clusters
5. Fallback: if merge fails (all branches share same parent → micro-merge), use passant

### 5. Forced Merge

If multiple branches remain after all planned layers:

```
while len(branches) > 1:
    if all branches share same parent:
        execute passant layer (diverge first)
    execute merge layer (converge)
```

Inserts passant layers as needed to break micro-merge patterns. Uses N-ary merges (up to `max_branches`) for efficiency.

### 6. End Node

- Pick final boss from candidates (default: Radagon/PCR)
- Prefer main-tagged entry fog (correct Stake of Marika placement)
- No exits (terminal node)
- Connect single remaining branch

## Tier Interpolation

Linear interpolation from tier 1 (layer 0) to `final_tier` (last layer, default 28):

```
tier(layer) = round(1 + (layer / (total_layers - 1)) * (final_tier - 1))
```

This maps to FogMod's enemy scaling SpEffects.

## Budget and Weight

Each cluster has a **weight** (approximate traversal time in minutes). Each path through the DAG has a total weight = sum of node weights along the path.

**Balance constraint** (from config):
- `tolerance`: maximum allowed weight spread between heaviest and lightest paths (default 5)

The validator checks that all paths have similar weights. If the spread exceeds tolerance, a warning is produced (not a hard error).

## Retry System

**Fixed seed** (`config.seed != 0`): single attempt, fail on error.

**Auto-reroll** (`config.seed == 0`):
- Generate random seed
- Attempt generation + validation
- Retry on `GenerationError` or validation failure
- Up to `max_attempts` (default 100)
- Return first successful result

## Configuration Reference

| Config Key | Default | Description |
|------------|---------|-------------|
| `structure.max_parallel_paths` | 3 | Maximum concurrent branches |
| `structure.max_branches` | 3 | Maximum fan-out/fan-in per operation |
| `structure.min_layers` | 6 | Minimum intermediate layers |
| `structure.max_layers` | 10 | Maximum intermediate layers |
| `structure.split_probability` | 0.3 | Chance of split at each layer |
| `structure.merge_probability` | 0.3 | Chance of merge at each layer |
| `structure.first_layer_type` | None | Force type for first layer |
| `structure.major_boss_ratio` | 0.0 | Fraction of layers with major bosses |
| `structure.final_boss_candidates` | `["leyndell_erdtree", "enirilim_radahn"]` | Possible end bosses |
| `structure.final_tier` | 28 | Enemy tier for final boss |
| `budget.tolerance` | 5 | Max allowed spread between paths |
| `requirements.legacy_dungeons` | 1 | Minimum legacy dungeons |
| `requirements.bosses` | 5 | Minimum boss arenas |
| `requirements.mini_dungeons` | 5 | Minimum mini dungeons |

## Validation

The validator (`speedfog/validator.py`) checks:

1. **Structural**: start/end exist, all edges valid, no backward edges
2. **Reachability**: all nodes reachable from start (BFS), all can reach end (reverse BFS)
3. **Entry consistency**: entry fog count matches incoming edge count
4. **No duplicate edges**: prevents trivial Y-patterns
5. **Requirements**: minimum zone type counts met
6. **Budget**: all paths within `[min_weight, max_weight]`

## References

- Generator: `speedfog/generator.py`
- DAG data structures: `speedfog/dag.py`
- Planner: `speedfog/planner.py`
- Validator: `speedfog/validator.py`
- Balance analysis: `speedfog/balance.py`
- Config: `speedfog/config.py`
