# Cluster-First Selection Design

## Problem

The current DAG generator decides the operation (split/merge/passant) first, then
searches for a compatible cluster. This creates severe distribution bias:

- Clusters with more fog gates (entries/exits) pass compatibility filters more often
- Pearson correlation between connectivity and selection frequency: 0.82 (mini_dungeon), 0.87 (boss_arena)
- Gini coefficient for mini_dungeons: 0.215 (top cluster appears in 74% of seeds, bottom in 22%)
- 30% generation failure rate due to split-compatible pool exhaustion

## Solution

Invert the selection logic: pick a random cluster uniformly first, then determine
what operation it supports based on its fog gate structure and current DAG state.

Simulation results (3000 seeds, standard racing pool config):

| Metric          | Current | Cluster-First |
|-----------------|---------|---------------|
| Failure rate    | 29.9%   | 12.4%         |
| Gini mini_dung  | 0.215   | 0.063         |
| Gini boss_arena | 0.188   | 0.083         |
| Gini major_boss | 0.329   | 0.287         |
| Avg paths       | 535     | 102           |
| Linear DAGs     | 0%      | 0%            |

## Design

### What changes

1. **New `pick_cluster_uniform()`**: uniform selection among available clusters
   (only zone overlap check, no capability filter)

2. **New `determine_operation()`**: takes pre-selected cluster + DAG state,
   returns operation based on cluster capabilities:
   - If cluster can split AND room available: P(split) = split_probability
   - If cluster can merge AND valid merge pair exists: P(merge) = merge_probability
   - Both possible: weighted competition
   - Otherwise: passant (guaranteed by upstream filter)

3. **Rewrite main loop in `generate_dag()`**: replace
   `decide_operation() -> pick_cluster` with `pick_cluster -> determine_operation()`

4. **`execute_*_layer` become `execute_*_node`**: take pre-selected cluster
   instead of searching internally

5. **`ClusterPool.filter_passant_incompatible()`**: exclude clusters that can
   never serve as passant nodes (1 bidirectional entry + 1 exit = 0 net exits).
   Called once at load time.

### What stays the same

- All compatibility helpers: `can_be_passant_node()`, `can_be_split_node()`,
  `can_be_merge_node()`, `compute_net_exits()`, `select_entries_for_merge()`,
  `_pick_entry_and_exits_for_node()`
- Merge helpers: `_find_valid_merge_indices()`, `_has_valid_merge_pair()`
- `execute_forced_merge()` (end-of-DAG logic)
- `generate_with_retry()`
- End node (final boss) selection
- Config parameters (split_probability/merge_probability keep their semantics)

### Multi-branch handling

For a layer with N active branches:
1. **N=1**: pick cluster, check split capability, decide
2. **N>1, N<max**: pick cluster for potential merge/split, decide based on capabilities
3. **N==max_paths**: no split possible, only merge or passant

Non-involved branches each pick their own cluster uniformly.

### Excluded clusters

Clusters with 1 entry + 1 exit (bidirectional) are filtered out at load time:
- 2 mini_dungeons: caelid_abandonedcave_2746, graveyard_grave_2a33
- 5 boss_arenas: cerulean_mausoleum_boss_0d19, dragonbarrow_cave_boss_f4c5,
  gravesite_mausoleum_boss_259d, rauhbase_mausoleum_boss_2ecc, scadualtus_mausoleum_boss_a068
- 2 legacy_dungeons: haligtree_f5b5, leyndell_pretower_64cd
