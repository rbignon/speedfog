# Conditional Death Markers

## Summary

Make death marker visibility conditional on event flags set by the speedfog-racing mod
based on real-time death counts from other players. Three intensity levels (low/med/high)
controlled by 3 flags per cluster, activated when aggregate death counts cross thresholds.

## Trigger

When the racing server receives a `status_update` with increased `death_count`, it
attributes deaths to the player's current zone (already implemented in `attribute_deaths()`).
The server then aggregates total deaths per node_id across all participants and broadcasts
a `DeathCounts` message to all mods in the race.

The broadcast is event-driven (on death attribution), not periodic.

## Data flow

```
Player dies in zone X
    |
    v
Mod sends status_update { death_count: N+1 }
    |
    v
Server: attribute_deaths() updates zone_history
Server: aggregate deaths per node_id across all participants
Server: broadcast DeathCounts { counts: { node_id: total } }
    |
    v
All mods receive DeathCounts
Mod: look up death_flags for each node_id from graph data
Mod: apply thresholds (1/3/5) -> set/unset event flags
    |
    v
EMEVD events waiting on those flags fire
    -> ChangeAssetEnableState + CreateAssetfollowingSFX
    -> Bloodstains appear in-game
```

## Thresholds

| Flag | Threshold | Bloodstains visible per gate |
|------|-----------|----------------------------|
| low  | 1+ deaths | 1                          |
| med  | 3+ deaths | 2 (cumulative)             |
| high | 5+ deaths | 3 (cumulative)             |

## Placement logic

For a cluster C with death_flags [low, med, high]:

- **flag_low** controls 1 bloodstain at every gate associated with C
- **flag_med** controls a 2nd bloodstain at every gate associated with C
- **flag_high** controls a 3rd bloodstain at every gate associated with C

A gate is "associated with C" if it is either:
- An **entrance gate** of a connection whose destination is C (inside the zone)
- An **exit gate** of a connection whose destination is C (in the adjacent zone, before traversal)

Both types are identified by `event_map[flag_id] == C` for each connection.

Example: Godskin Duo zone with 2 entrances + 1 exit from Stormveil, 4 deaths:
- flag_low ON, flag_med ON, flag_high OFF
- 2 bloodstains visible at each of the 3 gates = 6 bloodstains total

## Components

### 1. Python (speedfog): graph.json death_flags

**File:** `speedfog/output.py`

Allocate 3 event flags per cluster, sequentially after connection flags.
Add to graph.json:

```json
{
  "death_flags": {
    "cluster_id_1": [1040292500, 1040292501, 1040292502],
    "cluster_id_2": [1040292503, 1040292504, 1040292505]
  }
}
```

The `death_flags` dict maps cluster_id to [flag_low, flag_med, flag_high].

### 2. C# (FogModWrapper): conditional EMEVD events

**File:** `writer/FogModWrapper/DeathMarkerInjector.cs`

Currently places 3 bloodstains per gate unconditionally (enable + SFX in event 0).
Change to:

1. Read `death_flags` from graph.json (add to `GraphData` model)
2. For each connection, determine the destination cluster via `event_map`
3. Place 3 bloodstains per gate (same MSB logic as current)
4. Instead of unconditional instructions in event 0, create EMEVD events:

```
Event DEATH_MARKER_EVENT_BASE + N:
  IfEventFlag(MAIN, ON, death_flag_low_for_cluster_X)
  ChangeAssetEnableState(bloodstain_entity_1, Enabled)
  CreateAssetfollowingSFX(bloodstain_entity_1, 100, 42)
  ChangeAssetEnableState(bloodstain_entity_2, Enabled)
  CreateAssetfollowingSFX(bloodstain_entity_2, 100, 42)
  ...all bloodstains controlled by this flag in this map
```

One event per (flag, map) pair. Group all bloodstains that share the same flag
and the same map into a single event. Initialize each event from event 0.

Events are one-shot (no restart): once the flag is set, bloodstains stay visible.
The racing mod only increases death counts during a race, so flags go ON but never OFF.

### 3. Server (speedfog-racing): DeathCounts broadcast

**File:** `speedfog-racing/server/speedfog_racing/websocket/mod.py`

In `handle_status_update()`, after `attribute_deaths()` succeeds (delta > 0):
1. Query all participants of the race
2. Aggregate `deaths` field from all zone_history entries, grouped by node_id
3. Broadcast `DeathCounts` to all mods in the race room

**File:** `speedfog-racing/server/speedfog_racing/websocket/schemas.py`

New message type:

```python
class DeathCountsMessage(BaseModel):
    type: Literal["death_counts"] = "death_counts"
    counts: dict[str, int]  # node_id -> total deaths across all participants
```

### 4. Mod (speedfog-racing): flag setting

**File:** `speedfog-racing/mod/src/core/protocol.rs`

New ServerMessage variant:

```rust
DeathCounts {
    counts: HashMap<String, u32>,  // node_id -> total deaths
},
```

**File:** `speedfog-racing/mod/src/dll/tracker.rs`

On receiving `DeathCounts`:
1. For each (node_id, count) in counts:
   a. Look up death_flags for node_id from seed data
   b. set_flag(flag_low, count >= 1)
   c. set_flag(flag_med, count >= 3)
   d. set_flag(flag_high, count >= 5)

**File:** `speedfog-racing/mod/src/core/protocol.rs` (SeedInfo extension)

Add death_flags to SeedInfo:

```rust
pub struct SeedInfo {
    // ... existing fields ...
    #[serde(default)]
    pub death_flags: HashMap<String, [u32; 3]>,  // node_id -> [low, med, high]
}
```

The server sends death_flags in `AuthOk` from graph.json.

## Flag allocation

Connection flags: 1040292400+ (current, ~200 max)
Death flags: allocated sequentially after the last connection flag.
With ~30 clusters * 3 flags = ~90 flags. Total stays well within the
1040292400-1040292999 range.

## EMEVD event allocation

Base: 755862100 (adjacent to existing boss death event at 755862000).
One event per (flag, map) pair. With ~30 clusters * 3 flags * ~2 maps average,
that is ~180 events max. Range 755862100-755862999.

## Changes summary

| Repo | File | Change |
|------|------|--------|
| speedfog | `speedfog/output.py` | Allocate death_flags per cluster, add to graph.json |
| speedfog | `writer/FogModWrapper.Core/Models/GraphData.cs` | Add DeathFlags property |
| speedfog | `writer/FogModWrapper/DeathMarkerInjector.cs` | Conditional events instead of unconditional |
| speedfog-racing | `server/.../schemas.py` | DeathCountsMessage type |
| speedfog-racing | `server/.../mod.py` | Aggregate + broadcast on death |
| speedfog-racing | `mod/src/core/protocol.rs` | DeathCounts variant, SeedInfo.death_flags |
| speedfog-racing | `mod/src/dll/websocket.rs` | Deserialize DeathCounts |
| speedfog-racing | `mod/src/dll/tracker.rs` | Apply thresholds, set flags |
