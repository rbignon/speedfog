-- SpeedFog: Aging Untouchable boss battle script (docs/untouchable-boss.md,
-- "AI script"). Decompiled from the vanilla 528000_battle.lua with
-- DSLuaDecompiler and renamed to battle goal 755890 (the boss NpcThinkParam
-- clone's battleGoalID). SpeedFog additions: the lantern swing (3001) as a
-- regular melee act (Act11), the dormant lantern ray (3004) re-enabled as a
-- beam (Act04), a two-shape teleport (Act02: near, the burst, a warp away
-- and the beam; far, vanilla's 3000, the warp behind the player and the
-- burst) offered at every range with its own cooldown, a beam after the
-- post-grab retreats (Act05/Act06) and three reactions in Goal.Interrupt
-- (hit, ranged attack, item use). Ambient untouchables keep the vanilla
-- bytecode script.
-- The engine keys goal tables by numeric id and starts the battle goal with
-- the raw NpcThinkParam.battleGoalID; the GOAL_<name> globals of vanilla
-- scripts come from the shared aiCommon global-name list, which does not
-- know these names, so they are assigned here (755890 must equal the boss
-- think row's battleGoalID; 755891 only needs to be unique).
GOAL_Houzuki755890_Battle = 755890
GOAL_Houzuki755890_AfterAttackAct = 755891
RegisterTableGoal(GOAL_Houzuki755890_Battle, "Houzuki755890_Battle")
REGISTER_GOAL_NO_SUB_GOAL(GOAL_Houzuki755890_Battle, true)

-- SpeedFog tuning knobs. Weights are percentages within a distance
-- bracket; the vanilla act of the bracket (grab at melee range, teleport
-- or approach at range) keeps the remainder. A cooling attack weighs 0
-- (SetCoolTime or the *_Ready helpers in Goal.Activate), so the remainder
-- grows while attacks cool, and every bracket keeps a movement filler with
-- a positive weight. Design and engine facts: docs/untouchable-boss.md,
-- "AI script" and "Engine facts and pitfalls".
local BEAM_FAR_TELEPORT_READY = 40      -- >= 10 m, teleport ready: Act04 (beam) vs Act02
local BEAM_FAR_TELEPORT_NOT_READY = 50  -- >= 10 m, teleport not ready: Act04 (beam) vs Act01
local SWING_MID = 10                    -- 3 to 10 m: Act11 (swing, radius-4 knockback burst)
local TELEPORT_MID = 15                 -- 3 to 10 m: Act02
local BEAM_MID = 10                     -- 3 to 10 m: Act04 (beam)
local MOVE_MID = 15                     -- 3 to 10 m: Act46 (close to 4 m, then strafe)
local SWING_CLOSE = 15                  -- < 3 m: Act11 (swing)
local TELEPORT_CLOSE = 10               -- < 3 m: Act02
local MOVE_CLOSE = 20                   -- < 3 m: Act42 (sidestep)
local TELEPORT_BEHIND = 50              -- player behind, < 8 m: Act02 when ready (0-90; Act01 keeps 10, Act43 takes the rest)
local GRAB_COOLDOWN = 6                 -- seconds between two grabs (3002); vanilla 12
local SWING_COOLDOWN = 8                -- seconds between two bursts (3001), any source (Act11, reaction, teleport wind-up)
local BEAM_COOLDOWN = 6                 -- seconds between two beams (3004), any source
local TELEPORT_COOLDOWN = 6             -- seconds after a near teleport before any teleport
local TELEPORT_FAR_COOLDOWN = 11.5      -- seconds after a far teleport before any teleport (its own sequence takes about 9 s)
local TELEPORT_FAR_RANGE = 5            -- from this distance (centre to centre; melee reach with a long weapon is 3-4 m) the teleport is vanilla's (3000, the warp behind the player, the burst); closer, the burst, a warp away and the beam
local TELEPORT_AWAY_DIST = 8            -- near variant: warp this far away from the player, relative to the boss itself
local TELEPORT_AWAY_FALLBACK = 5        -- near variant: second scan at this distance when nothing clears TELEPORT_AWAY_DIST (small arenas)
-- Animation spans (c5280 TAE event spans); the holds derive from them.
local BURST_CANCEL = 1.0                -- 3001's cancel window, where the warp fires
local BURST_LENGTH = 1.8
local ARRIVAL_MAX = 2.2                 -- the longest arrival animation (5012/5013)
local MARKER_3000 = 4.77                -- 3000's warp marker (SpEffect 20011452)
local GRAB_CHAIN = 6                    -- 3002 (4.2 s) then 3003 (1.8 s)
-- Reactions (Goal.Interrupt), percentages, 0 disables one. A reaction
-- spends an attack that is available anyway, so it moves an attack earlier
-- without adding any: hits landed while the swing cools stay free.
local REACT_HIT = 25                    -- hit by the player in front within REACT_HIT_RANGE: the near teleport if ready, else the burst
local REACT_HIT_RANGE = 2
local REACT_SHOOT = 50                  -- player casts or shoots from >= REACT_RANGE: beam
local REACT_HEAL = 80                   -- player uses an item from >= REACT_RANGE: beam
local REACT_RANGE = 5
-- No reaction while a teleport or a grab sequence is in flight (a
-- reaction's ClearSubGoal would drop the follow-up): the hold timer, set
-- with each teleport attempt, and the 3002 counter for the grab. The
-- margins round the holds to the windows validated in game (3.5 s, 9 s).
local TELEPORT_HOLD = BURST_CANCEL + ARRIVAL_MAX + 0.3                     -- near: 3.5 s
local TELEPORT_FAR_HOLD = MARKER_3000 + ARRIVAL_MAX + BURST_LENGTH + 0.23  -- far: 9 s
local REACT_HOLD = GRAB_CHAIN + 1                                          -- seconds after a grab (3002) starts
-- AI timer slots (vanilla 528000 uses none; the shared logic scripts use 13-15).
local TIMER_TELEPORT = 10
local TIMER_SWING = 11
local TIMER_HOLD = 9

-- Availability. The grab and the beam are engine counters registered by
-- SetCoolTime (the vanilla helper; a counter never registered reads 0, so
-- Goal.Activate registers both before any act or reaction runs); the
-- swing and the teleport are AI timers (SetTimer/GetTimer, the vanilla
-- idiom), so 3001 is never registered and no engine interval can hold the
-- boss on a burst. A cooling attack is never queued.
function Houzuki755890_SwingReady(ai)
    return ai:GetTimer(TIMER_SWING) <= 0
end

function Houzuki755890_BeamReady(ai, goal)
    return SetCoolTime(ai, goal, 3004, BEAM_COOLDOWN, 100, 0) > 0
end

function Houzuki755890_TeleportReady(ai)
    return ai:GetTimer(TIMER_TELEPORT) <= 0
end

function Houzuki755890_SequenceInFlight(ai)
    return ai:GetTimer(TIMER_HOLD) > 0 or ai:GetAttackPassedTime(3002) <= REACT_HOLD
end

-- Vanilla's post-3000 scan around the player (in front, then behind
-- right/left at 0 or 2 m, then behind at 2 m), kept verbatim for the far
-- teleport's second half: the warp direction and distance, or nil.
function Houzuki755890_FindRoomBehind(ai)
    local lineWidth = ai:GetMapHitRadius(TARGET_SELF)
    if ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_F, 3 + lineWidth, lineWidth, 0) >= 2.5 then
        return AI_DIR_TYPE_BR, 0
    elseif ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_BR, 3 + lineWidth, lineWidth, 0) >= 2.5 then
        return AI_DIR_TYPE_BR, 0
    elseif ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_BL, 3 + lineWidth, lineWidth, 0) >= 2.5 then
        return AI_DIR_TYPE_BL, 0
    elseif ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_BL, 3 + lineWidth, lineWidth, 2) >= 2.5 then
        return AI_DIR_TYPE_BL, 2
    elseif ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_BR, 3 + lineWidth, lineWidth, 2) >= 2.5 then
        return AI_DIR_TYPE_BR, 2
    elseif ai:GetExistMeshOnLineDistEx(TARGET_ENE_0, AI_DIR_TYPE_B, 3 + lineWidth, lineWidth, 2) >= 2.5 then
        return AI_DIR_TYPE_B, 2
    end
    return nil
end

-- Room from the boss itself in one direction (203100's scan, then a
-- five-argument TARGET_SELF warp); the line width is the body that has
-- to fit.
function Houzuki755890_HasRoom(ai, direction, distance, lineWidth)
    return ai:GetExistMeshOnLineDistEx(TARGET_SELF, direction, distance + lineWidth, lineWidth, 0) >= distance
end

-- Room away from the player at the given distance: straight behind the
-- boss then behind-left/right, or forward and the front diagonals when
-- the player stands in the boss's back, so it is always a retreat and
-- never a blink through the player. The warp direction and distance, or
-- nil.
function Houzuki755890_FindRoomAway(ai, distance)
    local straight, left, right = AI_DIR_TYPE_B, AI_DIR_TYPE_BL, AI_DIR_TYPE_BR
    if ai:IsInsideTarget(TARGET_ENE_0, AI_DIR_TYPE_B, 90) then
        straight, left, right = AI_DIR_TYPE_F, AI_DIR_TYPE_FL, AI_DIR_TYPE_FR
    end
    local lineWidth = ai:GetMapHitRadius(TARGET_SELF)
    if Houzuki755890_HasRoom(ai, straight, distance, lineWidth) then
        return straight, distance
    elseif Houzuki755890_HasRoom(ai, left, distance, lineWidth) then
        return left, distance
    elseif Houzuki755890_HasRoom(ai, right, distance, lineWidth) then
        return right, distance
    end
    return nil
end

-- Sub-goal builders shared by the acts and the reactions.
-- The lantern burst (3001) and its timer. Immediate: Jori's wind-up
-- recipe (ComboTunable_SuccessAngle180, reach 999, no turn, every angle
-- 180, so it fires whatever the player's side); otherwise Act11's melee
-- parameters (reach 4 m, turn 1.5 s / 60 degrees).
function Houzuki755890_AddSwing(ai, goal, immediate)
    ai:SetTimer(TIMER_SWING, SWING_COOLDOWN)
    if immediate then
        goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 8, 3001, TARGET_ENE_0, 999, 0, 180, 180, 180)
    else
        goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 8, 3001, TARGET_ENE_0, 4, 1.5, 60, 0, 0)
    end
end

-- The grab (3002) with vanilla Act03's parameters; the 5030 observation
-- lets the interrupt chain 3003 when the grab connects.
function Houzuki755890_AddGrab(ai, goal)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5030)
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 8, 3002, TARGET_ENE_0, 12, 2, 50, 0, 0)
end

function Houzuki755890_AddBeam(ai, goal)
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 3, 3004, TARGET_ENE_0, 999, 1.5, 60, 0, 0)
end

function Houzuki755890_AddBeamIfReady(ai, goal)
    if Houzuki755890_BeamReady(ai, goal) then
        Houzuki755890_AddBeam(ai, goal)
    end
end

-- The near teleport (Act02 under TELEPORT_FAR_RANGE, the hit reaction):
-- the burst as the wind-up (its hit lands from the first frame, the warp
-- fires at its cancel window), the warp away from the player, then the
-- beam when ready: the boss bursts, retreats and fires, in the player's
-- view since lock-on cannot be broken from the AI. Returns whether it was
-- queued: without room nothing is queued or cleared (the burst is not
-- spent on a warp that cannot happen; the sub-goals are cleared only once
-- the retreat is certain, vanilla Act05/Act06's idiom), but both timers
-- start with the attempt: the spot is not retried at every decision, and
-- the reactions stay quiet for the hold as they did in the validated
-- runs.
function Houzuki755890_AddTeleportAway(ai, goal)
    ai:SetTimer(TIMER_TELEPORT, TELEPORT_COOLDOWN)
    ai:SetTimer(TIMER_HOLD, TELEPORT_HOLD)
    local direction, distance = Houzuki755890_FindRoomAway(ai, TELEPORT_AWAY_DIST)
    if direction == nil then
        direction, distance = Houzuki755890_FindRoomAway(ai, TELEPORT_AWAY_FALLBACK)
    end
    if direction == nil then
        return false
    end
    goal:ClearSubGoal()
    Houzuki755890_AddSwing(ai, goal, true)
    goal:AddSubGoal(GOAL_COMMON_ToTargetWarp, 15, TARGET_SELF, direction, distance, TARGET_ENE_0)
    Houzuki755890_AddBeamIfReady(ai, goal)
    return true
end

-- The far teleport (Act02 from TELEPORT_FAR_RANGE): vanilla Act02 as
-- decompiled, the 5 s animation 3000 with the 20011452 observation; the
-- interrupt below warps behind the player and bursts when the marker
-- fires. Its timers keep every teleport and reaction away for the whole
-- sequence.
function Houzuki755890_AddTeleportFar(ai, goal)
    ai:SetTimer(TIMER_TELEPORT, TELEPORT_FAR_COOLDOWN)
    ai:SetTimer(TIMER_HOLD, TELEPORT_FAR_HOLD)
    local successDist = 5 - ai:GetMapHitRadius(TARGET_SELF) + 999
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 20011452)
    goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 10, 3000, TARGET_ENE_0, successDist, 0, 0, 0, 0)
end

-- The beam as a ranged reaction (a cast or a flask from REACT_RANGE)
-- when nothing is in flight: the draw, then the beam if it is ready.
function Houzuki755890_ReactBeam(ai, goal, chance)
    if ai:GetDist(TARGET_ENE_0) >= REACT_RANGE and not Houzuki755890_SequenceInFlight(ai) and ai:GetRandam_Int(1, 100) <= chance and Houzuki755890_BeamReady(ai, goal) then
        goal:ClearSubGoal()
        Houzuki755890_AddBeam(ai, goal)
        return true
    end
    return false
end

Goal.Initialize = function (self, ai, goal, battleActivatedCount)
    ai:EnableUnfavorableAttackCheck(0, 3002)
end

Goal.Activate = function (self, ai, goal)
    Init_Pseudo_Global(ai, goal)
    local probabilities = {}
    local acts = {}
    local paramTbls = {}
    Common_Clear_Param(probabilities, acts, paramTbls)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local random = ai:GetRandam_Int(1, 100)
    local paramDoAdmire = ai:GetExcelParam(AI_EXCEL_THINK_PARAM_TYPE__thinkAttr_doAdmirer)
    -- SpeedFog: the teleport weights of the melee brackets are 0 while the
    -- teleport is not offered, the grab takes the difference.
    local teleportReady = Houzuki755890_TeleportReady(ai)
    local teleportMid = teleportReady and TELEPORT_MID or 0
    local teleportClose = teleportReady and TELEPORT_CLOSE or 0
    local f2_local6 = 0
    local f2_local7 = TARGET_SELF
    local f2_local8 = TARGET_ENE_0
    local f2_local9 = AI_DIR_TYPE_F
    local f2_local10 = 30
    local f2_local11 = 180
    local f2_local12 = 1
    ai:AddObserveAreaCustom(f2_local6, f2_local7, f2_local8, f2_local9, f2_local10, f2_local11, f2_local12)
    if ai:IsInsideTarget(TARGET_ENE_0, AI_DIR_TYPE_B, 90) then
        if distanceEnemy >= 8 then
            if teleportReady then
                probabilities[2] = 100
            else
                probabilities[1] = 100
            end
            probabilities[43] = 0
        elseif teleportReady then
            -- SpeedFog: vanilla only turns here (Act43); the near teleport
            -- (burst, retreat forward since the player is in the back,
            -- beam) answers a player in the back better than a slow turn.
            probabilities[1] = 10
            probabilities[2] = TELEPORT_BEHIND
            probabilities[43] = 90 - TELEPORT_BEHIND
        else
            probabilities[1] = 20
            probabilities[2] = 0
            probabilities[43] = 80
        end
    elseif ai:HasSpecialEffectId(TARGET_SELF, 5031) then
        probabilities[1] = 0
        probabilities[2] = 0
        probabilities[3] = 0
        probabilities[5] = 100
        probabilities[6] = 0
        probabilities[40] = 0
        probabilities[41] = 0
        probabilities[42] = 0
        probabilities[43] = 0
        probabilities[44] = 0
        probabilities[45] = 0
    elseif ai:HasSpecialEffectId(TARGET_SELF, 5032) then
        probabilities[1] = 0
        probabilities[2] = 0
        probabilities[3] = 0
        probabilities[5] = 0
        probabilities[6] = 100
        probabilities[40] = 0
        probabilities[41] = 0
        probabilities[42] = 0
        probabilities[43] = 0
        probabilities[44] = 0
        probabilities[45] = 0
    elseif distanceEnemy >= 10 then
        if teleportReady then
            probabilities[1] = 0
            probabilities[2] = 100 - BEAM_FAR_TELEPORT_READY
            probabilities[3] = 0
            probabilities[4] = BEAM_FAR_TELEPORT_READY
            probabilities[40] = 0
            probabilities[41] = 0
            probabilities[42] = 0
            probabilities[43] = 0
            probabilities[44] = 0
            probabilities[45] = 0
        else
            probabilities[1] = 100 - BEAM_FAR_TELEPORT_NOT_READY
            probabilities[2] = 0
            probabilities[3] = 0
            probabilities[4] = BEAM_FAR_TELEPORT_NOT_READY
            probabilities[40] = 0
            probabilities[41] = 0
            probabilities[42] = 0
            probabilities[43] = 0
            probabilities[44] = 0
            probabilities[45] = 0
        end
    elseif distanceEnemy >= 3 then
        probabilities[1] = 0
        probabilities[2] = teleportMid
        probabilities[3] = 100 - SWING_MID - teleportMid - BEAM_MID - MOVE_MID
        probabilities[4] = BEAM_MID
        probabilities[5] = 0
        probabilities[11] = SWING_MID
        probabilities[46] = MOVE_MID
        probabilities[40] = 0
        probabilities[41] = 0
        probabilities[42] = 0
        probabilities[43] = 0
        probabilities[44] = 0
        probabilities[45] = 0
    else
        probabilities[1] = 0
        probabilities[2] = teleportClose
        probabilities[3] = 100 - SWING_CLOSE - teleportClose - MOVE_CLOSE
        probabilities[4] = 0
        probabilities[5] = 0
        probabilities[11] = SWING_CLOSE
        probabilities[40] = 0
        probabilities[41] = 0
        probabilities[42] = MOVE_CLOSE
        probabilities[43] = 0
        probabilities[44] = 0
        probabilities[45] = 0
    end
    -- The last argument of SetCoolTime is the weight kept while the attack
    -- cools. Vanilla mostly passes 1 so a table never sums to zero; here every
    -- bracket keeps a movement filler, so 0: a cooling attack picked with
    -- weight 1 is held by the engine until its interval expires, up to the
    -- act's goal life, which reads as the boss freezing in place.
    probabilities[3] = SetCoolTime(ai, goal, 3002, GRAB_COOLDOWN, probabilities[3], 0)
    probabilities[4] = SetCoolTime(ai, goal, 3004, BEAM_COOLDOWN, probabilities[4], 0)
    -- The swing's and the teleport's cooldowns are AI timers read through
    -- the *Ready helpers (the swing here, the teleport inside the
    -- teleportMid and teleportClose weights above).
    if not Houzuki755890_SwingReady(ai) then
        probabilities[11] = 0
    end
    acts[1] = REGIST_FUNC(ai, goal, Houzuki755890_Act01)
    acts[2] = REGIST_FUNC(ai, goal, Houzuki755890_Act02)
    acts[3] = REGIST_FUNC(ai, goal, Houzuki755890_Act03)
    acts[4] = REGIST_FUNC(ai, goal, Houzuki755890_Act04)
    acts[5] = REGIST_FUNC(ai, goal, Houzuki755890_Act05)
    acts[6] = REGIST_FUNC(ai, goal, Houzuki755890_Act06)
    acts[7] = REGIST_FUNC(ai, goal, Houzuki755890_Act07)
    acts[8] = REGIST_FUNC(ai, goal, Houzuki755890_Act08)
    acts[9] = REGIST_FUNC(ai, goal, Houzuki755890_Act09)
    acts[10] = REGIST_FUNC(ai, goal, Houzuki755890_Act10)
    acts[11] = REGIST_FUNC(ai, goal, Houzuki755890_Act11)
    acts[40] = REGIST_FUNC(ai, goal, Houzuki755890_Act40)
    acts[41] = REGIST_FUNC(ai, goal, Houzuki755890_Act41)
    acts[42] = REGIST_FUNC(ai, goal, Houzuki755890_Act42)
    acts[43] = REGIST_FUNC(ai, goal, Houzuki755890_Act43)
    acts[44] = REGIST_FUNC(ai, goal, Houzuki755890_Act44)
    acts[45] = REGIST_FUNC(ai, goal, Houzuki755890_Act45)
    acts[46] = REGIST_FUNC(ai, goal, Houzuki755890_Act46)
    acts[47] = REGIST_FUNC(ai, goal, Houzuki755890_Act47)
    local actAfter = REGIST_FUNC(ai, goal, Houzuki755890_ActAfter_AdjustSpace)
    Common_Battle_Activate(ai, goal, probabilities, acts, actAfter, paramTbls)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5031)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5032)
end

function Houzuki755890_Act01(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 0.5
    local canRunDist = 0
    local forceRunMinDist = 0.1
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 1
    local runLife = 5
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local goalLife = 0.1
    local animationId = 2100
    local target = TARGET_ENE_0
    local successDist = 5
    local turnTime = 1.5
    local turnFaceAngle = 20
    local upAngleThreshold = 0
    local downAngleThreshold = 0
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, goalLife, animationId, target, successDist, turnTime, turnFaceAngle, upAngleThreshold, downAngleThreshold)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act02(ai, goal, paramTbl)
    -- Teleport: near, SpeedFog's lantern burst, warp away and beam; far,
    -- vanilla's 3000, warp behind the player and burst.
    if ai:GetDist(TARGET_ENE_0) >= TELEPORT_FAR_RANGE then
        Houzuki755890_AddTeleportFar(ai, goal)
    else
        Houzuki755890_AddTeleportAway(ai, goal)
    end
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act03(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 12
    local canRunDist = 0
    local forceRunMinDist = 0.1
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 1
    local runLife = 8
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    Houzuki755890_AddGrab(ai, goal)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act04(ai, goal, paramTbl)
    -- SpeedFog: beam. Vanilla registers this act but never gives it any
    -- probability. Animation 3004's bullet events are retargeted to judge
    -- 150 by StaticModBuilder (UntouchableTaePatcher) and resolved to the
    -- beam bullet under the boss's behavior variation (UntouchableBossInjector).
    Houzuki755890_AddBeam(ai, goal)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act05(ai, goal, paramTbl)
    local goalLife = 5
    local moveTarget = TARGET_ENE_0
    local f7_local2 = 999
    local f7_local3 = 1.5
    local f7_local4 = 60
    local f7_local5 = 0
    local f7_local6 = 0
    local turnTarget = TARGET_ENE_0
    goal:ClearSubGoal()
    goal:AddSubGoal(GOAL_COMMON_LeaveTarget, goalLife, moveTarget, 10, turnTarget, true, 0)
    -- SpeedFog: punish from the retreat distance when the beam is available.
    Houzuki755890_AddBeamIfReady(ai, goal)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act06(ai, goal, paramTbl)
    local goalLife = 4
    local moveTarget = TARGET_ENE_0
    local f8_local2 = 999
    local f8_local3 = 1.5
    local f8_local4 = 60
    local f8_local5 = 0
    local f8_local6 = 0
    local turnTarget = TARGET_ENE_0
    goal:ClearSubGoal()
    goal:AddSubGoal(GOAL_COMMON_LeaveTarget, goalLife, moveTarget, 8, turnTarget, true, 0)
    -- SpeedFog: punish from the retreat distance when the beam is available.
    Houzuki755890_AddBeamIfReady(ai, goal)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act07(ai, goal, paramTbl)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act08(ai, goal, paramTbl)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act09(ai, goal, paramTbl)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act10(ai, goal, paramTbl)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act11(ai, goal, paramTbl)
    -- SpeedFog: lantern swing. 3001 is vanilla's post-teleport surprise
    -- attack (magic, no grab); as a regular melee act the boss keeps
    -- attacking while the grab (3002) sits on its cooldown.
    local stopDist = 3
    local canRunDist = 0
    local forceRunMinDist = 0.1
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 1
    local runLife = 5
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    Houzuki755890_AddSwing(ai, goal, false)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act40(ai, goal, paramTbl)
    local goalLife = ai:GetRandam_Int(1, 3)
    local moveTarget = TARGET_ENE_0
    local stopDist = 0.1
    local turnTarget = TARGET_SELF
    local walk = true
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local onGuardResult = GUARD_GOAL_DESIRE_RET_Continue
    local guardSuccessOnEnd = true
    local xzDistanceOnly = AI_CALC_DIST_TYPE__XYZ
    local f13_local9 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f13_local9 then
        guardStateId = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_ApproachTarget, goalLife, moveTarget, stopDist, turnTarget, walk, guardStateId, onGuardResult, guardSuccessOnEnd, xzDistanceOnly)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act41(ai, goal, paramTbl)
    local goalLife = ai:GetRandam_Int(1, 3)
    local moveTarget = TARGET_ENE_0
    local stopDist = 10
    local turnTarget = TARGET_ENE_0
    local walk = true
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local f14_local6 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f14_local6 then
        guardStateId = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_LeaveTarget, goalLife, moveTarget, stopDist, turnTarget, walk, guardStateId)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act42(ai, goal, paramTbl)
    local goalLife = ai:GetRandam_Float(0.8, 1.5)
    local moveTarget = TARGET_ENE_0
    local right = 1
    local angleThreshold = 90
    local f15_local4 = 0
    local f15_local5 = TARGET_SELF
    local isWalk = true
    local successOnEnd = true
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local f15_local9 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f15_local9 then
        guardStateId = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_SidewayMove, goalLife, moveTarget, right, angleThreshold, isWalk, successOnEnd, guardStateId)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act43(ai, goal, paramTbl)
    local goalLife = 2
    local turnTarget = TARGET_ENE_0
    local stopAngleWidth = 90
    local onGuardResult = GUARD_GOAL_DESIRE_RET_Continue
    local guardSuccessOnEnd = true
    local f16_local5 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f16_local5 then
        guardStateId = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_Turn, goalLife, turnTarget, stopAngleWidth, guardStateId, onGuardResult, guardSuccessOnEnd)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act44(ai, goal, paramTbl)
    local goalLife = 5
    local frontPriority = -1
    local backPriority = -1
    local leftPriority = 1
    local rightPriority = 1
    local target = TARGET_ENE_0
    local distSpaceCheck = 3
    local turnTime = 0
    local alwaysSuccess = true
    if ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 120, 180, 15) then
        goal:AddSubGoal(GOAL_COMMON_StepSafety, goalLife, frontPriority, 2, leftPriority, rightPriority, target, distSpaceCheck, turnTime, alwaysSuccess)
    elseif ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_R, 180, 180, 15) then
        goal:AddSubGoal(GOAL_COMMON_StepSafety, goalLife, frontPriority, backPriority, 1, -1, target, distSpaceCheck, turnTime, alwaysSuccess)
    elseif ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_L, 180, 180, 15) then
        goal:AddSubGoal(GOAL_COMMON_StepSafety, goalLife, frontPriority, backPriority, -1, 1, target, distSpaceCheck, turnTime, alwaysSuccess)
    end
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act45(ai, goal, paramTbl)
    local goalLife = 5
    local frontPriority = -1
    local backPriority = -1
    local leftPriority = 1
    local rightPriority = 1
    local target = TARGET_ENE_0
    local distSpaceCheck = 3
    local turnTime = 0
    local alwaysSuccess = true
    local random = ai:GetRandam_Int(1, 2)
    if random == 1 then
        rightPriority = 1
        leftPriority = -1
    end
    goal:AddSubGoal(GOAL_COMMON_StepSafety, goalLife, frontPriority, backPriority, leftPriority, rightPriority, target, distSpaceCheck, turnTime, alwaysSuccess)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act46(ai, goal, paramTbl)
    local goalLife = 10
    local moveTarget = TARGET_ENE_0
    local stopDist = 4
    local walk = true
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local f19_local5 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f19_local5 then
        guardStateId = 9910
    end
    if stopDist <= distanceEnemy then
        local turnTarget = TARGET_SELF
        goal:AddSubGoal(GOAL_COMMON_ApproachTarget, goalLife, moveTarget, stopDist, turnTarget, walk, guardStateId)
    else
        local turnTarget = TARGET_ENE_0
        goal:AddSubGoal(GOAL_COMMON_LeaveTarget, goalLife, moveTarget, stopDist, turnTarget, walk, guardStateId)
    end
    local goalLife_2 = ai:GetRandam_Float(0.1, 2)
    local moveTarget_2 = TARGET_ENE_0
    local right = ai:GetRandam_Int(0, 1)
    local angleThreshold = ai:GetRandam_Int(30, 45)
    local f19_local12 = 1
    local f19_local13 = TARGET_SELF
    local isWalk = true
    local successOnEnd = true
    local distanceEnemy_2 = ai:GetDist(TARGET_ENE_0)
    local f19_local17 = 0
    local random_2 = ai:GetRandam_Int(1, 100)
    local guardStateId_2 = -1
    if random_2 <= f19_local17 then
        guardStateId_2 = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_SidewayMove, goalLife_2, moveTarget_2, right, angleThreshold, isWalk, successOnEnd, guardStateId_2)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_Act47(ai, goal, paramTbl)
    local min = TORIMAKI_MIN_DIST
    local max = TORIMAKI_MAX_DIST
    local guardStateId = -1
    local walk = true
    local maxDistance = 1
    local goalLife = 10
    local goalLife_2 = 1.5
    local goalLife_3 = 0.5
    local existMesh = ai:IsExistMeshOnLine(TARGET_SELF, AI_DIR_TYPE_R, maxDistance)
    local existMesh_2 = ai:IsExistMeshOnLine(TARGET_SELF, AI_DIR_TYPE_L, maxDistance)
    local existMesh_3 = ai:IsExistMeshOnLine(TARGET_SELF, AI_DIR_TYPE_F, maxDistance)
    local existMesh_4 = ai:IsExistMeshOnLine(TARGET_SELF, AI_DIR_TYPE_B, maxDistance)
    local distanceTARGET_ENE0 = ai:GetDist(TARGET_ENE0)
    local right = ai:GetRandam_Int(0, 1)
    if existMesh_2 == true and existMesh == true then
    elseif existMesh_2 == true and existMesh == false then
        right = 0
    elseif existMesh_2 == false and existMesh == true then
        right = 1
    elseif existMesh_2 == false and existMesh == false then
        right = 2
    end
    if max < distanceTARGET_ENE0 then
        goal:AddSubGoal(GOAL_COMMON_ApproachTarget, goalLife, TARGET_ENE_0, ai:GetRandam_Float(min, max), TARGET_SELF, walk, guardStateId)
        GetWellSpace_Odds = 0
        return GetWellSpace_Odds
    elseif distanceTARGET_ENE0 <= max and min <= distanceTARGET_ENE0 then
        if right <= 1 then
            goal:AddSubGoal(GOAL_COMMON_SidewayMove, goalLife_2, TARGET_ENE_0, right, 100, walk, false, guardStateId, resultTypeIfGuardSuccess)
        else
            goal:AddSubGoal(GOAL_COMMON_Wait, 0.5, TARGET_ENE_0)
        end
    elseif distanceTARGET_ENE0 < min then
        if existMesh_4 == true then
            goal:AddSubGoal(GOAL_COMMON_LeaveTarget, goalLife_3, TARGET_ENE_0, ai:GetRandam_Float(min, max), TARGET_ENE_0, walk, guardStateId, GUARD_GOAL_DESIRE_RET_Success)
        elseif right <= 1 then
            goal:AddSubGoal(GOAL_COMMON_SidewayMove, goalLife_2, TARGET_ENE_0, right, 100, walk, false, guardStateId, resultTypeIfGuardSuccess)
        else
            goal:AddSubGoal(GOAL_COMMON_Wait, 0.5, TARGET_ENE_0)
        end
    end
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function Houzuki755890_ActAfter_AdjustSpace(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_Houzuki755890_AfterAttackAct, 10)
end

Goal.Update = function (self, ai, goal)
    return Update_Default_NoSubGoal(self, ai, goal)
end

Goal.Terminate = function (self, ai, goal)
end

Goal.Interrupt = function (self, ai, goal)
    if ai:IsLadderAct(TARGET_SELF) then
        return false
    end
    if ai:HasSpecialEffectId(TARGET_SELF, 5110) == true or ai:HasSpecialEffectAttribute(TARGET_SELF, SP_EFFECT_TYPE_ILLNESS) == true then
        return false
    end
    if ai:IsInterupt(INTERUPT_ActivateSpecialEffect) then
        if ai:HasSpecialEffectId(TARGET_SELF, 20011452) then
            -- Vanilla's post-3000 warp, the far teleport's second half:
            -- behind the player (scanned and warped around TARGET_ENE_0
            -- rather than vanilla's TARGET_EVENT, which served the fight's
            -- first teleport only here, see the doc), then the burst when
            -- its timer allows (vanilla swung unconditionally). As in
            -- vanilla, nothing is cleared when there is no room and 3000
            -- finishes on its own.
            local directionFromTarget, distanceFromTarget = Houzuki755890_FindRoomBehind(ai)
            if directionFromTarget ~= nil then
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ToTargetWarp, 15, TARGET_ENE_0, directionFromTarget, distanceFromTarget, TARGET_ENE_0)
                if Houzuki755890_SwingReady(ai) then
                    Houzuki755890_AddSwing(ai, goal, true)
                end
            end
            return true
        end
        if ai:GetSpecialEffectActivateInterruptId(5030) and ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 180, 180, 4) then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboRepeat_SuccessAngle180, 5, 3003, TARGET_ENE_0, 999, 0, 0)
            return true
        end
        return false
    end
    -- SpeedFog reactions, after the vanilla teleport and grab follow-ups
    -- (vanilla 472000's pattern: clear the sub-goals, queue the attack,
    -- return true). Each one spends an attack that is available anyway.
    if ai:IsInterupt(INTERUPT_Damaged) then
        if ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 120, 180, REACT_HIT_RANGE) and not Houzuki755890_SequenceInFlight(ai) and ai:GetRandam_Int(1, 100) <= REACT_HIT then
            -- Burst, retreat and beam when the teleport is ready (and there
            -- is room), a plain burst otherwise, nothing when neither is
            -- available: the running act is only cleared for a real answer.
            if Houzuki755890_TeleportReady(ai) and Houzuki755890_AddTeleportAway(ai, goal) then
                return true
            end
            if Houzuki755890_SwingReady(ai) then
                goal:ClearSubGoal()
                Houzuki755890_AddSwing(ai, goal, true)
                return true
            end
        end
        return false
    end
    if ai:IsInterupt(INTERUPT_Shoot) then
        -- A cast comes from REACT_RANGE, where the teleport would be the
        -- 5 s far one, no answer to a cast: the beam.
        return Houzuki755890_ReactBeam(ai, goal, REACT_SHOOT)
    end
    if ai:IsInterupt(INTERUPT_UseItem) then
        return Houzuki755890_ReactBeam(ai, goal, REACT_HEAL)
    end
    return false
end

RegisterTableGoal(GOAL_Houzuki755890_AfterAttackAct, "Houzuki755890_AfterAttackAct")
REGISTER_GOAL_NO_SUB_GOAL(GOAL_Houzuki755890_AfterAttackAct, true)

Goal.Activate = function (self, ai, goal)
end

Goal.Update = function (self, ai, goal)
    return Update_Default_NoSubGoal(self, ai, goal)
end
