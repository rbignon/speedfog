# Zone Tracking

**Date:** 2026-02-24 — **Updated:** 2026-03-14
**Status:** Active

How SpeedFog injects event flags into fog gate warp events so the racing mod can track which zone the player enters.

**Design spec:** `docs/specs/2026-03-12-region-based-zone-tracking.md`

## Purpose

Each fog gate traversal sets a unique event flag (range 1040292800-999) that maps to a DAG node via `event_map` in graph.json. The racing mod (speedfog-racing) reads these flags to display real-time zone progression.

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
        |  builds regionToFlags mapping inline:
        |    connection.flag_id + entranceEdge.Side.Warp.Region
        |    region -> List<flag_id> (multi-flag for shared entrances)
        |    asserts same-cluster invariant via event_map
        v
GameDataWriterE.Write()
        |  compiles fogwarp events (uses same Region values)
        v
ZoneTrackingInjector.Inject(regionToFlags, expectedFlags, ...)
        |  scan EMEVDs, extract region from warp instructions
        |  lookup region in dictionary
        |  inject SetEventFlag for each flag_id in list
        v
SetEventFlag injected before each matched warp
```

## ZoneTrackingInjector Pipeline

`ZoneTrackingInjector.Inject()` runs after `GameDataWriterE.Write()` and post-processes every EMEVD file. It takes `regionToFlags` and `expectedFlags` (built by `ConnectionInjector`) instead of raw connections.

### Phase 1: Mapping (ConnectionInjector)

The `regionToFlags` dictionary is built inside `ConnectionInjector.InjectAndExtract()`, where both the `Connection` (with `flag_id`) and the resolved entrance edge (with `Side.Warp.Region`) are available. For each connection:

1. After `Graph.Connect()` or `Graph.DuplicateEntrance()`, read `entranceEdge.Side.Warp.Region`
2. Add `region -> flag_id` to the dictionary (appending to the list if the region already exists)
3. If `entranceEdge.Side.AlternateSide?.Warp?.Region` exists (AlternateFlag warps like flag 300/330), register the alternate region with the same `flag_id`

After all connections are processed, validate that all `flag_id`s for the same region map to the same cluster in `event_map`. This invariant is structurally guaranteed (an entrance gate is in one zone, one cluster) but verified as a safety net.

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

When two connections share the same entrance gate (`DuplicateEntrance`), they share the same `Warp.Region` but have different `flag_id`s (flags are allocated per-connection in `output.py`). The mapping becomes `region -> [flag_A, flag_B]`, and both `SetEventFlag` instructions are injected before the warp.

### Semantic change: per-connection to per-cluster

This changes the flag semantic from **per-connection** ("flag F means connection C was traversed") to **per-cluster** ("flag F means cluster X was entered") for connections that share an entrance gate. In the common case (no shared entrance), the list has one flag_id and the semantics are identical.

For shared entrances, all flags for the region fire simultaneously on any traversal -- the consumer cannot determine which specific exit gate was used. This is acceptable because:

1. **The per-connection semantic was never consumed.** The racing mod resolves `flag_id -> node_id` via `event_map` and discards the flag. It tracks cluster-level progression.
2. **The lost information is recoverable from context.** The racing mod tracks `zone_history` -- the previous entry identifies the source cluster.
3. **Event flags are one-shot.** Re-entry from a different branch is indistinguishable once the flag is already ON.

### Consumer guidelines

Systems consuming SpeedFog event flags should:

1. **Resolve flags via `event_map`** -- treat flags as opaque identifiers that resolve to node_ids.
2. **Handle duplicate node arrivals** -- multiple flags may fire for the same node in the same frame. Be idempotent on `(node_id, timestamp)`.
3. **Do not assume flag uniqueness per traversal** -- a single fog gate traversal may set 1 or N flags.

See the design spec for detailed consumer impact analysis and recommended deduplication guard.

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
