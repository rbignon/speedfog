# Quit-Out Respawn (Stable Position)

**Date:** 2026-07-10, updated 2026-07-26
**Status:** Active (PlayRegionPatcher); the makestable pulse patch was removed
after an A/B test, see the post-mortem below.

Why quit-outs sometimes respawned players at the last grace instead of their
last position, the patch that fixes it (`PlayRegionPatcher`), and why a second
patch (`MakestablePulsePatcher`, 2026-07-10 to 2026-07-26) was removed.

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

## What FogMod does

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
re-runs common.emevd Event 0. The intent of the 10-frame pulse is to anchor
the player's arrival position when warping into a boss arena, so a quit-out
mid-fight respawns at the arena entrance instead of a stale pre-warp
position in another map. In practice the pulse runs while the loading screen
is still up and appears to capture rarely, if ever (see the post-mortem).

## Fix: PlayRegionPatcher (regulation phase)

Remapping the constant-flag rows buys nothing: 6001 is already ON at every
load, and FogMod's remap replaces a permanently ON gate with a temp flag
re-asserted by EMEVD timing (plus a 1-2 frame flicker at load). Worse, the
single 6000 row gets inverted: vanilla never saves there, but the pulse
makes it saveable for 10 frames per load.

`PlayRegionPatcher.ApplyTo` (Phase 7, `ApplyRegulation`) opens the vanilla
regulation from `--game-dir`, collects the rows whose vanilla value is 6000
or 6001, and writes those values back into the modded regulation. Rows gated
by boss defeat flags keep FogMod's stock makestable behavior. The makestable
event instance for the 6001 group keeps running in common.emevd; it just
pulses a flag no row references anymore.

This is the fix for the reported last-grace respawns: the 2026-07-26 A/B
test showed post-kill arena quit-outs were fine with the stock event
(below), so the field reports are attributed to the constant-flag rows this
patcher restores. The 2026-07 open-ground spot checks that had originally
exonerated those rows were few and manual; they could not rule out an
intermittent exposure of the EMEVD-timed temp-flag re-assert, which is
exactly the dependency this patcher removes.

## Post-mortem: MakestablePulsePatcher (removed 2026-07-26)

The 2026-07-10 investigation attributed the last-grace field reports to the
10-frame pulse racing the post-warp fade-in inside boss-gated regions (an
attribution by elimination: rest-wipe and open-ground checks came back
clean, and the pulse miss itself was never directly reproduced). Two
iterations of a pulse patch followed:

1. **150-frame window** (b2b9e1c): extending `WaitFixedTimeFrames(10)` to
   150 frames made the capture reliable but broke the anchor semantics: the
   engine keeps re-saving the position while the flag is ON, so the anchor
   drifted to wherever the player stood 5 s after entry. A racer could
   sprint toward the boss and quit out to skip the run-back.
2. **Load-end gate** (4c8206b): restored the 10-frame pulse and gated its
   start on engine flag 2200 ("world clock stopped", drops ~0.9 s after the
   player position becomes readable at fade-in end), so the anchor landed
   reliably at the arena entrance.

The gate worked as designed, and that was the problem: it guaranteed a
stale entrance anchor exists, and a quit-out shortly after a boss kill
could reload at that anchor instead of the kill position.

A/B test (2026-07-26, seed 514184634 built with and without the patch,
about ten post-kill quit-outs each): the entrance respawn reproduced
intermittently with the gate and never without it; the stock build always
resumed at the kill position. The engine's exact post-defeat save timing
remains uncharacterized: under the model above, a stock quit-out completed
before the defeat flag rises and saving resumes should fall back to the
last grace, which was never observed either, so either every stock quit-out
landed after the re-capture or the engine's behavior with no saved position
in the region is gentler than the last-grace fallback here. What the A/B
does establish is the causal direction: the stale anchor introduced by the
gate is what sends post-kill quit-outs to the entrance. Since boss arenas
never exhibited the last-grace problem in the field, the patch fixed a
defect that was never observed and introduced one that was. It was removed
entirely; the makestable event ships stock (FogRando parity).

If arena mid-fight quit-outs ever do fall back to the last grace, revisit
this with the git history (`MakestablePulsePatcher.cs` and its tests were
deleted on 2026-07-26); any future anchor mechanism must keep the post-kill
window in mind, e.g. by re-enabling the temp flag on boss death
(`IfCharacterDeadAlive`) rather than waiting for the defeat flag.

## Verifying a build

```bash
# Restored rows in the output regulation (expect 6001, not 10402921xx)
wine tools/game_inspect/publish/win-x64/game_inspect.exe dump-param \
  <seed_dir>/mods/fogmod/regulation.bin PlayRegionParam --row 0 \
  --defs writer/FogModWrapper/eldendata/Defs --field pcPositionSaveLimitEventFlagId

# Stock makestable event (expect the 6-instruction template, no flag 2200 gate)
cd tools/dump_emevd_warps && dotnet run -- dump <seed_dir>/mods/fogmod/event/ --event 755850000
```

In-game: quit-outs on open ground and at graces respawn in place; a
quit-out right after killing a boss resumes at the kill position. A
quit-out mid-fight inside a warp-entered arena is expected to have no saved
position in the region and to fall back to the last grace: that is stock
FogRando behavior, and it has not been a problem in the field.
