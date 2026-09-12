## 1. 状态定位

`Knockdown` 是玩家角色的大硬直 / 倒地状态，用于处理玩家被重击、击倒攻击、破防攻击、连续受击打倒后的击飞、倒地、倒地循环、起身恢复、起身后重置响应、起身结束后返回 Idle / Locomotion 等逻辑。

`Knockdown` 是强制状态。敌人攻击命中玩家时不直接进入 `Knockdown`，而是先进入 `CombatHitResolver`。当攻击类型为 `CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0`，或普通受击连续次数达到倒地条件时，进入 `Knockdown`。

当前实现修订：

- Boss 对玩家的正式命中不再通过 CombatReactionIntent.Knockdown / GuardValue <= 0 等攻击类型决定击倒，只通过 `CombatReactionIntent.Knockdown` 表达击倒意图。
- 当前正式 `Reaction_Player_Knockdown.asset` 总时长为 `5.0s`，Reaction 窗口为 `Stun 0.0-2.25s / Recovery 2.25-3.6833s / CanReturn 3.6833-5.0s`；本文后续 `1.60s` 表格是早期节奏示例，不作为运行时硬编码。运行时始终读取 Timeline 窗口。
- 当前资产在 `4.0-5.0s` 开放 `Cancel_MovementReturn`；窗口内有实时 MoveInput 时立即返回 Locomotion，窗口外只记录输入供自然结束判断。
- 击倒动画按 `PlayerKnockdownTypeId` 选择：背后来源命中使用 Forward，其余方向使用 Backward。
- 倒地 Loop 阶段默认忽略 `CombatReactionIntent.HitReaction` 普通地面命中，但致命伤仍进入 Dead。
- 连续普通受击转 Knockdown 本阶段不实现，避免重新引入复杂攻击类型和受击等级。

`Knockdown` 负责：

```text
1. 处理 CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0 进入 Knockdown。
2. 处理连续受击转 Knockdown。
3. 播放击倒开始动画。
4. 应用击倒位移 / 击飞 / 倒地冲击。
5. 管理 KnockdownStart / Knockdown / GetUp / KnockdownResetWindow。
6. 管理倒地期间不可控。
7. 管理起身恢复阶段。
8. 管理 KnockdownResetWindow 内的 Attack / Evade / Guard / Skill / Move 响应。
9. 管理 KnockdownEnd 后返回 Idle / Locomotion。
10. 清除当前所有输入缓存。
```

`Knockdown` 不负责：

```text
1. 普通受击 HitReaction。
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
Knockdown
├── KnockdownStart
├── Knockdown
├── GetUp
└── KnockdownResetWindow
```

---

## 3. 进入条件

| 来源状态        | 条件                                                    | 目标状态      | 说明        |
| ----------- | ----------------------------------------------------- | --------- | --------- |
| Any         | EnemyAttackResolvedAsKnockdown 且 HP > 0               | Knockdown | 强制倒地      |
| Idle        | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown 且非无敌、未防御                 | Knockdown | 待机被击倒     |
| Locomotion  | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown 且非无敌、未防御                 | Knockdown | 移动中被击倒    |
| Attack      | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown 且非霸体、非无敌                 | Knockdown | 攻击中被击倒    |
| Skill       | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown                          | Knockdown | 技能中被强攻击击倒 |
| Guard       | GuardValue <= 0 或 GuardValue <= 0                          | Knockdown | 破防倒地      |
| Guard       | EnemyAttackHit 且 AttackData.GuardValue <= 0=true           | Knockdown | 破防攻击      |
| Evade       | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown 且非 EvadeInvincibleWindow | Knockdown | 闪避后摇被击倒   |
| HitReaction | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0          | Knockdown | 受击中转大硬直   |
| HitReaction | ConsecutiveHitCount 达到上限                              | Knockdown | 连续受击打倒    |
| Recover     | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown                          | Knockdown | 回血被击倒     |

受击解析优先级：

```text
1. HP <= 0 → Dead
2. Guard + PerfectGuard 成功 → Guard.PerfectGuard
3. Evade + PerfectEvade 成功 → Evade.PerfectEvade
4. EvadeInvincibleWindow=true → 忽略伤害
5. Guard + 可普通防御 → GuardHit
6. CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0 → Knockdown
7. 其他命中 → HitReaction
```

---

## 4. 进入时处理

进入 `Knockdown` 的第一帧执行：

```text
1. Set PlayerState = Knockdown。
2. Set KnockdownPhase = KnockdownStart。
3. 记录 KnockdownData。
4. 结算 HP 伤害。
5. 计算 KnockdownDirection。
6. 计算 KnockdownType。
7. 清除所有输入缓存。
8. 关闭玩家攻击 Hitbox。
9. 关闭 SkillHitbox。
10. 关闭 GuardBlockActive。
11. 关闭 EvadeInvincibleWindow。
12. 关闭 PerfectEvadeWindow。
13. 关闭 PerfectGuardWindow。
14. 关闭所有 CancelWindow / ResetWindow。
15. 取消当前动作主动位移。
16. 应用击倒初始位移。
17. 播放击倒开始动画。
```

---

## 5. KnockdownData 数据结构

```text
KnockdownData
{
    AttackId
    AttackerId
    AttackType
    Damage
    PoiseDamage
    KnockdownDirection
    HitPosition
    KnockbackDirection
    KnockbackDistance
    KnockdownDuration
    GetUpDuration
    KnockdownType
    CanBeHitOnGround
}
```

| 字段                 | 说明                                   |
| ------------------ | ------------------------------------ |
| AttackId           | 本次敌人攻击编号                             |
| AttackerId         | 攻击来源                                 |
| AttackType         | CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0 |
| Damage             | HP 伤害                                |
| PoiseDamage        | 韧性伤害                                 |
| KnockdownDirection | 击倒方向                                 |
| HitPosition        | 命中位置                                 |
| KnockbackDirection | 击退 / 击飞方向                            |
| KnockbackDistance  | 击退距离                                 |
| KnockdownDuration  | 倒地循环最短持续时间                           |
| GetUpDuration      | 起身时长                                 |
| KnockdownType      | Forward / Backward                   |
| CanBeHitOnGround   | 倒地期间是否可被追击                           |

---

## 6. KnockdownStart

`KnockdownStart` 是击倒启动阶段。

```text
作用：
1. 中断玩家当前动作。
2. 播放击倒开始动画。
3. 结算 HP 伤害。
4. 应用击倒初始位移。
5. 关闭所有玩家主动判定。
6. 禁止玩家输入切换。
```

时间：

```text
KnockdownStartDuration = 0.36s
```

窗口：

```text
0.00 - 0.36 KnockdownStart
```

规则：

```text
1. KnockdownStart 中不响应 MoveInput。
2. KnockdownStart 中 LookInput 持续响应。
3. KnockdownStart 中不响应 LockOnPressed。
4. KnockdownStart 中不响应 LightAttackPressed / HeavyAttackPressed。
5. KnockdownStart 中不响应 EvadePressed。
6. KnockdownStart 中不响应 GuardHeld。
7. KnockdownStart 中不响应 Skill1Pressed。
8. KnockdownStart 中不响应 RecoverHpPressed。
9. KnockdownStart 中 HP <= 0，进入 Dead。
10. KnockdownStart 中再次受到 CombatReactionIntent.Knockdown，不重置 KnockdownStart。
```

---

## 7. Knockdown

`Knockdown` 是倒地循环阶段。

```text
作用：
1. 播放倒地循环动画。
2. 保持玩家不可控。
3. 等待最短倒地时间结束。
4. 处理倒地期间受击。
5. 进入 GetUp。
```

时间：

```text
KnockdownLoopMinDuration = 0.60s
```

窗口：

```text
0.36 - 0.96 Knockdown
```

规则：

```text
1. Knockdown 中不响应 MoveInput。
2. Knockdown 中 LookInput 持续响应。
3. Knockdown 中不响应 LockOnPressed。
4. Knockdown 中不响应 Attack / Evade / Guard / Skill / Recover。
5. Knockdown 中不缓存任何输入。
6. Knockdown 中 HP <= 0，进入 Dead。
7. KnockdownLoopMinDuration 结束后进入 GetUp。
```

倒地受击规则：

```text
if CanBeHitOnGround == false:
    IgnoreGroundHitExceptFatal()

if CanBeHitOnGround == true:
    ApplyGroundHitDamage()
    StayKnockdown()

if HP <= 0:
    EnterDead()
```

---

## 8. GetUp

`GetUp` 是起身恢复阶段。

```text
作用：
1. 播放起身动画。
2. 玩家从倒地状态恢复站立。
3. 读取 MoveInput，用于 `Cancel_MovementReturn` 窗口内提前返回 Locomotion，或 KnockdownEnd 后判断回 Idle / Locomotion。
4. 不开启输入缓存。
5. 后段进入 KnockdownResetWindow。
```

时间：

```text
GetUpDuration = 0.54s
```

窗口：

```text
0.96 - 1.50 GetUp
```

规则：

```text
1. GetUp 中 MoveInput 默认只读取；只有 `Cancel_MovementReturn` 激活时才允许转入 Locomotion。
2. GetUp 中 LookInput 持续响应。
3. GetUp 中 LockOnPressed 不响应。
4. GetUp 前段不响应 Attack / Evade / Guard / Skill / Recover。
5. GetUp 中不缓存任何输入。
6. GetUp 后段进入 KnockdownResetWindow。
```

---

## 9. KnockdownResetWindow

`KnockdownResetWindow` 是起身恢复末段的重置区间。

```text
作用：
1. 允许 EvadePressed 即时进入 Evade。
2. 允许 Skill1Pressed 即时进入 Skill。
3. 允许 LightAttackPressed / HeavyAttackPressed 即时进入 Attack。
4. 允许 GuardHeld 即时进入 Guard。
5. `Cancel_MovementReturn` 激活时允许实时 MoveInput 进入 Locomotion。
6. 允许动作结束时自然回 Idle / Locomotion。
```

时间：

```text
KnockdownResetWindow = 1.30s - 1.60s
```

窗口：

```text
1.30 - 1.60 KnockdownResetWindow
1.30 - 1.60 EvadeResetResponseWindow
1.30 - 1.60 SkillResetResponseWindow
1.30 - 1.60 GuardResetResponseWindow
1.30 - 1.60 AttackResetResponseWindow
1.48 - 1.60 ReturnWindow
```

规则：

```text
1. EvadePressed 只在 KnockdownResetWindow 内即时响应，不缓存。
2. Skill1Pressed 只在 KnockdownResetWindow 内即时响应，不缓存。
3. LightAttackPressed / HeavyAttackPressed 只在 KnockdownResetWindow 内即时响应，不缓存。
4. GuardHeld 只在 KnockdownResetWindow 内检测，不缓存。
5. RecoverHpPressed 不响应。
6. LockOnPressed 不响应。
```

---

## 10. 击倒方向规则

击倒方向由攻击来源相对玩家朝向计算：

```text
KnockdownDirection = DirectionFromAttackerToPlayerRelativeToPlayerFacing
```

方向映射：

| KnockdownDirection | KnockdownType | 动画                              |
| ------------------ | ------------- | ------------------------------- |
| Front              | Backward      | Result_State_KnockDown_Start_Bw |
| Back               | Forward       | Result_State_KnockDown_Start_Fw |
| Left               | Backward      | Result_State_KnockDown_Start_Bw |
| Right              | Backward      | Result_State_KnockDown_Start_Bw |

规则：

```text
1. 正面被击中时，角色向后倒地。
2. 背面被击中时，角色向前倒地。
3. 左右侧被击中时，根据攻击配置选择向后倒地。
4. KnockdownType 在进入 KnockdownStart 第一帧锁定。
```

---

## 11. 击倒等级规则

`Knockdown` 处理强受击、击倒攻击、破防攻击、连续受击打倒。

|AttackType|目标状态|
|---|---|
|CombatReactionIntent.HitReaction|HitReaction|
|CombatReactionIntent.Knockdown|Knockdown|
|CombatReactionIntent.Knockdown|Knockdown|
|GuardValue <= 0|Knockdown|

规则：

```text
if AttackType == CombatReactionIntent.Knockdown:
    EnterKnockdown()

if AttackType == CombatReactionIntent.Knockdown:
    EnterKnockdown()

if AttackType == GuardValue <= 0:
    EnterKnockdown()

if ConsecutiveHitCount >= MaxConsecutiveHitBeforeKnockdown:
    EnterKnockdown()
```

---

## 12. 击倒位移规则

```text
1. Knockdown 进入时通过 PlayerStateContext.RequestActionMotion(...) 请求击倒位移。
2. 首版统一使用 KnockdownBackward。
3. 击倒方向优先使用 CombatHitData.HitDirection.normalized。
4. 如果 HitDirection 无效，使用 player.position - CombatHitData.HitPosition。
5. 如果仍无效，fallback 为 -player.forward。
6. 击倒位移由 PlayerMovementMotor 通过 PlayerActionMotionRuntime 和 CharacterController.Move() 执行。
7. KnockdownStart 中完成主要击退位移，GetUp 阶段不继续主动滑动。
8. 击倒位移不能穿墙，不能穿过 Boss 大体积碰撞。
9. StopOnSideCollision=true，遇到侧向阻挡时停止剩余水平动作位移，但不提前结束 Knockdown。
```

位移参数：

```text
KnockdownBackward.Distance = 2.2m
KnockdownBackward.Duration = 0.36s
StopOnSideCollision = true
```

---

## 13. 倒地受击规则

```text
1. KnockdownStart 中受到新的普通攻击，不切换状态。
2. KnockdownStart 中受到新的 CombatReactionIntent.Knockdown，不重置 KnockdownStart。
3. Knockdown 循环阶段是否受击由 CanBeHitOnGround 决定。
4. GetUp 阶段受到普通攻击，进入 HitReaction。
5. GetUp 阶段受到 CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0，重新进入 Knockdown。
6. KnockdownResetWindow 中受到攻击，按普通 CombatHitResolver 解析。
7. HP <= 0 时立即进入 Dead。
```

倒地追击参数：

```text
CanBeHitOnGround = false
```

---

## 14. Knockdown 输入响应规则

| 输入                 | 响应方式                        | 是否缓存 |
| ------------------ | --------------------------- | ---: |
| MoveInput          | `Cancel_MovementReturn` 内转 Locomotion；其余时间只记录用于 KnockdownEnd 后去向 |    否 |
| LookInput          | 持续响应                        |    否 |
| LockOnPressed      | 不响应                         |    否 |
| LightAttackPressed | KnockdownResetWindow 内即时响应  |    否 |
| HeavyAttackPressed | KnockdownResetWindow 内即时响应  |    否 |
| EvadePressed       | KnockdownResetWindow 内即时响应  |    否 |
| EvadeHeld          | 不响应                         |    否 |
| EvadeReleased      | 不响应                         |    否 |
| GuardHeld          | KnockdownResetWindow 内即时响应  |    否 |
| Skill1Pressed      | KnockdownResetWindow 内即时响应  |    否 |
| Skill2Pressed      | 不响应                         |    否 |
| RecoverHpPressed   | 不响应                         |    否 |

---

## 15. Knockdown 时间轴与窗口

以 `KnockdownTotalDuration = 1.60s` 为标准时间轴：

```text
0.00s ───────────────────────────────────── 1.60s

0.00 - 0.36  KnockdownStart
0.36 - 0.96  Knockdown
0.96 - 1.50  GetUp
1.30 - 1.60  KnockdownResetWindow

1.30 - 1.60  EvadeResetResponseWindow
1.30 - 1.60  SkillResetResponseWindow
1.30 - 1.60  GuardResetResponseWindow
1.30 - 1.60  AttackResetResponseWindow
1.48 - 1.60  ReturnWindow
```

---

## 16. 输入缓存规则

Knockdown 状态不使用输入缓存。

```text
1. Knockdown 进入时清除所有输入缓存。
2. Knockdown 状态内不创建任何新的输入缓存。
3. EvadePressed 不缓存。
4. Skill1Pressed 不缓存。
5. LightAttackPressed / HeavyAttackPressed 不缓存。
6. GuardHeld 不缓存。
7. RecoverHpPressed 不缓存。
8. LockOnPressed 不缓存。
9. KnockdownEnd 时再次清除 Knockdown 状态内的临时输入记录。
```

---

## 17. KnockdownResetWindow 响应规则

### 17.1 EvadePressed

```text
if KnockdownResetWindow == true:
    if EvadePressedThisFrame and CanEvade:
        EnterEvade()
```

限制：

```text
1. KnockdownResetWindow 开启前的 EvadePressed 直接忽略。
2. KnockdownResetWindow 内 EvadePressed 当帧即时响应。
3. EvadePressed 不进入缓存。
```

---

### 17.2 Skill1Pressed

```text
if KnockdownResetWindow == true:
    if Skill1PressedThisFrame:
        if BetaEnergy >= SkillCost:
            EnterSkill()
        else:
            PlaySkillFailFeedback()
```

限制：

```text
1. KnockdownResetWindow 开启前的 Skill1Pressed 直接忽略。
2. KnockdownResetWindow 内 Skill1Pressed 当帧即时响应。
3. Skill1Pressed 不进入缓存。
4. BetaEnergy 不足时不进入 Skill。
```

---

### 17.3 LightAttackPressed / HeavyAttackPressed

```text
if KnockdownResetWindow == true:
    if LightAttackPressedThisFrame and CanAttack:
        EnterAttack(Light)
    else if HeavyAttackPressedThisFrame and CanAttack:
        EnterAttack(Heavy)
```

限制：

```text
1. KnockdownResetWindow 开启前的攻击输入直接忽略。
2. KnockdownResetWindow 内攻击输入当帧即时响应。
3. LightAttackPressed / HeavyAttackPressed 不进入缓存。
4. LightAttack 和 HeavyAttack 同帧输入时，后输入覆盖前输入。
```

---

### 17.4 GuardHeld

```text
if KnockdownResetWindow == true:
    if GuardHeld == true and CanGuard:
        EnterGuard()
```

限制：

```text
1. Guard 不进入普通缓存。
2. KnockdownResetWindow 开启前按下 Guard，且窗口开启时 GuardHeld=true，则进入 Guard。
3. KnockdownResetWindow 开启前按下 Guard，但窗口开启前已经松开，则不进入 Guard。
```

---

### 17.5 ReturnWindow

```text
if Cancel_MovementReturn == true
and MoveMagnitude > MoveThreshold:
    EnterLocomotion()
else if KnockdownEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 18. 重置区间输入冲突处理

KnockdownResetWindow 内多个输入同帧满足条件时，按输入消费优先级处理：

```text
EvadeInput > SkillInput > Light/HeavyAttack > GuardHeld > MoveInput
```

### 18.1 EvadePressed vs Skill1Pressed

```text
if KnockdownResetWindow == true
and EvadePressedThisFrame
and CanEvade:
    EnterEvade()
else if KnockdownResetWindow == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
```

---

### 18.2 Skill1Pressed vs AttackPressed

```text
if KnockdownResetWindow == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
else if KnockdownResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
```

---

### 18.3 AttackPressed vs GuardHeld

```text
if KnockdownResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
else if KnockdownResetWindow == true
and GuardHeld == true:
    EnterGuard()
```

---

### 18.4 无输入时自然返回

```text
if KnockdownEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 19. 状态退出条件

|输入 / 事件|条件|下一个状态|
|---|---|---|
|HP <= 0|任意时刻|Dead|
|EvadePressed|KnockdownResetWindow=true 且 CanEvade=true|Evade|
|Skill1Pressed|KnockdownResetWindow=true 且 BetaEnergy 足够|Skill|
|LightAttackPressed|KnockdownResetWindow=true 且 CanAttack=true|Attack|
|HeavyAttackPressed|KnockdownResetWindow=true 且 CanAttack=true|Attack|
|GuardHeld|KnockdownResetWindow=true 且 CanGuard=true|Guard|
|MoveInput|Cancel_MovementReturn=true 且 MoveMagnitude > 0.1|Locomotion|
|KnockdownEnd|MoveMagnitude > 0.1|Locomotion|
|KnockdownEnd|MoveMagnitude <= 0.1|Idle|

---

## 20. 动画需求

### 20.1 击倒动画

|场景|动画资源|
|---|---|
|向后击倒开始|Result_State_KnockDown_Start_Bw|
|向前击倒开始|Result_State_KnockDown_Start_Fw|
|倒地循环|Result_State_KnockDown_Loop|
|起身恢复|Result_State_KnockDown_End|

---

## 21. 动画参数

| 参数名                        | 类型         | 用途                                |
| -------------------------- | ---------- | --------------------------------- |
| PlayerState                | Enum / Int | 当前顶层状态为 Knockdown                 |
| KnockdownPhase             | Enum / Int | Start / Knockdown / GetUp / Reset |
| KnockdownType              | Enum / Int | Forward / Backward                |
| KnockdownDirection         | Enum / Int | Front / Back / Left / Right       |
| KnockdownKnockbackDistance | Float      | 当前击倒击退距离                          |
| MoveMagnitude              | Float      | 判断 KnockdownEnd 后去向               |
| MoveX                      | Float      | 记录横向移动输入                          |
| MoveY                      | Float      | 记录纵向移动输入                          |
| IsInCombat                 | Bool       | 是否战斗模式                            |
| IsLockOn                   | Bool       | 是否锁定 Boss                         |
| IsGroundedKnockdown        | Bool       | 是否处于倒地循环                          |
| TriggerKnockdown           | Trigger    | 触发击倒                              |
| TriggerGetUp               | Trigger    | 触发起身                              |
| TriggerKnockdownToEvade    | Trigger    | 起身恢复转闪避                           |
| TriggerKnockdownToSkill    | Trigger    | 起身恢复转技能                           |
| TriggerKnockdownToGuard    | Trigger    | 起身恢复转防御                           |
| TriggerKnockdownToAttack   | Trigger    | 起身恢复转攻击                           |
| TriggerKnockdownToMove     | Trigger    | 起身恢复转移动                           |
| TriggerKnockdownToIdle     | Trigger    | 起身恢复转待机                           |
| TriggerKnockdownToDead     | Trigger    | 倒地死亡                              |

---

## 22. 配置事件

|事件|时间|作用|
|---|--:|---|
|KnockdownStart|0.00s|进入 Knockdown|
|ApplyKnockdownDamage|0.00s|结算 HP 伤害|
|ApplyKnockdownImpulse|0.00s|应用击倒位移|
|KnockdownStartEnd|0.36s|进入倒地循环|
|KnockdownLoopStart|0.36s|播放倒地循环|
|GetUpStart|0.96s|进入起身|
|KnockdownResetWindowStart|1.30s|开启起身重置区间|
|ReturnWindowStart|1.48s|允许自然回 Idle / Locomotion|
|KnockdownEnd|1.60s|结束 Knockdown|

---

## 23. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|KnockdownTotalDuration|1.60s|
|KnockdownStartDuration|0.36s|
|KnockdownLoopMinDuration|0.60s|
|GetUpDuration|0.54s|
|KnockdownResetStartTime|1.30s|
|KnockdownResetEndTime|1.60s|
|ReturnWindowStartTime|1.48s|
|KnockdownKnockbackDistance|2.2m|
|KnockdownKnockbackDuration|0.36s|
|KnockdownStopOnSideCollision|true|
|CanBeHitOnGround|false|
|KnockdownToAttackBlendTime|0.05s|
|KnockdownToEvadeBlendTime|0.05s|
|KnockdownToGuardBlendTime|0.06s|
|KnockdownToSkillBlendTime|0.06s|
|KnockdownToMoveBlendTime|0.10s|
|KnockdownToIdleBlendTime|0.10s|

---

## 24. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|EvadePressed|KnockdownResetWindow 未开启|忽略，不缓存|
|EvadePressed|KnockdownResetWindow 开启但 CanEvade=false|不进入 Evade|
|Skill1Pressed|KnockdownResetWindow 未开启|忽略，不缓存|
|Skill1Pressed|KnockdownResetWindow 开启但 BetaEnergy 不足|不进入 Skill，播放能量不足反馈|
|Skill2Pressed|任意 Knockdown 阶段|不响应|
|GuardHeld|KnockdownResetWindow 未开启|保持 Knockdown，不缓存|
|GuardHeld|KnockdownResetWindow 开启但 CanGuard=false|不进入 Guard|
|LightAttackPressed|KnockdownResetWindow 未开启|忽略，不缓存|
|HeavyAttackPressed|KnockdownResetWindow 未开启|忽略，不缓存|
|RecoverHpPressed|任意 Knockdown 阶段|不响应|
|LockOnPressed|任意 Knockdown 阶段|不响应|
|MoveInput|Cancel_MovementReturn 未开启|只记录，不移动|
|MoveInput|Cancel_MovementReturn 开启|转入 Locomotion；不缓存|

---

## 25. 状态流转

```text
Any
+ EnemyAttackResolvedAsKnockdown
+ HP > 0
→ KnockdownStart
→ Knockdown
→ GetUp
→ KnockdownResetWindow

Guard
+ GuardValue <= 0 / GuardValue <= 0
→ KnockdownStart

HitReaction
+ CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0
→ KnockdownStart

HitReaction
+ ConsecutiveHitCount >= MaxConsecutiveHitBeforeKnockdown
+ CanChainToKnockdown=true
→ KnockdownStart

Knockdown
+ HP <= 0
→ Dead

GetUp
+ EnemyCombatReactionIntent.HitReaction
→ HitReaction

GetUp
+ CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown / GuardValue <= 0
→ KnockdownStart

KnockdownResetWindow
+ EvadePressed
+ CanEvade=true
→ Evade

KnockdownResetWindow
+ Skill1Pressed
+ BetaEnergy >= SkillCost
→ Skill

KnockdownResetWindow
+ GuardHeld
+ CanGuard=true
→ Guard

KnockdownResetWindow
+ LightAttackPressed / HeavyAttackPressed
+ CanAttack=true
→ Attack

Cancel_MovementReturn
+ MoveMagnitude > 0.1
→ Locomotion

KnockdownEnd
+ MoveMagnitude > 0.1
→ Locomotion

KnockdownEnd
+ MoveMagnitude <= 0.1
→ Idle
```
