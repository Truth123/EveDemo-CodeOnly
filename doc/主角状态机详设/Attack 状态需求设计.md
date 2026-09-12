## 1. 状态定位

`Attack` 是玩家角色的普通攻击状态，用于处理轻攻击、重攻击、轻重攻击组合连招、攻击判定、攻击命中、攻击后摇、连招派生、攻击取消、攻击结束后返回 Idle / Locomotion 等逻辑。

`Attack` 负责：

```text
1. 处理 LightAttackPressed / HeavyAttackPressed 进入普通攻击。
2. 管理轻攻击与重攻击的 ComboNode。
3. 管理 InputCommitWindow / Startup / Active / Recovery / ResetWindow。
4. 管理攻击 Hitbox 开启与关闭。
5. 管理攻击命中 Boss 后的伤害、削韧、命中反馈。
6. 管理 LightAttack / HeavyAttack 连招缓存。
7. 管理 EvadeInput / SkillInput 攻击取消缓存。
8. 管理 GuardHeld 在 CommitCancelWindow / GuardCancelWindow 内即时进入 Guard。
9. 管理 AttackEnd 后返回 Idle / Locomotion。
10. 管理攻击中被敌人命中后进入 HitReaction / Knockdown。

### 需求变更：攻击缓存与取消窗口规则

本版窗口规则调整如下：

1. `Active` 阶段开始时开启所有缓存接收窗口，包括 `AttackBufferWindow`、`EvadeBufferWindow` 和 `SkillBufferWindow`，这些缓存窗口持续到 `Active` 阶段结束。
2. `ComboWindow` 只在 `Recovery` 阶段开启，一定时间后关闭。窗口内既可以消费攻击缓存，也可以直接接收当帧 `LightAttackPressed / HeavyAttackPressed` 进入下一连段；窗口外不能继续当前连段。
3. `ComboWindow` 关闭后，如果当前已经进入 `ResetWindow`，则 `LightAttackPressed / HeavyAttackPressed` 会重新进入对应起手攻击，而不是等待回到 `Idle / Locomotion` 后再起手。
4. `EvadeCancelWindow` 和 `SkillCancelWindow` 在 `Recovery` 阶段开启，并覆盖 `ResetWindow`。窗口内既可以消费缓存，也可以直接响应即时按键进入 `Evade / Skill`。
5. `GuardCancelWindow` 在 `Recovery` 阶段开启，并覆盖 `ResetWindow`。窗口内直接读取 `GuardHeld` 进入 `Guard`。
```

`Attack` 不负责：

```text
1. BetaAttack 实现。
2. GuardCounter。
3. Execution。
4. Airborne 攻击。
5. PerfectGuard 判定。
6. PerfectEvade 判定。
7. 回血。
8. LockOn 切换。
```

---

## 2. 状态分层

```text
Attack
└── ComboSystem
    ├── InputCommitWindow
    ├── Startup
    ├── Active
    ├── Recovery
    └── ResetWindow
```

---

## 3. 进入条件

|来源状态|条件|目标状态|说明|
|---|---|---|---|
|Idle|LightAttackPressed 且 CanAttack=true|Attack|待机轻攻击|
|Idle|HeavyAttackPressed 且 CanAttack=true|Attack|待机重攻击|
|Locomotion|LightAttackPressed 且 CanAttack=true|Attack|移动轻攻击|
|Locomotion|HeavyAttackPressed 且 CanAttack=true|Attack|移动重攻击|
|Evade|LightAttackPressed 且 ResetWindow=true|Attack|闪避结束转轻攻击|
|Evade|HeavyAttackPressed 且 ResetWindow=true|Attack|闪避结束转重攻击|
|Guard|LightAttackPressed 且 ResetWindow=true|Attack|防御结束转轻攻击|
|Guard|HeavyAttackPressed 且 ResetWindow=true|Attack|防御结束转重攻击|
|Skill|LightAttackPressed 且 ResetWindow=true|Attack|技能结束转轻攻击|
|Skill|HeavyAttackPressed 且 ResetWindow=true|Attack|技能结束转重攻击|
|HitReaction|LightAttackPressed 且 ResetWindow=true|Attack|受击恢复转轻攻击|
|HitReaction|HeavyAttackPressed 且 ResetWindow=true|Attack|受击恢复转重攻击|
|Knockdown|LightAttackPressed 且 ResetWindow=true|Attack|起身恢复转轻攻击|
|Knockdown|HeavyAttackPressed 且 ResetWindow=true|Attack|起身恢复转重攻击|
|Attack|LightAttackPressed 且 ComboWindow=true 且当前节点支持 Light|Attack|进入下一 ComboNode|
|Attack|HeavyAttackPressed 且 ComboWindow=true 且当前节点支持 Heavy|Attack|进入下一 ComboNode|
|Attack|LightAttackPressed 且 ComboWindow=false 且 ResetWindow=true|Attack|重新进入轻攻击起手|
|Attack|HeavyAttackPressed 且 ComboWindow=false 且 ResetWindow=true|Attack|重新进入重攻击起手|

---

## 4. Attack 输入规则

```text
LightAttackPressed：
- 当前不在 Attack 状态时，进入轻攻击起手。
- 当前处于 Attack 状态时，在 ComboWindow 内被消费。
- 当前 ComboNode 支持 Light 派生时，进入下一 ComboNode。
- 当前 ComboNode 不支持 Light 派生时，不进入下一段。
- 当前处于 Attack 状态且 ComboWindow 已关闭、ResetWindow 已开启时，重新进入轻攻击起手。

HeavyAttackPressed：
- 当前不在 Attack 状态时，进入重攻击起手。
- 当前处于 Attack 状态时，在 ComboWindow 内被消费。
- 当前 ComboNode 支持 Heavy 派生时，进入下一 ComboNode。
- 当前 ComboNode 不支持 Heavy 派生时，不进入下一段。
- 当前处于 Attack 状态且 ComboWindow 已关闭、ResetWindow 已开启时，重新进入重攻击起手。

ResetWindow 内重新进入起手节点时，每次都会生成新的 `CurrentAttackInstanceId`。如果前后都是同一个起手节点，即 `L1 -> L1` 或 `H1 -> H1`，Gameplay 状态机仍只负责重启攻击节点；动画层由 `PlayerAnimationBridge` 检测新的实例 ID 后对 `Anim_Attack_L1 / Anim_Attack_H1` 从 0 执行极短 `CrossFade`，确保同一动画可重播。其他攻击切换，例如 `L1 -> L2`、`L1 -> H1`、`H1 -> H2`，继续由 `Eve.controller` 现有 Animator 连线处理。

LightAttackHeld：
- 记录 Hold 状态。
- 预留 BetaAttack。
- 当前 Attack 状态不进入 BetaAttack。

HeavyAttackHeld：
- 记录 Hold 状态。
- 预留 BetaAttack / 蓄力攻击。
- 当前 Attack 状态不进入 BetaAttack。
```

---

## 5. ComboSystem

`ComboSystem` 根据当前 `ComboNode` 和输入类型决定下一段攻击。

```text
ComboSystem 输入：
1. CurrentComboNode
2. AttackInputType
3. ComboWindow
4. HitResult
5. OnHit / OnWhiff
6. BufferedAttackInput
```

```text
ComboSystem 输出：
1. NextComboNode
2. AttackAnimation
3. AttackData
4. ComboIndex
5. AttackPhase
```

---

## 6. ComboNode 数据结构

```text
ComboNode
{
    NodeId
    AttackType
    AnimationName
    Damage
    PoiseDamage
    HitboxStartTime
    HitboxEndTime
    AttackTotalDuration
    InputCommitStartTime
    InputCommitEndTime
    StartupStartTime
    StartupEndTime
    ActiveStartTime
    ActiveEndTime
    RecoveryStartTime
    RecoveryEndTime
    ResetWindowStartTime
    ResetWindowEndTime
    ComboBufferStartTime
    ComboBufferEndTime
    ComboWindowStartTime
    ComboWindowEndTime
    EvadeBufferStartTime
    EvadeBufferEndTime
    EvadeCancelStartTime
    EvadeCancelEndTime
    SkillBufferStartTime
    SkillBufferEndTime
    SkillCancelStartTime
    SkillCancelEndTime
    GuardCancelStartTime
    GuardCancelEndTime
    SupportsLightNext
    SupportsHeavyNext
    NextLightNodeId
    NextHeavyNodeId
    CanBeCanceledByEvade
    CanBeCanceledBySkill
    CanBeCanceledByGuard
}
```

---

## 7. 连招节点

### 7.1 轻攻击链

|ComboNode|输入|动画资源|说明|
|---|---|---|---|
|L1|LightAttackPressed|Proto_Sword_Lightattack_01_Root|轻攻击1|
|L2|LightAttackPressed|Proto_Sword_Lightattack_02_Root|轻攻击2|
|L3|LightAttackPressed|Proto_Sword_Lightattack_03_Root|轻攻击3|
|L4|LightAttackPressed|Proto_Sword_Lightattack_04_Root|轻攻击4|

轻攻击链规则：

```text
L1 + LightAttackPressed in ComboWindow → L2
L2 + LightAttackPressed in ComboWindow → L3
L3 + LightAttackPressed in ComboWindow → L4
L4 + LightAttackPressed in ComboWindow → 不进入下一段
```

### 7.2 重攻击链

|ComboNode|输入|动画资源|说明|
|---|---|---|---|
|H1|HeavyAttackPressed|Proto_Sword_Strongattack_01_Root|重攻击1|
|H2|HeavyAttackPressed|Proto_Sword_Strongattack_02_Root|重攻击2|

重攻击链规则：

```text
H1 + HeavyAttackPressed in ComboWindow → H2
H2 + HeavyAttackPressed in ComboWindow → 不进入下一段
```

### 7.3 轻重组合派生

```text
L1 + HeavyAttackPressed in ComboWindow → H1
L2 + HeavyAttackPressed in ComboWindow → H1
L3 + HeavyAttackPressed in ComboWindow → H2
H1 + LightAttackPressed in ComboWindow → L2
H2 + LightAttackPressed in ComboWindow → L3
```

---

## 8. AttackPhase

每个 `ComboNode` 都包含以下阶段：

```text
1. InputCommitWindow
2. Startup
3. Active
4. Recovery
5. ResetWindow
```

---

## 9. InputCommitWindow

`InputCommitWindow` 是招式起手可取消区间。

```text
作用：
1. 播放攻击起手前段。
2. 允许 EvadePressed 立即取消进入 Evade。
3. 允许 Skill1Pressed 且 BetaEnergy 足够时立即取消进入 Skill。
4. 允许 GuardHeld=true 时立即取消进入 Guard。
5. 不开启攻击 Hitbox。
6. 不消费连招输入。
```

时间：

```text
InputCommitWindow = 0.00s - 0.10s
```

规则：

```text
1. EvadePressed 在 InputCommitWindow 内即时进入 Evade。
2. Skill1Pressed 在 InputCommitWindow 内且 BetaEnergy 足够时即时进入 Skill。
3. GuardHeld 在 InputCommitWindow 内且 CanGuard=true 时即时进入 Guard。
4. LightAttackPressed / HeavyAttackPressed 在 InputCommitWindow 内不触发下一段连招。
5. InputCommitWindow 结束后进入 Startup。
```

---

## 10. Startup

`Startup` 是攻击前摇锁定阶段。

```text
作用：
1. 播放攻击前摇。
2. 锁定当前攻击动作。
3. 不允许普通输入取消。
4. 不开启攻击 Hitbox，或只在末尾准备开启 Hitbox。
5. 允许读取 MoveInput 作为方向微调参考。
```

时间：

```text
Startup = 0.10s - 0.24s
```

规则：

```text
1. Startup 中不响应 LightAttackPressed / HeavyAttackPressed。
2. Startup 中不响应 EvadePressed。
3. Startup 中不响应 GuardHeld。
4. Startup 中不响应 Skill1Pressed。
5. Startup 中不响应 RecoverHpPressed。
6. Startup 中 LockOnPressed 不响应。
7. Startup 中被敌人普通攻击命中，进入 HitReaction。
8. Startup 中被敌人强攻击命中，进入 Knockdown。
```

---

## 11. Active

`Active` 是攻击判定阶段。

```text
作用：
1. 开启 AttackHitbox。
2. 检测 Boss Hurtbox。
3. 造成攻击伤害。
4. 造成削韧。
5. 记录 OnHit / OnWhiff。
6. 播放命中特效、音效、HitStop。
```

时间：

```text
Active = 0.24s - 0.38s
```

攻击判定：

```text
AttackHitboxStart = 0.24s
AttackHitboxEnd   = 0.38s
```

命中规则：

```text
1. AttackHitbox overlap Boss Hurtbox 时，记录 AttackHit。
2. 同一 AttackId 对同一 BossHitPart 只命中一次。
3. AttackHit 成功后造成 Damage。
4. AttackHit 成功后造成 PoiseDamage。
5. AttackHit 成功后设置 HitResult=OnHit。
6. 攻击判定结束仍未命中，设置 HitResult=OnWhiff。
```

---

## 12. Recovery

`Recovery` 是攻击后摇阶段。

```text
作用：
1. 关闭 AttackHitbox。
2. 播放攻击后摇。
3. 根据当前 ComboNode 开启 BufferWindow。
4. 根据当前 ComboNode 开启 ComboWindow / EvadeCancelWindow / SkillCancelWindow / GuardCancelWindow。
5. 根据 OnHit / OnWhiff 调整允许窗口。
6. 后段进入 ResetWindow。
```

时间：

```text
Recovery = 0.38s - 0.78s
```

---

## 13. ResetWindow

`ResetWindow` 是攻击后摇末段的重置区间。

```text
作用：
1. 允许自然回 Idle / Locomotion。
2. 允许 Attack 结束后转下一合法状态。
3. 允许已缓存输入在对应允许窗口内消费。
4. 作为默认移动取消窗口起点；更高优先级派生未触发且存在移动输入时，可提前回 `Locomotion`。
```

时间：

```text
ResetWindow = 0.66s - 0.78s
```

自然结束规则：

```text
if AttackEnd:
    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 14. 攻击方向规则

进入 `Attack` 的第一帧锁定本次攻击方向：

```text
AttackDirection = ResolveAttackDirection(
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

### 14.1 自由视角

```text
if IsLockOn == false:
    if MoveMagnitude > MoveThreshold:
        AttackDirection = CameraRelativeMoveDirection
    else:
        AttackDirection = CharacterForward
```

角色朝 `AttackDirection` 快速转向。

### 14.2 锁定视角

```text
if IsLockOn == true and first enter Attack:
    AttackDirection = DirectionToLockOnTarget

if IsLockOn == true and switch attack node inside Attack:
    DesiredTurnAngle = SignedAngle(CharacterForward, DirectionToLockOnTarget)
    AppliedTurnAngle = Clamp(DesiredTurnAngle, -LockOnComboMaxTurnAngle, LockOnComboMaxTurnAngle)
    AttackDirection = Rotate(CharacterForward, AppliedTurnAngle)
```

锁定视角下，首次从 Idle / Locomotion / Evade / Guard / Skill 进入 Attack 时仍允许起手朝向 Boss，保证起手攻击可用。

Attack 内部连段切换和 ResetWindow 重启起手时，不再强制完全朝向 Boss。如果朝向 Boss 需要超过最大转角，则只应用最大转角并锁存为本攻击节点方向，避免连招中发生大角度自动吸附。

转向参数：

```text
AttackRotateSpeed = 1440°/s
AttackFaceTargetAngleLimit = 3°
LockOnComboMaxTurnAngle = 60°
```

---

## 15. 攻击命中规则

```text
AttackHit
{
    AttackId
    ComboNodeId
    AttackerId
    TargetId
    HitPartId
    Damage
    PoiseDamage
    HitPosition
    HitDirection
    HitStopDuration
}
```

处理规则：

```text
1. AttackHitbox 开启期间检测 Boss Hurtbox。
2. 命中成功时生成 AttackHit。
3. 每个 AttackId 对同一 HitPartId 只生效一次。
4. 命中后触发 Boss 受击逻辑。
5. 命中后播放命中特效。
6. 命中后播放命中音效。
7. 命中后触发 HitStop。
8. 命中后设置 CurrentComboNode.HitResult=OnHit。
```

---

## 16. OnHit / OnWhiff 窗口规则

攻击允许区间根据命中情况变化。

### 16.1 OnHit

```text
OnHit：
1. ComboWindow 提前开启。
2. EvadeCancelWindow 提前开启。
3. SkillCancelWindow 提前开启。
4. GuardCancelWindow 正常开启。
5. Recovery 缩短。
```

### 16.2 OnWhiff

```text
OnWhiff：
1. ComboWindow 延后开启。
2. EvadeCancelWindow 延后开启。
3. SkillCancelWindow 延后开启。
4. GuardCancelWindow 延后开启。
5. Recovery 保持完整。
```

---

## 17. Attack 输入响应规则

|输入|响应方式|是否缓存|
|---|---|--:|
|MoveInput|读取方向，不直接移动|否|
|LookInput|持续响应|否|
|LockOnPressed|不响应|否|
|LightAttackPressed|Active 内缓存，ComboWindow 内消费；ComboWindow 关闭后的 ResetWindow 内重新起手|是|
|HeavyAttackPressed|Active 内缓存，ComboWindow 内消费；ComboWindow 关闭后的 ResetWindow 内重新起手|是|
|EvadePressed|Active 内缓存，InputCommitWindow / EvadeCancelWindow 内消费；EvadeCancelWindow 内也可即时响应|是|
|EvadeHeld|读取，不触发 Sprint|否|
|EvadeReleased|清除 Evade 缓存|否|
|GuardHeld|InputCommitWindow / GuardCancelWindow 内即时响应，GuardCancelWindow 覆盖 ResetWindow|否|
|Skill1Pressed|Active 内缓存，InputCommitWindow / SkillCancelWindow 内消费；SkillCancelWindow 内也可即时响应|是|
|Skill2Pressed|不响应|否|
|RecoverHpPressed|不响应|否|

---

## 18. Attack 时间轴与窗口

以标准轻攻击节点 `AttackTotalDuration = 0.78s` 为基准：

```text
0.00s ───────────────────────────────────── 0.78s

0.00 - 0.10  InputCommitWindow
0.10 - 0.24  Startup
0.24 - 0.38  Active
0.38 - 0.78  Recovery
0.66 - 0.78  ResetWindow

0.24 - 0.38  AttackBufferWindow
0.38 - 0.62  ComboWindow
0.24 - 0.38  EvadeBufferWindow
0.00 - 0.10  EvadeCommitCancelWindow
0.38 - 0.78  EvadeCancelWindow
0.24 - 0.38  SkillBufferWindow
0.00 - 0.10  SkillCommitCancelWindow
0.38 - 0.78  SkillCancelWindow
0.00 - 0.10  GuardCommitCancelWindow
0.38 - 0.78  GuardCancelWindow
0.66 - 0.78  ReturnWindow
0.66 - 0.78  MoveCancelWindow
```

---

## 19. 输入缓存规则

Attack 状态缓存：

```text
1. LightAttackPressed
2. HeavyAttackPressed
3. EvadePressed
4. Skill1Pressed
```

Attack 状态不缓存：

```text
1. GuardInput
2. RecoverInput
3. LockOnPressed
4. Skill2Pressed
5. MoveInput
6. LookInput
```

缓存槽：

```text
Attack 槽：
- LightAttackPressed
- HeavyAttackPressed

Evade 槽：
- EvadePressed

Skill 槽：
- Skill1Pressed
```

默认规则：

```text
1. 同槽后输入覆盖前输入。
2. 跨槽保留。
3. 每帧最多消费一个主动作。
4. 消费后清除自身缓存槽和冲突缓存槽。
5. AttackEnd 时清除 Attack 状态本地缓存。
6. 进入 HitReaction / Knockdown / Dead 时清除所有缓存。
```

---

## 20. AttackBufferWindow

```text
AttackBufferWindow:
Start = ActiveStartTime
End   = ActiveEndTime
Duration = ActiveDuration
```

缓存规则：

```text
if AttackBufferWindow == true:
    if LightAttackPressed:
        BufferAttackInput(Light)

    if HeavyAttackPressed:
        BufferAttackInput(Heavy)
```

缓存有效期：

```text
AttackBufferExpireTime = min(BufferedTime + 0.30s, AttackEndTime)
```

消费规则：

```text
if ComboWindow == true:
    if HasBufferedAttackInput:
        if CurrentComboNode supports BufferedAttackType:
            ConsumeAttackBuffer()
            EnterNextComboNode()
        else:
            ClearAttackBuffer()
```

---

## 21. ComboWindow

```text
ComboWindow:
Start = RecoveryStartTime
End   = 0.62s
Duration = 0.24s
```

响应规则：

```text
if ComboWindow == true:
    if HasBufferedAttackInput:
        if BufferedAttackType == Light and CurrentComboNode.SupportsLightNext:
            EnterNextComboNode(CurrentComboNode.NextLightNodeId)

        if BufferedAttackType == Heavy and CurrentComboNode.SupportsHeavyNext:
            EnterNextComboNode(CurrentComboNode.NextHeavyNodeId)

    else if LightAttackPressedThisFrame and CurrentComboNode.SupportsLightNext:
        EnterNextComboNode(CurrentComboNode.NextLightNodeId)

    else if HeavyAttackPressedThisFrame and CurrentComboNode.SupportsHeavyNext:
        EnterNextComboNode(CurrentComboNode.NextHeavyNodeId)
```

限制：

```text
1. ComboWindow 开启前的 LightAttackPressed / HeavyAttackPressed 只在 AttackBufferWindow 内缓存。
2. ComboWindow 开启前且不在 AttackBufferWindow 内的攻击输入直接忽略。
3. ComboWindow 内攻击输入当帧可直接消费。
4. 当前 ComboNode 不支持对应派生时，清除 Attack 缓存。
```

---

## 22. EvadeBufferWindow

```text
EvadeBufferWindow:
Start = ActiveStartTime
End   = ActiveEndTime
Duration = ActiveDuration
```

缓存规则：

```text
if EvadeBufferWindow == true
and EvadePressed:
    BufferEvadeInput()
```

缓存有效期：

```text
EvadeBufferExpireTime = min(BufferedTime + 0.25s, AttackEndTime)
```

---

## 23. EvadeCancelWindow

```text
EvadeCommitCancelWindow:
Start = 0.00s
End   = 0.10s

EvadeCancelWindow:
Start = RecoveryStartTime
End   = AttackEndTime
Duration = RecoveryDuration
```

消费规则：

```text
if EvadeCommitCancelWindow == true:
    if EvadePressedThisFrame and CanEvade:
        EnterEvade()

if EvadeCancelWindow == true:
    if HasBufferedEvadeInput and CanEvade:
        ConsumeEvadeBuffer()
        EnterEvade()
    else if EvadePressedThisFrame and CanEvade:
        EnterEvade()
```

---

## 24. SkillBufferWindow

```text
SkillBufferWindow:
Start = ActiveStartTime
End   = ActiveEndTime
Duration = ActiveDuration
```

缓存规则：

```text
if SkillBufferWindow == true
and Skill1Pressed:
    BufferSkillInput()
```

缓存有效期：

```text
SkillBufferExpireTime = min(BufferedTime + 0.30s, AttackEndTime)
```

---

## 25. SkillCancelWindow

```text
SkillCommitCancelWindow:
Start = 0.00s
End   = 0.10s

SkillCancelWindow:
Start = RecoveryStartTime
End   = AttackEndTime
Duration = RecoveryDuration
```

消费规则：

```text
if SkillCommitCancelWindow == true:
    if Skill1PressedThisFrame and BetaEnergy >= SkillCost:
        EnterSkill()

if SkillCancelWindow == true:
    if HasBufferedSkillInput:
        if BetaEnergy >= SkillCost:
            ConsumeSkillBuffer()
            EnterSkill()
        else:
            ClearSkillBuffer()
            PlaySkillFailFeedback()
    else if Skill1PressedThisFrame:
        if BetaEnergy >= SkillCost:
            EnterSkill()
        else:
            PlaySkillFailFeedback()
```

---

## 26. GuardCancelWindow

```text
GuardCommitCancelWindow:
Start = 0.00s
End   = 0.10s

GuardCancelWindow:
Start = RecoveryStartTime
End   = AttackEndTime
Duration = RecoveryDuration
```

响应规则：

```text
if GuardCommitCancelWindow == true:
    if GuardHeld == true and CanGuard:
        EnterGuard()

if GuardCancelWindow == true:
    if GuardHeld == true and CanGuard:
        EnterGuard()
```

限制：

```text
1. Guard 不进入普通缓存。
2. GuardCancelWindow 开启前按下 Guard，且窗口开启时 GuardHeld=true，则进入 Guard。
3. GuardCancelWindow 开启前按下 Guard，但窗口开启前已经松开，则不进入 Guard。
```

---

## 27. ReturnWindow

```text
ReturnWindow:
Start = 0.66s
End   = 0.78s
Duration = 0.12s
```

移动取消规则：

```text
MoveCancelWindow:
Start = ResetWindowStart
End   = AttackEndTime

if MoveCancelWindow == true
and MoveMagnitude > MoveThreshold
and no higher priority cancel / combo / restart was consumed:
    EnterLocomotion()
```

优先级：

```text
EvadeCancel / SkillCancel / Combo / AttackRestart / GuardCancel > MoveCancel
```

自然结束规则：

```text
if AttackEnd:
    ClearAttackStateLocalBuffer()

    if MoveMagnitude > MoveThreshold:
        EnterLocomotion()
    else:
        EnterIdle()
```

---

## 28. 缓存冲突处理

### 28.1 Evade 缓存 vs Skill 缓存

```text
优先级：
EvadeInput > SkillInput
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput
and CanEvade:
    EnterEvade()
else if SkillCancelWindow == true
and HasBufferedSkillInput
and BetaEnergy >= SkillCost:
    EnterSkill()
```

---

### 28.2 Evade 缓存 vs Attack 缓存

```text
优先级：
EvadeInput > LightAttack / HeavyAttack
```

处理：

```text
if EvadeCancelWindow == true
and HasBufferedEvadeInput
and CanEvade:
    EnterEvade()
else if ComboWindow == true
and HasBufferedAttackInput:
    EnterNextComboNode()
```

---

### 28.3 Skill 缓存 vs Attack 缓存

```text
优先级：
SkillInput > LightAttack / HeavyAttack
```

处理：

```text
if SkillCancelWindow == true
and HasBufferedSkillInput
and BetaEnergy >= SkillCost:
    EnterSkill()
else if ComboWindow == true
and HasBufferedAttackInput:
    EnterNextComboNode()
```

---

### 28.4 Attack 槽内部冲突

```text
1. LightAttackPressed 和 HeavyAttackPressed 属于同一 Attack 槽。
2. 后输入覆盖前输入。
3. LightAttackPressed 后接 HeavyAttackPressed，缓存结果为 Heavy。
4. HeavyAttackPressed 后接 LightAttackPressed，缓存结果为 Light。
```

---

### 28.5 GuardHeld vs 其他输入

```text
优先级：
EvadeInput > SkillInput > Light/HeavyAttack > GuardHeld
```

处理：

```text
if EvadeCancelWindow == true and HasBufferedEvadeInput:
    EnterEvade()
else if SkillCancelWindow == true and HasBufferedSkillInput:
    EnterSkill()
else if ComboWindow == true and HasBufferedAttackInput:
    EnterNextComboNode()
else if GuardCancelWindow == true and GuardHeld == true:
    EnterGuard()
```

---

## 29. OnHit / OnWhiff 窗口配置

当前阶段先统一窗口规则，不再根据 OnHit / OnWhiff 调整窗口开闭时间。真实命中系统接入后可以再扩展命中分支，但不得改变以下基本约束：

```text
AttackBufferWindow / EvadeBufferWindow / SkillBufferWindow:
    ActiveStartTime - ActiveEndTime

ComboWindow:
    RecoveryStartTime - ComboWindowEndTime
    ComboWindowEndTime 必须早于 ResetWindowStartTime

EvadeCancelWindow / SkillCancelWindow / GuardCancelWindow:
    RecoveryStartTime - AttackEndTime
    必须覆盖 ResetWindow

ResetWindow:
    ResetWindowStartTime - AttackEndTime
```

---

## 30. 攻击判定与受击规则

|项目|规则|
|---|---|
|攻击判定|Active 阶段开启|
|Hitbox|第 6 阶段测试版使用武器 Trigger Collider，由 `PlayerWeaponHitbox` 按 Active 窗口启停|
|Hurtbox|第 6 阶段测试版 Boss Hurtbox 固定接在场景对象 `RavenMonster_Mesh`|
|友方命中|无|
|自身无敌|无|
|霸体|无|
|防御判定|无|
|PerfectGuard|不可触发|
|PerfectEvade|不可触发|
|受击|可被敌人命中|
|CombatHitOutcome.HitReaction|进入 HitReaction|
|CombatHitOutcome.Knockdown|进入 Knockdown|
|CombatHitOutcome.DamageOnly|只扣 HP，不打断 Attack|
|HP <= 0|进入 Dead|

### 30.1 第 6 阶段测试版实现规则

```text
1. PlayerAttackState 每次进入新的 ComboNode 时生成 CurrentAttackInstanceId。
2. CurrentAttackInstanceId、CombatAttackType、Damage、PoiseDamage 写入 PlayerStateContext。
3. PlayerWeaponHitbox 只在 IsAttackHitboxActive=true 时启用武器 Trigger Collider。
4. PlayerWeaponHitbox 命中 CombatHurtbox 后生成 CombatHitData。
5. CombatHurtbox 过滤同阵营目标后调用 CombatHitResolver.Resolve()。
6. CombatResourceComponent 根据 CombatHitResult 只扣除 Boss HP；GuardDamage 保留为底层命中数据，不驱动 Boss 独立护盾。
7. 同一 CurrentAttackInstanceId 对同一个 CombatHurtbox 只结算一次。
8. 有效命中后设置 AttackContactResult=OnHit，并记录 LastCombatHitOutcome。
9. `DamageOnly / HitReaction / Knockdown / Dead` 视为普通攻击 BE 有效命中；每个 `CurrentAttackInstanceId` 最多计数一次，每累计两次增加 1 BE 后归零。
10. Active 结束后如果仍未命中，则设置 `AttackContactResult=OnWhiff` 并清除尚未兑换的连续命中次数；连段结束、取消、转 Guard/Evade/Skill 或被打断不清除。
11. Skill 命中、多 Hurtbox 重复接触、PerfectGuard、PerfectEvade 与无敌忽略结果不得增加普通攻击 BE。
```

测试对象：

```text
玩家武器测试对象：CH_W_Sword_01_Mesh
Boss Hurtbox 测试对象：RavenMonster_Mesh
Boss 阵营：CombatTeam.Boss
玩家武器阵营：CombatTeam.Player
```

限制：

```text
1. 当前只实现玩家攻击 Boss，不实现 Boss 攻击玩家。
2. 当前不接 Boss 受击动画、HitStop、VFX、SFX。
3. 如果 CH_W_Sword_01_Mesh 不是实际手部武器节点，后续需要迁移 PlayerWeaponHitbox。
```

---

## 32. 状态退出条件

|输入 / 事件|条件|下一个状态|
|---|---|---|
|HP <= 0|任意时刻|Dead|
|CombatHitOutcome.HitReaction|非无敌、无霸体|HitReaction|
|CombatHitOutcome.Knockdown|任意 Attack 阶段|Knockdown|
|LightAttackPressed|ComboWindow=true 且当前节点支持 Light|Attack|
|HeavyAttackPressed|ComboWindow=true 且当前节点支持 Heavy|Attack|
|EvadePressed|CommitCancelWindow=true 或 EvadeCancelWindow=true|Evade|
|GuardHeld|CommitCancelWindow=true 或 GuardCancelWindow=true|Guard|
|Skill1Pressed|CommitCancelWindow=true 或 SkillCancelWindow=true，且 BetaEnergy 足够|Skill|
|MoveInput|MoveCancelWindow=true，且未触发更高优先级派生|Locomotion|
|AttackEnd|MoveMagnitude > 0.1|Locomotion|
|AttackEnd|MoveMagnitude <= 0.1|Idle|

---

## 33. 动画需求

### 33.1 轻攻击动画

|ComboNode|动画资源|
|---|---|
|L1|Proto_Sword_Lightattack_01_Root|
|L2|Proto_Sword_Lightattack_02_Root|
|L3|Proto_Sword_Lightattack_03_Root|
|L4|Proto_Sword_Lightattack_04_Root|

### 33.2 重攻击动画

|ComboNode|动画资源|
|---|---|
|H1|Proto_Sword_Strongattack_01_Root|
|H2|Proto_Sword_Strongattack_02_Root|

### 33.3 预留攻击动画

|输入|动画资源|说明|
|---|---|---|
|HeavyAttackHeld|Proto_Sword_Thrust_Start / Proto_Sword_Thrust|长按重攻击快速向前瞬移推刺|

---

## 33.4 攻击 Root Motion 位移

Light / Heavy 攻击动画资源带有 Root Motion，攻击位移使用动画自带水平 Root Motion：

1. 玩家 Animator 的 `applyRootMotion` 在场景 / Prefab 中固定开启，不在运行时反复切换。
2. `PlayerRootMotionRelay.OnAnimatorMove()` 读取 `animator.deltaPosition / deltaRotation`，默认忽略垂直位移和 Root Motion rotation。
3. `PlayerMovementMotor.HandleRootMotion(...)` 只在 `PlayerState == Attack` 时消费水平 Root Motion。
4. Root Motion 仍通过 `CharacterController.Move()` 执行，撞墙时由 CharacterController 裁剪，不直接改 `Transform.position`。
5. Root Motion 在进入 `CharacterController.Move()` 前必须经过 `PlayerBossSeparation` 裁剪，不能把 Player 推进 Boss 核心安全距离，也不能通过身体碰撞挤开 Boss。
6. Attack 默认不保留切线滑动，攻击前冲接触 Boss 时裁剪朝 Boss 内部的位移分量，伤害仍只由武器 Hitbox 命中 Boss Hurtbox 产生。
7. Attack 期间普通水平速度清零；取消到 Evade / Skill / Guard / HitReaction / Knockdown / Dead 后，上一段攻击 Root Motion 立即不再被消费。
8. 攻击朝向仍由状态机 / Motor 的锁定逻辑管理，避免 clip root rotation 破坏面向 Boss。

Skill 不使用攻击 Root Motion 规则，Skill 位移见 Skill 状态需求文档。

---

## 34. 动画参数

|参数名|类型|用途|
|---|---|---|
|PlayerState|Enum / Int|当前顶层状态为 Attack|
|AttackPhase|Enum / Int|Commit / Startup / Active / Recovery / Reset|
|CurrentComboNodeId|String / Int|当前连招节点|
|ComboIndex|Int|当前连招段数|
|AttackInputType|Enum|Light / Heavy|
|HitResult|Enum|None / OnHit / OnWhiff|
|AttackDirection|Vector3|本次攻击方向|
|MoveMagnitude|Float|判断 AttackEnd 后去向|
|MoveX|Float|记录横向移动输入|
|MoveY|Float|记录纵向移动输入|
|IsInCombat|Bool|是否战斗模式|
|IsLockOn|Bool|是否锁定 Boss|
|IsAttackHitboxActive|Bool|攻击 Hitbox 是否开启|
|HasBufferedAttackInput|Bool|是否有 Attack 缓存|
|HasBufferedEvadeInput|Bool|是否有 Evade 缓存|
|HasBufferedSkillInput|Bool|是否有 Skill 缓存|
|BufferedAttackType|Enum|Light / Heavy|
|TriggerAttackLight|Trigger|触发轻攻击|
|TriggerAttackHeavy|Trigger|触发重攻击|
|TriggerAttackHit|Trigger|触发攻击命中反馈|
|TriggerAttackToEvade|Trigger|攻击转闪避|
|TriggerAttackToGuard|Trigger|攻击转防御|
|TriggerAttackToSkill|Trigger|攻击转技能|
|TriggerAttackToMove|Trigger|攻击转移动|
|TriggerAttackToIdle|Trigger|攻击转待机|

---

## 35. 配置事件

|事件|时间|作用|
|---|--:|---|
|AttackStart|0.00s|进入 Attack|
|InputCommitWindowStart|0.00s|开启招式起手取消|
|InputCommitWindowEnd|0.10s|关闭招式起手取消|
|StartupStart|0.10s|进入前摇锁定|
|ActiveStart|0.24s|进入攻击生效阶段|
|AttackHitboxStart|0.24s|开启 AttackHitbox|
|AttackHitboxEnd|0.38s|关闭 AttackHitbox|
|RecoveryStart|0.38s|进入攻击后摇|
|AttackBufferWindowStart|0.24s|Active 开始开启攻击缓存|
|AttackBufferWindowEnd|0.38s|Active 结束关闭攻击缓存|
|EvadeBufferWindowStart|0.24s|Active 开始开启闪避缓存|
|EvadeBufferWindowEnd|0.38s|Active 结束关闭闪避缓存|
|SkillBufferWindowStart|0.24s|Active 开始开启技能缓存|
|SkillBufferWindowEnd|0.38s|Active 结束关闭技能缓存|
|ComboWindowStart|0.38s|Recovery 开始开启连招消费|
|ComboWindowEnd|0.62s|关闭连招消费，之后不能继续当前连段|
|EvadeCancelWindowStart|0.38s|Recovery 开始开启闪避取消|
|EvadeCancelWindowEnd|0.78s|覆盖 ResetWindow|
|SkillCancelWindowStart|0.38s|Recovery 开始开启技能取消|
|SkillCancelWindowEnd|0.78s|覆盖 ResetWindow|
|GuardCancelWindowStart|0.38s|Recovery 开始开启防御取消|
|GuardCancelWindowEnd|0.78s|覆盖 ResetWindow|
|ResetWindowStart|0.66s|开启重置区间|
|ReturnWindowStart|0.66s|允许自然回 Idle / Locomotion|
|MoveCancelWindowStart|0.66s|允许后摇末段按移动输入提前回 Locomotion|
|AttackEnd|0.78s|结束当前攻击节点|

---

## 36. 数值参数

|参数名|数值|
|---|--:|
|MoveThreshold|0.1|
|AttackTotalDuration|0.78s|
|InputCommitStartTime|0.00s|
|InputCommitEndTime|0.10s|
|StartupStartTime|0.10s|
|StartupEndTime|0.24s|
|ActiveStartTime|0.24s|
|ActiveEndTime|0.38s|
|RecoveryStartTime|0.38s|
|RecoveryEndTime|0.78s|
|ResetWindowStartTime|0.66s|
|ResetWindowEndTime|0.78s|
|AttackBufferStartTime|0.24s|
|AttackBufferEndTime|0.38s|
|ComboWindowStartTime|0.38s|
|ComboWindowEndTime|0.62s|
|EvadeBufferStartTime|0.24s|
|EvadeBufferEndTime|0.38s|
|EvadeCancelStartTime|0.38s|
|EvadeCancelEndTime|0.78s|
|SkillBufferStartTime|0.24s|
|SkillBufferEndTime|0.38s|
|SkillCancelStartTime|0.38s|
|SkillCancelEndTime|0.78s|
|GuardCancelStartTime|0.38s|
|GuardCancelEndTime|0.78s|
|ReturnWindowStartTime|0.66s|
|MoveCancelWindowStartTime|0.66s|
|AttackRotateSpeed|1440°/s|
|AttackFaceTargetAngleLimit|3°|
|LockOnComboMaxTurnAngle|60°|
|LightAttackDamage|60|
|HeavyAttackDamage|110|
|LightAttackPoiseDamage|10|
|HeavyAttackPoiseDamage|22|
|AttackHitStopDuration|0.04s|
|AttackToEvadeBlendTime|0.05s|
|AttackToGuardBlendTime|0.06s|
|AttackToSkillBlendTime|0.06s|
|AttackToMoveBlendTime|0.10s|
|AttackToIdleBlendTime|0.10s|

---

## 37. 输入失败处理

|输入|失败条件|处理|
|---|---|---|
|LightAttackPressed|不在 AttackBufferWindow / ComboWindow / ResetWindow|忽略，不缓存|
|HeavyAttackPressed|不在 AttackBufferWindow / ComboWindow / ResetWindow|忽略，不缓存|
|LightAttackPressed|ComboWindow=false 且 ResetWindow=true|重新进入轻攻击起手|
|HeavyAttackPressed|ComboWindow=false 且 ResetWindow=true|重新进入重攻击起手|
|LightAttackPressed|ComboWindow 开启但当前节点不支持 Light|清除 Attack 缓存|
|HeavyAttackPressed|ComboWindow 开启但当前节点不支持 Heavy|清除 Attack 缓存|
|EvadePressed|不在 InputCommitWindow / EvadeBufferWindow / EvadeCancelWindow|忽略，不缓存|
|EvadePressed|EvadeCancelWindow 开启但 CanEvade=false|不进入 Evade|
|Skill1Pressed|不在 InputCommitWindow / SkillBufferWindow / SkillCancelWindow|忽略，不缓存|
|Skill1Pressed|SkillCancelWindow 开启但 BetaEnergy 不足|清除 Skill 缓存，播放能量不足反馈|
|Skill2Pressed|任意 Attack 阶段|不响应|
|GuardHeld|不在 InputCommitWindow / GuardCancelWindow|不进入 Guard，不缓存|
|RecoverHpPressed|任意 Attack 阶段|不响应|
|LockOnPressed|任意 Attack 阶段|不响应|
|MoveInput|MoveCancelWindow 开启前|只读取，不直接移动|
|MoveInput|MoveCancelWindow 开启且没有更高优先级派生|进入 Locomotion|

---

## 38. 状态流转

```text
Idle / Locomotion
+ LightAttackPressed
+ CanAttack=true
→ Attack(L1)

Idle / Locomotion
+ HeavyAttackPressed
+ CanAttack=true
→ Attack(H1)

Attack
+ LightAttackPressed in Active / AttackBufferWindow
→ 缓存 LightAttack

Attack
+ HeavyAttackPressed in Active / AttackBufferWindow
→ 缓存 HeavyAttack

Attack
+ ComboWindow=true
+ BufferedAttackType=Light
+ CurrentComboNode.SupportsLightNext=true
→ NextLightComboNode

Attack
+ ComboWindow=true
+ BufferedAttackType=Heavy
+ CurrentComboNode.SupportsHeavyNext=true
→ NextHeavyComboNode

Attack
+ ResetWindow=true
+ ComboWindow=false
+ LightAttackPressed / HeavyAttackPressed
→ 重新进入 L1 / H1 起手

Attack
+ EvadePressed
+ InputCommitWindow=true
→ Evade

Attack
+ EvadeInput in EvadeBufferWindow
→ 缓存
→ EvadeCancelWindow 消费
→ Evade

Attack
+ Skill1Pressed
+ InputCommitWindow=true
+ BetaEnergy >= SkillCost
→ Skill

Attack
+ SkillInput in SkillBufferWindow
→ 缓存
→ SkillCancelWindow 消费
→ Skill

Attack
+ GuardHeld
+ InputCommitWindow=true
→ Guard

Attack
+ GuardHeld
+ GuardCancelWindow=true
→ Guard

Attack
+ MoveInput
+ MoveCancelWindow=true
+ 未触发 Evade / Skill / Combo / AttackRestart / Guard
→ Locomotion

Attack
+ CombatHitOutcome.HitReaction
→ HitReaction

Attack
+ CombatHitOutcome.Knockdown
→ Knockdown

Attack
+ HP <= 0
→ Dead

AttackEnd
+ MoveMagnitude > 0.1
→ Locomotion

AttackEnd
+ MoveMagnitude <= 0.1
→ Idle
```

## 2026-06-23 规则补充：PlayerAttack Timeline 是普通攻击数据源

- PlayerAttack 的运行时窗口只来自 `PlayerAttackTimelineAsset` 中的 Timeline clips，不再从旧 Catalog 或旧窗口集合生成默认窗口；相关转发层和默认窗口结构已删除。
- `Phase` 轨道保留运行时读取，但只用于设置 `CurrentPhase` / Animator phase 参数；它不参与 Hitbox、Cancel、Buffer、Reset、MoveCancel 等功能判断。
- 功能判断分别读取对应 functional tracks：Hitbox 的 `Active`、Input Buffer 的 `AttackBuffer / EvadeBuffer / SkillBuffer`、Cancel 的 `CommitCancel / Combo / EvadeCancel / SkillCancel / GuardCancel / Reset / MoveCancel`。
- `CombatPhaseClip` 必须配置强类型 `Phase` 字段，PlayerAttack 至少覆盖 `Start / Active / Recovery / Reset` 且时间上覆盖整个动作有效时长。
- 起手攻击不再硬编码为 L1 / H1 常量，而是从资产中查找 `AttackInputType + ComboIndex == 1`。
- `默认导入入口` 的 PlayerAttack 默认数据来自当前编辑器模板目录 `Assets/Editor/ProjectEVE/CombatTimeline/Defaults/PlayerAttack/`，不再从代码数值表生成。
