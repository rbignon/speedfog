# Erdtree Flag 300 Fix

**Date:** 2026-03-28
**Status:** Approved

## Problem

When a SpeedFog DAG includes the `farumazula_maliketh` connection (which uses the Forge WarpBonfire transition via Event 900 in common.emevd), Event 900 sets flag 300 (Erdtree burning) as a side effect. Later, when the player reaches the `leyndell_erdtree` fog gate, the compiled fogwarp event contains:

```
[019] SkipIfEventFlag(skip=2, state=ON, flag=300)
[020] SetEventFlag(zone_tracking_flag, ON)      // ZoneTrackingInjector
[021] SetEventFlag(300, ON)                      // ErdtreeWarpPatcher
[022] WarpPlayer(m11_05, altRegion)
```

Because flag 300 is already ON, the SkipIfEventFlag skips instructions [020] and [021], and the zone tracking flag is never set. The racing mod cannot detect the player entering `leyndell_erdtree`.

### Evidence

Verified on two seeds with `leyndell_erdtree_ca15` as final boss:

- **Seed eccdfc291f70**: 1 player, flag 1050294077 never emitted. Event 900 fired at IGT 703s, set flag 300. Erdtree fogwarp at IGT 2750s skipped zone tracking.
- **Seed 4d7e4ee690ae**: 25 players. Perfect correlation: all 5 players who traversed `farumazula_maliketh` (triggering Event 900) are missing `leyndell_erdtree_ca15` from zone_history. All 17 finished players who did NOT traverse `farumazula_maliketh` have it correctly tracked.

The bug is deterministic by DAG path, not save-dependent (all players use fresh saves).

### Root Cause Chain

1. FogMod repurposes Event 900 (Forge bonfire transition) for the `farumazula_maliketh -> X` connection
2. Event 900 sets flag 300 ON at instruction [017] (vanilla Erdtree burning logic)
3. ErdtreeWarpPatcher patches the fogwarp destination but does not neutralize the SkipIfEventFlag(flag=300) in the compiled template
4. ZoneTrackingInjector inserts SetEventFlag after the SkipIfEventFlag, so the tracking flag is in the skipped range

## Solution

Neutralize `SetEventFlag(300, ON)` in Event 900, following the exact pattern already used by `SealingTreePatcher` for flag 330 / Event 915:

1. **NOP**: Replace `SetEventFlag(300, ON)` in Event 900 with `WaitFixedTime(0)`
2. **Clear at startup**: Insert `SetEventFlag(300, OFF)` at the start of Event 0

ErdtreeWarpPatcher already sets flag 300 at the correct moment (just before each Erdtree warp instruction). Event 900 setting it earlier is both redundant and harmful.

### Why Not Alternatives

- **Move zone tracking before SkipIfEventFlag (approach B)**: Requires parsing compiled fogwarp template structure (skip chains, labels). Fragile, FogMod template format could change.
- **NOP the SkipIfEventFlag itself (approach C)**: Requires heuristic scan to find the associated SkipIfEventFlag for each warp. More intrusive than neutralizing the source.

## Implementation

### Rename SealingTreePatcher to AlternateFlagPatcher

The class handles both AlternateFlag cases (flag 300/Event 900 and flag 330/Event 915). Rename to reflect this broader scope.

### Changes

**AlternateFlagPatcher.cs** (renamed from SealingTreePatcher.cs):

- Add constants: `ERDTREE_BURNING_FLAG = 300`, `EVENT_900_ID = 900`
- Rename existing constants for clarity (SEALING_TREE_FLAG, EVENT_915_ID stay as-is or follow a consistent naming)
- `Patch()` applies the NOP + clear pattern to both (Event 900, flag 300) and (Event 915, flag 330)
- Update class-level docstring to describe both flags
- Internal methods (`NopSetEventFlag`, `InsertClearFlag`, `MakeSetEventFlag`, `MakeWaitFixedTime`) are already generic, no changes needed

**Program.cs**:

- Update the call site: `SealingTreePatcher.Patch(commonEmevd)` becomes `AlternateFlagPatcher.Patch(commonEmevd)`
- Update the comment at the call site

**FogModWrapper.Tests/**:

- Add test for Event 900 flag 300 patching (parallel to existing Event 915 tests if any)
- Verify existing tests still pass after rename

**docs/alternate-warp-patching.md**:

- Correct the comparison table: "Companion patcher: None needed" becomes reference to AlternateFlagPatcher
- Add Event 900 details to the SealingTreePatcher section (now AlternateFlagPatcher)

**CLAUDE.md**:

- Update the FogModWrapper class table: rename SealingTreePatcher entry to AlternateFlagPatcher, update description

### What Does NOT Change

- `ErdtreeWarpPatcher.cs`: unchanged, still sets flag 300 just before each warp
- `ZoneTrackingInjector.cs`: unchanged
- `ConnectionInjector.cs`: unchanged
- Pipeline execution order in Program.cs: identical
- Flags 301, 302 in Event 900: untouched (they don't interfere with zone tracking)

## Verification

1. Build succeeds, all existing tests pass
2. New unit test confirms Event 900 is patched (SetEventFlag(300) replaced with NOP)
3. New unit test confirms flag 300 cleared in Event 0
4. Dump EMEVD on a regenerated seed: `dump_emevd_warps search --flag 300` should show no SetEventFlag(300, ON) in Event 900
