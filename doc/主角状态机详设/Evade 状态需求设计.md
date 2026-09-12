## 1. 状态定位

`Evade` 是玩家角色的闪避 / 完美闪避状态，用于处理普通闪避、无敌帧、完美闪避判定、闪避后派生、闪避后转入奔跑等逻辑。

`Evade` 负责：

```text
1. 处理 EvadePressed 进入闪避。
2. 进入闪避时确定 EvadeDirection。
3. 管理 EvadeStart / EvadeInvincible / PerfectEvade / EvadeRecovery / EvadeReset。
4. 管理 EvadeInvincibleWindow。
5. 管理 PerfectEvadeWindow。
6. 管理 NearMissCandidate 与 HitAttempt 双通道完美闪避判定。
7. 管理 Evade 状态内 SkillInput 缓存。
8. 管理 Attack / Guard / Skill 在闪避后段的允许响应。
9. 管理 EvadeEnd 后回 Idle / Locomotion / Sprint。
10. 在恢复 / 重置后段允许玩家用移动输入提前回到 Locomotion，避免长恢复动画锁死移动。
```

`Evade` 不负责：

```text
1. Sprint 循环移动。
2. Sprint 中再次触发 EvadePressed。
3. 攻击判定。
4. 防御判定。
5. 技能攻击判定。
6. 受击硬直表现。
7. 回血。
8. LockOn 切换。
```

---

## 2. 状态分层

```text
Evade
├── Evade.Normal
│   ├── EvadeStart
│   ├── EvadeInvincible
│   ├── EvadeRecovery
│   └── EvadeReset
└── Evade.Perfect
    ├── PerfectEvadeStart
    ├── PerfectEvadeInvincible
    ├── PerfectEvadeRecovery
    └── PerfectEvadeReset / Return
```

`Evade.Perfect` 不是新的顶层状态。触发 PerfectEvade 后 `PlayerStateId` 仍为 `Evade`，但 `CurrentEvadeMode` 从 `Normal` 切到 `Perfect`，后续阶段、窗口和自然返回改用 PerfectEvade 专用固定时间轴。

---

## 3. 进入条件

| 来源状态                 | 条件                                     | 目标状态  | 说明       |
| -------------------- | -------------------------------------- | ----- | -------- |
| Idle                 | EvadePressed 且 CanEvade=true           | Evade | 待机闪避     |
| FreeMove.Run         | EvadePressed 且 CanEvade=true           | Evade | 普通移动闪避   |
| FreeMove.BattleRun   | EvadePressed 且 CanEvade=true           | Evade | 战斗移动闪避   |
| LockOnMove.LockOnRun | EvadePressed 且 CanEvade=true           | Evade | 锁定移动闪避   |
| Attack               | EvadePressed 且 CommitCancelWindow=true | Evade | 攻击起手取消闪避 |
| Attack               | EvadePressed 且 EvadeCancelWindow=true  | Evade | 攻击后段取消闪避 |
| Guard                | EvadePressed 且 EvadeCancelWindow=true  | Evade | 防御转闪避    |
| Skill                | EvadePressed 且 EvadeCancelWindow=true  | Evade | 技能后摇转闪避  |

Sprint 状态下不响应 EvadePressed：

```text
FreeMove.Sprint / FreeMove.BattleSprint / LockOnMove.LockOnSprint
+ EvadeInput Hold
→ 维持 Sprint

FreeMove.Sprint / FreeMove.BattleSprint / LockOnMove.LockOnSprint
+ EvadeReleased
→ SprintIntent=false
→ 退出 Sprint

Sprint 状态下不监听 EvadePressed。
Sprint 状态下不能通过 EvadeInput 再次进入 Evade。
```

---

## 4. 闪避方向规则

进入 `Evade` 的第一帧锁定本次闪避方向：

```text
EvadeDirection = ResolveEvadeDirection(
    ControlMode,
    IsInCombat,
    IsLockOn,
    MoveInput,
    MoveMagnitude,
    CharacterForward,
    CharacterBackward,
    CameraForward,
    CameraRight,
    LockOnTargetDirection
)
```

进入 Evade 后，本次 `EvadeDirection` 不再被新的 MoveInput 改写。  
MoveInput 后续只用于决定 EvadeEnd 后进入 Idle / Locomotion / Sprint。

### 4.1 无移动输入

```text
if MoveMagnitude <= MoveThreshold:
    EvadeDirection = CharacterBackward
```

无移动输入时单按 `EvadePressed`，默认向角色当前 `Backward` 方向闪避。

### 4.2 自由视角非战斗模式

```text
IsInCombat=false
IsLockOn=false
```

```text
if MoveMagnitude > MoveThreshold:
    EvadeDirection = CameraRelativeMoveDirection
else:
    EvadeDirection = CharacterBackward
```

### 4.3 自由视角战斗模式

```text
IsInCombat=true
IsLockOn=false
```

```text
if MoveMagnitude > MoveThreshold:
    EvadeDirection = CameraRelativeMoveDirection
else:
    EvadeDirection = CharacterBackward
```

### 4.4 锁定视角战斗模式

```text
IsInCombat=true
IsLockOn=true
```

|输入|EvadeDirection|
|---|---|
|W|Forward|
|S|Backward|
|A|Left|
|D|Right|
|WA / WD|Forward|
|SA / SD|Backward|
|无输入|Backward|

锁定视角下角色持续朝向 Boss，MoveInput 只决定闪避方向，不改变角色朝向。

### 4.5 闪避代码位移规则

普通闪避和 PerfectEvade 动画保持 non-root-motion / in-place。进入 `Evade` 时，状态只在 `PlayerStateContext` 中请求对应 `PlayerActionMotionId`，实际位移由 `PlayerMovementMotor` 统一执行。

```text
普通 Evade:
- Forward  -> EvadeForward
- Backward -> EvadeBackward
- Left     -> EvadeLeft
- Right    -> EvadeRight

PerfectEvade:
- Forward  -> PerfectEvadeForward
- Backward -> PerfectEvadeBackward
- Left     -> PerfectEvadeLeft
- Right    -> PerfectEvadeRight
```

方向锁定规则：

```text
1. 锁定视角：ActionMove 映射前 / 后 / 左 / 右，角色继续朝向 Boss。
2. 自由视角：有移动输入时按进入 Evade 时相机相对世界方向前闪。
3. 自由视角：无移动输入时按角色后方向后闪。
4. 进入 Evade 后，动作位移方向不再随相机旋转改变。
5. PerfectEvade 触发时重启对应 PerfectEvade 动作位移，覆盖普通 Evade 剩余位移。
```

锁定侧向位移补充：

```text
1. 锁定模式下 EvadeLeft / EvadeRight / PerfectEvadeLeft / PerfectEvadeRight 不再使用纯直线切线位移。
2. 左 / 右侧闪启动时锁定当前 Boss 水平位置、玩家起始位置和起始半径。
3. 动作位移仍复用 PlayerActionMotionProfile 的距离、时长和 NormalizedDistanceCurve。
4. 每帧按曲线进度把移动距离换算成圆弧角度，并输出当前位置到圆弧目标点的水平 delta。
5. 侧闪目标是绕 Boss 规避并保持大致交战半径，不负责主动靠近 Boss。
6. 起始半径过小、未锁定或没有 LockOnTarget 时，回退到原直线位移。
7. 最终位移仍通过 CharacterController.Move() 执行；碰撞阻挡时不强制贴回圆弧，不穿墙、不传送。
8. 闪避位移在进入 `CharacterController.Move()` 前经过 `PlayerBossSeparation` 裁剪，不能进入 Boss 闪避安全距离；接触 Boss 时保留切线滑动，避免侧闪被完全截停。
9. 闪避不会通过身体碰撞推动 Boss；PerfectEvade / 受击判定仍只由 Boss Hitbox 与 Player Hurtbox / NearMiss 通道产生。
```

---

## 5. Sprint 衔接规则

`EvadeInput` 同时承担两种语义：

```text
Press：在 Idle / Run / BattleRun / LockOnRun 等合法状态中触发 Evade。
Hold：形成 SprintIntent，并在合法状态中维持 Sprint。
```

### 5.1 Evade 中记录 SprintIntent

```text
EvadeInputPressed 当帧：
- 进入 Evade。
- 不进入 Sprint。
- 不设置 SprintIntent=true。

EvadeInputHeld 持续超过 SprintHoldThreshold：
- SprintIntent=true。

EvadeInputReleased：
- EvadeHeld=false。
- SprintIntent=false。
```

```text
SprintHoldThreshold = 0.25s
```

### 5.2 EvadeEnd 后进入 Sprint

```text
EvadeEnd
+ SprintIntent=true
+ EvadeHeld=true
+ MoveMagnitude > MoveThreshold
+ IsInCombat=false
+ IsLockOn=false
→ FreeMove.Sprint
```

```text
EvadeEnd
+ SprintIntent=true
+ EvadeHeld=true
+ MoveMagnitude > MoveThreshold
+ IsInCombat=true
+ IsLockOn=false
→ FreeMove.BattleSprint
```

```text
EvadeEnd
+ SprintIntent=true
+ EvadeHeld=true
+ MoveMagnitude > MoveThreshold
+ IsInCombat=true
+ IsLockOn=true
→ LockOnMove.LockOnSprint
```

### 5.3 Sprint 中释放 EvadeInput

```text
FreeMove.Sprint
+ EvadeReleased
+ MoveMagnitude > MoveThreshold
→ FreeMove.Run
```

```text
FreeMove.BattleSprint
+ EvadeReleased
+ MoveMagnitude > MoveThreshold
→ FreeMove.BattleRun
```

```text
LockOnMove.LockOnSprint
+ EvadeReleased
+ MoveMagnitude > MoveThreshold
→ LockOnMove.LockOnRun
```

```text
任意 Sprint
+ EvadeReleased
+ MoveMagnitude <= MoveThreshold
→ Idle
```

---

## 6. PerfectEvade 双通道判定

PerfectEvade 使用两个检测通道：

```text
通道 A：NearMissCandidate
通道 B：HitAttempt
```

### 6.1 检测体定义

```text
Hurtbox：
玩家真实受击体积。

PerfectEvadeCheckVolume：
玩家完美闪避外圈检测体积。
PerfectEvadeCheckVolume 大于 Hurtbox。
```

### 6.2 HitAttempt 记录

```text
Boss AttackHitbox overlap Player Hurtbox
→ 记录 HitAttempt
```

```text
HitAttempt
{
    AttackId
    AttackerId
    AttackType
    HitFrameTime
    HitboxId
    DamageData
    CanBePerfectEvaded
    WasBlockedByInvincible
}
```

### 6.3 NearMissCandidate 记录

```text
Boss AttackHitbox overlap Player PerfectEvadeCheckVolume
且没有 overlap Player Hurtbox
→ 记录 NearMissCandidate
```

```text
NearMissCandidate
{
    AttackId
    AttackerId
    AttackType
    NearMissFrameTime
    HitboxId
    CanBePerfectEvaded
}
```

### 6.4 PerfectEvade 触发规则

```text
if Evade
and PerfectEvadeWindow=true
and NearMissCandidate exists
and NearMissCandidate.CanBePerfectEvaded=true:
    激活 Evade.PerfectEvade 动画模式
```

```text
if Evade
and PerfectEvadeWindow=true
and HitAttempt exists
and HitAttempt.WasBlockedByInvincible=true
and HitAttempt.CanBePerfectEvaded=true:
    激活 Evade.PerfectEvade 动画模式
```

### 6.5 同帧处理顺序

```text
1. 记录 HitAttempt。
2. 记录 NearMissCandidate。
3. 判断 HitAttempt 是否被 EvadeInvincibleWindow 拦截。
4. 如果 HitAttempt 在 PerfectEvadeWindow 内被无敌帧拦截，触发 PerfectEvade。
5. 否则，如果 NearMissCandidate 在 PerfectEvadeWindow 内存在，触发 PerfectEvade。
6. 否则，如果 HitAttempt 被 EvadeInvincibleWindow 拦截，忽略伤害。
7. 否则，如果 HitAttempt 命中且不在无敌帧，进入 HitReaction / Knockdown。
```

### 6.6 判定结果表

| 条件                                                                        | 结果                      |
| ------------------------------------------------------------------------- | ----------------------- |
| HP <= 0                                                                   | Dead                    |
| PerfectEvadeWindow=true 且 HitAttempt 被无敌帧拦截                               | PerfectEvade            |
| PerfectEvadeWindow=true 且 NearMissCandidate exists                        | PerfectEvade            |
| EvadeInvincibleWindow=true 且 HitAttempt exists 且 PerfectEvadeWindow=false | 忽略伤害                    |
| EvadeInvincibleWindow=false 且 HitAttempt exists                           | HitReaction / Knockdown |
| NearMissCandidate exists 且 PerfectEvadeWindow=false                       | 无效果                     |

### 6.7 PerfectEvade 触发后处理

```text
1. 记录 HasPerfectEvadedAttackId。
2. 清除本次 AttackId 对应的 HitAttempt。
3. 清除本次 AttackId 对应的 NearMissCandidate。
4. 标记本次 AttackId 已经对玩家完成闪避解析。
5. 设置 IsPerfectEvadeActive=true。
6. CurrentEvadeMode 切换为 Perfect。
7. 记录 PerfectEvadeActivatedElapsed = 当前普通 Evade 已经过时间。
8. PerfectEvadeModeElapsed 从 0 开始计算。
9. 奖励 2 点 BetaEnergy，并钳制在 32 上限内。
10. 按进入 Evade 时锁存的 ActionMove 计算 PerfectEvadeDirection。
11. PlayerAnimationBridge 使用 Animator.CrossFadeInFixedTime 切入对应方向 PerfectEvade 动画。
12. 通过 Combat Feedback 层请求短子弹时间；不使用命中 HitStop，避免把成功闪避误当作受击卡肉。
13. SkillCancelWindow、GuardCancelWindow、AttackResetWindow 改用 PerfectEvade 专用固定时间轴生效。
```

`Evade.PerfectEvade` 是 Evade 内部动画 / 时间轴模式，不是新的顶层 `PlayerStateId`。触发后普通闪避动画入口必须停止抢占，四方向 PerfectEvade 离散动画由 `PlayerAnimationBridge` 主动使用固定秒数 CrossFade 播放，Animator 不再使用 PerfectEvade 的 Any State 条件入口。

---

## 7. Evade 输入响应规则

| 输入                 | 响应方式                                        | 是否缓存 |
| ------------------ | ------------------------------------------- | ---: |
| MoveInput          | 持续读取；进入时决定方向；后续决定 EvadeEnd 去向               |    否 |
| LookInput          | 持续响应                                        |    否 |
| LockOnPressed      | 不响应                                         |    否 |
| LightAttackPressed | AttackResetWindow 内即时响应                     |    否 |
| HeavyAttackPressed | AttackResetWindow 内即时响应                     |    否 |
| EvadePressed       | 不响应                                         |    否 |
| EvadeHeld          | 持续读取，用于 SprintIntent                        |    否 |
| EvadeReleased      | 持续读取，用于清除 SprintIntent                      |    否 |
| GuardHeld          | GuardCancelWindow 内即时响应                     |    否 |
| Skill1Pressed      | SkillBufferWindow 内缓存，SkillCancelWindow 内即时响应或消费缓存 |    是 |
| Skill2Pressed      | SkillBufferWindow 内缓存，SkillCancelWindow 内即时响应或消费缓存 |    是 |
| RecoverHpPressed   | 不响应                                         |    否 |

Evade 状态只缓存 SkillInput。  
Attack 输入不缓存，只在 AttackResetWindow 内即时响应。  
Guard 输入不缓存，只在 GuardCancelWindow 内检测 `GuardHeld=true`。  
EvadePressed 不缓存，Evade 中不连续闪避。  
Sprint 中不接受 EvadePressed。

当前实现修订：

```text
NormalMovementReturnStart = 0.90s
PerfectMovementReturnStart = 1.10s
```

规则：

```text
1. Skill / Guard / Attack 派生检查优先于移动返回。
2. 普通 Evade 到达 0.90s 后，如果 MoveMagnitude > MoveThreshold，直接返回 Locomotion。
3. PerfectEvade 到达 1.10s 后，如果 MoveMagnitude > MoveThreshold，直接返回 Locomotion。
4. 无移动输入时仍按当前完整 Evade Duration 自然回 Idle，避免原地恢复动画被过早截断。
5. 该规则不改变 EvadeDirection 锁存、动作位移、无敌帧和 PerfectEvade 判定。
```

---

## 8. Evade 时间轴与窗口

### 8.1 NormalEvade 固定时间轴

未触发 PerfectEvade 时，`Evade.Normal` 使用以下固定时间轴：

```text
0.00s ───────────────────────────────────── 0.65s

0.00 - 0.08  EvadeStart
0.06 - 0.34  EvadeInvincibleWindow
0.05 - 0.24  PerfectEvadeWindow
0.26 - 0.52  SkillBufferWindow
0.38 - 0.58  SkillCancelWindow
0.42 - 0.62  GuardCancelWindow
0.46 - 0.65  AttackResetWindow
0.52 - 0.65  ReturnWindow
```

### 8.2 PerfectEvade 固定时间轴

PerfectEvade 在普通闪避 `0.05s - 0.24s` 判定窗口内触发后，`Evade.Perfect` 从 `PerfectEvadeModeElapsed = 0` 重新计算窗口：

```text
0.00s ───────────────────────────────────── 1.10s

0.00 - 0.55  PerfectEvadeInvincibleWindow
0.32 - 0.78  SkillBufferWindow
0.58 - 0.96  SkillCancelWindow
0.66 - 1.02  GuardCancelWindow
0.72 - 1.10  AttackResetWindow
0.90 - 1.10  ReturnWindow
```

PerfectEvade 模式下不再开启新的 PerfectEvadeWindow，避免同一次 Evade 内重复触发。

---

## 9. 缓存区间设计

### 9.1 SkillBufferWindow

```text
SkillBufferWindow:
Start = 0.26s
End   = 0.52s
Duration = 0.26s
```

作用：

```text
1. SkillInput 在该区间内进入缓存。
2. 如果 SkillCancelWindow 尚未开启，则等待消费。
3. 如果 SkillCancelWindow 已开启，则立即消费。
4. 如果 EvadeEnd 前仍未消费，则清除缓存。
```

缓存输入：

```text
Skill1Pressed
Skill2Pressed
```

缓存数据：

```text
EvadeInputBuffer
{
    BufferedSkillType
    BufferedTime
    ExpireTime
    InputSource
    Consumed
}
```

缓存有效期：

```text
ExpireTime = min(BufferedTime + 0.30s, EvadeEndTime)
```

---

### 9.2 SkillCancelWindow

```text
SkillCancelWindow:
Start = 0.38s
End   = 0.58s
Duration = 0.20s
```

消费规则：

```text
if SkillCancelWindow == true:
    if HasBufferedSkillInput:
        if BetaEnergy >= SkillCost:
            ConsumeSkillBuffer()
            EnterSkill()
        else:
            ClearSkillBuffer()
            PlaySkillFailFeedback()
    else if SkillPressedThisFrame:
        if BetaEnergy >= SkillCost:
            EnterSkill()
        else:
            PlaySkillFailFeedback()
```

---

### 9.3 AttackResetWindow

```text
AttackResetWindow:
Start = 0.46s
End   = 0.65s
Duration = 0.19s
```

响应规则：

```text
if AttackResetWindow == true:
    if LightAttackPressedThisFrame and CanAttack:
        EnterAttack(Light)
    else if HeavyAttackPressedThisFrame and CanAttack:
        EnterAttack(Heavy)
```

限制：

```text
1. LightAttackPressed 不进入缓存。
2. HeavyAttackPressed 不进入缓存。
3. AttackResetWindow 开启前的攻击输入直接忽略。
4. AttackResetWindow 关闭后的攻击输入由后续状态处理。
```

---

### 9.4 GuardCancelWindow

```text
GuardCancelWindow:
Start = 0.42s
End   = 0.62s
Duration = 0.20s
```

响应规则：

```text
if GuardCancelWindow == true:
    if GuardHeld == true and CanGuard:
        EnterGuard()
```

限制：

```text
1. Guard 不进入普通缓存。
2. GuardPressed 发生在 GuardCancelWindow 开启前，且玩家在窗口开启前松开，则不进入 Guard。
3. GuardCancelWindow 开启时 GuardHeld=true，立即进入 Guard。
```

---

### 9.5 ReturnWindow

```text
ReturnWindow:
Start = 0.52s
End   = 0.65s
Duration = 0.13s
```

自然结束规则：

```text
if EvadeEnd:
    if SprintIntent == true and EvadeHeld == true and MoveMagnitude > MoveThreshold:
        EnterLocomotionSprint()
    else if MoveMagnitude > MoveThreshold:
        EnterLocomotionRun()
    else:
        EnterIdle()
```

---

## 10. 缓存冲突处理

### 10.1 Skill1 / Skill2 冲突

```text
1. Skill1 和 Skill2 属于同一 Skill 槽。
2. 后输入覆盖前输入。
3. 被覆盖的 SkillInput 清除。
```

### 10.2 Skill 缓存 vs Attack 输入

```text
if SkillCancelWindow == true
and HasBufferedSkillInput
and BetaEnergy >= SkillCost:
    EnterSkill()
else if AttackResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
```

优先级：

```text
SkillInput > LightAttack / HeavyAttack
```

### 10.3 Skill 缓存 vs GuardHeld

```text
if SkillCancelWindow == true
and HasBufferedSkillInput
and BetaEnergy >= SkillCost:
    EnterSkill()
else if GuardCancelWindow == true
and GuardHeld == true:
    EnterGuard()
```

优先级：

```text
SkillInput > GuardHeld
```

### 10.4 Skill 缓存 vs EvadeEnd

```text
if EvadeEnd:
    if HasBufferedSkillInput:
        ClearSkillBuffer()
    ResolveReturnToIdleOrLocomotion()
```

Skill 缓存只在 Evade 状态内有效，不带入 Idle / Locomotion / Sprint。

---

## 11. 状态退出条件

| 输入 / 事件            | 条件                                                       | 下一个状态                   |
| ------------------ | -------------------------------------------------------- | ----------------------- |
| HP <= 0            | 任意时刻                                                     | Dead                    |
| HitAttempt         | 非无敌帧                                                     | HitReaction / Knockdown |
| HitAttempt         | 无敌帧内且 PerfectEvadeWindow=true                            | Evade.Perfect           |
| NearMissCandidate  | PerfectEvadeWindow=true                                  | Evade.Perfect           |
| HitAttempt         | 无敌帧内且 PerfectEvadeWindow=false                           | Evade                   |
| SkillInput         | SkillCancelWindow=true 且 BetaEnergy 足够                   | Skill                   |
| GuardHeld          | GuardCancelWindow=true 且 CanGuard=true                   | Guard                   |
| LightAttackPressed | AttackResetWindow=true 且 CanAttack=true                  | Attack                  |
| HeavyAttackPressed | AttackResetWindow=true 且 CanAttack=true                  | Attack                  |
| EvadeEnd           | SprintIntent=true 且 EvadeHeld=true 且 MoveMagnitude > 0.1 | Locomotion.Sprint       |
| EvadeEnd           | MoveMagnitude > 0.1                                      | Locomotion              |
| EvadeEnd           | MoveMagnitude <= 0.1                                     | Idle                    |

---

## 12. 动画需求

### 12.1 普通闪避动画

|场景|动画资源|
|---|---|
|非战斗前向闪避|P_Eve_Peaceful_Evade_Forward|
|非战斗后向闪避|P_Eve_Peaceful_Evade_Backward|
|战斗前向闪避|Proto_Lockon_Evade_Forward / Proto_Lockon_Evade_Forward_L / Proto_Lockon_Evade_Forward_R|
|战斗后向闪避|Proto_Lockon_Evade_Backward / Proto_Lockon_Evade_Backward_L / Proto_Lockon_Evade_Backward_R|
|战斗左闪|Proto_Lockon_Evade_Left|
|战斗右闪|Proto_Lockon_Evade_Right|

### 12.2 完美闪避动画

|方向|动画资源|
|---|---|
|Forward|Proto_Lockon_Sword_Just_Evade_Forward|
|Backward|Proto_Lockon_Sword_Just_Evade_backward|
|Left|Proto_Lockon_Sword_Just_Evade_Left|
|Right|Proto_Lockon_Sword_Just_Evade_Right|

### 12.3 闪避后移动过渡动画

|目标|动画资源|
|---|---|
|非战斗闪避后进入 Run|Proto_Run_StartAfterEvade|
|战斗闪避后进入 BattleRun / LockOnRun|Proto_Battle_Run_StartAfterEvade|

---

## 13. 动画参数

|参数名|类型|用途|
|---|---|---|
|PlayerState|Enum / Int|当前顶层状态为 Evade|
|EvadePhase|Enum / Int|Start / Invincible / Perfect / Recovery / Reset|
|EvadeDirection|Enum / Int|Forward / Backward / Left / Right|
|EvadeMoveX|Float|进入闪避时锁定的横向输入|
|EvadeMoveY|Float|进入闪避时锁定的纵向输入|
|MoveMagnitude|Float|判断 EvadeEnd 后去向|
|IsInCombat|Bool|是否战斗模式|
|IsLockOn|Bool|是否锁定 Boss|
|EvadeHeld|Bool|EvadeInput 是否仍按住|
|SprintIntent|Bool|是否满足长按奔跑意图|
|IsEvadeInvincible|Bool|当前是否处于无敌帧|
|IsPerfectEvadeWindow|Bool|当前是否处于完美闪避窗口|
|CurrentEvadeMode|Int|Evade 内部模式：None=0、Normal=1、Perfect=2|
|IsPerfectEvadeActive|Bool|本次 Evade 是否已经触发完美闪避动画模式|
|PerfectEvadeDirection|Int|完美闪避离散动画方向：None=0、Forward=1、Backward=2、Left=3、Right=4|
|PerfectEvadeActivatedElapsed|Float|触发 PerfectEvade 时普通 Evade 已经过时间，用于 CrossFade offset 映射|
|PerfectEvadeModeElapsed|Float|PerfectEvade 专用时间轴已经过时间|
|HasBufferedSkillInput|Bool|是否有 SkillInput 缓存|
|TriggerEvadeToSkill|Trigger|闪避转技能|
|TriggerEvadeToGuard|Trigger|闪避转防御|
|TriggerEvadeToAttack|Trigger|闪避转攻击|
|TriggerEvadeToMove|Trigger|闪避转移动|

---

## 14. 配置事件

### 14.1 NormalEvade 配置事件

| 事件                      |    时间 | 作用                      |
| ----------------------- | ----: | ----------------------- |
| EvadeStart              | 0.00s | 进入 Evade.Normal         |
| PerfectEvadeWindowStart | 0.05s | 开启完美闪避窗口                |
| EvadeInvincibleStart    | 0.06s | 开启无敌帧                   |
| PerfectEvadeWindowEnd   | 0.24s | 关闭完美闪避窗口                |
| SkillBufferWindowStart  | 0.26s | 开始允许 SkillInput 缓存      |
| EvadeInvincibleEnd      | 0.34s | 关闭无敌帧                   |
| SkillCancelWindowStart  | 0.38s | 允许消费 SkillInput         |
| GuardCancelWindowStart  | 0.42s | 允许 GuardHeld 转 Guard    |
| AttackResetWindowStart  | 0.46s | 允许 Attack 即时响应          |
| ReturnWindowStart       | 0.52s | 允许自然回 Idle / Locomotion |
| SkillCancelWindowEnd    | 0.58s | 关闭技能取消                  |
| GuardCancelWindowEnd    | 0.62s | 关闭防御取消                  |
| EvadeEnd                | 0.65s | 结束普通闪避                  |

### 14.2 PerfectEvade 配置事件

| 事件                         |    时间 | 作用                         |
| -------------------------- | ----: | -------------------------- |
| PerfectEvadeStart          | 0.00s | 进入 Evade.Perfect 专用时间轴     |
| PerfectEvadeInvincibleEnd  | 0.55s | 关闭完美闪避无敌帧                  |
| SkillBufferWindowStart     | 0.32s | 开始允许 SkillInput 缓存         |
| SkillBufferWindowEnd       | 0.78s | 关闭 SkillInput 缓存            |
| SkillCancelWindowStart     | 0.58s | 允许消费 SkillInput            |
| SkillCancelWindowEnd       | 0.96s | 关闭技能取消                     |
| GuardCancelWindowStart     | 0.66s | 允许 GuardHeld 转 Guard       |
| GuardCancelWindowEnd       | 1.02s | 关闭防御取消                     |
| AttackResetWindowStart     | 0.72s | 允许 Attack 即时响应             |
| ReturnWindowStart          | 0.90s | 允许自然回 Idle / Locomotion    |
| PerfectEvadeEnd            | 1.10s | 结束 PerfectEvade 专用时间轴      |

---

## 15. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|SprintHoldThreshold|0.25s|
|EvadeTotalDuration|0.65s|
|EvadeStartDuration|0.08s|
|EvadeInvincibleStartTime|0.06s|
|EvadeInvincibleEndTime|0.34s|
|PerfectEvadeStartTime|0.05s|
|PerfectEvadeEndTime|0.24s|
|SkillBufferStartTime|0.26s|
|SkillBufferEndTime|0.52s|
|SkillCancelStartTime|0.38s|
|SkillCancelEndTime|0.58s|
|GuardCancelStartTime|0.42s|
|GuardCancelEndTime|0.62s|
|AttackResetStartTime|0.46s|
|AttackResetEndTime|0.65s|
|ReturnWindowStartTime|0.52s|
|EvadeDistanceForward|3.4m|
|EvadeDistanceBackward|3.0m|
|EvadeDistanceSide|3.2m|
|EvadeMotionDurationForward|0.38s|
|EvadeMotionDurationBackward|0.36s|
|EvadeMotionDurationSide|0.36s|
|PerfectEvadeCheckRadiusScale|1.4|
|EvadeToAttackBlendTime|0.05s|
|EvadeToGuardBlendTime|0.05s|
|EvadeToSkillBlendTime|0.06s|
|EvadeToMoveBlendTime|0.10s|
|EvadeToIdleBlendTime|0.10s|

PerfectEvade 固定测试数值：

|参数名|数值|
|---|--:|
|PerfectEvadeTotalDuration|1.10s|
|PerfectEvadeInvincibleStartTime|0.00s|
|PerfectEvadeInvincibleEndTime|0.55s|
|PerfectEvadeSkillBufferStartTime|0.32s|
|PerfectEvadeSkillBufferEndTime|0.78s|
|PerfectEvadeSkillCancelStartTime|0.58s|
|PerfectEvadeSkillCancelEndTime|0.96s|
|PerfectEvadeGuardCancelStartTime|0.66s|
|PerfectEvadeGuardCancelEndTime|1.02s|
|PerfectEvadeAttackResetStartTime|0.72s|
|PerfectEvadeAttackResetEndTime|1.10s|
|PerfectEvadeReturnWindowStartTime|0.90s|
|PerfectEvadeDistanceForward|2.8m|
|PerfectEvadeDistanceBackward|2.6m|
|PerfectEvadeDistanceSide|2.7m|
|PerfectEvadeMotionDuration|0.45s|
|PerfectEvadeCrossFadeDuration|0.10s，固定秒数|
|PerfectEvadeCrossFadeOffsetRange|0.00 - 0.125s，固定起播秒数|
|PerfectEvadeSlowTimeScale|0.20，由 CombatFeedbackPlayer 请求子弹时间|
|PerfectEvadeSlowDuration|0.25s，由 CombatFeedbackPlayer 请求子弹时间|
|PerfectEvadeRecoverDuration|0.18s，由 CombatFeedbackPlayer 平滑恢复时间缩放|

---

## 16. 输入失败处理

| 输入                 | 失败条件                                       | 处理               |
| ------------------ | ------------------------------------------ | ---------------- |
| EvadePressed       | 当前处于 Sprint                                | 不响应              |
| EvadePressed       | 当前处于 Evade                                 | 不响应              |
| EvadePressed       | CanEvade=false                             | 不进入 Evade，播放失败反馈 |
| LightAttackPressed | AttackResetWindow 未开启                      | 忽略，不缓存           |
| HeavyAttackPressed | AttackResetWindow 未开启                      | 忽略，不缓存           |
| GuardHeld          | GuardCancelWindow 未开启                      | 保持 Evade，不缓存     |
| Skill1Pressed      | 不在 SkillBufferWindow 且不在 SkillCancelWindow | 忽略，不缓存           |
| Skill1Pressed      | SkillCancelWindow 开启但 BetaEnergy 不足        | 清除缓存，播放能量不足反馈    |
| RecoverHpPressed   | 任意 Evade 阶段                                | 不响应              |
| LockOnPressed      | 任意 Evade 阶段                                | 不响应              |

---

## 17. 状态流转

```text
Idle / Run / BattleRun / LockOnRun
+ EvadePressed
+ CanEvade
→ EvadeStart
→ EvadeInvincible
    ├─ PerfectEvadeWindow
    │   ├─ NearMissCandidate exists
    │   │   → Evade.PerfectEvade 动画模式
    │   └─ HitAttempt blocked by EvadeInvincibleWindow
    │       → Evade.PerfectEvade 动画模式
    │
    ├─ HitAttempt + EvadeInvincibleWindow + 非 PerfectEvadeWindow
    │   → 忽略伤害，继续 Evade
    │
    └─ HitAttempt + 非无敌
        → HitReaction / Knockdown

EvadeRecovery / EvadeReset
    ├─ SkillInput in SkillBufferWindow
    │   → 缓存
    │   → SkillCancelWindow 消费
    │   → Skill
    │
    ├─ GuardHeld + GuardCancelWindow
    │   → Guard
    │
    ├─ LightAttackPressed / HeavyAttackPressed + AttackResetWindow
    │   → Attack
    │
    └─ EvadeEnd
        ├─ SprintIntent=true + EvadeHeld=true + MoveMagnitude > 0.1
        │   → Locomotion.Sprint
        ├─ MoveMagnitude > 0.1
        │   → Locomotion
        └─ MoveMagnitude <= 0.1
            → Idle

Sprint
+ EvadeHeld=true
→ 维持 Sprint

Sprint
+ EvadeReleased
→ SprintIntent=false
→ Run / BattleRun / LockOnRun / Idle

Sprint
+ EvadePressed
→ 不响应
```
