# Death Markers at Fog Gates

Bloodstain visual markers placed near fog gates throughout the DAG.
Implemented in `writer/FogModWrapper/DeathMarkerInjector.cs`.

## Visual

Each fog gate gets 3 bloodstain markers in a 120-degree arc on the approach side
(1.5-3m from the gate). The visual is the vanilla bloodstain decal model
(`AEG099_090`, an invisible anchor) with `CreateAssetfollowingSFX(entity, 100, 42)`
for the red glow effect. Positions are deterministic: PRNG seeded on the fog gate's
entity ID.

## Architecture

Two-phase injection per map, running after FogMod's `Write()`:

1. **MSB phase**: clone `AEG099_090` assets near each fog gate, with DrawGroups
   sourced from the nearest MapPiece
2. **EMEVD phase**: in event 0, `ChangeAssetEnableState(Enabled)` +
   `CreateAssetfollowingSFX(entity, 100, 42)` for each bloodstain

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

FogMod allocates entity IDs from a single counter starting at 755890000, shared
across Assets, Enemies, Players, and Regions. The counter reaches different values
depending on the graph size.

`FindMaxFogModEntityId()` scans all MSBs in the mod output to find the highest
FogMod entity ID. Bloodstain entity IDs are allocated sequentially above this
maximum. Colliding with FogMod entity IDs causes bloodstains to be invisible or
fog gates to disappear.

### Position Offsets

Bloodstains are placed in a 120-degree arc opposite the gate's facing direction
(the approach side for the player). The arc is split into 3 x 40-degree sectors.
Each bloodstain gets a random angle within its sector and a random radius (1.5-3m).

Offsets are computed in local space (relative to the gate's facing) then rotated
to world space by the gate's Y rotation.

## Known Limitations

- **Maps without MapPieces** (e.g., Roundtable Hold / m11_10): skipped entirely.
  These maps lack the geometry needed to determine correct DrawGroups, and no
  DrawGroup value tested produces visible bloodstains. Future work could investigate
  alternative visibility mechanisms for interior maps.

- **Backportal gates** (numeric entity IDs like `30022840`): not found by name or
  entity ID in the MSB. These are FogMod-created return warps from boss rooms that
  may use different naming conventions. Bloodstains are skipped for these gates.

## Pipeline Position

Called from `Program.cs` step 7h2, after `ChapelGraceInjector` (7h) and before
`RebirthInjector` (7i). Must run after FogMod's `Write()` since it reads
FogMod-generated MSBs and EMEVDs.
