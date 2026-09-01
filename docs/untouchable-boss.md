# Aging Untouchable minor boss

Promotes the Aging Untouchable (chr `c5280`, source entity `2049420200`,
vanilla part `c5280_9000` in `m61_49_42_00.msb.dcx`) to an allowlist-only
minor boss. Reachable only through:

```toml
[enemy]
randomize_bosses = "all"   # or "minor"
bosses = ["Aging Untouchable"]
```

## What it is

`data/boss_arena_tags.json` entry `"2049420200"` (name "Aging
Untouchable") carries `pool: "minor"`, `boss.exclude_from_pool: true`,
`boss.size: 2`, `dlc: true`, no `arena` block, following the promoted-skeleton
pattern (see `docs/boss-arena-constraints.md`, "Promoted allowlist-only
sources"):

- `exclude_from_pool: true` keeps it out of the ordinary
  `randomize_bosses = "minor"`/`"all"` pool: `_compose_pool` drops
  `exclude_from_pool` entries at every branch. The boss is completely inert
  on the standard path, no config change required to avoid it.
- `resolve_boss_allowlist` never reads `exclude_from_pool` or `dlc`, so the
  `enemy.bosses` allowlist above is authoritative and reaches it anyway.
- `boss.size: 2` is the only "open arena" lever the tag model has (no
  dedicated open-arena flag); it excludes only size-1 arenas from the
  compatibility match, leaving the boss's AI-driven teleport (navmesh scan
  around the player, then `GOAL_COMMON_ToTargetWarp`) room to work.

Once placed, `UntouchableBossInjector` (`writer/FogModWrapper/UntouchableBossInjector.cs`)
promotes the arena's enemy-randomizer-placed part into the actual boss.

## The vulnerability mechanism

Vanilla `NpcParam` row `52800086` (the Aging Untouchable's base stats,
HP 323, `getSoul` 4449) carries the parry-to-damage wall in `spEffectID18`:
SpEffect `20011473`, `stateInfo` 420. That `stateInfo` value is the
hardcoded "untouchable" mechanic: nothing short of a successful parry can
damage the character while it is set, regardless of any damage-cut field on
that row.

The Item Randomizer's always-on `nerflantern` option lifts this by
permanently adding SpEffect `20011471` (`stateInfo` 121) to every c5280
`NpcParam` row's first free slot. `stateInfo` 121 is a different permanent
state that does not carry the untouchable flag, so damage lands normally;
every damage-cut field on `20011471` sits at its default 1.0 (no cut).
`UntouchableBossInjector.Apply` clones `20011471` into a new `SpEffectParam`
row (`755890000`) that inherits `stateInfo` 121 (the wall-lifting mechanism)
and overrides the eight damage-cut fields:

```
slashDamageCutRate, blowDamageCutRate, thrustDamageCutRate,
neutralDamageCutRate, magicDamageCutRate, fireDamageCutRate,
thunderDamageCutRate, darkDamageCutRate
```

all set to `UntouchableBossInjector.DAMAGE_CUT` (`0.35f`, i.e. the boss
takes 35% of incoming damage: a 65% cut). The vanilla parry window is
untouched: the wall-lift SpEffect only ever governs the permanent state
outside of a successful parry, so a parry still opens the normal
full-damage window exactly as with `nerflantern`.

The clone (`NpcParam` row `755890000`, `UntouchableBossInjector.UNTOUCHABLE_VANILLA_NPC`
= clone of `52800086`) sets:

- `hp` = `BOSS_HP` = 3000
- `getSoul` = `BOSS_RUNES` = 20000
- `spEffectID19` = `755890000` (the custom SpEffect row). Slot 19 is the
  first free slot on `52800086`: slot 17 (`20011450`) permanently gates the
  AI's long-range teleport loop and slot 18 (`20011473`) is the parry wall
  being superseded; both stay as-is on the clone.

The clone deliberately lives outside the `5280xxxx` band. The Item
Randomizer's `nerflantern` option is globally on in SpeedFog and patches
every `NpcParam` row in that band unconditionally (adding `20011471` to
whatever free slot it finds); if the boss reused a `5280`-band row id,
nerflantern's own pass would collide with (or follow) this injector's edit
and the boss's vulnerability would no longer be fully under SpeedFog's
control. Row `755890000` sits far outside that band, so nerflantern never
touches it. `NpcParam`, `SpEffectParam` and `NpcThinkParam` are separate row
namespaces, so this value colliding with `UntouchableBossInjector.PassiveGreeterThinkRow`
(also `755890000`, a `NpcThinkParam` row from the Halloween ambient-spawn
feature) is not a conflict.

## Two-phase injector

`UntouchableBossInjector` runs in two phases from `Program.cs`, both
unconditional on the Halloween plugin (this feature is independent of
`[plugin.halloween]`; it only depends on the enemy allowlist actually
placing the boss):

1. **Regulation phase** (`ApplyRegulation`, gated by
   `UntouchableBossInjector.IsBossPlaced(ctx.GraphData.EnemyAssignments)`):
   `ApplyParams` clones the `NpcParam` and `SpEffectParam` rows described
   above into `regulation.bin`. `IsBossPlaced` checks whether any
   `enemy_assignments` value equals `SpeedFogIds.UntouchableSourceEntity`
   (`2049420200`) as a decimal string; if the boss was not placed anywhere
   this run, the phase (and the whole feature) is skipped and neither param
   row is added.
2. **MSB phase** (`ApplyModDirInjectors`, runs post-Write, unconditional):
   `Inject` collects every `enemy_assignments` key (an arena entity id)
   whose value is the source entity, then scans every `.msb.dcx` in the mod
   directory in parallel. For each map, `ApplyToMsb` repoints the
   `NPCParamID` of every placed enemy part whose `EntityID` is one of those
   arena ids to the boss clone row, but only if the part's `ModelName` is
   `c5280`; a model mismatch is logged as a warning and the part is left
   alone (defensive: nothing else should ever share an untouchable's arena
   entity id, but the injector never repoints the wrong model). `ThinkParamID`
   is left at its vanilla value (`52800000`); AI tuning is a separate,
   documented follow-up (see below), not part of this injector.

`ApplyToMsb` returns `(Repointed, Ids)`: the repointed count and the exact
set of entity ids it touched. `Inject` uses that tuple directly for its
found/missing bookkeeping instead of re-scanning the MSB by `NPCParamID`
after the fact. An earlier revision did that scan-by-`NPCParamID` and, in
the same fix round, also skipped printing warnings for a map when nothing
was repointed there; both were corrected together (commit f05a7d9). Every
collected log line, including "not c5280" warnings, is printed for every
map, even when that map's `repointed` count is zero; only the found-id
bookkeeping is gated on `repointed > 0`. After the scan, any
`enemy_assignments` arena id that never matched a placed MSB part is
reported as:

```
Warning: assignment target <id> not found in any map (phase slot?)
```

This is expected, not necessarily a bug: `enemy_assignments` expands one
slot per phase entity for multi-phase bosses (see
`docs/boss-arena-constraints.md`), and a phase-expanded slot may have no
MSB part of its own if the arena's boss is single-phase.

A second, verified cause also produces this warning: an assignment
target whose arena map FogMod never writes. Observed on smoke seed
391735550: Starscourge Radahn's arena is a private instance map
(`m60_13_09_02`, per `enemy.txt`'s `Map:` field) with no fog gate of its
own (the `caelid_radahn` gates live on the surrounding tiles), so it is
absent from `mods/fogmod`. The map still ships and loads in game: the
Item Randomizer writes the boss swap there and the seed registers
`mods/itemrando` (lowest priority, 510 maps) in ModEngine, so the swap
itself is live. What is lost is SpeedFog's post-processing: every
FogModWrapper injector, including this one's repoint scan, only patches
`mods/fogmod`. An Untouchable assigned to that arena therefore keeps the
Item Randomizer's own scaled placement clone (observed on seed
391735550: `npc=52800140`, a 5280-band row, so nerflantern makes it
damageable, with the randomizer's generic tier scaling and runes)
instead of SpeedFog's tuned boss profile (3000 HP, 65% damage cut,
20000 runes): a functional fight, just off-design. Note this is why the
gap was never observed before the boss feature: gameplay never depended
on FogMod writing this supertile (swaps ship via the `mods/itemrando`
layer, and scaling of randomized bosses travels in the randomizer's
NpcParam clones, not in FogMod EMEVD events); the repoint is the first
SpeedFog injector that needs to edit an arena-map MSB. Other fogless
arenas (Stone Platform m19_00, Farum m13_00 for Placidusax) are
unaffected because their boss parts live in maps FogMod writes anyway
for the area's gates and portals; caelid_radahn is unique in having its
boss on an 02-supertile that carries nothing FogMod touches (the zone's
gates are on the 00-tiles, and the stake fix lives on the OTHER
supertile m60_12_09_02, which FogMod does write for that reason).

Candidate fixes: (a) targeted, preferred: teach the repoint scan a
fallback source: when an assignment target is absent from every
`mods/fogmod` map, read the map from the merge-dir copy, repoint there,
and write the result into `mods/fogmod` (the higher-priority layer), so
the extra map ships only when the boss is actually placed in it;
(b) generic: add the tile to `[[pin_vanilla_maps]]` sourced from the
merge-dir copy, which also serves hypothetical future injectors but
ships the map on every seed (and keeps the 1.17 invasion-content check
on that file's contents relevant either way).

## Expected log lines

```
Untouchable boss: NpcParam 755890000 (clone of 52800086, hp 3000, runes 20000) + SpEffect 755890000 (cut 0.35)
Untouchable boss: repointing N placed boss slot(s)
  Repointed M untouchable boss part(s)
```

with `M >= 1` whenever the boss was actually placed in at least one
compatible (`c5280`-model) arena. Phase-slot warnings (see above) are
expected and not failures.

## In-game tuning session (owed)

Spec: `docs/superpowers/specs/2026-08-03-halloween-theme-design.md`
section 2.3. Not automatable; requires playing the fight. Owed checks:

- `BOSS_HP` (currently 3000) and `BOSS_RUNES` (currently 20000): feel of
  the fight length and reward relative to other minor bosses.
- `DAMAGE_CUT` (currently 0.35, i.e. a 65% cut): whether the boss is
  appropriately tanky without becoming a DPS check; grab damage on the
  clone (unchanged from vanilla `52800086`) should be reviewed too, since
  near-one-shot grab damage is fine for an ambiance mob but not for a
  boss encounter.
- Fight passivity in the arenas that received the boss (`boss.size: 2`
  arenas only): the moveset (from the decompiled `528000_battle.lua`) is
  2100 (close-range punish when stuck to its back), 3000/3001
  (long-range teleport initiator + post-teleport surprise attack), 3002 (a
  mid-range signature move on a fixed 12s cooldown), 3003 (follow-up combo
  on an SpEffect 5030 interrupt), and 3004 (registered but probability 0 in
  every branch of the logic script: dormant).
- Successful-parry reward and teleport behavior specifically in the arenas
  that actually received the boss (navmesh clearance for the AI's warp
  scan around the player).
- `BOSS_SCALE` (currently 1.3): an EXPERIMENT setting MSB `Part.Scale` on
  the promoted part so the boss reads bigger than the ambiance
  untouchables. Whether the ER engine honors the field for chr is unknown:
  no vanilla map scales any chr part (`game_inspect scan-scale` over all
  1347 maps: zero Enemy or DummyEnemy hits), SoulsFormats documents the field as
  map-piece/object-only, and no other offline mechanism exists (no
  NpcParam/SpEffect size field, no EMEVD instruction; runtime tools scale
  chr through live memory only). Outcomes: normal size in-game means the
  engine ignores it (revert to 1.0), visually scaled means judging whether
  hitboxes/animations track at this modest factor.

If in-game testing shows the fight too passive, the fix is an AI overlay,
not a change to this injector:

1. Probe the TAE first (SoulsFormatsNEXT, same approach as
   `StaticModBuilder/GraceAnimationPatcher.cs`) to determine whether
   animation 3004 exists at all before spending effort re-enabling it in
   the logic script.
2. Copy `Game/script/528000_battle.luabnd.dcx`, unpack with WitchyBND
   (`tools/witchybnd/WitchyBND.exe -p`), decompile with
   `DOTNET_ROLL_FORWARD=LatestMajor dotnet <DSLuaDecompiler.dll> <lua> -o <out>`
   (runs natively, no Wine needed for this step).
3. Ship edits via
   `data/mods-src/speedfog/script/528000_battle-luabnd-dcx/`, repacked by
   WitchyBND at bootstrap, following the Rykard precedent
   (`471000_battle`).
4. Knobs to try: the 3002 cooldown, the per-distance attack probability
   tables, and (only if step 1 shows it exists) re-enabling 3004, trialed
   during the same in-game session before deciding to keep it.

## Fallback

If the custom SpEffect misbehaves in game (parry feels broken, the
permanent state does something unexpected), fall back to a plain
`20011471` clone with no cut-rate override: fully touchable, like a
nerflantern-patched vanilla untouchable, compensated with higher `BOSS_HP`
instead of a damage cut. This only requires dropping the `DAMAGE_CUT`
field loop in `UntouchableBossInjector.Apply` and raising `BOSS_HP`; the
two-phase injector structure and the out-of-band row placement are
unaffected.
