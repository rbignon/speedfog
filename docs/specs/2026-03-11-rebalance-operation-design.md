# REBALANCE Operation Design

**Date:** 2026-03-11
**Status:** Proposed
**Supersedes:** Portions of `2026-03-11-max-branch-spacing-design.md` (Case 2, forced merge phase)

## Problem

The max_branch_spacing feature (implemented per the spacing design spec) introduced three separate mechanisms to enforce branch spacing:

1. **`_execute_spacing_rebalance`** (~210 lines): A standalone function that combines merge+split on the same layer when branches are saturated. Called as a special case before the normal operation flow.
2. **`force_op` bloc** (~90 lines): Biased cluster selection in the main loop that overrides normal selection when spacing enforcement triggers.
3. **`is_near_end` guard**: Disables spacing enforcement during convergence, causing 10-12 layer linear stretches at the end of runs — the hardest part of the run where players are most likely to get stuck.

These three mechanisms solve the same problem (keeping branches interesting) through different code paths. The convergence phase (`execute_forced_merge`) is spacing-unaware, producing the worst linear stretches in the run.

## Design

### REBALANCE as a Native Operation

Add `REBALANCE` to the `LayerOperation` enum as a 4th operation alongside PASSANT, SPLIT, and MERGE.

```python
class LayerOperation(Enum):
    PASSANT = auto()
    SPLIT = auto()
    MERGE = auto()
    REBALANCE = auto()  # merge 2 branches + split 1 stale branch (same layer)
```

A REBALANCE merges 2 branches and splits 1 stale branch on the same layer, keeping the total branch count constant (N → N). It uses 2 clusters: one merge-capable, one split-capable.

REBALANCE is internal to the generator — it is purely a control-flow enum variant. The graph.json serialization operates on nodes and edges, not on operations. A REBALANCE produces a split node and a merge node on the same layer, which appear in graph.json identically to nodes produced by separate SPLIT and MERGE operations. Spoiler logs similarly serialize nodes, not operations.

### determine_operation Changes

`determine_operation` gains the logic to return REBALANCE. The conditions are:

1. `max_branch_spacing > 0` (feature enabled)
2. `len(branches) >= 3` (minimum for REBALANCE: 1 split + 2 merge)
3. `max(b.layers_since_last_split for b in branches) >= max_branch_spacing` (threshold reached)
4. At least one valid merge pair exists among branches **other than the split target** (anti-micro-merge: different parent nodes)

When all 4 conditions are met, REBALANCE is returned **before** the probability roll for split/merge. It is a defensive override. When REBALANCE isn't possible (< 3 branches) and `num_branches < max_parallel_paths`, a forced SPLIT is returned instead.

**Priority hierarchy in `determine_operation`:**
1. **Spacing enforcement** (highest) — REBALANCE if stale + >= 3 branches + merge pair; forced SPLIT if stale + not saturated
2. **`prefer_merge`** — if `prefer_merge=True`, bypass probability roll, return MERGE
3. **Normal probability roll** — split_prob / merge_prob / passant

The `force: LayerOperation | None` parameter is removed. It is replaced by:
- Internal REBALANCE logic (covers the old `force_op = SPLIT` saturated case)
- `prefer_merge: bool = False` parameter (covers forced convergence)

When `prefer_merge=True`, the probability roll is bypassed in favor of MERGE. REBALANCE can still override if the staleness threshold is exceeded — spacing is maintained even during convergence. This replaces the old `is_near_end` forced merge behavior.

When `prefer_merge=True`, `min_branch_age` is bypassed for merge eligibility (same as the old `force_op == LayerOperation.MERGE` bypass). This ensures convergence can always proceed regardless of branch age.

### execute_rebalance_layer

New helper function at the same level as `execute_merge_layer` and `execute_passant_layer`:

```python
def execute_rebalance_layer(
    dag: Dag,
    branches: list[Branch],
    layer_idx: int,
    tier: int,
    layer_type: str,
    clusters: ClusterPool,
    used_zones: set[str],
    rng: random.Random,
    config: Config,
    *,
    reserved_zones: frozenset[str] = frozenset(),
) -> list[Branch]:
```

Steps:
1. Identify the most stale branch (highest `layers_since_last_split`) — split target
2. Find 2 merge candidates among branches **other than the split target** (bypass `min_branch_age`, enforce anti-micro-merge: different parent nodes)
3. Pick a split-capable cluster (preferred type first, then all types)
4. Pick a merge-capable cluster (preferred type first, then all types)
5. Execute split on the stale branch → 2 child branches with `layers_since_last_split = 0`
6. Execute merge on the pair → 1 merged branch initialized with `layers_since_last_split = max(A, B)`
7. Passant remaining branches (carrying their existing counters)
8. Update counters via `update_branch_counters(SPLIT, split_children=..., passant_branches=...)`: split children set to 0, all other branches (merged + passant) get `+= 1`. The merged branch ends up at `max(A, B) + 1`.

**Key difference from `_execute_spacing_rebalance`:** Always returns `list[Branch]`, never `None`. Raises `GenerationError` on failure (consistent with other helpers). The caller does not handle fallback.

**Cluster selection bias:** The helper picks split-capable and merge-capable clusters by iterating all cluster types (preferred type first). This biases toward clusters with 2+ exits for the split slot. The impact is ~1-2 additional biased selections per run during convergence, acceptable given the gameplay benefit. If distribution imbalance is observed in practice, alternatives can be explored.

### Unified Convergence

`execute_forced_merge` is deleted. Both the near-end convergence and the post-loop convergence become a single loop that calls `determine_operation` normally:

```python
# Post-loop convergence (replaces execute_forced_merge + is_near_end)
while len(branches) > 1:
    tier = compute_tier(...)
    operation, fan = determine_operation(..., prefer_merge=True)

    if operation == LayerOperation.REBALANCE:
        branches = execute_rebalance_layer(...)
    elif operation == LayerOperation.MERGE:
        branches = execute_merge_layer(...)
    else:
        # Can't merge yet (anti-micro-merge) — passant to diverge nodes
        branches = execute_passant_layer(...)
    # Counter updates happen inside each helper (same as main loop)
    current_layer += 1
```

**How convergence is forced:** `prefer_merge=True` bypasses the probability roll in favor of MERGE. REBALANCE can still override when a branch exceeds the staleness threshold.

**Guard-rail:** If the convergence loop uses more than `merge_reserve * 2` layers (i.e., `layers_used > merge_reserve * 2`, which is > 12 for `max_parallel_paths=4`) without converging to 1 branch, raise `GenerationError`.

Termination argument: REBALANCE maintains branch count (N → N), but resets the split target's counter to 0. On the next iteration, `determine_operation` sees no stale branch (counter just reset), so it returns MERGE (due to `prefer_merge=True`), reducing branch count by 1. The pattern is at worst: REBALANCE → MERGE → REBALANCE → MERGE → ... Each REBALANCE/MERGE pair reduces branch count by 1, so convergence completes in at most `2 * (N - 1)` layers. With `max_parallel_paths=4`, that's 6 layers — well within `merge_reserve * 2 = 12`.

**`is_near_end` in the main loop:** The `is_near_end` flag and its associated block (lines 1575-1598) are removed. The main loop runs all planned layers normally. Convergence happens exclusively in the post-loop while loop.

### merge_reserve Adjustment

`merge_reserve` increases from `max_parallel_paths` to `max_parallel_paths + 2` to absorb the additional layers REBALANCE may inject during convergence.

With 4 branches and a REBALANCE intercalated, convergence takes at worst ~6 layers instead of ~4.

```python
merge_reserve = config.structure.max_parallel_paths + 2
```

Justification: worst case is 4 branches with REBALANCE intercalated. Pattern: REBALANCE (4→4, 1 layer) + MERGE (4→3, 1 layer) + MERGE (3→2, 1 layer) + MERGE (2→1, 1 layer) = 4 layers. With a passant for anti-micro-merge divergence, 5-6 layers. `max_parallel_paths + 2 = 6` covers this.

## Code Removed

| Code | ~Lines | Reason |
|------|--------|--------|
| `_execute_spacing_rebalance` | 210 | Replaced by `execute_rebalance_layer` |
| `force_op` bloc + biased selection | 90 | Integrated into `determine_operation` + `execute_rebalance_layer` |
| `execute_forced_merge` | 70 | Replaced by unified convergence loop |
| `is_near_end` flag + guard | 15 | No longer needed |
| `force` parameter in `determine_operation` | 15 | Replaced by internal REBALANCE logic + `prefer_merge` |
| `min_age` bypass in main loop MERGE bloc | 3 | Handled by `prefer_merge` in `determine_operation` |
| **Total removed** | **~400** | |

## Code Added

| Code | ~Lines | Purpose |
|------|--------|---------|
| `execute_rebalance_layer` | 100 | Rebalance helper (merge+split+passant) |
| REBALANCE logic in `determine_operation` | 20 | Detect when REBALANCE is needed |
| `prefer_merge` logic in `determine_operation` | 10 | Replace `force` + `is_near_end` |
| Convergence while loop in `generate_dag` | 20 | Unified convergence |
| **Total added** | **~150** | |

**Net change: ~-250 lines.**

## Testing Strategy

**Modified tests:**
- Tests for `_execute_spacing_rebalance` → rewritten for `execute_rebalance_layer` (same logic, no `None` return)
- Tests for `determine_operation` with `force=` → replaced by tests with REBALANCE conditions (saturated + stale)
- Tests for `execute_forced_merge` → replaced by convergence loop tests

**New tests:**
- `test_determine_operation_returns_rebalance`: Verify the 4 conditions trigger REBALANCE
- `test_determine_operation_prefer_merge`: Verify `prefer_merge` bypasses probability roll
- `test_rebalance_during_convergence`: Generate DAGs, verify convergence doesn't create stretches > threshold + 2
- `test_convergence_terminates`: Verify the convergence loop terminates within `merge_reserve * 2` layers

**Statistical test:**
- `test_max_branch_spacing_statistical`: Unchanged threshold (`max_branch_spacing + 2`), but now covers the entire run including convergence (previously convergence was excluded)

## Interactions with Existing Features

### Crosslinks
No interaction. Crosslinks add lateral connections; REBALANCE handles structural branching.

### first_layer_type
No interaction. Counter propagation after first_layer_type passant is unchanged.

### max_layers / merge_reserve
`merge_reserve` increased by 2 to accommodate REBALANCE during convergence. No other changes.

### min_branch_age
Bypassed during convergence (`prefer_merge=True`), same as before. No change in behavior.

### Cluster distribution
REBALANCE biases cluster selection toward split-capable clusters (~1-2 additional biased picks per run during convergence). Acceptable trade-off for eliminating 10-12 layer linear stretches. Can be revisited if distribution imbalance is observed.
