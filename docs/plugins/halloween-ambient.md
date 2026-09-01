# Halloween Ambient Spawns and Decorations

Two non-text layers of the Halloween plugin, both running after FogMod's
`Write()`: passive "greeter" enemies (plus optional hostile ambush packs)
and data-driven prop decorations, placed at cluster exit gates.
Implemented in `writer/FogModWrapper/AmbientSpawnInjector.cs` and
`writer/FogModWrapper/GateDecorInjector.cs`, with the shared anchor
collection in `writer/FogModWrapper/HalloweenGateAnchors.cs`. See
[halloween-theme.md](halloween-theme.md) for the text reskin layer that
shares the same `[plugin.halloween]` namespace.

## Configuration

```toml
[plugin.halloween]
enabled = true    # gates greeters + decorations (and the text theme)
ambushes = false  # also spawn hostile skeleton packs at the anchored gates
```

Parsed strictly by `HalloweenPluginSettings.Parse`: unknown keys or a
non-boolean `ambushes` abort the build (same idiom as `WeatherInjector.Parse`).

## The two flavors

- **Greeters**: one passive Aging Untouchable (`c5280`, `NPCParamID
  52800086`) standing watch beside every anchored exit gate, always placed
  when `enabled = true`.
- **Ambushers**: when `ambushes = true`, a pack of 2-3 hostile skeletons
  (`c3500`) sharing the same gate. They aggro normally but are DECORATIVE:
  a clone of the Sage's Cave skeleton row (35000030) into
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
  alongside the passive think row; the untouchable-boss rows are appended
  first to keep NpcParam/SpEffectParam row ids ascending).

Both are placed by `AmbientSpawnInjector`; the gate decoration catalogue
(candelabras, cobwebs, glow anchors) is a separate, independently-gated
feature handled by `GateDecorInjector`.

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

Both injectors run in `ApplyModDirInjectors`, strictly after FogMod's own
`Write()` phase. This means:

- **No scaling**: FogMod's `EldenScaling` tier pass has already completed
  when these spawns are added, so they are never touched by it. The
  greeter uses the Aging Untouchable's vanilla `NpcParam` row as-is;
  ambushers use SpeedFog's decorative clone (token HP, near-zero attack, see
  "The two flavors"). Either way, difficulty is deliberately flat and
  negligible regardless of DAG tier or dungeon depth.
- **No EMEVD needed**: spawns are ordinary always-on MSB `Enemy` parts,
  `EntityID = 0` (no flag, no scripted behavior), so nothing needs to
  initialize or gate them at runtime. Gate decorations with `sfx_id > 0`
  are the only piece that touches EMEVD, and only for one unconditional
  `CreateAssetfollowingSFX` per map (see below); geometry-only decorations
  need none either.

## Passivity mechanism

The greeter's passivity comes from a cloned `NpcThinkParam` row, not from
disabling AI: `AmbientSpawnInjector.ApplyPassiveThinkRow` clones the Aging
Untouchable's vanilla think row (`52800000`) into
`SpeedFogIds.PassiveGreeterThinkRow` (755890000, `GameEditor.AddRow`) with
every perception field zeroed:

| Field | Storage type | Value |
|-------|--------------|-------|
| `ear_dist` | f32 | 0 |
| `eye_dist` | u16 | 0 |
| `nose_dist` | u16 | 0 |
| `searchEye_dist` | u16 | 0 |
| `BattleStartDist` | u16 | 0 |

With every detection radius at zero, the greeter can never perceive the
player and never enters battle state; every other AI/combat parameter
(including the madness aura, if the vanilla NPC carries one) is untouched.
Ambushers keep the vanilla `ThinkParamID 35000000` and aggro normally;
their harmlessness comes from the decorative `NpcParam` clone instead
(see "The two flavors").

## MSB clone recipe and visibility groups

Each spawn/decoration starts as a `DeepCopy()` of the nearest vanilla part
of the same kind (nearest `Enemy` for spawns, nearest `Asset` for
decorations, both excluding parts already at or above FogMod's own entity
floor 755890000), then retargeted: new model, generated name (`MsbHelper.
GeneratePartName`), position/rotation, `EntityID`, and identity fields
cleared as appropriate for the part kind.

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
in the same play space.

## Gate decoration catalogue

`data/plugins/halloween_decorations.toml` ships with a starter set scouted
in Smithbox (2026-09): catacombs bone piles (`AEG023_920`/`924`), the
catacombs standing candelabra (`AEG023_862`), and the Volcano Manor
candelabra and floor candles (`AEG270_684`/`686`/`687`), 7 decorations per
gate in total. Entries require visual scouting (asset models and SFX ids
cannot be picked from data alone); only free-standing floor props work,
since placement is a ground ring around the gate.
`HalloweenDecorLoader.Load` returns an empty catalogue when the file is
absent or has no active entries, making `GateDecorInjector.Inject` a
silent one-line no-op.

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

Candidates are vanilla assets (excluding `AEG099_*`: fog gates, warp
doors, glow anchors are gameplay helpers, not floor evidence) plus
vanilla enemies, which are hand-placed on walkable floor and never
wall-mounted. Using enemies is why `GateDecorInjector` MUST run before
`AmbientSpawnInjector` in `Program.cs`: the greeters/ambushers carry
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
  reason.

## In-game checks still owed

Deferred to manual verification (see the task brief); tuning knobs are the
constants at the top of `AmbientSpawnInjector.cs` (radii, arc spreads,
pack size range):

- Greeter presence and passivity at anchored exit gates: no aggro, and no
  madness (or other status) buildup when walking past; greeters face the
  approaching player, not the gate; the Chapel of Anticipation exit shows
  decorations but NO greeter or ambushers (decor-only start cluster).
- Per-map performance with 1-4 extra chr loads per gate: no visible
  hitch on map load.
- Ambush pack feel when `ambushes = true`: packs aggro and swing but die
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
