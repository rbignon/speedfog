RegisterTableGoal(GOAL_KingGhoul_471000_Battle, "KingGhoul_471000_Battle")
REGISTER_GOAL_NO_SUB_GOAL(GOAL_KingGhoul_471000_Battle, true)

Goal.Initialize = function (self, ai, goal, battleActivatedCount)
    ai:SetNumber(1, 0)
end

Goal.Activate = function (self, ai, goal)
    local probabilities = {}
    local acts = {}
    local paramTbls = {}
    Common_Clear_Param(probabilities, acts, paramTbls)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local random = ai:GetRandam_Int(1, 100)
    local eventRequest = ai:GetEventRequest()
    local hasEffect11900 = ai:HasSpecialEffectId(TARGET_SELF, 11900)
    local hasEffect11901 = ai:HasSpecialEffectId(TARGET_SELF, 11901)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local hasEffect11941 = ai:HasSpecialEffectId(TARGET_SELF, 11941)
    local hasEffect11945 = ai:HasSpecialEffectId(TARGET_SELF, 11945)
    local hasEffect11947 = ai:HasSpecialEffectId(TARGET_SELF, 11947)
    local hasEffect5030 = ai:HasSpecialEffectId(TARGET_SELF, 5030)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5025)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5026)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5027)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5028)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5029)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5030)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5031)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5032)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5033)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5034)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5035)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 11938)
    if hasEffect11900 == true then
        local f2_local13 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local14 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FR, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local15 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FL, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local16 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_B, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local17 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_L, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local18 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_R, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        if f2_local16 == true then
            if distanceEnemy < 10 then
                probabilities[4] = 10
                probabilities[10] = 50
                probabilities[21] = 40
            else
                probabilities[10] = 50
                probabilities[21] = 50
            end
        elseif f2_local13 == true then
            if distanceEnemy < 10 then
                probabilities[4] = 30
                probabilities[25] = 70
            elseif distanceEnemy <= 15 then
                probabilities[1] = 10
                probabilities[2] = 30
                probabilities[3] = 20
                probabilities[6] = 10
                probabilities[29] = 30
            elseif distanceEnemy <= 25 then
                probabilities[1] = 5
                probabilities[2] = 15
                probabilities[3] = 20
                probabilities[6] = 10
                probabilities[20] = 50
            elseif distanceEnemy <= 30 then
                probabilities[5] = 20
                probabilities[6] = 30
                probabilities[20] = 20
                probabilities[28] = 30
            else
                probabilities[5] = 20
                probabilities[7] = 10
                probabilities[22] = 30
                probabilities[28] = 40
            end
        elseif f2_local14 == true then
            if distanceEnemy < 15 then
                probabilities[4] = 30
                probabilities[16] = 50
                probabilities[25] = 20
            elseif distanceEnemy <= 25 then
                probabilities[1] = 5
                probabilities[2] = 15
                probabilities[3] = 20
                probabilities[6] = 10
                probabilities[9] = 25
                probabilities[29] = 20
            elseif distanceEnemy <= 35 then
                probabilities[5] = 20
                probabilities[6] = 30
                probabilities[7] = 10
                probabilities[20] = 20
                probabilities[28] = 10
                probabilities[22] = 10
            else
                probabilities[6] = 10
                probabilities[20] = 20
                probabilities[22] = 40
                probabilities[28] = 30
            end
        elseif f2_local15 == true then
            if distanceEnemy < 15 then
                probabilities[4] = 30
                probabilities[15] = 50
                probabilities[25] = 20
            elseif distanceEnemy <= 25 then
                probabilities[8] = 25
                probabilities[1] = 5
                probabilities[2] = 15
                probabilities[3] = 20
                probabilities[6] = 10
                probabilities[29] = 20
            elseif distanceEnemy <= 35 then
                probabilities[5] = 20
                probabilities[6] = 30
                probabilities[7] = 10
                probabilities[20] = 20
                probabilities[28] = 10
                probabilities[22] = 10
            else
                probabilities[6] = 10
                probabilities[20] = 20
                probabilities[22] = 40
                probabilities[28] = 30
            end
        elseif f2_local17 == true then
            if distanceEnemy < 10 then
                probabilities[4] = 30
                probabilities[25] = 70
            elseif distanceEnemy <= 20 then
                probabilities[6] = 10
                probabilities[8] = 20
                probabilities[15] = 60
                probabilities[29] = 10
            else
                probabilities[6] = 10
                probabilities[21] = 40
                probabilities[22] = 50
            end
        elseif f2_local18 == true then
            if distanceEnemy < 10 then
                probabilities[4] = 30
                probabilities[25] = 70
            elseif distanceEnemy <= 20 then
                probabilities[6] = 10
                probabilities[9] = 20
                probabilities[16] = 60
                probabilities[29] = 10
            else
                probabilities[6] = 10
                probabilities[21] = 40
                probabilities[22] = 50
            end
        end
    elseif hasEffect11901 == true then
        local f2_local13 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local14 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FR, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local15 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FL, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local16 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_B, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local17 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_L, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local18 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_R, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        if ai:GetNumber(1) == 0 then
            probabilities[31] = 100
        elseif hasEffect11941 == true then
            probabilities[19] = 100
        elseif hasEffect11945 == true then
            probabilities[30] = 100
        elseif hasEffect11947 == true then
            local f2_local19 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 100, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
            local f2_local20 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_B, 260, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
            if f2_local20 == true then
                if distanceEnemy <= 15 then
                    probabilities[14] = 50
                    probabilities[21] = 50
                else
                    probabilities[21] = 100
                end
            elseif f2_local19 == true then
                if distanceEnemy < 15 then
                    probabilities[11] = 70
                    probabilities[13] = 30
                elseif distanceEnemy <= 30 then
                    probabilities[12] = 30
                    probabilities[17] = 20
                    probabilities[27] = 50
                else
                    probabilities[12] = 15
                    probabilities[17] = 70
                    probabilities[27] = 15
                end
            end
        elseif f2_local16 == true then
            probabilities[21] = 100
        elseif f2_local13 == true then
            if distanceEnemy < 15 then
                probabilities[11] = 50
                probabilities[13] = 30
                probabilities[18] = 20
            elseif distanceEnemy <= 30 then
                probabilities[11] = 20
                probabilities[14] = 10
                probabilities[17] = 20
                probabilities[18] = 40
                probabilities[12] = 10
                probabilities[27] = 10
            elseif distanceEnemy <= 40 then
                probabilities[12] = 20
                probabilities[17] = 20
                probabilities[18] = 30
                probabilities[27] = 30
            else
                probabilities[17] = 40
                probabilities[18] = 60
            end
        elseif f2_local14 == true or f2_local15 == true then
            if distanceEnemy < 15 then
                probabilities[11] = 20
                probabilities[13] = 20
                probabilities[14] = 30
                probabilities[18] = 30
            elseif distanceEnemy <= 30 then
                probabilities[11] = 20
                probabilities[13] = 20
                probabilities[14] = 10
                probabilities[18] = 30
                probabilities[12] = 10
                probabilities[17] = 20
            elseif distanceEnemy <= 40 then
                probabilities[12] = 30
                probabilities[17] = 40
                probabilities[18] = 30
            else
                probabilities[17] = 40
                probabilities[18] = 60
            end
        elseif f2_local18 or f2_local17 == true then
            if distanceEnemy < 15 then
                probabilities[13] = 50
                probabilities[14] = 10
                probabilities[18] = 10
                probabilities[21] = 30
            elseif distanceEnemy <= 30 then
                probabilities[11] = 15
                probabilities[12] = 15
                probabilities[14] = 20
                probabilities[17] = 20
                probabilities[18] = 20
                probabilities[21] = 10
            elseif distanceEnemy <= 40 then
                probabilities[12] = 20
                probabilities[17] = 20
                probabilities[18] = 40
                probabilities[21] = 20
            else
                probabilities[17] = 20
                probabilities[18] = 50
                probabilities[21] = 30
            end
        end
    elseif hasEffect11902 == true then
        local f2_local13 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local14 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FR, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local15 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_FL, 30, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local16 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_B, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local17 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_L, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        local f2_local18 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_R, 60, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
        if hasEffect11941 == true then
            probabilities[19] = 100
        elseif hasEffect11945 == true then
            probabilities[30] = 100
        elseif hasEffect11947 == true then
            local f2_local19 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_F, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
            local f2_local20 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_B, 240, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
            if f2_local20 == true then
                if distanceEnemy < 20 then
                    probabilities[10] = 60
                    probabilities[21] = 40
                else
                    probabilities[21] = 100
                end
            elseif f2_local19 == true then
                if distanceEnemy < 15 then
                    probabilities[3] = 50
                    probabilities[11] = 50
                elseif distanceEnemy <= 30 then
                    probabilities[12] = 30
                    probabilities[20] = 40
                    probabilities[27] = 30
                else
                    probabilities[12] = 15
                    probabilities[17] = 40
                    probabilities[20] = 30
                    probabilities[27] = 15
                end
            end
        elseif f2_local16 == true then
            if distanceEnemy < 20 then
                probabilities[10] = 80
                probabilities[21] = 20
            else
                probabilities[21] = 100
            end
        elseif f2_local13 == true then
            if distanceEnemy < 17 then
                probabilities[3] = 50
                probabilities[11] = 30
                probabilities[14] = 20
            elseif distanceEnemy <= 30 then
                probabilities[11] = 20
                probabilities[12] = 10
                probabilities[14] = 20
                probabilities[17] = 10
                probabilities[20] = 30
                probabilities[27] = 10
            elseif distanceEnemy <= 40 then
                probabilities[12] = 10
                probabilities[17] = 20
                probabilities[18] = 20
                probabilities[20] = 40
                probabilities[27] = 10
            else
                probabilities[17] = 30
                probabilities[18] = 40
                probabilities[20] = 30
            end
        elseif f2_local14 and distanceEnemy <= 17 == true then
            probabilities[3] = 30
            probabilities[11] = 35
            probabilities[16] = 35
        elseif f2_local15 and distanceEnemy <= 17 == true then
            probabilities[3] = 30
            probabilities[11] = 35
            probabilities[15] = 35
        elseif f2_local14 or f2_local15 == true then
            if distanceEnemy <= 25 then
                probabilities[11] = 35
                probabilities[18] = 10
                probabilities[12] = 25
                probabilities[20] = 30
            elseif distanceEnemy <= 40 then
                probabilities[12] = 20
                probabilities[17] = 40
                probabilities[18] = 20
                probabilities[20] = 30
            else
                probabilities[17] = 30
                probabilities[18] = 40
                probabilities[20] = 30
            end
        elseif f2_local18 == true then
            if distanceEnemy < 15 then
                probabilities[11] = 30
                probabilities[13] = 20
                probabilities[16] = 50
            elseif distanceEnemy <= 25 then
                probabilities[12] = 25
                probabilities[16] = 30
                probabilities[21] = 45
            elseif distanceEnemy <= 40 then
                probabilities[12] = 20
                probabilities[17] = 30
                probabilities[18] = 30
                probabilities[21] = 20
            else
                probabilities[17] = 40
                probabilities[21] = 30
                probabilities[22] = 30
            end
        elseif f2_local17 == true then
            if distanceEnemy < 15 then
                probabilities[11] = 30
                probabilities[13] = 20
                probabilities[15] = 50
            elseif distanceEnemy <= 25 then
                probabilities[11] = 35
                probabilities[12] = 15
                probabilities[15] = 30
                probabilities[21] = 20
            elseif distanceEnemy <= 40 then
                probabilities[12] = 20
                probabilities[17] = 30
                probabilities[18] = 30
                probabilities[21] = 20
            else
                probabilities[17] = 40
                probabilities[21] = 30
                probabilities[22] = 30
            end
        end
    end
    probabilities[1] = SetCoolTime(ai, goal, 3000, 8, probabilities[1], 1)
    probabilities[2] = SetCoolTime(ai, goal, 3001, 8, probabilities[2], 1)
    probabilities[3] = SetCoolTime(ai, goal, 3002, 8, probabilities[3], 1)
    probabilities[4] = SetCoolTime(ai, goal, 3004, 25, probabilities[4], 0)
    probabilities[5] = SetCoolTime(ai, goal, 3015, 10, probabilities[5], 1)
    probabilities[6] = SetCoolTime(ai, goal, 3014, 15, probabilities[6], 0)
    probabilities[7] = SetCoolTime(ai, goal, 3010, 20, probabilities[7], 1)
    probabilities[8] = SetCoolTime(ai, goal, 3007, 10, probabilities[8], 1)
    probabilities[9] = SetCoolTime(ai, goal, 3009, 10, probabilities[9], 1)
    probabilities[10] = SetCoolTime(ai, goal, 3003, 15, probabilities[10], 1)
    probabilities[11] = SetCoolTime(ai, goal, 3021, 10, probabilities[11], 1)
    probabilities[12] = SetCoolTime(ai, goal, 3024, 15, probabilities[12], 1)
    probabilities[13] = SetCoolTime(ai, goal, 3025, 10, probabilities[13], 1)
    probabilities[13] = SetCoolTime(ai, goal, 3026, 15, probabilities[13], 1)
    probabilities[14] = SetCoolTime(ai, goal, 3025, 15, probabilities[14], 1)
    probabilities[14] = SetCoolTime(ai, goal, 3026, 15, probabilities[14], 1)
    probabilities[15] = SetCoolTime(ai, goal, 3011, 10, probabilities[15], 1)
    probabilities[16] = SetCoolTime(ai, goal, 3012, 10, probabilities[16], 1)
    probabilities[17] = SetCoolTime(ai, goal, 3030, 15, probabilities[17], 1)
    probabilities[18] = SetCoolTime(ai, goal, 3031, 35, probabilities[18], 0)
    probabilities[20] = SetCoolTime(ai, goal, 3013, 13, probabilities[20], 0)
    probabilities[27] = SetCoolTime(ai, goal, 3037, 15, probabilities[27], 1)
    probabilities[28] = SetCoolTime(ai, goal, 3016, 15, probabilities[28], 1)
    probabilities[29] = SetCoolTime(ai, goal, 3017, 15, probabilities[29], 1)
    probabilities[29] = SetCoolTime(ai, goal, 3018, 15, probabilities[29], 1)
    if hasEffect11902 == true then
        probabilities[3] = SetCoolTime(ai, goal, 3000, 10, probabilities[3], 1)
        probabilities[3] = SetCoolTime(ai, goal, 3002, 10, probabilities[3], 1)
        probabilities[3] = SetCoolTime(ai, goal, 3013, 10, probabilities[3], 1)
        probabilities[3] = SetCoolTime(ai, goal, 3035, 10, probabilities[3], 1)
        probabilities[15] = SetCoolTime(ai, goal, 3028, 15, probabilities[15], 0)
        probabilities[16] = SetCoolTime(ai, goal, 3029, 15, probabilities[16], 0)
        probabilities[20] = SetCoolTime(ai, goal, 3013, 10, probabilities[20], 0)
    end
    acts[1] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act01)
    acts[2] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act02)
    acts[3] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act03)
    acts[4] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act04)
    acts[5] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act05)
    acts[6] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act06)
    acts[7] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act07)
    acts[8] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act08)
    acts[9] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act09)
    acts[10] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act10)
    acts[11] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act11)
    acts[12] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act12)
    acts[13] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act13)
    acts[14] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act14)
    acts[15] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act15)
    acts[16] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act16)
    acts[17] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act17)
    acts[18] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act18)
    acts[19] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act19)
    acts[20] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act20)
    acts[21] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act21)
    acts[22] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act22)
    acts[23] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act23)
    acts[24] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act24)
    acts[25] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act25)
    acts[26] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act26)
    acts[27] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act27)
    acts[28] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act28)
    acts[29] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act29)
    acts[30] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act30)
    acts[31] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act31)
    acts[45] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act45)
    acts[46] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act46)
    acts[47] = REGIST_FUNC(ai, goal, KingGhoul_471000_Act47)
    local actAfter = REGIST_FUNC(ai, goal, KingGhoul_471000_ActAfter_AdjustSpace)
    Common_Battle_Activate(ai, goal, probabilities, acts, actAfter, paramTbls)
end

function KingGhoul_471000_Act01(ai, goal, paramTbl)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local random = ai:GetRandam_Int(1, 100)
    local random_2 = ai:GetRandam_Int(1, 100)
    local hasEffect11900 = ai:HasSpecialEffectId(TARGET_SELF, 11900)
    local hasEffect11902_2 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local stopDist = 17
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 2
    local runLife = 2
    local animationId = 3000
    local successDist = 20
    local turnTime = 0
    local turnFaceAngle = 180
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act02(ai, goal, paramTbl)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local stopDist = 17
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 2
    local runLife = 2
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3000
    local animationId_2 = 3001
    local successDist = 18
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, animationId_2, TARGET_ENE_0, successDist, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act03(ai, goal, paramTbl)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local stopDist = 17
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 2
    local runLife = 2
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3000
    local animationId_2 = 3001
    local animationId_3 = 3002
    local f5_local11 = 3035
    local successDist = 18
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, animationId_2, TARGET_ENE_0, successDist, 0, 0)
    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, animationId_3, TARGET_ENE_0, successDist, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act04(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local f6_local1 = 10
    local f6_local2 = 0
    local f6_local3 = 0
    local f6_local4 = 0
    local f6_local5 = 0
    local f6_local6 = 3
    local f6_local7 = 3
    local animationId = 3004
    local successDist = 20
    local turnTime = 0
    local f6_local11 = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 5, animationId, TARGET_ENE_0, successDist, turnTime, 0, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act05(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local stopDist = 28
    local canRunDist = 0
    local forceRunMinDist = 0
    local runProbability = 0
    local guardProbability = 0
    local walkLife = 3
    local runLife = 3
    local animationId = 3015
    local successDist = 10
    local turnTime = 0
    local f7_local11 = 180
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, 0, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act06(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local f8_local1 = 30
    local f8_local2 = 0
    local f8_local3 = 0
    local f8_local4 = 0
    local f8_local5 = 0
    local f8_local6 = 3
    local f8_local7 = 3
    local animationId = 3014
    local successDist = 40
    local f8_local10 = 0
    local f8_local11 = 60
    goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 5, animationId, TARGET_ENE_0, successDist, 0, 0, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act07(ai, goal, paramTbl)
    local stopDist = 30
    local canRunDist = 0
    local forceRunMinDist = 0
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 3
    local runLife = 3
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3010
    local successDist = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act08(ai, goal, paramTbl)
    local stopDist = 14
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 2
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3007
    local successDist = 15
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act09(ai, goal, paramTbl)
    local stopDist = 14
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 2
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3009
    local successDist = 15
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act10(ai, goal, paramTbl)
    local f12_local0 = 20
    local f12_local1 = 0
    local f12_local2 = 999
    local f12_local3 = 100
    local f12_local4 = 0
    local f12_local5 = 4
    local f12_local6 = 4
    local animationId = 3003
    local successDist = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 15, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act11(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 19
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 8
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3021
    local successDist = 20
    local f13_local12 = 20
    local f13_local13 = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local f13_local14 = 0
    local f13_local15 = 120
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5025)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5029)
    ai:AddObserveSpecialEffectAttribute(TARGET_SELF, 5030)
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, 0, 180, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act12(ai, goal, paramTbl)
    local stopDist = 20
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 4
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3024
    local successDist = 30
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act13(ai, goal, paramTbl)
    local f15_local0 = 9 + ai:GetMapHitRadius(TARGET_SELF) - ai:GetMapHitRadius(TARGET_SELF) + 5
    local f15_local1 = 9 + ai:GetMapHitRadius(TARGET_SELF) - ai:GetMapHitRadius(TARGET_SELF)
    local f15_local2 = 9 + ai:GetMapHitRadius(TARGET_SELF) - ai:GetMapHitRadius(TARGET_SELF) + 3
    local f15_local3 = 0
    local f15_local4 = 0
    local f15_local5 = 4
    local f15_local6 = 8
    local animationId = 3025
    local successDist = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act14(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local animationId = 3026
    local f16_local2 = 3027
    local successDist = 20
    local f16_local4 = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act15(ai, goal, paramTbl)
    local f17_local0 = 15
    local f17_local1 = 0
    local f17_local2 = 999
    local f17_local3 = 100
    local f17_local4 = 0
    local f17_local5 = 4
    local f17_local6 = 4
    local animationId = 3011
    local successDist = 20
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 15, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act16(ai, goal, paramTbl)
    local f18_local0 = 15
    local f18_local1 = 0
    local f18_local2 = 999
    local f18_local3 = 100
    local f18_local4 = 0
    local f18_local5 = 4
    local f18_local6 = 4
    local animationId = 3012
    local successDist = 20
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 15, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act17(ai, goal, paramTbl)
    local f19_local0 = 31
    local f19_local1 = 0
    local f19_local2 = 999
    local f19_local3 = 100
    local f19_local4 = 0
    local f19_local5 = 4
    local f19_local6 = 8
    local animationId = 3030
    local f19_local8 = 30 + ai:GetMapHitRadius(TARGET_SELF)
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, DistToAtt2, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act18(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local f20_local2 = 60
    local f20_local3 = 0
    local f20_local4 = 999
    local f20_local5 = 100
    local f20_local6 = 0
    local f20_local7 = 4
    local f20_local8 = 8
    local animationId = 3031
    local animationId_2 = 3032
    local successDist = 60
    local turnTime = 0
    local f20_local13 = 180
    goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 10, animationId, TARGET_ENE_0, successDist, turnTime, 0, 0)
    if hasEffect11902 == true then
        goal:AddSubGoal(GOAL_COMMON_ComboFinal, 10, animationId_2, TARGET_ENE_0, successDist, 0, 0)
    end
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act19(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 20, 3033, TARGET_ENE_0, 99, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act20(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 20
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 4
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    if hasEffect11902 == true then
        stopDist = 30
    end
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3013
    local target = TARGET_ENE_0
    local successDist = 30
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, target, successDist, 0, 0, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act21(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_Turn, 2, TARGET_ENE_0, 45, GUARD_GOAL_DESIRE_RET_Continue, true)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act22(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 20
    local f24_local2 = 999
    local f24_local3 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    goal:AddSubGoal(GOAL_COMMON_ApproachTarget, 5, TARGET_ENE_0, stopDist, TARGET_ENE_0, false, guardStateId)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act23(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 15
    local f25_local2 = 999
    local f25_local3 = 0
    local random = ai:GetRandam_Int(1, 100)
    local f25_local5 = -1
    goal:AddSubGoal(GOAL_COMMON_LeaveTarget, 3, TARGET_ENE_0, stopDist, TARGET_ENE_0, false, -1)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act24(ai, goal, paramTbl)
    local f26_local0 = 80
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    if random <= f26_local0 then
        guardStateId = 9910
    end
    goal:AddSubGoal(GOAL_COMMON_SidewayMove, 2, TARGET_ENE_0, ai:GetRandam_Int(0, 1), ai:GetRandam_Int(30, 45), true, true, guardStateId)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act25(ai, goal, paramTbl)
    local goalLife = 3
    local frontPriority = -1
    local backPriority = 100
    local leftPriority = -1
    local rightPriority = -1
    local target = TARGET_ENE_0
    local distSpaceCheck = 3
    local turnTime = 0
    local alwaysSuccess = true
    goal:AddSubGoal(GOAL_COMMON_StepSafety, goalLife, frontPriority, backPriority, leftPriority, rightPriority, target, distSpaceCheck, turnTime, alwaysSuccess)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act26(ai, goal, paramTbl)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local stopDist = 20 + ai:GetMapHitRadius(TARGET_SELF)
    local f28_local2 = 999
    local f28_local3 = 0
    local random = ai:GetRandam_Int(1, 100)
    local guardStateId = -1
    goal:AddSubGoal(GOAL_COMMON_Turn, 2, TARGET_ENE_0, 90, 0, 0)
    goal:AddSubGoal(GOAL_COMMON_LeaveTarget, 4, TARGET_ENE_0, stopDist, TARGET_ENE_0, true, guardStateId)
    for f28_local6 = 3000, 3012, 1 do
        ai:RegistAttackTimeInterval(f28_local6, 0)
    end
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds

end

function KingGhoul_471000_Act27(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local stopDist = 23
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 4
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3037
    local animationId_2 = 3038
    local successDist = 30
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, animationId_2, TARGET_ENE_0, successDist, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act28(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local stopDist = 40
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 4
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3016
    local successDist = 40
    local turnTime = 0
    local turnFaceAngle = 180
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act29(ai, goal, paramTbl)
    local random = ai:GetRandam_Int(1, 100)
    local stopDist = 14
    local canRunDist = 0
    local forceRunMinDist = 999
    local runProbability = 100
    local guardProbability = 0
    local walkLife = 4
    local runLife = 4
    Approach_Act_Flex(ai, goal, stopDist, canRunDist, forceRunMinDist, runProbability, guardProbability, walkLife, runLife)
    local animationId = 3017
    local successDist = 40
    local turnTime = 0
    local turnFaceAngle = 180
    local f31_local12 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_R, 180, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
    if f31_local12 == true then
        animationId = 3018
    end
    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, animationId, TARGET_ENE_0, successDist, turnTime, turnFaceAngle, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act30(ai, goal, paramTbl)
    local f32_local0 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_BL, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
    local f32_local1 = ai:IsInsideTargetCustom(TARGET_SELF, TARGET_ENE_0, AI_DIR_TYPE_BR, 120, 90, 200 + ai:GetMapHitRadius(TARGET_SELF))
    if f32_local0 or f32_local1 == true then
        goal:AddSubGoal(GOAL_COMMON_Turn, 2, TARGET_ENE_0, 45, GUARD_GOAL_DESIRE_RET_Continue, true)
    end
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 20, 3039, TARGET_ENE_0, 99, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act31(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_Wait, 3, TARGET_ENE_0)
    ai:SetNumber(1, 1)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act45(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, 3021, TARGET_ENE_0, DIST_None, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act46(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, 3021, TARGET_ENE_0, DIST_None, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_Act47(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_COMMON_AttackTunableSpin, 10, 3021, TARGET_ENE_0, DIST_None, 0, 0)
    GetWellSpace_Odds = 0
    return GetWellSpace_Odds
end

function KingGhoul_471000_ActAfter_AdjustSpace(ai, goal, paramTbl)
    goal:AddSubGoal(GOAL_KingGhoul_471000_AfterAttackAct, 10)
end

Goal.Update = function (self, ai, goal)
    return Update_Default_NoSubGoal(self, ai, goal)
end

Goal.Terminate = function (self, ai, goal)
end

Goal.Interrupt = function (self, ai, goal)
    local distanceEnemy = ai:GetDist(TARGET_ENE_0)
    local distanceFriend = ai:GetDist(TARGET_FRI_0)
    local random = ai:GetRandam_Int(1, 100)
    local hasEffect11900 = ai:HasSpecialEffectId(TARGET_SELF, 11900)
    local hasEffect11901 = ai:HasSpecialEffectId(TARGET_SELF, 11901)
    local hasEffect11902 = ai:HasSpecialEffectId(TARGET_SELF, 11902)
    local hasEffect11941 = ai:HasSpecialEffectId(TARGET_SELF, 11941)
    local hasEffect11945 = ai:HasSpecialEffectId(TARGET_SELF, 11945)
    local hasEffect5030 = ai:HasSpecialEffectId(TARGET_SELF, 5030)
    if ai:IsLadderAct(TARGET_SELF) then
        return false
    end
    if ai:IsInterupt(INTERUPT_ActivateSpecialEffect) then
        if hasEffect11941 == true then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 10, 3033, TARGET_ENE_0, 99, 0, 0)
            return true
        end
        if hasEffect11945 == true then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboTunable_SuccessAngle180, 10, 3039, TARGET_ENE_0, 99, 0, 0)
            return true
        end
        if ai:HasSpecialEffectId(TARGET_SELF, 5029) then
            if hasEffect11902 == true then
                if random <= 50 and distanceEnemy <= 16 and ai:GetAttackPassedTime(3035) >= 15 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3035, TARGET_ENE_0, 20, 0, 0)
                    return true
                else
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3022, TARGET_ENE_0, 20, 0, 0)
                    return true
                end
            else
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3022, TARGET_ENE_0, 20, 0, 0)
                return true
            end
        end
        if ai:HasSpecialEffectId(TARGET_SELF, 5025) and distanceEnemy <= 30 then
            if hasEffect11902 == true then
                if distanceEnemy <= 15 and random <= 50 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3036, TARGET_ENE_0, 20, 0, 0)
                    return true
                elseif ai:GetAttackPassedTime(3023) >= 10 and distanceEnemy >= 15 and distanceEnemy <= 20 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3023, TARGET_ENE_0, 30, 0, 0)
                    return true
                elseif random <= 50 and ai:GetAttackPassedTime(3037) >= 10 and distanceEnemy >= 20 and distanceEnemy <= 30 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 5, 3037, TARGET_ENE_0, 30, 0, 0)
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 5, 3038, TARGET_ENE_0, 30, 0, 0)
                    return true
                elseif ai:GetAttackPassedTime(3034) >= 10 and distanceEnemy >= 20 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 5, 3034, TARGET_ENE_0, 35, 0, 0)
                    return true
                elseif ai:GetAttackPassedTime(3024) >= 10 and distanceEnemy >= 15 and distanceEnemy <= 22 then
                    goal:ClearSubGoal()
                    goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, 3024, TARGET_ENE_0, 30, 0, 0)
                    return true
                end
            elseif ai:GetAttackPassedTime(3023) >= 10 and distanceEnemy >= 15 and distanceEnemy <= 20 then
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3023, TARGET_ENE_0, 30, 0, 0)
                return true
            elseif random <= 50 and ai:GetAttackPassedTime(3037) >= 10 and distanceEnemy >= 20 and distanceEnemy <= 30 then
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 5, 3037, TARGET_ENE_0, 30, 0, 0)
                goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 5, 3038, TARGET_ENE_0, 30, 0, 0)
                return true
            elseif ai:GetAttackPassedTime(3034) >= 10 and distanceEnemy >= 20 then
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 5, 3034, TARGET_ENE_0, 35, 0, 0)
                return true
            elseif ai:GetAttackPassedTime(3024) >= 10 and distanceEnemy >= 15 and distanceEnemy <= 22 then
                goal:ClearSubGoal()
                goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, 3024, TARGET_ENE_0, 30, 0, 0)
                return true
            end
        end
        if ai:HasSpecialEffectId(TARGET_SELF, 5031) then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3002, TARGET_ENE_0, 30, 0, 0)
            return true
        end
        if ai:HasSpecialEffectId(TARGET_SELF, 5033) then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3001, TARGET_ENE_0, 30, 0, 0)
            goal:AddSubGoal(GOAL_COMMON_ComboRepeat, 10, 3002, TARGET_ENE_0, 30, 0, 0)
            return true
        end
        if ai:HasSpecialEffectId(TARGET_SELF, 5035) and ai:GetAttackPassedTime(3013) >= 15 and distanceEnemy >= 13 and distanceEnemy <= 25 then
            goal:ClearSubGoal()
            goal:AddSubGoal(GOAL_COMMON_ComboAttackTunableSpin, 10, 3013, TARGET_ENE_0, 30, 0, 0)
            return true
        end
    end
    return false
end

RegisterTableGoal(GOAL_KingGhoul_471000_AfterAttackAct, "KingGhoul_471000_AfterAttackAct")
REGISTER_GOAL_NO_SUB_GOAL(GOAL_KingGhoul_471000_AfterAttackAct, true)

Goal.Activate = function (self, ai, goal)
end

Goal.Update = function (self, ai, goal)
    return Update_Default_NoSubGoal(self, ai, goal)
end
