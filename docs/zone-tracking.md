# Zone Tracking

**Date:** 2026-02-24 — **Updated:** 2026-02-26
**Status:** Active

How SpeedFog injects event flags into fog gate warp events so the racing mod can track which zone the player enters.

## Purpose

Each fog gate traversal sets a unique event flag (range 1040292800-999) that maps to a DAG node via `event_map` in graph.json. The racing mod (speedfog-racing) reads these flags to display real-time zone progression.

The racing mod has a runtime fallback (detecting WarpPlayer source/dest maps directly), but it's imprecise when multiple DAG nodes share the same map ID. EMEVD flags give exact zone identification.

## How FogMod Compiles Warp Events

Understanding FogMod's event compilation is essential to understand how zone tracking works.

### fogwarp template (9005777)

FogMod's `EventEditor.Process()` compiles the `fogwarp` template from `fogevents.txt` into per-instance events at build time. The template is **never called at runtime** — each instance gets a unique event ID (typically 1040290xxx) with literal values baked in.

A compiled fogwarp event contains:
- `IfActionButtonInArea` (bank 3, id 24) — checks the player is near the fog gate entity
- `WarpPlayer` (bank 2003, id 14) — teleports to the destination map + region

The fogwarp template also has built-in alt-warp logic (AlternateFlag, altRegion, altMapBytes) for gates with two destinations depending on game state (e.g., Erdtree pre/post Maliketh).

### Manual fogwarp events

Some fog gates use hand-crafted events instead of the fogwarp template (e.g., lie-down warps like Placidusax). These events may:
- Use **parameterized** `IfActionButtonInArea` where entity_id=0 is a placeholder, with the real entity passed via `InitializeEvent` args
- Use vanilla destination region IDs (below `FOGMOD_ENTITY_BASE = 755890000`)
- Contain multiple `WarpPlayer` instructions on different execution paths

### PlayCutsceneToPlayerAndWarp

Cutscene-based transitions (bank 2002, id 11/12) pack the destination map as an int32 (`area*1000000 + block*10000 + sub*100 + sub2`). FogMod replaces the region and map in these instructions. Used by transitions like the Erdtree burning at the Forge of the Giants.

### WarpBonfire gates

WarpBonfire gates are bonfire-sit warps triggered by resting at a specific bonfire entity rather than walking through a fog wall. In fog.txt, these are entries with `WarpBonfire: <bonfire_entity>` on their ASide. Examples:

- **Fire Giant forge** (`13002500`) — After defeating Fire Giant, sitting at the forge bonfire warps the player to the Erdtree burning cutscene, then to the next area (Farum Azula by default, randomized by FogMod).
- **Maliketh** (`13000950`) — After defeating Maliketh, sitting at a bonfire warps to Ashen Leyndell.
- **Fell Twins** (`34140950`) — A WarpBonfire gate in the Divine Tower of East Altus.

FogMod handles WarpBonfire gates via two mechanisms:

1. **Vanilla event in common.emevd** (e.g., Event 901) — FogMod's EventEditor replaces the region/map in `PlayCutsceneToPlayerAndWarpWithWeatherAndTime`. Fires on the first traversal.
2. **WarpFlag event in map EMEVD** — FogMod creates a new event triggered by a WarpFlag (set via grace "Repeat warp" menu). Only usable after the first traversal.

Only Fire Giant (`13002500`) and Maliketh (`13000950`) have vanilla events in common.emevd; Fell Twins (`34140950`) is handled entirely in a map EMEVD where compound key matching (Strategies 0-1) works fine.

**Why WarpBonfire vanilla events need special handling:**

The vanilla events in common.emevd lack `IfActionButtonInArea` (the player triggers the warp by sitting at a bonfire, not by pressing an action button at a fog gate), so Strategy 0 (entity matching) cannot find them. Additionally, common.emevd has no source map prefix, so Strategy 1 (compound key) has no `sourceMap` to form a key. Strategy 2 (dest-only) might work in isolation, but when another connection targets the same destination map, it produces a collision and either picks the wrong flag or skips injection. Strategy 3 (common event matching) solves this by using a dedicated lookup restricted to connections that declare `has_common_event: true`.

The WarpFlag events (mechanism 2) are placed in map EMEVDs and do have entity-based or compound keys, so Strategies 0-2 handle them normally.

### Event placement

FogMod's `getEventMap()` decides which EMEVD file hosts each warp event. This may differ from the exit gate's map prefix — e.g., parent maps for open world tiles, or map deduplication. This is why the injector can't assume the EMEVD filename matches the exit gate.

## ZoneTrackingInjector Pipeline

`ZoneTrackingInjector.Inject()` runs after `GameDataWriterE.Write()` and post-processes every EMEVD file. Internally, it calls `InjectFogGateFlags()` for part A (fog gate tracking flags) and `InjectBossDeathEvent()` for part B (boss death monitor).

### Phase 1: Build Lookups

For each connection in graph.json:

1. **Source maps** — from exit_gate name + exit_area's areaMaps (FogMod internal map IDs)
2. **Dest maps** — from entrance_gate name + entrance_area's areaMaps
3. **Entity candidates** — `entityToFlag: Dictionary<int, List<EntityCandidate>>`. Each candidate pairs a flag_id with its destination maps. Two entity sources per connection:
   - `ExitEntityId` — the fog gate asset entity from fog_data.json
   - Gate name suffix — for numeric gates (e.g., `m34_12_00_00_34122840`), the suffix is the action entity used by FogMod in `IfActionButtonInArea`
4. **Region candidates** — `regionToFlag: Dictionary<int, List<RegionCandidate>>`. Each candidate pairs a flag_id with its source maps. For numeric entrance gates (e.g., `m60_35_45_00_1035462610`), the suffix is the WarpPlayer region entity. FogMod may warp to adjacent map tiles, so dest map matching fails — region matching bypasses dest_map entirely.
5. **Compound lookup** — `(source_map, dest_map) → flag_id`, with collision tracking
5. **Dest-only lookup** — `dest_map → flag_id`, with collision tracking
6. **Common event lookup** — `dest_map → flag_id` for connections with `HasCommonEvent` (WarpBonfire gates whose vanilla events live in common.emevd)

The entity lookup is a **multimap** (one entity → multiple candidates) because two connections can share the same exit fog gate when `allow_entry_as_exit` is used. The old `Dictionary.TryAdd` silently dropped duplicates; the multimap preserves all candidates and disambiguates by destination map at match time.

### Phase 2: Scan and Match

For each event in each EMEVD file:

1. **Pre-scan** — `TryMatchEntityCandidates()` scans the event's instructions for `IfActionButtonInArea` (bank 3, id 24) with a known entity. Handles both literal entity IDs and parameterized ones (resolved via `InitializeEvent` args + Parameter list). Returns the candidate list or null. Also outputs `hasFogModEntity` = true if any IfActionButtonInArea entity is >= `FOGMOD_ENTITY_BASE` (even if not in entityToFlag), signaling the event is FogMod-generated.

2. **Per-warp matching** — for each `WarpPlayer` or `PlayCutsceneToPlayerAndWarp` instruction:

   **FogMod filter**: skip if `region < FOGMOD_ENTITY_BASE` AND no entity candidates found AND `hasFogModEntity` is false (vanilla event, not FogMod-generated). The `hasFogModEntity` flag is needed because FogMod allocates new entities for AEG099 fog gates that aren't in our entityToFlag lookup, but the event IS FogMod-generated and should be processed.

   Then try five strategies in order:

   | Strategy | Key | When it resolves |
   |----------|-----|-----------------|
   | 0. Entity match | IfActionButtonInArea entity → candidates → resolve by dest map | Most reliable. Handles manual fogwarps with vanilla region IDs (e.g., Placidusax). Handles shared gates via dest map disambiguation. |
   | R. Region match | WarpPlayer region → entrance_gate numeric entity suffix → resolve by source map | For numeric entrance gates (e.g., `m60_35_45_00_1035462610`), WarpPlayer uses the vanilla entity as the region. Maps region back to the connection's flag_id. Handles FogMod events that warp to adjacent map tiles (dest map mismatch). |
   | 1. Compound key | (EMEVD filename, warp dest map) → flag | Resolves same-dest collisions when exits come from different maps. On compound collision, falls back to entity resolution. |
   | 2. Dest-only | warp dest map → flag | Fallback. Skips injection on collisions when source map is known (likely back-portal) or when common event lookup covers the dest map (defers to Strategy 3). |
   | 3. Common event | warp dest map → flag (common event lookup) | For WarpBonfire gates whose vanilla events live in common.emevd. Only checked when sourceMap is null and strategies 0-2 did not match. See details below. |

3. **Injection** — insert `SetEventFlag(flag_id, ON)` before each matched warp instruction, from last to first (to preserve instruction indices). Shift Parameter entries accordingly.

### Strategy 3: Common Event Matching (Detail)

Strategy 3 exists to handle WarpBonfire gates whose first-traversal vanilla events live in common.emevd, where Strategies 0-2 all fail:

- **Strategy 0 fails** — no `IfActionButtonInArea` in the event (bonfire-sit warp, not fog gate action).
- **Strategy 1 fails** — common.emevd has no map prefix, so `sourceMap` is null and no compound key can be formed.
- **Strategy 2 may fail** — if another connection targets the same destination map, the dest-only lookup has a collision. Even without collision, Strategy 2 is imprecise because common.emevd contains many FogMod warp events from different source areas.

**Data flow (`has_common_event`):**

1. **fog.txt** — FogMod's annotation data declares `WarpBonfire: <bonfire_entity>` on the ASide of certain fogs.
2. **generate_clusters.py** — Parses the `WarpBonfire` field from fog.txt into `FogSide.warp_bonfire`. When building cluster exit_fogs, sets `"warp_bonfire": True` on exit fog entries whose ASide has a WarpBonfire value (`fog.has_warp_bonfire and fog.aside.area == zone`).
3. **output.py** — When serializing graph.json connections, checks if any exit_fog in the source cluster has `warp_bonfire` set for the matching fog_id. If so, sets `has_common_event: true` on the connection dict. The field is only emitted when true (omitted when false to keep the JSON compact).
4. **GraphData.cs** — The C# `Connection` model deserializes `has_common_event` into `HasCommonEvent` (bool, defaults to false). Unknown fields are ignored by `System.Text.Json`.
5. **ZoneTrackingInjector** — During Phase 1, builds `commonEventLookup` by iterating connections where `HasCommonEvent` is true and registering their dest maps. During Phase 2, after Strategies 0-2 fail and `sourceMap` is null, looks up the warp's dest map in this dedicated lookup (skipping collided entries).

**Matching conditions:**

Strategy 3 only fires when all of these are true:
- `matched` is still false after Strategies 0-2
- `sourceMap` is null (the EMEVD file is common.emevd or another non-map file)
- The dest map exists in `commonEventLookup`
- The dest map is not in `commonEventCollisions`

**Interaction with Strategy 2:**

Strategy 2 explicitly defers to Strategy 3 in one case: when a dest-only collision occurs in common.emevd (`sourceMap` is null) and the collided dest map has an entry in `commonEventLookup`, Strategy 2 skips injection and increments `skippedCollisions`, allowing Strategy 3 to handle the warp with its more precise lookup. Without a common event entry for the dest map, Strategy 2 injects anyway (no better option available).

### Entity Disambiguation Detail

When `TryMatchEntityCandidates` returns multiple candidates (shared gate), `ResolveEntityCandidate` picks the right one:

| Candidates | Dest map match | Result |
|-----------|---------------|--------|
| 1 | (ignored) | Return directly — unambiguous |
| N | Exactly 1 candidate's DestMaps contains the warp dest map | Return that candidate |
| N | 0 matches or N matches | Return null — fall through to compound/dest-only |

This works because FogMod compiles a separate warp event per destination, so two connections sharing a gate produce events with different `WarpPlayer` dest maps.

### Phase 3: Validation

After processing all EMEVD files, compare injected flags against expected flags (all connections with `flag_id > 0`). If any flag was not injected, **the build aborts with a fatal exception**. This prevents producing a mod with silent tracking gaps.

## Boss Death Monitor

In addition to fog gate flags, `ZoneTrackingInjector.InjectBossDeathEvent()` creates event 755862000 in common.emevd:

```
IfEventFlag(MAIN, ON, bossDefeatFlag)
SetEventFlag(finishEvent, ON)
```

This translates the boss's vanilla defeat flag into SpeedFog's `finish_event` flag for the racing mod.

## Known Limitations

### Roundtable exit

FogMod's Roundtable exit events use `IfActionButtonInArea` with `entity_id=0` and no `InitializeEvent` args available in the EMEVD file where the event is placed. Entity matching fails (logged as diagnostic). These events are still matched by compound or dest-only strategies, but this is fragile. The diagnostic logging helps identify these cases.

### Per-warp flag injection (future alternative)

Instead of matching warp instructions back to graph.json connections, a future approach could assign a unique flag to every FogMod warp event unconditionally, then export the flag→zone mapping for the racing mod. This would be 100% accurate by construction, but requires changes to the racing mod's flag consumption.

## File References

| File | Role |
|------|------|
| `writer/FogModWrapper/ZoneTrackingInjector.cs` | All injection logic |
| `writer/FogModWrapper.Tests/ZoneTrackingTests.cs` | Unit tests |
| `writer/FogModWrapper.Core/Models/GraphData.cs` | `Connection.HasCommonEvent` model field |
| `speedfog/output.py` | Flag allocation (EVENT_FLAG_BASE), exit_entity_id lookup, `has_common_event` emission |
| `tools/generate_clusters.py` | `warp_bonfire` propagation to cluster exit_fogs |
| `data/fog_data.json` | Fog gate metadata (entity IDs, positions) |
| `docs/event-flags.md` | Flag ranges and EMEVD event ID allocation |
