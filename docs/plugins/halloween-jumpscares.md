# Halloween Jumpscares

Part of the Halloween plugin (`[plugin.halloween]`, see
[halloween-theme.md](halloween-theme.md)). In a few dungeon layers of the
run, an Aging Untouchable (`c5280`) appears right on the player a random
delay after they enter the cluster, fires its lantern burst (in `strike`,
then fights for a few seconds), then vanishes in a puff. Generation-side
only (regulation + MSB + EMEVD): the racing mod needs no change, but its
flag handling shapes the trigger (see "Why mirror flags").

## Configuration

```toml
[plugin.halloween]
enabled = true
jumpscares = 3              # dungeon layers armed per run; default 0 (off), max 10
jumpscare_mode = "scare"    # "scare" | "strike"
jumpscare_delay = [5, 25]   # seconds after entering an armed cluster
```

Opt-in: without `jumpscares`, no layer is armed, so a config that enables
the plugin for its text theme alone gets no jumpscare. Parsed strictly by
`HalloweenPluginSettings.Parse` (wrong type or value aborts the build).
Independent of `greeters`, `ambushes` and `darkness`.

## Selection

`JumpscarePlanner` (pure, unit tested) draws `jumpscares` layers among
the eligible ones with `new Random(graph.Seed ^ tag)`: the same seed always
arms the same layers. A layer is eligible when every one of its clusters:

- is a `mini_dungeon` or `legacy_dungeon`;
- is entered through at least one zone-tracking flag (`event_map`);
- contains no boss zone (fog.txt `DefeatFlag`): no zone-tracked fog
  separates a fight inside the cluster from the rest of it, so the delay could land
  mid-fight (both Academy clusters with the Red Wolf, the Leyndell and
  Ashen Leyndell sanctuaries);
- has at least one map that is not an open-world tile (`m60_*`, `m61_*`).

Every cluster of a drawn layer is armed. The DAG only links consecutive
layers and layers are type-homogeneous, so every path crosses each drawn
layer exactly once: every run meets exactly `jumpscares` armed clusters.
With fewer eligible layers, all are armed and a warning is logged.

Open-world clusters are left out because a tile streams in and out as the
player moves: Castle Morne, Redmane Castle, Castle Sol, Caria Manor,
Shaded Castle, Castle Ensis and Ruins of Rauh never qualify, so a layer
holding one is never drawn. The Leyndell clusters spanning m11_00 plus
one m60 tile qualify through m11_00, except the sanctuary one (boss
zone).

## Why mirror flags

The obvious trigger, the zone-tracking flag of each connection into the
cluster (`event_map`, 1050294xxx), does not work through the platform: the
racing mod polls those flags at 10 Hz, even during loading screens, and
resets each captured fog-gate flag to false to detect re-traversals
(`speedfog-racing` `mod/src/core/race_machine.rs`, "Regular fog gate:
clear after capture"). The destination map's event starts after the load,
when the flag is already gone. Offline play without the mod hides this.

The planner therefore allocates private saved flags in the mod-state
category the mod never polls: one "entered" flag per armed cluster, set on
every connection into it, and one "left" flag per drawn layer, set on
every connection into the next layer (the DAG only links consecutive
layers, so entering layer L+1 is leaving layer L). `HalloweenPlugin.AfterFogModWrite` (hook called by `WriteFogModPhase`)
appends them to `InjectionResult.RegionToFlags` right after
`BuildRegionToFlags`, next to the zone flag of each matching connection
(`JumpscarePlanner.AddMirrorFlags`), and `ZoneTrackingInjector` sets them
before the fog warp like any zone flag. Without jumpscares the table is
left untouched, so other seeds are unaffected.

A boss arena of the same dungeon (usually its own `boss_arena` cluster in
the same map) is only reached through a fog: before the armed cluster, the
event still waits for its entered flag; after it, the left flag or the
layer flag ends it. Only a forward fog sets the left flag: a backward fog
(back through the entrance), a grace warp, or a death respawn outside the
cluster but in the same map does not, so the actor may then appear
elsewhere in that map, once: an accepted residual.

## Placement

One actor per (armed cluster, dungeon map), placed by
`HalloweenJumpscareInjector.ApplyToMsb` from `HalloweenAmbientPass`,
between the decorations and the greeters (greeters carry `EntityID` 0 and
would pass the actor's vanilla clone-source filter). The actor clones the
vanilla enemy nearest to an entrance gate of the cluster in that map (the
map's first clone source without one, see
`AmbientSpawnInjector.CloneSources`) and stands on that enemy's spot. It
uses NpcParam `755890003` (the greeter recipe: flat `GREETER_TIER`
scaling, no runes, no drop) and the vanilla think row 52800000 (its AI
stays off, except for the fight of `strike` mode).

## Event

One event per actor in its map's EMEVD:

1. Disable the actor and its AI (always first: MSB parts load enabled).
2. End if the layer already played, or if the player already left the
   cluster (the layer's left flag is set).
3. Wait for the cluster's entered flag, then `jumpscare_delay`.
4. End if the layer played meanwhile (another actor of the cluster, in
   its other map) or the player left (lost, no carry-over); restart if
   the player is not in this map.
5. Set the layer flag (before the visual: a death or quit-out never
   replays it).
6. Enable and warp onto the player (`Warp Character and Copy Floor`,
   dummy 12, as vanilla event 2049422200 does with its helper). The actor
   stays hostile in both modes: a friendly team (type 26) would spare the
   player, but the hit of the burst is wanted.
7. Lantern burst 3001 straight away, the AOE vanilla plays right after its
   far teleport (in game, an arrival animation first showed the actor
   dropping in, left behind by the running player). It lands on the
   player: AtkParam_Npc 5280115, radius 4 m, 100 magic at the actor's
   tier-6 scaling (x1.76), reaction level EXLARGE (thrown back) only when
   its poise damage of 11 breaks the player's poise; the three lantern
   pulses it fires (bullets 205280000-002) add 10 madness each.
8. `strike` only: after `BURST_SECONDS`, switch the AI on and let the
   actor fight for `FIGHT_SECONDS` with the vanilla ambient untouchable's
   moveset (grab, far teleport, lantern pulses), not the boss's. `scare`
   keeps the AI off: the burst is the whole encounter.
9. The puff vanilla common event 90005636 spawns on an NPC before
   disabling it (SFX 643041 at dummy 905, 643040 at dummy 960), then
   disable `PUFF_SECONDS` later: the puff masks the disable.

| Mode | Burst | AI |
|------|-------|----|
| `scare` (default) | hits the player | off |
| `strike` | hits the player | on for `FIGHT_SECONDS` after the burst |

Timings are fixed waits (`BURST_SECONDS`, `FIGHT_SECONDS`, `PUFF_SECONDS`),
constants at the top of `HalloweenJumpscareInjector.cs` with the animation
and SFX ids.

### Why a puff, not a fade

The body cannot fade from EMEVD. `Set Character Fade On Enable` fades in
only: the disable stays instant. c5280's opacity keyframes (TAE event 193)
live in 3000 (vanilla teleport: snaps to 0 at 4.73 s, fades back in), 3001,
7600-7620 and the death animations, but `Force Animation Playback` only
reaches an animation the behavior exposes as `W_Event<id>`, and the shared
c9997 behavior has none for 7600-7620 (7600 forced anyway showed nothing in
game). c5280's materials expose no fade parameter for a SpEffectVfx
`materialParamId` to drive. Three vanilla exits were then compared in game
(2026-10-16): this puff, a forced death (its animation fades the body) and
the 601110 dissolve of common event 90005390. The puff was kept.

## IDs

| Kind | Value |
|------|-------|
| Layer flags (saved) | 1050290010-1050290019, ascending layer order |
| Left flags (saved, mirror) | 1050290020-1050290029, one per drawn layer, ascending |
| Entered flags (saved, mirror) | 1050290030-1050290099, one per armed cluster |
| Events | 755865600+, capacity 100 (one per actor) |
| Entities | 755920000+ (managed band) |
| NpcParam | 755890003 |

## In-game checks

Validated in game (2026-10-16, two test seeds, AI off): the actor appears
on the player and bursts at once, the hostile burst hits, and the puff
masks the disable.

Still owed:

1. Generation log: one `Halloween jumpscares: layer ...` line per drawn
   layer, followed by one line per armed cluster (display name, cluster
   id, the dungeon maps planned to hold its actors; a map skipped at
   placement logs its own warning); each path meets one jumpscare per
   drawn layer, none elsewhere.
   Play it through a racing-platform session with the mod connected
   (training is enough): offline play would hide a trigger that depends
   on flags the mod touches.
2. The warp lands the untouchable on the player without pushing them into
   a wall or off a ledge; no actor visible at its MSB spot before or after
   (die and reload in an armed cluster after it played). If the overlap
   looks bad, scout a c0000 dummy poly behind the player.
3. The burst (both modes): damage, thrown back only when it breaks the
   player's poise, madness up to 30.
4. `strike`: the fight. Tune `FIGHT_SECONDS`. Its far teleport (3000) may
   carry it away before the puff, which then plays wherever it stands
   (same mechanism as the greeters' open far-teleport question, see
   [halloween-ambient.md](halloween-ambient.md) "Greeter stats"). The
   puff disables it on a timer whatever it is doing: a puff landing
   during its grab (3002/3003, throw 4100) must release the player
   cleanly; if not, wait for the grab to end before the puff.
   The vanilla immunity wall is an EMEVD event per untouchable (see
   [untouchable-boss.md](../untouchable-boss.md)) the actor does not get,
   so it can be killed: check that the puff then plays on the body. Its
   lantern pulses keep adding madness during the fight.
5. Leaving a cluster before the delay expires loses its jumpscare.
6. Leyndell (m11_00 + m60 tile) and Nokron Ancestral Woods (two dungeon
   maps): fires only while the player is in a map hosting an actor, once.
