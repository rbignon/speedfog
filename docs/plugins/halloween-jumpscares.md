# Halloween Jumpscares

Part of the Halloween plugin (`[plugin.halloween]`, see
[halloween-theme.md](halloween-theme.md)). In a few dungeon layers of the
run, an Aging Untouchable (`c5280`) appears right on the player a random
delay after they enter the cluster, then fades away. Generation-side only
(regulation + MSB + EMEVD): the racing mod needs no change, but its flag
handling shapes the trigger (see "Why mirror flags").

## Configuration

```toml
[plugin.halloween]
enabled = true
jumpscares = 3              # dungeon layers armed per run; 0 disables (max 10)
jumpscare_mode = "scare"    # "scare" | "strike"
jumpscare_delay = [5, 25]   # seconds after entering an armed cluster
```

Parsed strictly by `HalloweenPluginSettings.Parse` (wrong type or value
aborts the build). Independent of `greeters`, `ambushes` and `darkness`.

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
layers, so entering layer L+1 is leaving layer L). `Program.WriteFogMod`
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
scaling, no runes, no drop) and the vanilla think row 52800000 (its AI is
off for the whole jumpscare).

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
6. Enable; in `scare` mode switch to team type 26 (Friendly NPC); warp
   onto the player (`Warp Character and Copy Floor`, dummy 12, as vanilla
   event 2049422200 does with its helper); arrival animation 5010.
7. `strike`: lantern burst 3001. `scare`: stand for 2 s.
8. Fade-out 3000, then disable.

Timings are fixed waits (`ARRIVAL_SECONDS`, `BURST_SECONDS`,
`LINGER_SECONDS`, `FADE_SECONDS`), constants at the top of
`HalloweenJumpscareInjector.cs` with the animation ids.

## IDs

| Kind | Value |
|------|-------|
| Layer flags (saved) | 1050290010-1050290019, ascending layer order |
| Left flags (saved, mirror) | 1050290020-1050290029, one per drawn layer, ascending |
| Entered flags (saved, mirror) | 1050290030-1050290099, one per armed cluster |
| Events | 755865600+, capacity 100 (one per actor) |
| Entities | 755920000+ (managed band) |
| NpcParam | 755890003 |

## In-game checks still owed

1. Generation log: one `Halloween jumpscares: layer ...` line per drawn
   layer; each path meets one jumpscare per drawn layer, none elsewhere.
   Play it through a racing-platform session with the mod connected
   (training is enough): offline play would hide a trigger that depends
   on flags the mod touches.
2. The warp lands the untouchable on the player without pushing them into
   a wall or off a ledge; no actor visible at its MSB spot before or after
   (die and reload in an armed cluster after it played). If the overlap
   looks bad, scout a c0000 dummy poly behind the player.
3. Pick the best arrival animation among 5010-5013; tune
   `ARRIVAL_SECONDS`.
4. `scare`: no damage and no madness buildup over the whole sequence. The
   lantern pulses are bullets of TAE judges 100-102 and fire without AI;
   team type 26 is expected to keep them off the player. If not, give the
   actor a behavior variation whose judges 100-102 point at madness-free
   pulse clones (the recipe of the Untouchable boss's `ApplyMoveset`).
5. `strike`: the burst lands at point-blank range at tier-6 damage, then
   the actor fades and disappears.
6. Fade length: whether the full 3000 (5 s) drags; cut `FADE_SECONDS`.
7. Leaving a cluster before the delay expires loses its jumpscare.
8. Leyndell (m11_00 + m60 tile) and Nokron Ancestral Woods (two dungeon
   maps): fires only while the player is in a map hosting an actor, once.
