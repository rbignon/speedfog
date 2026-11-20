# Relay Plugin

Lets an outside driver (the racing mod) hold a player in place and move them
along the graph by raising event flags: a lock on every graph warp and on
fast travel from the Chapel, a warp to the entrance of any connection, and a
warp back to the Chapel grace. This is
the seed side of relay races, where a team's runners take turns on one run.
Opt-in via config:

```toml
[plugin.relay]
enabled = true
```

`enabled` is the only setting. With the plugin on and the lock flag OFF, the
gate events run as they do without it (the added instructions pass through).

## Flags

All saved, in category 1050290 (`SpeedFogIds`). Nothing in the seed raises
them: they are driven from outside EMEVD.

| Flag | Effect |
|------|--------|
| 1050290500 (lock) | While ON, no graph warp takes the player, and the Chapel map refuses fast travel |
| 1050290501 (Chapel warp) | Raised: warps the player to the Chapel grace, then clears itself |
| 1050290510 + n (connection warp) | Raised: warps the player to the entrance of connection n, then clears itself |

Flag 1050290502 is the plugin's own (the fast travel gate below): a driver
never writes it.

`n` is the connection's zone-tracking flag minus 1050294000 (`flag_id` in
`graph.json`), so a driver that knows which connection a runner took derives
the warp flag from it. The plugin holds 490 warp flags; a seed with a
connection flag beyond that fails to build.

FogModWrapper prints one line per warp flag in its build output (`relay:
flag 1050290512 -> <node> (...)`), with the destination map, the region and
the flags the warp sets.

## Lock

`RelayInjector.PatchEmevdFile` runs from the `OnPatchEmevd` hook on every
EMEVD file. Each event holding a warp instruction whose region belongs to a
graph connection (`RegionToFlags`, as for zone tracking) gets a wait and,
after each of its triggers, a check (here a fog gate):

```
IfEventFlag(MAIN, OFF, TargetEventFlagType.EventFlag, 1050290500)        # first line
...
IfConditionGroup(MAIN, PASS, OR_01)                                      # the gate's prompt
EndIfEventFlag(EventEndType.Restart, ON, TargetEventFlagType.EventFlag, 1050290500)
...
RotateCharacter(...)                                                     # walk through the fog wall
SetEventFlag(<zone-tracking flag>, ON)                                   # core patches
SetEventFlag(<trap flag>, ON)
WarpPlayer(...)
```

- **Wait at the top.** While the lock is ON the event is dormant: a fog gate
  shows no prompt, and an event triggered by a flag or a region runs none of
  its side effects.
- **Check after the trigger.** It covers a lock raised after the event
  passed its first line (a player already standing at the prompt). It sits
  right after the last wait on MAIN that precedes the warp (by instruction
  order, not by control flow), above everything the trigger sets off; the
  event restarts and stops on the top wait. An event with no wait above its
  warp only gets the top wait.

The check must come before the gate's animation: `RotateCharacter` with
animation 60060 walks the character through the fog wall, and FogMod warps
one second in. A check placed just before the warp restarts the event too
late: the player ends up on the far side of the gate, in the vanilla area
behind it (seen in game on the first test seed). For the same reason, a
lock raised during that second does not stop the warp.

Every skip-by-line-count instruction whose range holds an insertion point
skips one more line, so the event's branches land where they did.

Limits:

- Death and quit-outs are not EMEVD warps: the lock does not act on them.
- Fast travel is only refused from the Chapel map (next section), where a
  locked player is meant to wait.
- A save made while locked stays locked when nothing drives it any more:
  no gate, and no fast travel out of the Chapel.
- A lock raised between an event's trigger and its warp comes too late: the
  warp happens, with its zone and trap flags. The window is one second on a
  fog gate, 2 to 5 seconds on the sending gates and cutscene transitions.
  A driver that locks a player and then moves them must wait it out.
- Unlocking a fog gate brings its prompt back; an event triggered by a flag
  or a region whose trigger still holds warps by itself at the unlock.

## Fast travel gate

The game allows fast travel from a map while the event flag named by its
`MapDefaultInfoParam` row (`EnableFastTravelEventFlagId`) is ON; 0 means
always. Vanilla catacombs point it at their boss defeat flag, which is the
"cannot fast travel here" of an uncleared dungeon; FogMod zeroes every row.

`RelayInjector.ApplyFastTravelGate` points the Chapel row (10010000,
m10_01_00_00) at flag 1050290502, and a common.emevd event (slot 1 of
`RelayEvents`) keeps that flag the opposite of the lock:

```
SkipIfEventFlag(1, ON, EventFlag, 1050290500)
SetEventFlag(EventFlag, 1050290502, ON)
IfEventFlag(MAIN, ON, EventFlag, 1050290500)
SetEventFlag(EventFlag, 1050290502, OFF)
IfEventFlag(MAIN, OFF, EventFlag, 1050290500)
EndUnconditionally(EventEndType.Restart)
```

Each start of the event derives the flag from the lock's current state, so
a lock raised or lowered while the event was not running (a loading screen)
is caught up when it starts again. A seed nobody drives never raises the
lock, so its Chapel allows fast travel from the first frame. The refusal is the game's own, with its message. Only
the Chapel map is gated: a player locked elsewhere can still fast travel.

## Warp events

`RelayInjector.InjectWarpEvents` adds to common.emevd (event range
`RelayEvents`, 755866000+): slot 0 for the Chapel warp, slot 10 + n for
connection n:

```
IfEventFlag(MAIN, ON, EventFlag, <warp flag>)
SetEventFlag(EventFlag, <warp flag>, OFF)
<the SetEventFlag instructions the core patches put before the gate's warp>
WarpPlayer(<map>, <region>, 0)
WaitFixedTimeFrames(1)
EndUnconditionally(EventEndType.Restart)
```

The map, the region and the replayed flags (zone-tracking flags, boss trap
flag, the Erdtree map variant flag 300) are recorded during the lock scan,
from the first compiled warp met for the region: the run of `SetEventFlag`
right above it, from the region's first zone-tracking flag down (the ones
above are the event's own). Notes:

- The warp events ignore the lock: the driver decides who moves.
- They have no guard on the player's state (dead, in an animation, in a
  menu): the driver picks the moment.
- A shared entrance sets every zone flag of its region, as its gate does.
- An entrance with an alternate side (AlternateFlag) has two regions; the
  event warps to the first one a compiled warp was found for, in region
  order, whatever the alternate flag's state.
- A cutscene transition (`PlayCutsceneToPlayerAndWarp`) gets a plain
  `WarpPlayer` to the same region: no cutscene, and none of the story flags
  the vanilla event sets on the way. Not checked in game.
- The Chapel warp targets the grace's default warp entity
  (`ChapelGraceInjector.DefaultPlayerEntity`); the injector warns when the
  grace had to take another id. A seed without the Chapel grace
  (`chapel_grace = false`) gets no Chapel warp event (build warning).

## In-game checks

On a test seed, without the racing mod: Cheat Engine with The Grand Archives
table, `Event Flags > Event Flags by ID` (re-type the ID after an action, its
`State` is a snapshot).

1. **Baseline.** Lock OFF: take a fog gate from the Chapel, it works as usual.
2. **Lock.** At the Chapel, set 1050290500 ON and walk to a fog gate: no
   prompt. Set it OFF: the prompt comes back and the gate works.
3. **Late lock.** Stand at a gate with its prompt shown, set the lock ON,
   then use the gate: nothing happens (no animation, no warp) and the prompt
   is gone afterwards. Set it OFF: the gate works at the next use, not by
   itself.
4. **Lock across a reload.** Lock ON, quit to the title screen, load: still
   locked.
5. **Connection warp.** From the Chapel, raise a warp flag from the build output
   (pick a non-boss node first): the player lands at that node's entrance,
   the flag reads OFF afterwards.
6. **Boss arena warp.** Raise the warp flag of a boss arena with a trap flag
   (its line lists a second flag after the zone flag, e.g. `sets 1050294002, 21000851`): the player lands in the arena and its exits
   are locked until the boss dies.
7. **Warp while locked.** Lock ON, raise a warp flag: the warp still happens;
   the fog gates of the destination show no prompt.
8. **Chapel warp.** From anywhere, raise 1050290501: back at the Chapel grace.
9. **Fast travel, unlocked.** At the Chapel with the lock OFF: fast travel
   from the map and from the Chapel grace both work (flag 1050290502 reads
   ON).
10. **Fast travel, locked.** Lock ON at the Chapel: the map refuses fast
    travel, from the map screen and from the grace. Lock OFF: it works again
    without reloading.
11. **Fast travel, locked, after a reload.** Lock ON, quit to the title
    screen, load: still refused.
12. **Fast travel, map already open.** Open the map with the lock OFF,
    raise the lock, then pick a grace: note whether the travel goes through.
13. **Fast travel, lock raised during a load.** Raise the Chapel warp flag
    from another zone and set the lock ON during the loading screen: at the
    Chapel, fast travel is refused. Try it from two spots of the Chapel.
14. **Warp from a menu or a grace.** Raise a warp flag while sitting at a
    grace, and while a menu is open: note what happens (warp at once, on
    leaving, or never).
