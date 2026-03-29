# Alternate Warp Patching

**Date:** 2026-02-26
**Status:** Active

How SpeedFog patches FogMod's compiled fogwarp events to eliminate dependencies on AlternateFlag map variants.

## Background: FogMod's AlternateSide Mechanism

Some Elden Ring map locations exist as two variants controlled by a game progress flag. FogMod's `fogwarp` template (9005777) has built-in alt-warp logic: each compiled event can have a primary destination and an alternate destination, selected at runtime by checking an `AlternateFlag`.

From `fog.txt`, an entrance `Side` can declare `AlternateOf: other_entrance_name A 330`, which means:
- The primary `Side.Warp` is used when flag 330 is OFF
- The `AlternateSide.Warp` is used when flag 330 is ON

FogMod compiles this into literal WarpPlayer instructions with branching:
```
if AlternateFlag is ON:
    WarpPlayer(altMap, altRegion)
else:
    WarpPlayer(primaryMap, primaryRegion)
```

Both branches are baked into the per-instance event at build time (no template call at runtime).

### Known AlternateFlag pairs

| Flag | Primary map | Alternate map | Area | Controlled by |
|------|-------------|---------------|------|---------------|
| 300 | m11_00_00_00 (Leyndell) | m11_05_00_00 (Ashen Leyndell) | Erdtree | Maliketh death (Event 900) |
| 330 | m61_44_45_00 (Romina arena) | m61_44_45_10 (post-burning) | DLC Sealing Tree | Dancing Lion death (Event 915) |

## The Problem

SpeedFog generates short runs where the player may reach these areas without completing the vanilla prerequisites. However:

- **Flag 300** (Erdtree): Needs to be ON for SpeedFog. The Ashen variant (m11_05) is the one with the Erdtree boss. Without Maliketh, flag 300 stays OFF and the warp goes to m11_00 (pre-ash Leyndell with no Erdtree boss).
- **Flag 330** (Sealing Tree): Needs to be OFF for SpeedFog. When ON, the warp goes to m61_44_45_10 (post-burning variant where Romina is absent). Something outside EMEVD — likely save file state from a prior DLC playthrough — activates this flag.

Both cases cause the wrong map variant to load, breaking the run.

## Solution: Post-Process Warp Destinations

Rather than managing the flags at runtime, SpeedFog rewrites the compiled warp instructions directly, eliminating the flag dependency.

### ErdtreeWarpPatcher (flag 300)

**Goal**: Make Erdtree warps always go to m11_05 (Ashen Leyndell).

**Strategy**: Replace primary destination (m11_00) with alternate destination (m11_05).

1. For each EMEVD file (via consolidated scan in Program.cs), find WarpPlayer/CutsceneWarp targeting the primary region
2. Replace map bytes and region with the alternate (m11_05) values
3. Insert `SetEventFlag(300, ON)` before each patched warp -- the engine needs flag 300 ON to load m11_05 tile assets

The SetEventFlag insertion is critical: unlike flag 330, flag 300 controls which physical map tile the engine loads at Leyndell coordinates. Setting it only at warp time means Leyndell stays in its primary state during the run, and only switches to Ashen at the moment the player warps to the Erdtree.

**Wired in Program.cs** at step 7f2. Entrance discovery:
```csharp
// Find the entrance where BSide targets leyndell_erdtree with an AlternateSide
var erdtreeEntrance = ann.Entrances.Concat(ann.Warps).FirstOrDefault(e =>
    e.BSide?.Area == "leyndell_erdtree" &&
    e.BSide?.AlternateSide?.Warp != null &&
    e.BSide.AlternateFlag > 0);
```

**Source**: `writer/FogModWrapper/ErdtreeWarpPatcher.cs`

### SealingTreeWarpPatcher (flag 330)

**Goal**: Make Romina-area warps always go to m61_44_45_00 (pre-burning, where Romina exists).

**Strategy**: Replace alternate destination (m61_44_45_10) with primary destination (m61_44_45_00). This is the reverse of ErdtreeWarpPatcher.

1. For each EMEVD file (via consolidated scan in Program.cs), find WarpPlayer/CutsceneWarp targeting the alternate region
2. Replace map bytes and region with the primary (m61_44_45_00) values
3. No SetEventFlag insertion -- flag 330 only controls the fogwarp destination, not map tile loading. We want it OFF (or irrelevant).

There are 2 entrance pairs with flag 330 (front + back of Romina's arena). Both are patched if connected.

**Wired in Program.cs** at step 7f3. Entrance discovery:
```csharp
// Find all sides with AlternateFlag 330 (Sealing Tree burned)
var sealingTreeEntrances = ann.Entrances.Concat(ann.Warps)
    .SelectMany(e => e.Sides())
    .Where(s => s.AlternateFlag == 330 && s.AlternateSide?.Warp != null && s.Warp != null)
    .Select(s => (altRegion: s.AlternateSide.Warp.Region, primaryRegion: s.Warp.Region, primaryMap: s.Warp.Map))
    .ToList();
```

**Source**: `writer/FogModWrapper/SealingTreeWarpPatcher.cs`

### AlternateFlagPatcher (defense-in-depth)

In addition to rewriting warp destinations, SpeedFog also neutralizes the EMEVD events that set AlternateFlag values:

**Flag 330 / Event 915 (Sealing Tree):**
1. NOP `SetEventFlag(330, ON)` in Event 915 (common.emevd) -- replaced with `WaitFixedTime(0)`
2. Insert `SetEventFlag(330, OFF)` in Event 0 -- clears the flag on game start for stale saves

**Flag 300 / Event 900 (Erdtree burning):**
1. NOP `SetEventFlag(300, ON)` in Event 900 (common.emevd) -- replaced with `WaitFixedTime(0)`
2. Insert `SetEventFlag(300, OFF)` in Event 0 -- clears the flag on game start

Without this, Event 900 sets flag 300 when the DAG includes `farumazula_maliketh` connections (which use the Forge WarpBonfire transition). This causes the compiled fogwarp's `SkipIfEventFlag(flag=300)` to skip the zone tracking `SetEventFlag` injected by ZoneTrackingInjector.

**Source**: `writer/FogModWrapper/AlternateFlagPatcher.cs`

## Comparison

| | ErdtreeWarpPatcher | SealingTreeWarpPatcher |
|---|---|---|
| Flag | 300 (Erdtree burning) | 330 (Sealing Tree burned) |
| Direction | Primary → Alternate | Alternate → Primary |
| Inserts SetEventFlag | Yes (300 ON, before warp) | No |
| Why SetEventFlag | Engine needs flag to load m11_05 tile | Flag only affects fogwarp branch, not tile loading |
| Entrances | 1 (leyndell_erdtree BSide) | 2 (front + back of Romina arena) |
| Companion patcher | AlternateFlagPatcher (neutralizes Event 900) | AlternateFlagPatcher (neutralizes Event 915) |

## Pipeline Order

All three patchers run after FogMod writes its EMEVD files. Program.cs performs a single consolidated scan over all EMEVD files, applying ErdtreeWarpPatcher.PatchEmevd(), SealingTreeWarpPatcher.PatchEmevd(), and ZoneTrackingInjector.PatchEmevdFile() to each file in one pass (one Read + one Write per file). AlternateFlagPatcher operates on common.emevd in memory.

```
7.   FogMod GameDataWriterE.Write()    — generates EMEVD with compiled fogwarps
     Consolidated EMEVD scan           — single pass applies all three patchers per file
     common.emevd injectors            — includes AlternateFlagPatcher (neutralize Events 900/915)
```

## Adding New AlternateFlag Patchers

If another `AlternateOf` flag causes issues in the future:

1. Check `fog.txt` for the `AlternateOf` declaration to identify the flag, primary/alt maps, and entrance sides
2. Determine the direction: does SpeedFog need the primary or alternate variant?
3. Determine if SetEventFlag is needed: does the flag control map tile loading (like 300) or only fogwarp branching (like 330)?
4. Create a patcher following the ErdtreeWarpPatcher/SealingTreeWarpPatcher pattern
5. Wire it in Program.cs between steps 7f2 and 7g

## References

- FogMod alt-warp compilation: `reference/fogrando-src/GameDataWriterE.cs` L3333-3348
- AlternateOf parsing: `reference/fogrando-src/GameDataWriterE.cs` L595-605
- fogwarp template alt-warp args: `data/fogevents.txt` (X24_4=AlternateFlag, X28_4=altRegion, X32=altMapBytes)
- WarpPlayer instruction: bank 2003, id 14 — `[area(1), block(1), sub(1), sub2(1), region(4)]`
- PlayCutsceneToPlayerAndWarp: bank 2002, id 11/12 — `[cutsceneId(4), playback(4), region(4), mapPacked(4)]`
