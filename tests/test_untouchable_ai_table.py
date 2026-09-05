"""Behavioural tests of the Aging Untouchable battle script (Lua) run in an
embedded Lua through lupa.

The script's decision table (Goal.Activate) and reactions (Goal.Interrupt)
are exercised against a fake ai/goal pair whose answers come from a small
state dict (distance, behind, SpEffects, seconds since each attack, random
draw). The aiCommon helpers the script calls are transcribed from the
decompiled 1.17 bundle (Common_Clear_Param, SetCoolTime); Common_Battle_Activate
is replaced by a capture of the weight table. The embedded Lua is newer
than the game's 5.0, so this checks logic, not the engine's dialect (see
test_mods_src_lua_scripts.py for that).

Engine model of the attack counters (docs/untouchable-boss.md, "Teleport
cooldown"): GetAttackPassedTime reads 0 for an animation never registered
with RegistAttackTimeInterval (in game, 2026-09-05, an unregistered 3000
counter left the teleport dead), and a registered counter reads large
before the attack's first use (the grab fires from the start).

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
TELEPORT_ANIM, SWING_ANIM, GRAB_ANIM, BEAM_ANIM = 3000, 3001, 3002, 3004
GATE_SPEFFECT = 20011450
TELEPORTING_SPEFFECT = 20011453

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
    function ai:GetExistMeshOnLineDistEx(target, dir, dist, width, offset) return 3 end
    function ai:RegistAttackTimeInterval(animId, interval)
        self.registered[animId] = interval
        return interval
    end
    return ai
end
function new_goal()
    local goal = { subgoals = {}, cleared = 0 }
    function goal:AddSubGoal(kind, life, third, ...)
        table.insert(self.subgoals, { kind = kind, anim = third, args = { life, third, ... } })
    end
    function goal:ClearSubGoal() self.cleared = self.cleared + 1 end
    return goal
end
"""

ALL_ATTACKS_COOLING = {SWING_ANIM: 1, GRAB_ANIM: 1, BEAM_ANIM: 1}


def load(state: dict):
    lua = LuaRuntime(unpack_returned_tuples=True)
    lua.execute(PRELUDE)
    lua.execute(SCRIPT.read_text(encoding="utf-8"))
    g = lua.globals()
    lua_state = lua.table(
        dist=state.get("dist", 2),
        random=state.get("random", 1),
        behind=state.get("behind", False),
        interrupt=state.get("interrupt"),
        speffects=lua.table_from(dict.fromkeys(state.get("speffects", []), True)),
        passed=lua.table_from(state.get("passed", {})),
    )
    return g, g.new_ai(lua_state), g.new_goal(), lua_state


def weights(**state) -> tuple[dict[int, float], Any]:
    """Positive weights of the decision table for the given state, and the fake ai."""
    g, ai, goal, _ = load(state)
    g.GOALS[BATTLE_GOAL].Activate(None, ai, goal)
    probs = g.CAPTURED
    return {i: probs[i] for i in range(1, 51) if probs[i] and probs[i] > 0}, ai


def react(**state) -> tuple[bool, list[tuple[str, object]]]:
    """Whether Goal.Interrupt handled the state's interrupt, and the sub-goals it queued.

    Goal.Activate runs first, as in the engine (the battle goal activates
    before any interrupt), so the counters it registers are in place.
    """
    g, ai, goal, _ = load(state)
    g.GOALS[BATTLE_GOAL].Activate(None, ai, goal)
    fired = g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
    return bool(fired), [(s.kind, s.anim) for s in goal.subgoals.values()]


def test_melee_gap_offers_the_teleport_next_to_the_sidestep():
    w, _ = weights(dist=2, passed=ALL_ATTACKS_COOLING)
    assert w.get(GRAB, 0) == 0 and w.get(SWING, 0) == 0
    assert w.get(TELEPORT, 0) > 0
    assert w.get(SIDESTEP, 0) > 0


def test_mid_range_gap_offers_the_teleport_next_to_the_strafe():
    w, _ = weights(dist=6, passed=ALL_ATTACKS_COOLING)
    assert w.get(GRAB, 0) == 0 and w.get(BEAM, 0) == 0
    assert w.get(TELEPORT, 0) > 0
    assert w.get(STRAFE, 0) > 0


def test_teleport_has_its_own_cooldown_and_the_grab_takes_the_difference():
    ready, _ = weights(dist=2)
    cooling, _ = weights(dist=2, passed={TELEPORT_ANIM: 1})
    assert ready[TELEPORT] > 0
    assert cooling.get(TELEPORT, 0) == 0
    assert cooling[GRAB] == ready[GRAB] + ready[TELEPORT]


def test_teleport_ignores_the_vanilla_one_shot_gate_speffect():
    # With vanilla's 20011450 check the boss teleported once per fight; the
    # boss's teleport follows its own cooldown and the SpEffect changes nothing.
    without, _ = weights(dist=2, speffects=[])
    with_gate, _ = weights(dist=2, speffects=[GATE_SPEFFECT])
    assert without.get(TELEPORT, 0) > 0
    assert without == with_gate


def test_every_counter_the_script_reads_is_registered():
    _, ai = weights(dist=2)
    for anim in (TELEPORT_ANIM, SWING_ANIM, GRAB_ANIM, BEAM_ANIM):
        assert ai.registered[anim] is not None, anim


def test_no_counter_is_read_before_its_registration():
    g, ai, goal, state = load({"dist": 8, "speffects": [20011452]})
    g.GOALS[BATTLE_GOAL].Activate(None, ai, goal)
    for dist in (8, 1.5):
        state.dist = dist
        for interrupt in ("ActivateSpecialEffect", "Damaged", "Shoot", "UseItem"):
            state.interrupt = interrupt
            g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
    g.Houzuki755890_Act05(ai, goal, None)
    g.Houzuki755890_Act06(ai, goal, None)
    assert list(ai.unregistered_reads.keys()) == []


def test_swing_cooldown_zeroes_the_swing_act():
    w, _ = weights(dist=2, passed={SWING_ANIM: 1})
    assert w.get(SWING, 0) == 0
    assert w.get(GRAB, 0) > 0


def test_post_warp_swing_only_when_the_swing_is_ready():
    warp_state = {
        "interrupt": "ActivateSpecialEffect",
        "speffects": [20011452],
        "dist": 1,
    }
    fired, queued = react(**warp_state)
    assert fired and queued == [
        ("ToTargetWarp", "event"),
        ("ComboAttackTunableSpin", SWING_ANIM),
    ]
    fired, queued = react(**warp_state, passed={SWING_ANIM: 1})
    assert fired and queued == [("ToTargetWarp", "event")]
    # The vanilla surprise-swing parameters, not Act11's (successDist 4, turn 1.5/60).
    g, ai, goal, _ = load(warp_state)
    g.GOALS[BATTLE_GOAL].Activate(None, ai, goal)
    g.GOALS[BATTLE_GOAL].Interrupt(None, ai, goal)
    swing = list(goal.subgoals.values())[-1]
    assert list(swing.args.values()) == [8, SWING_ANIM, "enemy", 999, 0, 0]


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
    w, _ = weights(passed={TELEPORT_ANIM: 1, **ALL_ATTACKS_COOLING}, **state)
    assert sum(w.values()) > 0


def test_behind_at_close_range_offers_the_teleport_when_ready():
    ready, _ = weights(dist=4, behind=True)
    assert ready[TELEPORT] > 0 and ready[TURN] > 0 and ready[APPROACH] > 0
    assert ready[TURN] == 4 * ready[APPROACH]  # the rest keeps vanilla's 20:80 split
    cooling, _ = weights(dist=4, behind=True, passed={TELEPORT_ANIM: 1})
    assert cooling.get(TELEPORT, 0) == 0
    assert cooling[APPROACH] == 20 and cooling[TURN] == 80


def test_far_bracket_teleport_or_beam():
    ready, _ = weights(dist=12)
    assert set(ready) == {TELEPORT, BEAM} and ready[TELEPORT] + ready[BEAM] == 100
    cooling, _ = weights(dist=12, passed={TELEPORT_ANIM: 1})
    assert set(cooling) == {APPROACH, BEAM} and cooling[APPROACH] + cooling[BEAM] == 100


def test_retreat_act_adds_the_beam_only_when_ready():
    g, ai, goal, _ = load({"dist": 2})
    g.GOALS[BATTLE_GOAL].Activate(
        None, ai, goal
    )  # registers the counters, as in the engine
    g.Houzuki755890_Act05(ai, goal, None)
    assert [s.kind for s in goal.subgoals.values()] == [
        "LeaveTarget",
        "ComboAttackTunableSpin",
    ]
    g, ai, goal, _ = load({"dist": 2, "passed": {BEAM_ANIM: 1}})
    g.GOALS[BATTLE_GOAL].Activate(
        None, ai, goal
    )  # registers the counters, as in the engine
    g.Houzuki755890_Act05(ai, goal, None)
    assert [s.kind for s in goal.subgoals.values()] == ["LeaveTarget"]


def test_hit_reaction_swings_only_when_ready_close_and_drawn():
    fired, queued = react(interrupt="Damaged", dist=1.5, random=1)
    assert fired and queued == [("ComboAttackTunableSpin", SWING_ANIM)]
    assert not react(interrupt="Damaged", dist=1.5, random=1, passed={SWING_ANIM: 1})[0]
    assert not react(interrupt="Damaged", dist=3, random=1)[0]
    assert not react(interrupt="Damaged", dist=1.5, random=100)[0]


def test_no_reaction_while_a_teleport_or_a_grab_is_in_flight():
    assert not react(
        interrupt="Damaged", dist=1.5, random=1, passed={TELEPORT_ANIM: 2}
    )[0]
    assert not react(interrupt="UseItem", dist=8, random=1, passed={GRAB_ANIM: 2})[0]
    # 20011453 is applied for 4 s at the start of 3000 (TAE): teleporting.
    assert not react(
        interrupt="Shoot", dist=8, random=1, speffects=[TELEPORTING_SPEFFECT]
    )[0]


def test_ranged_reactions_need_range_and_prefer_the_teleport():
    fired, queued = react(interrupt="Shoot", dist=8, random=1)
    assert fired and queued[0][1] == TELEPORT_ANIM
    # 9 s after 3000: ready for the cooldown (> TELEPORT_COOLDOWN) but still in
    # flight (<= REACT_HOLD), so the hold blocks the reaction, not the cooldown.
    assert not react(interrupt="Shoot", dist=8, random=1, passed={TELEPORT_ANIM: 9})[0]
    assert not react(interrupt="Shoot", dist=3, random=1)[0]
    fired, queued = react(interrupt="UseItem", dist=8, random=1)
    assert fired and queued == [("ComboAttackTunableSpin", BEAM_ANIM)]
    assert not react(interrupt="UseItem", dist=3, random=1)[0]
