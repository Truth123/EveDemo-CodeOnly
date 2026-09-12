
## 状态定位

当玩家没有移动输入、没有攻击输入、没有防御输入、没有闪避输入、没有技能输入、没有回血输入，并且不处于受击、倒地、死亡等强制状态时，角色进入 Idle。

---
## 状态类型

基础状态 / 默认状态

---
## 进入条件

| 来源状态        | 进入条件                                 | 说明          |
| ----------- | ------------------------------------ | ----------- |
| Locomotion  | MoveMagnitude <= 0.1                 | 玩家停止移动      |
| Evade       | EvadeEnd 且 MoveMagnitude <= 0.1      | 闪避结束后没有移动输入 |
| Attack      | AttackEnd 且 MoveMagnitude <= 0.1     | 攻击结束后没有移动输入 |
| Guard       | GuardReleased 且 MoveMagnitude <= 0.1 | 松开防御后没有移动输入 |
| Skill       | SkillEnd 且 MoveMagnitude <= 0.1      | 技能结束后没有移动输入 |
| HitReaction | HitEnd 且 MoveMagnitude <= 0.1        | 受击恢复后没有移动输入 |
| Knockdown   | KnockdownEnd 且 MoveMagnitude <= 0.1  | 起身后没有移动输入   |
| Recover     | RecoverEnd 且 MoveMagnitude <= 0.1    | 回血结束后没有移动输入 |
|             | 战斗重置完成                               | 重开战斗后初始化    |

通用条件：

```
1. 玩家 HP > 0。
2. 当前没有处于强制状态。
3. 当前没有动作状态正在播放。
4. MoveMagnitude <= 0.1。
```

---
## 退出条件

| 输入 / 事件            | 条件                                          | 下一个状态       | 说明       |
| ------------------ | ------------------------------------------- | ----------- | -------- |
| MoveInput          | MoveMagnitude > 0.1                         | Locomotion  | 进入移动     |
| LightAttackPressed | CanAttack=true                              | Attack      | 轻攻击起手    |
| HeavyAttackPressed | CanAttack=true                              | Attack      | 重攻击起手    |
| EvadePressed       | CanEvade=true                               | Evade       | 立即闪避     |
| GuardHeld          | CanGuard=true                               | Guard       | 进入防御     |
| Skill1Pressed      | BetaEnergy >= SkillCost                     | Skill       | 释放技能     |
| RecoverHpPressed   | HealingReagentCount > 0 且 CurrentHP < MaxHP | Recover     | 使用回血     |
| CombatHitOutcome.HitReaction | 非无敌，未防御                          | HitReaction | 被命中并进入普通受击 |
| CombatHitOutcome.Knockdown   | 非无敌，未防御                          | Knockdown   | 被命中并进入击倒   |
| HP <= 0            | 任意时刻                                        | Dead        | 死亡       |

---
## 可响应输入

Idle 是最高自由度的输入状态，应该响应所有 Gameplay 输入。

| 输入           | 响应方式    | 说明                                 |
| ------------ | ------- | ---------------------------------- |
| Move         | 立即响应    | MoveMagnitude > 0.1 时进入 Locomotion |
| Look         | 持续响应    | 控制相机                               |
| LockOn       | 立即响应    | 只切换 ControlMode，不改变顶层状态            |
| LightAttack  | 立即响应    | 进入 Attack 轻攻击起手                    |
| HeavyAttack  | 立即响应    | 进入 Attack 重攻击起手                    |
| EvadeInput   | 立即响应    | 按下立即进入 Evade                       |
| Guard        | Hold 响应 | GuardHeld=true 进入 Guard            |
| Skill1Input  | 资源足够则响应 | 进入 Skill                           |
| RecoverInput | 条件满足则响应 | 进入 Recover                         |

---
## 状态区间

循环状态, 不需要 Start / Active / Recovery / ResetWindow。

---
## 移动规则

Idle 状态下不主动移动。  
角色可以受到外部位移影响，例如轻微受击残余位移、地面重力贴地等。

---
## 转向规则

Idle 的转向取决于当前 ControlMode。
**自由视角 Free Mode:** 

```
1. 玩家无移动输入时，角色保持当前朝向。
2. Look 输入只旋转相机，不强制旋转角色。
```

**锁定视角 LockOn Mode:**

```
1. 角色保持朝向当前锁定 Boss。
2. 角色朝向可以平滑插值，不要瞬间旋转。
3. Boss 死亡或解除锁定后，回到自由视角朝向规则。
```

---
## 攻击 / 防御 / 无敌规则

| 项目           | 规则                    |
| ------------ | --------------------- |
| 攻击判定         | 无                     |
| 防御判定         | 无                     |
| 无敌帧          | 无                     |
| 霸体           | 无                     |
| 受击           | 可被敌人命中                |
| PerfectGuard | 不可触发，必须进入 Guard 后才能触发 |
| PerfectEvade | 不可触发，必须进入 Evade 后才能触发 |

---
## 输入缓存规则

Idle 不需要输入缓存。

---
## 动画需求

参考 [[动画资源]] 文档

---

## 动画参数

| 参数名                  | 类型         | 用途                |
| -------------------- | ---------- | ----------------- |
| PlayerState          | Int / Enum | 标记当前顶层状态为 Idle    |
| ControlMode          | Int / Enum | 区分 Free / LockOn  |
| IsInCombat           | Bool       | 是否处于战斗模式          |
| IsLockOn             | Bool       | 是否锁定 Boss         |
| IsWeaponDrawn        | Bool       | 是否拔剑              |
| MoveMagnitude        | Float      | 判断是否进入 Locomotion |
| MoveX                | Float      | 记录横向移动输入          |
| MoveY                | Float      | 记录纵向移动输入          |
| TargetAngle          | Float      | 锁定状态下玩家到 Boss 的角度 |
| TriggerWeaponDraw    | Trigger    | 触发拔剑过渡            |
| TriggerWeaponSheathe | Trigger    | 触发收剑过渡            |
| TriggerTurnLeft      | Trigger    | 触发原地左转            |
| TriggerTurnRight     | Trigger    | 触发原地右转            |

Idle 动画选择规则：  
  
| 条件                                | 动画状态                  | 对应动画                                                       |     |
| --------------------------------- | --------------------- | ---------------------------------------------------------- | --- |
| IsInCombat=false 且 IsLockOn=false | Idle.NormalIdle       | Proto_Idle                                                 |     |
| IsInCombat=true 且 IsLockOn=false  | Idle.BattleIdle       | Proto_Battle_Idle                                          |     |
| IsInCombat=true 且 IsLockOn=true   | Idle.LockOnBattleIdle | Proto_Lockon_Battle_Idle                                   |     |
| 普通待机进入战斗待机                        | WeaponDraw            | Eve_Weapon_Start_Anim / Proto_Battle_Start                 |     |
| 战斗待机退出普通待机                        | WeaponSheathe         | Eve_Weapon_End_Anim                                        |     |
| Idle 转向进入移动                       | IdleTurn              | Proto_Battle_Run_idleToTurn_L/R 或 Proto_Run_idleToTurn_L/R |     |

---

## 动画事件

Idle 第一版不需要动画事件。

---

## 数值参数

|参数名|建议值|说明|
|---|--:|---|
|`MoveThreshold`|`0.1`|MoveMagnitude 大于该值时退出 Idle，进入 Locomotion|
|`IdleToMoveBlendTime`|`0.10s - 0.20s`|Idle 到 Locomotion 的动画混合时间|
|`IdleToAttackBlendTime`|`0.03s - 0.08s`|Idle 到 Attack 的动画混合时间，建议较短，保证攻击响应|
|`IdleToEvadeBlendTime`|`0.03s - 0.06s`|Idle 到 Evade 的动画混合时间，保证闪避响应|
|`IdleToGuardBlendTime`|`0.03s - 0.08s`|Idle 到 Guard 的动画混合时间|
|`IdleToSkillBlendTime`|`0.05s - 0.10s`|Idle 到 Skill 的动画混合时间|
|`IdleToRecoverBlendTime`|`0.08s - 0.15s`|Idle 到 Recover 的动画混合时间|
|`FreeIdleRotateSpeed`|`0°/s`|自由视角 Idle 下不主动旋转角色|
|`LockOnIdleRotateSpeed`|`720°/s - 900°/s`|锁定 Idle 下角色朝向 Boss 的旋转速度|
|`LockOnFaceTargetAngleLimit`|`3° - 5°`|小于该角度时认为已朝向目标，避免抖动|
|`AutoBattleModeEnterDistance`|可选，`8m - 12m`|Boss 靠近到该距离时自动进入战斗待机|
|`AutoBattleModeExitTime`|可选，`5s - 10s`|脱离战斗一段时间后收剑回普通待机|
|`WeaponDrawBlendTime`|`0.10s - 0.20s`|拔剑动画混合时间|
|`WeaponSheatheBlendTime`|`0.10s - 0.20s`|收剑动画混合时间|

---

## 验收标准

1. 玩家无输入且 MoveMagnitude <= 0.1 时，角色稳定保持 Idle。  
2. 玩家 HP > 0，且没有处于其他动作状态或强制状态时，才允许进入 Idle。  
3. Idle 不使用输入缓存，所有合法输入立即响应。  
4. Idle 中 MoveMagnitude > 0.1 时进入 Locomotion。  
5. Idle 中 LightAttackPressed 且 CanAttack=true 时进入 Attack。  
6. Idle 中 HeavyAttackPressed 且 CanAttack=true 时进入 Attack。  
7. Idle 中 EvadePressed 且 CanEvade=true 时进入 Evade。  
8. Idle 中 GuardHeld 且 CanGuard=true 时进入 Guard。  
9. Idle 中 Skill1Pressed 且 BetaEnergy 足够时进入 Skill。  
10. Idle 中 RecoverInput 且 HealingReagentCount > 0、CurrentHP < MaxHP 时进入 Recover。  
11. Idle 中 RecoverInput 但 HP 已满时，不进入 Recover。  
12. Idle 中 RecoverInput 但 HealingReagentCount <= 0 时，不进入 Recover。  
13. Idle 中 LockOnPressed 只切换 ControlMode，不改变顶层状态。  
14. 非战斗、非锁定状态下播放 Proto_Idle。  
15. 战斗、非锁定状态下播放 Proto_Battle_Idle。  
16. 战斗、锁定状态下播放 Proto_Lockon_Battle_Idle。  
17. 锁定状态下角色平滑朝向 Boss，不出现明显抖动。  
18. Idle 中被普通攻击命中时进入 HitReaction。  
19. Idle 中被强攻击或击倒攻击命中时进入 Knockdown。  
20. Idle 中 HP <= 0 时立即进入 Dead。
