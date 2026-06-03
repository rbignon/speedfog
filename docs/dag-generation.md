# DAG Generation Algorithm

**Date:** 2026-02-15 -- **Updated:** 2026-05-09
**Status:** Active

How SpeedFog generates balanced, randomized DAGs from zone clusters.

## Overview

The generator (`speedfog/generator.py`) builds a directed acyclic graph layer by layer using an
exit-driven routing model. The total number of layers is fixed by `layers_count` (inclusive of
start and final boss). At each layer the algorithm picks a set of clusters and routes source exits
to them; splits, merges, and cross-links emerge from this routing rather than being scheduled
explicitly. All paths through the DAG have similar total weight (duration), ensuring fair races.

For full design rationale see `docs/specs/2026-04-25-exit-driven-dag-generation.md`.

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

### FogRef (`dag.py`)

A `NamedTuple(fog_id, zone)` that disambiguates the same `fog_id` across different zones.

## Width Model

The generator runs a single unified loop from layer 1 to `layers_count - 1`. At each step the
width (number of nodes) of the next layer is determined by a pure function of the current layer:

```
remaining = layers_count - current_layer   # layers left, including the boss layer

if remaining > current_width:              # saturation phase
    target_width = min(max_parallel_paths, sum_net_exits(current_layer))
else:                                      # convergence phase
    target_width = current_width - 1       # strict countdown to 1
```

`sum_net_exits` is the sum of net exits across all nodes in the current layer (see
`count_node_net_exits`). Convergence triggers when `remaining == current_width`, giving exactly
`current_width - 1` convergence layers before the boss. Example: with `max_parallel_paths=4` and
`layers_count=30`, roughly 25 saturation layers produce width up to 4, then 3 convergence layers
reduce width 4 -> 3 -> 2 -> 1, then the boss.

## Routing

After picking `target_width` clusters for the next layer, `route_exits()` distributes source exits
to the new target nodes in three phases:

**Phase 1 (no orphans):** every target receives at least one incoming edge. For each target, a
source is selected that still has free exits and has not yet been connected to that target.
Preference is given to source-target pairings where the target will still have exits remaining
after the new entry (preventing dead ends in the next routing step).

**Phase 1b (no avoidable dead ends):** every source that still has free exits after Phase 1 must
emit at least one outgoing edge. Sources whose sole fog gate was consumed as an entry (bidirectional
pair) are natural terminals and are skipped.

**Phase 2 (saturation):** any remaining (source, target) pair that would not leave the target with
zero exits is connected. This creates cross-links between every source that still has exits and every
reachable target.

Splits, merges, and cross-links emerge from this routing. A source with two exits routed to two
different targets acts as a split; two sources routed to one target act as a merge; a source with
enough exits connected to every target on the next layer creates cross-links.

Multi-edges (two fog gates from the same source to the same target) are forbidden.

## Exit Selection and Proximity

When choosing which exit to use for a new edge, `connect_nodes()` picks uniformly at random from
`_free_exits()`. The pool itself enforces proximity-group constraints, so any choice is admissible
and no ordering heuristic is needed.

`_free_exits()` excludes:
- exits already claimed by an outgoing edge on the source;
- exits filtered out by `compute_net_exits` / `_filter_exits_by_proximity` (entry-vs-exit
  constraint);
- exits sharing a proximity group with an already-used exit on the same source (exit-vs-exit
  mutual exclusion).

When an entry fog belongs to a proximity group, exits in the same group on the target are excluded
from `_free_entries()`. This prevents the player from exiting through a fog gate physically adjacent
to where they entered.

Entry selection prefers entries that leave the target with at least one remaining exit (non-
destructive entries). The generator falls back to any free entry only when no safe choice exists.

**Canonical entry on merges.** When a target already has an incoming edge, any
new edge to the same target reuses that first edge's `entry_fog` (the canonical
entry). The first edge's selection above therefore locks the arrival gate for
every later merge into this target. Because `compute_net_exits` uses set
semantics on consumed entries, reusing the canonical entry is free for the
target's exit capacity, and the merge is rendered in-game as multiple paths
physically converging on the same fog gate (see "Shared Entrance Handling" in
`docs/connection-injection.md`).

## Cluster Compatibility

### Net Exits

`count_net_exits(cluster, N)` calculates the minimum exits remaining after consuming N entry fogs:

1. Classify entries as bidirectional or non-bidirectional
2. Greedily consume non-bidirectional entries first (they do not reduce exits)
3. If more entries are needed, consume bidirectional entries (each reduces exits by 1)
4. Return `total_exits - bidirectional_consumed`

When `proximity_groups` are present, all entry combinations are evaluated and the worst-case
(minimum) net exits is returned. The count is then capped by `_max_independent_exits`: exits
sharing a proximity group are mutually exclusive on a given source, so each group contributes at
most one slot to the capacity.

`count_node_net_exits(dag, node_id)` adapts this for mid-routing: it also subtracts exits already
used by outgoing edges so that the count stays accurate as edges are added incrementally.

This determines the compatibility rules used at load time:

| Operation | Compatibility Rule |
|-----------|-------------------|
| Passant | `count_net_exits(cluster, 1) >= 1` |
| Split (fan-out N) | `count_net_exits(cluster, 1) >= N` |
| Merge (fan-in N) | `len(entry_fogs) >= N` AND `count_net_exits(cluster, N) >= 1` |

Passant-incompatible clusters (zero net exits after consuming one entry) are filtered at load time
by `ClusterPool.filter_passant_incompatible()`.

### Bidirectional Fogs

A fog gate is **bidirectional** if the same `(fog_id, zone)` pair appears in both `entry_fogs` and
`exit_fogs`. Using it as an entry consumes that potential exit (and vice versa).

Special cluster flag: `allow_entry_as_exit` -- the entry fog's bidirectional pair is NOT consumed.
The same gate is entered from one side and exited from the other. Net-exit computation skips the
bidirectional subtraction for these clusters.

### Proximity Groups

A cluster may define `proximity_groups`: lists of fog IDs that are spatially close in-game. Two
constraints apply:

- **Entry-vs-exit**: when an entry fog belongs to a proximity group, exits in the same group are
  excluded. This prevents the player from exiting through a fog gate physically adjacent to where
  they entered.
- **Exit-vs-exit**: on a given source, exits sharing a proximity group are mutually exclusive,
  i.e. only one fog gate per group can be used as an outgoing edge. This keeps successive edges
  from the same source geographically spread instead of clustered on adjacent gates.

## Generation Flow

```
Start Node → Execute Layers (routing) → End Node
   (L0)      L1..layers_count-2         (final boss)
                    |
        [saturation] [convergence]
         width ↑ to max      width -1 per layer
              |                     |
         route_exits           route_exits
         (cross-links           (merges emergent)
          emergent)
```

### 1. Pre-select Final Boss

Before executing any layers, a final boss cluster is selected by weighted random from
`final_boss_candidates`. Its zones are added to `used_zones` so they cannot be consumed by
intermediate layers.

**Zone conflicts**: Some zones are mutually exclusive and cannot both appear in the same run
(declared via `conflicts_with` in `zone_metadata.toml`). When a cluster is selected, conflicting
zones are added to `used_zones` via `_mark_cluster_used()`. Example: `stormveil_margit` (Margit)
and `leyndell_sanctuary` (Morgott) are mutually exclusive.

### 2. Start Node (layer 0)

The start cluster (type `"start"`, Chapel of Anticipation) is placed at layer 0. No entry is
consumed; all exits are available. The node is the sole member of `current_layer_nodes`.

### 3. Plan Layer Types

```python
num_layers = layers_count - 2  # exclude start and final boss
layer_types = plan_layer_types(requirements, num_layers, rng)
```

`plan_layer_types()` builds a list from required legacy dungeons, bosses, mini dungeons, and major
bosses (from config), pads with available types to fill `num_layers`, then shuffles. If
`first_layer_type` is set, it is locked to layer 1.

### 4. Main Loop (layers 1 to layers_count - 2)

For each intermediate layer:

1. Compute `target_width` from the width model above.
2. Pick `target_width` clusters of the planned type via `pick_layer_clusters()`, with type fallback
   when the requested pool is exhausted. Each fallback is recorded as a `FallbackEntry` in the
   generation log.

   **Intra-layer weight balance.** Cluster weights are floats (1 decimal of precision, computed in
   `tools/generate_clusters.py`). The first slot is picked uniformly from the primary pool.
   Subsequent slots use `pick_cluster_weight_matched` with two complementary mechanisms:

   - **Hard window** (`max_layer_spread`, default 2.0): candidates whose weight would push the
     layer's `max - min` spread above this threshold are filtered out before any preference is
     applied. This guarantees that parallel branches stay comparable; with `max_layer_spread=2`,
     a layer cannot mix a w=1 cluster with a w=4 cluster.
   - **Soft preference** (`anchor_tolerance`, default 3.0, exposed as `max_weight_tolerance` in
     the TOML config): inside the window, the matcher widens an anchor band in 0.5 steps around
     the running mean of prior picks. As soon as one candidate fits the current band, a uniform
     pick is made within it. If no band matches up to `anchor_tolerance`, the function falls
     back to a uniform pick across the window-filtered set, never beyond it.

   When the window leaves no in-type candidate, `pick_layer_clusters` falls through to other
   `allowed_types` (recorded as a `FallbackEntry`); the resulting mixed-type layer is then
   rejected by `_check_layer_type_homogeneity` so the seed is rerolled in auto mode.
   `pick_layer_clusters` returns a `weight_deltas` list (one entry per slot, `None` for slot 0
   and for type fallbacks); the generator records each non-None value as `NodeEntry.weight_delta`
   and the spoiler log surfaces it as `dw=<n>` next to `w=<n>`. A `weight_delta > anchor_tolerance`
   flags picks that landed in the window-uniform fallback rather than within an anchor band.
   Layers are independent: no anchor is carried across layers. A safety net in the validator
   (`_check_layer_weight_spread`) re-asserts the invariant on the final DAG.
3. Create `DagNode` instances, mark their zones used.
4. Call `route_exits(dag, current_layer_nodes, next_nodes, rng)` to connect the layers.
5. Advance `current_layer_nodes = next_nodes`.

### 5. End Node

The pre-selected final boss cluster is placed at `layers_count - 1`. `route_exits` connects the
single remaining node (convergence has reduced width to 1) to the boss.

### 6. Tier Assignment

Tiers are assigned in a post-pass after DAG construction is complete. This ensures `total_layers`
is exact (not estimated), guaranteeing monotonically non-decreasing tiers across layers. See "Tier
Interpolation" below.

## Tier Interpolation

Tiers are assigned in a **post-pass** after DAG construction is complete. The progression curve is
configurable via `tier_curve`:

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
- `< 1.0` (e.g. 0.6): front-loaded tiers -- harder early, gentler late game plateau
- `= 1.0`: equivalent to linear
- `> 1.0` (e.g. 1.5): back-loaded tiers -- easy early, punitive late game ramp

Special cases:
- Single layer: always tier 1
- `final_tier` is clamped to range [1, 28]

This maps to FogMod's enemy scaling SpEffects.

## Budget and Weight

Each cluster has a **weight** (approximate traversal time in minutes). Each path through the DAG
has a total weight = sum of node weights along the path.

**Balance constraint** (from config):
- `tolerance`: maximum allowed weight spread between heaviest and lightest paths (default 5)

The validator checks that all paths have similar weights. If the spread exceeds tolerance, a
warning is produced (not a hard error).

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
| `structure.max_parallel_paths` | 3 | Maximum concurrent nodes per layer |
| `structure.max_exits` | 3 | Maximum fan-out when routing (caps exit count per source) |
| `structure.max_entrances` | 3 | Maximum fan-in per target node |
| `structure.layers_count` | 30 | Total layers (start + intermediates + final boss) |
| `structure.first_layer_type` | None | Force type for first layer |
| `requirements.major_bosses` | 8 | Number of major boss layers |
| `structure.final_boss_candidates` | `{"leyndell_erdtree": 1, "enirilim_radahn": 1}` | Possible end bosses (zone -> weight). Also accepts a flat list (all weight 1). |
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
3. **Requirements vs layers_count**: error if total requirements exceed `layers_count - 2` (the intermediate layer budget)
4. **final_boss_candidates**: all zones must exist in the pool, all weights must be >= 1
5. **Pool capacity**: warning when `requirement * max_parallel_paths > pool_size` for any cluster type. With parallel nodes, each planned layer consumes up to `max_parallel_paths` clusters of the same type. Example: `major_bosses=12` with `max_parallel_paths=4` requires up to 48 clusters from a pool of 38, which will cause type exhaustion during generation.

### DAG Validation (`validator.py`)

Post-generation checks on the built DAG:

1. **Structural**: start/end exist, all edges valid, no backward edges
2. **Reachability**: all nodes reachable from start (BFS), all can reach end (reverse BFS)
3. **Entry consistency**: entry fog count matches incoming edge count
4. **No duplicate edges**: prevents trivial Y-patterns
5. **Entry zone membership**: entry fog zone belongs to target cluster zones
6. **Layer type homogeneity**: all nodes in a layer share the same cluster type (prevents unfair asymmetry between parallel branches)
7. **Requirements**: minimum zone type counts met
8. **Layer count**: few layers = warning
9. **Event flag budget**: total flag allocation within budget

## Required Zones

`requirements.zones` lists zones that must appear in the generated DAG.
They are prioritized at construction time: when the cluster picker for
a layer slot has at least one candidate covering a still-needed required
zone, the draw is restricted to that subset. Weight matching is bypassed
for that pick.

Properties:

- Greedy placement: each required zone is consumed by the first
  compatible slot encountered.
- Per-zone consumption: between slots of the same layer, the set of
  remaining required zones shrinks so a single zone is not
  over-prioritized.
- Post-process validation (`_check_requirements`) remains as a safety
  net for pathological cases (e.g. a required zone whose cluster
  conflicts with one already placed earlier).
- A required zone whose cluster type is excluded from `allowed_types`
  is reported by `_check_zone_types_allowed` as a config error before
  generation starts.

The generation log records each placement under a `REQUIRED ZONES`
section, useful for debugging when the safety-net validator fires.

## Excluded Zones

`requirements.exclude_zones` is the hard counterpart of `requirements.zones`.
Where required zones are a *soft* picker preference (validated post-hoc),
excluded zones are a *hard* pool filter: every cluster whose zones intersect
the excluded set is removed from the pool once at load (`ClusterPool.exclude_zones`,
called from `main.py`), before roundtable merge and passant filtering. The
removed clusters can never appear anywhere in the DAG, including as the final
boss, because all downstream stages read from the filtered pool.

`validate_exclusions` (`validator.py`) runs first, on the unfiltered pool, and
rejects the config (fail-fast) when an excluded zone is unknown, is also
required (`zones`), or is also an explicit `final_boss_candidates` entry. The
`"all"` keyword is exempt from the final-boss conflict check ("all except X");
only the case where exclusion removes every possible final boss is an error.

Granularity is cluster-level: excluding any zone drops the entire cluster that
contains it. Boss zones (the main use case) are typically single-zone clusters,
so this is intuitive; a multi-zone cluster is removed as a whole.

## Allowed Cluster Types

By default, the DAG can include any of four cluster types:
`legacy_dungeon`, `mini_dungeon`, `boss_arena`, `major_boss`. The
`requirements.allowed_types` setting restricts this to a subset,
enabling modes such as boss-rush (`["boss_arena", "major_boss"]`) or
legacy-marathon (`["legacy_dungeon"]`).

Semantics:

- Only types listed in `allowed_types` participate in the DAG: they
  appear in the initial requirement list, in padding, and in
  convergence type selection.
- The per-type minimums (`legacy_dungeons`, `bosses`, `mini_dungeons`,
  `major_bosses`) apply only to types present in `allowed_types`.
  Minimums for excluded types are silently ignored; a warning is
  emitted at config load if a non-zero minimum is ignored.
- The final boss is selected from `final_boss_candidates` and is
  always a major_boss, independent of `allowed_types`. A config like
  `allowed_types = ["mini_dungeon", "boss_arena"]` produces a DAG
  whose intermediate layers contain no major bosses but which still
  ends on a major-boss node.
- `structure.first_layer_type`, if set, must be in `allowed_types`.
- A required zone (`requirements.zones`) whose cluster type is
  excluded from `allowed_types` is reported as a validator error
  (the zone would be unreachable).

## References

- Generator: `speedfog/generator.py`
- DAG data structures: `speedfog/dag.py`
- Planner: `speedfog/planner.py`
- Clusters: `speedfog/clusters.py`
- Validator: `speedfog/validator.py`
- Output: `speedfog/output.py`
- Config: `speedfog/config.py`
- Spec: `docs/specs/2026-04-25-exit-driven-dag-generation.md`
