"""Behavioural tests of the Aging Untouchable battle script (Lua) run in an
embedded Lua through lupa.

The script's decision table (Goal.Activate), acts and reactions
(Goal.Interrupt) are exercised against a fake ai/goal pair whose answers
come from a small state dict (distance, behind, SpEffects, seconds since
each attack, AI timers, room around the scanned character, random draw).
The aiCommon helpers the script calls are transcribed from the decompiled
1.17 bundle (Common_Clear_Param, SetCoolTime); Common_Battle_Activate is
replaced by a capture of the weight table. The embedded Lua is newer than
the game's 5.0, so this checks logic, not the engine's dialect (see
test_mods_src_lua_scripts.py for that).

Engine model (docs/untouchable-boss.md, "AI script"): GetAttackPassedTime
reads 0 for an animation never registered with RegistAttackTimeInterval
(in game, 2026-09-05, an unregistered counter left the teleport dead), a
registered counter reads large before the attack's first use (the grab
fires from the start), and AI timers (SetTimer/GetTimer) count down to 0;
the swing (3001), the teleport and the reaction hold run on such timers
and 3001 is never registered.

Weights are indexed by act number as in the script: Act01 approach, Act02
teleport, Act03 grab, Act04 beam, Act11 swing, Act42 sidestep, Act43 turn,
Act46 close-and-strafe.
"""

from pathlib import Path
from typing import Any

import pytest
from lupa import LuaRuntime

SCRIPT = (
    Path(__file__).resolve().parents[1]
    / "data/mods-src/speedfog/script/755890_battle-luabnd-dcx/755890_battle.lua"
)
SCRIPT_SOURCE = SCRIPT.read_text(encoding="utf-8")
BATTLE_GOAL = 755890
APPROACH, TELEPORT, GRAB, BEAM, SWING, SIDESTEP, TURN, STRAFE = (
    1,
    2,
    3,
    4,
    11,
    42,
    43,
    46,
)
SWING_ANIM, GRAB_ANIM, BEAM_ANIM = 3001, 3002, 3004
GATE_SPEFFECT = 20011450  # vanilla's one-shot teleport gate, ignored by the boss
WARP_MARKER_SPEFFECT = 20011452  # vanilla's post-3000 warp marker
HOLD_TIMER, TELEPORT_TIMER, SWING_TIMER = 9, 10, 11  # TIMER_* slots in the script
BURST = ("ComboTunable_SuccessAngle180", SWING_ANIM)  # Jori's wind-up wrapper
MELEE_SWING = ("ComboAttackTunableSpin", SWING_ANIM)  # Act11
GRAB_ATTACK = ("ComboAttackTunableSpin", GRAB_ANIM)
BEAM_ATTACK = ("ComboAttackTunableSpin", BEAM_ANIM)
FAR_WINDUP = ("ComboTunable_SuccessAngle180", 3000)  # vanilla Act02's 5 s teleport-out
RETREAT = ("ToTargetWarp", "self")  # the near warp, relative to the boss itself
WARP_BEHIND = ("ToTargetWarp", "enemy")  # the far warp, around the player
BURST_ARGS = [8, SWING_ANIM, "enemy", 999, 0, 180, 180, 180]  # fires whatever the side
WAIT = ("Wait", "enemy")  # the beat between the warp and the beam


def retreat_args(direction: str = "B", distance: float = 8) -> list:
    """ToTargetWarp arguments of the near warp: from the boss, facing the player."""
    return [15, "self", direction, distance, "enemy"]


def warp_behind_args(direction: str, distance: float) -> list:
    """ToTargetWarp arguments of the far warp: around the player, facing them."""
    return [15, "enemy", direction, distance, "enemy"]


PRELUDE = r"""
TARGET_SELF, TARGET_ENE_0, TARGET_EVENT = "self", "enemy", "event"
AI_DIR_TYPE_F, AI_DIR_TYPE_B, AI_DIR_TYPE_L, AI_DIR_TYPE_R = "F", "B", "L", "R"
AI_DIR_TYPE_BL, AI_DIR_TYPE_BR, AI_DIR_TYPE_FL, AI_DIR_TYPE_FR = "BL", "BR", "FL", "FR"
AI_EXCEL_THINK_PARAM_TYPE__thinkAttr_doAdmirer = 0
SP_EFFECT_TYPE_ILLNESS = "illness"
INTERUPT_ActivateSpecialEffect = "ActivateSpecialEffect"
INTERUPT_Damaged, INTERUPT_Shoot, INTERUPT_UseItem = "Damaged", "Shoot", "UseItem"
GOAL_COMMON_ComboAttackTunableSpin = "ComboAttackTunableSpin"
GOAL_COMMON_ComboTunable_SuccessAngle180 = "ComboTunable_SuccessAngle180"
GOAL_COMMON_ComboRepeat_SuccessAngle180 = "ComboRepeat_SuccessAngle180"
GOAL_COMMON_LeaveTarget, GOAL_COMMON_ToTargetWarp = "LeaveTarget", "ToTargetWarp"
GOAL_COMMON_ApproachTarget, GOAL_COMMON_SidewayMove = "ApproachTarget", "SidewayMove"
GOAL_COMMON_Turn, GOAL_COMMON_StepSafety, GOAL_COMMON_Wait = "Turn", "StepSafety", "Wait"

GOALS = {}
function RegisterTableGoal(id, name)
    Goal = {}
    GOALS[id] = Goal
end
function REGISTER_GOAL_NO_SUB_GOAL(id, flag) end
function REGIST_FUNC(ai, goal, fn) return fn end
function Init_Pseudo_Global(ai, goal) end
function Approach_Act_Flex(ai, goal, ...) end
function Update_Default_NoSubGoal(self, ai, goal) end

-- Transcribed from the decompiled aicommon bundle.
function Common_Clear_Param(probabilities, acts)
    for i = 1, 50 do
        probabilities[i] = 0
        acts[i] = nil
    end
end
function SetCoolTime(ai, goal, animId, coolTime, probability, coolingWeight)
    coolTime = ai:RegistAttackTimeInterval(animId, coolTime)
    if probability <= 0 then
        return 0
    elseif ai:GetAttackPassedTime(animId) <= coolTime then
        return coolingWeight
    end
    return probability
end
CAPTURED = nil
function Common_Battle_Activate(ai, goal, probabilities, acts, actAfter, paramTbls)
    CAPTURED = probabilities
end

function new_ai(state)
    local ai = { registered = {}, unregistered_reads = {}, scanned = {} }
    function ai:GetDist(target) return state.dist end
    function ai:GetRandam_Int(lo, hi) return state.random end
    function ai:GetRandam_Float(lo, hi) return lo end
    function ai:GetExcelParam(kind) return 0 end
    function ai:GetMapHitRadius(target) return 1 end
    function ai:AddObserveAreaCustom(...) end
    function ai:AddObserveSpecialEffectAttribute(target, id) end
    function ai:EnableUnfavorableAttackCheck(...) end
    function ai:IsInsideTarget(target, dir, angle)
        return dir == AI_DIR_TYPE_B and state.behind == true
    end
    function ai:IsInsideTargetCustom(subject, target, dir, angle, angle2, dist)
        return dir == AI_DIR_TYPE_F and state.dist <= dist
    end
    function ai:HasSpecialEffectId(target, id) return state.speffects[id] == true end
    function ai:HasSpecialEffectAttribute(target, attr) return false end
    function ai:IsLadderAct(target) return false end
    function ai:IsInterupt(kind) return state.interrupt == kind end
    function ai:GetSpecialEffectActivateInterruptId(id) return false end
    function ai:GetAttackPassedTime(animId)
        if self.registered[animId] == nil then
            self.unregistered_reads[animId] = true
            return 0
        end
        return state.passed[animId] or 1000
    end
    function ai:RegistAttackTimeInterval(animId, interval)
        self.registered[animId] = interval
        return interval
    end
    function ai:GetTimer(id) return state.timers[id] or 0 end
    function ai:SetTimer(id, seconds) state.timers[id] = seconds end
    -- Room for the warp scans: a number for every direction (10 passes
    -- every branch of both scans), or a table keyed by direction. The
    -- scanned character is recorded per call.
    function ai:GetExistMeshOnLineDistEx(target, dir, dist, width, offset)
        table.insert(self.scanned, target)
        if type(state.mesh) == "table" then
            return state.mesh[dir] or 0
        end
        return state.mesh
    end
    return ai
end
function new_goal()
    local goal = { subgoals = {}, cleared = 0 }
    function goal:AddSubGoal(kind, life, third, ...)
        table.insert(self.subgoals, { kind = kind, anim = third, args = { life, third, ... } })
    end
    function goal:ClearSubGoal()
        self.cleared = self.cleared + 1
        self.subgoals = {}
    end
    return goal
end
"""

COOLING_PASSED = {GRAB_ANIM: 1, BEAM_ANIM: 1}  # registered counters just used
SWING_COOLING = {SWING_TIMER: 1}
TELEPORT_COOLING = {TELEPORT_TIMER: 2}  # teleport timer running, hold timer expired
TELEPORT_HOLDING = {TELEPORT_TIMER: 5, HOLD_TIMER: 2}  # a teleport in flight


def load(state: dict):
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(PRELUDE)
    lua.execute(SCRIPT_SOURCE)
    g = lua.globals()
    mesh = state.get("mesh", 10)
    lua_state = lua.table(
        dist=state.get("dist", 2),
        random=state.get("random", 1),
        behind=state.get("behind", False),
        interrupt=state.get("interrupt"),
        mesh=lua.table_from(mesh) if isinstance(mesh, dict) else mesh,
        speffects=lua.table_from(dict.fromkeys(state.get("speffects", []), True)),
        passed=lua.table_from(state.get("passed", {})),
        timers=lua.table_from(state.get("timers", {})),
    )
    return g, g.new_ai(lua_state), g.new_goal(), lua_state


def activate(state: dict):
    """Runs Goal.Activate once, as the engine does before any act or interrupt."""
    g, ai, goal, lua_state = load(state)
    g.GOALS[BATTLE_GOAL].Activate(None, ai, goal)
    return g, ai, goal, lua_state


def weights(**state) -> tuple[dict[int, float], Any]:
    """Positive weights of the decision table for the given state, and the fake ai."""
    g, ai, _, _ = activate(state)
    probs = g.CAPTURED
    return {i: probs[i] for i in range(1, 51) if probs[i] and probs[i] > 0}, ai


def queued(goal) -> list[tuple[str, object]]:
    return [(s.kind, s.anim) for s in goal.subgoals.values()]


def args_of(goal, index: int) -> list:
    return list(list(goal.subgoals.values())[index].args.values())


def act(name: str, **state) -> tuple[Any, Any, Any]:
    """Runs one act (by its short name, Act02) after Goal.Activate; returns
    the goal, the fake ai and the Lua state."""
    g, ai, goal, lua_state = activate(state)
    g["Houzuki755890_" + name](ai, goal, None)
    return goal, ai, lua_state


def react(**state) -> tuple[bool, Any, Any]:
    """Runs Goal.Interrupt after Goal.Activate; returns whether it handled
    the state's interrupt, the goal and the fake ai."""
    g, ai, goal, _ = activate(state)
    fired = g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
    return bool(fired), goal, ai


def test_melee_gap_offers_the_teleport_next_to_the_sidestep():
    w, _ = weights(dist=2, passed=COOLING_PASSED, timers=SWING_COOLING)
    assert w.get(GRAB, 0) == 0 and w.get(SWING, 0) == 0
    assert w.get(TELEPORT, 0) > 0
    assert w.get(SIDESTEP, 0) > 0


def test_mid_range_gap_offers_the_teleport_next_to_the_strafe():
    w, _ = weights(dist=6, passed=COOLING_PASSED, timers=SWING_COOLING)
    assert w.get(GRAB, 0) == 0 and w.get(BEAM, 0) == 0
    assert w.get(TELEPORT, 0) > 0
    assert w.get(STRAFE, 0) > 0


def test_teleport_has_its_own_timer_and_the_grab_takes_the_difference():
    ready, _ = weights(dist=2)
    cooling, _ = weights(dist=2, timers=TELEPORT_COOLING)
    assert ready[TELEPORT] > 0
    assert cooling.get(TELEPORT, 0) == 0
    assert cooling[GRAB] == ready[GRAB] + ready[TELEPORT]


def test_teleport_ignores_the_vanilla_one_shot_gate_speffect():
    # With vanilla's 20011450 check the boss teleported once per fight; the
    # boss's teleport follows its own timer and the SpEffect changes nothing.
    without, _ = weights(dist=2, speffects=[])
    with_gate, _ = weights(dist=2, speffects=[GATE_SPEFFECT])
    assert without.get(TELEPORT, 0) > 0
    assert without == with_gate


def test_registered_counters_are_the_grab_and_the_beam_only():
    _, ai = weights(dist=2)
    assert ai.registered[GRAB_ANIM] is not None and ai.registered[BEAM_ANIM] is not None
    # The burst runs on a timer: no engine interval can ever hold it.
    assert ai.registered[SWING_ANIM] is None


def test_no_counter_is_read_before_its_registration():
    g, ai, goal, state = activate({"dist": 8, "speffects": [WARP_MARKER_SPEFFECT]})
    for dist in (8, 1.5):
        state.dist = dist
        # "Shoot" is included precisely because it is ignored: the branch
        # that answered it is gone, and no counter may be read on the way out.
        for interrupt in ("ActivateSpecialEffect", "Damaged", "Shoot", "UseItem"):
            state.interrupt = interrupt
            g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
        for name in ("Act02", "Act03", "Act05", "Act06", "Act11"):
            g["Houzuki755890_" + name](ai, goal, None)
    assert list(ai.unregistered_reads.keys()) == []


def test_swing_timer_zeroes_the_swing_act():
    w, _ = weights(dist=2, timers=SWING_COOLING)
    assert w.get(SWING, 0) == 0
    assert w.get(GRAB, 0) > 0


@pytest.mark.parametrize(
    "state",
    [
        {"dist": 1},
        {"dist": 2.9},
        {"dist": 5},
        {"dist": 12},
        {"dist": 4, "behind": True},
        {"dist": 12, "behind": True},
    ],
    ids=str,
)
def test_every_bracket_keeps_a_positive_weight_while_everything_cools(state):
    w, _ = weights(
        passed=COOLING_PASSED, timers={**SWING_COOLING, **TELEPORT_COOLING}, **state
    )
    assert sum(w.values()) > 0


def test_behind_at_close_range_offers_the_teleport_when_ready():
    ready, _ = weights(dist=4, behind=True)
    assert ready[TELEPORT] > 0 and ready[TURN] > 0 and ready[APPROACH] > 0
    assert ready[TURN] == 4 * ready[APPROACH]  # the rest keeps vanilla's 20:80 split
    cooling, _ = weights(dist=4, behind=True, timers=TELEPORT_COOLING)
    assert cooling.get(TELEPORT, 0) == 0
    assert cooling[APPROACH] == 20 and cooling[TURN] == 80


def test_far_bracket_teleport_or_beam():
    ready, _ = weights(dist=12)
    assert set(ready) == {TELEPORT, BEAM} and ready[TELEPORT] + ready[BEAM] == 100
    cooling, _ = weights(dist=12, timers=TELEPORT_COOLING)
    assert set(cooling) == {APPROACH, BEAM} and cooling[APPROACH] + cooling[BEAM] == 100


def test_near_teleport_is_burst_retreat_wait_beam_and_starts_the_three_timers():
    goal, _, state = act("Act02", dist=2)
    assert queued(goal) == [BURST, RETREAT, WAIT, BEAM_ATTACK]
    # The wind-up keeps vanilla's goal life: the warp waits for the burst to
    # release the character, and the beat the player reads sits after it.
    assert args_of(goal, 0) == BURST_ARGS
    assert args_of(goal, 1) == retreat_args()
    assert args_of(goal, 2)[0] == 1.0
    assert state.timers[TELEPORT_TIMER] == 6
    # The hold covers wind-up, arrival, beat and beam.
    assert state.timers[HOLD_TIMER] == pytest.approx(4.5)
    assert state.timers[SWING_TIMER] > 0


def test_near_teleport_skips_the_beam_and_its_beat_while_it_cools():
    # No beam to telegraph, no reason to stand still after the warp, and the
    # hold covers the shorter sequence rather than a second of nothing.
    goal, _, state = act("Act02", dist=2, passed={BEAM_ANIM: 1})
    assert queued(goal) == [BURST, RETREAT]
    assert state.timers[HOLD_TIMER] == pytest.approx(3.5)


def test_near_teleport_bursts_even_while_the_swing_timer_runs():
    goal, _, _ = act("Act02", dist=2, timers={SWING_TIMER: 3})
    assert queued(goal)[0] == BURST


def test_near_teleport_without_room_queues_nothing_and_retries_soon():
    goal, _, state = act("Act02", dist=2, mesh=0)
    _, _, with_room = act("Act02", dist=2)
    assert queued(goal) == []
    # No sequence in flight: the burst is not spent, no hold, and the
    # teleport timer restarts short of the full cooldown so a cramped spot
    # is retried soon rather than burning the whole interval.
    assert 0 < state.timers[TELEPORT_TIMER] < with_room.timers[TELEPORT_TIMER]
    assert state.timers[HOLD_TIMER] is None
    assert state.timers[SWING_TIMER] is None


def test_far_teleport_plays_vanilla_3000_and_holds_the_timers_longer():
    goal, _, state = act("Act02", dist=12)
    assert queued(goal) == [FAR_WINDUP]
    assert args_of(goal, 0) == [
        10,
        3000,
        "enemy",
        1003,
        0,
        0,
        0,
        0,
    ]  # 5 - hit radius 1 + 999
    assert state.timers[SWING_TIMER] is None  # the burst comes with the warp, later
    # The cooldown and the hold validated in game (2026-09-06).
    assert state.timers[TELEPORT_TIMER] == 11.5
    assert state.timers[HOLD_TIMER] == pytest.approx(9)


def test_teleport_variant_switches_at_the_far_range():
    goal, _, _ = act("Act02", dist=5)
    assert queued(goal) == [FAR_WINDUP]
    goal, _, _ = act("Act02", dist=4.9)
    assert queued(goal)[0] == BURST


def test_retreat_scan_is_behind_the_boss_itself():
    # Every direction free: straight back, 8 m, relative to the boss, and
    # the room is scanned from the boss, never from the player.
    goal, ai, _ = act("Act02", dist=2)
    assert args_of(goal, 1) == retreat_args()
    assert set(ai.scanned.values()) == {"self"}
    # Only behind-left free, then only behind-right free: the branch order.
    goal, _, _ = act("Act02", dist=2, mesh={"BL": 10})
    assert args_of(goal, 1) == retreat_args("BL")
    goal, _, _ = act("Act02", dist=2, mesh={"BR": 10})
    assert args_of(goal, 1) == retreat_args("BR")
    # Room only in front or to the sides is no retreat.
    goal, _, _ = act("Act02", dist=2, mesh={"F": 10, "L": 10, "R": 10})
    assert queued(goal) == []
    # The player in the boss's back: the retreat goes forward, away from them.
    goal, _, _ = act("Act02", dist=2, behind=True)
    assert args_of(goal, 1) == retreat_args("F")
    goal, _, _ = act("Act02", dist=2, behind=True, mesh={"FR": 10, "B": 10})
    assert args_of(goal, 1) == retreat_args("FR")
    # 7 m of room is not enough for an 8 m retreat: the 5 m fallback (small arenas).
    goal, _, _ = act("Act02", dist=2, mesh=7)
    assert queued(goal) == [BURST, RETREAT, WAIT, BEAM_ATTACK]
    assert args_of(goal, 1) == retreat_args("B", 5)
    goal, _, _ = act("Act02", dist=2, mesh=4)
    assert queued(goal) == []


def test_grab_act_and_swing_act_keep_their_vanilla_parameters():
    goal, _, _ = act("Act03", dist=2)
    assert queued(goal) == [GRAB_ATTACK]
    assert args_of(goal, 0) == [8, GRAB_ANIM, "enemy", 12, 2, 50, 0, 0]
    goal, _, state = act("Act11", dist=2)
    assert queued(goal) == [MELEE_SWING]
    assert args_of(goal, 0) == [8, SWING_ANIM, "enemy", 4, 1.5, 60, 0, 0]
    assert state.timers[SWING_TIMER] > 0


def test_far_teleport_second_half_warps_behind_the_player_with_vanillas_scan():
    marker = {
        "interrupt": "ActivateSpecialEffect",
        "speffects": [WARP_MARKER_SPEFFECT],
        "dist": 1,
    }
    # Vanilla's scan around the player (TARGET_ENE_0, not vanilla's event
    # target, which only served the fight's first teleport): every direction
    # free gives its first branch (front check, behind-right, 0 m).
    fired, goal, ai = react(**marker)
    assert fired and queued(goal) == [WARP_BEHIND, BURST]
    assert args_of(goal, 0) == warp_behind_args("BR", 0)
    assert set(ai.scanned.values()) == {"enemy"}
    fired, goal, _ = react(**marker, timers=SWING_COOLING)
    assert fired and queued(goal) == [WARP_BEHIND]
    # Only straight behind free: 2 m behind.
    _, goal, _ = react(**marker, mesh={"B": 3})
    assert args_of(goal, 0) == warp_behind_args("B", 2)
    # No room: as in vanilla, nothing is cleared and 3000 finishes on its own.
    fired, goal, _ = react(**marker, mesh=0)
    assert fired and queued(goal) == [] and goal.cleared == 0


@pytest.mark.parametrize("name", ["Act05", "Act06"])
def test_retreat_acts_are_the_near_teleport_when_it_is_ready(name):
    # After a grab the boss retreats: the burst, the warp away and the beam
    # when the teleport timer allows, the vanilla walk otherwise.
    goal, _, state = act(name, dist=2)
    assert queued(goal) == [BURST, RETREAT, WAIT, BEAM_ATTACK]
    assert state.timers[TELEPORT_TIMER] > 0 and state.timers[HOLD_TIMER] > 0
    goal, _, _ = act(name, dist=2, timers=TELEPORT_COOLING)
    assert [s.kind for s in goal.subgoals.values()] == [
        "LeaveTarget",
        "ComboAttackTunableSpin",
    ]
    goal, _, _ = act(name, dist=2, timers=TELEPORT_COOLING, passed={BEAM_ANIM: 1})
    assert [s.kind for s in goal.subgoals.values()] == ["LeaveTarget"]
    # No room to warp: the walk, and the teleport retried soon.
    goal, _, state = act(name, dist=2, mesh=0)
    assert [s.kind for s in goal.subgoals.values()] == [
        "LeaveTarget",
        "ComboAttackTunableSpin",
    ]
    assert 0 < state.timers[TELEPORT_TIMER] < 6
    assert state.timers[HOLD_TIMER] is None


def test_close_bracket_offers_the_beam_next_to_the_grab():
    ready, _ = weights(dist=2)
    assert ready[BEAM] > 0 and ready[GRAB] > 0
    assert sum(ready.values()) == 100
    cooling, _ = weights(dist=2, passed={BEAM_ANIM: 1})
    assert cooling.get(BEAM, 0) == 0
    assert cooling[GRAB] == ready[GRAB]  # the beam's share is not redistributed


def test_hit_reaction_prefers_the_retreat_then_the_burst():
    hit = {"interrupt": "Damaged", "dist": 1.5, "random": 1}
    fired, goal, _ = react(**hit)
    assert fired and queued(goal) == [BURST, RETREAT, WAIT, BEAM_ATTACK]
    fired, goal, _ = react(**hit, timers=TELEPORT_COOLING)
    assert fired and queued(goal) == [BURST]
    # No room to retreat: the burst alone.
    fired, goal, _ = react(**hit, mesh=0)
    assert fired and queued(goal) == [BURST]
    # No room and the swing cooling: nothing, and the running act is not cleared.
    fired, goal, _ = react(**hit, mesh=0, timers=SWING_COOLING)
    assert not fired and goal.cleared == 0
    assert not react(**hit, timers={**TELEPORT_COOLING, **SWING_COOLING})[0]
    assert not react(interrupt="Damaged", dist=3, random=1)[0]
    assert not react(interrupt="Damaged", dist=1.5, random=100)[0]


def test_no_reaction_while_a_teleport_or_a_grab_is_in_flight():
    hit = {"interrupt": "Damaged", "dist": 1.5, "random": 1}
    flask = {"interrupt": "UseItem", "dist": 8, "random": 1}
    assert not react(**hit, timers=TELEPORT_HOLDING)[0]
    assert not react(**flask, timers=TELEPORT_HOLDING)[0]
    assert not react(**flask, passed={GRAB_ANIM: 2})[0]  # its own chain in flight
    assert not react(**hit, passed={GRAB_ANIM: 2})[0]  # the hit reaction too
    # The hold, not the grab cooldown, is what suppresses them: 6.5 s is past
    # GRAB_COOLDOWN (6) and still inside REACT_HOLD (7).
    assert not react(**flask, passed={GRAB_ANIM: 6.5})[0]
    assert react(**flask, passed={GRAB_ANIM: 7.5})[0]
    # Both fire once nothing is in flight.
    assert react(**flask)[0]
    # Past the hold the reactions are back even though the teleport timer still runs.
    assert react(**hit, timers=TELEPORT_COOLING)[0]


def test_flask_reaction_draws_the_grab_within_its_reach():
    # Drinking is punished by the grab's dash, not by the beam: the boss
    # closes on the player rather than chipping them.
    fired, goal, _ = react(interrupt="UseItem", dist=8, random=1)
    assert fired and queued(goal) == [GRAB_ATTACK]
    assert args_of(goal, 0) == [8, GRAB_ANIM, "enemy", 12, 2, 50, 0, 0]
    assert not react(interrupt="UseItem", dist=3, random=1)[0]
    # The draw is REACT_HEAL (80), not the hit reaction's 25.
    assert react(interrupt="UseItem", dist=8, random=50)[0]
    assert not react(interrupt="UseItem", dist=8, random=100)[0]


def test_flask_reaction_beyond_the_grabs_reach_draws_the_beam():
    # The grab cannot launch past its successDist, and an attack the engine
    # cannot start holds the boss: past that reach the beam punishes instead.
    fired, goal, _ = react(interrupt="UseItem", dist=12, random=1)
    assert fired and queued(goal) == [GRAB_ATTACK]
    fired, goal, _ = react(interrupt="UseItem", dist=12.1, random=1)
    assert fired and queued(goal) == [BEAM_ATTACK]
    fired, goal, _ = react(interrupt="UseItem", dist=25, random=1)
    assert fired and queued(goal) == [BEAM_ATTACK]
    # Neither answer available: the running act is left alone.
    fired, goal, _ = react(
        interrupt="UseItem", dist=25, random=1, passed={BEAM_ANIM: 1}
    )
    assert not fired and goal.cleared == 0


def test_grab_readiness_reads_the_grab_cooldown():
    # The reaction cannot show it (REACT_HOLD 7 covers GRAB_COOLDOWN 6, so a
    # cooling grab always reads as a chain in flight first), but the helper is
    # what keeps a cooling grab out of the queue if either knob moves.
    g, ai, goal, _ = activate({"dist": 8, "passed": {GRAB_ANIM: 1}})
    assert not g["Houzuki755890_GrabReady"](ai, goal)
    g, ai, goal, _ = activate({"dist": 8, "passed": {GRAB_ANIM: 100}})
    assert g["Houzuki755890_GrabReady"](ai, goal)


def test_the_boss_never_reads_an_attack_input():
    # A cast or a shot starting is the player's input, not a consequence:
    # answering it reads as unfair, so the boss ignores the interrupt at
    # every distance, and leaves the running act alone.
    for dist in (2, 8, 12):
        fired, goal, _ = react(interrupt="Shoot", dist=dist, random=1)
        assert not fired and goal.cleared == 0
