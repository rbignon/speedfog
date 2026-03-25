# Weight-Matched Cluster Selection

**Date:** 2026-03-25
**Status:** Implemented

## Problem

When the DAG has parallel branches (after a SPLIT), each branch receives a cluster independently via uniform random selection within the layer's type. This can produce large weight imbalances between parallel branches on the same layer.

**Example:** On a legacy_dungeon layer with 2 branches, one player gets `ensis` (weight 4, ~4 min median) while the other gets `mountaintops_sol` (weight 1, ~1 min median). That is a 3-minute discrepancy on a single layer, purely from randomness.

This matters because parallel branches represent a choice point: the player picks a fog gate and commits to one branch. Balanced weights across branches mean the choice is about route preference, not about which branch is objectively shorter.

### Current state: weights are computed but unused

Cluster weights are computed during cluster generation (`generate_clusters.py`, line 1708) from zone weights in `zone_metadata.toml`, using logarithmic aggregation for multi-zone clusters:

```
cluster_weight = round(avg_zone_weight * (1 + 0.5 * ln(n_zones)))
```

These weights are stored in `clusters.json` and written to the DAG output (`graph.json`), but `generator.py` never reads `.weight` during cluster selection. This was likely lost during DAG generation refactorings.

### Weight distributions by type (current clusters.json)

| Type | Weight range | Distribution |
|------|-------------|--------------|
| mini_dungeon (69) | 1-6 | 44 at w=1, 15 at w=2, 3 at w=3, then sparse |
| legacy_dungeon (32) | 1-8 | Spread across range |
| major_boss (36) | 1-5 | 28 at w=1, 4 at w=2, then sparse |
| boss_arena (84) | 1-2 | 80 at w=1, 4 at w=2 (nearly uniform, filter has minimal effect) |
| final_boss (6) | 2-4 | Small pool, rarely on parallel branches |

The feature primarily impacts **legacy_dungeon** (wide weight range, significant time differences) and **mini_dungeon/major_boss** (large pools with outliers).

## Design

### Core idea: anchor + progressive tolerance

When a layer has multiple parallel branches, the first cluster selected acts as a weight anchor. Secondary clusters for other branches are filtered to have a similar weight, with progressive tolerance widening to guarantee selection always succeeds.

### The function

```python
def pick_cluster_weight_matched(
    candidates: list[ClusterData],
    used_zones: set[str],
    rng: random.Random,
    anchor_weight: int,
    filter_fn: Callable[[ClusterData], bool] = lambda c: True,
    *,
    reserved_zones: frozenset[str] = frozenset(),
    max_tolerance: int = 3,
) -> ClusterData | None:
    """Pick a cluster with weight close to anchor_weight.

    Filters candidates once (zone availability + filter_fn), then applies
    progressive weight tolerance starting from exact match.
    Falls back to any available cluster if no match within max_tolerance.
    """
    if max_tolerance <= 0:
        # Weight matching disabled: uniform random from available
        available = [
            c for c in candidates
            if not any(z in used_zones or z in reserved_zones for z in c.zones)
            and filter_fn(c)
        ]
        return rng.choice(available) if available else None

    available = [
        c for c in candidates
        if not any(z in used_zones or z in reserved_zones for z in c.zones)
        and filter_fn(c)
    ]
    if not available:
        return None

    for tol in range(0, max_tolerance + 1):
        matched = [c for c in available if abs(c.weight - anchor_weight) <= tol]
        if matched:
            return rng.choice(matched)

    # No match within tolerance: pick any available cluster
    return rng.choice(available)
```

**Key properties:**

- **Single pass for availability:** Zone overlap, reserved zones, and capability (`can_be_passant_node`, etc.) are checked once. The tolerance loop only re-filters by weight on the already-filtered list.
- **Composable filter_fn:** Callers pass capability checks (e.g., `can_be_passant_node`) via `filter_fn`, composed with weight matching naturally.
- **Exact match first:** Tolerance starts at 0 (prefer same weight), then widens. This avoids unnecessarily pairing a weight-3 anchor with a weight-2 cluster when a weight-3 is available.
- **Guaranteed success:** If no candidate matches within `max_tolerance`, falls back to uniform random from all available candidates. Generation never fails due to weight matching.
- **Explicit opt-out:** `max_tolerance <= 0` skips the tolerance loop entirely and returns uniform random (current behavior).
- **Absolute tolerance:** +/-1 weight unit per step. This reflects player experience (a 1-weight difference is ~1 minute, which is what players feel) rather than relative percentage.

### Tolerance behavior

For `max_tolerance=3`:

| Step | Filter | Anchor=3 accepts | Anchor=1 accepts |
|------|--------|-------------------|-------------------|
| 0 | exact | [3] | [1] |
| 1 | +/-1 | [2, 4] | [0, 2] |
| 2 | +/-2 | [1, 5] | [0, 3] |
| 3 | +/-3 | [0, 6] | [0, 4] |
| fallback | any | all | all |

For mini_dungeon at anchor=1 (the most common case), step 0 already matches 40/69 clusters. Step 1 matches 64/69.

### Call sites

Weight matching applies at **5 sites** where secondary clusters are picked for parallel branches.

#### Sites in `generate_dag()` (main loop)

These 3 sites currently call `pick_cluster_with_type_fallback()`. They have a `primary_cluster` variable available as anchor.

1. **SPLIT layers** (line ~1969): non-split branches get passant clusters
2. **MERGE layers** (line ~2118): non-merged branches get passant clusters
3. **PASSANT layers** (line ~2170): branches after the first (first reuses `primary_cluster`)

Replacement pattern:

```python
pc = pick_cluster_weight_matched(
    clusters.get_by_type(layer_type),
    used_zones,
    rng,
    anchor_weight=primary_cluster.weight,
    max_tolerance=config.structure.max_weight_tolerance,
    reserved_zones=reserved_zones,
)
if pc is None:
    # Type exhausted: cross-type fallback without weight constraint
    pc = pick_cluster_with_type_fallback(
        clusters, layer_type, used_zones, rng,
        reserved_zones=reserved_zones,
    )
```

**Cross-type fallback drops weight constraint.** When the preferred type is exhausted and fallback picks from a different type, the anchor weight from the primary is no longer meaningful. Applying it would just reduce variety for no fairness gain.

#### Sites in convergence functions

These 2 sites currently call `pick_cluster_with_filter()` with `can_be_passant_node`. They do NOT have cross-type fallback (they raise `GenerationError` on exhaustion).

4. **`execute_passant_layer()`** (line ~1322): picks a cluster for each branch in a loop. **There is no pre-selected primary.** The first branch's cluster is picked normally and then serves as anchor for the remaining branches.

```python
# In execute_passant_layer loop:
anchor_weight: int | None = None
for i, branch in enumerate(branches):
    if anchor_weight is None:
        # First branch: pick normally (establishes anchor)
        cluster = pick_cluster_with_filter(
            candidates, used_zones, rng, can_be_passant_node,
            reserved_zones=reserved_zones,
        )
        if cluster is not None:
            anchor_weight = cluster.weight
    else:
        # Subsequent branches: weight-matched
        cluster = pick_cluster_weight_matched(
            candidates, used_zones, rng, anchor_weight,
            filter_fn=can_be_passant_node,
            max_tolerance=config.structure.max_weight_tolerance,
            reserved_zones=reserved_zones,
        )
    ...
```

5. **`execute_merge_layer()`** (line ~1577): the merge cluster is selected first and serves as anchor. Non-merged branches get passant clusters weight-matched to it.

```python
# In execute_merge_layer, non-merged branch loop:
passant_cluster = pick_cluster_weight_matched(
    candidates, used_zones, rng,
    anchor_weight=cluster.weight,  # merge cluster is anchor
    filter_fn=can_be_passant_node,
    max_tolerance=config.structure.max_weight_tolerance,
    reserved_zones=reserved_zones,
)
```

Both convergence functions thread `config` through their signatures (adding it as parameter to `execute_passant_layer` which currently lacks it).

### What does NOT change

- **Primary cluster selection:** remains uniform random within the preferred type (via `_pick_cluster_biased_for_split` or `pick_cluster_with_type_fallback`). The primary determines the operation (SPLIT/MERGE/PASSANT) and serves as the weight anchor.
- **`determine_operation()`:** unchanged.
- **Type fallback logic:** preserved in the final fallback path for the 3 main-loop sites. Convergence functions do not have type fallback today and this spec does not add it.
- **Single-branch layers:** no secondary picks, so weight matching is irrelevant.

### Configuration

Add `max_weight_tolerance` to `StructureConfig`:

```python
# In config.py, StructureConfig dataclass:
max_weight_tolerance: int = 3
"""Maximum weight tolerance for cluster matching on parallel branches.
0 disables weight matching (uniform random, current behavior)."""
```

```toml
# In pool config TOML:
[structure]
max_weight_tolerance = 3  # 0 = disabled
```

Validation: non-negative integer. No upper bound needed (the fallback handles any value).

## Interactions with existing features

### max_branch_spacing forced splits

When a forced SPLIT triggers due to a stale branch, the non-split branches still receive passant clusters. Weight matching applies to these secondaries with the primary (split cluster) as anchor. No special handling needed.

### Convergence phase

During convergence (`prefer_merge=True`), `execute_merge_layer` and `execute_passant_layer` are called. Weight matching applies (sites 4-5 above). The fallback guarantees convergence is never blocked.

### Rebalance operations

`_rebalance_merge_first()` and `_rebalance_split_first()` pick clusters internally for their specific topological needs (merge-capable, split-capable). These are structural operations where the capability constraint is already tight. Weight matching is NOT applied to rebalance cluster selection to avoid over-constraining an already-restrictive search.

## Trade-offs

### Accepted: usage bias toward common weights

Weight-1 mini_dungeons (40/69) are selected both as primaries (58% chance) and as secondaries (matched by most anchors). Weight-6 mini_dungeons (4/69) are rarely primary (5.8%) and rarely matched as secondaries. This is the desired behavior: outlier-weight clusters appear less on parallel layers, which is exactly the fairness goal.

For variety across seeds, this slightly increases the existing skew toward common clusters. Acceptable since weight-1 already dominates the pool.

### Accepted: primary is unconstrained

The primary cluster is chosen without weight consideration. This means the anchor itself can be an outlier, forcing tolerance widening for secondaries. Constraining the primary would require knowing the branch count before cluster selection, which inverts the current flow where the operation is decided after the primary is chosen.

### Accepted: boss_arena is nearly unaffected

boss_arena has 80/84 clusters at weight 1. Weight matching has no practical effect for this type. This is fine because boss_arena weight variance is already minimal.

## Testing strategy

### Unit tests

- **Exact match preferred:** when candidates include an exact weight match and a +/-1 match, the exact match is always chosen (deterministic with seeded rng and single exact candidate).
- **Tolerance widening:** when no exact match exists, +/-1 is tried before +/-2. Verify with a candidate pool that has no exact match but has a +/-1 match.
- **Fallback to any:** when no candidate matches within `max_tolerance`, any available candidate is returned.
- **Disabled (`max_tolerance=0`):** produces uniform random selection, equivalent to current behavior.
- **`filter_fn` composition:** weight matching + `can_be_passant_node` together. A candidate that matches weight but fails passant check is excluded.
- **Empty candidates:** returns None.
- **Zone exclusion:** candidates with overlapping zones are filtered out before weight matching.

### Simulation (post-implementation)

Run DAG generation on the standard pool (100+ seeds) and compare before/after:

- **Weight variance per layer:** for layers with 2+ branches, compute `max_weight - min_weight` across branches.
- **Generation success rate:** should be identical (fallback guarantees).
- **Cluster usage distribution:** compare Gini coefficient to confirm variety degradation is small.
