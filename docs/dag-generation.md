# DAG Generation Algorithm

**Date:** 2026-02-15 — **Updated:** 2026-02-27
**Status:** Active

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
| `entry_fogs` | list[FogRef] | Fog gates consumed to enter (empty for start) |
| `exit_fogs` | list[FogRef] | Available exits after consuming entries |

### DagEdge (`dag.py`)

A connection between two nodes via specific fog gates.

| Field | Type | Description |
|-------|------|-------------|
| `source_id` | str | Node the player leaves |
| `target_id` | str | Node the player enters |
| `exit_fog` | FogRef | Fog gate used to exit source |
| `entry_fog` | FogRef | Fog gate used to enter target |

### Branch (`dag.py`)

Tracks parallel path state during generation.

| Field | Type | Description |
|-------|------|-------------|
| `id` | str | Branch identifier (e.g., `"b0"`, `"b0_a"`, `"merged_3"`) |
| `current_node_id` | str | Where this branch is now |
| `available_exit` | FogRef | Fog gate to use for next connection |
| `birth_layer` | int | Layer when this branch was created (for `min_branch_age`) |

### FogRef (`dag.py`)

A `NamedTuple(fog_id, zone)` that disambiguates the same `fog_id` across different zones.

## Topology Operations

The algorithm decides between three operations at each layer:

### Passant (1->1 per branch)

Each branch independently advances through its own node. The simplest operation.

**Filter**: `can_be_passant_node(cluster)` -- cluster must have at least 1 net exit after consuming 1 entry fog. Extra exits are left unmapped.

**Process**:
1. For each branch, find a compatible cluster
2. Pick entry fog that maximizes remaining exits (prefer non-bidirectional)
3. Create node, connect edge from branch's current node
4. Update branch to point to new node

### Split (1->N branches)

One branch fans out into N parallel branches. Creates divergence in the DAG.

**Filter**: `can_be_split_node(cluster, N)` -- cluster must have at least N net exits after consuming 1 entry fog. Extra exits beyond N are left unmapped.

**Process**:
1. Pick a branch to split (random selection)
2. Find a cluster with N+ available exits
3. Create one node with N outgoing edges
4. Replace the single branch with N new branches (named `{parent}_{a..z}`)
5. Non-split branches execute passant in the same layer

**Constraints**:
- `N` ranges from 2 to `max_branches` (config, default 3)
- Total branches after split must not exceed `max_parallel_paths` (config, default 3)
- Room calculation: `max_parallel_paths - len(branches) + 1`
- Tries max fan-out first, falls back to smaller N (greedy)

### Merge (N->1 branches)

N branches converge into a single node. Creates convergence in the DAG.

**Filter**: `can_be_merge_node(cluster, N)` -- cluster must have N+ entry fogs and at least 1 net exit after consuming N entries. Extra exits are left unmapped.

**Process**:
1. Select N branches to merge (random subset)
2. Find a cluster with enough entries and 1+ net exit
3. Create one node with N incoming edges
4. Replace N branches with 1 new branch (named `merged_{layer}`)
5. Non-merged branches execute passant in the same layer

**Anti-micro-merge**: Selected branches must have at least 2 different parent nodes. This prevents trivial split-then-immediate-merge patterns (Y-shapes) that add no meaningful divergence.

**Branch age gate**: When `min_branch_age > 0`, only branches that have existed for at least that many layers are eligible for merging (`current_layer - birth_layer >= min_branch_age`). This prevents premature merges where branches split and immediately reconverge, creating long linear (width=1) sections. Branch age is tracked via `birth_layer`: split and merge operations reset it to the current layer; passant operations preserve it.

**Entry selection**: `select_entries_for_merge()` prefers non-bidirectional entries (preserves exit count for future operations), with main-tagged entries as a soft preference within each group.

**Shared entrance mode**: When `cluster.allow_shared_entrance` is true, all merging branches connect to the same entry fog. Only requires 2+ entries and 1+ exit regardless of fan-in N.

## Cluster-First Selection

The generator uses a **cluster-first** model: pick a cluster uniformly at random, then determine what operation it supports based on its fog gate structure.

For each layer in the main loop:

```
candidates = clusters.get_by_type(layer_type)

primary_cluster = pick_cluster_uniform(candidates, used_zones, rng, reserved_zones)
    -> filter by zone overlap and reserved zones, pick uniformly

operation, fan = determine_operation(primary_cluster, branches, config, rng, current_layer):
    check cluster capabilities (split, merge, passant)
    merge eligibility also requires age-eligible branches (see Branch age gate)
    if can_split AND can_merge:
        roll = random()
        if roll < split_prob:                     -> SPLIT
        elif roll < split_prob + merge_prob:       -> MERGE
        else:                                      -> PASSANT
    elif can_split:
        -> SPLIT (split_prob) or PASSANT (1 - split_prob)
    elif can_merge:
        -> MERGE (merge_prob) or PASSANT (1 - merge_prob)
    else:
        -> PASSANT (always)
```

Default probabilities: split=0.9, merge=0.5. When `split_prob + merge_prob >= 1.0` (as with the defaults, 0.9 + 0.5 = 1.4), the probabilities act as a priority cascade: split is tried first, then merge gets the remainder, and passant is only reached if the sum < 1.0. Because operations are gated by the cluster's capability (many clusters only support passant), these values are higher than they appear -- the effective rate is `P(cluster supports op) * configured_prob`.

Passant-incompatible clusters (those with zero net exits after consuming an entry) are filtered at load time by `ClusterPool.filter_passant_incompatible()`, ensuring every cluster in the pool can serve as at least a passant node.

For the primary branch, the pre-selected `primary_cluster` is used. For non-primary branches (passant companions during split or merge layers), a separate `pick_cluster_uniform()` call selects each companion cluster independently.

## Cluster Compatibility

### Bidirectional Fogs

A fog gate is **bidirectional** if the same `(fog_id, zone)` pair appears in both a cluster's `entry_fogs` and `exit_fogs`. This means using it as an entry consumes a potential exit (and vice versa).

### Net Exits

`count_net_exits(cluster, N)` calculates the minimum exits remaining after consuming N entry fogs:

1. Classify entries as bidirectional or non-bidirectional
2. Greedily consume non-bidirectional entries first (they don't reduce exits)
3. If more entries needed, consume bidirectional (each reduces exits by 1)
4. Return `total_exits - bidirectional_consumed`

When `proximity_groups` are present, the function evaluates all entry combinations and returns the worst-case (minimum) net exits, accounting for proximity-blocked exits.

This determines which topology operations are compatible:

| Operation | Compatibility Rule |
|-----------|-------------------|
| Passant | `count_net_exits(cluster, 1) >= 1` |
| Split(N) | `count_net_exits(cluster, 1) >= N` |
| Merge(N) | `len(entry_fogs) >= N` AND `count_net_exits(cluster, N) >= 1` |

Special cluster flags override the standard checks:
- `allow_entry_as_exit`: The entry fog's bidirectional pair is NOT consumed. Passant needs `>=1` entry and `>=1` exit. Split(N) needs `>=1` entry and `>=N` exits.
- `allow_shared_entrance`: All merging branches share one entry fog. Merge needs `>=2` entries and `>=1` exit, regardless of fan-in N.

### Proximity Groups

A cluster may define `proximity_groups`: lists of fog IDs that are spatially close in-game. When an entry fog belongs to a proximity group, exits in the same group are excluded. This prevents the player from exiting through a fog gate that is physically adjacent to where they entered.

## Generation Flow

```
Start Node → [First Layer] → Plan Types → Execute Layers → Forced Merge → [Prerequisites] → End Node → [Cross-Links]
   (L0)       (optional)      (shuffle)     (cluster-first)   (converge)    (if required)   (final boss) (post-hoc)
                                                 │
                                          ┌──────┼──────┐
                                        SPLIT  MERGE  PASSANT
                                       (1→N)  (N→1)   (1→1)
```

### 1. Start Node (layer 0)

- Select start cluster (type `"start"`, Chapel of Anticipation)
- No entry consumed (player spawns here)
- All exits available
- Initialize branches from exits (limited by `max_parallel_paths` and `max_branches`)

### 2. Pre-select Final Boss and Reserve Zones

Before executing any intermediate layers:

1. Resolve `final_boss_candidates` from config (supports `"all"` keyword)
2. Find an available final boss cluster from the candidate zones
3. Compute **reserved zones**: the final boss cluster's zones plus the prerequisite cluster's zones (if the boss has a `requires` field)
4. Reserved zones are excluded from intermediate layer selection, preventing them from being consumed before they are needed

### 3. First Layer (optional forced type)

If `first_layer_type` is configured (e.g., `"legacy_dungeon"`), execute a passant layer with only clusters of that type. Uses `pick_cluster_uniform()` (cluster-first, no capability filter -- all clusters passed `filter_passant_incompatible()` at load time). Ensures a consistent opening experience.

### 4. Plan Layer Types

```python
num_layers = random(min_layers, max_layers)
# Reduced by 1 if first_layer_type was used
layer_types = plan_layer_types(requirements, num_layers, rng, major_boss_ratio)
```

`plan_layer_types()` builds a list from:
- Required legacy dungeons, bosses, mini dungeons (from config)
- Pad with mini_dungeons or trim to fit `num_layers`
- Replace some with `major_boss` based on `major_boss_ratio`
- Shuffle for randomness

### 5. Execute Layers (cluster-first)

For each planned layer:
1. Compute tier via interpolation: `tier = 1 + (layer / (total - 1)) * (final_tier - 1)`
2. **Near-end check**: if within last 2 layers and multiple branches remain, trigger `execute_forced_merge()` and skip the normal operation
3. Pick a cluster uniformly from the layer type's candidates (`pick_cluster_uniform()`)
4. Call `determine_operation()` on the selected cluster to decide split/merge/passant
5. Execute the operation:
   - **SPLIT**: The primary cluster becomes the split node. Non-split branches get their own `pick_cluster_uniform()` call for passant companions.
   - **MERGE**: The primary cluster becomes the merge node. Non-merged branches get their own `pick_cluster_uniform()` call for passant companions. If merge indices cannot be found (micro-merge), falls back to passant.
   - **PASSANT**: The primary cluster is assigned to the first branch. Remaining branches get their own `pick_cluster_uniform()` call.

### 6. Forced Merge

If multiple branches remain after all planned layers:

```
while len(branches) > 1:
    if all branches share same parent:
        execute passant layer (diverge first)
    execute merge layer (converge)
```

Inserts passant layers as needed to break micro-merge patterns. Uses N-ary merges (up to `max_branches`) for efficiency. Forced merges deliberately bypass `min_branch_age` (use `min_age=0`) to guarantee convergence regardless of branch age.

### 7. Prerequisite Injection

If the final boss cluster has a `requires` field (e.g., `leyndell_erdtree` requires `farumazula_maliketh`):

1. Find the cluster containing the prerequisite zone
2. Insert it as a passant node on the single merged path
3. Connect the branch through the prerequisite to the final boss

This runs after the forced merge (so exactly 1 branch exists) and before the end node.

### 8. End Node

- Use the pre-selected final boss cluster
- Prefer main-tagged entry fog (correct Stake of Marika placement)
- No exits (terminal node)
- Connect single remaining branch

### 9. Cross-Links (post-hoc)

After the complete DAG is built (start → layers → forced merge → prerequisite → end), an optional cross-link pass adds edges between parallel branches.

**When:** `crosslink_ratio > 0` (default: 0.0, disabled)

**Algorithm:**
1. Find all eligible (source, target) pairs:
   - `source.layer == target.layer - 1` (adjacent layers only — no layer-skipping, which would let players bypass content and break racing balance)
   - Source has unused exit fogs (surplus from **cluster's** full exit list)
   - Target has unused entry fogs (surplus from **cluster's** full entry list)
   - No existing edge between them
   - No existing path from source to target (different branches)
2. Compute count: `round(len(eligible_pairs) * crosslink_ratio)`; if 0, skip
3. Shuffle and select that many pairs
4. For each: re-check surplus (earlier cross-links may have consumed it), pick an unused exit fog from source, unused entry fog from target, add edge

**Surplus from cluster, not node:** The generator's `_pick_entry_and_exits_for_node()` truncates `node.exit_fogs` to `min_exits` (typically 1 for passant nodes). Cross-link surplus is computed from `node.cluster.exit_fogs` (the full list) minus fogs consumed by outgoing edges. This is why most passant nodes have surplus exits available for cross-links despite `node.exit_fogs` containing only 1.

**Pair chain exclusion:** Bidirectional fog gates (same fog_id in both entry_fogs and exit_fogs of a cluster) are linked via FogMod's Pair chain. When `Graph.Connect()` uses one side, it marks the Pair as consumed. Therefore, surplus exits exclude any fog_id already consumed as entry on the same node (and vice versa), preventing "Already matched" errors in FogMod.

**Fog list consistency:** Each cross-link appends the consumed exit fog to the source node's `exit_fogs` and the consumed entry fog to the target node's `entry_fogs`, maintaining the invariant that node fog lists reflect actual edge usage.

**Effect on paths:** Cross-links create additional start→end paths through the DAG. The balance checker considers all paths, including those using cross-links.

## Tier Interpolation

Linear interpolation from tier 1 (layer 0) to `final_tier` (last layer, default 28):

```
tier(layer) = round(1 + (layer / (total_layers - 1)) * (final_tier - 1))
```

Special cases:
- Single layer: always tier 1
- `final_tier` is clamped to range [1, 28]

This maps to FogMod's enemy scaling SpEffects.

## Budget and Weight

Each cluster has a **weight** (approximate traversal time in minutes). Each path through the DAG has a total weight = sum of node weights along the path.

**Balance constraint** (from config):
- `tolerance`: maximum allowed weight spread between heaviest and lightest paths (default 5)

The validator checks that all paths have similar weights. If the spread exceeds tolerance, a warning is produced (not a hard error).

## Retry System (`generate_with_retry`)

**Fixed seed** (`config.seed != 0`): single attempt, fail on error or validation failure.

**Auto-reroll** (`config.seed == 0`):
- Generate random seed
- Attempt generation + validation
- Retry on `GenerationError` or validation failure
- Up to `max_attempts` (default 100)
- Print each failure with seed and reason
- Return first successful result as `GenerationResult(dag, seed, validation, attempts)`

Config validation runs once before any attempts; invalid config raises `GenerationError` immediately.

## Configuration Reference

| Config Key | Default | Description |
|------------|---------|-------------|
| `structure.max_parallel_paths` | 3 | Maximum concurrent branches |
| `structure.max_branches` | 3 | Maximum fan-out/fan-in per operation |
| `structure.min_layers` | 6 | Minimum intermediate layers |
| `structure.max_layers` | 10 | Maximum intermediate layers |
| `structure.split_probability` | 0.9 | Chance of split at each layer (if cluster supports it) |
| `structure.merge_probability` | 0.5 | Chance of merge at each layer (if cluster supports it) |
| `structure.min_branch_age` | 0 | Minimum layers before a branch can be merged (0=no limit) |
| `structure.crosslink_ratio` | 0.0 | Fraction of eligible pairs that become cross-links (0.0-1.0) |
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
6. **Zone tracking collisions**: shared exit gate + same entrance map (warning)
7. **Path count**: no paths = error, single path = warning
8. **Budget**: all paths within `[min_weight, max_weight]`
9. **Layer count**: few layers = warning

## References

- Generator: `speedfog/generator.py`
- Cross-links: `speedfog/crosslinks.py`
- DAG data structures: `speedfog/dag.py`
- Planner: `speedfog/planner.py`
- Clusters: `speedfog/clusters.py`
- Validator: `speedfog/validator.py`
- Balance analysis: `speedfog/balance.py`
- Config: `speedfog/config.py`
