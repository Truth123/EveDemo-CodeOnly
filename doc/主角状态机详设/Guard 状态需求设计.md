## 1. 状态定位

`Guard` 是玩家角色的防御 / 完美防御状态，用于处理按住防御、普通防御受击、完美防御、破防、防御中慢速移动、防御释放、防御后派生等逻辑。

`GuardInput` 是即时按下 + Hold 保持输入：`GuardPressed || GuardHeld` 可在合法状态进入 Guard，`GuardHeld` 持续保持 Guard，`!GuardHeld` 表示请求释放 Guard。PerfectGuard 不需要额外按键，由 Guard 状态、敌人攻击命中时机和 `PerfectGuardWindow` 共同触发。  
状态机中 `Guard` 分层为 `GuardStart / GuardLoop / PerfectGuard / GuardHit / GuardRelease / GuardReset`，受击解析优先级中规定 Guard 状态下先判定 PerfectGuard，再判定普通防御、破防和受击。

当前实现修订：

- `PerfectGuard` 和 `GuardHit` 是 Guard 顶层状态内的内部反应模式，不新增顶层状态。
- Boss HitNode 只提供 `CombatReactionIntent.HitReaction / Knockdown / DamageOnly`；`GuardHit / PerfectGuard` 是玩家防御窗口结算后的输出结果。
- 背后命中默认不可被 Guard / PerfectGuard 处理，`BackGuardAllowed=false`。
- PerfectGuard 反应结束后，若仍按住 Guard 回到 `GuardLoop`，否则进入 `GuardRelease`；GuardHit 只有在 `Cancel_ToGuardRelease` 窗口内松开 Guard，才可以提前进入真实 `GuardRelease`，所有 Evade / Skill / Attack 派生都必须由该真实 Release 阶段产生。
- Guard Walk 已接入完整 Start / Loop / End：实际位移由 `PlayerMovementMotor` 执行，Animator 使用 `IsGuardWalking / GuardMoveDirection` 选择起步 / 停步方向，并使用实际防御慢走速度驱动的 `MoveX / MoveY` 播放 2D 慢走混合。
- 移动中进入 Guard 时，`GuardStart` 仍保留 PerfectGuard / Block 时间窗，但允许快速进入对应方向 Guard Walk Start / Loop，避免站桩防御起手期间角色滑步。
- 移动中松开 Guard 时，状态机直接回 `Locomotion`，Animator 快速回 `ReturnToLocomotion`，不强制播放站桩 `GuardEnd`；原地松开 Guard 仍播放 `GuardRelease / GuardEnd`。
- Guard 不再依赖 `GuardReleased` 瞬时事件；释放由 `!GuardHeld` 推导，Animator 释放表现由整个 GuardRelease 生命周期内稳定的 `PlayerStatePhase=Recovery` 驱动。
- GuardStart 增加最短生效时间 `MinimumGuardStartDuration = 0.15s`；短按 Guard 也会至少进入防御一小段时间，确保 `BlockActiveStart = 0.04s` 后普通防御可以生效。
- GuardRelease 中重新按下 Guard 会重新进入 GuardStart 时间轴，恢复 PerfectGuardWindow / GuardBlockActive 判定；动画层用 `IsGuardReentry=true` 从 GuardEnd 快速混回 GuardLoop / GuardWalk，避免完整重播 GuardStart。
- PerfectGuard 反应期间新增 `PerfectGuardChainWindow`：玩家必须在 `0.1667s - 0.4667s` 重新按下 Guard，才会打开 `0.16s` 连续弹反窗口；持续按住 Guard 不会自动连弹。
- Guard Timeline 编辑结构修订：`Perfect Guard` Track 保存 `PerfectGuardReaction` Marker、`Input_PerfectGuardChain` 与 `Cancel_ToGuardRelease`；`GuardHit` Track 只保存 `GuardHitReaction` Marker 与 `Cancel_ToGuardRelease`。两个反应阶段都只能先进入真实 GuardRelease，动作 Buffer / Cancel 只允许配置在 `Release` Track。
- Lock-on 转向 V1：`GuardStart / GuardLoop / GuardRelease` 即使没有 GuardWalk 输入，也会由 `PlayerMovementMotor` 用防御转向速度平滑面向锁定 Boss；`GuardHit / PerfectGuard` 不自动回正，避免破坏防御受击和精防方向动画。

`Guard` 负责：

```text
1. 处理 GuardPressed / GuardHeld 进入防御。
2. 处理 GuardHeld 持续保持防御。
3. 处理 GuardHeld=false 退出防御。
4. 管理 GuardStart / GuardLoop / PerfectGuard / GuardHit / GuardRelease / GuardReset。
5. 管理 PerfectGuardWindow。
6. 管理普通防御成功后的 GuardHit。
7. 管理防御中慢速移动。
8. 管理 Guard 状态内 EvadeInput / SkillInput 缓存。
9. 管理 Guard 后段转 Attack / Evade / Skill / Idle / Locomotion。
```

`Guard` 不负责：

```text
1. 攻击判定。
2. 技能攻击判定。
3. 闪避无敌帧。
4. PerfectEvade 判定。
5. 回血。
6. GuardCounter 反击派生。
```

---

## 2. 状态分层

```text
Guard
├── GuardStart
├── GuardLoop
├── PerfectGuard
├── GuardHit
├── GuardRelease
└── GuardReset
```

---

## 3. 进入条件

| 来源状态        | 条件                                       | 目标状态  | 说明        |
| ----------- | ---------------------------------------- | ----- | --------- |
| Idle        | GuardPressed 或 GuardHeld 且 CanGuard=true           | Guard | 待机进入防御    |
| Locomotion  | GuardPressed 或 GuardHeld 且 CanGuard=true           | Guard | 移动进入防御    |
| Attack      | GuardPressed 或 GuardHeld 且 CommitCancelWindow=true | Guard | 攻击起手取消转防御 |
| Attack      | GuardPressed 或 GuardHeld 且 GuardCancelWindow=true  | Guard | 攻击后段转防御   |
| Evade       | GuardPressed 或 GuardHeld 且 GuardCancelWindow=true  | Guard | 闪避结束转防御   |
| Skill       | GuardPressed 或 GuardHeld 且 GuardCancelWindow=true  | Guard | 技能结束转防御   |
| HitReaction | GuardPressed 或 GuardHeld 且 ResetWindow=true        | Guard | 受击恢复转防御   |
| Knockdown   | GuardPressed 或 GuardHeld 且 ResetWindow=true        | Guard | 起身恢复转防御   |

Guard 输入不进入普通缓存。只有当前状态允许进入 Guard，或者当前状态的 `GuardCancelWindow / ResetWindow / CommitCancelWindow` 开启时，才检测 `GuardPressed || GuardHeld` 并进入 Guard。

---

## 4. Guard 输入规则

```text
GuardPressed：
- 记录 GuardPressedTime。
- 当前状态允许防御时进入 Guard。
- 当前状态不允许防御时不进入 Guard。
- 不进入普通输入缓存。

GuardHeld：
- Guard 状态中持续保持防御。
- GuardCancelWindow 开启时用于进入 Guard。
- GuardLoop 中保持 GuardLoop。
- GuardRelease 阶段中如果重新检测到 GuardPressed 或 GuardHeld，则重新进入 GuardStart 时间轴。

Guard 释放：
- 不读取 `GuardReleased` 瞬时事件。
- `GuardHeld=false` 表示释放请求。
- GuardStart 中短按释放必须等 `MinimumGuardStartDuration` 后才退出。
- GuardLoop 中 `GuardHeld=false` 进入 GuardRelease。
- 防御保持和释放动画同步必须使用稳定状态信号，例如 `PlayerState == Guard`、`PlayerStatePhase`、`IsGuardReentry`。
```

---

## 5. GuardStart

`GuardStart` 是防御启动阶段。

```text
作用：
1. 播放防御起手动画。
2. 记录 GuardStartTime。
3. 根据 GuardPressedTime / GuardStartTime 开启 PerfectGuardWindow。
4. 开启 GuardBlockActive。
5. 玩家进入可防御状态。
```

时间：

```text
GuardStartDuration = 0.16s
```

窗口：

```text
0.00 - 0.16 GuardStart
0.00 - 0.18 PerfectGuardWindow
0.04 - 持续 GuardBlockActive
```

说明：

```text
1. GuardStart 开始后，角色进入防御姿态。
2. GuardStart 前 0.18s 为 PerfectGuardWindow。
3. GuardBlockActive 从 0.04s 开始生效。
4. GuardBlockActive 生效前被命中，进入 HitReaction / Knockdown。
5. PerfectGuardWindow 内被允许完美防御的攻击命中，进入 PerfectGuard。
6. GuardStart 结束且 GuardHeld=true，进入 GuardLoop。
7. GuardStart 期间 GuardHeld=false：至少等 `MinimumGuardStartDuration` 后，无移动输入进入 GuardRelease，有移动输入直接回 Locomotion。
8. GuardStart 期间如果存在移动输入，更新 Guard Walk 方向并允许 `PlayerMovementMotor` 执行防御慢走；PerfectGuardWindow 与 GuardBlockActive 时间不因此改变。
```

---

## 6. GuardLoop

`GuardLoop` 是防御保持阶段。

```text
作用：
1. 持续保持 GuardBlockActive。
2. 持续读取 GuardHeld。
3. 允许防御中慢速移动。
4. 允许 LockOnPressed 切换锁定。
5. 处理普通防御受击。
6. 读取 EvadeInput / SkillInput 缓存。
7. GuardHeld=false 后进入 GuardRelease。
```

状态规则：

```text
if GuardHeld == true:
    Stay GuardLoop

if GuardHeld == false and MoveInput <= MoveThreshold:
    Enter GuardRelease

if GuardHeld == false and MoveInput > MoveThreshold:
    Enter Locomotion
```

防御保持期间：

```text
1. GuardBlockActive=true。
2. PerfectGuardWindow=false。
3. MoveInput 控制防御慢走。
4. LookInput 持续响应。
5. LockOnPressed 响应，只切换 ControlMode。
6. RecoverHpPressed 不响应。
```

---

## 7. PerfectGuard

`PerfectGuard` 是完美防御阶段。

触发条件：

```text
if Guard
and PerfectGuardWindow == true
and EnemyAttackHit == true
and AttackData.CanBePerfectGuarded == true:
    Enter Guard.PerfectGuard
```

触发后处理：

```text
1. 本次攻击不造成 HP 伤害。
2. 本次攻击不造成 GuardDamage。
3. 记录 HasPerfectGuardedAttackId，防止同一 AttackId 重复触发。
4. 播放 PerfectGuard 动画。
5. 播放 PerfectGuard 特效。
6. 播放 PerfectGuard 音效。
7. 触发短时间 HitStop。
8. 触发轻微 CameraShake。
9. 奖励 BetaEnergy。
10. 进入 PerfectGuardRecovery。
```

PerfectGuard V1 标准：

```text
1. CombatHitResolver 输出 PerfectGuard 时 AppliedHpDamage=0，AppliedGuardDamage=0。
2. PlayerCombatReceiver 奖励 BetaEnergy=2，并钳制在 32 上限内。
3. CombatFeedbackPlayer 使用接触防御反馈：HitStop=0.06s，TimeScale=0.06。
4. CameraShake 开启：Amplitude=0.075，Duration=0.12s，Frequency=34。
5. Demo V1 使用正式自制白金接触爆发 `FX_Feedback_PerfectGuard_Stellar.prefab` 与 `Assets/Audio/hit_perfect.mp3`；旧测试占位资源已清理，但 PerfectGuard 反馈语义不变。
6. PerfectGuard 不使用 PerfectEvade 的 BulletTime；PerfectEvade 也不复用 PerfectGuard 的金属接触火花、强 HitStop 或 CameraShake。
7. 普通 GuardHit 不奖励 BetaEnergy，不扣除玩家 SH，也不会因连续防御进入 Knockdown。
8. 每次正式 PerfectGuard 都令 Boss 独立护盾扣 1 格；护盾未归零时，只有当前 HitNode 的 `TriggersPerfectGuardBossStagger` 为 true 才触发短硬直；最后一格归零时跳过短硬直并进入 6 秒 `ShieldBreakStun`。
```

PerfectGuardChainWindow：

```text
PerfectGuardChainInputStart = 0.1667s
PerfectGuardChainInputEnd = 0.4667s
PerfectGuardChainActiveDuration = 0.16s
```

规则：

```text
1. ChainWindow 只在 PerfectGuard 反应阶段处理。
2. 玩家必须在 ChainInput 区间内重新按下 Guard，才会打开 ChainWindow。
3. ChainWindow 打开期间，如果下一次敌方 HitNode 可被 PerfectGuard，则再次进入 PerfectGuard。
4. 每次链式 PerfectGuard 成功后重置 PerfectGuard 反应时间轴，并清空旧 ChainWindow。
5. 持续按住 Guard 不会自动打开 ChainWindow。
6. GuardHit、GuardLoop、GuardRelease 不处理 ChainWindow。
```

PerfectGuard 后续流转：

```text
PerfectGuardRecovery
+ GuardHeld=true
→ GuardLoop

PerfectGuardRecovery
+ GuardHeld=false
+ PerfectGuard Cancel_ToGuardRelease=true
→ GuardRelease

GuardRelease
+ AttackPressed in AttackResetWindow
→ Attack
```

PerfectGuard 的 Release 窗口为 `0.21666667s - 0.50s`。窗口前松开 Guard 不会提前结束反应；窗口内松开会关闭 GuardBlockActive 和 ChainWindow，进入真实 GuardRelease。松手同帧 Attack 不缓存，必须在 Release 的现有 AttackReset 窗口内重新按下。

`GuardCounter` 不纳入本状态设计。普通攻击在 `GuardCounterWindow` 中的反击派生不实现，只保留参数与窗口占位。

---

## 8. GuardHit

`GuardHit` 是普通防御成功受击阶段。

触发条件：

```text
if Guard
and PerfectGuardWindow == false
and EnemyAttackHit == true
and AttackData.CanBeGuarded == true:
    Enter GuardHit
```

处理规则：

```text
1. 本次攻击不造成任何 HP 伤害。
2. 不消费 SH；底层 GuardDamage 只保留为 Timeline / 命中结果数据。
3. 播放对应方向 GuardHit 动画。
4. 触发轻微 HitStop。
5. 触发轻微 CameraShake。
6. GuardHit 结束后，如果 GuardHeld=true，回到 GuardLoop。
7. GuardHit 结束后，如果 GuardHeld=false，进入 GuardRelease。
8. GuardHit 反应期间保持 GuardBlockActive；如果一直按住 Guard，期间再次普通防御受击会重新进入 GuardHit 并重播防御受击动画。
9. GuardHit 不直接响应 Evade / Skill / Attack，也不写入对应动作缓存；该阶段唯一取消许可是 `Cancel_ToGuardRelease`。
10. `Cancel_ToGuardRelease` 窗口开启且 `GuardHeld=false` 时进入真实 GuardRelease 并关闭 GuardBlockActive；松手同帧的 Evade / Skill 转存到 Release 起始 Buffer，等待 Release Cancel 窗口消费，Attack 同帧输入忽略且必须在 Release AttackReset 窗口内重新按下。
```

伤害规则：

```text
BlockedHpDamage = 0
```

普通 GuardHit 不存在 SH 破防规则，也不会因本次可防攻击的原始伤害足以致死而进入 Dead。不可防攻击不进入 GuardHit，仍按自身 ReactionIntent 与完整伤害结算。

---

## 9. GuardRelease

`GuardRelease` 是松开防御后的退出阶段。

触发条件：

```text
GuardHeld=false
→ GuardRelease
```

时间：

```text
GuardReleaseDuration = 0.22s
```

规则：

```text
1. 播放 GuardRelease 动画。
2. GuardBlockActive=false。
3. PerfectGuardWindow=false。
4. 不再普通防御。
5. 可进入 GuardReset 功能窗口，但动画阶段继续保持 Recovery。
6. GuardRelease 阶段重新检测到 GuardPressed 或 GuardHeld 时，重新进入 GuardStart 判定时间轴，并通过 `IsGuardReentry` 使用快速回防御动画路径。
7. GuardRelease 阶段如果出现 MoveInput，则直接进入 Locomotion，避免移动中继续播放站桩 GuardEnd 造成脚步不动但角色滑动。
8. 从 GuardHit 进入 Release 的同帧 Evade / Skill 可先写入现有 Release Buffer，动作切换仍必须等待对应 Release Cancel 窗口；同帧 Attack 不写入缓存。
```

---

## 10. GuardReset

`GuardReset` 是防御释放后的功能重置区间，不是 GuardRelease 的 Animator Phase。该窗口开启时 `IsResetWindow=true`，`PlayerStatePhase` 仍保持 `Recovery`。

```text
作用：
1. 允许转入 Attack。
2. 允许消费 EvadeInput 缓存进入 Evade。
3. 允许消费 SkillInput 缓存进入 Skill。
4. 允许自然回 Idle / Locomotion。
```

时间：

```text
GuardResetWindow = GuardRelease 0.10s - 0.22s
```

---

## 11. PerfectGuard 判定规则

### 11.1 判定数据

```text
GuardPressedTime：
GuardInput 按下时间。

GuardStartTime：
进入 Guard 状态时间。

PerfectGuardWindow：
完美防御窗口。

PerfectGuardChainWindow：
PerfectGuard 反应阶段中通过重按 Guard 打开的连续完美防御窗口。

GuardBlockActive：
普通防御生效标记。

EnemyAttackHit：
敌人攻击命中玩家 Guard / Hurtbox 的事件。
```

### 11.2 PerfectGuardWindow

```text
PerfectGuardWindowStart = GuardStartTime + 0.00s
PerfectGuardWindowEnd   = GuardStartTime + 0.18s
PerfectGuardWindowDuration = 0.18s
```

### 11.3 判定顺序

```text
1. 如果 HP <= 0：
   → Dead

2. 如果 Guard 状态中 EnemyAttackHit 且 PerfectGuardWindow=true 且 AttackData.CanBePerfectGuarded=true：
   → Guard.PerfectGuard

   说明：PerfectGuardWindow 是聚合结果，来源包括 GuardStart 普通窗口和 PerfectGuardChainWindow。

3. 如果 Guard 状态中 EnemyAttackHit 且 GuardBlockActive=true 且 AttackData.CanBeGuarded=true：
   → Guard.GuardHit

4. 如果 Guard 状态中 EnemyAttackHit 且 AttackData.CanBeGuarded=false：
   → 按 CombatHitOutcome 进入 HitReaction / Knockdown，DamageOnly 只扣 HP
```

---

## 12. 防御方向规则

敌人攻击命中时计算攻击来源方向：

```text
HitDirection = DirectionFromAttackerToPlayerRelativeToPlayerFacing
```

方向映射：

| HitDirection | GuardHit 动画           |
| ------------ | --------------------- |
| Front        | Proto_Guard_Hit_Back  |
| Left         | Proto_Guard_Hit_Left  |
| Right        | Proto_Guard_Hit_Right |

攻击来自背后：

```text
if HitDirection == Back:
    if BackGuardAllowed == false:
        Enter HitReaction / Knockdown
    else:
        Enter GuardHit
```

参数：

```text
BackGuardAllowed = false
GuardFrontAngle = 140°
```

---

## 13. 防御中移动规则

Guard 中允许慢速移动：

```text
1. MoveInput 持续读取。
2. MoveMagnitude > MoveThreshold 时进入 GuardWalk。
3. MoveMagnitude <= MoveThreshold 时播放 GuardIdle。
4. GuardWalk 不改变顶层状态，仍属于 Guard。
5. GuardWalk 主要在 GuardLoop / PlayerStatePhase.Loop 执行。
6. GuardStart 中如果有 MoveInput，也允许 GuardWalk 位移和方向动画提前接入；Gameplay 上仍处于 GuardStart，PerfectGuardWindow / GuardBlockActive 按 GuardStart 时间轴生效。
7. GuardHit / PerfectGuard / 原地 GuardRelease 不执行 GuardWalk 位移。
8. Guard 状态但 `IsGuardWalking=false` 时，Motor 立即清零普通水平速度，避免站桩 GuardStart / GuardEnd 滑步。
```

自由视角：

```text
1. MoveDirection 按相机相对方向计算。
2. 角色保持防御朝向。
3. 角色转向速度降低。
```

锁定视角：

```text
1. 角色持续朝向 Boss。
2. MoveInput 转换为相对 Boss 的前后左右慢走。
3. W：防御前进。
4. S：防御后退。
5. A：防御左移。
6. D：防御右移。
7. 原地 GuardStart / GuardLoop / GuardRelease 也持续朝向 Boss；GuardHit / PerfectGuard 期间保留反应方向。
```

速度：

```text
GuardWalkSpeed = 1.8 m/s
GuardBackwardSpeedRate = 0.75
GuardStrafeSpeedRate = 0.85
GuardRotateSpeed = 540°/s
```

---

## 14. Guard 输入响应规则

|输入|响应方式|是否缓存|
|---|---|--:|
|MoveInput|慢速移动|否|
|LookInput|持续响应|否|
|LockOnPressed|响应，只切换 ControlMode|否|
|LightAttackPressed|GuardResetWindow 内即时响应|否|
|HeavyAttackPressed|GuardResetWindow 内即时响应|否|
|EvadePressed|EvadeBufferWindow 内缓存，EvadeCancelWindow 内消费|是|
|EvadeHeld|读取，不触发 Sprint|否|
|EvadeReleased|清除 Evade 缓存|否|
|GuardHeld|持续保持 Guard|否|
|GuardPressed|PerfectGuard 反应 `0.1667s - 0.4667s` 内重新按下时打开 ChainWindow|否|
|GuardHeld=false|进入 GuardRelease|否|
|Skill1Pressed|SkillBufferWindow 内缓存，SkillCancelWindow 内消费|是|
|Skill2Pressed|SkillBufferWindow 内缓存，SkillCancelWindow 内消费|是|
|RecoverHpPressed|不响应|否|

---

## 15. Guard 时间轴与窗口

### 15.1 GuardStart 时间轴

```text
0.00s ───────────────────────────── 0.16s

0.00 - 0.16 GuardStart
0.00 - 0.18 PerfectGuardWindow
0.04 - ∞    GuardBlockActive
```

### 15.2 GuardLoop 时间轴

```text
GuardLoop 为循环状态。

GuardHeld=true  → 保持 GuardLoop
GuardHeld=false → GuardRelease
EnemyAttackHit + PerfectGuardWindow=true → PerfectGuard
EnemyAttackHit + GuardBlockActive=true   → GuardHit
```

### 15.2.1 PerfectGuardChainWindow 时间轴

```text
PerfectGuard 反应开始

0.00 - 0.50 PerfectGuardReaction
0.1667 - 0.4667 ChainInputWindow
0.2167 - 0.50 GuardReleaseCancelWindow
GuardPressed in ChainInputWindow → 0.16s PerfectGuardChainWindow
GuardHeld=false 且 GuardReleaseCancelWindow=true → GuardRelease
```

说明：

```text
1. ChainWindow 允许 Boss 连击间隔缩短到 0.25s - 0.45s。
2. ChainWindow 需要每一击重新按 Guard，不接受一直按住 Guard 自动连弹。
3. ChainWindow 到期或离开 PerfectGuard 反应阶段后立即关闭。
4. GuardReleaseCancelWindow 前松开 Guard 不提前结束 PerfectGuard；窗口内松开先进入真实 Release，再由 Release AttackReset 接受重新按下的攻击。
5. PerfectGuard 不直接响应或缓存 Attack，也不允许配置 Evade / Skill Buffer 或 Evade / Skill / AttackReset Cancel。
```

### 15.2.2 GuardHitReaction 时间轴

```text
GuardHit 反应开始

0.00 - 0.83 GuardHitReaction
0.00 - 0.83 GuardBlockActive 保持
0.18 - 0.83 GuardReleaseCancelWindow
GuardHeld=false 且 GuardReleaseCancelWindow=true → GuardRelease
```

说明：

```text
1. GuardHit 前段是强硬直，且整个反应阶段都不能直接取消到 Evade / Skill / Attack。
2. `Guard.asset` 的 `GuardHit` 轨道只允许 `GuardHitReaction` Marker 与 `Cancel_ToGuardRelease`；配置 Evade / Skill Buffer 或 Evade / Skill / AttackReset Cancel 时 Validation 必须报错。
3. 持续按住 Guard 时的 Evade / Skill / Attack 输入直接忽略，不产生状态切换或缓存。
4. GuardReleaseCancelWindow 内松开 Guard 会进入真实 GuardRelease，GuardBlockActive 关闭；窗口前松开不会打断 GuardHit。Boss 下一击如果打在 Release 空档或 GuardStart block 生效前，不会被普通防御保护。
5. 重新精防必须通过 Release 中重按 Guard 进入 GuardStart，再使用 GuardStart 的 PerfectGuardWindow。
6. 松开 Guard 同帧的 Evade / Skill 输入按 Release 在 `elapsed=0` 的 Buffer 规则写入现有缓存，随后只能由 Release Cancel 窗口消费；Attack 不缓存，必须在 Release AttackReset 窗口内重新按下。
7. 如果持续按住 Guard，GuardHit 结束后回 GuardLoop。
```

### 15.3 GuardRelease 时间轴

```text
0.00s ───────────────────────────── 0.22s

0.00 - 0.22 GuardRelease
0.04 - 0.18 EvadeBufferWindow
0.08 - 0.20 SkillBufferWindow
0.10 - 0.22 GuardResetWindow
0.12 - 0.22 EvadeCancelWindow
0.14 - 0.22 SkillCancelWindow
0.12 - 0.22 AttackResetWindow
0.18 - 0.22 ReturnWindow
```

---

## 16. 缓存区间设计

Guard 状态只缓存：

```text
1. EvadeInput
2. SkillInput
```

Guard 状态不缓存：

```text
1. LightAttackPressed
2. HeavyAttackPressed
3. GuardInput
4. RecoverInput
5. LockOnPressed
```

---

### 16.1 EvadeBufferWindow

```text
EvadeBufferWindow:
Start = GuardReleaseStartTime + 0.04s
End   = GuardReleaseStartTime + 0.18s
Duration = 0.14s
```

缓存规则：

```text
if EvadeBufferWindow == true
and EvadePressed:
    BufferEvadeInput()
```

缓存有效期：

```text
EvadeBufferExpireTime = min(BufferedTime + 0.25s, GuardReleaseEndTime)
```

---

### 16.2 EvadeCancelWindow

```text
EvadeCancelWindow:
Start = GuardReleaseStartTime + 0.12s
End   = GuardReleaseStartTime + 0.22s
Duration = 0.10s
```

消费规则：

```text
if EvadeCancelWindow == true:
    if HasBufferedEvadeInput:
        ConsumeEvadeBuffer()
        EnterEvade()
    else if EvadePressedThisFrame:
        EnterEvade()
```

---

### 16.3 SkillBufferWindow

```text
SkillBufferWindow:
Start = GuardReleaseStartTime + 0.08s
End   = GuardReleaseStartTime + 0.20s
Duration = 0.12s
```

缓存规则：

```text
if SkillBufferWindow == true
and SkillPressed:
    BufferSkillInput()
```

缓存有效期：

```text
SkillBufferExpireTime = min(BufferedTime + 0.30s, GuardReleaseEndTime)
```

---

### 16.4 SkillCancelWindow

```text
SkillCancelWindow:
Start = GuardReleaseStartTime + 0.14s
End   = GuardReleaseStartTime + 0.22s
Duration = 0.08s
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

### 16.5 AttackResetWindow

```text
AttackResetWindow:
Start = GuardReleaseStartTime + 0.12s
End   = GuardReleaseStartTime + 0.22s
Duration = 0.10s
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
```

---

### 16.6 ReturnWindow

```text
ReturnWindow:
Start = GuardReleaseStartTime + 0.18s
End   = GuardReleaseStartTime + 0.22s
Duration = 0.04s
```

自然结束规则：

```text
if GuardReleaseEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 17. 缓存冲突处理

### 17.1 Evade 缓存 vs Skill 缓存

```text
优先级：
EvadeInput > SkillInput
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput:
    EnterEvade()
else if SkillCancelWindow == true
and HasBufferedSkillInput
and BetaEnergy >= SkillCost:
    EnterSkill()
```

### 17.2 Skill1 / Skill2 冲突

```text
1. Skill1 和 Skill2 属于同一 Skill 槽。
2. 后输入覆盖前输入。
3. 被覆盖的 SkillInput 清除。
```

### 17.3 Evade 缓存 vs Attack 输入

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput:
    EnterEvade()
else if AttackResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
```

优先级：

```text
EvadeInput > LightAttack / HeavyAttack
```

### 17.4 Skill 缓存 vs Attack 输入

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

### 17.5 缓存过期

```text
if GuardReleaseEnd:
    ClearEvadeBuffer()
    ClearSkillBuffer()
```

Guard 缓存只在 Guard 状态内有效，不带入 Idle / Locomotion / Evade / Skill / Attack。

---

## 18. 状态退出条件

|输入 / 事件|条件|下一个状态|
|---|---|---|
|HP <= 0|任意时刻|Dead|
|EnemyAttackHit|PerfectGuardWindow=true 且 AttackData.CanBePerfectGuarded=true|Guard.PerfectGuard|
|EnemyAttackHit|GuardBlockActive=true 且 AttackData.CanBeGuarded=true|Guard.GuardHit|
|EnemyAttackHit|AttackData.CanBeGuarded=false|HitReaction / Knockdown|
|GuardHeld=false|GuardStart / GuardLoop 中松开防御且无移动输入|GuardRelease|
|GuardHeld=false|GuardStart / GuardLoop 中松开防御且有移动输入|Locomotion|
|GuardPressed 或 GuardHeld|GuardRelease 阶段重新按下或按住|GuardStart|
|EvadePressed|EvadeCancelWindow=true|Evade|
|Skill1Pressed|SkillCancelWindow=true 且 BetaEnergy 足够|Skill|
|LightAttackPressed|GuardResetWindow=true 且 CanAttack=true|Attack|
|HeavyAttackPressed|GuardResetWindow=true 且 CanAttack=true|Attack|
|GuardReleaseEnd|MoveMagnitude > 0.1|Locomotion|
|GuardReleaseEnd|MoveMagnitude <= 0.1|Idle|

---

## 19. 动画需求

### 19.1 基础防御动画

|场景|动画资源|
|---|---|
|防御起手 / 防御动画|Proto_Guard|
|防御保持|Proto_Guard_Idle|
|防御取消|Proto_Guard_End|

### 19.2 防御受击动画

|场景|动画资源|
|---|---|
|防御成功受击-正面|Proto_Guard_Hit_Back|
|防御成功受击-左侧|Proto_Guard_Hit_Left|
|防御成功受击-右侧|Proto_Guard_Hit_Right|

### 19.3 完美防御动画

|场景|动画资源|
|---|---|
|完美防御-左|P_Eve_Sword_Normal_JustParryLeft|
|完美防御-右|P_Eve_Sword_Normal_JustParryRight|

### 19.4 防御移动动画

|场景|动画资源|
|---|---|
|防御前进|Proto_Guard_Walk_Forward_Start / Proto_Guard_Walk_Forward / Proto_Guard_Walk_Forward_Left / Proto_Guard_Walk_Forward_End|
|防御后退|Proto_Guard_Walk_Backward_Start / Proto_Guard_Walk_Backward / Proto_Guard_Walk_Backward_Right / Proto_Guard_Walk_Backward_End|
|防御左移|Proto_Guard_Walk_Left_Start / Proto_Guard_Walk_Left / Proto_Guard_Walk_Left_End|
|防御右移|Proto_Guard_Walk_Right_Start / Proto_Guard_Walk_Right / Proto_Guard_Walk_Right_End|

Guard 动画资源中已经包含防御保持、防御受击、完美防御、防御取消和防御慢走动画。

---

## 20. 动画参数

|参数名|类型|用途|
|---|---|---|
|PlayerState|Enum / Int|当前顶层状态为 Guard|
|PlayerStatePhase|Enum / Int|Start / Active / Recovery / Reset / Loop；GuardLoop 使用 Loop，GuardRelease 全程使用 Recovery，Reset 保留给其他动作与反应状态|
|GuardHeld|Bool|GuardInput 是否仍按住|
|IsGuardReentry|Bool|GuardRelease 中重新进入 GuardStart 时为 true，用于 Animator 避免完整重播 GuardStart|
|GuardBlockActive|Bool|普通防御是否生效|
|IsPerfectGuardWindow|Bool|是否处于完美防御窗口|
|IsPerfectGuardChainWindow|Bool|PerfectGuard 反应阶段中是否处于连续弹反窗口；仅作为代码调试字段，不新增 Animator 参数|
|IsGuardWalking|Bool|GuardLoop 中是否正在防御慢走|
|GuardMoveDirection|Enum / Int|None / Forward / Backward / Left / Right；停止输入时保留最近方向以选择对应 GuardWalkEnd|
|GuardHitDirection|Enum / Int|Back / Left / Right|
|MoveMagnitude|Float|实际防御慢走速度归一化后的动画混合强度|
|MoveX|Float|实际防御慢走速度投影后的横向动画参数|
|MoveY|Float|实际防御慢走速度投影后的纵向动画参数|
|IsInCombat|Bool|是否战斗模式|
|IsLockOn|Bool|是否锁定 Boss|
|HasBufferedEvadeInput|Bool|是否有 Evade 缓存|
|HasBufferedSkillInput|Bool|是否有 Skill 缓存|
|TriggerGuard|Trigger|触发防御|
|TriggerPerfectGuard|Trigger|触发完美防御|
|TriggerGuardHit|Trigger|触发普通防御受击|
|TriggerGuardRelease|Trigger|触发防御释放|
|TriggerGuardToEvade|Trigger|防御转闪避|
|TriggerGuardToSkill|Trigger|防御转技能|
|TriggerGuardToAttack|Trigger|防御转攻击|
|TriggerGuardToMove|Trigger|防御转移动|

---

## 21. 配置事件

|事件|时间|作用|
|---|--:|---|
|GuardStart|0.00s|进入 Guard|
|PerfectGuardWindowStart|0.00s|开启完美防御窗口|
|GuardBlockActiveStart|0.04s|开启普通防御|
|GuardStartEnd|0.16s|进入 GuardLoop|
|PerfectGuardWindowEnd|0.18s|关闭完美防御窗口|
|GuardReleaseStart|0.00s|进入 GuardRelease|
|EvadeBufferWindowStart|0.04s|开启 Evade 缓存|
|SkillBufferWindowStart|0.08s|开启 Skill 缓存|
|GuardResetWindowStart|0.10s|开启重置区间|
|EvadeCancelWindowStart|0.12s|允许消费 Evade|
|AttackResetWindowStart|0.12s|允许 Attack 即时响应|
|SkillCancelWindowStart|0.14s|允许消费 Skill|
|ReturnWindowStart|0.18s|允许自然回 Idle / Locomotion|
|GuardReleaseEnd|0.22s|结束 Guard|

---

## 22. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|GuardStartDuration|0.16s|
|PerfectGuardWindowDuration|0.18s|
|MinimumGuardStartDuration|0.15s|
|PerfectGuardChainInputStart|0.1667s|
|PerfectGuardChainInputEnd|0.4667s|
|PerfectGuardChainActiveDuration|0.16s|
|GuardBlockActiveStartTime|0.04s|
|GuardReleaseDuration|0.22s|
|EvadeBufferStartTime|0.04s|
|EvadeBufferEndTime|0.18s|
|EvadeCancelStartTime|0.12s|
|EvadeCancelEndTime|0.22s|
|SkillBufferStartTime|0.08s|
|SkillBufferEndTime|0.20s|
|SkillCancelStartTime|0.14s|
|SkillCancelEndTime|0.22s|
|AttackResetStartTime|0.12s|
|AttackResetEndTime|0.22s|
|ReturnWindowStartTime|0.18s|
|GuardWalkSpeed|1.8 m/s|
|GuardBackwardSpeedRate|0.75|
|GuardStrafeSpeedRate|0.85|
|GuardRotateSpeed|540°/s|
|GuardFrontAngle|140°|
|BackGuardAllowed|false|
|PerfectGuardHitStopDuration|0.08s|
|GuardHitStopDuration|0.04s|
|PerfectGuardBetaEnergyGain|2|
|GuardToAttackBlendTime|0.05s|
|GuardToEvadeBlendTime|0.05s|
|GuardToSkillBlendTime|0.06s|
|GuardToMoveBlendTime|0.10s|
|GuardToIdleBlendTime|0.10s|

---

## 23. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|GuardHeld|CanGuard=false|不进入 Guard|
|GuardPressed|当前状态无 GuardCancelWindow / ResetWindow / CommitCancelWindow|不进入 Guard，不缓存|
|GuardPressed|GuardHit 中按下 Guard|不触发 PerfectGuard，不缓存；需要先松开进入 Release 再重按 GuardStart|
|GuardHeld=false|当前不在 Guard|无状态影响|
|LightAttackPressed|GuardResetWindow 未开启|忽略，不缓存|
|HeavyAttackPressed|GuardResetWindow 未开启|忽略，不缓存|
|EvadePressed|不在 EvadeBufferWindow 且不在 EvadeCancelWindow|忽略，不缓存|
|Skill1Pressed|不在 SkillBufferWindow 且不在 SkillCancelWindow|忽略，不缓存|
|Skill1Pressed|SkillCancelWindow 开启但 BetaEnergy 不足|清除缓存，播放能量不足反馈|
|RecoverHpPressed|任意 Guard 阶段|不响应|
|EnemyAttackHit|攻击不可防御|HitReaction / Knockdown|

---

## 24. 状态流转

```text
Idle / Locomotion
+ GuardPressed / GuardHeld
+ CanGuard
→ GuardStart
→ GuardLoop

Attack
+ GuardPressed / GuardHeld
+ CommitCancelWindow / GuardCancelWindow
→ GuardStart

Evade / Skill
+ GuardPressed / GuardHeld
+ GuardCancelWindow
→ GuardStart

GuardStart
+ EnemyAttackHit
+ PerfectGuardWindow=true
+ CanBePerfectGuarded=true
→ PerfectGuard

GuardStart / GuardLoop
+ EnemyAttackHit
+ GuardBlockActive=true
+ CanBeGuarded=true
→ GuardHit

GuardStart / GuardLoop
+ EnemyAttackHit
+ CanBeGuarded=false
→ HitReaction / Knockdown

GuardStart
+ MoveMagnitude > 0.1
→ GuardWalkStart
→ GuardWalkLoop

PerfectGuard
+ GuardPressed in ChainInputWindow
→ PerfectGuardChainWindow

PerfectGuard
+ EnemyAttackHit
+ PerfectGuardChainWindow=true
+ CanBePerfectGuarded=true
→ PerfectGuard

PerfectGuard
+ GuardHeld=true
→ GuardLoop

PerfectGuard
+ GuardHeld=false
→ GuardRelease

GuardHit
+ GuardHeld=true
→ GuardLoop

GuardHit
+ GuardHeld=false
→ GuardRelease

GuardRelease
+ GuardPressed / GuardHeld
→ GuardStart

GuardStart
+ EnemyAttackHit
+ PerfectGuardWindow=true
+ CanBePerfectGuarded=true
→ PerfectGuard

GuardLoop
+ GuardHeld=false
→ GuardRelease

GuardLoop
+ MoveMagnitude > 0.1
→ GuardWalkStart
→ GuardWalkLoop

GuardWalkLoop
+ MoveMagnitude <= 0.1
+ GuardHeld=true
→ GuardWalkEnd
→ GuardLoop

GuardWalkLoop
+ GuardHeld=false
→ Locomotion

GuardStart / GuardLoop
+ GuardHeld=false
+ MoveMagnitude > 0.1
→ Locomotion

GuardRelease
+ GuardPressed / GuardHeld
→ GuardStart

GuardRelease / GuardReset
+ EvadeInput in EvadeBufferWindow
→ 缓存
→ EvadeCancelWindow 消费
→ Evade

GuardRelease / GuardReset
+ SkillInput in SkillBufferWindow
→ 缓存
→ SkillCancelWindow 消费
→ Skill

GuardReset
+ LightAttackPressed / HeavyAttackPressed
+ AttackResetWindow=true
→ Attack

GuardReleaseEnd
+ MoveMagnitude > 0.1
→ Locomotion

GuardReleaseEnd
+ MoveMagnitude <= 0.1
→ Idle
```
