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
namespaces, so this value colliding with `SpeedFogIds.PassiveGreeterThinkRow`
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
   above into `regulation.bin`, returning `true` when both rows were
   written. `IsBossPlaced` checks whether any `enemy_assignments` value
   equals `SpeedFogIds.UntouchableSourceEntity` (`2049420200`) as a decimal
   string; if the boss was not placed anywhere this run, the phase (and the
   whole feature) is skipped and neither param row is added. `Program.cs`
   stores the boolean result on `Context.UntouchableBossParamsApplied` for
   the MSB phase to read.
2. **MSB phase** (`ApplyModDirInjectors`, runs post-Write): `Inject` runs
   unconditionally except for one guard: when the boss was placed
   (`IsBossPlaced`) but the regulation phase warn-returned (`NpcParam` or
   `SpEffectParam` unavailable, so `Context.UntouchableBossParamsApplied` is
   `false`), the call is skipped with a one-line warning instead, because the
   boss `NpcParam` row the repoint would point at was never written. When it
   does run, `Inject` collects every `enemy_assignments` key (an arena entity id)
   whose value is the source entity, then scans every `.msb.dcx` in the mod
   directory in parallel. For each map, `ApplyToMsb` repoints the
   `NPCParamID` of every placed enemy part whose `EntityID` is one of those
   arena ids to the boss clone row, but only if the part's `ModelName` is
   `c5280`; a model mismatch is logged as a warning and the part is left
   alone (defensive: nothing else should ever share an untouchable's arena
   entity id, but the injector never repoints the wrong model). `ThinkParamID`
   is repointed to the boss think row (`755890001`) only when the moveset
   rows were written (see "Moveset" below); otherwise it stays vanilla
   `52800000`. If
   assignment targets are still unfound after this mod-dir scan, `Inject`
   runs a merge-dir fallback over a named list of arena maps FogMod never
   writes; see "Implemented fix" below for the full mechanics.

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

Implemented fix (user-approved option a, 2026-09-01): the repoint scan
in `Inject` gained a named fallback source, data-driven from
`data/game_tweaks.toml`'s `[[fallback_arena_maps]]` (parsed by
`GameTweaksLoader`, mirroring `[[pin_vanilla_maps]]`, exposed as
`GameTweaks.FallbackArenaMaps`). After the primary `mods/fogmod` scan, if
any assignment target is still unfound and a merge dir is available
(`Program.cs` passes `ctx.Config.MergeDir` and
`ctx.Tweaks.FallbackArenaMaps` into `Inject`), it walks that list
(currently just `m60_13_09_02`), skipping any name already present in
`mods/fogmod` (already scanned by the primary loop). For each remaining
name it reads the merge-dir copy (`<mergeDir>/map/mapstudio/<name>.msb.dcx`,
missing being logged and skipped), runs the same `ApplyToMsb` repoint,
and, only when something was actually repointed there, writes the result
into `mods/fogmod` (the higher-priority ModEngine layer) and folds the
repointed ids into the found set. The extra map therefore ships only on
seeds that actually place the boss in it; any id still unfound after the
fallback keeps the existing phase-slot warning. Extend
`[[fallback_arena_maps]]` in `data/game_tweaks.toml` if the "assignment
target not found" warning ever fires for another arena whose map exists
in the merge-dir.

The discarded generic alternative (option b) was to add the tile to
`[[pin_vanilla_maps]]` sourced from the merge-dir copy: it would also
serve hypothetical future injectors, but ships the map on every seed
regardless of whether the boss landed there, which was not worth the
unconditional cost for a single-arena case.

## Moveset

Spec: `docs/superpowers/specs/2026-09-04-untouchable-moveset-design.md`.
The boss gets two tools vanilla AI never uses, applied to the promoted
instance only:

- **Lantern swing** at melee range: animation 3001 (AtkParam_Npc 5280115,
  magic 100, hit radius 4 at dummy 906, no throw), vanilla's post-teleport
  surprise attack, promoted to a regular act (Act11) next to the grab
  (3002/3003, AtkParam 5280110, throwTypeId 4100). Pure AI: no param, no
  TAE change.
- **Frenzy beam** at range: animation 3004 (lantern raised, thirteen
  bullet events from dummy 210), registered by vanilla AI with probability
  0 everywhere, re-enabled (Act04) and made to fire a Frenzied Burst-style
  laser (magic damage, no madness).

### Why 3004 needs a TAE edit

3004's bullet events carry judge ids 101/102, the same ids the idle, walk
and teleport animations fire for the lantern's ambient pulses. Remapping
them under the boss variation would fire the beam at rest, so
`StaticModBuilder/UntouchableTaePatcher` rewrites the judge of four of the
thirteen events (spread by start time: indices 0, 4, 8, 12) to 150 in the
shipped `chr/c5280.anibnd.dcx`. The patch is inert for ambient
untouchables twice over: vanilla AI never selects 3004, and variation
52800 has no row for judge 150. The patcher refuses (warning, nothing
written) any layout other than dummy 210 with judges 101/102, so a game
patch renumbering c5280's judges disables the moveset instead of
corrupting the TAE. Knobs: `BEAM_EVENT_COUNT` (4) and `BEAM_DUMMY` (null,
keep 210; candidates 10 or 906 if the lantern does not aim at the player).
The patched anibnd (about 1.3 MB) ships in every seed's static mod whether
or not the boss is placed; it is inert without the boss rows.

### Per-seed rows (UntouchableBossInjector.ApplyMoveset)

| Param | Row | Source | Changes |
|-------|-----|--------|---------|
| NpcParam | 755890000 (existing clone) | 52800086 | `behaviorVariationId` 75589 |
| NpcThinkParam | 755890001 | 52800000 | `battleGoalID` 755890 (`logicId` stays 528000) |
| BehaviorParam | 275589100/101/102/110/111/112/113/115/500 | the nine vanilla rows of variation 52800 (judge 500 is the non-formula row 1170) | `variationId` 75589 |
| BehaviorParam | 275589150 | 252800101 | judge 150, refType 1, refId 755890000 |
| Bullet | 755890000 | 10732000 (Frenzied Burst) | `atkId_Bullet` 755890000, `spEffectId0-4` -1 |
| AtkParam_Npc | 755890000 | 5280115 | `atkMag` 110 (`BEAM_MAGIC`) |

Row ids: `200000000 + variation * 1000 + judge` (`SpeedFogIds.BehaviorRowId`).
The Frenzied Burst SFX (527032 laser, 527033 hit) live in
`sfxbnd_commoneffects`, so no SFX bundle work.

A single 3004 can land up to BEAM_EVENT_COUNT (4) beams, so the per-cast
ceiling is 4 x BEAM_MAGIC (440 magic before the player's defenses); tune the
two knobs together.

### AI script

`data/mods-src/speedfog/script/755890_battle-luabnd-dcx/755890_battle.lua`
(plain text, repacked at bootstrap into
`data/mods/speedfog/script/755890_battle.luabnd.dcx`) is the decompiled
vanilla `528000_battle` renamed to goal 755890, plus Act11 (swing) and a
working Act04 (beam, `successDist` 999, 8 s cooldown via `SetCoolTime`). The
`GOAL_Houzuki755890_Battle` and `GOAL_Houzuki755890_AfterAttackAct` globals
are not provided by the shared aiCommon global-name list (which only knows
the vanilla `GOAL_Houzuki528000_*` names), so the script assigns them itself
at the top (755890 = the boss think row's `battleGoalID`, 755891 = any
unused id).
Probability table (vanilla -> boss), knobs at the top of `Goal.Activate`:

| Situation | Vanilla | Boss |
|-----------|---------|------|
| player behind, >= 8 m | Act02 100 | unchanged |
| >= 10 m, teleport ready (SpEffect 20011450) | Act02 99 / Act01 1 | Act02 60 / Act04 40 |
| >= 10 m, teleport not ready | Act01 100 | Act01 50 / Act04 50 |
| 3 to 10 m | Act03 100 | Act03 60 / Act11 40 |
| < 3 m | Act03 100 | Act03 50 / Act11 50 |

During the 12 s grab cooldown vanilla had no act left at 3-10 m (the
passivity observed in earlier sessions); the swing fills that gap.
`battleGoalID` selects the battle luabnd independently of `logicId` (255
vanilla think rows share the generic 29999), and the logic script does not
reference the battle goal by name, so only the battle script is cloned.

### Gating

All-or-nothing. `ApplyParams` returns `(Core, Moveset)`: the core rows as
before, the moveset only if `<data-dir>/mods/speedfog/chr/c5280.anibnd.dcx`
and `<data-dir>/mods/speedfog/script/755890_battle.luabnd.dcx` exist and
NpcThinkParam, BehaviorParam, Bullet and AtkParam_Npc are available, with
every template row present. The `behaviorVariationId` change belongs to
the moveset group: alone, it would leave the boss with a variation that
has no BehaviorParam rows, i.e. no attacks. A think row pointing at a
missing luabnd would leave the boss without battle AI, which is why a
missing static asset drops the whole moveset (one warning line) rather
than just the beam.

### In-game validation sequence

1. **Goal resolution**: knobs temporarily at `SWING_MID = 100`,
   `SWING_CLOSE = 100`, both `BEAM_*` at 0. The boss must swing the
   lantern at melee range instead of always grabbing. The explicit
   `GOAL_Houzuki755890_Battle`/`GOAL_Houzuki755890_AfterAttackAct`
   assignments are already in the script (goal tables are keyed by the
   numeric id, the battle goal is started with the raw `battleGoalID`, and
   the `GOAL_` globals of vanilla scripts come from the shared aiCommon
   global-name list, decompiled from the 1.17 aicommon bundle), so this
   step confirms the mechanism rather than introducing it. If the boss
   still idles or the game logs a script error, the remaining fallback is
   the spec's approach 2 (shared decompiled 528000 script branching on
   `ai:HasSpecialEffectId(TARGET_SELF, 755890000)`).
2. **Beam**: knobs at their defaults. Laser from the lantern at >= 10 m,
   Frenzied Burst visual, aimed at the player, ~110 magic per hit; no beam
   during idle or walk. Wrong origin or direction: `BEAM_DUMMY`.
3. **Tuning**: probabilities, `BEAM_COOLDOWN`, `BEAM_MAGIC`,
   `BEAM_EVENT_COUNT`, the 3002 cooldown.
4. **Ambient regression**: an ambient untouchable still only teleports and
   grabs, no swing at range, no beam, no script error.

## Expected log lines

```
Untouchable boss: NpcParam 755890000 (clone of 52800086, hp 3000, runes 20000) + SpEffect 755890000 (cut 0.35)
Untouchable boss: moveset rows (think 755890001 -> battle 755890, variation 75589 with 9 vanilla judges + beam judge 150, bullet 755890000 (clone of 10732000), atk 755890000 magic 110)
Untouchable boss: repointing N placed boss slot(s)
  <part> (entity <id>): NPCParamID -> 755890000, ThinkParamID -> 755890001
  Fallback: repointed N part(s) in <name> (merge-dir copy shipped into the mod dir)
  Repointed M untouchable boss part(s)
```

with `M >= 1` whenever the boss was actually placed in at least one
compatible (`c5280`-model) arena. The `Fallback:` line only appears when
the merge-dir fallback described above actually repointed something in
one of `data/game_tweaks.toml`'s `[[fallback_arena_maps]]`. Phase-slot
warnings (see above) are expected and not failures.

When the moveset is skipped, the second line is replaced by one
`Untouchable boss: moveset skipped (<reason>)` line (static asset missing,
param unavailable, vanilla judge or template row missing) and the repoint
lines carry only `NPCParamID`. At bootstrap, StaticModBuilder prints
`Untouchable TAE patch: retargeted 4 bullet event(s) of animation 3004 to judge 150 in chr/c5280.anibnd.dcx`.

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
- Fight passivity: addressed by the moveset (see "Moveset"); run its
  validation sequence instead.
- Successful-parry reward and teleport behavior specifically in the arenas
  that actually received the boss (navmesh clearance for the AI's warp
  scan around the player).

`BOSS_HP` scaling note: the MSB repoint (`ApplyToMsb`) replaces the Item
Randomizer's own scaled placement clone outright; it does not layer on top
of it (see the caelid_radahn note above for what happens when the repoint
does not reach an arena: the randomizer's generic tier scaling is what
survives instead). So `BOSS_HP` (3000) is an ABSOLUTE HP value in every
repointed arena, with no tier/area scaling multiplier applied afterward.
Tune it as a flat number for the fight you want, not as a base that some
external multiplier will adjust later.

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

## Size: settled (no resize possible)

The boss keeps its vanilla size, by engine constraint, not by choice. An
MSB `Part.Scale` experiment (1.3x and higher on the promoted part,
in-game test 2026-09-01) confirmed the engine ignores the field for chr
parts, matching vanilla usage (`game_inspect scan-scale` over all 1347
maps: zero Enemy or DummyEnemy hits; SoulsFormats documents the field as
map-piece/object-only). No other offline mechanism exists: NpcParam and
SpEffectParam carry no size field, EMEVD has no resize instruction, and
the decompiled enemy randomizer never rescales. Runtime tools scale chr
through live process memory only, which is outside SpeedFog's
offline-patching paradigm. Any future resize would likely need a full
chr clone with a rescaled skeleton (chrbnd + anibnd + behbnd),
disproportionate for a cosmetic. Do not revisit without new evidence.

## Fallback

If the custom SpEffect misbehaves in game (parry feels broken, the
permanent state does something unexpected), fall back to a plain
`20011471` clone with no cut-rate override: fully touchable, like a
nerflantern-patched vanilla untouchable, compensated with higher `BOSS_HP`
instead of a damage cut. This only requires dropping the `DAMAGE_CUT`
field loop in `UntouchableBossInjector.Apply` and raising `BOSS_HP`; the
two-phase injector structure and the out-of-band row placement are
unaffected.
