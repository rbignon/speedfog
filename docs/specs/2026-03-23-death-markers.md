# Death Markers at Fog Gates

## Summary

Place bloodstain visual markers (AEG099_090 + SFX 42) at fog gate exits and entrances
throughout the DAG. Prototype phase: unconditional display, no racing integration yet.

## Visual

Each fog gate gets 3 bloodstain markers randomly dispersed within 1.5-3m radius.
The visual is the vanilla bloodstain decal model (`AEG099_090`) with
`CreateAssetfollowingSFX(entity, 100, 42)` for the red glow effect.

Positions are deterministic: PRNG seeded on the fog gate's entity ID hash,
with minimum 60-degree angular separation between bloodstains.

## Placement logic

For each connection in graph.json:
- **Exit gate**: 3 bloodstains at the fog gate in the source zone (player sees them before traversing)
- **Entrance gate**: 3 bloodstains at the fog gate in the destination zone (player sees them after arriving)

Gate position is read from the compiled MSB after FogMod writes. The FullName
`m10_01_00_00_AEG099_001_9000` is split into map ID (`m10_01_00_00`) and
part name (`AEG099_001_9000`).

## Implementation

### New class: DeathMarkerInjector.cs

Called from `Program.cs` after `ConnectionInjector.BuildRegionToFlags()`.

Inputs:
- `modDir`: mod output directory (contains map/mapstudio/*.msb.dcx and event/*.emevd.dcx)
- `gameDir`: vanilla game directory (fallback for MSBs/EMEVDs not in mod output)
- `connections`: list of Connection objects from graph.json
- `events`: SoulsIds Events for EMEVD instruction parsing

Steps:
1. Group connections by map (both exit and entrance sides)
2. For each map with fog gates:
   a. Read the MSB, find fog gate assets by part name, get positions
   b. For each gate: generate 3 offset positions (PRNG seeded on gate entity ID)
   c. Place 3 `AEG099_090` assets with unique entity IDs (base 755895000, incrementing)
   d. Ensure `AEG099_090` model definition exists in the MSB
   e. Write the modified MSB
3. For each map with bloodstain assets:
   a. Read the EMEVD (from mod output, fallback to game dir)
   b. In event 0, add for each bloodstain:
      - `ChangeAssetEnableState(entityId, Enabled)`
      - `CreateAssetfollowingSFX(entityId, 100, 42)`
   c. Write the modified EMEVD

### Entity ID allocation

Range: 755895000+, incremented per bloodstain. Maximum ~180 IDs
(~30 connections * 2 gates * 3 bloodstains).

### Asset creation pattern

Clone from an existing asset in the MSB (same pattern as ChapelGraceInjector):
- `ModelName = "AEG099_090"`
- `AssetSfxParamRelativeID = -1` (no model SFX, visual comes from EMEVD SFX 42)
- Clear `UnkPartNames`, `UnkT54PartName`, `EntityGroupIDs`
- Set `Unk08` from part name suffix (SetNameIdent pattern)

### Position offset algorithm

```
seed = hash(gateEntityId)
rng = new Random(seed)
angles = [rng.Next(0,120), rng.Next(120,240), rng.Next(240,360)]  // 3 sectors
for each angle:
    radius = 1.5 + rng.NextDouble() * 1.5   // 1.5m to 3.0m
    offsetX = sin(angle_rad) * radius
    offsetZ = cos(angle_rad) * radius
    position = gatePosition + (offsetX, 0, offsetZ)
```

## Future work (not in this spec)

- 3 event flags per cluster (low/med/high death count) controlling visibility
- graph.json extension with death_flags mapping
- Racing mod integration (server sends death counts, mod sets flags)
- Death markers at exits leading TO a deadly zone (not just at the zone's own gates)
