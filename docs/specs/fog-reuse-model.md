# Fog Reuse Model: Breaking Split/Merge Monopolies

## Problem

When generating DAGs, certain clusters appear in a disproportionate number of runs
because they are the only ones capable of serving as split or merge nodes. The DAG
generator picks split/merge nodes from tiny pools, while passant nodes are picked
from large pools. This creates repetitive gameplay.

### Measured frequencies (500 runs, default config)

**max_branches=3:**

| Cluster                     | Frequency | Reason                             |
|-----------------------------|:---------:|------------------------------------|
| Sage's Cave                 | 60.8%     | 1 of only 2 mini_dungeon merge(2)  |
| Black Knife Catacombs       | 59.4%     | 1 of only 2 mini_dungeon merge(2)  |
| Sealed Tunnel - Onyx Lord   | 36.8%     | 1 of 6 boss_arena merge(2)         |
| Crystalian Duo              | 35.8%     | 1 of 6 boss_arena merge(2)         |
| Valiant Gargoyles           | 33.0%     | 1 of 6 boss_arena merge(2)         |
| Redmane Castle Boss         | 31.8%     | the only boss_arena split(3)       |
| Typical passant cluster     | ~12%      | diluted across 59-74 options       |

**max_branches=4:** Redmane Castle Boss rises to 51.2%, Sage's Cave and Black Knife
Catacombs remain at ~47%, and merge(2) boss_arenas rise to 38-46%.

### Root cause

The current model couples a cluster's topological role to its physical fog gate
geometry: a cluster can only be a split(N) node if it has exactly N net exits after
consuming 1 entry, and a merge(N) node if it has N entries and exactly 1 net exit.

"Net exits" means exit fogs remaining after removing bidirectional pairs consumed
by the entry. A fog is bidirectional when the same `(fog_id, zone)` pair appears in
both `entry_fogs` and `exit_fogs` — walking back through the entry fog would return
the player to the source zone, so it is removed from available exits.

This creates very small pools for split/merge roles:

| Type           | Role     | Pool size | Per-cluster probability |
|----------------|----------|:---------:|:-----------------------:|
| mini_dungeon   | merge(2) | 2         | 50.0%                   |
| mini_dungeon   | split(2) | 7         | 14.3%                   |
| boss_arena     | split(3) | 1         | 100%                    |
| boss_arena     | merge(2) | 6         | 16.7%                   |
| legacy_dungeon | merge(2) | 4         | 25.0%                   |
| major_boss     | split(3) | 1         | 100%                    |

Meanwhile, passant pools are 59 (mini_dungeon), 74 (boss_arena), 11 (legacy_dungeon).

### What doesn't work

**Flexible fan-out** (allowing clusters to serve in lower-N roles, e.g. a split(3)
cluster used as split(2)): this was tested and makes the problem worse. Multi-fog
clusters become eligible for MORE roles, so they appear even MORE often. Redmane
Castle Boss goes from 29% to 48% (mb=3) and 52% to 73% (mb=4). It also does nothing
for the critical mini_dungeon merge(2) bottleneck (still 2 clusters, since no
mini_dungeon has merge(3+) capability to "downgrade").

## Solution: Fog Reuse Model

The key insight: SpeedFog does not need to map the return direction through fog
gates. When a player arrives through fog A, walking back through fog A does not need
to return them to the source zone — it can lead forward to the next layer instead.

This enables two mechanisms:

### 1. Shared entrance for merges

Currently, merge(N) requires a cluster with N distinct entry fogs. Under the reuse
model, multiple branches all connect to the same single entry fog. The merge happens
at the connection level, not the cluster level.

**Before:** merge(2) requires 2 entry fogs + 1 net exit. Only 2 mini_dungeons qualify.

**After:** merge(N) requires 2+ entry fogs + 1+ exit fogs.

Branch A's exit fog → cluster Z's fog A (warp to Z).
Branch B's exit fog → cluster Z's fog A (same warp to Z).
The player arrives at the same spawn point regardless of which branch they took.

New eligibility: `can_be_merge(cluster, n)` requires `len(entry_fogs) >= 2` and
`len(exit_fogs) >= 1`.

### 2. Entry-as-exit for splits (boss arenas only)

Currently, consuming an entry fog removes its bidirectional exit pair. Under the
reuse model, consuming an entry does NOT remove any exits, because the "return"
direction is repurposed as a forward connection.

**Restriction:** entry-as-exit is limited to `boss_arena` clusters. In a dungeon
(mini or legacy), if the player can advance in the tree by walking back through the
entry fog without completing the dungeon, they will skip it. In a boss arena there
is nothing to skip — the boss IS the content.

**Before:** A boss_arena with entry_fogs=`[A]` and exit_fogs=`[A, B]`
(A is bidirectional) has `count_net_exits(1) = 1` → passant only.

**After:** All exits remain available → 2 usable exits → split(2) capable.

The player arrives through fog A from zone X. Walking through fog A again does not
return to X — it warps to zone Y (next layer). Walking through fog B warps to zone Z
(another branch).

New eligibility: `can_be_split(cluster, n)` requires `len(exit_fogs) >= n` and
`len(entry_fogs) >= 1`. The `>=` is intentional — excess exits beyond `num_out` are
left unconnected. Unconnected exit fogs don't generate fog walls (FogMod only places
fog walls for Graph-connected edges), so they are invisible in-game.

For non-boss_arena clusters, the existing model applies: entry-as-exit is disabled,
bidirectional exit pairs are consumed by entries.

### Eligibility constraints

The reuse model applies conservatively:

- **Shared entrance (merges):** cluster must have `>= 2` entry fogs AND `>= 1`
  exit fog. Single-entry clusters are NOT eligible for shared-entrance merges.
  This preserves dead-end bosses (Malenia, Rykard, Bayle — 1 entry, 1 exit) as
  final boss candidates rather than diluting them as merge points.
- **Entry-as-exit (splits):** cluster must be `boss_arena` type AND have `>= 2`
  exit fogs. Dungeons (mini or legacy) never use entry-as-exit.
- Both mechanisms can be overridden per-zone via `data/zone_metadata.toml`
  (see [Per-zone overrides](#per-zone-overrides)).

### Pool impact

With these constraints, pool sizes increase significantly while keeping dead-end
bosses excluded:

| Type           | Role     | Current | Reuse | Notes                         |
|----------------|----------|:-------:|:-----:|-------------------------------|
| mini_dungeon   | merge(2) | 2       | large | all with 2+ entries qualify    |
| mini_dungeon   | split(2) | 7       | 7     | no change (no entry-as-exit)   |
| boss_arena     | merge(2) | 6       | large | all with 2+ entries qualify    |
| boss_arena     | split(3) | 1       | more  | entry-as-exit adds candidates  |
| legacy_dungeon | merge(2) | 4       | large | all with 2+ entries qualify    |

Previously unusable single-entry/single-exit bidirectional clusters (Malenia,
Rykard, Bayle, etc.) remain excluded — these are more valuable as final boss
candidates at the end of the tree.

### Gameplay implications

- **No return through entry fog (boss arenas only):** walking back through the
  entry fog in a boss arena leads forward (to another layer), not back to the
  source zone. This only affects boss arenas where there is no content to skip.
  An existing overlay mod shows available exits in-game.
- **Shared merge spawn point:** all paths through a merge arrive at the same
  physical location. This reduces spatial variety at merge points, but merge nodes
  are a small fraction of all nodes (~15% of operations).

### Per-zone overrides

Both mechanisms can be overridden per-zone in `data/zone_metadata.toml`:

```toml
# Allow/deny shared entrance (for merges) on a specific zone.
# Default: true for clusters with 2+ entries, false for 1-entry clusters.
[zones.some_zone]
allow_shared_entrance = false

# Allow/deny entry-as-exit (for splits) on a specific zone.
# Default: true for boss_arena, false for all other types.
[zones.some_boss_arena]
allow_entry_as_exit = false
```

These overrides flow through `generate_clusters.py` into `clusters.json` as
cluster-level fields, following the existing pattern for weight and type overrides.

Use cases:
- Disable shared entrance on a zone where arriving from multiple directions to the
  same spawn point creates a confusing in-game experience
- Disable entry-as-exit on a boss arena where the entry fog's return direction has
  special significance (e.g., leads to a scenic vista or story-relevant area)
- Enable entry-as-exit on a non-boss_arena cluster if testing shows it works well

## Technical Design

### Phase 1: Shared entrance for merges

This phase has the largest impact (merge pools are the primary bottleneck) and is
the simplest to implement.

#### DuplicateEntrance() safety analysis

FogMod's `GameDataWriterE.cs` event generation loop (L3260-3549) iterates
`node.To` (exit edges), not entrance edges. For each exit, it generates events in
the **exit's map EMEVD** (`eventMap = warp4.Map` at L3421). This means:

- Exit A (map M1) → Original Entrance → event in M1's EMEVD
- Exit B (map M2) → Duplicate Entrance → event in M2's EMEVD

These are **different EMEVD files**. No duplication conflict.

Additional safety checks:
- **Boss defeat flags** (L3363): from `edge2.Side` (exit side), not entrance.
  Each exit has its own Side data. No conflict.
- **dictionary7** (L940-943): keyed by `entrance.Name`. Overwrite is harmless
  because original and duplicate share the same Side (same WarpPoint data).
- **edge.Pair null** (L947): this checks the exit's Pair, not the entrance's.
  The exit edge's Pair is set by `Graph.Construct()`, unaffected by entrance
  duplication.
- **Scaling** (L1964+): applied per-area, not per-edge. Unaffected.

**Verdict:** `DuplicateEntrance()` is safe for shared-entrance merges. An in-game
test remains necessary to confirm no edge cases exist.

#### FogMod Graph.DuplicateEntrance()

FogMod already provides this method (Graph.cs:394):

```csharp
public Edge DuplicateEntrance(Edge entrance)
{
    Edge edge = new Edge {
        Expr = side.Expr,
        To = side.Area,
        Name = entrance.Name,
        Text = entrance.Text,
        IsFixed = entrance.IsFixed,
        Side = side,              // same WarpPoint data → same warp destination
        Type = EdgeType.Entrance
    };
    Nodes[side.Area].From.Add(edge);
    return edge;
}
```

Creates a new entrance edge with identical WarpPoint data (same destination
coordinates) but distinct identity. No Pair (no paired exit edge). FogMod generates
EMEVD events, fog walls, and VFX for each connected edge automatically.

#### Python-side changes (Phase 1)

**`ClusterData`** (`clusters.py`): add two fields:

```python
allow_shared_entrance: bool = False  # from clusters.json, set by generate_clusters
allow_entry_as_exit: bool = False    # from clusters.json, set by generate_clusters
```

Default values are computed in `generate_clusters.py` based on type/fog counts,
then overridden by `zone_metadata.toml` entries.

**Eligibility functions** (`generator.py`):

```python
def can_be_merge_node(cluster, num_in):
    if cluster.allow_shared_entrance:
        # Shared entrance: only need 1 entry + 1 exit regardless of fan-in
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= 1
    else:
        # Original model: need num_in distinct entries + 1 net exit
        return len(cluster.entry_fogs) >= num_in and count_net_exits(cluster, num_in) == 1
```

**`count_net_exits` / `compute_net_exits`**: kept for non-shared-entrance clusters.
Only removed when entry-as-exit is also implemented (Phase 2).

**`execute_merge_layer`**: the most significant change. Currently selects N distinct
entries and assigns one per branch. Under shared entrance:
1. Select 1 entry fog from the merge cluster
2. All merging branches connect to this same entry
3. `DagNode.entry_fogs` stores just `[entry_fog]` (not N copies)
4. graph.json connections: multiple connections with different `exit_gate` but
   identical `entrance_area` and `entrance_gate`

**`select_entries_for_merge`**: when `allow_shared_entrance`, select 1 entry
(not N). The same entry is reused for all merging branches.

#### C# changes (Phase 1)

**ConnectionInjector**: detect shared-entrance connections (multiple connections
with identical `entrance_area` + `entrance_gate`) and handle them:
1. Group connections by `(entrance_area, entrance_gate)`
2. For single-connection groups: connect normally via `Graph.Connect()`
3. For multi-connection groups: connect the first via `Graph.Connect()`, subsequent
   ones via `graph.DuplicateEntrance()` + `Graph.Connect()`

**ZoneTrackingInjector**: no changes. It scans all EMEVD files for WarpPlayer
instructions, which includes events generated for duplicate entrances.

#### generate_clusters.py changes (Phase 1)

Compute `allow_shared_entrance` default:
- `true` if `len(entry_fogs) >= 2`
- `false` if `len(entry_fogs) < 2`
- Overridden by `[zones.X] allow_shared_entrance = true/false`

Write `allow_shared_entrance` into each cluster dict in `clusters.json`.

### Phase 2: Entry-as-exit for splits (boss arenas)

After Phase 1 is validated in-game, Phase 2 adds the entry-as-exit mechanism.

#### FogMod Graph.Connect() with ignorePair

`Graph.Connect(exit, entrance, ignorePair)` (Graph.cs:415) auto-links Pair edges
for bidirectional return warps, unless `ignorePair=true`:

```csharp
if (exit == entrance.Pair || ignorePair)
    return;  // skip Pair auto-linking
```

For entry-as-exit: connect the incoming entrance with `ignorePair=true` to prevent
auto-linking the Pair (which IS the bidirectional exit we want to reuse as a forward
exit). Then connect that Pair as an exit to a different destination separately.

#### Connection ordering in ConnectionInjector

For entry-as-exit splits, the same physical fog gate serves as both entrance and
exit on the same cluster. Connection ordering matters:

1. **Incoming connection** (from previous layer): connect exit → entrance with
   `ignorePair=true`. This avoids auto-linking the Pair, leaving it free.
2. **Outgoing connection** (to next layer): connect the entrance's Pair (which is
   the bidirectional exit) → next entrance, as a normal forward connection.

Connections in graph.json are ordered by source layer (ascending), which naturally
satisfies this constraint. ConnectionInjector must assert that for entry-as-exit
connections, the entrance is not already connected when processing the incoming
connection.

#### Python-side changes (Phase 2)

**Eligibility functions** (`generator.py`):

```python
def can_be_split_node(cluster, num_out):
    if cluster.allow_entry_as_exit:
        # Entry-as-exit: all exit_fogs available (entry doesn't consume its pair)
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= num_out
    else:
        # Original model: exit count after consuming 1 entry
        return count_net_exits(cluster, 1) == num_out

def can_be_passant_node(cluster):
    if cluster.allow_entry_as_exit:
        return len(cluster.entry_fogs) >= 1 and len(cluster.exit_fogs) >= 1
    else:
        return count_net_exits(cluster, 1) == 1
```

**`count_net_exits` / `compute_net_exits`**: can be removed entirely once Phase 2
is complete, since both merge and split eligibility use the simplified checks.

**`execute_split_layer`**: `compute_net_exits` replaced with `cluster.exit_fogs`
directly (all exits available) for clusters with `allow_entry_as_exit`.

**`pick_entry_with_max_exits`**: the `min_exits` parameter becomes unnecessary
for entry-as-exit clusters since entries never consume exits.

#### C# changes (Phase 2)

**ConnectionInjector**: for entry-as-exit connections, detect when an entrance's
Pair is used as an exit in a later connection:
1. If the incoming connection's entrance has a Pair that appears as an exit in
   another connection from the same node: use `ignorePair=true`
2. Assert the entrance is not already connected (ordering guard)

#### generate_clusters.py changes (Phase 2)

Compute `allow_entry_as_exit` default:
- `true` if `type == "boss_arena"` and `len(exit_fogs) >= 2`
- `false` for all other types
- Overridden by `[zones.X] allow_entry_as_exit = true/false`

Write `allow_entry_as_exit` into each cluster dict in `clusters.json`.

## Verification plan

### Phase 1

- Unit tests for new merge eligibility functions (Python)
- Pool-size verification: reproduce pool size tables from this spec
- Generation simulation: 500 runs, verify no cluster exceeds 20% frequency
  (excluding start/end nodes)
- C# integration test: generate a seed with shared-entrance merges, verify
  `DuplicateEntrance()` connections produce valid EMEVD events and fog walls
- In-game test: verify shared-entrance merges warp correctly from both branches

### Phase 2

- Unit tests for entry-as-exit split eligibility (Python)
- Pool-size verification for split pools
- C# integration test: verify `ignorePair=true` connections and Pair reuse
- In-game test: verify entry-as-exit splits lead forward (not backward)
  in boss arenas only
