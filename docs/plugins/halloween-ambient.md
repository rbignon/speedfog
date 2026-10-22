# Halloween Ambient Spawns and Decorations

Two non-text layers of the Halloween plugin, both running after FogMod's
`Write()`: hostile "greeter" enemies and optional decorative ambush packs,
plus data-driven prop decorations, placed at cluster exit gates. A single
driver, `writer/FogModWrapper/HalloweenAmbientPass.cs`, reads each map's
MSB once and calls into both features (decorations from
`writer/FogModWrapper/GateDecorInjector.cs` before spawns from
`writer/FogModWrapper/AmbientSpawnInjector.cs`) rather than each running
its own independent per-map pass, with the shared anchor collection in
`writer/FogModWrapper/HalloweenGateAnchors.cs`. The same driver also
places the jumpscare actors (see
[halloween-jumpscares.md](halloween-jumpscares.md)). See "Driver architecture"
below for the call graph. See [halloween-theme.md](halloween-theme.md) for
the text reskin layer that shares the same `[plugin.halloween]` namespace.

## Configuration

```toml
[plugin.halloween]
enabled = true    # gates greeters + decorations (and the text theme)
greeters = true   # hostile greeter beside each anchored gate: "untouchable" (= true) or "wormface"
ambushes = false  # also spawn decorative packs at the anchored gates: "skeleton" (= true) or "basilisk"
```

`greeters` and `ambushes` take a boolean or a species name: `true` means
the first (default) species, `false` turns the flavor off, a name turns it
on with that species (`ambushes = "basilisk"` alone is enough). The
booleans predate the species and keep every existing config on the
untouchable/skeleton pair. Parsed strictly by
`HalloweenPluginSettings.Parse`: unknown keys, another type, or an unknown
or miscased species name abort the build (same idiom as
`WeatherInjector.Parse`). The two spawn toggles are independent;
`greeters = false` with `ambushes = false` leaves the decorations as the
only ambient layer at the gates (the text theme and the item icons are not
affected by either toggle). With both off, `CollectSpawnSpecsByMap` returns
no map, so the driver reads no MSB on the spawn layer's behalf, and the
regulation rows of a disabled flavor (greeter row, decorative ambusher
rows) are not written. Conversely, a flavor whose `NpcParam` row the
regulation phase could not write (regulation.bin or a paramdef missing) is
not placed: `Program.cs` hands `HalloweenAmbientPass` the settings with
that flavor turned off and logs the skip, the same two-phase guard as the
Untouchable boss (the parts would otherwise point at a missing row). The same table's `darkness` key drives a
separate layer, see [halloween-darkness.md](halloween-darkness.md).

## The two flavors

- **Greeters**: one hostile enemy standing watch beside every anchored
  exit gate, placed when `enabled = true` unless `greeters = false`: an
  Aging Untouchable (`c5280`, default) or a Wormface (`c4570`, see
  "Deathblight species"). Vanilla AI and moveset (the untouchable's
  madness lantern, the wormface's Deathblight), with a 10 m aggro radius,
  on a flat-tier clone of the vanilla row with no runes and no drop (see
  "Greeter stats").
- **Ambushers**: when `ambushes` is on, a pack of 2-3 hostile skeletons
  (`c3500`, default) or basilisks (`c4150`, see "Deathblight species")
  sharing the same gate. They aggro normally but are DECORATIVE:
  a clone of the species' vanilla row (Sage's Cave skeleton 35000030 or
  Ainsel basilisk 41500060) into
  `SpeedFogIds.DecorativeAmbusherNpcRow` with token HP (`AMBUSH_HP`, 100:
  1 HP self-destructed in-game, the skeletons' own collapse/assembly
  mechanics finished them off), no runes, and a custom
  SpEffect (`DecorativeAmbusherSpEffectRow`, cloned from scaling tier-1 row
  7010, attached to the first free `spEffectID` slot) multiplying the five
  attack power rates plus `staminaAttackRate` by `AMBUSH_ATTACK_RATE`
  (0.01). Inherited slots pointing into the game's own scaling bands
  (vanilla 35000030 carries area-scaling row 7080, ~2x) are cleared on the
  clone so the numbers are exact. Scenery that swings, not a threat
  (`AmbientSpawnInjector.ApplyAmbusher`, applied in `ApplyRegulation`
  alongside the greeter row). The parts keep the species' vanilla think
  row (skeleton 35000000, basilisk 41500000): vanilla perception.

One species per flavor and per seed: the cloned rows keep their ids
(`HostileGreeterNpcRow`, `HostileGreeterThinkRow`,
`DecorativeAmbusherNpcRow`), only the clone template and the chr model
follow the species (`AmbientSpawnInjector.GreeterProfile` /
`AmbusherProfile`). The jumpscare actors and the Aging Untouchable boss do
not follow `greeters`: they stay untouchables whatever the species.

Both are placed by `AmbientSpawnInjector`; the gate decoration catalogue
(candelabras, cobwebs, glow anchors) is a separate, independently-gated
feature handled by `GateDecorInjector`.

## Driver architecture

`HalloweenAmbientPass.Inject` is the single call site (from
`ApplyModDirInjectors` in `Program.cs`) for all three features; none of
`GateDecorInjector`, `AmbientSpawnInjector` and
`HalloweenJumpscareInjector` has its own top-level
`Inject` entry point or reads/writes an MSB on its own. Per map, the
driver:

1. Reads the map's MSB once.
2. Calls `GateDecorInjector.ApplyToMsb` (decorations) before
   `AmbientSpawnInjector.ApplyToMsb` (greeters/ambushers): the ground-
   evidence invariant in "Ground estimation" below requires vanilla
   enemies still be in the MSB when decor placement runs, and the spawns
   carry `EntityID = 0` until placed, which would otherwise pass decor's
   own vanilla-enemy filter.
3. Calls `HalloweenJumpscareInjector.ApplyToMsb` (jumpscare actors, see
   [halloween-jumpscares.md](halloween-jumpscares.md)) between the two:
   greeters and ambushers carry `EntityID = 0` and would pass the actors'
   vanilla clone-source filter, while the actors carry managed ids that
   the spawn phase already ignores.
4. Writes the MSB once, only if any phase placed something.
5. Separately calls `GateDecorInjector.WriteSfxEvents` for the decor
   layer's own EMEVD tail (`sfx_id > 0` entries only) and
   `HalloweenJumpscareInjector.WriteEvents` for the actors it placed;
   spawns need no EMEVD at all (see "Why no EMEVD and no scaling").

Because the two features anchor on different cluster-type sets
(`DecorClusterTypes` includes `start`, `SpawnClusterTypes` does not), the
driver unions the two features' per-map anchor/spec dictionaries to decide
which maps to visit, but plans decor's entity/event id allocation
(`GateDecorInjector.PlanAllocations`) only over the maps that actually
have decor anchors, in that dictionary's own enumeration order, so decor
ids stay identical to before this driver existed (a wider plan over the
map union would hand spawn-only maps an unused decor event slot and shift
every later map's allocation).

## Placement rules

Both injectors reuse `GateGeometry` (extracted from `DeathMarkerInjector`,
see [death-markers.md](../death-markers.md)) for gate parsing, side
resolution, and arc math:

- Anchors come from `HalloweenGateAnchors.Collect`, shared by both
  injectors: the EXIT gates of connections whose source cluster type is
  `mini_dungeon`, `legacy_dungeon` or `start` (the source cluster is
  resolved from `conn.ExitArea` through `GraphNode.Zones`). Each consumer
  passes its own type set: decorations use `DecorClusterTypes` (which adds
  `start`, so the run's very first fog gate at the Chapel of Anticipation
  is dressed with props), spawns use `SpawnClusterTypes` (no `start`: no
  mobs at the Chapel). The start cluster's `roundtable` zone is excluded
  for both (nothing in the safe hub). Boss arena interiors never receive
  spawns or decorations, though the fog INTO a boss arena can be dressed,
  since it is an exit of the preceding cluster.
- Anchoring was originally on entrance gates ("greet the player as they
  arrive") and flipped after in-game review: on arrival the entrance gate
  is behind the player and its dressing is never seen, while exit gates
  are hunted for and approached frontally. Death markers already anchor
  exit gates the same way.
- Placement is on the approach side, i.e. the side of the exit area itself
  (`GateGeometry.ResolveIsASide(conn.ExitGate, conn.ExitArea, gateSides)`,
  mapped `isASide ? 180f : 0f`, the same ASide/BSide semantics as death
  markers; see "Position Offsets" in death-markers.md).
- Radii: greeters 4-6m from the gate in a 60-degree arc; ambushers 3-7m in
  an 80-degree arc (both arcs were originally 120/140 degrees and were
  tightened after in-game review: at those radii the wide arcs regularly
  clipped spawns into corridor walls; decorations keep the 120-degree
  default, the shipped catalogue's 1.5-4m radii stay clear of walls).
  Greeters face AWAY
  from the gate, toward the player walking up to it; ambushers keep pack
  scatter rotation.
- One spec group per (map, gate part name) pair: at most one greeter and
  one ambush pack per gate, even if several connections share it.
- Ambush pack size (2 or 3) is drawn from a process-stable string hash of
  the gate part name, not `string.GetHashCode()`, which .NET randomizes per
  process (hash-flooding mitigation) and would make pack sizes differ
  between builds of the same seed.

## Why no EMEVD and no scaling

`HalloweenAmbientPass.Inject` runs in `ApplyModDirInjectors`, strictly
after FogMod's own `Write()` phase. This means:

- **No scaling**: FogMod's `EldenScaling` tier pass has already completed
  when these spawns are added, so they are never touched by it. Each
  flavor carries fixed stats on its own cloned `NpcParam` row instead: the
  greeter fights at `GREETER_TIER` (see "Greeter stats"), ambushers at
  token HP and near-zero attack (see "The two flavors"). Either way,
  difficulty is deliberately flat regardless of DAG tier or dungeon depth.
- **No EMEVD needed**: spawns are ordinary always-on MSB `Enemy` parts,
  `EntityID = 0` (no flag, no scripted behavior), so nothing needs to
  initialize or gate them at runtime (the greeter's vanilla far teleport
  does rely on one, see "Greeter stats"). Gate decorations with `sfx_id > 0`
  are the only piece that touches EMEVD, and only for one unconditional
  `CreateAssetfollowingSFX` per map (see below); geometry-only decorations
  need none either.

## Greeter stats

This section describes the default untouchable greeter; the wormface
shares the mechanism (flat tier, no runes, no drop, capped think clone),
see "Deathblight species" for its rows.

Greeters were passive until 2026-10 (a cloned `NpcThinkParam` row with
every perception radius zeroed); they now fight. Their AI is vanilla (same
logic and battle scripts, 528000), so they aggro, grab, and keep the
lantern's madness pulses (bullets 205280000-002, SpEffect 26000 rider, see
[untouchable-boss.md](../untouchable-boss.md)), which build madness even
while the untouchable stands still.

Only their perception is shorter. The MSB part points at
`SpeedFogIds.HostileGreeterThinkRow` (755890000), a clone of the Aging
Untouchable's think row 52800000 written by
`AmbientSpawnInjector.ApplyGreeterThink`, with every perception radius
capped at `GREETER_AGGRO_DIST` (10 m). A cap, not a set: a radius already
shorter keeps its vanilla value.

| Field | Vanilla 52800000 | Greeter think row |
|-------|------------------|-------------------|
| `eye_dist` (sight cone) | 30 | 10 |
| `ear_dist` (hearing, all around) | 30 | 10 |
| `searchEye_dist` (sight while searching) | 10 | 10 |
| `BattleStartDist` | 10 | 10 |
| `nose_dist` (smell, all around) | 2 | 2 |

With vanilla's 30 m, a greeter noticed the player from far down the
corridor and came to meet them. Greeters stand 4-6 m from their gate, so
10 m still has them react when the player walks up to the exit fog, which
is the point of a greeter. No vanilla untouchable row is shorter
(52800001-004 share 52800000's radii; 52809000 sees from 50 m), hence
the clone. Whether `eye_dist` also bounds the chase once the fight has
started (a greeter dropping a player who backs off past 10 m) is
unverified, see "In-game checks still owed".

Their far teleport is the open question. Vanilla's script picks it (the
5 s teleport-out 3000, then a warp around `TARGET_EVENT`) at 10 m and
more while the gate SpEffect 20011450 is active, and in the back cone at
8 m and more without checking the gate. Both halves of the mechanism live
in a per-entity EMEVD event (2049422200 for the untouchable of
m61_49_42): once the untouchable carries 20011451 and 20011450, it warps
a helper character onto the player, makes that helper the untouchable's
event target, and clears 20011450. Greeters have `EntityID` 0 and no such
event: `TARGET_EVENT` is never designated and nothing clears the gate
(which the c5280 TAE re-applies anyway, see untouchable-boss.md "Engine
facts and pitfalls"). Where the warp lands, and whether a greeter keeps
fading out whenever the player stands 10 m away, is unverified (see "In-game
checks still owed").

Their `NpcParam` is
`SpeedFogIds.HostileGreeterNpcRow` (755890002), a clone of vanilla
52800086 written by `AmbientSpawnInjector.ApplyHostileGreeter`:

| Field | Vanilla 52800086 | Greeter row |
|-------|------------------|-------------|
| Area scaling (`spEffectID*`) | 20007100 (DLC tier 31: hp x13.6, attack x3.8, defense x1.245, status resistance x2.85) | 7000 + 10 * `GREETER_TIER` (tier 6, 7060: hp x2.27, attack x1.76, defense x1.079, status resistance x2.105) |
| `getSoul` | 4449 | 0 |
| `itemLotId_enemy` | 528000000 | -1 |

The swap reuses the ambushers' scaling-slot scan (every slot in the
7000-7200 and 20007000-20007130 bands cleared, the tier row written to the
first free slot). It gives the greeter exactly the stats FogMod would give
an untouchable moved into a tier-`GREETER_TIER` area: FogMod multiplies by
`curve[target] / curve[source]` on top of the native row (see
[enemy-scaling.md](../enemy-scaling.md)). At tier 6 that is ~730 HP (base
`hp` 323). DLC enemies keep their difficulty in their base `AtkParam`
values, so the greeter still hits harder than a base-game enemy of the
same tier. `GREETER_TIER` is the single tuning knob, flat over the whole
run (the Halloween preset runs tiers 3 to 18), valid from 1 to 20: DLC
tiers live in another row band (`20007000 + 10 * (tier - 21)`, see
enemy-scaling.md). No runes and no drop: every anchored exit gate has a
greeter, so a kill must not pay.

Everything else stays vanilla: the base status resistances (`resist_*`),
the teleport gate SpEffects, `behaviorVariationId` 52800, so the boss
moveset's patched `c5280` TAE stays inert for greeters (untouchable-boss.md
"TAE patch"). When the item randomizer runs, the clone also inherits its
`nerflantern` slot (20011471 in slot 31, the resident parry window), as
every vanilla untouchable does; harmless without the wall event.
They are damageable from the first hit: the vanilla immunity wall is
applied by a per-entity EMEVD event (untouchable-boss.md "Vulnerability
mechanism"), and greeters carry `EntityID` 0 with no event.
`UntouchableBossInjector.ApplyToMsb` picks the boss part by entity id only,
so greeters keep their own npc and think rows.

## Deathblight species

The alternative species (2026-10, for the final Halloween race seed) are
the two Deathblight enemies of the base game. Deathblight is a status
bar that kills outright when it fills; both keep it at full vanilla
strength on purpose.

| | Wormface greeter | Basilisk ambusher |
|---|---|---|
| Model | `c4570` | `c4150` |
| NpcParam template | 45700030 (Altus Plateau, m60_40_53, tier 8: 7080) | 41500060 (Ainsel River, m12_01, tier 5: 7050) |
| Think row | 45700100, cloned and capped (every radius already at or under 10 m: eye 10, battle start 10, search 5, ear 0.1, smell 1) | 41500000, vanilla (eye 16 m, battle start 20 m) |
| Deathblight | mist and grab | black cloud |
| Base `hp` | 621: ~1410 HP at `GREETER_TIER` 6 (the untouchable: 323, ~730) | 338, forced to `AMBUSH_HP` 100 |
| Vanilla drop | lot 457000000 (cleared like every greeter) | lot 415000000, empty (and `getSoul` 0 like every ambusher) |

The ambusher nerf leaves the cloud alone: the clone's SpEffect scales
the five attack power rates and `staminaAttackRate`, while a status
buildup is its own value on the attack's SpEffect (`curseAttackPower`
for Deathblight), which no scaling row multiplies (vanilla tier row 7060
carries attack rates and the `regist*ChangeRate` resistances, nothing on
status attack). Every basilisk bullet (4150100-4150102,
4150110-4150111) applies SpEffect 23001: `curseAttackPower` 20 with
`isUseStatusAilmentAtkPowerCorrect` off, like the whole enemy Deathblight
band (23000-23100; the rows with it on, 6800-6805 and 1794, are
player-side). So a basilisk dies to one hit and its blows barely
register, but standing in its cloud still fills the bar. The wormface
keeps its whole vanilla `spEffectID` set (16582, 16583, 16595, 5851
among others); only the area-scaling slot changes.

## MSB clone recipe and visibility groups

Each spawn/decoration starts as a `DeepCopy()` of the nearest vanilla part
of the same kind (nearest `Enemy` for spawns, nearest `Asset` for
decorations), then retargeted: new model, generated name (`MsbHelper.
GeneratePartName`), position/rotation, `EntityID`, and identity fields
cleared as appropriate for the part kind.

"Vanilla" means outside the managed entity band
`[SpeedFogIds.FogModEntityMin, SpeedFogIds.ModEntityMaxExclusive)`
(`SpeedFogIds.IsModManagedEntity`: FogMod's own parts, death markers,
earlier decorations). Vanilla ids sit on both sides of that band: dungeon
maps use 8-digit ids (`30040800`), open-world tiles 10-digit ones (m60
`10xxxxxxxx`, m61 `20xxxxxxxx`). Until 2026-10 the filter was "below
755890000", which dropped every id-bearing open-world part: on the
Halloween race seed `f7280403ab5d` the Darklight Catacombs exit gate
(m61_52_43, three enemies, all with 10-digit ids) got no spawn at all, and
the other open-world greeters were cloned from entity-0 enemies up to
390m away. With the band, open-world decorations now usually clone the
vanilla fog gate asset itself (10-digit id, distance 0), as dungeon
decorations already did (8-digit gate ids); the model is retargeted, so
only the clone's inherited fields change. The band has one known gap:
FogMod's replacement stakes (986021940+) and their player points
(986020970+) sit outside it and read as vanilla, harmless today (see the
`IsModManagedEntity` doc comment).

The one place spawns and decorations diverge is visibility-group handling,
because an enemy chr renders through its own model while a bloodstain-style
asset anchor is only visible through a following SFX:

- **Enemy spawns** (`MsbHelper.CopyVisibilityGroups`): copies the clone
  source's `DrawGroups`/`DisplayGroups` values into fresh, un-aliased
  arrays. An all-zero profile (safe for an invisible SFX anchor) would risk
  making a chr-rendered greeter or ambusher invisible under a restrictive
  `DisplayGroups` mask in dungeon interiors, so the spawn keeps the
  neighbor's own visibility conditions instead.
- **SFX-visible asset clones** (`MsbHelper.DetachVisibilityGroups`, used by
  both `GateDecorInjector` and `DeathMarkerInjector`): zeroes
  `DrawGroups`/`DisplayGroups` instead, the profile every working
  bloodstain marker ships with (see death-markers.md).

Both helpers still replace `Unk1` with a fresh `UnkStruct1` to break
SoulsFormats' `DeepCopy` array-aliasing bug (only `CollisionMask` is
cloned by `UnkStruct1.DeepCopy`; `DrawGroups`/`DisplayGroups` would
otherwise alias the clone source).

`CollisionPartName` is deliberately left inherited from the cloned
neighbor on spawns: the nearest enemy already stands on a valid collision
in the same play space. That only holds when the source HAS a collision:
in dungeon maps the c1000 dummies near catacomb boss fogs often name none,
and a greeter cloned from one never shows up in-game (confirmed on the
four catacomb/cave exit gates of seed `f7280403ab5d` cloned from a
collisionless c1000). The Item Randomizer treats an
enemy's collision as mandatory too (`EnemyRandomizer` throws on a
cross-map transplant with no collision). So spawns follow the map's own
convention (`AmbientSpawnInjector.CloneSources`, computed once per map):
when at least half of the map's vanilla enemies name a collision, only
enemies with one are clone sources. The families separate cleanly (survey
of every map of seed `f7280403ab5d`): dungeon maps sit at 67-100%
collision-bearing enemies, open-world tiles leave nearly every enemy
collisionless (157 of 185 tiles name none, the rest at most ~13%, cellars
and interiors), so an open-world spawn keeps the nearest enemy as its
source instead of a rare collision-bearing one far away.

A collision-bearing source can still stand in another part of the map,
and its collision is then not the one under the spawn. Sources farther
than 40m from their gate (`FAR_CLONE_SOURCE_DISTANCE`) are logged per gate
(`Gate '...': clone source ... is NNm away (dy ...m, collision
...)`). On seed `f7280403ab5d` the outlier is Tombsward Cave (m31_02): no
vanilla enemy stands on the entrance-area collisions, the nearest
collision-bearing one is 96m away and 29m lower.

## Gate decoration catalogue

`data/plugins/halloween_decorations.toml` ships with a starter set scouted
in Smithbox (2026-09): catacombs bone piles (`AEG023_920`/`924`), the
catacombs standing candelabra (`AEG023_862`), and the Volcano Manor
candelabra and floor candles (`AEG270_684`/`686`/`687`), 7 decorations per
gate in total. Entries require visual scouting (asset models and SFX ids
cannot be picked from data alone); only free-standing floor props work,
since placement is a ground ring around the gate.
`HalloweenDecorLoader.Load` returns an empty catalogue when the file is
absent or has no active entries; `HalloweenAmbientPass.Inject` checks
`catalog.IsEmpty` and skips collecting decor anchors and planning decor
allocations entirely, making the whole decor layer a silent no-op while
the spawn layer still runs.

Registered decor models get a SibPath following FogRando's own
`addAssetModel` convention, e.g.
`N:\GR\data\Asset\Environment\geometry\AEG270\AEG270_684\sib\AEG270_684.sib`
(`MsbHelper.EnsureAssetModel`); without it, real geometry models
registered by name only may fail to resolve in-game.

### Ground estimation

Chr parts (greeters, ambushers) need no vertical care: the engine
gravity-snaps characters onto the collision below at spawn. Static assets
render exactly at their MSB Y, so decorations are anchored on a per-gate
ground estimate instead of the gate origin (which is not reliably at floor
level; the bloodstain death markers inherit this and often float or sink).

`GateGeometry.EstimateGroundY` takes the median Y of vanilla parts within
6m horizontal of the gate, clamps the correction to ±2m, and falls back
to the gate Y with fewer than two candidates. Two guards shape which
candidates count and whether the correction applies:

- **Asymmetric vertical window** ([-2.5m, +0.5m] around the gate Y):
  floor evidence well above the gate origin is almost always wall-mounted
  decor. The instructive failure was Shadow Keep gate `AEG099_230_9500`:
  the gate origin sat exactly at floor level, but under an earlier
  symmetric ±2.5m window two wall props at +2.3m outvoted the single
  floor asset and pulled every decoration to mid-gate height. The costs
  are asymmetric too: a floating prop is glaring, a slightly sunken one
  reads as settled. Downward corrections (the floating-bloodstain case)
  stay fully allowed.
- **Consensus**: the correction applies only when the selected candidates
  agree within 0.75m (max - min); mixed-level neighborhoods (stairs,
  ledges) fall back to the gate Y instead of trusting a median between
  levels.

Candidates are vanilla assets (outside the managed entity band, see "MSB
clone recipe" above, and excluding `AEG099_*`: fog gates, warp
doors, glow anchors are gameplay helpers, not floor evidence) plus
vanilla enemies, which are hand-placed on walkable floor and never
wall-mounted. Using enemies is why `GateDecorInjector.ApplyToMsb` MUST run
before `AmbientSpawnInjector.ApplyToMsb` within `HalloweenAmbientPass`'s
per-map ordering (see "Driver architecture"): the greeters/ambushers carry
`EntityID = 0` and would otherwise pass the vanilla filter with the same
unreliable gate Y. A survey over every fog gate and dungeon door in
m30/m31/m32 found ~85% of gates within 0.3m of the neighborhood median
(doors max 0.5m, fog gates up to 1.7m, typically stairs). The
catalogue's `y_offset` applies on top; the shipped entries sink props a
few centimeters so residual error reads as settled rather than floating.
Corrections beyond 0.3m are logged per gate. Each decoration also gets a
deterministic random yaw (`GateGeometry.GenerateYaws`, a separate PRNG
stream from the arc offsets) instead of identity rotation.

Schema (`[[entries]]`, one array element per decoration type):

| Field | Type | Default | Notes |
|-------|------|---------|-------|
| `model` | string | required | Must match `AEGnnn_nnn` |
| `count` | int | required | Decorations per gate, 1-5 |
| `min_radius` | float | required | Meters from the gate, `> 0` |
| `max_radius` | float | required | Meters from the gate, `>= min_radius` |
| `y_offset` | float | 0.0 | Vertical offset |
| `sfx_dummy` | int | 100 | Dummy poly id, only used when `sfx_id > 0` |
| `sfx_id` | int | 0 | `0` = geometry only, no EMEVD event |

Scouting workflow (copied from the TOML file's own header comment):

1. In Smithbox, open a candle-lit vanilla map (catacombs work well) and
   note the AEG model of the prop you want (e.g. a candelabra).
2. Cross-check placement context with:
   ```
   wine tools/game_inspect/publish/win-x64/game_inspect.exe \
     near <msb-file> <x> <y> <z> --radius 10
   ```
3. For glow-only decorations, pick an SFX id with:
   ```
   wine tools/game_inspect/publish/win-x64/game_inspect.exe \
     <game>/sfx/ --search <id>
   ```
   and use `model = "AEG099_090"` (the invisible anchor the death markers
   use) with `sfx_dummy = 100`.
4. Add an `[[entries]]` block, rebuild a seed, and check the exit gates of
   the Chapel of Anticipation or any mini dungeon in game.

Each catalogue entry's arc offsets are seeded independently: the gate's
own `EntityID` is XORed with a fixed decor tag constant
(`DecorSeedTag`) and mixed with the entry's index in the catalogue before
being passed to `GateGeometry.GenerateArcOffsets`. Seeding straight off the
raw gate `EntityID` (as `AmbientSpawnInjector`'s greeters do for the same
arc center) would make two catalogue entries with equal count/radius bands
draw byte-identical positions, and would coincide with the greeter's own
placement sequence at the same gate.

Entries with `sfx_id > 0` get one unconditional
`ChangeAssetEnableState(entity, Enabled)` + `CreateAssetfollowingSFX(entity,
sfx_dummy, sfx_id)` pair per placed decoration, batched into one event per
map (`SpeedFogIds.HalloweenDecorEvents`, base 755865100) registered via
`InitializeEvent` in event 0, no flag wait since decorations are always
present (unlike death markers, which wait on a racing death-count flag).
Entity IDs come from `SpeedFogIds.HalloweenDecorEntityBase` (755910000),
disjoint from FogMod's own range and from death markers (755900000+).

## Known limitations

- **Backportal gates** (numeric entity IDs, e.g. `12012504`): same limitation
  as death markers (see death-markers.md), not found by name or entity ID in
  the MSB. Both injectors log a warning and skip these gates; a smoke run
  of a full 46-connection DAG left 3-4 anchored gates undressed for this
  reason. Cluster exits can be backportals too: Taylew's Ruined Forge
  (`rauhbase_forge`) leaves through `42032840` in m42_03 (tagged
  `backportal` in fog.txt), so that exit gets neither spawns nor
  decorations.

## In-game checks still owed

Deferred to manual verification (see the task brief); tuning knobs are the
constants at the top of `AmbientSpawnInjector.cs` (radii, arc spreads,
pack size range):

- Greeter presence and behavior at anchored exit gates: greeters face the
  approaching player, not the gate, then aggro and fight with the vanilla
  ambient moveset (grab, lantern madness, no beam, no flame nova), and die
  without dropping runes or items; the Chapel of Anticipation exit shows
  decorations but NO greeter or ambushers (decor-only start cluster).
- Greeter aggro radius (`GREETER_AGGRO_DIST`, 10 m): a greeter should stay
  put while the player is still down the corridor and react when they
  walk up to the exit fog. Then back off past 10 m mid-fight: does it keep
  chasing, or lose the player after a few seconds and walk back to its
  spot? Tune `GREETER_AGGRO_DIST` if the reaction comes too late to matter
  or still from too far. This is the same experiment as the far teleport
  below: both start when the player backs off past 10 m, so the greeter
  may lose the player (vanilla `SightTargetForgetTime` 3 s) before or
  after it picks the 3000 fade-out. Record which happens.
- Greeter far teleport (see "Greeter stats", no event target): back off
  past 10 m and watch the 3000 fade-out. Does the greeter reappear in
  place, near the player, or somewhere else, and does it fade out again
  every time the player stands that far? A repeated 5 s fade-out makes it
  harmless at range; a warp into a wall or out of the play space is the
  bad case.
- Boss retries: most exit gates are boss fogs, so after a death the player
  often respawns next to a re-spawned greeter 4-6 m from the fog. Check
  whether its grab or madness gets in the way of re-entering the fog.
- Greeter difficulty at `GREETER_TIER` 6: fair at the first dungeon exits
  of the Halloween preset (tier 3) without being trivial at the last ones
  (tier 18), and madness buildup survivable for an early low-Focus
  character. Tune `GREETER_TIER` first.
- Greeters at catacomb/cave exits now cloned from a collision-bearing
  enemy instead of a collisionless c1000 dummy (Murkwater, Spiritcaller
  Cave, Fog Rift, Wyndham were empty on seed `f7280403ab5d`), and at
  open-world exits (Darklight Catacombs was empty): the replacement source
  can stand farther away, on another collision of the same map. Check the
  gates the generation log flags as far clone sources first, starting with
  Tombsward Cave (m31_02, collision source 96m away, 29m lower).
- Per-map performance with 1-4 extra chr loads per gate: no visible
  hitch on map load.
- Deathblight species (`greeters = "wormface"`, `ambushes = "basilisk"`):
  the basilisk cloud reaches the player and fills the Deathblight bar
  (offline, its bullets keep their full 23001 buildup), and a basilisk pack is beatable (it dies to one hit) before the
  bar fills; the wormface fits beside a narrow corridor gate (taller than
  an untouchable) and its ~1410 HP at tier 6 is not a wall for an early
  character (tune `GREETER_TIER` first, it moves both species); the
  boss-retry case above is worse here, a Deathblight kill on the way back
  to the boss fog. Tune or nerf from this run.
- Ambush pack feel when `ambushes` is on: packs aggro and swing but die
  to any real hit and deal negligible damage (decorative clone); confirm
  a hit from them barely registers, and that at 100 HP they no longer
  self-destruct (they did at 1 HP) while still folding to one player hit.
- Gate decorations (starter catalogue, first seed): the Volcano Manor
  candles (`AEG270_684`/`686`/`687`) burn outside m16, or ship unlit
  geometry (their vanilla flame may come from map lighting rather than the
  model; if unlit, pair them with a candle-flame `sfx_id`); the `AEG270`
  models load at all outside m16, where vanilla never places them (the
  `AEG023` set is pan-catacombs, no doubt there); decorations sit on the
  ground (per-gate median estimate) without cluttering the doorway at 7
  props per gate.
