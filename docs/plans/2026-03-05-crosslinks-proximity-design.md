# Crosslinks Proximity Filtering

## Problem

`crosslinks.py` does not respect `proximity_groups` when computing surplus exits/entries. This allows cross-links to create connections where entry and exit fogs are spatially adjacent in-game (e.g., Sage's Cave: entry via `AEG099_001_9002` and exit via `AEG099_001_9000`, which are in the same proximity group).

## Design

### Approach: Filter inside `_surplus_exits` / `_surplus_entries`

Add proximity filtering directly into these two functions, matching the logic already used by `_filter_exits_by_proximity` in `generator.py`.

### Changes

1. **`_surplus_exits()`**: After the existing bidirectional filter, for each consumed entry on the node (from incoming edges), remove surplus exits that share a proximity group with that entry.

2. **`_surplus_entries()`**: Symmetric — for each consumed exit on the node (from outgoing edges), remove surplus entries that share a proximity group with that exit.

3. **Shared helper**: Extract a `_is_blocked_by_proximity(cluster, fog_ref, consumed_fogrefs)` helper that checks whether a FogRef shares a proximity group with any of the consumed FogRefs.

4. **Imports**: Reuse `parse_qualified_fog_id` from `speedfog.clusters` and `_fog_matches_spec` from `speedfog.generator`.

### Tests

- Surplus exit blocked when it shares a proximity group with an existing entry
- Surplus entry blocked when it shares a proximity group with an existing exit
- No false blocking when fogs are in different proximity groups
