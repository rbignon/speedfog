# Aging Untouchable minor boss

SpeedFog promotes the Aging Untouchable (chr `c5280`, the lantern-bearing
inquisitor of the DLC, source entity `2049420200`, vanilla part
`c5280_9000` in `m61_49_42_00.msb.dcx`) to an allowlist-only minor boss.
It is reached only through the enemy allowlist:

```toml
[enemy]
randomize_bosses = "all"   # or "minor"
bosses = ["Aging Untouchable"]
```

Nothing of this feature runs unless the enemy randomizer actually places
the boss in an arena. It is independent of every plugin.

## The fight

The boss keeps the untouchable's vanilla identity, a walking lantern that
grabs, and adds what a boss needs:

- **A partial wall until the first parry.** Hits land at half damage and
  never interrupt the boss: it plays only the small hit twitch of an
  immune untouchable, whatever the attack. Its posture can still be
  broken (super armor meter 80, Jori's profile) and its HP bar shows from
  the start.
- **The parry breaks the wall for good.** The riposte lands at full
  damage, the break flash plays, and from then on every hit deals twice
  the vanilla damage (four times the pre-parry number) and flinches the
  boss like any untouchable whose wall is gone.
- **A moveset vanilla AI never uses:** a lantern burst at melee range, a
  frenzy beam at range, an offensive teleport in two shapes (a burst,
  a retreat and a beam at melee range; the vanilla fade and a warp
  behind the player from range), and reactions to hits and flasks.
- 2000 HP at the arena's vanilla tier (see "Scaling and tuning knobs")
  and 20000 runes.

Dying and re-entering restores the partial wall until the next parry:
the wall event restarts with the arena.

## Placement

`data/boss_arena_tags.json` entry `"2049420200"` ("Aging Untouchable")
carries `pool: "minor"`, `boss.exclude_from_pool: true`, `boss.size: 2`,
`dlc: true` and no `arena` block, the promoted allowlist-only pattern of
`docs/boss-arena-constraints.md`:

- `exclude_from_pool` keeps it out of the ordinary
  `randomize_bosses = "minor"`/`"all"` pool (`_compose_pool` drops such
  entries at every branch); the allowlist (`resolve_boss_allowlist`)
  ignores that flag and reaches it.
- `boss.size: 2` is the tag model's only "open arena" lever: it excludes
  size-1 arenas, leaving the AI's teleport scans room to work.

Once placed, the enemy randomizer's part in the arena carries the source
entity in `enemy_assignments` (graph.json), and `UntouchableBossInjector`
(`writer/FogModWrapper/UntouchableBossInjector.cs`) turns that part into
the boss.

## Vulnerability mechanism

A vanilla untouchable is immune until parried. The mechanism is an EMEVD
event per untouchable (a `Restart` event initialized with the entity,
`1700783` on 1.17; the enemy randomizer copies it, with three sibling
events, next to every placed boss; `tools/dump_emevd_warps` `dump
--event` and `init` show the patched copies):

1. At spawn: `SetSpEffect(entity, 20011470)`, the wall (category 1001,
   every damage cut rate 0, and the damage-level table of "Hit
   reactions"), `SetSpEffect(entity, 19690)`, and
   `SetCharacterHPBarDisplay(entity, disabled)`.
2. Wait for `IfCharacterHasSpEffect(entity, 20011471)`: the parried
   animation (8500) applies the parry-window effect through a TAE event.
   Same category 1001 and priority, so it overrides the wall during the
   window and the riposte lands.
3. Once and for all: `ClearSpEffect(entity, 20011470)`, `Create NPC Part`
   (a part with an HP bar), `SetCharacterHPBarDisplay(entity, enabled)`,
   `SetSpEffect(entity, 20011472)` (a one-second break VFX).

The Item Randomizer's always-on `nerflantern` option defeats this for
ambient untouchables by writing 20011471 into a free `NpcParam` slot of
every 5280-band row (slot 31): resident in the same category, it keeps
the wall from taking hold and the detector of step 2 permanently true.

SpeedFog keeps the vanilla flow and replaces the immunity by a partial
wall, then a broken state:

- **The cut row** (`SpeedFogIds.UntouchableBossSpEffectRow`, 755890000)
  is a clone of the parry-window effect 20011471 (category 1001,
  priority 0, `stateInfo` 121) with the eight damage-cut fields
  (`slashDamageCutRate` ... `darkDamageCutRate`) at
  `UntouchableBossInjector.DAMAGE_CUT` (0.5) and the twelve `dmgLv_*`
  fields copied from SpEffect 5300 (see "Hit reactions"). It thereby
  matches the vanilla wall on every field that governs hit reactions;
  the cut rates, the seven status-buildup defense rates and the `vfxId`
  stay the parry window's. It is never resident on the boss row: the
  copied event applies it at spawn and clears it at the break.
- **The broken row** (`SpeedFogIds.UntouchableBrokenSpEffectRow`,
  755890002) is a permanent, VFX-less clone of the break VFX effect
  20011472 (category 0, so it coexists with the parry windows of later
  parries) whose eight cut rates are
  `UntouchableBossInjector.BROKEN_DAMAGE_TAKEN` (2). The ratio against
  the partial wall is `BROKEN_DAMAGE_TAKEN / DAMAGE_CUT`.
- **`PatchWallEvents`** (MSB phase, per arena map) rewrites the copied
  events of each placed boss: 20011470 becomes 755890000 in the wall
  event's `SetSpEffect` and `ClearSpEffect`; the first
  `SetCharacterHPBarDisplay(disabled)` of every copied event becomes
  enabled (the wall event's spawn-time one and the teleport sibling's
  mid-warp one, so the bar shows from the start and survives each
  teleport); a `SetSpEffect` of the broken row is inserted right after
  the wall event's `SetSpEffect(entity, 20011472)` with the same entity
  parameter. Five instructions per boss. Two opposite failures, each with
  its warning: a map whose EMEVD is in neither the mod dir nor the merge
  dir keeps the vanilla full wall (an immune boss until parried); a map
  with an EMEVD but no copied wall event (never observed) gets no cut at
  all (full damage from the start).
- **The boss `NpcParam` row** (`SpeedFogIds.UntouchableBossNpcRow`,
  755890000) is a clone of vanilla 52800086 with `hp` = `BOSS_HP`
  (2000), `getSoul` = `BOSS_RUNES` (20000), `toughness` =
  `BOSS_TOUGHNESS` (0, Jori's value), `superArmorDurability` =
  `BOSS_SUPER_ARMOR` (80) and `superArmorRecoverCorrection` =
  `BOSS_SUPER_ARMOR_RECOVER` (3/13, Jori's profile), and nerflantern's
  20011471 scrubbed from every slot (resident, it would defeat the partial
  wall as it defeats the vanilla one).
  Slots 17 (20011450, vanilla's teleport gate) and 18 (20011473) stay.
  The row lives outside the 5280 band on purpose: nerflantern patches
  every row of that band unconditionally, and the boss's vulnerability
  must stay under SpeedFog's control. Ambient untouchables keep the
  vanilla row and its nerflantern slot, so they stay damageable.

`NpcParam`, `SpEffectParam`, `NpcThinkParam`, `Bullet` and `AtkParam_Npc`
are separate row namespaces: the same numeric ids recur across them
(`SpeedFogIds`) without conflict.

### Hit reactions

Which damage animation an NPC plays for a hit is decided by the attack's
damage level (`AtkParam.dmgLevel`: small, medium, large, blow...) after
the target's active SpEffects have had their say: SpEffectParam's twelve
`dmgLv_*` fields (`dmgLv_None` ... `dmgLv_Breath`, enum
`ATKPARAM_REP_DMGTYPE`, 0 = keep the attack's level) replace one incoming
level by another. Bosses do not flinch because they carry a resident
replacement table: SpEffect 5300 (permanent, category 1001, priority
200, the twelve fields at 1 = every level replaced by None) sits in slot
1 of Jori, Godrick, Margit and 690 `NpcParam` rows in all; the regular
Inquisitor c5311 shares Jori's animations and flinches on every hit
because its row has no 5300. The super armor meter is a separate path:
an attack's poise damage depletes `superArmorDurability`, and the
stagger it triggers ignores the table (Godrick carries 5300 and still
gets stance-broken). `toughness` plays no part in either.

The vanilla wall 20011470 carries the same table, which is why an immune
untouchable never flinches; the parry-window effect the cut row is
cloned from has none. The cut row therefore takes the table from 5300,
and the boss does not flinch until the first parry. The engine only
reads the table from a category-1001 effect (see "Engine facts and
pitfalls"), and a category-1001 row cannot be resident next to the wall,
so the broken row (category 0 by necessity) carries no table: once the
wall is broken the boss flinches on ordinary hits, like a vanilla
untouchable whose wall is gone. The level-None reaction is the small hit
twitch seen before the break; it interrupts nothing.

## Two-phase injector

`Program.cs` runs the injector in two phases, both gated on the boss
being placed (`IsBossPlaced`: some `enemy_assignments` value equals
`SpeedFogIds.UntouchableSourceEntity`, `2049420200`, as a decimal
string):

1. **Regulation phase** (`ApplyRegulation`): `ApplyParams` writes the
   core rows (boss `NpcParam`, cut row, broken row) and, when the static
   assets and the four moveset params are available, the moveset rows
   (see "Moveset"). It returns `(Core, Moveset)`; `Program.cs` keeps the
   result on the context for the MSB phase.
2. **MSB phase** (`ApplyModDirInjectors`, after FogMod's write): `Inject`
   collects every `enemy_assignments` key (an arena entity id) whose
   value is the source entity and scans every `.msb.dcx` of the mod
   directory in parallel. `ApplyToMsb` repoints the `NPCParamID` of each
   placed part carrying one of those entity ids to the boss row, and its
   `ThinkParamID` to the boss think row (755890001) when the moveset was
   written; a part whose `ModelName` is not `c5280` is left alone with a
   warning. `PatchWallEvents` then rewrites the map's EMEVD as described
   above. When the core rows were not written (`NpcParam` or
   `SpEffectParam` unavailable), the phase is skipped with a warning
   instead of repointing at a missing row.

Assignment targets left unfound after the mod-dir scan are reported as
`Warning: assignment target <id> not found in any map (phase slot?)`.
Two causes are expected:

- `enemy_assignments` expands one slot per phase entity of multi-phase
  bosses (`docs/boss-arena-constraints.md`); a phase slot of a
  single-phase arena has no MSB part.
- The arena map is one FogMod never writes. Starscourge Radahn's arena
  is a private instance map (`m60_13_09_02`) with no fog gate of its
  own, so it is absent from `mods/fogmod`; the Item Randomizer's swap
  ships through `mods/itemrando`, but SpeedFog's injectors only patch
  `mods/fogmod`. For such maps, listed in `data/game_tweaks.toml`
  `[[fallback_arena_maps]]` (`GameTweaks.FallbackArenaMaps`), `Inject`
  reads the merge-dir copy (`<mergeDir>/map/mapstudio/<name>.msb.dcx`,
  with its EMEVD), runs the same repoint and wall patch, and ships the
  result into `mods/fogmod` only when something was repointed there.
  Extend the list if the warning ever fires for another arena whose map
  exists in the merge dir. The other fogless arenas (Stone Platform
  m19_00, Farum Azula m13_00) have their boss parts in maps FogMod writes
  anyway for the area's gates; Radahn's is the only boss on an
  02-supertile FogMod never touches.

## Moveset

Three tools vanilla AI never uses, applied to the promoted instance only:

- **Lantern burst** at melee range: animation 3001 (AtkParam_Npc
  5280115, magic 100, hit radius 4 m at dummy 906, 1.5 m knockback, no
  throw), vanilla's post-teleport surprise attack promoted to a regular
  act next to the grab (3002/3003, AtkParam 5280110, throw 4100). It
  reads as a burst around the lantern that pushes the player back and is
  kept rare so the parryable grab stays the main threat. Pure AI.
- **Frenzy beam** at range: animation 3004 (lantern raised, thirteen
  bullet events from dummy 210), which vanilla AI registers with
  probability 0 everywhere, re-enabled and made to fire a Frenzied
  Burst-style laser (magic damage, no madness).
- **Flame nova** on the same animation: three of 3004's other bullet
  events fire Midra's Flame of Frenzy in four directions around the
  lantern (magic damage, no madness). It is what lands when the player
  stands at contact, where the beam mostly whiffs, so the one act
  threatens at every range: c5280's behavior graph has no fourth attack
  state to give the boss, and a second payload on the one dormant
  animation is the way around that.

### TAE patch

3004's bullet events carry judge ids 101/102, the same ids the idle,
walk and teleport animations fire for the lantern's ambient pulses.
Remapping them under the boss variation would fire the beam at rest, so
`StaticModBuilder/UntouchableTaePatcher` rewrites the judge of seven of
the thirteen events in the shipped `chr/c5280.anibnd.dcx`: four to 150,
the beam (indices 0, 4, 8, 12, spread by start time), and the event
halfway between each consecutive pair to 151, the flame nova (indices 2,
6, 10). The patch is inert for ambient untouchables twice over: vanilla
AI never selects 3004, and variation 52800 has no row for either judge.
The patcher refuses (warning, nothing written) any layout other than
dummy 210 with judges 101/102, so a game patch renumbering c5280's
judges disables the moveset instead of corrupting the TAE. The patched
anibnd (about 1.3 MB) ships in every seed's static mod; it is inert
without the boss rows. Knob: `BEAM_EVENT_COUNT` (4), which also sets the
nova count (one less). If a payload ever fails to aim at the player,
candidate dummies are 10 (the grab's flash origin) and 906 (the burst).

### Per-seed rows (`ApplyMoveset`)

| Param | Row | Source | Changes |
|-------|-----|--------|---------|
| NpcParam | 755890000 (the boss row) | 52800086 | `behaviorVariationId` 75589 |
| NpcThinkParam | 755890001 | 52800000 | `battleGoalID` 755890 (`logicId` stays 528000) |
| BehaviorParam | 275589100/101/102/110/111/112/113/115/500 | the nine vanilla rows of variation 52800 (500 is the non-formula row 1170) | `variationId` 75589; judges 100-102 re-pointed at the pulse clones below |
| Bullet | 755890003-005 | 205280000-002 (the lantern's ambient pulses, judges 100-102) | madness rider (SpEffect 26000) cleared, VFX rider kept |
| BehaviorParam | 275589150 | 252800101 | judge 150, refType 1, refId 755890000 |
| Bullet | 755890000 | 10732000 (Frenzied Burst) | `atkId_Bullet` 755890000, `spEffectId0-4` and `spEffectIDForShooter` -1 (no madness rider on the target or the caster) |
| AtkParam_Npc | 755890000 | 5280115 | `atkMag` = `BEAM_MAGIC` (110) |
| BehaviorParam | 275589151 | 252800101 | judge 151, refType 1, refId 755890006 |
| Bullet | 755890006-008 | 210730000 -> 210730005 -> 210730006 (Midra's Flame of Frenzy, followed link by link through `HitBulletID`) | `atkId_Bullet` 755890001, chain re-pointed at the clones (the last link ends it), every `spEffectId0-4` and `spEffectIDForShooter` -1; on the root only, `numShoot` = `FLAME_DIRECTIONS` (4) and `shootAngleInterval` = 90 degrees |
| AtkParam_Npc | 755890001 | 5280115 | `atkMag` = `FLAME_MAGIC` (50) |

Row ids: `200000000 + variation * 1000 + judge` (`SpeedFogIds.BehaviorRowId`).
The Frenzied Burst SFX (527032 laser, 527033 hit) live in
`sfxbnd_commoneffects`, Midra's (527062, 527063) in
`sfxbnd_commoneffects_dlc02`: both bundles c5280 loads, unlike the
per-chr ones (see "Engine facts and pitfalls"). Every link of the chain
carries the damage row, not just the terminal ball, so one cast can land
`BEAM_EVENT_COUNT` beams at `BEAM_MAGIC` each plus, per flame event, up
to one hit per direction and per link: three events, four directions and
three links at `FLAME_MAGIC` is the ceiling before the player's
defenses. The damage template is the lantern swing, which carries a
1.5 m knockback, so the nova's real risk at contact is chained stagger
rather than the damage number; judge both in game and tune the knobs
together. The chain is followed rather than hardcoded, and a chain
longer than `FLAME_CHAIN_MAX` (3, vanilla's length) skips the moveset
rather than overrun the clone band.

### Gating

All-or-nothing. The moveset rows are written only if
`<data-dir>/mods/speedfog/chr/c5280.anibnd.dcx` and
`<data-dir>/mods/speedfog/script/755890_battle.luabnd.dcx` exist
(`MovesetStaticAssets`, built by `tools/bootstrap.py`), NpcThinkParam,
BehaviorParam, Bullet and AtkParam_Npc are available, every template
row is present, variation 52800 has exactly the nine rows of
`VanillaJudges` (refresh the list after a game patch that adds or
renumbers a c5280 behavior row) and each pulse judge resolves to a
Bullet row; otherwise one warning line with the reasons and the boss
keeps vanilla AI. The `behaviorVariationId` change belongs to the
group: alone, it would leave the boss with a variation that has no
BehaviorParam rows, i.e. no attacks, and a think row pointing at a
missing luabnd would leave it without battle AI.

## AI script

`data/mods-src/speedfog/script/755890_battle-luabnd-dcx/755890_battle.lua`
(plain text, repacked at bootstrap into
`data/mods/speedfog/script/755890_battle.luabnd.dcx`, see
`data/mods-src/README.md`) is the decompiled vanilla `528000_battle`
renamed to goal 755890 plus SpeedFog's acts, gates and reactions.
`battleGoalID` selects the battle luabnd independently of `logicId`, so
only the battle script is cloned; ambient untouchables keep the vanilla
script. Goal tables are keyed by numeric id and the `GOAL_` globals of
vanilla scripts come from the shared aiCommon global-name list, which
only knows the vanilla names, so the script assigns
`GOAL_Houzuki755890_Battle` (755890, the think row's `battleGoalID`) and
`GOAL_Houzuki755890_AfterAttackAct` (755891, any unused id) itself.
Every knob is a file-scope local at the top of the script, followed by
the vanilla ids named for reading (`ANIM_*`, `SPEFFECT_*`,
`GUARD_EZSTATE`), all captured as upvalues by the functions below (Lua
5.0 allows 32 per function, counted by
`tests/test_mods_src_lua_scripts.py`); a nil arithmetic error on the
first activation points there. The goal model, the act table, the attack and
movement goals and the `ai:` queries the script relies on are described
in `docs/ai-scripts.md`.

### Decision tree

The vanilla logic script (528000, kept by the boss) adds the battle goal
with an infinite life. Each activation builds a weight table, draws one
act, and that act queues sub-goals run in order. Once the queue is empty
the goal ends and the logic re-adds it at once: a new draw, no idle
time between decisions. A weight of 0 is never drawn. `Goal.Interrupt`
can clear the queue and replace it at any time (see "Interrupts").

```
Goal.Activate, in evaluation order (weights)
1. player in the back cone (IsInsideTarget B, 90), tested first, at any distance
   dist >= 8 : teleport ready → Act02 100            else → Act01 100
   dist <  8 : teleport ready → Act02 50 / Act43 40 / Act01 10   (TELEPORT_BEHIND)
               else           → Act43 80 / Act01 20
2. else SpEffect 5031 active → Act05 100
3. else SpEffect 5032 active → Act06 100
4. else dist >= 10 : teleport ready → Act02 60 / Act04 40   (BEAM_FAR_TELEPORT_READY)
                     else           → Act01 50 / Act04 50   (BEAM_FAR_TELEPORT_NOT_READY)
5. else dist >= 3  : Act03 35 / Act02 25* / Act04 15 / Act46 15 / Act11 10
                     (TELEPORT_MID, BEAM_MID, MOVE_MID, SWING_MID; Act03 keeps the rest)
6. else (< 3 m)    : Act03 30 / Act02 25* / Act04 15 / Act11 15 / Act42 15
                     (TELEPORT_CLOSE, BEAM_CLOSE, SWING_CLOSE, MOVE_CLOSE; Act03 keeps the rest)
   * 0 while the teleport cools; the grab takes that share (55 close, 60 mid)
Then, on the table obtained:
   Act03 → 0 while the last 3002 is <= GRAB_COOLDOWN (6 s) old   (engine counter)
   Act04 → 0 while the last 3004 is <= BEAM_COOLDOWN (4 s) old   (engine counter)
   Act11 → 0 while the swing timer (slot 11) runs
   "teleport ready" = the teleport timer (slot 10) at 0, nothing else
```

Direct consequences: a player in the boss's back gets no grab and no
swing at any distance, only a turn, an approach or a teleport, and the
post-grab retreat (5031/5032) is skipped while they stand there. Every
grab ends on one of those two markers (3002's own TAE applies 5032, the
throw 4100 applies 5031), so the act that follows a grab is the near
teleport whenever its timer allows: that is the boss's main way of
opening the distance, and the walk retreat is the fallback. Between
5 and 10 m, Act02 is the far teleport (the 5 s fade), not the burst and
retreat. A near teleport that finds no room queues nothing, so the next
draw follows at once with the teleport weight at 0 for `TELEPORT_RETRY`
(2 s). Act46
parks the boss at 4 m, inside the near band, so a teleport picked after
a strafe is the near one and its burst mostly whiffs at that range: a
retreat and a beam.

```
What each drawn act queues
Act01  run to 0.5 m (5 s max), then animation 2100 (life 0.1 s, reach 5 m)
Act02  dist >= TELEPORT_FAR_RANGE (5 m) → far: teleport timer 11.5, hold 9, watch 20011452,
         3000 (life 10 s, reach 5 - hit radius + 999, effectively always); the rest
         happens in Goal.Interrupt
       dist < 5 m → near: room scan from the boss, B then BL then BR (F/FL/FR when the
         player is in its back), at 8 m then at 5 m
         no room → teleport timer 2 s, nothing queued
         room    → teleport timer 6, hold 3.5, ClearSubGoal, swing timer 4 + immediate
                   burst 3001 (the swing timer is not read), warp from TARGET_SELF,
                   beam if ready
Act03  approach to 12 m (never queued: offered under 10 m only), watch 5030,
       grab 3002 (life 8, reach 12, turn 2 s / 50 degrees)
Act04  beam 3004 (life 3, reach 999, turn 1.5 s / 60 degrees)
Act05  teleport ready and room → the near teleport (see Act02), which is the
       whole act; else ClearSubGoal, LeaveTarget 10 m (5 s), then the beam if ready
Act06  the same, LeaveTarget 8 m (4 s)
Act11  run to 3 m (5 s max), swing timer 4, burst 3001 (life 8, reach 4, turn 1.5 s / 60 degrees)
Act42  sidestep to the right, 0.8 to 1.5 s
Act43  turn toward the player, 2 s, until within 90 degrees
Act46  walk to 4 m (or back off to 4 m), life 10 s, then strafe to a random side 0.1 to 2 s
Act07 to Act10, Act40, Act41, Act44, Act45, Act47, ActAfter: never drawn
```

```
Vanilla 528000, same order, for comparison
1. player in the back cone
   dist >= 8 → Act02 100 (3000, then the warp around TARGET_EVENT and 3001)
   dist <  8 → Act43 80 / Act01 20
2. else 5031 → Act05 100
3. else 5032 → Act06 100
4. else dist >= 10 → 20011450 active: Act02 99 / Act01 1, else Act01 100
5. else dist >= 3  → Act03 100
6. else            → Act03 100
Then Act03 → 1 while 3002 cools (12 s): the engine holds the boss on the cooling grab
```

Not readable from the Lua, because the goals it queues are native:
whether an attack outside its `successDist` still plays, waits for its
life or fails (`GOAL_COMMON_CommonAttack`); whether the warp queued
after the burst starts at 3001's cancel window (the wrapper sets
`moveCancel` and `attackCancel`; observed in game, see
`docs/ai-scripts.md`); what `ToTargetWarp` does with a 0 m
distance around a character; the exact cone of `IsInsideTarget(TARGET_ENE_0,
AI_DIR_TYPE_B, 90)` (the same call in 60 vanilla scripts); whether an
interrupt reaches the battle goal while a `REGISTER_GOAL_NO_INTERUPT`
sub-goal runs, and whether `ClearSubGoal` cuts the running animation or
lets it finish.

### Acts and probabilities

Every act of the script (the vanilla ones are kept as decompiled, even
those no bracket ever weights; where each is weighted is in the
decision tree above). Distances are `successDist` or `stopDist` values
in metres, turns are `turnTime` / `turnFaceAngle` (see
`docs/ai-scripts.md`):

| Act | Origin | What it queues |
|-----|--------|----------------|
| Act01 | vanilla | approach (`Approach_Act_Flex`, stop 0.5 m, always running) then animation 2100 (life 0.1 s, reach 5 m) |
| Act02 | SpeedFog (vanilla's teleport act rewritten) | under `TELEPORT_FAR_RANGE`: the burst 3001 as wind-up, `ToTargetWarp` away from the player, the beam if ready; from it: vanilla's 3000 with the 20011452 watch, whose interrupt warps behind the player and bursts |
| Act03 | vanilla, grab through the shared builder | approach (stop 12 m, so never queued in the brackets that weight it) then the grab 3002 (reach 12 m, turn 2 s / 50 degrees, life 8 s) with the 5030 watch that chains the throw 3003 |
| Act04 | SpeedFog | the beam and flame nova 3004 (reach 999, turn 1.5 s / 60 degrees, life 3 s) |
| Act05 | vanilla, teleport and beam added | the near teleport when its timer allows and there is room; else clears the queue, `LeaveTarget` to 10 m (life 5 s), then the beam if ready |
| Act06 | vanilla, teleport and beam added | the same with `LeaveTarget` to 8 m (life 4 s) |
| Act07 to Act10 | vanilla | nothing (empty acts) |
| Act11 | SpeedFog | approach (stop 3 m, running) then the burst 3001 (reach 4 m, turn 1.5 s / 60 degrees, life 8 s) and the swing timer |
| Act40 | vanilla | `ApproachTarget` to 0.1 m, walking, life 1-3 s |
| Act41 | vanilla | `LeaveTarget` to 10 m, walking, life 1-3 s |
| Act42 | vanilla | `SidewayMove` to the right, walking, life 0.8-1.5 s |
| Act43 | vanilla | `Turn` toward the player, stop width 90 degrees, life 2 s |
| Act44 | vanilla | `StepSafety` away from the player's side (in front: a step back or sideways; on the right: left; on the left: right), life 5 s |
| Act45 | vanilla | `StepSafety` to either side or the right only (a draw), life 5 s |
| Act46 | vanilla | `ApproachTarget` to 4 m (or `LeaveTarget` when closer), walking, life 10 s, then `SidewayMove` to a random side for 0.1-2 s |
| Act47 | vanilla | an encircling routine keyed on `TORIMAKI_MIN_DIST` / `TORIMAKI_MAX_DIST` and `TARGET_ENE0`, none of which is defined (a nil comparison or a nil target if it ever ran; `resultTypeIfGuardSuccess` is a fourth undefined global, a harmless trailing nil) |
| ActAfter_AdjustSpace | vanilla | the empty after-attack goal `GOAL_Houzuki755890_AfterAttackAct` (life 10 s); runs only when an act returns odds above 0, which none does |

### Teleports

Act02 has two shapes by distance, `TELEPORT_FAR_RANGE` (5 m center to
center, melee reach with a long weapon being 3-4 m); the hit reaction
always fires the near one.

**Near** (under 5 m): an attack, a warp and a follow-up, built from the
untouchable's own moves. The burst 3001 is the wind-up, queued whatever
the swing timer says (it re-arms it): its hit lands from the first frame
and the warp fires at its cancel window (`BURST_CANCEL`, 1.0 s,
observed in game, see `docs/ai-scripts.md`). The warp
lands `TELEPORT_AWAY_DIST` (8 m) from the boss's own position, away
from the player (the five-argument `TARGET_SELF` form of Rennala's
203100 and of 301010's retreats): straight behind the boss or, when the
player stands in its back, straight ahead, then the diagonals.
`Houzuki755890_FindRoomAway` scans those directions from the boss's own
position with its hit radius as the line width, then again at
`TELEPORT_AWAY_FALLBACK` (5 m) for small arenas. The beam follows when
it is ready: the boss bursts, retreats and fires. The scan runs when the
act is queued; without room nothing is queued (the burst is not spent on
a warp that cannot happen) and the teleport timer restarts at
`TELEPORT_RETRY` (2 s) instead of the full cooldown, so a cramped spot
is retried soon but not at every decision. The wind-up uses the
`ComboTunable_SuccessAngle180` wrapper with reach 999, turn 0 and every
angle 180, the argument set of 504000's post-warp attacks (its only
vanilla user), so it fires whatever the player's side. The retreat stays
in the player's view
on purpose: lock-on cannot be broken from the AI, so a warp behind the
player would only turn the camera.

**Far** (5 m and more), vanilla's own act: the 5 s teleport-out animation
3000 (queued with vanilla's own `successDist` expression, `5 - hit
radius + 999`, effectively always), whose marker (SpEffect 20011452 at 4.77 s) triggers vanilla's
interrupt, kept but for two changes: the scan
(`Houzuki755890_FindRoomBehind`: in front, then behind right/left at 0 or
2 m, then behind at 2 m) and the warp go around `TARGET_ENE_0` rather
than `TARGET_EVENT`, and the burst that follows waits for the swing timer
(vanilla swings unconditionally). The warp goal plays no animation
itself; the arrival animation comes with it. The direction and
distance pairs are vanilla's, written for the event point: the first
three branches land 0 m from the player (the Nox knight scripts
300000-302000 also warp 0 m around `TARGET_ENE_0`, as their no-room
fallback); in game the warp lands behind the player.

### Cooldowns

The grab (`GRAB_COOLDOWN` 6 s, vanilla 12) and 3004 (`BEAM_COOLDOWN`
4 s, beam and nova together) are engine counters through `SetCoolTime`, which registers the
interval (`RegistAttackTimeInterval`) and reads the counter
(`GetAttackPassedTime`) in one call: the table's two calls in
`Goal.Activate` run before any act or reaction, and
`Houzuki755890_BeamReady` is the same call with weights 100/0 wherever an
act or a reaction needs the beam, so no counter is ever read
unregistered. The burst (`SWING_COOLDOWN` 4 s, set by every 3001 the
script queues, read by Act11, by the far teleport's post-warp burst and
by the plain-burst hit reaction; the near teleport's wind-up fires
whatever the timer says, so 3001 can also play once per teleport
cooldown) and the teleport
(`TELEPORT_COOLDOWN` 6 s after a near one, `TELEPORT_FAR_COOLDOWN` 11.5 s
after a far one, whose own sequence takes about 9 s, `TELEPORT_RETRY` 2 s
after a near attempt that found no room) are AI timers
(`TIMER_SWING` slot 11, `TIMER_TELEPORT` slot 10, set when the act is
queued or, for the retry, attempted): 3001 is never registered, so no engine interval can hold the
boss on a burst. `Houzuki755890_TeleportReady` is the teleport timer back
at 0 and nothing else: the script reads neither the 3000 counter nor
vanilla's SpEffect 20011450 (see "Engine facts and pitfalls").

The last argument of `SetCoolTime` is the weight kept while the attack
cools. The script passes 0 and every bracket keeps a movement filler
with a positive weight, so the boss never freezes while its attacks
cool.

### Interrupts

An interrupt is an event the engine raises about the character (the
kinds are the `INTERUPT_*` constants of `ai_define`); it calls
`Goal.Interrupt`, which asks `ai:IsInterupt(kind)` which one fired and
either leaves the queue alone (return false) or replaces it (the
vanilla pattern of 472000: `ClearSubGoal`, queue the answer, return
true). The vanilla teleport and grab follow-ups are interrupts too: the
warp marker of 3000 and the grab's connect are watched SpEffects that
call the script back mid-animation. The reactions come after them. A
reaction spends an attack the table could offer anyway, so it moves an
attack earlier without adding any. No reaction fires while a teleport or
a grab sequence is in flight (`Houzuki755890_SequenceInFlight`), since
its `ClearSubGoal` would drop the warp, the beam or the throw: the hold
timer (`TIMER_HOLD`, slot 9) is set with each queued teleport to
`TELEPORT_HOLD` (near: `BURST_CANCEL` + `ARRIVAL_MAX` + margin, 3.5 s)
or `TELEPORT_FAR_HOLD` (far: `MARKER_3000` + `ARRIVAL_MAX` +
`BURST_LENGTH` + margin, 9 s), and the grab is covered by its counter
under `REACT_HOLD` (`GRAB_CHAIN` + 1 s, 7 s). The spans come from the c5280 TAE and
are named at the top of the script. Accepted exposure, shared with
vanilla scripts: a spirit ash's hit outside those windows can trigger
the hit reaction like a player's hit.

```
Goal.Interrupt, first matching case wins
0. on a ladder, SpEffect 5110 active, or an illness effect → nothing (false)
1. ActivateSpecialEffect
   a. 20011452 active (3000's marker at 4.77 s): room scan around the PLAYER
      front clear → BR 0 m ; BR clear → BR 0 m ; BL clear → BL 0 m ; BL 2 m ; BR 2 m ; B 2 m
      room    → ClearSubGoal, warp, then an immediate burst only if the swing timer is at 0
      no room → nothing, 3000 finishes on its own
      → true either way
   b. 5030 is the trigger AND the player in front within 4 m → ClearSubGoal + throw 3003 → true
   c. else false
2. Damaged: player in the front 120-degree cone within REACT_HIT_RANGE (2 m)
   AND nothing in flight (in flight: hold timer > 0, or 3002 started <= REACT_HOLD (7 s) ago)
   AND draw <= REACT_HIT (25)
   a. teleport ready AND room → the whole near teleport (burst, warp, beam if ready) → true
      (ready without room: the teleport timer is set to TELEPORT_RETRY, then fall through)
   b. else swing timer at 0 → ClearSubGoal + immediate burst → true
   c. else false
   A hit from the back never triggers anything.
3. UseItem: dist >= REACT_RANGE (5 m) AND nothing in flight AND draw <= REACT_HEAL (80)
   AND beam ready → ClearSubGoal + beam → true
Shoot (a cast or a shot starting) is deliberately not handled, and falls through to false.
```

That omission is the design's one refusal: answering the player's input
before its consequence reads as unfair. The two handled reactions are not
inputs in that sense. A hit is a consequence the player has already
committed to, and a flask is a commitment of its own that costs them
their guard, which vanilla bosses punish too. Re-adding the cast reaction
is one branch calling `Houzuki755890_ReactBeam` with a draw knob of its
own, but it is a design decision rather than a tuning value.

### Tests

`tests/test_mods_src_lua_scripts.py` parses the script with luaparser
and rejects the syntax Lua added after 5.0 (the engine's compiler).
`tests/test_untouchable_ai_table.py` runs `Goal.Activate`, the acts and
`Goal.Interrupt` in an embedded Lua (lupa) against a fake ai/goal and
checks the weight table, the queued sub-goals with their arguments, the
timers and the reactions per state (distance, behind, SpEffects, seconds
since each attack, AI timers, room per direction, draw). Extend it with
any table change. The in-game checklist below remains the real test.

## Scaling and tuning knobs

`BOSS_HP` is the boss's HP at the arena's vanilla tier, not an absolute:
the MSB repoint replaces the Item Randomizer's scaled placement clone
outright, and FogMod's area rescale applies on top at runtime like for
every enemy of the arena (`docs/enemy-scaling.md`: HP multiplied by
`curve[target tier] / curve[arena's vanilla tier]`). A depth-12 arena of
low vanilla tier multiplies it by about 2.8. Effective HP before the
parry is `BOSS_HP / DAMAGE_CUT`. An arena outside `mods/fogmod` and the
fallback list keeps the randomizer's own clone (a 5280-band row, so
nerflantern makes it damageable, with generic tier scaling and runes): a
functional fight, off-design.

Knobs, never edited in the shared vanilla `528000_battle`:

- `UntouchableBossInjector`: `BOSS_HP`, `BOSS_RUNES`, `BOSS_TOUGHNESS`,
  `BOSS_SUPER_ARMOR`, `BOSS_SUPER_ARMOR_RECOVER`, `DAMAGE_CUT`,
  `BROKEN_DAMAGE_TAKEN`, `BEAM_MAGIC`, `FLAME_MAGIC`, `FLAME_DIRECTIONS`;
  `UntouchableTaePatcher`: `BEAM_EVENT_COUNT` (beams, and one less nova).
- The battle script: the probabilities (`SWING_*`, `TELEPORT_*`,
  `BEAM_*`, `MOVE_*`), the five `*_COOLDOWN` and `TELEPORT_RETRY`,
  `TELEPORT_FAR_RANGE`,
  `TELEPORT_AWAY_DIST`, `TELEPORT_AWAY_FALLBACK`, the animation spans the
  holds derive from (`BURST_CANCEL`, `BURST_LENGTH`, `ARRIVAL_MAX`,
  `MARKER_3000`, `GRAB_CHAIN`), and the two reactions (`REACT_HIT`, `REACT_HEAL` and their ranges).

## Verifying in game

Generate a seed with the allowlist above, then in the arena:

1. **Wall and parry**: the HP bar shows from the start; hits land at half
   damage and never interrupt the boss (only a small twitch, a charged
   heavy included); heavy hits still break its posture. After the first
   parry: riposte at full damage, break flash, every later hit at four
   times the pre-parry number, and the boss flinches on ordinary hits.
   After dying and re-entering, the partial wall is back. A boss still
   interrupted before the break means the table does not act on c5280:
   check whether a vanilla immune untouchable flinches (a save without
   the item randomizer, whose nerflantern option lifts every wall), then
   try `npcType` 1, the next untested difference against Jori. A boss
   that never staggers means the meter is masked by the table: lower
   `BOSS_SUPER_ARMOR`.
2. **Melee**: grabs (parryable), timer-gated bursts (Act11's, the far
   teleport's post-warp one, the hit reaction's plain one) at most once
   per `SWING_COOLDOWN` (a near teleport's wind-up burst can come
   sooner), novas between them, and sidesteps or strafes through the
   cooldowns; the boss never stands still for several seconds, and a
   grab is followed by the burst and warp rather than the walk retreat
   whenever the teleport is off cooldown.
3. **Near teleport** (under 5 m): a burst on the spot, a vanish about 1 s
   into it with no fade, a reappearance 8 m from where the boss stood,
   away from the player (5 m in a small arena; straight ahead when the
   player was in its back) with
   the arrival animation, then the beam when it is ready. A burst that
   completes its 1.8 s before the vanish means the attack goal did not
   hand over at `BURST_CANCEL`. In a catacomb room, confirm it still
   retreats at least sometimes; a boss that never retreats there means
   both scan distances fail: lower `TELEPORT_AWAY_FALLBACK`.
4. **Far teleport** (5 m and more): the 5 s lantern fade, then the warp
   behind the player and the burst, every time and not only the first.
   Never two teleports within `TELEPORT_COOLDOWN`.
5. **Beam and flame nova**: from 10 m, at 3-10 m, at contact, and after a
   grab from the retreat distance; a Frenzied Burst laser from the
   lantern aimed at the player (about 110 magic per hit, up to four hits
   per cast) interleaved with three bursts of Midra's flames in four
   directions around the lantern (about 50 magic per hit, reaching a few
   metres); neither during idle or walk, no madness from either. Watch
   the knockback at contact: four directions times three chain links can
   chain-stagger, and `FLAME_MAGIC` or `FLAME_DIRECTIONS` is the knob.
   Beams but no flames means the shipped `chr/c5280.anibnd.dcx` predates
   the flame judge (re-run `tools/bootstrap.py`; nothing detects a stale
   anibnd, unlike the luabnd) or the DLC SFX bundle is absent. Template
   drift cannot produce it: it skips the whole moveset, and the boss then
   keeps vanilla AI and never casts 3004 at all.
6. **Reactions**: from 5 m or more, drinking a flask draws a beam most of
   the time when it is ready, and within 5 m it does not. Casting or
   shooting never draws anything, at any distance. At melee range, about
   one hit in four draws the near teleport when it is ready, or a burst.
7. **Ambient regression**: an ambient untouchable still only teleports
   (once per engagement) and grabs, builds madness with its lantern, no
   burst at range, no beam, no script error.

## Expected log lines

```
Untouchable boss: NpcParam 755890000 (clone of 52800086, hp 2000, runes 20000, toughness 0, super armor 80/0.23076923, nerflantern slot scrubbed) + partial wall SpEffect 755890000 (cut 0.5, no-flinch table of 5300 folded in) + broken SpEffect 755890002 (x2), both applied by the copied wall event
Untouchable boss: moveset rows (think 755890001 -> battle 755890, variation 75589 with 9 vanilla judges + beam judge 150, bullet 755890000 (clone of 10732000), atk 755890000 magic 110, + flame judge 151, bullets 755890006-755890008 (chain of 210730000, x4), atk 755890001 magic 50, pulses 755890003-755890005 without madness)
Untouchable boss: repointing N placed boss slot(s)
  <part> (entity <id>): NPCParamID -> 755890000, ThinkParamID -> 755890001
  wall event patched for entity <id>: 2 wall swap(s) (20011470 -> 755890000) + 2 HP bar flip(s) + 1 broken rider(s) (755890002)
  Fallback: repointed N part(s) in <name> (merge-dir copy shipped into the mod dir)
  Repointed M untouchable boss part(s)
  Wall patch: K instruction(s) in J map(s)
```

`M >= 1` whenever the boss was placed in at least one `c5280`-model
arena. The `Fallback:` line only appears when a `[[fallback_arena_maps]]`
map was patched. Phase-slot warnings are expected. When the moveset is
skipped, the second line is replaced by one
`Untouchable boss: moveset skipped (<reason>)` line and the repoint lines
carry only `NPCParamID`. At bootstrap, StaticModBuilder prints
two lines: `Untouchable TAE patch: retargeted 4 bullet event(s) of animation 3004 to judge 150 (beam) and 3 to judge 151 (flame nova)`
then `Untouchable TAE patch: wrote chr/c5280.anibnd.dcx`.

## Engine facts and pitfalls

Facts established in game or from the 1.17 data that this feature rests
on; each is a constraint for any change.

- **Attack counters must be registered.** `GetAttackPassedTime(anim)`
  reads 0 for an animation never registered with
  `RegistAttackTimeInterval`, and a registered counter reads large before
  the attack's first use. `SetCoolTime` registers and reads in one call
  and, with weights 100/0, is the idiomatic readiness read. A cooling
  registered attack picked with a positive weight is held by the engine
  until its interval expires (up to the act's goal life): pass 0 as
  `SetCoolTime`'s last argument and keep a movement filler in every
  bracket.
- **Never gate one attack on another that a reaction can consume**: the
  gate never opens while the player keeps hitting. One AI timer per
  concern (cooldown, hold) beats arithmetic on a shared timer.
- **SpEffect 20011450 is a one-shot.** Vanilla's far bracket gates the
  teleport on it; the c5280 TAE applies it through one-frame type-66
  events at the start of the idle, of 1020, 2300 and the arrival
  animations, and it is gone after the first teleport. A vanilla AI
  SpEffect gate may be a one-shot: check the TAE before reusing one as a
  cooldown. Ambient untouchables keep that gate.
- **Warp semantics.** Retreats warp relative to the boss
  (`ToTargetWarp(15, TARGET_SELF, dir, dist, TARGET_ENE_0)`); positions
  around the player use the `To*` directions (301010's blinks) or
  vanilla's own form, `B`/`BL`/`BR` at 0-2 m with the enemy target,
  which lands behind the player. A plain `F` at 8 m with the enemy
  target ("in front of the player") left the boss where it stood.
  `TARGET_EVENT` is an
  EMEVD-designated target: it resolves to the player once per fight
  here and reappears the boss where it stood afterwards; warp around
  `TARGET_ENE_0`. Lock-on cannot be broken from the AI (no SpEffect
  field does it): a "surprise from behind" only turns the camera, so
  retreat-and-punish is the lock-proof shape.
- **SpEffect categories.** Two effects of the same `spCategory` do not
  coexist: the lower `categoryPriority` wins, and at equal priority the
  later one replaces the earlier. Clones inherit the template's
  category; a clone that must coexist with its template goes to category
  0. A row cloned from the merged regulation carries the enemy
  randomizer's edits (nerflantern's slot): scrub what must not be
  inherited.
- **Damage-level tables are read from category 1001 only.** A resident
  category-0 copy of 5300 ships fine and changes nothing; 264 of the 267
  vanilla tables live in 1001. Since the wall rows (1001/0) outrank
  vanilla's priority 200, the table can only live in the wall row
  itself, and cannot survive the break.
- **Entity-scoped conditions in common.emevd do not see map entities.**
  A detector on a placed boss must live in the arena map's EMEVD. The
  enemy randomizer copies enemy-specific events next to every placement,
  with the entity as parameter: patching those copies is a clean
  per-instance lever, and a diagnostic banner on the condition, in a
  throwaway seed, is the fastest way to confirm what an event sees.
- **No chr resize offline.** The engine ignores MSB `Part.Scale` for chr
  parts (in-game test), NpcParam and SpEffectParam carry no size field,
  EMEVD has no resize instruction. A resize would need a runtime DLL or
  a full chr clone with a rescaled skeleton; the boss keeps its vanilla
  size.
- **A borrowed payload's SFX must be in a bundle c5280 loads.** Chr SFX
  live in per-chr bundles, which is why a boss cannot simply borrow
  another chr's attack; the common bundles are the exception. Frenzied
  Burst's SFX live in `sfxbnd_commoneffects` and Midra's Flame of Frenzy's
  in `sfxbnd_commoneffects_dlc02`, so both play on c5280 unmodified.
  Midra's own beam, whose FFX sits in `sfxbnd_c5050`, would need copying.
