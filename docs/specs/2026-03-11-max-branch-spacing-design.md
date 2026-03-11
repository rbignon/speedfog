# Max Branch Spacing Design

**Date:** 2026-03-11
**Status:** Draft

## Problem

SpeedFog's DAG generation can produce long linear corridors where a player has no alternative path. In observed races:

- **Run 1 (seed 111883420):** Player stuck on Malenia (layer 11) with nearest split at layer 7 — 4 layers back. Other players faced a 9-layer linear stretch (layers 15–24).
- **Run 2 (seed 223320154):** Player "wospins" stuck at Putrescent Knight (layer 19) with nearest split on their branch at layer 5 — **14 layers of linear corridor** with zero choices.

A core design goal of SpeedFog is that players can switch paths when they hit a wall. Long linear stretches undermine this.

**Key insight:** Branch spacing must be measured **per-branch**, not globally. A DAG can have many splits, but if they're all on other branches, they're inaccessible to the stuck player (fog gates are one-way).

## Design

### Per-Branch Stale Counter

Each active branch tracks `layers_since_last_split: int`, counting how many layers have passed since the player on that branch last had a choice (a split point).

**Counter rules:**

| Event | Counter value |
|-------|--------------|
| Branch created by split | `0` (player just made a choice) |
| Branch does a PASSANT | `+= 1` |
| Two branches merge | Result inherits `max(A, B)` |
| Start node (layer 0) | All initial branches start at `0` |

Merges do NOT reset the counter because a merge doesn't give the player a new choice — they arrived from one side and can't access the other branch's earlier nodes.

### Forced Split Mechanism

When any branch reaches `layers_since_last_split >= max_branch_spacing`:

**Case 1 — Room to split (`num_branches < max_parallel_paths`):**

1. **Biased cluster selection:** Instead of `pick_cluster_with_type_fallback` (type-only), first filter candidates through `can_be_split_node(c, 2)`. Fall back to normal selection if no split-capable cluster exists.
2. **Forced operation:** `determine_operation` bypasses probability roll and returns SPLIT.
3. **Target branch:** The branch with the highest counter (most stale) gets the split.
4. **Best-effort fallback:** If no split-capable cluster is available (exhausted pool), accept PASSANT, increment counter, retry next layer.

**Case 2 — Saturated (`num_branches == max_parallel_paths`):**

1. **Force merge first** on any valid pair of branches to free a slot. The stale branch does a PASSANT this layer (counter += 1).
2. **Next layer:** Room is now available; the forced split triggers normally (Case 1).
3. If the stale branch is itself part of the forced merge, its counter propagates via `max()` into the merged branch, and the split happens on that branch next layer.

### Multiple Stale Branches

If multiple branches exceed the threshold simultaneously and only one slot is available, split the most stale branch first. Others wait one more layer.

### Configuration

New field in `StructureConfig`:

```python
max_branch_spacing: int = 4  # 0 = disabled
```

**Validation constraint:** `min_branch_age` must be strictly less than `max_branch_spacing` (when both are enabled). If `min_branch_age >= max_branch_spacing`, config validation raises an error. With default values (`min_branch_age=0`, `max_branch_spacing=4`), no conflict is possible.

```python
# In StructureConfig.__post_init__
if max_branch_spacing > 0 and min_branch_age >= max_branch_spacing:
    raise ValueError(
        f"min_branch_age ({min_branch_age}) must be < "
        f"max_branch_spacing ({max_branch_spacing})"
    )
```

## Integration with Existing Flow

### Current flow

```
pick_cluster(type) → determine_operation(cluster, branches) → execute
```

### New flow

```
assess_branch_urgency → pick_cluster(type, biased if urgent) → determine_operation(cluster, branches, force?) → execute → update_counters
```

Before cluster selection each layer:

1. Compute `max_stale = max(b.layers_since_last_split for b in branches)`
2. `needs_forced_split = max_stale >= max_branch_spacing and max_branch_spacing > 0`
3. `needs_forced_merge = needs_forced_split and num_branches >= max_parallel_paths`

### Changes to `determine_operation`

Add parameter `force: LayerOperation | None = None`. When set, bypass probability logic and return the forced operation (after verifying cluster supports it). If cluster doesn't support the forced operation, fall back to normal logic.

### Changes to `Branch` dataclass

Add field: `layers_since_last_split: int = 0`

### Counter updates after execution

- **SPLIT:** Each child branch → `layers_since_last_split = 0`
- **PASSANT:** Each branch → `layers_since_last_split += 1`
- **MERGE:** Merged branch → `max(incoming counters)`. Non-merged branches doing passant → `+= 1`.

## Interactions with Existing Features

### `is_near_end` (forced merge phase)

`max_branch_spacing` is **disabled** during the near-end convergence phase. The player is close to the end — the remaining distance is short enough that backtracking isn't a major concern.

### Crosslinks

Crosslinks are a complementary mechanism (lateral connections between branches). `max_branch_spacing` ensures structural splits exist; crosslinks add optional shortcuts. No interaction issues.

### `first_layer_type`

When `first_layer_type` is configured, all branches get a PASSANT on the first layer. Counters go to 1. Normal behavior.

## Testing Strategy

- **Unit tests for counter logic:** Verify counter updates on split/merge/passant.
- **Unit tests for forced split:** Verify biased selection triggers when threshold reached.
- **Unit tests for saturated case:** Verify merge-then-split sequence.
- **Config validation:** Verify `min_branch_age >= max_branch_spacing` is rejected.
- **Statistical test:** Generate N seeds, assert no branch ever exceeds `max_branch_spacing + 1` (the +1 accounts for the best-effort fallback when no split-capable cluster exists).
- **Regression:** Existing tests pass unchanged when `max_branch_spacing = 0` (disabled).
