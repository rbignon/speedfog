# Halloween Jumpscares

Part of the Halloween plugin (`[plugin.halloween]`, see
[halloween-theme.md](halloween-theme.md)). In a few dungeon layers of the
run, an Aging Untouchable (`c5280`) appears right on the player a random
delay after they enter the cluster, then fades away. Generation-side only
(regulation + MSB + EMEVD): the racing mod knows nothing about it.

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
- has at least one map that is not an open-world tile (`m60_*`, `m61_*`).

Every cluster of a drawn layer is armed. The DAG only links consecutive
layers and layers are type-homogeneous, so every path crosses each drawn
layer exactly once: every run meets exactly `jumpscares` armed clusters.
With fewer eligible layers, all are armed and a warning is logged.

Open-world clusters are left out because a tile streams in and out as the
player moves: Castle Morne, Redmane Castle, Castle Sol, Caria Manor,
Shaded Castle, Castle Ensis and Ruins of Rauh never qualify, so a layer
holding one is never drawn. The Leyndell clusters (m11_00 plus one m60
tile) qualify through m11_00.

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
   cluster (a flag of an outgoing connection is set).
3. Wait for an entry flag of the cluster, then `jumpscare_delay`.
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
| Events | 755865600+, capacity 100 (one per actor) |
| Entities | 755920000+ (managed band) |
| NpcParam | 755890003 |

## In-game checks still owed

1. Generation log: one `Halloween jumpscares: layer ...` line per drawn
   layer; each path meets one jumpscare per drawn layer, none elsewhere.
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
