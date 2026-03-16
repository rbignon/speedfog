# Vanilla Stake Removal

**Date:** 2026-03-16
**Status:** Active

Post-processor that removes vanilla Stakes of Marika whose RetryPoints respawn the player outside the SpeedFog DAG.

## Problem

Some boss zones have a vanilla RetryPoint (Stake of Marika) that respawns the player in an adjacent zone. If that zone is not in the SpeedFog DAG, the player is softlocked after dying at the boss.

**Example — caelid_radahn:**

- Vanilla RetryPoint in MSB `m60_12_09_02` (LOD level 2 tile covering Redmane Castle area)
- Asset: `m60_51_36_00-AEG099_502_2000` (the Stake of Marika 3D model)
- PlayerMap: `m60_51_36_00` (respawn position is in `caelid_preradahn`)
- `caelid_preradahn` is NOT in the SpeedFog DAG → player softlocked after boss death

## Why FogMod Doesn't Handle This

FogMod's `GameDataWriterE` processes RetryPoints from fog.txt (lines 4444–4547). For `caelid_radahn`, it edits/moves the RetryPoint only when `caelid_preradahn` is in the graph (because it needs the PlayerMap zone to resolve the respawn position). Since SpeedFog never includes `caelid_preradahn` in the DAG, FogMod leaves the vanilla RetryPoint untouched.

FogMod also creates NEW stakes for boss areas (lines 4559–4600+), but only for areas not already handled by fog.txt RetryPoints. Since `caelid_radahn` has a RetryPoint entry in fog.txt, it goes through the "edit existing" path (which does nothing without `caelid_preradahn`), and is then skipped by the "create new" path.

## Solution

`StakeRemover` runs as a post-processing step after FogMod writes its output. It removes the RetryPoint event from the MSB. Without the RetryPoint event, the game does not treat the asset as a functional Stake of Marika.

## MSB Structure

A Stake of Marika consists of three MSB entries:

| Entry | Type | Purpose |
|-------|------|---------|
| RetryPoint | Event | Links asset to retry behavior (activation flag, retry region) |
| AEG099_502_XXXX | Part.Asset | 3D model (the physical stake in the world) |
| c0000_XXXX (EntityID = stake - 970) | Part.Player | Respawn position when using the stake |

Removing the **RetryPoint event** is sufficient to disable the stake. The asset model remains visible but is non-functional. This matches FogRando's own "remove" tag behavior (`GameDataWriterE.cs:4452–4458`), which also only removes the RetryPoint.

## Data Flow

1. **Hardcoded list** in `StakeRemover.StakesToRemove`: each entry specifies the MSB map and the `RetryPartName` (asset name referenced by the RetryPoint).
2. **MSB lookup**: tries mod dir first (in case FogMod already modified the MSB), then falls back to game dir.
3. **RetryPoint removal**: finds the RetryPoint by `RetryPartName` match, removes it, writes the MSB to mod dir.

## Implementation Details

- **MSB directory casing**: handles both `MapStudio` (vanilla Windows) and `mapstudio` (Wine/FogMod) directory names via `MsbDirVariants`, in both mod dir and game dir lookups.
- **LOD level 2 tiles**: vanilla stakes in open world areas may be in large tile MSBs (e.g., `m60_12_09_02` = LOD 2) that FogMod doesn't modify. The game dir fallback is essential for these.
- **Unconditional removal**: runs regardless of whether the boss zone is in the current seed's DAG.

## Current Stakes

| Zone | Map | Asset | Reason |
|------|-----|-------|--------|
| caelid_radahn | m60_12_09_02 | m60_51_36_00-AEG099_502_2000 | Respawns in caelid_preradahn (outside DAG) |

## Adding New Stakes

To remove additional vanilla stakes, add entries to `StakesToRemove` in `StakeRemover.cs`. The map and asset name can be found in fog.txt's `RetryPoints` section.
