# Quit-Out Respawn (Stable Position)

**Date:** 2026-07-10
**Status:** Active

Why quit-outs sometimes respawned players at the last grace instead of their
last position, and the two patches that fix it: `PlayRegionPatcher` and
`MakestablePulsePatcher`.

## Engine mechanism

`PlayRegionParam.pcPositionSaveLimitEventFlagId` gates the engine's player
position save (paramdef: flag ON = position save enabled, OFF = disabled,
0 = always enabled). On quit-out reload the player spawns at the last saved
position; when nothing usable was saved since the last map load, the engine
falls back to the last grace.

MSB collision parts carry a `PlayRegionID` that keys into `PlayRegionParam`
(inspect with `game_inspect list-collisions <msb>`). Vanilla regulation has
593 rows:

| Rows | Vanilla flag | Meaning |
|------|--------------|---------|
| row 0 + 89 rows | 6001 | Constant, always ON (set by vanilla common.emevd Event 50 at every load). Row 0 is the default region for all ground; the others are DLC overworld regions. |
| 129 rows | boss defeat flags (`…800`/`…850`) | Position save disabled inside the boss's play region until the boss dies. These regions are arena-scale: Stormveil has 4 such collisions (Godrick's arena), the Chapel has 1 (Grafted Scion's courtyard). |
| 1 row (3414010) | 6000 | Constant, always OFF: position never saved there. |
| rest | 0 | Always saved. |

Vanilla mid-boss quit-out behavior follows: you walked into the arena on
foot, so your last saved position is just outside the fog gate, and that is
where you reload.

## What FogMod does, and why it breaks

FogMod (GameDataWriterE.cs L1853-1879 in the decompiled sources) remaps
**every** nonzero `pcPositionSaveLimitEventFlagId` to a temp flag (base
1040292100, a 2xxx offset wiped on area reload, one flag per distinct
vanilla value) and initializes a `common_makestable` event (fogevents.txt ID
755850000) per distinct flag in common.emevd Event 0:

```
SetEventFlag(temp, ON)
EndIfEventFlag(End, ON, vanilla_flag)   // 6001 or boss already dead: stays ON
WaitFixedTimeFrames(10)
SetEventFlag(temp, OFF)                 // boss alive: unstable after 10 frames
IfEventFlag(MAIN, ON, vanilla_flag)     // wait for boss defeat
EndUnconditionally(Restart)             // then permanent ON
```

Every fog gate traversal is a `WarpPlayer` with a loading screen, which
re-runs common.emevd Event 0. The 10-frame pulse therefore anchors the
player's arrival position when warping into a boss arena: a quit-out
mid-fight respawns at the arena entrance (inside), instead of a stale
pre-warp position in another map.

The defect: 10 frames (~0.3 s at the 30 fps event tick) races against the
engine grounding the player after the warp fade-in. When the pulse misses,
no position is ever saved inside the gated region, and the next quit-out
falls back to the **last grace**. This is intermittent (load timing,
framerate) and hard to reproduce on demand, which matches the field reports.

In-game checks performed (2026-07): resting at a grace then quitting without
a loading screen respawns correctly (no rest-wipe issue), and quit-outs on
open ground after a fresh load respawn in place (the 6001 group is
re-asserted at every load). The exposure is the pulse race inside
boss-gated regions.

## Fix 1: PlayRegionPatcher (regulation phase)

Remapping the constant-flag rows buys nothing: 6001 is already ON at every
load, and FogMod's remap replaces a permanently ON gate with a temp flag
re-asserted by EMEVD timing (plus a 1-2 frame flicker at load). Worse, the
single 6000 row gets inverted: vanilla never saves there, but the pulse
makes it saveable for 10 frames per load.

`PlayRegionPatcher.ApplyTo` (Phase 7, `ApplyRegulation`) opens the vanilla
regulation from `--game-dir`, collects the rows whose vanilla value is 6000
or 6001, and writes those values back into the modded regulation. Rows gated
by boss defeat flags keep FogMod's makestable behavior (arena entrance
anchoring). The makestable event instance for the 6001 group keeps running
in common.emevd; it just pulses a flag no row references anymore.

## Fix 2: MakestablePulsePatcher (common.emevd phase)

A first iteration extended `WaitFixedTimeFrames(10)` to 150 frames (5 s at
30 fps). That made the capture reliable but broke the anchor semantics: the
engine keeps re-saving the position while the flag is ON, so the quit-out
anchor became "wherever the player stood 5 s after entry", not the arena
entrance. A racer could sprint toward the boss during the window and then
use quit-outs to skip the run-back.

`MakestablePulsePatcher.Patch` instead gates the pulse start on the end of
the loading screen, keeping FogRando's original 10-frame pulse. It inserts
three instructions before the wait in the compiled event 755850000:

```
SetEventFlag(temp, ON)
EndIfEventFlag(End, ON, vanilla_flag)
IfEventFlag(OR_01, OFF, EventFlag, 2200)   <- inserted: fade-in finished
IfElapsedSeconds(OR_01, 5)                 <- inserted: or safety timeout
IfConditionGroup(MAIN, ON, OR_01)          <- inserted
WaitFixedTimeFrames(10)
SetEventFlag(temp, OFF)
...
```

Engine flag 2200 means "world clock stopped": ON during loading screens and
cutscenes, it drops about 0.9 s after the player position becomes readable,
at fade-in end (characterized 2026-07 by instrumenting the racing overlay's
read of the flag byte; the Hexinton CE table mislabels it "In cut-scene/
loading screen", see the `freeze_time` note in `docs/plugins/weather.md`).
The pulse therefore starts once the player is placed and grounded, and the
anchor lands at the arena entrance with at most 10 frames of drift. When an
entry cutscene plays, 2200 stays ON through it and the anchor becomes the
post-cutscene position, which is the desirable behavior.

The `IfElapsedSeconds` timeout is a safety net for anything that keeps the
world clock stopped indefinitely (an EMEVD `FreezeTime(true)`, which the
weather plugin deliberately avoids): after 5 s the pulse runs anyway,
degrading to the fixed-window behavior of the first iteration.

The event is parameterized (X0_4/X4_4 substituted via the event's
`Parameters` table, keyed by instruction index), so the patcher shifts the
entries pointing past the insertion point by the number of inserted
instructions. The patch only applies when the event contains exactly one
`WaitFixedTimeFrames(10)`: if a future FogRando version changes the
template, the patcher logs and leaves it alone.

Patching the compiled event (rather than `data/fogevents.txt`) is deliberate:
the template file is overwritten at bootstrap.

## Verifying a build

```bash
# Restored rows in the output regulation (expect 6001, not 10402921xx)
wine tools/game_inspect/publish/win-x64/game_inspect.exe dump-param \
  output/mods/fogmod/regulation.bin PlayRegionParam --row 0 \
  --defs writer/FogModWrapper/eldendata/Defs --field pcPositionSaveLimitEventFlagId

# Gated pulse in the compiled makestable event (expect IfEventFlag(OR_01, OFF, 2200),
# IfElapsedSeconds(OR_01, 5), IfConditionGroup before WaitFixedTimeFrames(10))
cd tools/dump_emevd_warps && dotnet run -- dump ../../output/mods/fogmod/event/ --event 755850000
```

In-game: warp into a boss arena through a fog gate, quit out immediately
(before the fight) and again mid-fight; both should reload at the arena
entrance, never at the last grace.

The 2200 gate itself needs two in-game discriminants, because an unreadable
flag can degrade two ways (no vanilla script reads the 2200-2207 range, so
EMEVD readability rests on these tests):

- **Sprint test** (catches "condition never true" → timeout path): warp into
  an arena, sprint toward the boss, quit out after ~20 s. Respawning at the
  entrance means EMEVD read the flag; respawning where you stood ~5 s after
  entry means only the timeout fired and the anchor drift is back.
- **Repeated immediate quit-outs** (catches "flag reads constant OFF" → gate
  passes instantly, reintroducing the original 10-frame race): warp into an
  arena and quit out right away, several times across different loads. Any
  respawn at the last grace instead of the entrance means the pulse raced
  the fade-in again.
