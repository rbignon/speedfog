"""Behavioural tests of the Aging Untouchable battle script (Lua) run in an
embedded Lua through lupa.

The script's decision table (Goal.Activate), acts and reactions
(Goal.Interrupt) are exercised against a fake ai/goal pair whose answers
come from a small state dict (distance, behind, SpEffects, seconds since
each attack, AI timers, room around the target, random draw). The aiCommon
helpers the script calls are transcribed from the decompiled 1.17 bundle
(Common_Clear_Param, SetCoolTime); Common_Battle_Activate is replaced by a
capture of the weight table. The embedded Lua is newer than the game's
5.0, so this checks logic, not the engine's dialect (see
test_mods_src_lua_scripts.py for that).

Engine model (docs/untouchable-boss.md, "Teleport cooldown"):
GetAttackPassedTime reads 0 for an animation never registered with
RegistAttackTimeInterval (in game, 2026-09-05, an unregistered counter
left the teleport dead), a registered counter reads large before the
attack's first use (the grab fires from the start), and AI timers
(SetTimer/GetTimer) count down to 0; the swing (3001) and the teleport run
on such timers and 3001 is never registered.

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
TELEPORT_TIMER, SWING_TIMER = 10, 11  # TIMER_TELEPORT / TIMER_SWING in the script
WARP = ("ToTargetWarp", "enemy")
BURST = ("ComboTunable_SuccessAngle180", SWING_ANIM)  # Jori's wind-up wrapper
MELEE_SWING = ("ComboAttackTunableSpin", SWING_ANIM)  # Act11
GRAB_ATTACK = ("ComboAttackTunableSpin", GRAB_ANIM)
BEAM_ATTACK = ("ComboAttackTunableSpin", BEAM_ANIM)
BURST_ARGS = [
    8,
    SWING_ANIM,
    "enemy",
    999,
    0,
    180,
    180,
    180,
]  # fires whatever the player's side

PRELUDE = r"""
TARGET_SELF, TARGET_ENE_0, TARGET_EVENT = "self", "enemy", "event"
AI_DIR_TYPE_F, AI_DIR_TYPE_B, AI_DIR_TYPE_L, AI_DIR_TYPE_R = "F", "B", "L", "R"
AI_DIR_TYPE_BL, AI_DIR_TYPE_BR = "BL", "BR"
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
    local ai = { registered = {}, unregistered_reads = {} }
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
    -- Room around the target for the warp scan: a number for every
    -- direction (3 passes every branch), or a table keyed by direction.
    function ai:GetExistMeshOnLineDistEx(target, dir, dist, width, offset)
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
TELEPORT_COOLING = {TELEPORT_TIMER: 2}  # timer running, but past the teleport hold
TELEPORT_IN_FLIGHT = {TELEPORT_TIMER: 5}  # timer just started: inside the teleport hold


def load(state: dict, script_override: dict[str, str] | None = None):
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(PRELUDE)
    source = SCRIPT.read_text(encoding="utf-8")
    for old, new in (script_override or {}).items():
        assert source.count(old) == 1, old
        source = source.replace(old, new)
    lua.execute(source)
    g = lua.globals()
    lua_state = lua.table(
        dist=state.get("dist", 2),
        random=state.get("random", 1),
        behind=state.get("behind", False),
        interrupt=state.get("interrupt"),
        mesh=lua.table_from(state["mesh"])
        if isinstance(state.get("mesh"), dict)
        else state.get("mesh", 3),
        speffects=lua.table_from(dict.fromkeys(state.get("speffects", []), True)),
        passed=lua.table_from(state.get("passed", {})),
        timers=lua.table_from(state.get("timers", {})),
    )
    return g, g.new_ai(lua_state), g.new_goal(), lua_state


def activate(state: dict, script_override: dict[str, str] | None = None):
    """Runs Goal.Activate once, as the engine does before any act or interrupt."""
    g, ai, goal, lua_state = load(state, script_override)
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


def act(name: str, script_override: dict[str, str] | None = None, **state):
    """Runs one act after Goal.Activate; returns the goal and the Lua state."""
    g, ai, goal, lua_state = activate(state, script_override)
    g[name](ai, goal, None)
    return goal, lua_state


def react(**state) -> tuple[bool, list[tuple[str, object]]]:
    """Whether Goal.Interrupt handled the state's interrupt, and the sub-goals it left queued."""
    g, ai, goal, _ = activate(state)
    fired = g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
    return bool(fired), queued(goal)


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


def test_teleport_act_is_burst_warp_grab_and_starts_both_timers():
    goal, state = act("Houzuki755890_Act02", dist=2)
    assert queued(goal) == [BURST, WARP, GRAB_ATTACK]
    assert args_of(goal, 0) == BURST_ARGS
    assert args_of(goal, 2) == [
        8,
        GRAB_ANIM,
        "enemy",
        12,
        2,
        50,
        0,
        0,
    ]  # vanilla Act03's grab
    assert state.timers[TELEPORT_TIMER] > 0 and state.timers[SWING_TIMER] > 0


def test_teleport_act_skips_the_grab_while_it_cools():
    goal, _ = act("Houzuki755890_Act02", dist=2, passed={GRAB_ANIM: 1})
    assert queued(goal) == [BURST, WARP]


def test_teleport_act_bursts_even_while_the_swing_timer_runs():
    goal, _ = act("Houzuki755890_Act02", dist=2, timers={SWING_TIMER: 3})
    assert queued(goal)[0] == BURST


def test_teleport_act_without_room_queues_nothing_and_only_starts_the_teleport_timer():
    goal, state = act("Houzuki755890_Act02", dist=2, mesh=0)
    assert queued(goal) == []
    assert state.timers[TELEPORT_TIMER] > 0
    assert state.timers[SWING_TIMER] is None  # the burst is not spent


def test_teleport_grab_knob_off_stops_after_the_warp():
    goal, _ = act(
        "Houzuki755890_Act02",
        script_override={"local TELEPORT_GRAB = 1 ": "local TELEPORT_GRAB = 0 "},
        dist=2,
    )
    assert queued(goal) == [BURST, WARP]


def test_warp_scan_follows_vanilla_order_and_distances():
    # Every direction free: vanilla's first branch (front check, behind-right, 0 m).
    goal, _ = act("Houzuki755890_Act02", dist=2)
    assert args_of(goal, 1) == [15, "enemy", "BR", 0, "enemy"]
    # Only straight behind free: the last branch, 2 m behind.
    goal, _ = act("Houzuki755890_Act02", dist=2, mesh={"B": 3})
    assert args_of(goal, 1) == [15, "enemy", "B", 2, "enemy"]


def test_grab_act_and_swing_act_keep_their_vanilla_parameters():
    goal, _ = act("Houzuki755890_Act03", dist=2)
    assert queued(goal) == [GRAB_ATTACK]
    assert args_of(goal, 0) == [8, GRAB_ANIM, "enemy", 12, 2, 50, 0, 0]
    goal, state = act("Houzuki755890_Act11", dist=2)
    assert queued(goal) == [MELEE_SWING]
    assert args_of(goal, 0) == [8, SWING_ANIM, "enemy", 4, 1.5, 60, 0, 0]
    assert state.timers[SWING_TIMER] > 0


def test_vanilla_warp_marker_interrupt_follows_the_same_rule():
    warp_state = {
        "interrupt": "ActivateSpecialEffect",
        "speffects": [WARP_MARKER_SPEFFECT],
        "dist": 1,
    }
    fired, q = react(**warp_state)
    assert fired and q == [("ToTargetWarp", "event"), BURST]
    fired, q = react(**warp_state, timers=SWING_COOLING)
    assert fired and q == [("ToTargetWarp", "event")]


def test_retreat_act_adds_the_beam_only_when_ready():
    goal, _ = act("Houzuki755890_Act05", dist=2)
    assert [s.kind for s in goal.subgoals.values()] == [
        "LeaveTarget",
        "ComboAttackTunableSpin",
    ]
    goal, _ = act("Houzuki755890_Act05", dist=2, passed={BEAM_ANIM: 1})
    assert [s.kind for s in goal.subgoals.values()] == ["LeaveTarget"]


def test_hit_reaction_prefers_the_teleport_then_the_burst():
    fired, q = react(interrupt="Damaged", dist=1.5, random=1)
    assert fired and q[0] == BURST and WARP in q
    fired, q = react(interrupt="Damaged", dist=1.5, random=1, timers=TELEPORT_COOLING)
    assert fired and q == [BURST]
    assert not react(
        interrupt="Damaged",
        dist=1.5,
        random=1,
        timers={**TELEPORT_COOLING, **SWING_COOLING},
    )[0]
    assert not react(interrupt="Damaged", dist=3, random=1)[0]
    assert not react(interrupt="Damaged", dist=1.5, random=100)[0]


def test_no_reaction_while_a_teleport_or_a_grab_is_in_flight():
    assert not react(
        interrupt="Damaged", dist=1.5, random=1, timers=TELEPORT_IN_FLIGHT
    )[0]
    assert not react(interrupt="UseItem", dist=8, random=1, passed={GRAB_ANIM: 2})[0]
    # Past the teleport hold the reactions are back even though the timer still runs.
    assert react(interrupt="Damaged", dist=1.5, random=1, timers=TELEPORT_COOLING)[0]


def test_ranged_reactions_need_range_and_prefer_the_teleport():
    fired, q = react(interrupt="Shoot", dist=8, random=1)
    assert fired and q == [BURST, WARP, GRAB_ATTACK]
    # Teleport cooling but out of its hold: the beam stands in.
    fired, q = react(interrupt="Shoot", dist=8, random=1, timers=TELEPORT_COOLING)
    assert fired and q == [BEAM_ATTACK]
    assert not react(
        interrupt="Shoot",
        dist=8,
        random=1,
        timers=TELEPORT_COOLING,
        passed={BEAM_ANIM: 1},
    )[0]
    # No room behind the player: the beam stands in as well, and the burst
    # is not spent on it.
    g, ai, goal, state = activate(
        {"interrupt": "Shoot", "dist": 8, "random": 1, "mesh": 0}
    )
    assert g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal) and queued(goal) == [
        BEAM_ATTACK
    ]
    assert state.timers[SWING_TIMER] is None
    assert not react(interrupt="Shoot", dist=3, random=1)[0]
    fired, q = react(interrupt="UseItem", dist=8, random=1)
    assert fired and q == [BEAM_ATTACK]
    assert not react(interrupt="UseItem", dist=3, random=1)[0]
