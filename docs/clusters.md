# Cluster Generation

**Date:** 2026-02-15
**Status:** Active

How SpeedFog transforms FogRando's `fog.txt` into curated zone clusters for DAG generation.

## Overview

`tools/generate_clusters.py` parses FogRando's zone definitions and fog gate data, groups connected zones into clusters, classifies entry/exit fogs, and outputs `clusters.json`. This is a one-time process run after updating FogRando dependencies.

```
fog.txt (FogRando)
   ├─ Areas section (zones, world connections, boss flags)
   ├─ Entrances section (fog gates between zones)
   └─ Warps section (teleporter-style connections)
         ↓
   Parse → Build world graph → Classify fogs → Generate clusters → Filter → Output
         ↓
   clusters.json v1.9
```

## Key Concepts

### Zone vs. Cluster

- **Zone**: A named area from fog.txt (e.g., `stormveil`, `academy_town`, `volcano_jail`)
- **Cluster**: A group of zones connected by guaranteed world transitions (no progression requirements). Once a player enters a cluster via an entry fog, they can traverse all zones within it and exit via any exit fog.

### World Connections vs. Fog Gates

**World connections** (`Area.To` in fog.txt):
- Physical terrain transitions (stairs, doors, elevators)
- Used to determine which zones cluster together
- Bidirectional by default unless tagged `drop` (one-way, e.g., coffin rides)

**Fog gates** (`Entrances`/`Warps` sections):
- Randomizable teleport gates placed by FogMod
- Classified as entry/exit based on cluster topology
- These are what FogMod actually randomizes

## World Graph

### Guaranteed Connections

A world connection is **guaranteed** if it has no conditions, or only conditions that are always satisfied (because SpeedFog gives all key items at start).

Always-satisfied conditions include: Great Runes, medallions, keys (Academy, Rusty, Imbued Sword, etc.), DLC passes, and logic/scale passes.

Connections with zone-based conditions (e.g., "requires reaching zone X") are **not** guaranteed and excluded from clustering.

### Graph Building

1. Collect all `Area.To` connections from fog.txt
2. Skip if condition requires zone progression (not guaranteed)
3. Skip drops whose target is a `major_boss` arena (see below)
4. Mark `drop`-tagged connections as unidirectional
5. Check for reverse connections → bidirectional edges
6. Build adjacency graph

### Drops into major boss zones

When a non-boss zone has a `drop` connection into a `major_boss` arena (e.g., `academy_courtyard` → `academy_redwolf`, `leyndell_bedchamber` → `leyndell_sanctuary`), flood-fill from the upstream zone would otherwise form a mixed `{upstream, boss}` cluster. The primary-zone selection picks the boss (highest type priority), so the cluster inherits `major_boss` type while still containing a non-boss zone. The multi-zone downgrade to `legacy_dungeon` does not apply because at least one entry fog lives in the boss arena.

These incoming drops are therefore filtered out at graph-build time. The boss arena still forms a clean single-zone `major_boss` cluster via flood-fill from itself; the upstream zone forms its own cluster without the boss. "Major boss arena" here means a zone that has `BossTrigger` AND sits on a fog tagged `major` (i.e., the subset of `get_major_zones()` with `has_boss=True`).

## Cluster Generation

**Algorithm**: For each zone, flood-fill via guaranteed world connections to find all reachable zones. The union of start zone + reachable zones forms a cluster. Deduplicate by frozenset of zones.

**Result**: All possible connected zone groups. A zone may appear in multiple clusters (different entry points lead to different reachable sets when drops create asymmetric connectivity).

## Fog Classification

This is the most complex part. Each fog gate gets classified for each cluster it touches.

### Skipped Fogs

| Tag | Reason |
|-----|--------|
| `norandom` | Fixed connection, not randomizable |
| `split` | Ashen capital alternate (shares canonical fog) |
| `door` | Morgott barriers (pre-connected) |
| `caveonly`, `catacombonly`, `forgeonly`, `gaolonly` | Dungeon-entrance-only (FogMod marks unused in crawl mode) |

### One-Way Warps (exit only)

| Type | Tags | Examples |
|------|------|----------|
| Unique warps | `unique` | Coffins, abductors, sending gates |
| Minor warps | `minorwarp` | Transporter chests (no fog model) |
| Return warps | `return` (not `returnpair`) | Post-boss returns |

Added to `exit_fogs` with `"unique": true` for the ASide zone only. BSide is not added.

### Paired Sending Gates

Fogs tagged `uniquegate` (without `unique`) represent paired sending gates (e.g., Redmane gates, DLC waygates). Gates connecting the same zone pair are grouped into **one bidirectional connection**, preventing double-counting.

### Backportals

Boss room self-warps (entry and exit in the same zone). Added to both `entry_fogs` and `exit_fogs` for the ASide zone only.

### Standard Bidirectional Fogs

Both sides (ASide and BSide) are added as both entry and exit for their respective zones. Exception: sides with `Cond` requiring their own zone are dropped (would create circular dependency).

## Bidirectional Fog Detection

A fog is **bidirectional within a cluster** if the same `(fog_id, zone)` pair appears in both `entry_fogs` and `exit_fogs`. This matters for DAG generation because using a bidirectional fog as an entry consumes a potential exit.

The `compute_net_exits()` function in `clusters.py` handles this:

```
net_exits = total_exits - bidirectional_entries_consumed
```

When consuming N entries:
1. Prefer non-bidirectional entries (don't reduce exits)
2. If more entries needed, consume bidirectional (each reduces exits by 1)

## Entry Zones and Entry Fogs

### Entry Zones

An **entry zone** is a zone within a cluster that has no unidirectional incoming edge from another cluster zone. These are the zones reachable from outside without needing to traverse internal drops.

Example:
- Cluster: `{volcano_town, volcano_abductors, volcano_jail}`
- World graph: `volcano_town → volcano_abductors` (drop, one-way)
- Entry zones: `{volcano_town, volcano_jail}` (volcano_abductors has incoming drop)

### Entry vs. Exit Fog Sources

- **Entry fogs**: collected only from entry zones (represent actual entry points from outside)
- **Exit fogs**: collected from ALL zones in the cluster (once inside, all exits available)

## Side Text

Fog gates connecting two zones can have different descriptions depending on which side you're on. The `side_text` field provides zone-specific context.

Example: AEG099_002_9000 connecting `stormveil_ramparts` and `stormveil_godrick`:
- ASide (ramparts): `"to Godrick arena"`
- BSide (godrick): `"from ramparts"`

When this fog is an entry to the `stormveil_godrick` cluster, the entry fog gets `side_text: "from ramparts"` (BSide text, matching the entering zone). Used in the spoiler log and visualization.

## Unique Exit Fogs

Exit fogs tagged `"unique": true` (coffins, DLC warps) are separated in `ClusterData`:

- `exit_fogs`: bidirectional exits only (for standard DAG connections)
- `unique_exit_fogs`: one-way exits (cannot be used as DAG edges, but their MSB entities need removal by `VanillaWarpRemover`)

## Zone Type Assignment

Heuristic classification based on map IDs and fog.txt tags:

| Type | Detection Rules |
|------|----------------|
| `start` | Tag `start` or name `chapel_start` |
| `final_boss` | Name `leyndell_erdtree` or `leyndell2_erdtree` |
| `major_boss` | Has `BossTrigger` AND connected to fog with `major` tag |
| `boss_arena` | Has `BossTrigger` (no major fog) |
| `legacy_dungeon` | Maps `m10_`-`m16_`, or fortress zone (Caria, Shaded, Redmane, Sol) |
| `mini_dungeon` | Maps `m30` (catacombs), `m31` (caves), `m32` (tunnels), `m35` (sewers), `m39` (gaols) |
| `underground` | Maps `m12_` (Siofra, Ainsel, Deeproot) — excluded from DAG |

Overrides from `data/zone_metadata.toml` take priority (e.g., DLC `m20_` maps → `legacy_dungeon`).

## Weight System

Weight = approximate traversal time in minutes.

**Defaults** from `zone_metadata.toml`:
- `legacy_dungeon`: 10 min
- `mini_dungeon`: 4 min
- `boss_arena`: 2 min
- `start`: 1 min
- `final_boss`: 4 min

Per-zone overrides for notably larger/smaller areas (e.g., `stormveil`: 15, `leyndell`: 20).

Cluster weight = sum of all zone weights in the cluster.

## Defeat Flag Discovery

Each cluster needs a boss defeat flag for race tracking.

**Method 1**: Direct — check each zone's `DefeatFlag` field from fog.txt.

**Method 2**: Traversal — some clusters (e.g., `leyndell_erdtree`) don't have DefeatFlag on their own zones. The generator traverses via unconditional `Area.To` connections and `norandom` fog gates to find a reachable boss zone (max 5 hops).

Fallback: 0 if no flag found (cluster has no associated boss).

## Roundtable Merge

Roundtable Hold is a hub accessible via menu teleport, but fog.txt treats it as a separate cluster. `ClusterPool.merge_roundtable_into_start()` merges it into the start cluster:

1. Find start cluster (type `"start"`) and roundtable cluster (zone `"roundtable"`)
2. Merge zones, entry_fogs, exit_fogs, unique_exit_fogs
3. Do **not** update weight (roundtable is a hub, not traversal time)
4. Remove roundtable from cluster pool

This gives the start node extra exits, enabling dual-branch starts when `max_exits >= 2`.

## Zone Merge (`merge_into`)

Trivial zones (e.g., corridors, elevators) can be absorbed into an adjacent cluster via `merge_into` in `zone_metadata.toml`:

```toml
[zones.corridor_zone]
weight = 0
merge_into = "target_zone"
```

`apply_cluster_merges()` runs after flood-fill but before fog computation:
1. Find the source cluster (containing `corridor_zone`) and target cluster (containing `target_zone`)
2. Merge zones into the target cluster
3. Remove the source cluster
4. Internal fog gates between merged zones become non-randomized

The target cluster inherits all merged zones. Currently no `merge_into` declarations are active — the mechanism is preserved for future use.

## Exclusions

### Area-Level

| Exclusion | Reason |
|-----------|--------|
| Tag `unused`, `crawlonly` | Not usable in SpeedFog |
| Tag `evergaol` | Unpaired entry/exit, no StakeAsset |
| `leyndell2_` prefix | Ashen capital (use pre-ashen instead) |
| Overworld zones | Large open areas (optional via `--include-overworld`) |
| DLC zones | Optional via `--exclude-dlc` |
| `exclude = true` in zone_metadata.toml | Per-zone exclusion (e.g., `fissure_preboss`, `rauhruins_postromina`) |

### Cluster-Level

Clusters with no entry_fogs or no exit_fogs are filtered out (unreachable or dead-end).

## Major Boss Downgrade

Multi-zone `major_boss` clusters can be **downgraded** to `legacy_dungeon` when the boss is skippable — i.e., the player can enter and exit the cluster without fighting the boss.

**Detection**: After building the world graph within the cluster, compute which zones are reachable from entry zones via non-boss zones only. If any exit fog exists in a reachable non-boss zone (excluding entry fogs reused as exits), the boss is skippable.

**Exit pruning**: When a cluster is downgraded, exit fogs in boss zones are removed. Without this, the DAG generator could randomly select only boss-zone exits, forcing a mandatory boss fight in what should be a traversal cluster.

**Affected clusters**: `academy_redwolf`, `leyndell_sanctuary`, `leyndell2_sanctuary` (clusters where the boss arena has exits but alternative non-boss routes exist).

## Output Format

```json
{
  "version": "1.10",
  "generated_from": "fog.txt",
  "cluster_count": 123,
  "zone_maps": {"stormveil": "m10_00_00_00"},
  "zone_names": {"stormveil": "Stormveil Castle"},
  "zone_conflicts": {"stormveil_margit": ["leyndell_sanctuary"], "leyndell_sanctuary": ["stormveil_margit"]},
  "clusters": [
    {
      "id": "stormveil_1a2b",
      "zones": ["stormveil", "stormveil_godrick"],
      "type": "legacy_dungeon",
      "weight": 19,
      "defeat_flag": 10000800,
      "boss_name": "Godrick the Grafted",
      "entry_fogs": [
        {"fog_id": "AEG099_001_9000", "zone": "stormveil", "side_text": "from gate", "main": true}
      ],
      "exit_fogs": [
        {"fog_id": "AEG099_002_9000", "zone": "stormveil_godrick"},
        {"fog_id": "30052840", "zone": "stormveil", "unique": true, "location": 30051890}
      ]
    }
  ]
}
```

- `id`: `{primary_zone}_{md5_hash[:4]}` for uniqueness
- `zone_maps`: zone → primary map ID (from `area.maps[0]`)
- `zone_names`: zone → display name (from `area.text`)
- `zone_conflicts`: zone → list of mutually exclusive zones (from `conflicts_with` in `zone_metadata.toml`). When a cluster containing one zone is selected, clusters containing conflicting zones are excluded. Example: Margit (`stormveil_margit`) and Morgott (`leyndell_sanctuary`) are the same character — killing Morgott removes Margit from his arena.
- `defeat_flag`: boss defeat event flag (0 if none)
- `boss_name`: canonical boss name from ItemRandomizer's `enemy.txt` (via `DefeatFlag -> ExtraName` mapping). Only present on boss clusters with a matching defeat_flag. Phase suffixes are stripped ("Fire Giant 2" -> "Fire Giant"). Used by the racing server for consistent stats naming across seeds with and without boss randomization.
- `main`: entry fog has the `main` tag (preferred for Stake placement)

## References

- Cluster generator: `tools/generate_clusters.py`
- Cluster data model: `speedfog/clusters.py`
- Zone weights: `data/zone_metadata.toml`
- FogRando zone definitions: `data/fog.txt` (gitignored)
- Cluster generator tests: `tools/test_generate_clusters.py`
