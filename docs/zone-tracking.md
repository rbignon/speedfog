# Zone Tracking

**Date:** 2026-02-24 — **Updated:** 2026-03-14
**Status:** Active

How SpeedFog injects event flags into fog gate warp events so the racing mod can track which zone the player enters.

**Design spec:** `docs/specs/2026-03-12-region-based-zone-tracking.md`

## Purpose

Each fog gate traversal sets a unique event flag (range 1040292400-999) that maps to a DAG node via `event_map` in graph.json. The racing mod (speedfog-racing) reads these flags to display real-time zone progression.

The racing mod has a runtime fallback (detecting WarpPlayer source/dest maps directly), but it's imprecise when multiple DAG nodes share the same map ID. EMEVD flags give exact zone identification.

## How FogMod Compiles Warp Events

Understanding FogMod's event compilation is essential to understand how zone tracking works.

### fogwarp template (9005777)

FogMod's `EventEditor.Process()` compiles the `fogwarp` template from `fogevents.txt` into per-instance events at build time. The template is **never called at runtime** -- each instance gets a unique event ID (typically 1040290xxx) with literal values baked in.

A compiled fogwarp event contains:
- `IfActionButtonInArea` (bank 3, id 24) -- checks the player is near the fog gate entity
- `WarpPlayer` (bank 2003, id 14) -- teleports to the destination map + region

The fogwarp template also has built-in alt-warp logic (AlternateFlag, altRegion, altMapBytes) for gates with two destinations depending on game state (e.g., Erdtree pre/post Maliketh).

### Manual fogwarp events

Some fog gates use hand-crafted events instead of the fogwarp template (e.g., lie-down warps like Placidusax). These events may contain multiple `WarpPlayer` instructions on different execution paths, but their destination regions are still literal values baked in at compile time.

### PlayCutsceneToPlayerAndWarp

Cutscene-based transitions (bank 2002, id 11/12) pack the destination map as an int32 (`area*1000000 + block*10000 + sub*100 + sub2`). FogMod replaces the region and map in these instructions. Used by transitions like the Erdtree burning at the Forge of the Giants.

### WarpBonfire gates

WarpBonfire gates are bonfire-sit warps triggered by resting at a specific bonfire entity rather than walking through a fog wall. Examples: Fire Giant forge (`13002500`), Maliketh (`13000950`), Fell Twins (`34140950`).

FogMod handles WarpBonfire gates via two mechanisms:

1. **Vanilla event in common.emevd** (e.g., Event 901) -- FogMod's EventEditor replaces the region/map in `PlayCutsceneToPlayerAndWarpWithWeatherAndTime`. Fires on the first traversal.
2. **WarpFlag event in map EMEVD** -- FogMod creates a new event triggered by a WarpFlag (set via grace "Repeat warp" menu). Only usable after the first traversal.

Both mechanisms write the entrance Region into the compiled warp instructions, so region-based lookup handles them uniformly.

### Event placement

FogMod's `getEventMap()` decides which EMEVD file hosts each warp event. This may differ from the exit gate's map prefix (e.g., parent maps for open world tiles, or map deduplication).

## Architecture: Region-Based Lookup

Zone tracking uses a single region-based dictionary lookup to match compiled warp instructions back to graph.json connections. The mapping is built **before** FogMod compiles events, eliminating the information-loss problem that previously required heuristic matching.

### Why the region is a reliable key

Every entrance in fog.txt has a unique `Warp.Region` -- either a pre-existing vanilla entity or a FogMod-allocated entity from `FOGMOD_ENTITY_BASE` (755890000). FogMod bakes this region value into all compiled warp instruction types (WarpPlayer, PlayCutsceneToPlayerAndWarp, manual fogwarps, WarpFlag events). The value is always a literal integer, never parameterized.

Both template-compiled and manual event paths read from `edge.Link.Side.Warp.Region`, which is exactly what we capture during mapping construction.

### Data flow

```
graph.json connections + event_map
        |
        v
ConnectionInjector.InjectAndExtract()
        |  connects edges in FogMod Graph
        |  saves (flag_id, entranceEdge) references
        v
GameDataWriterE.Write()
        |  populates Side.Warp from MSB data
        |  compiles fogwarp events (bakes Region into WarpPlayer)
        v
InjectionResult.BuildRegionToFlags(eventMap)
        |  reads entranceEdge.Side.Warp.Region from saved references
        |  builds region -> List<flag_id> dictionary
        |  validates same-cluster invariant
        v
ZoneTrackingInjector.Inject(regionToFlags, expectedFlags, ...)
        |  scan EMEVDs, extract region from warp instructions
        |  lookup region in dictionary
        |  inject SetEventFlag for each flag_id in list
        v
SetEventFlag injected before each matched warp
```

## ZoneTrackingInjector Pipeline

`ZoneTrackingInjector.Inject()` runs after `GameDataWriterE.Write()` and post-processes every EMEVD file. It takes `regionToFlags` and `expectedFlags` instead of raw connections.

### Phase 1: Mapping (deferred)

`Side.Warp` is not available during connection injection — `GameDataWriterE.Write()` populates it later from MSB data. The mapping is built in two steps:

1. **During injection** (`ConnectionInjector.InjectAndExtract()`): save `(flag_id, entranceEdge)` references for each connection.
2. **After Write()** (`InjectionResult.BuildRegionToFlags(eventMap)`): read `entranceEdge.Side.Warp.Region` from the saved references and build the dictionary:
   - Add `region -> flag_id` (appending to the list if the region already exists for shared entrances)
   - If `entranceEdge.Side.AlternateSide?.Warp?.Region` exists (AlternateFlag warps like flag 300/330), register the alternate region with the same `flag_id`
   - Validate that all `flag_id`s for the same region map to the same cluster in `event_map` (structural safety net)

### Phase 2: Scan and Inject

For each event in each EMEVD file, scan for `WarpPlayer` (2003:14) and `PlayCutsceneToPlayerAndWarp` (2002:11/12) instructions:

1. Extract the region from instruction arguments via `TryExtractWarpInfo()`
2. Look up region in `regionToFlags`
3. If found, inject `SetEventFlag(flag_id, ON)` for **each** flag_id in the list, before the warp instruction
4. If not found, skip (not one of our connections' warps)

Insertions proceed from last to first within each event to preserve instruction indices. Parameter entries are shifted accordingly.

### Phase 3: Validation

After processing all EMEVD files, compare injected flags against `expectedFlags` (all connections with `flag_id > 0`). If any flag was not injected, **the build aborts with a fatal exception**. This prevents producing a mod with silent tracking gaps.

## Shared Entrances

When two connections share the same entrance gate (`DuplicateEntrance`), they share the same `Warp.Region` but have different `flag_id`s (flags are allocated per-connection in `output.py`). The mapping becomes `region -> [flag_A, flag_B]`, and **all** `SetEventFlag` instructions are injected before the warp. All flags for a shared region map to the same cluster in `event_map` (enforced by the same-cluster assertion in ConnectionInjector).

In the common case (no shared entrance), each region maps to exactly one flag. For shared entrances, a single fog gate traversal sets N flags — all resolving to the same node via `event_map`.

### Consumer guidelines

Systems consuming SpeedFog event flags should:

1. **Resolve flags via `event_map`** — treat flags as opaque identifiers that resolve to node_ids, not as connection identifiers.
2. **Handle duplicate node arrivals** — multiple flags may fire for the same node in the same frame. Be idempotent on `(node_id, timestamp)`.
3. **Do not assume flag uniqueness per traversal** — a single fog gate traversal may set 1 or N flags (N > 1 for shared entrances). All resolve to the same node.

See the [design spec](specs/2026-03-12-region-based-zone-tracking.md#consumer-impact) for detailed consumer impact analysis and recommended deduplication guard.

## Boss Death Monitor

`ZoneTrackingInjector.InjectBossDeathEvent()` creates event 755862000 in common.emevd:

```
IfEventFlag(MAIN, ON, bossDefeatFlag)
SetEventFlag(finishEvent, ON)
```

This translates the boss's vanilla defeat flag into SpeedFog's `finish_event` flag for the racing mod.

## File References

| File | Role |
|------|------|
| `writer/FogModWrapper/ZoneTrackingInjector.cs` | Scan/inject logic (Phase 2-3) |
| `writer/FogModWrapper/ConnectionInjector.cs` | Region-to-flags mapping construction (Phase 1) |
| `writer/FogModWrapper.Tests/ZoneTrackingTests.cs` | Unit tests |
| `speedfog/output.py` | Flag allocation (EVENT_FLAG_BASE), event_map construction |
| `docs/event-flags.md` | Flag ranges and EMEVD event ID allocation |
| `docs/specs/2026-03-12-region-based-zone-tracking.md` | Full design spec (rationale, consumer impact, edge cases) |

## Design History

The original ZoneTrackingInjector (pre-March 2026) reverse-engineered compiled EMEVD events to match warp instructions back to graph.json connections using five heuristic strategies (entity matching, region suffix, compound key, dest-only, common event) plus a residual fallback. This was inherently fragile because FogMod's compilation discards connection identity. Collision-prone configurations required conservative Python-side validators that limited seed diversity. The region-based approach captures the mapping before compilation, eliminating the information loss. See the [design spec](specs/2026-03-12-region-based-zone-tracking.md) for the full rationale.
