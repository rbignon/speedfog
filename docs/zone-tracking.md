# Zone Tracking

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

WarpBonfire gates are bonfire-sit warps (e.g., Fire Giant forge → Erdtree burning, Maliketh → Ashen Leyndell). FogMod handles these via two mechanisms:

1. **Vanilla event in common.emevd** (e.g., Event 901) — FogMod's EventEditor replaces the region/map in `PlayCutsceneToPlayerAndWarpWithWeatherAndTime`. Fires on the first traversal.
2. **WarpFlag event in map EMEVD** — FogMod creates a new event triggered by a WarpFlag (set via grace "Repeat warp" menu). Only usable after the first traversal.

Strategies 0-2 can match WarpFlag events (they have entity-based or compound keys), but vanilla events in common.emevd lack `IfActionButtonInArea` and have no source map. When dest maps collide, dest-only matching picks the wrong flag. Strategy 3 uses a dedicated `commonEventLookup` built from connections with `has_common_event: true` in graph.json.

The 3 WarpBonfire gates: `13002500` (Fire Giant → Farum Azula), `13000950` (Maliketh → Ashen Leyndell), `34140950` (Fell Twins). Only the first two have vanilla events in common.emevd; Fell Twins is in a map EMEVD where compound key matching works.

### Event placement

FogMod's `getEventMap()` decides which EMEVD file hosts each warp event. This may differ from the exit gate's map prefix — e.g., parent maps for open world tiles, or map deduplication. This is why the injector can't assume the EMEVD filename matches the exit gate.

## ZoneTrackingInjector Pipeline

`ZoneTrackingInjector.InjectFogGateFlags()` runs after `GameDataWriterE.Write()` and post-processes every EMEVD file.

### Phase 1: Build Lookups

For each connection in graph.json:

1. **Source maps** — from exit_gate name + exit_area's areaMaps (FogMod internal map IDs)
2. **Dest maps** — from entrance_gate name + entrance_area's areaMaps
3. **Entity candidates** — `entityToFlag: Dictionary<int, List<EntityCandidate>>`. Each candidate pairs a flag_id with its destination maps. Two entity sources per connection:
   - `ExitEntityId` — the fog gate asset entity from fog_data.json
   - Gate name suffix — for numeric gates (e.g., `m34_12_00_00_34122840`), the suffix is the action entity used by FogMod in `IfActionButtonInArea`
4. **Compound lookup** — `(source_map, dest_map) → flag_id`, with collision tracking
5. **Dest-only lookup** — `dest_map → flag_id`, with collision tracking
6. **Common event lookup** — `dest_map → flag_id` for connections with `HasCommonEvent` (WarpBonfire gates whose vanilla events live in common.emevd)

The entity lookup is a **multimap** (one entity → multiple candidates) because two connections can share the same exit fog gate when `allow_entry_as_exit` is used. The old `Dictionary.TryAdd` silently dropped duplicates; the multimap preserves all candidates and disambiguates by destination map at match time.

### Phase 2: Scan and Match

For each event in each EMEVD file:

1. **Pre-scan** — `TryMatchEntityCandidates()` scans the event's instructions for `IfActionButtonInArea` (bank 3, id 24) with a known entity. Handles both literal entity IDs and parameterized ones (resolved via `InitializeEvent` args + Parameter list). Returns the candidate list or null.

2. **Per-warp matching** — for each `WarpPlayer` or `PlayCutsceneToPlayerAndWarp` instruction:

   **FogMod filter**: skip if `region < FOGMOD_ENTITY_BASE` AND no entity candidates found (vanilla event, not FogMod-generated).

   Then try three strategies in order:

   | Strategy | Key | When it resolves |
   |----------|-----|-----------------|
   | 0. Entity match | IfActionButtonInArea entity → candidates → resolve by dest map | Most reliable. Handles manual fogwarps with vanilla region IDs (e.g., Placidusax). Handles shared gates via dest map disambiguation. |
   | 1. Compound key | (EMEVD filename, warp dest map) → flag | Resolves same-dest collisions when exits come from different maps. On compound collision, falls back to entity resolution. |
   | 2. Dest-only | warp dest map → flag | Fallback. Skips injection on collisions when source map is known (likely back-portal) or when common event lookup covers the dest map (let Strategy 3 handle it). |
   | 3. Common event | warp dest map → flag (common event lookup) | For WarpBonfire gates whose vanilla events live in common.emevd. Only checked when sourceMap is null and strategies 0-2 fail. |

3. **Injection** — insert `SetEventFlag(flag_id, ON)` before each matched warp instruction, from last to first (to preserve instruction indices). Shift Parameter entries accordingly.

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
| `speedfog/output.py` | Flag allocation (EVENT_FLAG_BASE), exit_entity_id lookup |
| `data/fog_data.json` | Fog gate metadata (entity IDs, positions) |
| `docs/event-flags.md` | Flag ranges and EMEVD event ID allocation |
