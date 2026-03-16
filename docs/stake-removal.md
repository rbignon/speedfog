# Vanilla Stake Removal

**Date:** 2026-03-16
**Status:** Active

Removes vanilla Stakes of Marika whose RetryPoints respawn the player outside the SpeedFog DAG.

## Problem

Some boss zones have a vanilla RetryPoint (Stake of Marika) that respawns the player in an adjacent zone. If that zone is not in the SpeedFog DAG, the player is softlocked after dying at the boss.

**Example — caelid_radahn:**

- Vanilla RetryPoint in MSB `m60_12_09_02` (LOD level 2 tile covering Redmane Castle area)
- Asset: `m60_51_36_00-AEG099_502_2000` (the Stake of Marika 3D model)
- PlayerMap: `m60_51_36_00` (respawn position is in `caelid_preradahn`)
- `caelid_preradahn` is NOT in the SpeedFog DAG → player softlocked after boss death

## Why FogMod Doesn't Handle This Automatically

FogMod's `GameDataWriterE` processes RetryPoints from fog.txt (lines 4444–4547). For `caelid_radahn`, it edits/moves the RetryPoint only when `caelid_preradahn` is in the graph (because it needs the PlayerMap zone to resolve the respawn position). Since SpeedFog never includes `caelid_preradahn` in the DAG, FogMod leaves the vanilla RetryPoint untouched.

## Solution

Tag vanilla RetryPoints with `"remove"` and inject them into `ann.RetryPoints` **before** `GameDataWriterE.Write()`. FogMod's existing "remove" tag logic (`GameDataWriterE.cs:4452–4458`) handles the rest: it reads the MSB from BHD archives, removes the RetryPoint event, and writes the modified MSB to mod dir.

This approach is necessary because game MSBs are stored in **BHD/BDT archives**, not as loose files. FogMod's `GameEditor` (SoulsIds) reads from these archives, but simple `File.Exists()` lookups cannot.

### Why not post-process?

A previous approach tried to read/write MSBs as a post-processing step after `Write()`. This failed because:
1. Game MSBs are in BHD archives, not loose files — `File.Exists()` can't find them
2. FogMod only writes MSBs it modifies — unmodified maps (like `m60_12_09_02`) are never extracted to mod dir

## MSB Structure

A Stake of Marika consists of three MSB entries:

| Entry | Type | Purpose |
|-------|------|---------|
| RetryPoint | Event | Links asset to retry behavior (activation flag, retry region) |
| AEG099_502_XXXX | Part.Asset | 3D model (the physical stake in the world) |
| c0000_XXXX (EntityID = stake - 970) | Part.Player | Respawn position when using the stake |

Removing the **RetryPoint event** is sufficient to disable the stake. The asset model remains visible but is non-functional. This matches FogRando's own "remove" tag behavior, which also only removes the RetryPoint.

## Data Flow

1. `StakeRemover.GetRetryPointsToRemove()` returns `AnnotationData.RetryPoint` entries tagged `"remove"`
2. `Program.cs` sets `ann.RetryPoints` before calling `writer.Write()`
3. `GameDataWriterE.Write()` reads the MSB from BHD archives, finds the RetryPoint by `Name` (= `RetryPartName`), removes it, and writes the MSB to mod dir

## Current Stakes

| Zone | Map | Asset (RetryPartName) | Reason |
|------|-----|----------------------|--------|
| caelid_radahn | m60_12_09_02 | m60_51_36_00-AEG099_502_2000 | Respawns in caelid_preradahn (outside DAG) |

## Adding New Stakes

To remove additional vanilla stakes, add entries to `StakesToRemove` in `StakeRemover.cs`. The map and asset name can be found in fog.txt's `RetryPoints` section.
