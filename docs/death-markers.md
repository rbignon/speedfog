# Death Markers at Fog Gates

Bloodstain visual markers placed near fog gates throughout the DAG.
Implemented in `writer/FogModWrapper/DeathMarkerInjector.cs`.

## Configuration

```toml
[run]
death_markers = true   # default: true
```

When `death_markers = false`, Python sets `death_flags = {}` in graph.json and
no bloodstain assets or EMEVD events are created.

## Mode

Requires death flags from the racing mod (`death_flags` non-empty in graph.json).
When `death_flags` is empty, no bloodstains are placed.

Each cluster gets 3 event flags (low/med/high) allocated in graph.json.
Bloodstains appear only when the racing mod sets these flags based on real-time
death counts from other players.

| Flag | Threshold | Bloodstains visible per gate |
|------|-----------|----------------------------|
| low  | 1+ deaths | 1                          |
| med  | 3+ deaths | 2 (cumulative)             |
| high | 5+ deaths | 3 (cumulative)             |

Each death flag controls 1 bloodstain at every **exit gate** leading to the
cluster (the fog the player sees before entering the dangerous zone). Entrance
gates inside the destination zone do not receive bloodstains.

EMEVD events wait for the flag (`IfEventFlag(MAIN, ON, ...)`) then activate assets.
One event per (flag, map) pair, registered via `InitializeEvent` in event 0.
Event IDs allocated from base 755862100.

## Visual

Each exit fog gate gets up to 3 bloodstain markers in a 120-degree arc on the
approach side (1.5-3m from the gate). The visual is the vanilla bloodstain decal model
(`AEG099_090`, an invisible anchor) with `CreateAssetfollowingSFX(entity, 100, 42)`
for the red glow effect. Positions are deterministic: PRNG seeded on the fog gate's
entity ID.

## Architecture

Two-phase injection per map, running after FogMod's `Write()`:

1. **MSB phase**: clone a nearby vanilla asset per exit fog gate, retarget it
   to `AEG099_090`, and detach its visibility groups (all-zero, see below)
2. **EMEVD phase**: dedicated events per (death_flag, map) pair, registered
   via `InitializeEvent` in event 0

## Key Concepts

### Visibility: SFX, not the asset model

The visible part of a marker is the `CreateAssetfollowingSFX` red glow, not
the `AEG099_090` anchor model. Production evidence (2026-07-22 survey of
built seeds): every placed bloodstain ships with all-zero DrawGroups AND
all-zero DisplayGroups, and the markers render fine on every map, the same
way FogMod's own fog gates (all-zero DrawGroups) are made visible by their
`showsfx` mist. An earlier design tried to copy DrawGroups from the nearest
MapPiece (`GetDrawGroupsAtPosition`/`ApplyDrawGroups`); the save/restore
choreography below silently clobbered those writes from day one (clones
aliased the base asset's arrays, and the post-batch restore reset them), so
zero groups is both the historical and the intended profile. The machinery
was removed once the aliasing was understood.

What DOES matter is a **restrictive inherited `DisplayGroups`**: the clone
starts as a copy of the nearest vanilla asset, and when that asset is an
interior prop with a display-cell mask (hit at Fort of Reprimand's chapel,
`m61_49_43_00`, mask `0x10`), the marker and its SFX are display-culled
outside that cell. `DetachVisibilityGroups()` therefore gives every clone
its own `Unk1` with all-zero group arrays (scalar display-condition fields
and CollisionMask values preserved).

### DeepCopy Shallow Array Bug

SoulsFormats' `MSBE.Part.DeepCopy()` clones `EntityGroupIDs` but shares the
rest: `UnkStruct1.DeepCopy` only clones `CollisionMask`, leaving
`DrawGroups`/`DisplayGroups` aliased between base and clone, and
`UnkPartNames` is aliased at the Part level. Consequences:

- Editing a clone's aliased arrays silently corrupts the base asset. For fog
  gates this caused "visible from far, disappears when approaching".
- The aliasing also runs the other way: restoring the base's arrays after a
  clone batch resets every clone that still aliases them (this is what made
  the old `ApplyDrawGroups` a no-op).

Fix: `DetachVisibilityGroups()` replaces each clone's `Unk1` outright, and
the only remaining save/restore protects `UnkPartNames`/`UnkT54PartName`
(still aliased, nulled in place on clones).

### Entity ID Allocation

FogMod allocates entity IDs from a single counter starting at 755890000
(`FOGMOD_ENTITY_MIN`), shared across Assets, Enemies, Players, and Regions.
Bloodstain entity IDs start at 755900000 (`FOGMOD_ENTITY_MAX`), above FogMod's
range, avoiding collisions without needing to scan MSBs.

Maps are processed in parallel (independent MSB/EMEVD files), so entity IDs and
event IDs are pre-partitioned per map by `PlanAllocations()` in map order: one
entity ID per spec, one event slot per distinct death flag. Specs skipped at
injection time (missing gate assets) leave unused gaps in the map's block; the
IDs stay deterministic regardless of thread scheduling. The event budget
(`SpeedFogIds.DeathMarkerEvents`) is checked upfront against the summed upper
bounds.

### Position Offsets (ASide/BSide)

Each fog gate in `fog.txt` has two sides: **ASide** (the zone in the gate model's
facing direction, based on Y rotation) and **BSide** (the opposite zone). The
bloodstains are placed on the side where players approach from, which depends on
the connection direction:

- **Exit gates** (the only gates receiving bloodstains): approach from `exit_area`.
  If `exit_area` matches ASide.Area, bloodstains are placed at 180 degrees from
  facing (the ASide player stands opposite the ASide warp region). If BSide.Area,
  at 0 degrees (facing direction).

`BuildGateSideLookup()` in Program.cs builds a mapping from gate FullName to
(ASideArea, BSideArea) using `ann.Entrances` and `ann.Warps` from fog.txt. This
is passed to DeathMarkerInjector which calls `ResolveIsASide()` per gate.

Bloodstains are spread across a 120-degree arc on the approach side, split into
3 x 40-degree sectors. Each bloodstain gets a random angle within its sector and
a random radius (1.5-3m).

Offsets are computed in local space (relative to the gate's facing) then rotated
to world space by the gate's Y rotation. If a gate is not found in the fog.txt
lookup, the default placement is BSide (180 degrees, legacy behavior).

## Known Limitations

- **Maps without MapPieces** (e.g., Roundtable Hold / m11_10): skipped entirely.
  These maps lack the geometry needed to determine correct DrawGroups, and no
  DrawGroup value tested produces visible bloodstains. Future work could investigate
  alternative visibility mechanisms for interior maps.

- **Backportal gates** (numeric entity IDs like `30022840`): not found by name or
  entity ID in the MSB. These are FogMod-created return warps from boss rooms that
  may use different naming conventions. Bloodstains are skipped for these gates.

## Data Flow (racing integration)

```
Python (graph_export.py)           graph.json              C# (DeathMarkerInjector)
------------------           ----------              ------------------------
Allocate 3 flags/cluster --> death_flags: {           Read death_flags
                               "cluster_1": [X,Y,Z]  Map connections -> cluster
                             }                        Place 1 bloodstain per (gate, tier)
                                                      Create EMEVD event per (flag, map)

Server (speedfog-racing)     WebSocket                Mod (speedfog-racing)
------------------------     ---------                ---------------------
Aggregate deaths/zone   --> DeathCounts { counts }    Lookup death_flags for node_id
On player death:            broadcast to all mods     Apply thresholds (1/3/5)
  attribute_deaths()                                  set_flag(low/med/high, on/off)
                                                          |
                                                          v
                                                      EMEVD event fires
                                                      Bloodstains appear in-game
```

## Pipeline Position

Called from `Program.cs` step 7h2, after `ChapelGraceInjector` (7h) and before
`RebirthInjector` (7i). Must run after FogMod's `Write()` since it reads
FogMod-generated MSBs and EMEVDs.
