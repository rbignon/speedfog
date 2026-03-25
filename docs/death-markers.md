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

## Modes

### Conditional mode (death_flags non-empty)

Used when the speedfog-racing mod is active. Each cluster gets 3 event flags
(low/med/high) allocated in graph.json. Bloodstains appear only when the racing
mod sets these flags based on real-time death counts from other players.

| Flag | Threshold | Bloodstains visible per gate |
|------|-----------|----------------------------|
| low  | 1+ deaths | 1                          |
| med  | 3+ deaths | 2 (cumulative)             |
| high | 5+ deaths | 3 (cumulative)             |

Each death flag controls 1 bloodstain at every gate associated with that cluster:
- Entrance gates of connections whose destination is the cluster
- Exit gates of connections whose destination is the cluster (in adjacent zones)

EMEVD events wait for the flag (`IfEventFlag(MAIN, ON, ...)`) then activate assets.
One event per (flag, map) pair, registered via `InitializeEvent` in event 0.
Event IDs allocated from base 755862100.

### Unconditional mode (death_flags empty)

Used without the racing mod. All bloodstains are activated immediately in event 0.
Each fog gate gets 3 bloodstains regardless of death counts.

## Visual

Each fog gate gets up to 3 bloodstain markers in a 120-degree arc on the approach
side (1.5-3m from the gate). The visual is the vanilla bloodstain decal model
(`AEG099_090`, an invisible anchor) with `CreateAssetfollowingSFX(entity, 100, 42)`
for the red glow effect. Positions are deterministic: PRNG seeded on the fog gate's
entity ID.

## Architecture

Two-phase injection per map, running after FogMod's `Write()`:

1. **MSB phase**: clone `AEG099_090` assets near each fog gate, with DrawGroups
   sourced from the nearest MapPiece
2. **EMEVD phase**: conditional events (with death_flags) or unconditional
   activation in event 0 (without death_flags)

## Key Concepts

### DrawGroups

Elden Ring uses DrawGroups (8 x uint32 bitmasks) to control asset visibility based
on camera position. The engine activates specific bits depending on where the camera
is; an asset renders only if its DrawGroups overlap the active bits.

Critical behaviors discovered during implementation:
- **All-zero DrawGroups** = invisible for newly added assets (even though vanilla
  assets with zero DrawGroups may render via other mechanisms)
- **All-ones DrawGroups (0xFFFFFFFF)** = also invisible (not "render everywhere")
- **DrawGroups must match the camera zone** at the asset's position

FogMod-created fog gate assets have all-zero DrawGroups. They are made visible via
the `showsfx` EMEVD event, not through DrawGroups. New bloodstain assets cannot rely
on this mechanism and need correct DrawGroups.

### MapPieces as DrawGroup Source

MapPieces are static level geometry (floors, walls, ceilings). Their DrawGroups
accurately represent the rendering zone at their position, unlike interactive assets
which often have zero or partial DrawGroups.

`GetDrawGroupsAtPosition()` finds the nearest MapPiece with non-zero DrawGroups and
copies its DrawGroups to the bloodstain. This gives correct visibility in the vast
majority of maps.

### DeepCopy Shallow Array Bug

SoulsFormats' `MSBE.Part.DeepCopy()` produces shallow copies of internal arrays:
`DrawGroups`, `DisplayGroups`, `CollisionMask`, `EntityGroupIDs`, `UnkPartNames`.
The clone and the original share the same array references.

Modifying the clone's arrays (e.g., `ApplyDrawGroups`, `Array.Clear(EntityGroupIDs)`)
silently corrupts the original asset. For fog gates, this causes the gate to lose
its DrawGroups and become invisible at close range ("visible from far, disappears
when approaching").

Fix: save all array contents from the source asset before cloning, restore after
all clones are created.

### Entity ID Allocation

FogMod allocates entity IDs from a single counter starting at 755890000
(`FOGMOD_ENTITY_MIN`), shared across Assets, Enemies, Players, and Regions.
Bloodstain entity IDs start at 755900000 (`FOGMOD_ENTITY_MAX`), above FogMod's
range, avoiding collisions without needing to scan MSBs.

### Position Offsets (ASide/BSide)

Each fog gate in `fog.txt` has two sides: **ASide** (the zone in the gate model's
facing direction, based on Y rotation) and **BSide** (the opposite zone). The
bloodstains are placed on the side where players approach from, which depends on
the connection direction:

- **Exit gates**: approach from `exit_area`. If `exit_area` matches ASide.Area,
  bloodstains are placed at 180 degrees from facing (the ASide player stands opposite
  the ASide warp region). If BSide.Area, at 0 degrees (facing direction).
- **Entrance gates**: approach from `entrance_area`, same logic.

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
Python (output.py)           graph.json              C# (DeathMarkerInjector)
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
