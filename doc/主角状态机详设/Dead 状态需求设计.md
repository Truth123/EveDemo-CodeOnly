## 1. 状态定位

`Dead` 是玩家角色死亡状态，用于处理玩家 HP 归零后的死亡动画、输入锁定、战斗失败、战斗重置等逻辑。

`Dead` 是最高优先级强制状态。任意状态下，只要 `HP <= 0`，状态机立即进入 `Dead`。进入 `Dead` 后，玩家不能再执行移动、攻击、闪避、防御、技能、回血、锁定等 Gameplay 操作。

当前实现修订：

- 死亡判定仍只由 HP 和 `CombatHitOutcome.Dead` 决定。
- `PlayerDeadTypeId` 只负责死亡表现选择：`Stand` 表示站立死亡，`Down` 表示倒地死亡。
- 进入 Dead 前或上一状态为 `Knockdown` 时选择 `Down`，其他情况选择 `Stand`。
- Boss 对玩家的正式命中不再用攻击来源决定死亡表现；死亡姿态只看进入 Dead 前的玩家状态。

`Dead` 负责：

```text
1. 处理任意状态下 HP <= 0 进入 Dead。
2. 中断当前玩家动作。
3. 清除所有输入缓存。
4. 关闭玩家所有攻击判定。
5. 关闭玩家所有防御判定。
6. 关闭玩家所有无敌帧和完美判定窗口。
7. 停止所有 CancelWindow / ResetWindow。
8. 播放死亡动画。
9. 锁定玩家 Gameplay 输入。
10. 保留 LookInput，用于死亡后观察。
11. 触发战斗失败流程。
12. 响应 ResetBattle，重新初始化战斗并回到 Idle。
```

`Dead` 不负责：

```text
1. 普通受击 HitReaction。
2. Knockdown 倒地与起身。
3. PerfectGuard 判定。
4. PerfectEvade 判定。
5. Guard 防御判定。
6. 攻击判定。
7. 技能判定。
8. 回血。
9. 移动。
10. 输入缓存。
```

---

## 2. 状态分层

```text
Dead
```

`Dead` 不再拆分标准动作阶段。  
死亡动画表现可以分为站立死亡、倒地死亡，但逻辑上仍属于同一个 `Dead` 顶层状态。

---

## 3. 进入条件

| 来源状态        | 条件      | 目标状态 | 说明      |
| ----------- | ------- | ---- | ------- |
| Any         | HP <= 0 | Dead | 最高优先级死亡 |
| Idle        | HP <= 0 | Dead | 待机死亡    |
| Locomotion  | HP <= 0 | Dead | 移动中死亡   |
| Evade       | HP <= 0 | Dead | 闪避中死亡   |
| Attack      | HP <= 0 | Dead | 攻击中死亡   |
| Guard       | HP <= 0 | Dead | 防御中死亡   |
| Skill       | HP <= 0 | Dead | 技能中死亡   |
| HitReaction | HP <= 0 | Dead | 受击死亡    |
| Knockdown   | HP <= 0 | Dead | 倒地死亡    |
| Recover     | HP <= 0 | Dead | 回血中死亡   |

受击解析优先级：

```text
1. HP <= 0 → Dead
2. Guard + PerfectGuard 成功 → Guard.PerfectGuard
3. Evade + PerfectEvade 成功 → Evade.PerfectEvade
4. EvadeInvincibleWindow=true → 忽略伤害
5. Guard + 可普通防御 → GuardHit
6. `CombatReactionIntent=DamageOnly` → 只扣 HP，不进入受击状态
7. `CombatReactionIntent=Knockdown` → Knockdown
8. `CombatReactionIntent=HitReaction` → HitReaction
```

---

## 4. 进入时处理

进入 `Dead` 的第一帧执行：

```text
1. Set PlayerState = Dead。
2. 设置 IsDead=true。
3. 设置 CurrentHP=0。
4. 清除所有输入缓存。
5. 关闭玩家攻击 Hitbox。
6. 关闭 SkillHitbox。
7. 关闭 GuardBlockActive。
8. 关闭 EvadeInvincibleWindow。
9. 关闭 PerfectEvadeWindow。
10. 关闭 PerfectGuardWindow。
11. 关闭所有 ComboWindow。
12. 关闭所有 CancelWindow。
13. 关闭所有 ResetWindow。
14. 取消当前动作主动位移。
15. 停止玩家移动控制。
16. 停止玩家旋转控制。
17. 停止 SprintIntent。
18. 清除 LockOn 目标。
19. 设置 IsLockOn=false。
20. 设置 ControlMode=Free。
21. 根据死亡来源选择死亡动画。
22. 播放死亡动画。
23. 触发战斗失败流程。
```

---

## 5. 死亡类型

`Dead` 根据进入前状态和死亡姿态选择死亡表现。

```text
DeadType
{
    StandDead
    KnockdownDead
}
```

| DeadType      | 条件                                        | 说明     |
| ------------- | ----------------------------------------- | ------ |
| StandDead     | 进入 Dead 前不处于 Knockdown                    | 站立状态死亡 |
| KnockdownDead | 进入 Dead 前处于 Knockdown | 倒地死亡   |

死亡类型选择规则：

```text
if PreviousState == Knockdown:
    DeadType = KnockdownDead
else:
    DeadType = StandDead
```

---

## 6. StandDead

`StandDead` 是站立死亡表现。

```text
作用：
1. 播放站立死亡动画。
2. 玩家从站立或普通动作状态进入死亡。
3. 角色死亡后保持死亡姿态。
4. 等待战斗失败 UI 或重置战斗。
```

适用来源：

```text
1. Idle
2. Locomotion
3. Attack
4. Guard
5. Skill
6. Evade
7. HitReaction
8. Recover
```

动画：

```text
Eve_Stand_Dead2
Eve_Stand_Dead3
```

---

## 7. KnockdownDead

`KnockdownDead` 是倒地死亡表现。

```text
作用：
1. 播放倒地死亡动画。
2. 玩家从 Knockdown 或击倒死亡进入死亡。
3. 角色死亡后保持倒地循环。
4. 等待战斗失败 UI 或重置战斗。
```

适用来源：

```text
1. Knockdown
2. Knockdown 状态中 HP <= 0
3. HitReaction 状态中 HP <= 0
4. Guard 状态中不可防攻击结算后 HP <= 0
```

动画：

```text
Eve_Dead_DownFaceUp
Eve_Dead_DownFaceUp_Loop
```

---

## 8. Dead 输入响应规则

| 输入                 | 响应方式     | 是否缓存 |
| ------------------ | -------- | ---: |
| MoveInput          | 不响应      |    否 |
| LookInput          | 持续响应     |    否 |
| LockOnPressed      | 不响应      |    否 |
| LightAttackPressed | 不响应      |    否 |
| HeavyAttackPressed | 不响应      |    否 |
| EvadePressed       | 不响应      |    否 |
| EvadeHeld          | 不响应      |    否 |
| EvadeReleased      | 不响应      |    否 |
| GuardHeld          | 不响应      |    否 |
| GuardReleased      | 不响应      |    否 |
| Skill1Pressed      | 不响应      |    否 |
| Skill2Pressed      | 不响应      |    否 |
| RecoverHpPressed   | 不响应      |    否 |
| PauseInput         | 响应 UI 暂停 |    否 |
| ResetBattle        | 响应       |    否 |

Dead 状态只保留 LookInput 和 UI / 战斗重置输入。  
LookInput 只控制死亡后的观察视角，不改变角色朝向，不触发状态切换。

---

## 9. Dead 移动规则

```text
1. Dead 状态下不响应 MoveInput。
2. Dead 状态下角色不主动移动。
3. Dead 状态下停止 Locomotion 移动速度。
4. Dead 状态下停止 Sprint。
5. Dead 状态下停止攻击 Root Motion。
6. Dead 状态下停止技能 Root Motion。
7. Dead 状态下保留死亡动画自身位移。
8. 死亡动画结束后角色保持最终死亡姿态。
```

---

## 10. Dead 转向规则

```text
1. Dead 状态下角色不再响应移动转向。
2. Dead 状态下角色不再朝向 Boss。
3. Dead 状态下解除 LockOn。
4. Dead 状态下 LookInput 只控制相机。
5. Dead 状态下角色朝向由死亡动画决定。
```

---

## 11. Dead 战斗判定规则

| 项目                    | 规则                           |
| --------------------- | ---------------------------- |
| 攻击判定                  | 关闭                           |
| SkillHitbox           | 关闭                           |
| GuardBlockActive      | 关闭                           |
| EvadeInvincibleWindow | 关闭                           |
| PerfectEvadeWindow    | 关闭                           |
| PerfectGuardWindow    | 关闭                           |
| ComboWindow           | 关闭                           |
| CancelWindow          | 关闭                           |
| ResetWindow           | 关闭                           |
| Hurtbox               | 保留或关闭，由死亡表现配置决定              |
| HP                    | 固定为 0                        |
| 受击                    | 不再进入 HitReaction / Knockdown |
| 死亡后追加命中               | 不改变状态                        |

死亡后追加命中处理：

```text
if PlayerState == Dead:
    IgnoreGameplayHit()
```

---

## 12. 输入缓存规则

Dead 状态不使用输入缓存。

```text
1. 进入 Dead 时清除所有输入缓存。
2. Dead 状态中不创建任何输入缓存。
3. Dead 状态中不消费任何 Gameplay 输入缓存。
4. Dead 状态中不保留 Attack 缓存。
5. Dead 状态中不保留 Evade 缓存。
6. Dead 状态中不保留 Skill 缓存。
7. Dead 状态中不保留 Guard 相关输入记录。
8. Dead 状态中不保留 Recover 输入记录。
```

---

## 13. 死亡动画选择规则

```text
if DeadType == StandDead:
    Play StandDead Animation

if DeadType == KnockdownDead:
    Play KnockdownDead Animation
```

站立死亡动画选择：

```text
if LastHitDirection == Front:
    Play Eve_Stand_Dead2
else:
    Play Eve_Stand_Dead3
```

倒地死亡动画选择：

```text
Play Eve_Dead_DownFaceUp
After animation end:
    Play Eve_Dead_DownFaceUp_Loop
```

---

## 14. 死亡后相机规则

```text
1. Dead 状态下 LookInput 持续响应。
2. 玩家可以移动镜头观察死亡场景。
3. 相机不再强制锁定 Boss。
4. 相机不再自动拉近攻击构图。
5. 相机保持第三人称观察模式。
6. 相机仍然使用碰撞检测，避免穿墙。
7. 战斗失败 UI 出现后，相机控制根据 UI 状态决定是否继续响应。
```

---

## 15. 战斗失败流程

进入 Dead 后触发战斗失败流程：

```text
1. PlayerState = Dead。
2. IsDead=true。
3. 停止玩家 Gameplay 输入。
4. 解除 LockOn。
5. 停止 Boss 对玩家的攻击目标逻辑。
6. 播放死亡动画。
7. 等待 DeathResultDelay。
8. 显示战斗失败 UI。
9. UI 提供 ResetBattle。
```

参数：

```text
DeathResultDelay = 1.50s
```

---

## 16. ResetBattle 规则

`Dead` 只通过 `ResetBattle` 离开。

```text
Dead
+ ResetBattle
→ BattleReset
→ Idle
```

ResetBattle 执行：

```text
1. 隐藏战斗失败 UI。
2. 重置玩家 HP 到 MaxHP。
3. 重置玩家位置到战斗初始点。
4. 重置玩家旋转到战斗初始朝向。
5. 重置 Boss HP。
6. 重置 Boss 状态。
7. 重置玩家资源。
8. 重置 HealingReagentCount。
9. 清除所有输入缓存。
10. 清除所有战斗临时数据。
11. 清除 LockOnTarget。
12. 设置 IsDead=false。
13. 设置 IsInCombat=true。
14. 根据战斗初始规则设置 IsLockOn。
15. PlayerState = Idle。
```

---

## 17. 状态退出条件

|输入 / 事件|条件|下一个状态|
|---|---|---|
|ResetBattle|玩家选择重开|Idle|

Dead 状态不通过任何 Gameplay 输入退出。

---

## 18. 动画需求

### 18.1 站立死亡动画

|场景|动画资源|
|---|---|
|站立死亡 1|Eve_Stand_Dead2|
|站立死亡 2|Eve_Stand_Dead3|

### 18.2 倒地死亡动画

|场景|动画资源|
|---|---|
|倒地死亡开始|Eve_Dead_DownFaceUp|
|倒地死亡循环|Eve_Dead_DownFaceUp_Loop|

---

## 19. 动画参数

|参数名|类型|用途|
|---|---|---|
|PlayerState|Enum / Int|当前顶层状态为 Dead|
|IsDead|Bool|玩家是否死亡|
|DeadType|Enum / Int|StandDead / KnockdownDead|
|LastHitDirection|Enum / Int|最后受击方向|
|LastAttackType|Enum / Int|最后攻击类型|
|IsInCombat|Bool|是否处于战斗|
|IsLockOn|Bool|是否锁定 Boss|
|CurrentHP|Float|当前 HP，死亡时为 0|
|TriggerDead|Trigger|触发死亡|
|TriggerStandDead|Trigger|触发站立死亡|
|TriggerKnockdownDead|Trigger|触发倒地死亡|
|TriggerBattleReset|Trigger|触发战斗重置|

---

## 20. 配置事件

|事件|时间|作用|
|---|--:|---|
|DeadStart|0.00s|进入 Dead|
|ClearAllInputBuffers|0.00s|清除所有输入缓存|
|DisableAllHitboxes|0.00s|关闭所有攻击判定|
|DisableAllWindows|0.00s|关闭所有窗口|
|UnlockCameraFromBoss|0.00s|解除锁定相机|
|DeathAnimationStart|0.00s|播放死亡动画|
|DeathResultDelayEnd|1.50s|显示战斗失败 UI|
|ResetBattle|玩家选择重开|重置战斗|
|BattleResetComplete|重置完成|进入 Idle|

---

## 21. 数值参数

|参数名|数值|
|---|--:|
|DeathResultDelay|1.50s|
|DeadToBattleResetBlendTime|0.00s|
|BattleResetToIdleBlendTime|0.10s|
|CurrentHPOnDead|0|
|ClearLockOnOnDead|true|
|AllowLookInputOnDead|true|
|AllowGameplayInputOnDead|false|
|ShowDeathUI|true|

---

## 22. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|MoveInput|任意 Dead 阶段|不响应|
|LockOnPressed|任意 Dead 阶段|不响应|
|LightAttackPressed|任意 Dead 阶段|不响应|
|HeavyAttackPressed|任意 Dead 阶段|不响应|
|EvadePressed|任意 Dead 阶段|不响应|
|GuardHeld|任意 Dead 阶段|不响应|
|Skill1Pressed|任意 Dead 阶段|不响应|
|Skill2Pressed|任意 Dead 阶段|不响应|
|RecoverHpPressed|任意 Dead 阶段|不响应|
|EnemyHit|任意 Dead 阶段|忽略 Gameplay 命中|
|ResetBattle|战斗失败 UI 未允许重置|不执行重置|
|ResetBattle|战斗失败 UI 已允许重置|执行战斗重置|

---

## 23. 验收标准

```text
1. 任意状态下 HP <= 0 时立即进入 Dead。
2. Dead 优先级高于 HitReaction、Knockdown、Attack、Skill、Guard、Evade、Locomotion、Idle。
3. 进入 Dead 时清除所有输入缓存。
4. 进入 Dead 时关闭所有攻击判定。
5. 进入 Dead 时关闭所有防御判定。
6. 进入 Dead 时关闭所有无敌帧。
7. 进入 Dead 时关闭所有 PerfectGuard / PerfectEvade 判定窗口。
8. 进入 Dead 时关闭所有 CancelWindow / ResetWindow。
9. Dead 中 MoveInput 不响应。
10. Dead 中 LightAttackPressed / HeavyAttackPressed 不响应。
11. Dead 中 EvadePressed 不响应。
12. Dead 中 GuardHeld 不响应。
13. Dead 中 Skill1Pressed / Skill2Pressed 不响应。
14. Dead 中 RecoverHpPressed 不响应。
15. Dead 中 LockOnPressed 不响应。
16. Dead 中 LookInput 持续响应。
17. 站立死亡时播放 Eve_Stand_Dead2 或 Eve_Stand_Dead3。
18. 倒地死亡时播放 Eve_Dead_DownFaceUp，并进入 Eve_Dead_DownFaceUp_Loop。
19. Dead 状态中不会进入 HitReaction。
20. Dead 状态中不会进入 Knockdown。
21. Dead 状态中不会进入 Idle / Locomotion，除非 ResetBattle。
22. 玩家选择 ResetBattle 后，重新初始化玩家、Boss 和战斗数据。
23. ResetBattle 完成后进入 Idle。
```

---

## 24. 状态流转

```text
Any
+ HP <= 0
→ Dead

Idle / Locomotion / Attack / Guard / Skill / Evade / HitReaction / Recover
+ HP <= 0
→ StandDead

Knockdown
+ HP <= 0
→ KnockdownDead

Dead
+ LookInput
→ 保持 Dead，仅控制相机

Dead
+ MoveInput / AttackInput / EvadeInput / GuardInput / SkillInput / RecoverInput / LockOnPressed
→ 不响应

Dead
+ EnemyHit
→ 忽略 Gameplay 命中

Dead
+ ResetBattle
→ BattleReset
→ Idle
```
