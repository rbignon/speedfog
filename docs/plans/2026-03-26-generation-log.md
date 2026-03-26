# Generation Log Design Spec

**Goal:** Add detailed diagnostic logging to DAG generation, capturing planned vs actual types per layer, operations, pool state at fallback points, crosslink decisions, and a summary. This replaces time-consuming manual analysis when debugging DAG structure issues (e.g., type fallbacks, pool exhaustion).

**Motivation:** Debugging a type fallback at layer 22 of seed `speedfog_41b53697886d` required manually counting 75 nodes across 22 layers, computing zone cross-blocking for 40 major_boss clusters, and deducing the planned type from uniform layers. A generation log would have made the root cause (major_boss pool exhaustion) immediately visible.

## Architecture

### Data Model

A `GenerationLog` dataclass accumulates structured events during `generate_dag`. Events are appended at key points in the generation flow. The log is attached to `GenerationResult` and serialized to `logs/generation.log` by `export_generation_log()` in `output.py`.

```
generate_dag()          output.py
  |                       |
  |-- GenerationLog       |
  |     .plan_event       |
  |     .layer_events[]   |-- export_generation_log(log, path)
  |     .fallbacks[]      |     -> logs/generation.log
  |     .crosslink_event  |
  |                       |-- export_spoiler_log(dag, path)
  v                       |     -> logs/spoiler.txt
GenerationResult.log      |
```

No Python logging module, no callbacks. The log is a pure data product of generation, like the DAG itself.

### Event Types

#### PlanEvent (emitted once, after `plan_layer_types`)

Captures the planner's decisions before any layer execution:

- `requirements`: dict of type -> count from config (e.g., `{"legacy_dungeon": 2, "boss_arena": 7, ...}`)
- `target_total`: total layer count chosen by rng
- `merge_reserve`: layers reserved for convergence
- `num_intermediate`: number of planned intermediate layers
- `planned_types`: the ordered list of types returned by `plan_layer_types` (post-shuffle)
- `pool_sizes`: dict of type -> total cluster count at generation start
- `final_boss`: cluster id of the pre-selected final boss
- `reserved_zones`: set of zone ids reserved for final boss / prerequisite

#### LayerEvent (emitted once per layer, including start, first_layer, convergence)

Captures what happened at each layer:

- `layer`: layer index
- `phase`: `"start"`, `"first_layer"`, `"planned"`, `"convergence"`, `"prerequisite"`, `"final_boss"`
- `planned_type`: the type from `plan_layer_types` (None for start/convergence/final_boss)
- `operation`: `"START"`, `"PASSANT"`, `"SPLIT"`, `"MERGE"`, `"REBALANCE"`
- `branches_before`: branch count entering this layer
- `branches_after`: branch count after this layer
- `nodes`: list of `NodeEntry` (one per node created at this layer)
- `fallbacks`: list of `FallbackEntry` (one per type fallback that occurred)

#### NodeEntry (nested in LayerEvent)

- `cluster_id`: cluster id
- `cluster_type`: cluster type string
- `weight`: cluster weight
- `role`: `"primary"`, `"passant"`, `"split_child"`, `"merge_target"`, `"rebalance_split"`, `"rebalance_merge"`

#### FallbackEntry (nested in LayerEvent)

Emitted each time `pick_cluster_with_type_fallback` skips the preferred type:

- `branch_index`: which branch triggered the fallback
- `preferred_type`: what type was requested
- `actual_type`: what type was actually picked
- `reason`: `"pool_exhausted"` or `"zone_conflict"` (all candidates zone-blocked)
- `pool_remaining`: dict of type -> available count at the moment of fallback

#### CrosslinkEvent (emitted once, after `add_crosslinks`)

- `eligible_pairs`: total count of pairs found by `find_eligible_pairs`
- `added`: count of crosslinks successfully added
- `skipped`: count of pairs skipped (surplus exhausted by earlier crosslinks)
- `added_details`: list of `CrosslinkDetail`
- `skipped_details`: list of `CrosslinkDetail`

#### CrosslinkDetail

- `source_id`: source node id
- `target_id`: target node id
- `reason`: None for added, `"no_surplus_exits"` or `"no_available_entries"` for skipped

### Output Format

`generation.log` is a plain text file, human-readable, with sections:

```
================================================================
GENERATION LOG (seed: 241523476)
================================================================

PLAN
  Final boss: jaggedpeak_bayle_f21a
  Reserved zones: jaggedpeak_bayle
  Requirements: legacy_dungeon=2, boss_arena=7, mini_dungeon=8, major_boss=8
  Target layers: 25 (min=25, max=30, merge_reserve=6)
  Intermediate layers: 22
  Planned sequence: [legacy_dungeon, boss_arena, mini_dungeon, major_boss, ...]
  Pool sizes: boss_arena=84, legacy_dungeon=32, mini_dungeon=69, major_boss=40

LAYERS
  L0 [start] START 0->2 branches
    chapel_start [start, w=1] (primary)

  L1 [first_layer] PASSANT 2->2 branches
    stormveil [legacy_dungeon, w=8] (passant)
    caelid_redmane [legacy_dungeon, w=3] (passant)

  L2 [planned=legacy_dungeon] PASSANT 2->2 branches
    academy_redwolf [legacy_dungeon, w=6] (primary)
    mohgwyn [legacy_dungeon, w=6] (passant)

  L3 [planned=mini_dungeon] SPLIT 2->4 branches
    gravesite_gaol [mini_dungeon, w=6] (primary, split 1->2)
    sewer [mini_dungeon, w=6] (passant)
    mountaintops_catacombs [mini_dungeon, w=4] (split_child)
    rauhbase_catacombs [mini_dungeon, w=4] (passant)

  ...

  L22 [planned=major_boss] PASSANT 4->4 branches
    haligtree_malenia [major_boss, w=5] (primary)
    liurnia_lakesidecave_boss [boss_arena, w=1] (passant) *** FALLBACK ***
    haligtree_elphael [legacy_dungeon, w=3] (passant) *** FALLBACK ***
    limgrave_murkwatercave_boss [boss_arena, w=1] (passant) *** FALLBACK ***
    Fallbacks:
      b1: wanted major_boss, got boss_arena (pool_exhausted: major_boss=0, boss_arena=61, legacy_dungeon=16, mini_dungeon=47)
      b2: wanted major_boss, got legacy_dungeon (pool_exhausted: major_boss=0, boss_arena=60, legacy_dungeon=16, mini_dungeon=47)
      b3: wanted major_boss, got boss_arena (pool_exhausted: major_boss=0, boss_arena=60, legacy_dungeon=15, mini_dungeon=47)

  --- CONVERGENCE (4 branches remaining) ---

  L24 [convergence] MERGE 4->3 branches
    ...

  L28 [final_boss] 1->0 branches
    jaggedpeak_bayle [final_boss, w=4]

CROSSLINKS
  Eligible pairs: 45, Added: 32, Skipped: 13
  Added:
    L3 gravesite_gaol_7342 -> L4 caelid_tower_boss_2e44
    L5 altus_elemer_e0b2 -> L6 snowfield_cave_boss_7d29
    ...
  Skipped:
    L7 mountaintops_catacombs_boss_d2b5 -> L8 gravesite_catacombs_1e77: no surplus exits
    ...

SUMMARY
  Layers: 29 (22 planned + 1 start + 1 first_layer + 4 convergence + 1 final_boss)
  Nodes: 92
  Crosslinks: 32
  Fallbacks: 3 (L22: major_boss pool exhausted)
  Pool at end: boss_arena=58, legacy_dungeon=12, mini_dungeon=43, major_boss=4
```

### Integration Points in generator.py

The `GenerationLog` is instantiated at the top of `generate_dag` and populated at these points:

| Code location | Event |
|---|---|
| After `plan_layer_types` (step 5) | `PlanEvent` |
| After start node creation (step 1) | `LayerEvent(phase="start")` |
| After first_layer_type loop (step 4) | `LayerEvent(phase="first_layer")` |
| Inside the main `for layer_type in layer_types` loop (step 6), after each layer completes | `LayerEvent(phase="planned")` |
| Inside convergence `while len(branches) > 1` (step 7), after each layer | `LayerEvent(phase="convergence")` |
| After prerequisite injection (step 8) | `LayerEvent(phase="prerequisite")` if injected |
| After end node (step 9) | `LayerEvent(phase="final_boss")` |

Fallback events are captured inside `pick_cluster_with_type_fallback` by passing the log object, or by wrapping the call sites in the main loop to detect when the returned cluster type differs from the requested type.

**Preferred approach for fallback capture:** Compare the returned cluster's type against the requested type at each call site. This avoids threading the log through utility functions:

```python
pc = pick_cluster_weight_matched(candidates, ...)
if pc is None:
    pc = pick_cluster_with_type_fallback(clusters, layer_type, ...)
if pc is not None and pc.type != layer_type:
    # Record fallback
    layer_event.fallbacks.append(FallbackEntry(...))
```

Pool remaining counts are computed at fallback time using `_available_count()` style logic (filter by used_zones and reserved_zones).

### Integration Points in crosslinks.py

`add_crosslinks` currently returns just `int`. It will return a `CrosslinkEvent` instead (or accept a `GenerationLog` to append to). The caller in `generate_dag` (or `main.py`) attaches it to the log.

**Preferred approach:** `add_crosslinks` returns a `CrosslinkEvent` alongside the count. The main code attaches it:

```python
crosslink_count, crosslink_event = add_crosslinks(dag, rng)
dag.crosslinks_added = crosslink_count
log.crosslink_event = crosslink_event
```

### CLI and File Layout Changes

#### CLI

- `--spoiler` flag renamed to `--logs`
- Old behavior: `--spoiler` generated `seed_dir/spoiler.txt`
- New behavior: `--logs` generates `seed_dir/logs/spoiler.txt` + `seed_dir/logs/generation.log`
- `append_boss_placements_to_spoiler` writes to `seed_dir/logs/spoiler.txt`

#### Seed directory layout (before)

```
seeds/<seed>/
  graph.json
  spoiler.txt          # --spoiler
  config_speedfog.toml
  launch_speedfog.bat
  ...
```

#### Seed directory layout (after)

```
seeds/<seed>/
  graph.json
  config_speedfog.toml
  launch_speedfog.bat
  ...
  logs/
    spoiler.txt        # --logs
    generation.log     # --logs
```

### GenerationResult Extension

```python
@dataclass
class GenerationResult:
    dag: Dag
    seed: int
    validation: ValidationResult
    attempts: int
    log: GenerationLog   # new field
```

### New Files

- `speedfog/generation_log.py`: `GenerationLog`, `PlanEvent`, `LayerEvent`, `NodeEntry`, `FallbackEntry`, `CrosslinkEvent`, `CrosslinkDetail` dataclasses + `export_generation_log()` function

Keeping the log model and serialization in a dedicated file avoids bloating `dag.py` or `output.py`.

### Testing

- **Unit tests for `GenerationLog` serialization**: build a log with known events, call `export_generation_log`, verify output format contains expected sections and values.
- **Integration test**: run `generate_dag` on a small config, verify `result.log` contains plan event, layer events for each layer, and summary.
- **Fallback test**: set up a config/cluster pool that forces type fallback, verify fallback entries appear in the log with correct pool counts.
- **Crosslink test**: verify crosslink event captures added/skipped counts.
- **CLI test**: verify `--logs` creates `logs/` directory with both files, and `--spoiler` no longer exists.

### Documentation Updates

- `CLAUDE.md`: update directory structure, commands section (`--spoiler` -> `--logs`), output description
- `docs/architecture.md`: update output diagram and seed directory layout
- `README.md`: update CLI usage
- `docs/care-package.md`: references `--spoiler`
