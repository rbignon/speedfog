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

The wall is not a `NpcParam` field and not `stateInfo` 420, as this
section claimed until the 2026-09-05 sessions. Decoded from the vanilla
per-untouchable EMEVD event (a `Restart` event initialized with the
untouchable's entity, copied next to every placed boss by the enemy
randomizer; on 1.17 it is `1700783` in an arena map, together with three
sibling events):

1. At spawn: `SetSpEffect(entity, 20011470)` (category 1001, every damage
   cut rate 0: full immunity, this is the wall), `SetSpEffect(entity,
   19690)`, and `SetCharacterHPBarDisplay(entity, disabled)`.
2. Wait for `IfCharacterHasSpEffect(entity, 20011471)`: the parried
   animation (8500) applies 20011471 through a TAE event; same category
   1001, so it overrides the wall during the parry window (the riposte
   lands).
3. Then, once and for all: `ClearSpEffect(entity, 20011470)`, `Create NPC
   Part` (a part with an HP bar), `SetCharacterHPBarDisplay(entity,
   enabled)`, `SetSpEffect(entity, 20011472)` (a one-second break VFX).

So "one parry and the wall is gone for good" is the vanilla design. The
Item Randomizer's always-on `nerflantern` option defeats it by writing
20011471 into a free `NpcParam` slot of every 5280-band row (slot 31 on
1.17): resident in the same category, it keeps the event's wall from
ever taking hold, and the parry detector of step 2 is permanently true.
Slot 18 (`20011473`, `stateInfo` 420, category 156) is unrelated to
damage and stays as vanilla.

SpeedFog's boss keeps the vanilla flow with a **partial wall** before the
break and a **broken state** after it (x4 damage ratio between the two,
2026-09-05 session request: 0.5 before, 2.0 after):

- `UntouchableBossInjector.Apply` clones 20011471 into `SpEffectParam`
  row 755890000 (category 1001 kept) with the eight damage-cut fields
  (`slashDamageCutRate` ... `darkDamageCutRate`) set to
  `UntouchableBossInjector.DAMAGE_CUT` (`0.5f`, the boss takes 50%; 0.35
  until the 2026-09-04 session found the boss too tanky). The row is not
  resident on the boss `NpcParam` row: a resident copy could not be
  cleared by step 3.
- `PatchWallEvents` (MSB phase, per arena map) rewrites the events the
  randomizer copied for each placed boss (all four take the entity as
  their parameter): 20011470 becomes 755890000 in the wall event's
  `SetSpEffect` and `ClearSpEffect`, and the first
  `SetCharacterHPBarDisplay(disabled)` of every copied event becomes
  enabled, the wall event's spawn-time one and the teleport sibling's
  mid-warp one alike (the boss takes damage from the start, so its bar
  shows from the start and must not vanish at each teleport), and a
  `SetSpEffect` of the broken row 755890002 is inserted right after the
  wall event's `SetSpEffect(entity, 20011472)` (the break VFX), with the
  same entity parameter. Five instructions per boss on 1.17. Step 2
  overrides the partial wall during the parry, step 3 clears it and
  applies the broken state: the first parry is the break.
- The broken row (`SpeedFogIds.UntouchableBrokenSpEffectRow`, 755890002)
  is a permanent, VFX-less clone of 20011472 (category 0, so it coexists
  with the parry-window effect of later parries) whose eight cut rates
  are `UntouchableBossInjector.BROKEN_DAMAGE_TAKEN` (`2f`: twice the
  vanilla damage). Knob; the ratio against the partial wall is
  `BROKEN_DAMAGE_TAKEN / DAMAGE_CUT`.
- `Apply` scrubs nerflantern's 20011471 from every slot of the boss
  clone; ambient untouchables keep it (they must stay damageable).

The clone (`NpcParam` row `755890000`, `UntouchableBossInjector.UNTOUCHABLE_VANILLA_NPC`
= clone of `52800086`) sets:

- `hp` = `BOSS_HP` = 2000 (3000 until the 2026-09-04 session; see the
  scaling note below: this is the HP at the arena's vanilla tier)
- `getSoul` = `BOSS_RUNES` = 20000
- `toughness` = `BOSS_TOUGHNESS` = 0: across the 1.17 regulation the field
  takes three values only (0 on 5512 rows, 20 on 41, 35 on 1492), so it is
  a hit-reaction class rather than a meter: 35 is the humanoid class that
  flinches on ordinary hits (vanilla 52800086, the regular Inquisitor
  c5311), 0 the boss class (Jori c5312, Godrick, every boss checked) that
  does not. With 35 the boss was interrupted by every hit even at super
  armor 120 (2026-09-05, fifth run).
- `superArmorDurability` = `BOSS_SUPER_ARMOR` = 80 and
  `superArmorRecoverCorrection` = `BOSS_SUPER_ARMOR_RECOVER` = 0.23 (3/13,
  Jori's exact value): the stagger meter, Jori's profile (vanilla 65 / 0).
- every `spEffectIDn` equal to 20011471 = -1 (nerflantern's slot); slot
  17 (`20011450`, vanilla's one-shot teleport gate, ignored by the boss
  script, see "Teleport cooldown") and slot 18 (`20011473`) stay.

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

A boss whose map carries no copied wall event (never observed; the
randomizer copies the four events with every placement) would have no
wall and no cut at all, with a warning and its bar flips discarded. The
merge-dir fallback arena has no EMEVD in the mod dir: its merge-dir copy
(which carries the randomizer's events) is shipped into the mod dir and
patched there, like its MSB.

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
instead of SpeedFog's tuned boss profile (2000 HP, 50% damage cut,
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
  magic 100, hit radius 4 at dummy 906, dmgLevel 4 with 1.5 m knockback, no
  throw), vanilla's post-teleport surprise attack, promoted to a regular
  act (Act11) next to the grab (3002/3003, AtkParam 5280110, throwTypeId
  4100). In game it reads as a burst around the lantern that pushes the
  player back; it is kept rare so the parryable grab stays the main
  threat. Pure AI: no param, no TAE change.
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
corrupting the TAE. Knob: `BEAM_EVENT_COUNT` (4). The events keep dummy
210 (the lantern); if it ever fails to aim at the player, candidates are
10 (the 3002/3003 flash origin) or 906 (the swing).
The patched anibnd (about 1.3 MB) ships in every seed's static mod whether
or not the boss is placed; it is inert without the boss rows.

### Per-seed rows (UntouchableBossInjector.ApplyMoveset)

| Param | Row | Source | Changes |
|-------|-----|--------|---------|
| NpcParam | 755890000 (existing clone) | 52800086 | `behaviorVariationId` 75589 |
| NpcThinkParam | 755890001 | 52800000 | `battleGoalID` 755890 (`logicId` stays 528000) |
| BehaviorParam | 275589100/101/102/110/111/112/113/115/500 | the nine vanilla rows of variation 52800 (judge 500 is the non-formula row 1170) | `variationId` 75589; judges 100-102 re-pointed at the pulse clones below |
| Bullet | 755890003-005 | 205280000-002 (the lantern's ambient pulses, judges 100-102) | madness rider (SpEffect 26000, stateInfo 437) cleared, VFX rider kept; the reason madness built up while the boss stood still |
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
vanilla `528000_battle` renamed to goal 755890, plus the SpeedFog acts,
gates and reactions below. The
`GOAL_Houzuki755890_Battle` and `GOAL_Houzuki755890_AfterAttackAct` globals
are not provided by the shared aiCommon global-name list (which only knows
the vanilla `GOAL_Houzuki528000_*` names), so the script assigns them itself
at the top (755890 = the boss think row's `battleGoalID`, 755891 = any
unused id). Every knob is a file-scope local at the top of the script,
captured as an upvalue by the functions below it: a first for this
engine's scripts (the vanilla scripts only use globals), so a nil
arithmetic error on the first activation would point there. The 2026-09-04
spec and plan (`docs/superpowers/specs/2026-09-04-untouchable-moveset-design.md`,
`docs/superpowers/plans/2026-09-04-untouchable-moveset.md`) predate this
rework: their cooldown weight 1, "unchanged" behind bracket and "unchanged"
interrupts are superseded by what follows.

Acts: Act11 (swing 3001, vanilla's post-teleport surprise attack as a
regular melee act), Act04 (beam 3004, `successDist` 999, at range and at
mid range), Act02 (two teleports by distance, `TELEPORT_FAR_RANGE` 5 m,
centre to centre, melee reach with a long weapon being 3-4 m:
near, Jori's structure from vanilla 531020, wind-up animation then
`GOAL_COMMON_ToTargetWarp` then a follow-up, built from the untouchable's
own moves, the lantern burst 3001 as the wind-up, its hit landing from the
first frame and the warp firing at its cancel window, 1.0 s, then a warp
`TELEPORT_AWAY_DIST` 8 m away from the player relative to the boss itself
(`TARGET_SELF`, straight behind the boss or, when the player stands in its
back, straight ahead: the five-argument `TARGET_SELF` warp of 203100,
Rennala's 7 m blink, and 301010's retreats), then the beam
when it is ready, `TELEPORT_BEAM`: the boss bursts, retreats and fires;
far, vanilla's own act, the 5 s teleport-out animation 3000 whose marker
at 4.77 s triggers vanilla's interrupt, the warp behind the player and the
burst when its timer allows; the interrupt scans and warps around
`TARGET_ENE_0` rather than vanilla's `TARGET_EVENT`: with `TARGET_EVENT`
only the fight's first far teleport landed behind the player and every
later one reappeared where the boss stood (eighth run, 2026-09-06).
`TARGET_EVENT` is an event-designated target (EMEVD "Set Character Event
Target"; other vanilla scripts warp around it repeatedly, 462000, 536000,
531000, and 532000 guards it against sitting far from the player), so it
is not a once-valid slot; what designates it for this boss, and why it
resolved to the player once, is not established. `TARGET_ENE_0` with the
same directions landed behind the player every time (sixth run). Note
that Act46 parks the boss at 4 m, inside the near band, so a teleport
picked after a strafe is the near one and its burst mostly whiffs at that
range: a retreat and a beam. The near variant stays in the player's view
on purpose: lock-on cannot be broken from the AI (no SpEffect field does
it, and the three marker SpEffects of 3000 carry nothing of the kind), so
a warp behind the player only turned the camera; whatever the 5 s vanilla
teleport does to the lock is vanilla's. A bare warp with no animation
read as a glitch (sixth run), and a warp "8 m in front of the player"
with the plain directions and the enemy target landed the boss where it
stood (seventh run, 2026-09-06): vanilla's target-relative warps use the
`To*` directions (301010's blinks around the player, `AI_DIR_TYPE_ToB`
and kin), the plain ones with the enemy target read differently from one
script to the next, and the self-relative form is the unambiguous
retreat), Act05/Act06 (the
vanilla post-grab retreats to 10/8 m, followed by a beam when it is
available). Cooldowns: `GRAB_COOLDOWN` 6 s (vanilla 12) and
`BEAM_COOLDOWN` 6 s through `SetCoolTime`, both registered with the engine
by `Houzuki755890_RegisterIntervals`; `SWING_COOLDOWN` 8 s on 3001
whatever its source (Act11, reaction, teleport wind-up) and
`TELEPORT_COOLDOWN` 6 s on AI timers (`TIMER_SWING` slot 11,
`TIMER_TELEPORT` slot 10, `ai:SetTimer` when the act is queued, room or
not, so a spot without room is not retried at every decision; the far
variant sets it `TELEPORT_FAR_WINDUP` (5.5 s) higher so the reaction hold
covers the animation, the warp and the burst, about 8.8 s in all); 3001 is
never registered, so no engine interval can hold the boss on a burst, and
the teleport's burst fires whatever the swing timer says. The timer idiom
is vanilla's (slot 10 is set by 34 vanilla battle scripts, `GetTimer(n) <=
0` gates are the standard pattern, 1.17 survey). Two room scans: the
near variant's (`Houzuki755890_FindRoomAway`: from the boss itself,
straight away from the player then the two diagonals, at the retreat
distance, then again at
`TELEPORT_AWAY_FALLBACK` 5 m for small arenas, as Jori's own retreats
fall back), run when the act is queued,
about 1 s before the warp, and without room nothing is queued or cleared
(the burst is not spent on a warp that cannot happen; the act clears the
sub-goals only once the retreat is certain, vanilla Act05/Act06's idiom);
the far variant's (`Houzuki755890_FindRoomBehind`: in front, then behind
right/left at 0 or 2 m, then behind at 2 m), vanilla's own scan in
vanilla's own interrupt. The line width is the boss's own hit radius (the
body that has to fit; the untouchable's vanilla scan uses it too) and the
warp is the five-argument `ToTargetWarp` (the extra arguments of Jori's
and 301010's calls are unverified).
The
wind-up burst uses Jori's wrapper (`ComboTunable_SuccessAngle180`, reach
999, no turn, every angle 180) so it fires whatever the player's side;
`ComboAttackTunableSpin` would demand the player inside 90 degrees in
front, which the "player behind" brackets and the Shoot reaction cannot
promise.

Cooldown weight: the last argument of `SetCoolTime` is the weight kept
while the attack cools, not 0. Vanilla passes 1 in about two thirds of
its calls and 0 in a quarter (1.17 survey), so a table never sums to zero,
and a cooling attack picked with weight 1 is held by the
engine until its interval expires, up to the act's goal life (8 s for the
grab and the swing): the boss freezes in place. That was vanilla's
passivity at 3-10 m (grab alone, weight 1 during its 12 s) and, with the
earlier fillers at 25, about one decision in thirteen of the boss
(2026-09-05 session). The script now passes 0, and every bracket keeps a
movement filler with a positive weight.

Teleport cooldown: `Houzuki755890_TeleportReady` is the teleport timer
back at 0, nothing else. The script reads neither the 3000 counter (the
far variant plays 3000 but its clock is the timer) nor vanilla's SpEffect 20011450 (vanilla's far
bracket gates the teleport on it): with that check the boss teleported once per
fight (2026-09-05, third run: teleport or beam at the start, then never
again, approach or beam from range, grab/swing/move at melee range).
Facts about 20011450: resident in NpcParam slot 17 (category 0, endurance
-1); no EMEVD of the checked arena (m31_10), common or common_func names
it; no SpEffect chain replaces it; the c5280 TAE applies it through a
one-frame type-66 event (0.00-0.03 s) at the start of the idle (0), of
1020, 2300 and the 5010-5013 arrivals, while 3000 applies 20011453 (4 s,
"teleporting") at its first frame, then 20011451 and the 20011452 warp
marker at 4.73/4.77 s. Hypothesis, not established: the gate is consumed
at the first teleport and the one-frame events do not durably re-arm it
(20011471, also endurance -1, is applied by a 0.33 s type-66 event on
8500 and covers only the parry window, which suggests type-66 effects end
with their event). Unverified: the type-66 semantics, and which arrival
animation the AI's `ToTargetWarp` plays. Either way the boss no longer
depends on the gate; ambient untouchables keep the vanilla script and its
behaviour. Attack counters, confirmed by the third run: `GetAttackPassedTime`
reads 0 for an animation never registered with `RegistAttackTimeInterval`
until its first use, and a registered counter reads large before the
attack's first use (the grab fires from the start of every fight).
`SetCoolTime` registers as a side effect, but the decisions run before it,
so `Houzuki755890_RegisterIntervals` registers the two counters (3002,
3004) at the top of `Goal.Activate`; the swing and the teleport run on AI
timers. Vanilla evidence (survey
of the 396 battle scripts of 1.17): 84 reads of an unregistered counter,
all but six of them "long ago" checks (`>= N`) that 0 leaves silently
false, and vanilla 468000/631000 test `GetAttackPassedTime(3009) == 0` on
an unregistered 3009 to zero an act, i.e. "not used yet". History of the
two failed versions: the first gated
the teleport on the swing (the teleport ends in the interrupt's swing, and
a swing held by an engine interval would have left the boss standing
behind the player), and the hit reaction consumes the swing as soon as it
is ready while the player keeps hitting, so the gate stayed closed at
melee range; the second read an unregistered 3000 counter (and an
unregistered 3001, taken out of `SetCoolTime` to avoid the hold), so the
teleport was dead everywhere, the boss turned in place, and every reaction
was held off since `Houzuki755890_SequenceInFlight` read the same 3000
counter (0 is always within `REACT_HOLD`). The engine hold is now
impossible on 3001: it runs on `TIMER_SWING` and is never registered, so
the burst may open a teleport whatever the timer says; only Act11, the
hit reaction's burst branch and the dormant marker interrupt wait for the
timer.

Probability table (vanilla -> boss); the vanilla act of the bracket keeps
the remainder, the teleport weight counts only while the teleport is ready:

| Situation | Vanilla | Boss |
|-----------|---------|------|
| player behind, >= 8 m | Act02 100 | Act02 100 if the teleport is ready, else Act01 100 |
| player behind, < 8 m | Act01 20 / Act43 80 | Act02 50 / Act01 10 / Act43 40 if the teleport is ready, else vanilla |
| >= 10 m, teleport ready | Act02 99 / Act01 1 | Act02 60 / Act04 40 |
| >= 10 m, teleport not ready | Act01 100 | Act01 50 / Act04 50 |
| 3 to 10 m | Act03 100 | Act03 50 / Act11 10 / Act02 15 / Act04 10 / Act46 15 |
| < 3 m | Act03 100 | Act03 55 / Act11 15 / Act02 10 / Act42 20 |
| post-grab retreat (SpEffect 5031/5032) | Act05/Act06 | same, then Act04 if the beam is ready |

Act46 closes to 4 m and strafes; Act42 is a sidestep. The movement acts are
what keep the swing rare during the grab cooldown: the first boss version
had only the swing, which then fired every single time (2026-09-04
session).

Reactions (`Goal.Interrupt`, after the vanilla teleport and grab
follow-ups; vanilla 472000 pattern: `ClearSubGoal`, queue the attack,
return true). A reaction spends an attack that is available anyway, so it
moves an attack earlier without adding any: hits landed while the swing
cools stay free, and the grab, beam and post-teleport recoveries are
untouched. No reaction fires while a teleport or a grab sequence is in
flight (`Houzuki755890_SequenceInFlight`: the teleport timer above
`TELEPORT_COOLDOWN` minus `TELEPORT_HOLD`, i.e. the first 3.5 s of the
near variant and the far variant's whole 5 s animation plus its warp and
burst thanks to its higher timer, vanilla's 4 s "teleporting" marker
20011453 as a second signal for the far one, clamped so a hold longer than
the cooldown cannot silence the reactions for good; or less than
`REACT_HOLD`, 7 s, since 3002 started; per the TAE event spans the burst
reaches its cancel window at 1.0 s, the arrivals last 1.2 s (5010/5011) to
2.2 s (5012/5013), and 3002 + 3003 about 6 s): a reaction's `ClearSubGoal`
would otherwise drop the warp, the beam or the 3003 throw. Accepted exposure,
shared with vanilla 472000: a hit from a spirit ash outside those windows
can trigger the hit reaction like a player's hit.

| Interrupt | Conditions | Response | Knob |
|-----------|-----------|----------|------|
| `INTERUPT_Damaged` (the boss took damage) | player in front within `REACT_HIT_RANGE` (2 m), draw | the near teleport (burst, retreat, beam) if ready and there is room, else the burst if ready, else nothing (the current act keeps running) | `REACT_HIT` 25 |
| `INTERUPT_Shoot` (the player starts a cast or a shot) | player at `REACT_RANGE` (5 m) or more, beam ready, draw | beam (the 5 s far teleport is no answer to a cast, the near one would land the boss where it stands) | `REACT_SHOOT` 50 |
| `INTERUPT_UseItem` | player at `REACT_RANGE` or more, beam ready, draw | beam | `REACT_HEAL` 80 |

`tests/test_mods_src_lua_scripts.py` parses the script with luaparser and
rejects the syntax Lua added after 5.0 (the engine's compiler);
`tests/test_untouchable_ai_table.py` runs `Goal.Activate`, `Goal.Interrupt`
and the retreat act in an embedded Lua (lupa) against a fake ai/goal and
checks the weight table and the reactions per state (distance, behind,
SpEffects, seconds since each attack, draw): it would have caught the swing
gate. The in-game sequence below remains the real test.
`battleGoalID` selects the battle luabnd independently of `logicId` (255
vanilla think rows share the generic 29999), and the logic script does not
reference the battle goal by name, so only the battle script is cloned.

### Parry break

The first successful parry cancels the damage cut for the rest of the
fight (2026-09-05 session request). This is the vanilla untouchable flow
with a partial wall, see "The vulnerability mechanism": the copied wall
event applies the cut row at spawn, the parried animation's 20011471
overrides it during the parry (the riposte is full damage), and the event
then clears it and shows the break VFX. Nothing of SpeedFog's runs at
parry time; only the copied event's two SpEffect ids and its spawn-time
HP bar flags are rewritten and one `SetSpEffect` of the broken row is
inserted after the break VFX (`PatchWallEvents`, one warning per boss
whose map has no such event).

History of the 2026-09-05 sessions, kept because each step is a trap for
the next reader: (1) a SpeedFog-side detector event in common.emevd
never fired (entity-scoped conditions from common do not see map
entities); (2) moved into the arena map's EMEVD it fired in a loop from
game start, because the boss clone inherited nerflantern's resident
20011471; (3) with that slot scrubbed the boss became fully immune: the
"wall lift" credited to stateInfo 121 in the old documentation never
existed, the copied vanilla event was applying the real wall 20011470
and only nerflantern's resident 20011471 had kept it from taking hold.
The counter SpEffect (x2 cut rates) and the detector event were removed
once the vanilla flow was understood.

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

Run in full on 2026-09-05 (seeds 610166042, 1141387, 871254120): swing
and beam behave, no madness while idle, half damage behind the partial
wall, the first parry breaks it and every later hit lands at four times
the pre-parry number. Re-run after a game patch or a knob change. Steps
6 to 8 (cooldown weight 0, offensive teleport, mid-range and retreat beams,
reactions) were run twice on 2026-09-05: improvements but no teleport at
melee range (the swing gate), then no teleport at all and turning in place
(the unregistered counters), then teleport or beam at the start of the
fight and never again (the 20011450 check, see "Teleport cooldown"), then
teleports at melee range too (fourth run), with the 5 s delay of 3000 and
a boss that flinched on every hit, then (fifth run) a bare warp with no
animation, good pacing with the lower cooldowns, and still a boss
interrupted by every hit with super armor 120 (the toughness class, see
the clone), then (sixth run) the burst wind-up reads well but the warp
behind the player is defeated by lock-on (the player turns at once) and
the first, far teleport had lost its 5 s animation, then (seventh run) a
warp "8 m in front of the player" with the plain directions left the boss
where it stood, then (eighth run, 2026-09-06) the far variant played at
melee reach (threshold 4 m) and every far teleport after the first
reappeared at range (`TARGET_EVENT`); the self-relative retreat under 5 m,
the `TARGET_ENE_0` far warp and the toughness change await their own run
(steps 7 to 9).

1. **Goal resolution**: knobs temporarily at `SWING_MID = 100`,
   `SWING_CLOSE = 100`, the three `BEAM_*` and the two `TELEPORT_*` at 0
   (a teleport ends in a swing too, through the interrupt). The boss must swing the
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
   during idle or walk. Wrong origin or direction: retarget the dummy
   (see "Why 3004 needs a TAE edit").
3. **Tuning**: probabilities (`SWING_*`, `MOVE_*`), `GRAB_COOLDOWN`,
   `SWING_COOLDOWN`, `BEAM_COOLDOWN`, `BEAM_MAGIC`, `BEAM_EVENT_COUNT`.
   Second session (2026-09-05): swing still too frequent, `SWING_COOLDOWN`
   12 s added (10 s since the reactions pass, 8 s since the instant-warp
   pass that also lowered the grab and the beam to 6 s; it throttled the
   teleport until the same day's rework, see "Teleport cooldown"); madness rose
   while the boss idled, pulse clones without
   26000; parry break added. First session
   (2026-09-04): beam approved as is; the swing at 40/50 fired every time
   (see the AI script note), lowered to 10/15 with movement fillers and
   an 8 s grab cooldown; boss too tanky at 8427 HP on a depth-12 arena
   (3000 base x 2.81 FogMod rescale, then a 65% cut), lowered to
   `BOSS_HP` 2000 and `DAMAGE_CUT` 0.5.
4. **Parry break**: the HP bar shows from the start and hits land at half
   damage; after the first parry (riposte at full damage, break VFX) every
   hit lands at four times the pre-parry number (x2 vanilla); after dying
   and re-entering, the partial wall is back until the next parry (the
   copied event restarts).
5. **Ambient regression**: an ambient untouchable still only teleports and
   grabs, no swing at range, no beam, no script error, and still builds
   madness with its lantern. Count its teleports: the vanilla script gates
   them on 20011450 alone, so an ambient that teleports once and then runs
   at the player from 10 m or more supports the "consumed gate" reading of
   "Teleport cooldown", one that teleports again points at the 3000
   counter instead.
6. **No freeze**: at melee range, take a grab and a swing within a few
   seconds, then stay close: the boss must keep sidestepping or strafing
   through the cooldowns, never stand still for several seconds (the
   cooldown weight 0). Counters: the third run (2026-09-05) confirmed
   the registered counters (teleport at the start, grabs, swings and
   beams from the start), and that a script checking 20011450 teleports
   once per fight. The fourth run showed teleports at melee range (the
   gate was the limit); the teleport now runs on an AI timer and no
   longer plays 3000, so the 3000 counter is out of the picture.
7. **Teleports and beams**: under 5 m (melee range while the grab and the
   swing cool, the player in its back), the boss bursts its lantern on the
   spot (the hit lands if the player is close), vanishes about 1 s into
   the burst with no 5 s fade, reappears 8 m away from the player (5 m in
   a small arena; straight ahead when the player was in its back) with
   its arrival animation and fires the beam when it is ready; from 5 m,
   vanilla's teleport: the 5 s lantern fade, then the warp behind the
   player and the burst, every time and not only the first (eighth run:
   the later ones reappeared at range with vanilla's `TARGET_EVENT`).
   Never two teleports within
   `TELEPORT_COOLDOWN` (11 s after a far one). At 3-10 m it sometimes fires
   the beam; after a grab it retreats and fires the beam from the retreat
   distance. A cast from 5 m or more draws the beam when it is ready. If the boss finishes the whole 1.8 s burst before vanishing,
   the attack goal did not hand over at the cancel window and the wind-up
   needs another animation.
8. **Reactions and windows**: from 5 m or more, drinking a flask draws a
   beam most of the time and casting a spell draws the beam about half the
   time when it is ready; within 5 m neither
   reaction fires. At melee range,
   hitting the boss draws, about one hit in four, the near teleport
   (burst, retreat, beam) when it is ready or a burst at most once per
   `SWING_COOLDOWN`, so combos after a whiffed grab still land freely.
   In a small arena (catacomb rooms), confirm the boss still retreats at
   least sometimes: the retreat needs 8 m of navmesh straight behind the
   boss (or behind-left/right), then falls back to 5 m
   (`TELEPORT_AWAY_FALLBACK`); a boss that never retreats there means both
   distances fail and the fallback needs lowering. A boss that vanishes
   and reappears where it stood would contradict 203100's shipped blink
   (the same call shape and scan at 7 m), so look at the timing first (the
   burst's cancel window, the `ClearSubGoal` order) before trying
   301010's eight-argument call.
9. **Hit reactions**: light attacks no longer interrupt the boss's grab,
   burst, beam or teleport wind-up (`toughness` 0, the boss class); heavy
   hits and combos still break its super armor meter (80, Jori's) into a
   stagger. A boss still interrupted by every hit means the class reading
   of `toughness` is wrong and the next lever is per-animation, in the
   shared TAE.

## Expected log lines

```
Untouchable boss: NpcParam 755890000 (clone of 52800086, hp 2000, runes 20000, toughness 0, super armor 80/0.2307692, nerflantern slot scrubbed) + partial wall SpEffect 755890000 (cut 0.5) + broken SpEffect 755890002 (x2), both applied by the copied wall event
Untouchable boss: moveset rows (think 755890001 -> battle 755890, variation 75589 with 9 vanilla judges + beam judge 150, bullet 755890000 (clone of 10732000), atk 755890000 magic 110, pulses 755890003-755890005 without madness)
Untouchable boss: repointing N placed boss slot(s)
  <part> (entity <id>): NPCParamID -> 755890000, ThinkParamID -> 755890001
  wall event patched for entity <id>: 2 wall swap(s) (20011470 -> 755890000) + 2 HP bar flip(s) + 1 broken rider(s) (755890002)
  Fallback: repointed N part(s) in <name> (merge-dir copy shipped into the mod dir)
  Repointed M untouchable boss part(s)
  Wall patch: K instruction(s) in J map(s)
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

- `BOSS_HP` (currently 2000) and `BOSS_RUNES` (currently 20000): feel of
  the fight length and reward relative to other minor bosses.
- `DAMAGE_CUT` (currently 0.5, i.e. a 50% cut): whether the boss is
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
survives instead). FogMod's area rescale still applies on top at runtime,
like for every enemy in the arena (`docs/enemy-scaling.md`: the area's
SpEffect multiplies HP by `curve[target tier] / curve[arena's vanilla
tier]`, unique matrix), so `BOSS_HP` is the boss's HP at the arena's
vanilla tier, not an absolute. Observed 2026-09-04: 8427 HP on a depth-12
arena of low vanilla tier with `BOSS_HP` 3000 (x2.81). The multiplier
also varies with the arena the boss landed in. Effective HP outside the
parry window is `HP / DAMAGE_CUT`.

Fight feel is tuned in the boss's own battle script
(`data/mods-src/speedfog/script/755890_battle-luabnd-dcx/755890_battle.lua`,
knobs at the top of the script: `SWING_*`, `TELEPORT_*`, `BEAM_*`,
`MOVE_*`, the four `*_COOLDOWN`, `TELEPORT_HOLD`, `TELEPORT_FAR_RANGE`,
`TELEPORT_AWAY_DIST`, `TELEPORT_BEAM`, `TELEPORT_FAR_WINDUP` and the
`REACT_*` reactions) and in the
injector constants (`BOSS_HP`, `DAMAGE_CUT`,
`BEAM_MAGIC`), never by editing the shared vanilla `528000_battle` (see
"Moveset"). Regenerate the baseline script with WitchyBND and
DSLuaDecompiler as described in `data/mods-src/README.md`.

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
