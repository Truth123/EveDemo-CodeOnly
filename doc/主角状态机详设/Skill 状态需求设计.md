## 1. 状态定位

`Skill` 是玩家角色的技能释放状态，用于处理消耗 `BetaEnergy` 释放技能、技能攻击判定、技能霸体 / 受击、技能后摇派生、技能结束后返回 Idle / Locomotion 等逻辑。

`Skill1Input` 为 Press 输入。玩家按下 `Skill1Input` 后，状态机判断当前状态是否允许释放技能，资源系统判断 `BetaEnergy` 是否足够；条件满足时进入 `Skill` 状态并消耗 Beta 能量。`Skill2Input` 作为预留输入，不进入当前 Skill 实现。

`Skill` 负责：

```text
1. 处理 Skill1Pressed 进入 Skill。
2. 进入 Skill 时检查并消耗 BetaEnergy。
3. 管理 SkillStart / SkillActive / SkillRecovery / SkillResetWindow。
4. 管理技能攻击 Hitbox 开启与关闭。
5. 管理技能命中伤害、削韧、击退等攻击效果。
6. 管理技能释放期间的霸体 / 受击打断规则。
7. 管理 Skill 状态内 EvadeInput 缓存。
8. 管理 Skill 后段转 Attack / Evade / Guard / Skill。
9. 管理 SkillEnd 后回 Idle / Locomotion。
```

`Skill` 不负责：

```text
1. 普通攻击连招判定。
2. PerfectEvade 判定。
3. PerfectGuard 判定。
4. 防御判定。
5. 回血。
6. LockOn 切换。
7. BetaAttack。
```

## 1.1 Skill1 当前资产基线（2026-08-05）

本节记录 `Skill_Skill1.asset` 的当前正式值，并覆盖本文后续仍保留的早期 `0.90s` 原型示例：

- 总时长 `6.00s`；Startup `0.00～0.50s`、Active `0.50～2.583s`、Recovery `2.583～3.083s`、Reset `3.083～6.00s`。
- Hit1 `0.833～1.000s`：`Damage=60 / PoiseDamage=12 / ReactionIntent=HitReaction`。
- Hit2 `1.200～1.367s`：`Damage=60 / PoiseDamage=12 / ReactionIntent=HitReaction`。
- Hit3 `2.000～2.333s`：`Damage=80 / PoiseDamage=18 / ReactionIntent=Knockdown`。
- `Skill1Forward` Motion 窗口为 `0.50～0.80s / 0.90～1.18s / 1.65～1.98s`；每个窗口只在首次激活时请求一次动作位移。
- `CH_P_EVE_51/Trans/SkillEffect` 在三个 Motion 窗口首次激活时清空并重播主粒子；`hitEffect` 默认关闭，只在对应 HitNode 产生 `DamageOnly / HitReaction / Knockdown / Dead` 时每段播放一次，并在 HitNode 结束时关闭。
- Skill1 消耗 8 BE；SuperArmor 为 `0.00～3.00s`；Evade Buffer 为 `2.583～3.083s`；Evade/Skill Cancel 从 `3.083s` 开始，Guard Cancel 从 `3.417s` 开始，Attack Reset 从 `3.50s` 开始。

前两段命中会让可响应的 Boss 进入或重新播放 HitStagger，第二段重置反应计时；第三段进入 Knockdown。Boss 的 Reaction Gate 仍有最终决定权，黄光承诺段等显式 Block 区间只扣血、不切换反应。

---

## 2. 状态分层

```text
Skill
├── SkillStart
├── SkillActive
├── SkillRecovery
└── SkillResetWindow
```

状态机汇总中 Skill 已定义为 `SkillStart / SkillActive / SkillRecovery / SkillResetWindow` 四个阶段。

---

## 3. 进入条件

| 来源状态       | 条件                                                                | 目标状态  | 说明         |
| ---------- | ----------------------------------------------------------------- | ----- | ---------- |
| Idle       | Skill1Pressed 且 BetaEnergy >= SkillCost                           | Skill | 待机释放技能     |
| Locomotion | Skill1Pressed 且 BetaEnergy >= SkillCost                           | Skill | 移动释放技能     |
| Attack     | Skill1Pressed 且 CommitCancelWindow=true 且 BetaEnergy >= SkillCost | Skill | 攻击起手取消释放技能 |
| Attack     | Skill1Pressed 且 SkillCancelWindow=true 且 BetaEnergy >= SkillCost  | Skill | 攻击后段取消释放技能 |
| Evade      | Skill1Pressed 且 SkillCancelWindow=true 且 BetaEnergy >= SkillCost  | Skill | 闪避后段释放技能   |
| Guard      | Skill1Pressed 且 SkillCancelWindow=true 且 BetaEnergy >= SkillCost  | Skill | 防御后段释放技能   |
| Skill      | Skill1Pressed 且 SkillResetWindow=true 且 BetaEnergy >= SkillCost   | Skill | 技能结束接技能    |

Idle / Locomotion 中 `Skill1Pressed` 资源足够时直接进入 Skill；资源不足时不进入 Skill。

---

## 4. Skill 输入规则

```text
Skill1Pressed：
- 当前状态允许释放 Skill 时，检查 BetaEnergy。
- BetaEnergy >= SkillCost 时进入 Skill。
- 进入 Skill 时立即消耗 SkillCost。
- BetaEnergy < SkillCost 时不进入 Skill，播放能量不足反馈。

Skill2Pressed：
- 当前版本不进入 Skill。
- 不消耗 BetaEnergy。
- 播放技能不可用反馈。

Skill 状态中再次 Skill1Pressed：
- 只在 SkillResetWindow 内响应。
- BetaEnergy >= SkillCost 时重新进入 Skill。
- SkillResetWindow 外不响应，不缓存。

Skill 状态中的 MoveInput：
- 只在 `Cancel_MovementReturn` 窗口内响应。
- 窗口内实时 `MoveMagnitude > 0.1` 时进入 Locomotion。
- 移动输入不缓存；窗口外或无移动输入时继续 Skill。
```

---

## 5. SkillStart

`SkillStart` 是技能起手阶段。

```text
作用：
1. 播放技能起手动画。
2. 锁定本次技能释放方向。
3. 消耗 BetaEnergy。
4. 关闭普通移动控制。
5. 关闭 LockOn 切换。
6. 进入技能动作锁定。
7. 准备开启技能攻击判定。
```

时间：

```text
SkillStartDuration = 0.22s
```

进入时处理：

```text
1. Set PlayerState = Skill
2. Set SkillPhase = SkillStart
3. Consume BetaEnergy = SkillCost
4. Clear incompatible input buffers
5. Lock movement control
6. Lock rotation by SkillFacingRule
7. TriggerSkill1
```

窗口：

```text
0.00 - 0.22 SkillStart
0.00 - 0.12 SkillAimAdjustWindow
0.00 - 0.18 SkillSuperArmorWindow
```

---

## 6. SkillActive

`SkillActive` 是技能生效阶段。

```text
作用：
1. 开启 SkillHitbox。
2. 处理技能命中 Boss。
3. 造成技能伤害。
4. 造成削韧 / 硬直 / 击退。
5. 播放命中特效与音效。
6. 根据技能配置处理多段命中。
```

时间：`0.50～2.583s`。

窗口：

```text
0.500 - 2.583 SkillActive
0.833 - 1.000 Skill1_Hit1
1.200 - 1.367 Skill1_Hit2
2.000 - 2.333 Skill1_Hit3
0.000 - 3.000 SkillSuperArmorWindow
```

攻击判定：

```text
SkillHitboxActive = 任意 Skill HitNode 窗口激活
Skill1_Hit1 = 0.833s - 1.000s
Skill1_Hit2 = 1.200s - 1.367s
Skill1_Hit3 = 2.000s - 2.333s
```

命中规则：

```text
1. SkillHitbox overlap Boss Hurtbox 时，根据当前 Skill HitNode 记录 SkillHit。
2. 同一 SkillCastId + HitNodeId 对同一 Boss Hurtbox 只命中一次。
3. SkillHit 成功后造成该 HitNode 配置的 Damage。
4. SkillHit 成功后造成该 HitNode 配置的 PoiseDamage。
5. SkillHit 成功后播放命中特效、命中音效、HitStop。
6. 普通 SkillEffect 使用 Motion 窗口起点 `0.500 / 0.900 / 1.650s`；命中特效使用真实 HitNode 结算，不从动画或距离推测命中。
```

多段命中规则：

```text
Skill1_Hit1: Damage=60, PoiseDamage=12, ReactionIntent=HitReaction
Skill1_Hit2: Damage=60, PoiseDamage=12, ReactionIntent=HitReaction
Skill1_Hit3: Damage=80, PoiseDamage=18, ReactionIntent=Knockdown
```

---

## 7. SkillRecovery

`SkillRecovery` 是技能后摇阶段。

```text
作用：
1. 关闭 SkillHitbox。
2. 结束主要攻击判定。
3. 保留动作后摇。
4. 允许 EvadeInput 进入缓存。
5. 在对应窗口中响应 GuardHeld。
6. 在对应窗口中响应 Attack / Skill。
7. 在 `Cancel_MovementReturn` 窗口内允许实时移动输入返回 Locomotion。
8. 后段进入 SkillResetWindow。
```

时间：

```text
SkillRecoveryDuration = 0.34s
```

窗口：

```text
0.56 - 0.90 SkillRecovery
0.60 - 0.80 EvadeBufferWindow
0.68 - 0.90 EvadeCancelWindow
0.70 - 0.90 GuardCancelWindow
0.74 - 0.90 AttackResetWindow
0.76 - 0.90 SkillResetWindow
0.82 - 0.90 ReturnWindow
```

---

## 8. SkillResetWindow

`SkillResetWindow` 是技能后摇末段的重置区间。

```text
作用：
1. 允许转入 Attack。
2. 允许消费 EvadeInput 缓存进入 Evade。
3. 允许 GuardHeld 进入 Guard。
4. 允许 Skill1Pressed 再次进入 Skill。
5. 在 `Cancel_MovementReturn` 激活时允许实时移动输入返回 Locomotion。
6. 允许自然回 Idle / Locomotion。
```

时间：

```text
SkillResetWindow = 0.76s - 0.90s
```

---

## 9. 技能方向规则

进入 `Skill` 的第一帧锁定本次技能方向：

```text
SkillDirection = ResolveSkillDirection(
    ControlMode,
    IsLockOn,
    MoveInput,
    MoveMagnitude,
    CharacterForward,
    CameraForward,
    CameraRight,
    LockOnTargetDirection
)
```

### 9.1 自由视角

```text
if IsLockOn == false:
    if MoveMagnitude > MoveThreshold:
        SkillDirection = CameraRelativeMoveDirection
    else:
        SkillDirection = CharacterForward
```

角色朝 `SkillDirection` 快速转向。

### 9.2 锁定视角

```text
if IsLockOn == true:
    SkillDirection = DirectionToLockOnTarget
```

锁定视角下技能强制朝向 Boss。

转向参数：

```text
SkillRotateSpeed = 1440°/s
SkillFaceTargetAngleLimit = 3°
```

---

## 10. 技能资源规则

```text
SkillCost = 8
```

进入 Skill 时立即消耗资源：

```text
if BetaEnergy >= SkillCost:
    BetaEnergy -= SkillCost
    EnterSkill()
else:
    RejectSkill()
    PlaySkillFailFeedback()
```

资源消耗时机：

```text
1. Skill 状态进入成功时立即消耗 BetaEnergy。
2. SkillStart 被敌人打断后不返还 BetaEnergy。
3. SkillActive 未命中不返还 BetaEnergy。
4. Skill 被 HitReaction / Knockdown 打断后不返还 BetaEnergy。
```

---

## 11. 技能攻击规则

| 项目     | 规则                                  |
| ------ | ----------------------------------- |
| 攻击类型   | SkillAttack                         |
| Hitbox | SkillHitbox                         |
| 生效区间   | Skill1 三段 HitNode                   |
| 伤害     | HitNode Damage                       |
| 削韧     | HitNode PoiseDamage                  |
| 击退     | SkillKnockback                      |
| 命中停顿   | SkillHitStop                        |
| 多段命中   | 按 `SkillCastId + HitNodeId + Hurtbox` 去重 |
| 命中对象   | Boss Hurtbox                        |
| 友方命中   | 无                                   |
| 自身受击   | 按 SkillSuperArmor / Invincible 规则处理 |

数值：

```text
Skill1_Hit1 = 0.833s - 1.000s / Damage 60 / PoiseDamage 12 / HitReaction
Skill1_Hit2 = 1.200s - 1.367s / Damage 60 / PoiseDamage 12 / HitReaction
Skill1_Hit3 = 2.000s - 2.333s / Damage 80 / PoiseDamage 18 / Knockdown
SkillKnockback = 1.2m
SkillHitStopDuration = 0.06s
```

---

## 12. 霸体 / 无敌 / 受击规则

Skill 状态不提供闪避无敌帧。

```text
SkillInvincibleWindow = false
```

Skill 状态提供技能霸体窗口：

```text
SkillSuperArmorWindow:
Start = 0.00s
End   = 0.56s
```

受击规则：

```text
1. SkillSuperArmorWindow 内受到普通攻击：
   - 不进入 HitReaction。
   - 受到 HP 伤害。
   - 技能动作不中断。

2. SkillSuperArmorWindow 内受到 CombatReactionIntent.KnockdownReaction / Knockdown：
   - 进入 Knockdown。
   - 技能被打断。

3. SkillSuperArmorWindow 外受到普通攻击：
   - 进入 HitReaction。
   - 技能被打断。

4. HP <= 0：
   - 立即进入 Dead。
```

状态机总览中 Skill 被敌人命中时，如果非霸体 / 非无敌，会进入 HitReaction / Knockdown。

---

## 13. Skill 输入响应规则

| 输入                 | 响应方式                                        | 是否缓存 |
| ------------------ | ------------------------------------------- | ---: |
| MoveInput          | `Cancel_MovementReturn` 内即时返回 Locomotion；否则只用于 SkillEnd 后去向 |    否 |
| LookInput          | 持续响应                                        |    否 |
| LockOnPressed      | 不响应                                         |    否 |
| LightAttackPressed | AttackResetWindow 内即时响应                     |    否 |
| HeavyAttackPressed | AttackResetWindow 内即时响应                     |    否 |
| EvadePressed       | EvadeBufferWindow 内缓存，EvadeCancelWindow 内消费 |    是 |
| EvadeHeld          | 读取，不触发 Sprint                               |    否 |
| EvadeReleased      | 清除 Evade 缓存                                 |    否 |
| GuardHeld          | GuardCancelWindow 内即时响应                     |    否 |
| Skill1Pressed      | SkillResetWindow 内即时响应                      |    否 |
| Skill2Pressed      | 不响应                                         |    否 |
| RecoverHpPressed   | 不响应                                         |    否 |

Skill 状态总体输入响应为：Move 只在 `Cancel_MovementReturn` 内即时返回 Locomotion、窗口外仅用于 SkillEnd 去向且不缓存；Look 一直响应，LockOn 不响应，Light / HeavyAttack 在 ResetWindow 响应，EvadeInput 缓存并在 EvadeCancelWindow 响应，GuardInput 在 GuardCancelWindow 响应，SkillInput 在重置区间响应，RecoverInput 不响应。

---

## 14. Skill 时间轴与窗口

以当前 `SkillTotalDuration = 6.00s` 为标准时间轴：

```text
0.00s ───────────────────────────────────── 6.00s

0.000 - 0.500  SkillStart
0.500 - 2.583  SkillActive
2.583 - 3.083  SkillRecovery
3.083 - 6.000  SkillResetWindow

0.000 - 3.000  SkillSuperArmorWindow
0.500 - 0.800  Skill1Forward / Hit1 吸附
0.833 - 1.000  Skill1_Hit1
0.900 - 1.180  Skill1Forward / Hit2 吸附
1.200 - 1.367  Skill1_Hit2
1.650 - 1.980  Skill1Forward / Hit3 吸附
2.000 - 2.333  Skill1_Hit3
2.583 - 3.083  EvadeBufferWindow
3.083 - 6.000  EvadeCancelWindow / SkillResetWindow
3.417 - 6.000  GuardCancelWindow
3.500 - 6.000  AttackResetWindow
3.500 - 6.000  Cancel_MovementReturn
```

---

## 15. 缓存区间设计

Skill 状态只缓存：

```text
1. EvadeInput
```

Skill 状态不缓存：

```text
1. LightAttackPressed
2. HeavyAttackPressed
3. GuardInput
4. SkillInput
5. RecoverInput
6. LockOnPressed
```

输入系统中缓存规则要求：只有当前不能执行、但当前状态明确允许稍后执行时才进入缓存；消费缓存时按状态优先级处理。

---

### 15.1 EvadeBufferWindow

```text
EvadeBufferWindow:
Start = 0.60s
End   = 0.80s
Duration = 0.20s
```

缓存规则：

```text
if EvadeBufferWindow == true
and EvadePressed:
    BufferEvadeInput()
```

缓存数据：

```text
SkillInputBuffer
{
    BufferedEvadeInput
    BufferedTime
    ExpireTime
    Consumed
}
```

缓存有效期：

```text
EvadeBufferExpireTime = min(BufferedTime + 0.25s, SkillEndTime)
```

限制：

```text
1. EvadePressed 发生在 EvadeBufferWindow 前，直接忽略。
2. EvadePressed 发生在 EvadeCancelWindow 内，立即消费。
3. EvadeReleased 后清除 Evade 缓存。
4. SkillEnd 时清除 Evade 缓存。
```

---

### 15.2 EvadeCancelWindow

```text
EvadeCancelWindow:
Start = 0.68s
End   = 0.90s
Duration = 0.22s
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

### 15.3 GuardCancelWindow

```text
GuardCancelWindow:
Start = 0.70s
End   = 0.90s
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
2. GuardCancelWindow 开启前按下 Guard 且窗口开启时仍保持 GuardHeld=true，则进入 Guard。
3. GuardCancelWindow 开启前按下 Guard 但窗口开启前已经松开，则不进入 Guard。
```

---

### 15.4 AttackResetWindow

```text
AttackResetWindow:
Start = 0.74s
End   = 0.90s
Duration = 0.16s
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

### 15.5 SkillResetWindow

```text
SkillResetWindow:
Start = 0.76s
End   = 0.90s
Duration = 0.14s
```

响应规则：

```text
if SkillResetWindow == true:
    if Skill1PressedThisFrame:
        if BetaEnergy >= SkillCost:
            EnterSkill()
        else:
            PlaySkillFailFeedback()
```

限制：

```text
1. Skill1Pressed 不进入 Skill 状态本地缓存。
2. SkillResetWindow 开启前的 Skill1Pressed 直接忽略。
3. Skill2Pressed 不进入 Skill。
```

---

### 15.6 ReturnWindow

```text
ReturnWindow:
Start = 0.82s
End   = 0.90s
Duration = 0.08s
```

自然结束规则：

```text
if SkillEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 16. 缓存冲突处理

### 16.1 Evade 缓存 vs GuardHeld

```text
优先级：
EvadeInput > GuardHeld
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput:
    EnterEvade()
else if GuardCancelWindow == true
and GuardHeld == true:
    EnterGuard()
```

---

### 16.2 Evade 缓存 vs Skill1Pressed

```text
优先级：
EvadeInput > SkillInput
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput:
    EnterEvade()
else if SkillResetWindow == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
```

---

### 16.3 Evade 缓存 vs Attack 输入

```text
优先级：
EvadeInput > LightAttack / HeavyAttack
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput:
    EnterEvade()
else if AttackResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
```

---

### 16.4 Skill1Pressed vs Attack 输入

```text
优先级：
SkillInput > LightAttack / HeavyAttack
```

处理：

```text
if SkillResetWindow == true
and Skill1PressedThisFrame
and BetaEnergy >= SkillCost:
    EnterSkill()
else if AttackResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
```

---

### 16.5 GuardHeld vs Attack 输入

```text
优先级：
LightAttack / HeavyAttack > GuardHeld
```

处理：

```text
if AttackResetWindow == true
and AttackPressedThisFrame:
    EnterAttack()
else if GuardCancelWindow == true
and GuardHeld == true:
    EnterGuard()
```

---

### 16.6 动作取消 vs 移动取消

```text
优先级：
Evade > Skill > Attack > Guard > Move

只有更高优先级动作均未成立，且 Cancel_MovementReturn=true、MoveMagnitude>0.1 时，才进入 Locomotion。
MoveInput 不写入 Skill 状态本地缓存。
```

---

### 16.7 缓存过期

```text
if SkillEnd:
    ClearEvadeBuffer()
```

Skill 缓存只在 Skill 状态内有效，不带入 Idle / Locomotion / Evade / Guard / Attack。

---

## 17. 状态退出条件

| 输入 / 事件                      | 条件                                              | 下一个状态                   |
| ---------------------------- | ----------------------------------------------- | ----------------------- |
| HP <= 0                      | 任意时刻                                            | Dead                    |
| EnemyHit                     | SkillSuperArmorWindow=false                     | HitReaction / Knockdown |
| CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown | 任意 Skill 阶段                                     | Knockdown               |
| EvadePressed                 | EvadeCancelWindow=true                          | Evade                   |
| GuardHeld                    | GuardCancelWindow=true 且 CanGuard=true          | Guard                   |
| LightAttackPressed           | AttackResetWindow=true 且 CanAttack=true         | Attack                  |
| HeavyAttackPressed           | AttackResetWindow=true 且 CanAttack=true         | Attack                  |
| Skill1Pressed                | SkillResetWindow=true 且 BetaEnergy >= SkillCost | Skill                   |
| MoveInput                    | Cancel_MovementReturn=true 且 MoveMagnitude > 0.1 | Locomotion              |
| SkillEnd                     | MoveMagnitude > 0.1                             | Locomotion              |
| SkillEnd                     | MoveMagnitude <= 0.1                            | Idle                    |

状态机汇总中 Skill 的退出规则为：Light / HeavyAttack 在 ResetWindow 转 Attack，GuardHeld 在 GuardCancelWindow 转 Guard，EvadePressed 在 EvadeCancelWindow 转 Evade，Skill1Pressed 在 SkillCancelWindow 且能量足够时转 Skill；`Cancel_MovementReturn` 内有实时移动输入时转 Locomotion，窗口外不触发且不缓存。SkillEnd 后仍根据 MoveMagnitude 回 Locomotion 或 Idle。

---

## 18. 动画需求

### 18.1 技能动画

|场景|动画资源|
|---|---|
|Skill1|P_Eve_Sword_SlotNormal_ChainStab|

动画资源中 Skill 对应动画为 `P_Eve_Sword_SlotNormal_ChainStab`，说明为技能1。

---

### 18.2 技能代码动作位移

Skill1 动画没有可用 Root Motion，三段起势分别通过 Timeline 的 Player Motion 窗口请求 `PlayerActionMotionProfile.Skill1Forward`：

1. Motion 窗口为 `0.50～0.80s / 0.90～1.18s / 1.65～1.98s`；`PlayerSkillState` 只在每个窗口首次激活时请求一次，同一窗口不逐帧重启。
2. 配置为 `Duration=0.28s / Distance=1.85m / StopDistance=1.15m / StopOnSideCollision=true`，累计距离曲线为 `(0,0) → (0.35,0.60) → (0.75,0.92) → (1,1)`。
3. 只使用当前 Lock-on 目标；窗口开始时没有锁定目标、水平距离大于 `3m` 或角度大于 `60°` 时拒绝吸附，不搜索或切换目标。
4. 窗口运行中持续按锁定目标当前位置修正水平朝向和径向距离；达到 `1.15m` 后保持技能转向但停止前移，目标丢失或越界则立即停止本段吸附。
5. 实际位移仍由 `PlayerMovementMotor -> PlayerActionMotionRuntime -> CharacterController.Move()` 执行，并经过 Boss Separation 与侧碰裁剪，不瞬移、不穿墙。
6. 无锁定目标时技能仍按原朝向播放，但三个锁定目标 Motion 请求均安全失败；技能被 Evade / Guard / Attack / Reaction / Dead 抢占时 Motor 清除剩余动作位移。
7. Skill 不使用 `OnAnimatorMove()` 产生位移，也不直接修改 `Transform.position`。

---

## 19. 动画参数

| 参数名                   | 类型         | 用途                                |
| --------------------- | ---------- | --------------------------------- |
| PlayerState           | Enum / Int | 当前顶层状态为 Skill                     |
| SkillPhase            | Enum / Int | Start / Active / Recovery / Reset |
| SkillType             | Enum / Int | 当前技能类型                            |
| SkillCastId           | Int        | 当前技能释放编号                          |
| SkillDirection        | Vector3    | 技能释放方向                            |
| SkillMoveX            | Float      | 进入技能时锁定的横向输入                      |
| SkillMoveY            | Float      | 进入技能时锁定的纵向输入                      |
| MoveMagnitude         | Float      | 判断 SkillEnd 后去向                   |
| IsInCombat            | Bool       | 是否战斗模式                            |
| IsLockOn              | Bool       | 是否锁定 Boss                         |
| IsSkillHitboxActive   | Bool       | 技能攻击判定是否开启                        |
| IsSkillSuperArmor     | Bool       | 是否处于技能霸体窗口                        |
| HasBufferedEvadeInput | Bool       | 是否有 Evade 缓存                      |
| BetaEnergy            | Float      | 当前 BetaEnergy                     |
| TriggerSkill1         | Trigger    | 触发 Skill1                         |
| TriggerSkillHit       | Trigger    | 触发技能命中反馈                          |
| TriggerSkillToEvade   | Trigger    | 技能转闪避                             |
| TriggerSkillToGuard   | Trigger    | 技能转防御                             |
| TriggerSkillToAttack  | Trigger    | 技能转攻击                             |
| TriggerSkillToMove    | Trigger    | 技能转移动                             |

---

## 20. 配置事件

| 事件 | 时间 | 作用 |
| --- | ---: | --- |
| SkillStart / ConsumeSkillEnergy / SkillSuperArmorStart | 0.000s | 进入 Skill、消费 8 BE、开启霸体 |
| SkillActiveStart / Motion1Start | 0.500s | 进入 Active，尝试第一段锁定吸附 |
| Motion1End | 0.800s | 结束第一段吸附窗 |
| Skill1Hit1Start / End | 0.833s / 1.000s | 第一段 HitReaction 判定 |
| Motion2Start / End | 0.900s / 1.180s | 第二段锁定吸附窗 |
| Skill1Hit2Start / End | 1.200s / 1.367s | 第二段 HitReaction 判定 |
| Motion3Start / End | 1.650s / 1.980s | 第三段锁定吸附窗 |
| Skill1Hit3Start / End | 2.000s / 2.333s | 第三段 Knockdown 判定 |
| SkillEffect Segment1/2/3 | 0.500s / 0.900s / 1.650s | 分别清空并重播同一套 SkillEffect 主粒子 |
| hitEffect End1/2/3 | 1.000s / 1.367s / 2.333s | 对应段未命中保持关闭，命中过则清空并重新关闭 |
| SkillRecoveryStart / EvadeBufferStart | 2.583s | 进入 Recovery，开放 Evade Buffer |
| SkillSuperArmorEnd | 3.000s | 关闭技能霸体 |
| SkillReset / EvadeCancel / SkillCancel | 3.083s | 进入 Reset，开放 Evade/Skill Cancel |
| GuardCancelStart | 3.417s | 开放 Guard Cancel |
| AttackResetStart | 3.500s | 开放 Attack Reset |
| SkillEnd | 6.000s | 结束 Skill |

---

## 21. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|SkillCost|8|
|SkillTotalDuration|6.00s|
|SkillStartDuration|0.50s|
|SkillActiveDuration|2.083s|
|SkillRecoveryDuration|0.50s|
|SkillSuperArmorStartTime|0.00s|
|SkillSuperArmorEndTime|3.00s|
|SkillHitboxActive|任意 Skill1 HitNode 激活|
|EvadeBuffer|2.583s - 3.083s|
|EvadeCancel / SkillCancel|3.083s - 6.000s|
|GuardCancel|3.417s - 6.000s|
|AttackReset|3.500s - 6.000s|
|Skill1_Hit1|0.833s - 1.000s / Damage 60 / PoiseDamage 12 / HitReaction|
|Skill1_Hit2|1.200s - 1.367s / Damage 60 / PoiseDamage 12 / HitReaction|
|Skill1_Hit3|2.000s - 2.333s / Damage 80 / PoiseDamage 18 / Knockdown|
|Skill1Forward Windows|0.500s - 0.800s / 0.900s - 1.180s / 1.650s - 1.980s|
|Skill1Forward Target|LockOnTarget / Max 3m / Max 60° / Stop 1.15m|
|Skill1Forward Motion|0.28s / Max 1.85m / StopOnSideCollision|
|SkillKnockback|1.2m|
|SkillHitStopDuration|0.06s|
|SkillRotateSpeed|1440°/s|
|SkillFaceTargetAngleLimit|3°|
|SkillToAttackBlendTime|0.05s|
|SkillToEvadeBlendTime|0.05s|
|SkillToGuardBlendTime|0.06s|
|SkillToMoveBlendTime|0.10s|
|SkillToIdleBlendTime|0.10s|

---

## 22. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|Skill1Pressed|BetaEnergy < SkillCost|不进入 Skill，播放能量不足反馈|
|Skill1Pressed|当前状态无 SkillCancelWindow / SkillResetWindow|不进入 Skill，不缓存|
|Skill1Pressed|Skill 状态中但 SkillResetWindow 未开启|忽略，不缓存|
|Skill2Pressed|任意状态|不进入 Skill，播放技能不可用反馈|
|EvadePressed|不在 EvadeBufferWindow 且不在 EvadeCancelWindow|忽略，不缓存|
|EvadeReleased|Skill 状态中存在 Evade 缓存|清除 Evade 缓存|
|GuardHeld|GuardCancelWindow 未开启|保持 Skill，不缓存|
|LightAttackPressed|AttackResetWindow 未开启|忽略，不缓存|
|HeavyAttackPressed|AttackResetWindow 未开启|忽略，不缓存|
|RecoverHpPressed|任意 Skill 阶段|不响应|
|LockOnPressed|任意 Skill 阶段|不响应|
|CombatReactionIntent.Knockdown|任意 Skill 阶段|进入 Knockdown|
|HP <= 0|任意 Skill 阶段|进入 Dead|

---

## 23. 状态流转

```text
Idle / Locomotion
+ Skill1Pressed
+ BetaEnergy >= SkillCost
→ SkillStart
→ SkillActive
→ SkillRecovery
→ SkillResetWindow

Attack
+ Skill1Pressed
+ CommitCancelWindow / SkillCancelWindow
+ BetaEnergy >= SkillCost
→ SkillStart

Evade / Guard
+ Skill1Pressed
+ SkillCancelWindow
+ BetaEnergy >= SkillCost
→ SkillStart

SkillStart
+ Consume BetaEnergy
+ Lock SkillDirection
→ SkillActive

SkillActive
+ SkillHitboxActive
+ Boss Hurtbox overlap
→ SkillHit

Skill
+ EnemyHit
+ SkillSuperArmorWindow=true
+ 普通攻击
→ 受到伤害但不打断 Skill

Skill
+ EnemyHit
+ SkillSuperArmorWindow=false
→ HitReaction / Knockdown

Skill
+ CombatReactionIntent.KnockdownReaction / CombatReactionIntent.Knockdown
→ Knockdown

SkillRecovery / SkillResetWindow
+ EvadeInput in EvadeBufferWindow
→ 缓存
→ EvadeCancelWindow 消费
→ Evade

SkillRecovery / SkillResetWindow
+ GuardHeld
+ GuardCancelWindow=true
→ Guard

SkillResetWindow
+ LightAttackPressed / HeavyAttackPressed
+ AttackResetWindow=true
→ Attack

SkillResetWindow
+ Skill1Pressed
+ BetaEnergy >= SkillCost
→ SkillStart

SkillResetWindow
+ Cancel_MovementReturn=true
+ MoveMagnitude > 0.1
→ Locomotion

SkillEnd
+ MoveMagnitude > 0.1
→ Locomotion

SkillEnd
+ MoveMagnitude <= 0.1
→ Idle
```
## 2026-06-04 第 7 阶段机制可测版补充

- Skill1 进入状态时消耗 `8` 点 BetaEnergy；资源不足时不应进入 Skill。
- Skill1 判定窗口为 `0.24s - 0.48s`，由玩家武器 `PlayerWeaponHitbox` 在 `IsSkillHitboxActive` 为 true 时产生 `CombatAttackType.SkillAttack`。
- Skill1 霸体窗口为 `0.00s - 0.76s`，即进入 Skill 后到 Reset 阶段前一直霸体。
- Skill 霸体不是无敌：霸体期间受到 HitReaction / Knockdown / DamageOnly 时仍扣 HP，但不会进入 HitReaction / Knockdown。
- Dead 仍是最高优先级：HP 归零时即使处于 Skill 霸体窗口，也必须进入 Dead。
- Skill Reset 阶段从 `0.76s` 开始，允许按窗口响应 Evade、Skill、Attack、Guard 派生或自然返回 Idle / Locomotion。
