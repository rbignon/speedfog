# Battle AI scripts

What SpeedFog knows about Elden Ring's Lua AI, as far as the Aging
Untouchable boss script uses it (`docs/untouchable-boss.md`, "AI
script"). Everything below comes from the decompiled 1.17 `aicommon`
bundle and vanilla battle scripts, or from the boss script's own in-game
runs; where a goal or a query is engine-native, its parameters are named
as vanilla scripts pass them and their meaning is inferred from usage,
which the text says each time.

## Where a script comes from

- `NpcThinkParam` selects the scripts: `logicId` names
  `script/<logicId>_logic.luabnd.dcx` (70 in the game, most NPCs sharing
  a generic one) and `battleGoalID` names
  `script/<battleGoalID>_battle.luabnd.dcx` (396). A per-script luabnd
  holds the compiled Lua (Lua 5.0 bytecode; the engine also loads plain
  text) and the script's own `.luagnl`, the list of its global names
  (its act functions, `GetWellSpace_Odds`).
- `script/aicommon.luabnd.dcx`, `aicommon_dlc01.luabnd.dcx` and
  `aicommon_dlc02.luabnd.dcx` are the shared library: the goal wrappers
  and helpers (`common_func_plan`, `common_battle_func`,
  `common_logic_func`), `ai_define` (constants), `goal_list` (goal ids),
  the `.luainfo` files, some seventy numbered vanilla scripts embedded
  there rather than shipped alone (472000 and 450000 among them), and
  the `.luagnl` files that carry the `GOAL_<name>` globals of every
  vanilla script (`aiCommon.luagnl` for most, `aiCommon_DLC02.luagnl`
  for the rest, the untouchable's `GOAL_Houzuki528000_*` included).
- The logic script runs the perception states (caution, search, battle)
  and starts the battle goal: `_COMMON_AddBattleGoal` reads
  `battleGoalID` from the think row
  (`ai:GetExcelParam(AI_EXCEL_THINK_PARAM_TYPE__battleGoalID)`) and calls
  `ai:AddTopGoal(goalId, -1)`. The battle goal is therefore keyed by its
  numeric id; the logic script never names it.
- A battle script registers its goals with `RegisterTableGoal(id,
  "Name")`, which calls `REGISTER_GOAL(id, name)` and binds a fresh
  `Goal` table to `g_GoalTable[id]`; the `Goal.*` functions assigned right
  after belong to that goal, until the next `RegisterTableGoal`. Every
  vanilla battle goal also passes `REGISTER_GOAL_NO_SUB_GOAL(id, true)`
  and the aicommon wrappers `REGISTER_GOAL_NO_INTERUPT(id, true)`:
  engine-side flags whose meaning is not established.
- The `GOAL_<name>` globals of vanilla scripts are not defined by the
  scripts themselves but by the aicommon `.luagnl` files, which only know
  the vanilla names. A custom script defines its own (`GOAL_X_Battle` equal
  to the think row's `battleGoalID`, any unused id for a second goal).

## Goal life cycle

A goal is a table of callbacks; the engine drives it:

- `Goal.Initialize(self, ai, goal, battleActivatedCount)`: once, when
  the goal is created (the boss enables the unfavorable-attack check
  here).
- `Goal.Activate(self, ai, goal)`: each time the goal starts or restarts.
  A battle goal builds its act table here and queues sub-goals.
- `Goal.Update(self, ai, goal)`: every tick; returns `GOAL_RESULT_Continue`
  (0), `GOAL_RESULT_Success` (1) or `GOAL_RESULT_Failed` (-1).
  `Update_Default_NoSubGoal` returns Success as soon as no sub-goal
  remains, so the battle goal ends and the engine restarts it (observed;
  the restart is engine-side): a new `Activate`, a new act.
- `Goal.Terminate(self, ai, goal)`: when the goal ends.
- `Goal.Interrupt(self, ai, goal)`: on events (see "Interrupts");
  returns true when it handled one.

Sub-goals are the goal's queue: `goal:AddSubGoal(GOAL_ID, life,
params...)` appends one, run in order; `goal:ClearSubGoal()` drops the
queue (the running sub-goal included); `goal:GetSubGoalNum()` counts
it. Inside a goal's own callbacks, `goal:GetLife()` and
`goal:GetParam(i)` read that goal's life and `AddSubGoal` arguments (the
aicommon wrappers read theirs this way). `life` is
in seconds: a sub-goal ends when it succeeds, fails or outlives it. A
registered attack that is still cooling (see `SetCoolTime`) is held by
the engine until its interval expires, up to that life, so a queued
cooling attack reads as a frozen character.

## The act table

`Goal.Activate` of a vanilla battle script is a decision table:

```lua
local probabilities, acts, paramTbls = {}, {}, {}
Common_Clear_Param(probabilities, acts, paramTbls)   -- weights 0, acts nil, 1..50 (third argument ignored)
-- one bracket per situation, weights written by hand
if ai:IsInsideTarget(TARGET_ENE_0, AI_DIR_TYPE_B, 90) then ... -- the enemy behind
elseif ai:GetDist(TARGET_ENE_0) >= 10 then probabilities[2] = 100 ...
probabilities[3] = SetCoolTime(ai, goal, 3002, 12, probabilities[3], 1)
acts[1] = REGIST_FUNC(ai, goal, X_Act01)   -- closure over (ai, goal)
...
Common_Battle_Activate(ai, goal, probabilities, acts, actAfter, paramTbls)
```

`Common_Battle_Activate` sums the fifty weights, draws
`ai:GetRandam_Int(1, sum)` and runs the act whose cumulative weight
covers the draw (a debugger override, `ai:DbgGetForceActIdx`, can force
one). Weights are relative: "percentages" only when a bracket sums to
100. What a bracket that sums to zero does is not established (it hinges
on `ai:GetRandam_Int(1, 0)`), which is why the boss keeps every bracket
positive. An act with a weight
but no function runs `errorAct` (`GOAL_COMMON_ErrorNotification`).

An act function `X_ActNN(ai, goal, paramTbl)` queues its sub-goals and
returns `GetWellSpace_Odds` (0-100; a global by vanilla habit, listed in
the script's own luagnl): the chance that `actAfter` runs
next, a follow-up such as `ActAfter_AdjustSpace` queuing an
after-attack goal shaped by the `Init_AfterAttackAct` string-indexed
numbers (`DistMin_AAA`, `Odds_Backstep_AAA`...). Every act of the boss
script returns 0.

`SetCoolTime(ai, goal, animId, interval, weight, coolingWeight)` is the
cooldown idiom: it registers the attack's interval
(`ai:RegistAttackTimeInterval(animId, interval)`, whose return value the
helper takes as the interval; inferred), returns 0 when `weight` is 0 or less, `coolingWeight` when the
attack's counter (`ai:GetAttackPassedTime(animId)`) is still within the
interval, and `weight` otherwise. Vanilla passes 1 as `coolingWeight` in
about two thirds of its calls (a bracket never sums to zero) and 0 in a
quarter; called with weights 100/0 it is a plain readiness read. A
counter is only meaningful after registration: unregistered, it reads 0
(silently "cooling" for a `<=` test, silently "long ago" for a `>=`
one); registered, it reads large before the attack's first use.

`Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist,
runProbability, guardProbability, walkLife, runLife)` is the approach
helper most acts start with: run when the target is at
`forceRunMinDist` or more, or at `canRunDist` or more with a draw at or
under `runProbability`, walk otherwise (lives `walkLife` / `runLife`,
defaults 3 / 8); guard state 9910 with a draw at or under
`guardProbability`; then, only
if the target is at `stopDist` or more, `GOAL_COMMON_ApproachTarget` to
`stopDist` plus the `AddDistWalk` / `AddDistRun` string-indexed numbers.

## Attack goals

The three wrappers the boss uses are Lua (`aicommon`) and end in the
native `GOAL_COMMON_CommonAttack` (2200):

- `GOAL_COMMON_ComboAttackTunableSpin` (2221): `AddSubGoal(id, life,
  animId, target, successDist, turnTime, turnFaceAngle,
  upAngleThreshold, downAngleThreshold)`, success angle 90.
- `GOAL_COMMON_ComboTunable_SuccessAngle180` (2253): the same with
  success angle 180, so the attack fires whatever the target's side.
- `GOAL_COMMON_ComboRepeat_SuccessAngle180` (2231): the repeat variant, used
  for the throw's follow-up (3003) with `(animId, target, successDist,
  turnTime, turnFaceAngle)`.

Both wrappers default `turnTime` to 1.5 s and `turnFaceAngle` to 20
degrees when negative, and pass `comboAttack`, `moveCancel` and
`attackCancel` true, `guardBreakAttack` and `noTurn` false. The
parameters, as vanilla uses them (engine-native, inferred):

| Parameter | Meaning |
|-----------|---------|
| `animId` | the attack animation (3001 = `a000_003001`), also its EzState id |
| `target` | whom to attack and turn to, usually `TARGET_ENE_0` |
| `successDist` | the attack is launched only when the target is within this distance; 999 = always, 4 = melee reach, 12 = the grab's dash |
| success angle | the target must be within this angle of the facing direction (90 or 180) |
| `turnTime` | seconds allowed to turn toward the target before the attack starts |
| `turnFaceAngle` | the turn is considered done when the target is within this angle |
| `upAngleThreshold`, `downAngleThreshold` | vertical limits, 0 = none |

Observed in game: the goal hands over to the next sub-goal at the
animation's cancel window (the boss's near teleport warps 1.0 s into
its burst), and an attack queued while its registered interval runs is
held rather than skipped.

## Movement goals

Engine-native; parameters as vanilla scripts pass them, meanings
inferred from usage:

| Goal (id) | Parameters | Behaviour |
|-----------|-----------|-----------|
| `GOAL_COMMON_ApproachTarget` (2015) | `life, moveTarget, stopDist, turnTarget, walk, guardStateId[, onGuardResult, guardSuccessOnEnd, xzDistanceOnly]` | close in until `stopDist` |
| `GOAL_COMMON_LeaveTarget` (2016) | `life, moveTarget, distance, turnTarget, walk, guardStateId[, onGuardResult]` | back away until `distance`, facing `turnTarget` |
| `GOAL_COMMON_SidewayMove` (2017) | `life, moveTarget, right (0 left, 1 right), angleThreshold, isWalk, successOnEnd, guardStateId[, resultTypeIfGuardSuccess]` | strafe around the target |
| `GOAL_COMMON_Turn` (2001) | `life, turnTarget, stopAngleWidth, guardStateId, onGuardResult, guardSuccessOnEnd` | turn in place until the target is within the width |
| `GOAL_COMMON_StepSafety` (2602) | `life, frontPriority, backPriority, leftPriority, rightPriority, target, distSpaceCheck, turnTime, alwaysSuccess` | a step in the best-ranked direction (1 wanted, -1 excluded) that has `distSpaceCheck` of room |
| `GOAL_COMMON_Wait` (2000) | `life, target` | stand facing the target |
| `GOAL_COMMON_ToTargetWarp` (2037) | `life, target, direction, distance, turnTarget[, ...]` | warp `distance` in `direction` relative to `target`, facing `turnTarget` |

`ToTargetWarp` is the one whose semantics needed in-game runs (see
"Pitfalls"): relative to `TARGET_SELF` with a plain direction it is a
retreat (Rennala's 203100, 301010's retreats); relative to `TARGET_ENE_0`
with `AI_DIR_TYPE_B`/`BL`/`BR` and 0-2 m it lands behind the player
(vanilla's untouchable); the `To*` directions place the character around
the player (301010's blinks); a plain `F` with the enemy target left the
boss where it stood. The goal plays no animation: vanilla wraps it in an
animation whose marker SpEffect triggers the warp from `Goal.Interrupt`.
Guard state 9910 is the guard EzState the helpers pass with a
`guardProbability` draw; -1 disables it.

## Queries on `ai`

Engine-native. As used by the boss script and the aicommon helpers:

| Call | Meaning |
|------|---------|
| `ai:GetDist(target)` | distance to the target, metres, centre to centre |
| `ai:IsInsideTarget(target, dir, angle)` | the target lies in a cone of `angle` degrees opened in `dir` from self (whether full or half angle is not established; vanilla's "enemy behind" test is `(TARGET_ENE_0, AI_DIR_TYPE_B, 90)`) |
| `ai:IsInsideTargetCustom(subject, target, dir, angle, angle2, dist)` | the target within `dist` of `subject` inside a cone in `dir` (`(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 120, 180, 2)` = the enemy within 2 m in front; the second angle's role is not established) |
| `ai:GetRandam_Int(lo, hi)`, `ai:GetRandam_Float(lo, hi)` | draws, inclusive |
| `ai:HasSpecialEffectId(target, id)`, `ai:HasSpecialEffectAttribute(target, attr)` | active SpEffect by id or by `stateInfo` attribute (`SP_EFFECT_TYPE_ILLNESS`) |
| `ai:RegistAttackTimeInterval(animId, seconds)`, `ai:GetAttackPassedTime(animId)` | the attack counters (see `SetCoolTime`) |
| `ai:SetTimer(slot, seconds)`, `ai:GetTimer(slot)` | AI timers counting down to 0; the shared library uses slots 12-15 (`common_logic_func`, `common_func_plan`), slot 10 is set by 34 vanilla battle scripts, `GetTimer(n) <= 0` is the readiness gate |
| `ai:GetMapHitRadius(target)` | the character's collision radius, the line width of navmesh scans |
| `ai:IsExistMeshOnLine(target, dir, dist)` | navmesh along a line from the target's position |
| `ai:GetExistMeshOnLineDistEx(target, dir, dist, width, offset)` | how much of that line (`width` wide, `offset` sideways) has navmesh: the room for a step or a warp |
| `ai:GetExcelParam(AI_EXCEL_THINK_PARAM_TYPE__field)` | a `NpcThinkParam` field of the character |
| `ai:GetStringIndexedNumber(name)`, `ai:SetStringIndexedNumber(name, value)` | named scratch values (`Init_Pseudo_Global`) |
| `ai:EnableUnfavorableAttackCheck(0, animId)` | lets the engine veto that attack when unfavorable (grabs) |
| `ai:IsLadderAct(target)` | on a ladder |

## Interrupts

`Goal.Interrupt` runs when the engine raises an event; the script asks
`ai:IsInterupt(kind)` which one and answers with the vanilla pattern:
`goal:ClearSubGoal()`, queue the answer, `return true`. Returning false
leaves the running sub-goals alone. Kinds the boss handles:
`INTERUPT_Damaged` (2, took damage), `INTERUPT_UseItem` (12) and
`INTERUPT_ActivateSpecialEffect` (43, a watched SpEffect was applied).
Two more are raised and deliberately left unhandled: `INTERUPT_Shoot`
(10, the target starts a cast or a shot), whose omission is a design
decision explained in `docs/untouchable-boss.md`, "Interrupts", and
`INTERUPT_FindAttack` (1, the target starts an attack), which feeds the
aicommon step and guard helpers (`FindAttack_Step`, `FindAttack_Guard`)
the boss does not use.

Watches are declared in `Goal.Activate`:
`ai:AddObserveSpecialEffectAttribute(TARGET_SELF, id)` raises
`INTERUPT_ActivateSpecialEffect` when `id` is applied to the character,
read back with `ai:GetSpecialEffectActivateInterruptId(id)` or
`ai:HasSpecialEffectId`; this is how a TAE marker inside an animation
(the untouchable's 20011452 at 4.77 s of 3000, the grab's 5030 on a
connect) hands control back to the script mid-animation.
`ai:AddObserveAreaCustom(...)` declares an area watch with the arguments
vanilla passes (`0, TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 30, 180,
1`); its interrupt is not used by the boss.

## Constants (`ai_define`)

- Targets: `TARGET_SELF` -1, `TARGET_ENE_0` 0 (the current enemy),
  `TARGET_NONE` -2, `TARGET_EVENT` 20 (an EMEVD-designated target, see
  "Pitfalls"). `TARGET_ENE0` (no underscore) appears in vanilla
  decompiles and is undefined (nil).
- Directions: `AI_DIR_TYPE_CENTER` 0, `F` 1, `B` 2, `L` 3, `R` 4, `ToF` 5,
  `ToB` 6, `ToL` 7, `ToR` 8, `Top` 9, `FL` 10, `FR` 11, `BL` 12, `BR` 13,
  `ToFL` 14, `ToFR` 15, `ToBL` 16, `ToBR` 17. The plain ones are relative
  to the reference character's facing; the `To*` ones point at the
  target.
- Results: `GOAL_RESULT_Failed` -1, `Continue` 0, `Success` 1;
  `GUARD_GOAL_DESIRE_RET_Continue` 2; `AI_CALC_DIST_TYPE__XYZ` 0;
  `DIST_Near` -1, `DIST_Middle` -2, `DIST_Far` -3, `DIST_None` -5.
- Goal ids used: `GOAL_COMMON_Wait` 2000, `Turn` 2001, `ApproachTarget`
  2015, `LeaveTarget` 2016, `SidewayMove` 2017, `ToTargetWarp` 2037,
  `CommonAttack` 2200, `ComboAttackTunableSpin` 2221,
  `ComboRepeat_SuccessAngle180` 2231, `ComboTunable_SuccessAngle180`
  2253, `StepSafety` 2602.

## Tooling

- Unpack a luabnd with WitchyBND (`--passive
  Game/script/<id>_battle.luabnd.dcx`): the bytecode `.lua` and the
  script's own `.luagnl`. The boss luabnd ships the plain-text `.lua`
  alone.
- Decompile the bytecode with DSLuaDecompiler (nex3 fork, at
  `/data/thewall/DSLuaDecompiler`): locals come out as `fN_localM`
  except where the goal parameter names are known (`successDist`,
  `turnTime`...). The aicommon bundle decompiles the same way.
- Ship plain-text Lua in the WitchyBND-unpacked layout under
  `data/mods-src/speedfog/script/<id>_battle-luabnd-dcx/`;
  `tools/bootstrap.py` repacks it (`data/mods-src/README.md`, which also
  gives the repack alone). A seed ships the built luabnd, so `uv run
  speedfog` refuses to build one while that file is older than its
  source. The engine
  compiles Lua 5.0: `tests/test_mods_src_lua_scripts.py` rejects the
  syntax added since (`#`, `%`, `//`, the bitwise operators, `goto` and
  labels); library additions (`select`, newer `string` functions) are
  not caught.
- Test a script off-line with lupa: `tests/test_untouchable_ai_table.py`
  transcribes `SetCoolTime` and `Common_Clear_Param`, captures
  `Common_Battle_Activate`'s table and fakes `ai`/`goal` (distance,
  side, SpEffects, counters, timers, navmesh per direction, draws,
  interrupt kind), so the decision table, the queued sub-goals and the
  reactions are checked per state before any in-game run.
- Survey vanilla behaviour by unpacking and decompiling all 396 battle
  scripts and grepping the idiom in question (this is how the
  `SetCoolTime` weight and the unregistered-counter reads were settled).

## Pitfalls

Rules the boss script follows, established in game or by survey
(evidence in `docs/untouchable-boss.md`, "Engine facts and pitfalls"):

- Register an attack counter before reading it; `SetCoolTime` with
  weights 100/0 is the idiomatic read.
- Pass 0 as `SetCoolTime`'s cooling weight and keep a movement filler
  in every bracket, or the engine holds the character on a cooling
  attack for the act's life.
- Never gate one attack on another that a reaction can consume.
- One AI timer per concern (a cooldown, a hold), no arithmetic on a
  shared timer.
- A vanilla SpEffect gate may be a one-shot (the untouchable's
  20011450): read the TAE before reusing one as a cooldown.
- Warp retreats relative to `TARGET_SELF`; place around the player with
  the `To*` directions or vanilla's behind-the-player scan; never
  assume a plain direction with the enemy target. `TARGET_EVENT` is not
  a stable player reference.
- Lock-on cannot be broken from the AI: a "surprise from behind" only
  turns the camera.
- A `ClearSubGoal` in a reaction drops whatever is in flight (a warp, a
  follow-up throw): hold the reactions while a sequence runs.
- Lua 5.0 allows 32 upvalues per function: every file-scope local a
  function reads (knobs, named ids) is one, nested closures included,
  and the engine's compiler then rejects the whole script (the boss
  falls back to aiCommon's errorAct and stands still).
  `tests/test_mods_src_lua_scripts.py` counts them; the lupa harness
  (Lua 5.5) would not notice.
