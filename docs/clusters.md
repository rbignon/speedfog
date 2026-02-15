# Cluster Generation

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
   clusters.json v1.5
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
3. Mark `drop`-tagged connections as unidirectional
4. Check for reverse connections → bidirectional edges
5. Build adjacency graph

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

This gives the start node extra exits, enabling dual-branch starts when `max_branches >= 2`.

## Exclusions

### Area-Level

| Exclusion | Reason |
|-----------|--------|
| Tag `unused`, `crawlonly` | Not usable in SpeedFog |
| Tag `evergaol` | Unpaired entry/exit, no StakeAsset |
| `leyndell2_` prefix | Ashen capital (use pre-ashen instead) |
| Overworld zones | Large open areas (optional via `--include-overworld`) |
| DLC zones | Optional via `--exclude-dlc` |

### Cluster-Level

Clusters with no entry_fogs or no exit_fogs are filtered out (unreachable or dead-end).

## Output Format

```json
{
  "version": "1.5",
  "generated_from": "fog.txt",
  "cluster_count": 123,
  "zone_maps": {"stormveil": "m10_00_00_00"},
  "zone_names": {"stormveil": "Stormveil Castle"},
  "clusters": [
    {
      "id": "stormveil_1a2b",
      "zones": ["stormveil", "stormveil_godrick"],
      "type": "legacy_dungeon",
      "weight": 19,
      "defeat_flag": 10000800,
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
- `defeat_flag`: boss defeat event flag (0 if none)
- `main`: entry fog has the `main` tag (preferred for Stake placement)

## References

- Cluster generator: `tools/generate_clusters.py`
- Cluster data model: `speedfog/clusters.py`
- Zone weights: `data/zone_metadata.toml`
- FogRando zone definitions: `data/fog.txt` (gitignored)
- Cluster generator tests: `tools/test_generate_clusters.py`
