# Halloween Ambient Spawns and Decorations

Two non-text layers of the Halloween plugin, both running after FogMod's
`Write()`: passive "greeter" enemies (plus optional hostile ambush packs)
and data-driven prop decorations, placed at dungeon entrance gates.
Implemented in `writer/FogModWrapper/AmbientSpawnInjector.cs` and
`writer/FogModWrapper/GateDecorInjector.cs`. See
[halloween-theme.md](halloween-theme.md) for the text reskin layer that
shares the same `[plugin.halloween]` namespace.

## Configuration

```toml
[plugin.halloween]
enabled = true    # gates greeters + decorations (and the text theme)
ambushes = false  # also spawn hostile skeleton packs at dungeon entrances
```

Parsed strictly by `HalloweenPluginSettings.Parse`: unknown keys or a
non-boolean `ambushes` abort the build (same idiom as `WeatherInjector.Parse`).

## The two flavors

- **Greeters**: one passive Aging Untouchable (`c5280`, `NPCParamID
  52800086`) at every qualifying entrance gate, always placed when
  `enabled = true`.
- **Ambushers**: when `ambushes = true`, a pack of 2-3 hostile skeletons
  (`c3500`, `NPCParamID 35000030`, Sage's Cave low-tier variant) sharing
  the same entrance.

Both are placed by `AmbientSpawnInjector`; the gate decoration catalogue
(candelabras, cobwebs, glow anchors) is a separate, independently-gated
feature handled by `GateDecorInjector`.

## Placement rules

Both injectors reuse `GateGeometry` (extracted from `DeathMarkerInjector`,
see [death-markers.md](../death-markers.md)) for gate parsing, side
resolution, and arc math:

- Only entrance gates of connections whose destination cluster type is
  `mini_dungeon` or `legacy_dungeon` qualify (resolved via `eventMap` +
  `GraphData.Nodes`). Boss arenas never receive spawns or decorations.
- Spawns/decorations are anchored on the ENTRANCE gate (inside the
  destination zone), not the exit gate that death markers use: they greet
  the player as they arrive, not before they leave.
- Placement is on the interior side, i.e. the side of the entrance area
  itself (`GateGeometry.ResolveIsASide(conn.EntranceGate, conn.EntranceArea,
  gateSides)`, mapped `isASide ? 180f : 0f`, the same ASide/BSide semantics
  as death markers; see "Position Offsets" in death-markers.md).
- Radii: greeters 4-6m from the gate in a 120-degree arc; ambushers 3-7m in
  a 140-degree arc. Greeters face the gate (the arriving player); ambushers
  keep pack scatter rotation.
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
  vanilla `NpcParam` rows for the Aging Untouchable and the Sage's Cave
  skeleton are used as-is, giving a deliberately flat, low difficulty
  regardless of DAG tier or dungeon depth.
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
Ambushers keep the vanilla `ThinkParamID 35000000` and aggro normally.

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

`data/plugins/halloween_decorations.toml` ships empty (tracked, with a
documented header) because entries require visual scouting: asset models
and SFX ids cannot be picked from data alone. `HalloweenDecorLoader.Load`
returns an empty catalogue when the file is absent or has no active
entries, making `GateDecorInjector.Inject` a silent one-line no-op.

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
4. Add an `[[entries]]` block, rebuild a seed, and check the first entrance
   gates of any mini dungeon in game.

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
  the MSB. Both injectors log a warning and skip these entrances; a smoke
  run of a full 46-entrance DAG left 3-4 entrances without a greeter for
  this reason.

## In-game checks still owed

Deferred to manual verification (see the task brief); tuning knobs are the
constants at the top of `AmbientSpawnInjector.cs` (radii, arc spreads,
pack size range):

- Greeter presence and passivity at dungeon entrances: no aggro, and no
  madness (or other status) buildup when walking past.
- Per-map performance with 1-4 extra chr loads per entrance: no visible
  hitch on map load.
- Ambush pack difficulty feel when `ambushes = true`: packs should aggro
  normally and read as a deliberate, low-stakes hazard rather than a
  spike.
- Gate decorations: no content to verify yet (catalogue ships empty).
