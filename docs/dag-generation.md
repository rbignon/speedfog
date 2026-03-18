# DAG Generation Algorithm

**Date:** 2026-02-15 — **Updated:** 2026-03-18
**Status:** Active

How SpeedFog generates balanced, randomized DAGs from zone clusters.

## Overview

The generator (`speedfog/generator.py`) builds a directed acyclic graph layer by layer, using four topology operations (split, merge, passant, rebalance) to create parallel branches that converge before the final boss. The result is a graph where all paths have similar total weight (duration), ensuring fair races.

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
| `layers_since_last_split` | int | Layers since this branch last had a split point (for `max_branch_spacing`) |

### FogRef (`dag.py`)

A `NamedTuple(fog_id, zone)` that disambiguates the same `fog_id` across different zones.

## Topology Operations

The algorithm decides between four operations at each layer:

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
- `N` ranges from 2 to `max_exits` (config, default 3)
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

### Rebalance (N->N branches)

Merges 2 branches and splits 1 stale branch across 2 layers (merge-first: merge at layer N, split at layer N+1), keeping the total branch count constant. Uses 2 clusters: one merge-capable, one split-capable.

**Trigger conditions** (all must be met):
1. `max_branch_spacing > 0` (feature enabled)
2. `len(branches) >= max_parallel_paths` (saturated — no room to split)
3. `max(b.layers_since_last_split for b in branches) >= max_branch_spacing` (threshold reached)
4. At least one valid merge pair exists among branches other than the split target

**Process**:
1. Identify the most stale branch (highest `layers_since_last_split`) — split target
2. Find 2 merge candidates among remaining branches (bypass `min_branch_age`, enforce anti-micro-merge)
3. Pick a split-capable cluster, then a merge-capable cluster
4. Execute split on the stale branch → 2 child branches with `layers_since_last_split = 0`
5. Execute merge on the pair → 1 merged branch with `layers_since_last_split = max(A, B) + 1`
6. Passant remaining branches (carrying their existing counters)

REBALANCE is internal to the generator — graph.json serializes nodes and edges, not operations. A REBALANCE produces split and merge nodes identical to separate SPLIT and MERGE operations.

### Max Branch Spacing

When `max_branch_spacing > 0`, the generator guarantees that no branch goes more than ~`max_branch_spacing` layers without a split point. Each branch tracks `layers_since_last_split`, counting layers since the player on that branch last had a choice.

**Counter rules:**

| Event | Counter value |
|-------|---------------|
| Branch created by split | `0` (player just had a choice) |
| Branch does a passant | `+= 1` |
| Non-split branches on a split layer | `+= 1` (no choice for them) |
| Two branches merge | Result inherits `max(A, B)` |
| Branches not participating in a merge | `+= 1` |
| Start node (layer 0) | All initial branches start at `0` |

Merges do NOT reset the counter — a merge doesn't give the player a new choice (fog gates are one-way).

**Priority hierarchy in `determine_operation`:**
1. **REBALANCE** (highest) — if saturated + stale + merge pair available
2. **`prefer_merge`** — if convergence phase, bypass probability roll, return MERGE
3. **Forced split** — if not saturated but stale, force SPLIT on the most stale branch
4. **Normal probability roll** — split_prob / merge_prob / passant

**Config validation:** `min_branch_age` must be strictly less than `max_branch_spacing` (when both are enabled).

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

For the primary branch, the pre-selected `primary_cluster` is used. For non-primary branches (passant companions during split or merge layers), `pick_cluster_with_type_fallback()` selects each companion cluster independently: it tries the planned layer type first, then falls back to other types using **weighted random selection** proportional to remaining available cluster counts. This prevents temporal segregation where one type (e.g., `mini_dungeon`) would systematically fill late layers because it had the largest remaining pool.

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
- Initialize branches from exits (limited by `max_parallel_paths` and `max_exits`)

### 2. Pre-select Final Boss and Reserve Zones

Before executing any intermediate layers:

1. Resolve `final_boss_candidates` from config (supports `"all"` keyword)
2. Find an available final boss cluster from the candidate zones
3. Compute **reserved zones**: the final boss cluster's zones plus the prerequisite cluster's zones (if the boss has a `requires` field)
4. Reserved zones are excluded from intermediate layer selection, preventing them from being consumed before they are needed

**Zone conflicts**: Some zones are mutually exclusive and cannot both appear in the same run (declared via `conflicts_with` in `zone_metadata.toml`). When a cluster is selected, conflicting zones are added to `used_zones` via `_mark_cluster_used()`, preventing any cluster containing those zones from being picked later. Example: `stormveil_margit` (Margit) and `leyndell_sanctuary` (Morgott) — Margit is Morgott in disguise, and killing Morgott removes Margit from his arena. Conflicts are non-transitive and must be declared symmetrically on both sides.

### 3. First Layer (optional forced type)

If `first_layer_type` is configured (e.g., `"legacy_dungeon"`), execute a passant layer with only clusters of that type. Uses `pick_cluster_uniform()` (cluster-first, no capability filter -- all clusters passed `filter_passant_incompatible()` at load time). Ensures a consistent opening experience.

### 4. Plan Layer Types

```python
num_layers = random(min_layers, max_layers)
# Reduced by 1 if first_layer_type was used
layer_types = plan_layer_types(requirements, num_layers, rng)
```

`plan_layer_types()` builds a list from:
- Required legacy dungeons, bosses, mini dungeons, major bosses (from config)
- Pad with mini_dungeons/boss_arenas/legacy_dungeons or trim to fit `num_layers`
- Shuffle for randomness

### 5. Execute Layers (cluster-first)

For each planned layer:
1. Pick a cluster uniformly from the layer type's candidates (`pick_cluster_with_type_fallback()`)
2. Call `determine_operation()` on the selected cluster to decide rebalance/split/merge/passant
3. Execute the operation:
   - **REBALANCE**: Merge a pair + split the most stale branch on the same layer (N→N).
   - **SPLIT**: The primary cluster becomes the split node. Non-split branches get their own cluster for passant companions.
   - **MERGE**: The primary cluster becomes the merge node. Non-merged branches get their own cluster for passant companions.
   - **PASSANT**: The primary cluster is assigned to the first branch. Remaining branches get their own cluster.

### 6. Convergence

If multiple branches remain after all planned layers, a unified convergence loop runs with `prefer_merge=True`:

```
conv_pool_sizes = {t: pool_size for t in FALLBACK_TYPES}

while len(branches) > 1:
    conv_used = count types already in dag.nodes
    conv_layer_type = pick_weighted_type(conv_pool_sizes, conv_used, rng)
    operation = determine_operation(..., prefer_merge=True)
    if REBALANCE: execute_rebalance_layer (maintains spacing)
    elif MERGE: execute_merge_layer (reduces branch count)
    else: try merge, fallback to passant (diverge nodes for anti-micro-merge)
```

**Type selection**: each convergence layer picks its type via `pick_weighted_type()`, which draws proportionally from remaining pool capacity. This distributes convergence layers across available types instead of using a single fixed type (previously `layer_types[-1]`).

REBALANCE can still trigger during convergence when a branch exceeds the staleness threshold while branches are saturated, maintaining spacing even during the final merge phase.

`merge_reserve` is set to `max_parallel_paths + 2` to accommodate REBALANCE layers intercalated with merges. Guard-rail: if convergence exceeds `merge_reserve * 2` layers, a `GenerationError` is raised.

Inserts passant layers as needed to break micro-merge patterns. Uses N-ary merges (up to `max_entrances`) for efficiency. Forced merges deliberately bypass `min_branch_age` (use `min_age=0`) to guarantee convergence regardless of branch age.

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

**When:** `crosslinks = true` (default: false)

**Algorithm:**
1. Find all eligible (source, target) pairs:
   - `source.layer == target.layer - 1` (adjacent layers only — no layer-skipping, which would let players bypass content and break racing balance)
   - Source has unused exit fogs (surplus from **cluster's** full exit list)
   - Target has available entry fogs (from **cluster's** full entry list — entries are reusable since FogMod handles multiple connections to the same entrance via `DuplicateEntrance()`, only Pair chain and proximity exclusions apply)
   - No existing edge between them
   - No existing path from source to target (different branches)
2. Shuffle eligible pairs and try every one — eligible pairs are structurally rare (typically 0-4 per DAG) because most clusters have just enough exit fogs for their normal edges
3. For each: re-check availability (earlier cross-links may have consumed exit surplus), pick an unused exit fog from source, an available entry fog from target, add edge

**Surplus from cluster, not node:** The generator's `_pick_entry_and_exits_for_node()` truncates `node.exit_fogs` to `min_exits` (typically 1 for passant nodes). Cross-link exit surplus is computed from `node.cluster.exit_fogs` (the full list) minus fogs consumed by outgoing edges. This is why most passant nodes have surplus exits available for cross-links despite `node.exit_fogs` containing only 1.

**Entry reuse via DuplicateEntrance:** Unlike exits (one gate = one destination), entries are arrival points — multiple exits can all warp to the same entrance. FogMod handles this via `DuplicateEntrance()`, which creates independent edge copies. Therefore, entry fogs already used by incoming edges are still available for cross-links. The only exclusions are bidirectional Pair consumption (entry fog used as exit on the same node) and proximity groups.

**Pair chain exclusion:** Bidirectional fog gates have both an exit and entry side linked via FogMod's Pair chain. When `Graph.Connect()` uses one side, it marks the Pair as consumed. The Pair is per-zone: the same fog_id on different zones creates independent Pairs in FogMod's Graph. Therefore, surplus exits exclude any `(fog_id, zone)` already consumed as entry on the same node (and vice versa), preventing "Already matched" errors in FogMod.

**Fog list consistency:** Each cross-link appends the consumed exit fog to the source node's `exit_fogs` and the consumed entry fog to the target node's `entry_fogs`, maintaining the invariant that node fog lists reflect actual edge usage.

**Effect on paths:** Cross-links create additional start→end paths through the DAG. The balance checker considers all paths, including those using cross-links.

## Tier Interpolation

Tiers are assigned in a **post-pass** after DAG construction is complete. This ensures `total_layers` is exact (not estimated), guaranteeing monotonically non-decreasing tiers across layers. The computation uses `compute_tier()` from `planner.py`. The progression curve is configurable via `tier_curve`:

**Linear** (default): constant tier increase per layer.

```
progress = layer / (total_layers - 1)
tier(layer) = round(1 + progress * (final_tier - 1))
```

**Power**: applies an exponent to the progress, shaping the difficulty curve.

```
progress = (layer / (total_layers - 1)) ^ exponent
tier(layer) = round(1 + progress * (final_tier - 1))
```

The exponent controls the shape:
- `< 1.0` (e.g. 0.6): front-loaded tiers — harder early, gentler late game plateau
- `= 1.0`: equivalent to linear
- `> 1.0` (e.g. 1.5): back-loaded tiers — easy early, punitive late game ramp

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
| `structure.max_exits` | 3 | Maximum split fan-out (1→N branches) |
| `structure.max_entrances` | 3 | Maximum merge fan-in (N→1 branches) |
| `structure.max_branches` | 3 | Default for max_exits/max_entrances (backward compat) |
| `structure.min_layers` | 6 | Minimum intermediate layers |
| `structure.max_layers` | 10 | Maximum intermediate layers |
| `structure.split_probability` | 0.9 | Chance of split at each layer (if cluster supports it) |
| `structure.merge_probability` | 0.5 | Chance of merge at each layer (if cluster supports it) |
| `structure.min_branch_age` | 0 | Minimum layers before a branch can be merged (0=no limit) |
| `structure.max_branch_spacing` | 4 | Maximum layers a branch can go without a split (0=disabled) |
| `structure.crosslinks` | false | Add cross-links between parallel branches |
| `structure.first_layer_type` | None | Force type for first layer |
| `requirements.major_bosses` | 8 | Number of major boss layers |
| `structure.final_boss_candidates` | `["leyndell_erdtree", "enirilim_radahn"]` | Possible end bosses |
| `structure.final_tier` | 28 | Enemy tier for final boss |
| `structure.tier_curve` | `"linear"` | Tier progression curve (`"linear"` or `"power"`) |
| `structure.tier_curve_exponent` | 0.6 | Power curve exponent (only for `"power"`) |
| `structure.start_tier` | 1 | Starting enemy tier (range 1-28) |
| `budget.tolerance` | 5 | Max allowed spread between paths |
| `requirements.legacy_dungeons` | 1 | Minimum legacy dungeons |
| `requirements.bosses` | 5 | Minimum boss arenas |
| `requirements.mini_dungeons` | 5 | Minimum mini dungeons |

## Validation

### Config Validation (`validate_config`)

Pre-generation checks on configuration vs cluster pool:

1. **first_layer_type**: must be a valid cluster type
2. **major_bosses**: must be >= 0
3. **Requirements vs min_layers**: warning if total requirements exceed `min_layers`
4. **final_boss_candidates**: all zones must exist in the pool
5. **Pool capacity**: warning when `requirement * max_parallel_paths > pool_size` for any cluster type. With parallel branches, each planned layer consumes up to `max_parallel_paths` clusters of the same type. Example: `major_bosses=12` with `max_parallel_paths=4` requires up to 48 clusters from a pool of 38, which will cause type exhaustion during generation.

### DAG Validation (`validator.py`)

Post-generation checks on the built DAG:

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
- Output: `speedfog/output.py`
- Config: `speedfog/config.py`
