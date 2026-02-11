# Zone Tracking Accuracy

## Objective

SpeedFog injects EMEVD event flags into FogMod-generated fog gate warp events so that
an external racing mod (speedfog-racing, written in Rust) can detect exactly which zone
the player has entered. Each fog gate traversal should set a unique flag that maps back
to a specific DAG node via `event_map` in graph.json.

The racing mod already has a fallback: it can detect WarpPlayer events at runtime and
determine source/destination map IDs. But this is imprecise when multiple DAG nodes share
the same map ID. The EMEVD flags are supposed to provide 100% accurate zone identification.

## The Problem

ZoneTrackingInjector post-processes FogMod's EMEVD output. It scans for WarpPlayer
instructions (bank 2003, id 14) and inserts a SetEventFlag before each one. To decide
*which* flag to insert, it must match each WarpPlayer to the correct graph.json connection.

WarpPlayer contains:
- bytes 0-3: destination map (area, block, sub, sub2)
- bytes 4-7: destination region entity ID (unique per fog gate, allocated by FogMod)

The original implementation used only destination map bytes as the lookup key. This fails
when two connections lead to different zones that share the same map ID. Example from
seed 744785138:

| Connection target          | entrance map     | flag       |
|---------------------------|------------------|------------|
| graveyard_grave_boss      | m18_00_00_00     | 1040292804 |
| graveyard_cave_postboss   | m18_00_00_00     | 1040292813 |

Both zones are in map m18. With a dest-only key, the first-registered flag wins and
the second connection's fog gate sets the wrong tracking flag.

## Option A: Compound Key (source_map, dest_map)

Use the EMEVD filename (= source map) combined with the WarpPlayer destination map as
a compound lookup key. Source maps are gathered from both the exit_gate name AND the
exit_area's areaMaps. Dest maps are gathered from the entrance_gate name AND the
entrance_area's areaMaps.

This resolves collisions because:
1. Two connections from different exit areas produce different compound keys even when
   targeting the same dest map (e.g., m31_01_00_00 for both earthbore_boss and
   earthbore_cave on seed 305519984 — resolved by different exit area maps).
2. Including exit_area areaMaps covers FogMod's getEventMap() remapping, where the
   warp event is placed in the exit area's internal map file instead of the gate's tile.

**Collision handling**: when dest-only fallback hits a collision (same dest map registered
for multiple flags) AND the source EMEVD is a map-specific file (known source map),
injection is skipped. These collisions are typically back-portal return warps from
entrance areas (e.g., m31_22 → m31_01_00_00 return warp alongside a forward entrance
to earthbore_boss on the same map). When the source EMEVD is common.emevd (unknown
source map), injection proceeds — FogMod places forward warps for vanilla gate types
(numeric entity IDs like coffins/teleports) in common.emevd, not return warps.

**Remaining limitation**: two connections from the same source map to the same destination
map would still collide. This requires two fog gates in one map both leading to different
zones in another map — extremely unlikely in a SpeedFog DAG but theoretically possible.

**Status**: implemented as the current fix.

## Option B: Per-Warp Flag Injection (future, 100% accurate)

Instead of matching WarpPlayer instructions to graph.json connections, assign a unique
flag to every FogMod warp event unconditionally, then export the mapping for the racing
mod to consume.

### Design

1. **Scan phase**: after FogMod's `GameDataWriterE.Write()`, scan all map EMEVD files for
   events containing WarpPlayer with region >= 755890000 (FOGMOD_ENTITY_BASE).

2. **Inject phase**: for each matching WarpPlayer, allocate a fresh flag from the
   SpeedFog range (1040292800+) and insert SetEventFlag before the WarpPlayer.

3. **Export phase**: write a mapping file (e.g., `warp_flags.json`) containing:
   ```json
   {
     "flags": [
       {
         "flag_id": 1040292800,
         "source_map": [43, 1, 0, 0],
         "dest_map": [18, 0, 0, 0],
         "dest_region": 755890042,
         "emevd_file": "m43_01_00_00.emevd.dcx",
         "event_id": 1040290157
       }
     ]
   }
   ```

4. **Reconciliation**: the racing mod reads both `graph.json` (event_map, connections)
   and `warp_flags.json`. It matches warp flags to connections using available fields
   (source_map from exit_gate, dest_map from entrance_gate). For any remaining
   ambiguities, the dest_region provides a unique tiebreaker.

Roger edit: maybe just update the graph.json keys? But in the exemple, we have to know that flag 1040292800 correspond to enter a precise zone

### Trade-offs vs Option A

| Aspect              | Option A (compound key)        | Option B (per-warp flags)         |
|---------------------|-------------------------------|-----------------------------------|
| Accuracy            | ~99.9%                        | 100%                              |
| Complexity          | Low (C# only)                 | Medium (C# + racing mod change)   |
| graph.json contract | flags match event_map directly | flags are intermediate; need join  |
| Racing mod impact   | None                          | Must read warp_flags.json         |

### When to Implement

Option A is sufficient for all currently observed seeds. Option B should be implemented
if/when a seed produces a same-source-same-dest collision, or when the racing mod needs
guaranteed 100% accuracy for competitive use.
