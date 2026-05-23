# Vanilla Stake Removal

**Date:** 2026-03-16
**Status:** Active

Removes vanilla Stakes of Marika whose RetryPoints respawn the player outside the boss arena that owns them, breaking the SpeedFog progression.

## Problem

Some boss zones have a vanilla RetryPoint (Stake of Marika) whose respawn position is not inside the boss arena. Two sub-cases:

1. **Cross-map respawn (DAG-aware):** the stake's `PlayerMap` points to a different map than the stake itself, in a zone outside the SpeedFog DAG. The player is softlocked after dying at the boss.
2. **Shared-MSB respawn (intra-map bypass):** the activation region and the respawn position live in the same MSB but in different fog.txt areas (e.g., a boss arena MSB shared with its "pre" zone). The respawn position bypasses the SpeedFog fog gate, breaking the run flow even when both zones are in the DAG.

In both cases the activation is **conditional on a region trigger**: the stake only takes effect if the player crosses a specific area inside the arena, so the symptom is intermittent.

**Example 1 — caelid_radahn (cross-map):**

- Vanilla RetryPoint in MSB `m60_12_09_02` (LOD level 2 tile covering Redmane Castle area)
- Asset: `m60_51_36_00-AEG099_502_2000` (the Stake of Marika 3D model)
- PlayerMap: `m60_51_36_00` (respawn position is in `caelid_preradahn`)
- `caelid_preradahn` is NOT in the SpeedFog DAG → player softlocked after boss death

**Example 2 — mohgwyn_boss (shared MSB):**

- Vanilla RetryPoint in MSB `m12_05_00_00` (shared between `mohgwyn` and `mohgwyn_boss`)
- Asset: `AEG099_503_9001`
- fog.txt tags it as `Area: mohgwyn_boss` (activation region labelled "ボス部屋" / boss room)
- But the respawn `c0000` Part.Player is positioned just outside the boss-room region (verified in-game: the player respawns in front of the arena, not inside)
- Triggers only when the player crosses a specific spot in the arena (e.g., near the right wall close to the entrance), hence intermittent reports
- Even with `mohgwyn` in the DAG, the respawn bypasses the SpeedFog fog gate that controls entry to the boss arena

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
| caelid_radahn | m60_12_09_02 | m60_51_36_00-AEG099_502_2000 | Cross-map: respawns in caelid_preradahn (outside DAG) |
| mohgwyn_boss | m12_05_00_00 | AEG099_503_9001 | Shared MSB: respawn position falls outside the arena, bypasses fog gate |

## Investigation Methodology

To identify additional vanilla stakes that need removal:

1. **Grep fog.txt RetryPoints by area:** locate the entry with `Area: <boss_zone>` and note `Map`, `Name`, optional `PlayerMap`, and the DebugInfo region label.
2. **Classify the risk:**
   - `PlayerMap` ≠ `Map` and the `PlayerMap` zone is not in the DAG → cross-map case.
   - Same `Map`, but several fog.txt areas list it under `Maps:` → shared-MSB case, requires geometric verification. Enumerate the sharing areas with:
     ```bash
     awk '/^- Name:/{name=$0} /Maps:.*<map>/{print name}' data/fog.txt
     ```
3. **Geometric verification (shared-MSB case):** use `tools/game_inspect` to dump the asset and its associated `c0000` Part.Player. The player entity ID is given by the RetryPoint's DebugInfo `player` line in fog.txt (it also equals the asset entity ID minus 970, per FogMod source). Compare its `Position` to the `BossPos` of each area sharing the map. If the position is closer to a non-boss area, the stake is a candidate.
4. **In-game confirmation:** with a minimal seed including only the target boss arena, die at the boss from multiple positions inside the arena to reproduce the intermittent activation, and observe the respawn point.

Once confirmed, add the `(Map, Name)` tuple to `StakesToRemove` in `StakeRemover.cs`.
