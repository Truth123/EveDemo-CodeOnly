## 1. 状态定位

`HitReaction` 是玩家角色的普通受击状态，用于处理玩家被敌人普通攻击命中后的受击硬直、受击位移、受击恢复、受击后重置响应、受击结束后返回 Idle / Locomotion 等逻辑。

`HitReaction` 是强制状态。敌人攻击命中玩家时不直接进入 `HitReaction`，而是先进入 `CombatHitResolver`。当本次攻击没有被 `Dead / PerfectGuard / PerfectEvade / EvadeInvincible / Guard / Knockdown` 等更高优先级规则处理时，进入 `HitReaction`。

当前实现修订：

- Boss 对玩家的正式命中不再使用 CombatReactionIntent.Knockdown / GuardValue <= 0 等多攻击类型决定玩家反应，只使用 `CombatReactionIntent.HitReactionReaction / Knockdown / DamageOnly`。
- `HitReaction` 只处理 `CombatReactionIntent=HitReaction` 的站立普通受击。
- `HitReaction` 不再区分 Light / Medium / Strong 受击等级；动画只按 `HitDirection Front / Back / Left / Right` 选择。
- 击退位移统一使用 `HitReactionLight` 动作位移配置，后续如需要更强受击再单独扩展。
- 连续普通受击转 Knockdown 本阶段不实现，避免重新引入攻击类型和受击等级膨胀。

`HitReaction` 负责：

```text
1. 处理普通受击进入 HitReaction。
2. 播放受击动画。
3. 应用受击硬直。
4. 应用受击位移 / 击退。
5. 清除当前所有输入缓存。
6. 关闭当前攻击判定、防御判定、技能判定、闪避窗口。
7. 管理 HitStart / HitStun / HitRecovery / HitResetWindow。
8. 管理 HitResetWindow 内的 Attack / Evade / Guard / Skill 响应。
9. 管理 HitEnd 后返回 Idle / Locomotion。
10. 管理连续受击转 Knockdown。
```

`HitReaction` 不负责：

```text
1. Knockdown 倒地与起身。
2. PerfectGuard 判定。
3. PerfectEvade 判定。
4. Evade 无敌判定。
5. Guard 普通防御判定。
6. 攻击判定。
7. 技能判定。
8. 回血。
9. LockOn 切换。
10. 输入缓存。
```

---

## 2. 状态分层

```text
HitReaction
├── HitStart
├── HitStun
├── HitRecovery
└── HitResetWindow
```

---

## 3. 进入条件

|来源状态|条件|目标状态|说明|
|---|---|---|---|
|Any|EnemyAttackResolvedAsHit 且 HP > 0|HitReaction|普通受击|
|Idle|EnemyCombatReactionIntent.HitReaction 且非无敌、未防御|HitReaction|待机被普通攻击命中|
|Locomotion|EnemyCombatReactionIntent.HitReaction 且非无敌、未防御|HitReaction|移动中被普通攻击命中|
|Attack|EnemyCombatReactionIntent.HitReaction 且无霸体、非无敌、未防御|HitReaction|攻击中被普通攻击打断|
|Skill|EnemyCombatReactionIntent.HitReaction 且不在 SkillSuperArmorWindow|HitReaction|技能中被普通攻击打断|
|Recover|EnemyCombatReactionIntent.HitReaction 且非无敌|HitReaction|回血被普通攻击打断|
|Guard|EnemyAttackHit 且不可防御|HitReaction / Knockdown|防御失败|
|Evade|EnemyHit 且非 EvadeInvincibleWindow|HitReaction / Knockdown|闪避后摇被击中|

受击解析优先级：

```text
1. HP <= 0 → Dead
2. Guard + PerfectGuard 成功 → Guard.PerfectGuard
3. Evade + PerfectEvade 成功 → Evade.PerfectEvade
4. EvadeInvincibleWindow=true → 忽略伤害
5. Guard + 可普通防御 → GuardHit
6. `CombatReactionIntent=Knockdown` → Knockdown
7. `CombatReactionIntent=HitReaction` → HitReaction
```

---

## 4. 进入时处理

进入 `HitReaction` 的第一帧执行：

```text
1. Set PlayerState = HitReaction。
2. Set HitReactionPhase = HitStart。
3. 记录 HitData。
4. 结算 HP 伤害。
5. 计算 HitDirection。
6. 计算 HitReactionType。
7. 清除所有输入缓存。
8. 关闭玩家攻击 Hitbox。
9. 关闭 SkillHitbox。
10. 关闭 GuardBlockActive。
11. 关闭 EvadeInvincibleWindow。
12. 关闭 PerfectEvadeWindow。
13. 关闭 PerfectGuardWindow。
14. 关闭所有 CancelWindow / ResetWindow。
15. 取消当前动作的主动位移。
16. 应用受击初始位移。
17. 播放受击动画。
```

---

## 5. HitData 数据结构

```text
HitData
{
    AttackId
    AttackerId
    AttackType
    Damage
    PoiseDamage
    HitDirection
    HitPosition
    KnockbackDirection
    KnockbackDistance
    HitStunTime
    HitReactionLevel
    CanChainToKnockdown
}
```

|字段|说明|
|---|---|
|AttackId|本次敌人攻击编号|
|AttackerId|攻击来源|
|AttackType|CombatReactionIntent.HitReaction / CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0|
|Damage|HP 伤害|
|PoiseDamage|韧性伤害|
|HitDirection|相对玩家朝向的命中方向|
|HitPosition|命中位置|
|KnockbackDirection|击退方向|
|KnockbackDistance|击退距离|
|HitStunTime|硬直时长|
|HitReactionLevel|轻受击 / 中受击|
|CanChainToKnockdown|连续受击是否可转 Knockdown|

---

## 6. HitStart

`HitStart` 是受击启动阶段。

```text
作用：
1. 中断玩家当前动作。
2. 播放受击动画起始段。
3. 结算 HP 伤害。
4. 施加初始击退。
5. 关闭所有玩家主动判定。
6. 禁止玩家输入切换。
```

时间：

```text
HitStartDuration = 0.08s
```

窗口：

```text
0.00 - 0.08 HitStart
```

规则：

```text
1. HitStart 中不响应 MoveInput。
2. HitStart 中 LookInput 持续响应。
3. HitStart 中不响应 LockOnPressed。
4. HitStart 中不响应 LightAttackPressed / HeavyAttackPressed。
5. HitStart 中不响应 EvadePressed。
6. HitStart 中不响应 GuardHeld。
7. HitStart 中不响应 Skill1Pressed。
8. HitStart 中不响应 RecoverHpPressed。
9. HitStart 中受到 CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0，进入 Knockdown。
10. HitStart 中 HP <= 0，进入 Dead。
```

---

## 7. HitStun

`HitStun` 是受击硬直阶段。

```text
作用：
1. 保持受击不可控。
2. 播放受击硬直动画。
3. 处理击退位移衰减。
4. 禁止玩家主动状态切换。
5. 处理连续受击。
```

时间：

```text
HitStunDuration = 0.22s
```

窗口：

```text
0.08 - 0.30 HitStun
```

规则：

```text
1. HitStun 中不响应 MoveInput。
2. HitStun 中 LookInput 持续响应。
3. HitStun 中不响应 LockOnPressed。
4. HitStun 中不响应 Attack / Evade / Guard / Skill / Recover。
5. HitStun 中再次受到普通攻击，重新进入 HitReaction 或刷新当前 HitReaction。
6. HitStun 中受到 CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0，进入 Knockdown。
7. HitStun 中 HP <= 0，进入 Dead。
```

连续受击规则：

```text
if ConsecutiveHitCount >= MaxConsecutiveHitBeforeKnockdown:
    EnterKnockdown()
```

---

## 8. HitRecovery

`HitRecovery` 是受击恢复阶段。

```text
作用：
1. 玩家从硬直中恢复。
2. 受击动画进入后摇。
3. 读取 MoveInput，用于 HitEnd 后判断回 Idle 或 Locomotion。
4. 不开启输入缓存。
5. 后段进入 HitResetWindow。
```

时间：

```text
HitRecoveryDuration = 0.22s
```

窗口：

```text
0.30 - 0.52 HitRecovery
```

规则：

```text
1. HitRecovery 中 MoveInput 只读取，不产生移动。
2. HitRecovery 中 LookInput 持续响应。
3. HitRecovery 中 LockOnPressed 不响应。
4. HitRecovery 中 Attack / Evade / Guard / Skill / Recover 不响应。
5. HitRecovery 中不缓存任何输入。
```

---

## 9. HitResetWindow

`HitResetWindow` 是受击恢复末段的重置区间。

```text
作用：
1. 允许 EvadePressed 即时进入 Evade。
2. 允许 Skill1Pressed 即时进入 Skill。
3. 允许 LightAttackPressed / HeavyAttackPressed 即时进入 Attack。
4. 允许 GuardHeld 即时进入 Guard。
5. 允许自然回 Idle / Locomotion。
```

时间：

```text
HitResetWindow = 0.533s - 0.633s
```

窗口：

```text
0.533 - 0.633 Reaction_CanReturn
0.533 - 0.633 Cancel_ToEvade
0.533 - 0.633 Cancel_ToSkill
0.533 - 0.633 Cancel_ToGuard
0.533 - 0.633 Cancel_AttackReset
```

规则：

```text
1. EvadePressed 只在 HitResetWindow 内即时响应，不缓存。
2. Skill1Pressed 只在 HitResetWindow 内即时响应，不缓存。
3. LightAttackPressed / HeavyAttackPressed 只在 HitResetWindow 内即时响应，不缓存。
4. GuardHeld 只在 HitResetWindow 内检测，不缓存。
5. RecoverHpPressed 不响应。
6. LockOnPressed 不响应。
```

---

## 10. 受击方向规则

受击方向由攻击来源相对玩家朝向计算：

```text
HitDirection = DirectionFromAttackerToPlayerRelativeToPlayerFacing
```

方向映射：

| HitDirection | 说明   |
| ------------ | ---- |
| Front        | 正面受击 |
| Back         | 背面受击 |
| Left         | 左侧受击 |
| Right        | 右侧受击 |

动画选择：

| HitDirection | HitReactionAnimation |
| ------------ | -------------------- |
| Front        | Hit_Front            |
| Back         | Hit_Back             |
| Left         | Hit_Left             |
| Right        | Hit_Right            |

---

## 11. 受击等级规则

`HitReaction` 只处理普通受击，不处理倒地受击。

| HitReactionLevel | 说明   | 目标状态        |
| ---------------- | ---- | ----------- |
| Light            | 轻受击  | HitReaction |
| Medium           | 中受击  | HitReaction |
| Heavy            | 强受击  | Knockdown   |
| Knockdown        | 击倒攻击 | Knockdown   |
| GuardValue <= 0       | 破防攻击 | Knockdown   |

规则：

```text
if AttackType == CombatReactionIntent.Knockdown:
    EnterKnockdown()

if AttackType == CombatReactionIntent.Knockdown:
    EnterKnockdown()

if AttackType == GuardValue <= 0:
    EnterKnockdown()

else:
    EnterHitReaction()
```

---

## 12. 受击位移规则

```text
1. HitReaction 进入时通过 PlayerStateContext.RequestActionMotion(...) 请求受击击退。
2. 轻受击使用 HitReactionLight，中受击使用 HitReactionMedium。
3. 击退方向优先使用 CombatHitData.HitDirection.normalized。
4. 如果 HitDirection 无效，使用 player.position - CombatHitData.HitPosition。
5. 如果仍无效，fallback 为 -player.forward。
6. 受击位移由 PlayerMovementMotor 通过 PlayerActionMotionRuntime 和 CharacterController.Move() 执行。
7. 击退不能穿墙，不能穿过 Boss 大体积碰撞。
8. StopOnSideCollision=true，遇到侧向阻挡时停止剩余水平动作位移，但不提前结束 HitReaction。
```

位移参数：

```text
HitReactionLight.Distance = 0.6m
HitReactionLight.Duration = 0.22s
HitReactionMedium.Distance = 1.0m
HitReactionMedium.Duration = 0.26s
StopOnSideCollision = true
```

---

## 13. 连续受击规则

```text
1. 进入 HitReaction 时 ConsecutiveHitCount += 1。
2. HitReaction 结束并成功回到 Idle / Locomotion 后，ConsecutiveHitCount 清零。
3. 进入 Knockdown 后，ConsecutiveHitCount 清零。
4. 距离上次受击超过 ConsecutiveHitResetTime，ConsecutiveHitCount 清零。
5. ConsecutiveHitCount 达到上限时，下一次普通受击进入 Knockdown。
```

参数：

```text
MaxConsecutiveHitBeforeKnockdown = 3
ConsecutiveHitResetTime = 1.2s
```

连续受击转 Knockdown：

```text
if ConsecutiveHitCount >= MaxConsecutiveHitBeforeKnockdown
and HitData.CanChainToKnockdown == true:
    EnterKnockdown()
```

---

## 14. HitReaction 输入响应规则

| 输入                 | 响应方式                  | 是否缓存 |
| ------------------ | --------------------- | ---: |
| MoveInput          | 不移动；持续读取用于 HitEnd 后去向 |    否 |
| LookInput          | 持续响应                  |    否 |
| LockOnPressed      | 不响应                   |    否 |
| LightAttackPressed | HitResetWindow 内即时响应  |    否 |
| HeavyAttackPressed | HitResetWindow 内即时响应  |    否 |
| EvadePressed       | HitResetWindow 内即时响应  |    否 |
| EvadeHeld          | 不响应                   |    否 |
| EvadeReleased      | 不响应                   |    否 |
| GuardHeld          | HitResetWindow 内即时响应  |    否 |
| Skill1Pressed      | HitResetWindow 内即时响应  |    否 |
| Skill2Pressed      | 不响应                   |    否 |
| RecoverHpPressed   | 不响应                   |    否 |

---

## 15. HitReaction 时间轴与窗口

以 `HitReactionTotalDuration = 38f / 60fps = 0.633s` 为标准时间轴：

```text
0.00s ───────────────────────────────────── 0.633s

Phase:
0.000 - 0.083  Start
0.083 - 0.333  Loop
0.333 - 0.533  Recovery
0.533 - 0.633  Reset

Reaction:
0.000 - 0.333  Reaction_Stun
0.333 - 0.533  Reaction_Recovery
0.533 - 0.633  Reaction_CanReturn

Cancel:
0.533 - 0.633  Cancel_ToEvade
0.533 - 0.633  Cancel_ToSkill
0.533 - 0.633  Cancel_ToGuard
0.533 - 0.633  Cancel_AttackReset
```

`Phase` 只用于动画 / 调试展示。HitReaction 恢复末段能否转 Evade / Skill / Guard / Attack 必须读取对应 Cancel capability；`Reset` Phase 本身不直接开放动作。

---

## 16. 输入缓存规则

HitReaction 状态不使用输入缓存。

```text
1. HitReaction 进入时清除所有输入缓存。
2. HitReaction 状态内不创建任何新的输入缓存。
3. EvadePressed 不缓存。
4. Skill1Pressed 不缓存。
5. LightAttackPressed / HeavyAttackPressed 不缓存。
6. GuardHeld 不缓存。
7. RecoverHpPressed 不缓存。
8. LockOnPressed 不缓存。
9. HitReactionEnd 时再次清除 HitReaction 状态内的临时输入记录。
```

---

## 17. HitResetWindow 响应规则

运行时不直接用 `CurrentPhase == Reset` 判断能否响应输入；`HitResetWindow` 在 Combat Timeline 中由对应 Cancel capability 表达。

### 17.1 EvadePressed

```text
if Cancel_ToEvade == true:
    if EvadePressedThisFrame and CanEvade:
        EnterEvade()
```

限制：

```text
1. HitResetWindow 开启前的 EvadePressed 直接忽略。
2. HitResetWindow 内 EvadePressed 当帧即时响应。
3. EvadePressed 不进入缓存。
```

---

### 17.2 Skill1Pressed

```text
if Cancel_ToSkill == true:
    if Skill1PressedThisFrame:
        if BetaEnergy >= SkillCost:
            EnterSkill()
        else:
            PlaySkillFailFeedback()
```

限制：

```text
1. HitResetWindow 开启前的 Skill1Pressed 直接忽略。
2. HitResetWindow 内 Skill1Pressed 当帧即时响应。
3. Skill1Pressed 不进入缓存。
4. BetaEnergy 不足时不进入 Skill。
```

---

### 17.3 LightAttackPressed / HeavyAttackPressed

```text
if Cancel_AttackReset == true:
    if LightAttackPressedThisFrame and CanAttack:
        EnterAttack(Light)
    else if HeavyAttackPressedThisFrame and CanAttack:
        EnterAttack(Heavy)
```

限制：

```text
1. HitResetWindow 开启前的攻击输入直接忽略。
2. HitResetWindow 内攻击输入当帧即时响应。
3. LightAttackPressed / HeavyAttackPressed 不进入缓存。
4. LightAttack 和 HeavyAttack 同帧输入时，后输入覆盖前输入。
```

---

### 17.4 GuardHeld

```text
if Cancel_ToGuard == true:
    if GuardHeld == true and CanGuard:
        EnterGuard()
```

限制：

```text
1. Guard 不进入普通缓存。
2. HitResetWindow 开启前按下 Guard，且窗口开启时 GuardHeld=true，则进入 Guard。
3. HitResetWindow 开启前按下 Guard，但窗口开启前已经松开，则不进入 Guard。
```

---

### 17.5 ReturnWindow

```text
if HitEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 18. 重置区间输入冲突处理

HitResetWindow 内多个输入同帧满足条件时，按输入消费优先级处理：

```text
EvadeInput > SkillInput > Light/HeavyAttack > GuardHeld > MoveInput
```

### 18.1 EvadePressed vs Skill1Pressed

```text
if Cancel_ToEvade == true
and EvadePressedThisFrame
and CanEvade:
    EnterEvade()
else if Cancel_ToSkill == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
```

---

### 18.2 Skill1Pressed vs AttackPressed

```text
if Cancel_ToSkill == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
else if Cancel_AttackReset == true
and AttackPressedThisFrame:
    EnterAttack()
```

---

### 18.3 AttackPressed vs GuardHeld

```text
if Cancel_AttackReset == true
and AttackPressedThisFrame:
    EnterAttack()
else if Cancel_ToGuard == true
and GuardHeld == true:
    EnterGuard()
```

---

### 18.4 无输入时自然返回

```text
if HitEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 19. 状态退出条件

| 输入 / 事件                  | 条件                                   | 下一个状态      |
| ------------------------ | ------------------------------------ | ---------- |
| HP <= 0                  | 任意时刻                                 | Dead       |
| CombatReactionIntent.Knockdown            | 任意 HitReaction 阶段                    | Knockdown  |
| CombatReactionIntent.Knockdown        | 任意 HitReaction 阶段                    | Knockdown  |
| GuardValue <= 0       | 任意 HitReaction 阶段                    | Knockdown  |
| ConsecutiveHitCount 达到上限 | CanChainToKnockdown=true             | Knockdown  |
| EvadePressed             | HitResetWindow=true 且 CanEvade=true  | Evade      |
| Skill1Pressed            | HitResetWindow=true 且 BetaEnergy 足够  | Skill      |
| LightAttackPressed       | HitResetWindow=true 且 CanAttack=true | Attack     |
| HeavyAttackPressed       | HitResetWindow=true 且 CanAttack=true | Attack     |
| GuardHeld                | HitResetWindow=true 且 CanGuard=true  | Guard      |
| HitEnd                   | MoveMagnitude > 0.1                  | Locomotion |
| HitEnd                   | MoveMagnitude <= 0.1                 | Idle       |

---

## 20. 动画需求

### 20.1 普通受击动画

| 场景   | 动画资源      |
| ---- | --------- |
| 正面受击 | Hit_Front |
| 背面受击 | Hit_Back  |
| 左侧受击 | Hit_Left  |
| 右侧受击 | Hit_Right |

### 20.2 受击恢复动画

根据受击动画决定

---

## 21. 动画参数

| 参数名                   | 类型         | 用途                                          |
| --------------------- | ---------- | ------------------------------------------- |
| PlayerState           | Enum / Int | 当前顶层状态为 HitReaction                         |
| HitReactionPhase      | Enum / Int | HitStart / HitStun / HitRecovery / HitReset |
| HitReactionLevel      | Enum / Int | Light / Medium                              |
| HitDirection          | Enum / Int | Front / Back / Left / Right                 |
| HitStunTime           | Float      | 当前受击硬直时长                                    |
| HitKnockbackDistance  | Float      | 当前受击击退距离                                    |
| MoveMagnitude         | Float      | 判断 HitEnd 后去向                               |
| MoveX                 | Float      | 记录横向移动输入                                    |
| MoveY                 | Float      | 记录纵向移动输入                                    |
| IsInCombat            | Bool       | 是否战斗模式                                      |
| IsLockOn              | Bool       | 是否锁定 Boss                                   |
| ConsecutiveHitCount   | Int        | 连续受击次数                                      |
| TriggerHitReaction    | Trigger    | 触发普通受击                                      |
| TriggerHitToEvade     | Trigger    | 受击恢复转闪避                                     |
| TriggerHitToSkill     | Trigger    | 受击恢复转技能                                     |
| TriggerHitToGuard     | Trigger    | 受击恢复转防御                                     |
| TriggerHitToAttack    | Trigger    | 受击恢复转攻击                                     |
| TriggerHitToMove      | Trigger    | 受击恢复转移动                                     |
| TriggerHitToIdle      | Trigger    | 受击恢复转待机                                     |
| TriggerHitToKnockdown | Trigger    | 连续受击 / 强攻击转 Knockdown                       |

---

## 22. 配置事件

|事件|时间|作用|
|---|--:|---|
|HitReactionStart|0.00s|进入 HitReaction|
|ApplyHitDamage|0.00s|结算 HP 伤害|
|ApplyHitKnockback|0.00s|应用受击击退|
|HitStartEnd|0.08s|进入 HitStun|
|HitStunEnd|0.333s|进入 HitRecovery|
|HitResetWindowStart|0.533s|开启受击重置区间|
|HitReactionEnd|0.633s|结束 HitReaction|

---

## 23. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|HitReactionTotalDuration|38f / 60fps = 0.633s|
|HitStartDuration|5f / 60fps = 0.083s|
|HitStunDuration|15f / 60fps = 0.25s|
|HitRecoveryDuration|12f / 60fps = 0.2s|
|HitResetStartTime|32f / 60fps = 0.533s|
|HitResetEndTime|38f / 60fps = 0.633s|
|LightHitKnockbackDistance|0.6m|
|MediumHitKnockbackDistance|1.0m|
|LightHitKnockbackDuration|0.22s|
|MediumHitKnockbackDuration|0.26s|
|HitStopOnSideCollision|true|
|MaxConsecutiveHitBeforeKnockdown|3|
|ConsecutiveHitResetTime|1.2s|
|HitToAttackBlendTime|0.05s|
|HitToEvadeBlendTime|0.05s|
|HitToGuardBlendTime|0.06s|
|HitToSkillBlendTime|0.06s|
|HitToMoveBlendTime|0.10s|
|HitToIdleBlendTime|0.10s|

---

## 24. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|EvadePressed|HitResetWindow 未开启|忽略，不缓存|
|EvadePressed|HitResetWindow 开启但 CanEvade=false|不进入 Evade|
|Skill1Pressed|HitResetWindow 未开启|忽略，不缓存|
|Skill1Pressed|HitResetWindow 开启但 BetaEnergy 不足|不进入 Skill，播放能量不足反馈|
|Skill2Pressed|任意 HitReaction 阶段|不响应|
|GuardHeld|HitResetWindow 未开启|保持 HitReaction，不缓存|
|GuardHeld|HitResetWindow 开启但 CanGuard=false|不进入 Guard|
|LightAttackPressed|HitResetWindow 未开启|忽略，不缓存|
|HeavyAttackPressed|HitResetWindow 未开启|忽略，不缓存|
|RecoverHpPressed|任意 HitReaction 阶段|不响应|
|LockOnPressed|任意 HitReaction 阶段|不响应|
|MoveInput|HitReaction 未结束|只记录，不移动|

---

## 25. 状态流转

```text
Any
+ EnemyAttackResolvedAsHit
+ HP > 0
+ 非 Guard / Evade / Knockdown 解析成功
→ HitStart
→ HitStun
→ HitRecovery
→ HitResetWindow

HitReaction
+ HP <= 0
→ Dead

HitReaction
+ CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0
→ Knockdown

HitReaction
+ ConsecutiveHitCount >= MaxConsecutiveHitBeforeKnockdown
+ CanChainToKnockdown=true
→ Knockdown

HitResetWindow
+ EvadePressed
+ CanEvade=true
→ Evade

HitResetWindow
+ Skill1Pressed
+ BetaEnergy >= SkillCost
→ Skill

HitResetWindow
+ GuardHeld
+ CanGuard=true
→ Guard

HitResetWindow
+ LightAttackPressed / HeavyAttackPressed
+ CanAttack=true
→ Attack

HitReactionEnd
+ MoveMagnitude > 0.1
→ Locomotion

HitReactionEnd
+ MoveMagnitude <= 0.1
→ Idle
```
