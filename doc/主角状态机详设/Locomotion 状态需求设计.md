## 状态定位

`Locomotion` 是玩家角色的地面移动状态，用于管理 Boss 战 Demo 中玩家的所有基础移动行为。

该状态负责处理：

```text
1. 自由视角下的普通移动。
2. 自由视角下的战斗移动。
3. 自由视角下的冲刺奔跑。
4. 锁定视角下的八方向移动。
5. 锁定视角下的八方向冲刺奔跑。
6. 移动起步、移动循环、移动停止、转向、刹车、身体倾斜等动画表现。
7. Idle、Evade、Attack、Guard、Skill、HitReaction、Knockdown、Recover 等状态结束后回到移动。
8. 移动状态下切换到攻击、闪避、防御、技能、回血、受击、死亡等状态。
```

`Locomotion` 不负责：

```text
1. 攻击判定。
2. 防御判定。
3. 闪避无敌帧。
4. 完美闪避判定。
5. 完美防御判定。
6. 技能攻击判定。
7. 受击硬直。
8. 倒地与起身。
```

这些逻辑由对应状态负责。

---

## 状态类型

```text
基础状态 / 移动状态 / 可自由响应输入状态
```

`Locomotion` 是低优先级状态，只高于 `Idle`，低于 `Recover / Guard / Attack / Skill / Evade / HitReaction / Knockdown / Dead`。

---

# 1. 状态分层

`Locomotion` 内部结构如下：

```text
Locomotion
├── FreeMove
│   ├── Run
│   ├── Sprint
│   ├── BattleRun
│   └── BattleSprint
│
└── LockOnMove
    ├── LockOnRun
    └── LockOnSprint
```

---

# 2. Sprint 输入意图设计

## 2.1 不能直接使用 EvadeHeld 判断 Sprint

`EvadeInput` 同时承担两个功能：

```text
1. Press：立即进入 Evade。
2. Hold：在合法条件下进入 Sprint。
```

将输入状态拆成两个概念：

```text
EvadeHeld：物理按键是否仍然按住。
SprintIntent：是否已经满足“长按后允许奔跑”的意图。
```

---

## 2.2 EvadeHeld 定义

```text
EvadeHeld 表示 EvadeInput 当前物理按键是否处于按住状态。
```

产生条件：

```text
EvadeInput 按下后，只要按键未释放，EvadeHeld=true。
EvadeInput 释放后，EvadeHeld=false。
```

---

## 2.3 SprintIntent 定义

```text
SprintIntent 表示玩家已经通过长按 EvadeInput 表达了“想要奔跑”的意图。
```

SprintIntent 只能在以下条件下变为 true：

```text
1. EvadeInput 已经处于 Held 状态。
2. EvadeInput 持续按住时间 >= SprintHoldThreshold。
3. 当前没有处于 EvadeInputPressed 的同一帧。
4. 当前状态允许记录 SprintIntent。
```

参数：

```text
SprintHoldThreshold = 0.25s
```

---

## 2.4 SprintIntent 生效规则

```text
EvadeInputPressed 当帧：
- 只允许触发 Evade。
- 不允许触发 Sprint。
- 不允许立刻设置 SprintIntent=true。

EvadeInputHeld 持续超过 SprintHoldThreshold 后：
- SprintIntent=true。

EvadeInputReleased：
- EvadeHeld=false。
- SprintIntent=false。
```

---

## 2.5 SprintIntent 适用场景

SprintIntent 可以在以下场景中生效：

```text
1. Evade 结束时 EvadeInput 仍然按住，并且 SprintIntent=true。
2. Evade 结束后回到 Idle，但 EvadeInput 仍然按住，之后玩家输入 Move 且 SprintIntent=true。
```

SprintIntent 不应在以下场景生效：

```text
1. EvadeInputPressed 的同一帧。
2. EvadeInput 按下时间未达到 SprintHoldThreshold。
3. EvadeInput 已释放。
4. 当前状态为 Attack / Guard / Skill / HitReaction / Knockdown / Recover，且状态没有允许 Sprint 的规则。
```

---

## 2.6 Idle 中 EvadeInput 的处理

Idle 中按下 EvadeInput 时：

```text
Idle
+ EvadeInputPressed
→ Evade
```

即使此时：

```text
EvadeHeld=true
MoveMagnitude > 0.1
```

也不能直接进入 Sprint。

正确流程是：

```text
Idle
+ EvadeInputPressed
→ Evade
→ Evade 结束时，如果 SprintIntent=true 且 MoveMagnitude > 0.1
→ Locomotion.Sprint
```

如果 Evade 结束时没有移动输入：

```text
EvadeEnd
+ SprintIntent=true
+ MoveMagnitude <= 0.1
→ Idle
```

之后如果玩家仍然按住 EvadeInput，并再次输入移动：

```text
Idle
+ SprintIntent=true
+ MoveMagnitude > 0.1
+ 非 EvadeInputPressed 当帧
→ Locomotion.Sprint
```

---

# 3. 子状态说明

## 3.1 FreeMove.Run

### 状态说明

`FreeMove.Run` 表示自由视角、非战斗模式下的普通移动。

适用条件：

```text
1. IsInCombat=false。
2. IsLockOn=false。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=false。
```

对应动画资源：

```text
Proto_Run_Start
Proto_Run
Proto_Run_End_L
Proto_Run_End_R
Proto_Run_Brake
Proto_Run_Turn_L
Proto_Run_Turn_R
Proto_Run_Roll_L
Proto_Run_Roll_R
```

---

## 3.2 FreeMove.Sprint

### 状态说明

`FreeMove.Sprint` 表示自由视角、非战斗模式下的冲刺奔跑。

适用条件：

```text
1. IsInCombat=false。
2. IsLockOn=false。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=true。
5. 当前不是 EvadeInputPressed 当帧。
```

对应动画资源：

```text
Proto_sprint_start
Proto_Sprint
Proto_Sprint_End
Proto_sprint_turn
Proto_sprint_turn_L
Proto_Sprint_Roll_L
Proto_Sprint_Roll_R
Proto_Sprint_Run
```

其中：

```text
Proto_Sprint_Run
用于从 Sprint 释放冲刺键后过渡回 Run。
```

---

## 3.3 FreeMove.BattleRun

### 状态说明

`FreeMove.BattleRun` 表示自由视角、战斗模式下的普通移动。

适用条件：

```text
1. IsInCombat=true。
2. IsLockOn=false。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=false。
```

对应动画资源：

```text
Proto_Battle_Run_Start
Proto_Battle_Run
Proto_Battle_Run_End
Proto_Battle_Run_End_R
Proto_Battle_Run_Turn_L
Proto_Battle_Run_Turn_R
Proto_Battle_Run_Roll_L
Proto_Battle_Run_Roll_R
```

---

## 3.4 FreeMove.BattleSprint

### 状态说明

`FreeMove.BattleSprint` 表示自由视角、战斗模式下的冲刺奔跑。

适用条件：

```text
1. IsInCombat=true。
2. IsLockOn=false。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=true。
5. 当前不是 EvadeInputPressed 当帧。
```

对应动画资源：

```text
Proto_Battle_Sprint_Start
Proto_Battle_Sprint
Proto_Battle_Sprint_Backward
Proto_Battle_Sprint_Left
Proto_Battle_Sprint_Right
Proto_Battle_Sprint_End
Proto_Battle_sprint_turn
Proto_Battle_Sprint_Turn_L
Proto_Battle_Sprint_Roll_L
Proto_Battle_Sprint_Roll_R
Proto_Sprint_Sprint_Run
```

其中：

```text
Proto_Sprint_Sprint_Run
用于从 BattleSprint 释放冲刺键后过渡回 BattleRun。
```

---

## 3.5 LockOnMove.LockOnRun

### 状态说明

`LockOnRun` 表示锁定视角下的普通战斗移动。

适用条件：

```text
1. IsInCombat=true。
2. IsLockOn=true。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=false。
5. 角色持续朝向 Boss。
```

对应动画资源：

```text
Proto_Lockon_Run_Forward_Start
Proto_Lockon_Run_Forward
Proto_Lockon_Run_Forward_Left
Proto_Lockon_Run_Forward_Right
Proto_Lockon_Run_Forward_End

Proto_Lockon_Run_Backward_Start
Proto_Lockon_Run_Backward
Proto_Lockon_Run_Backward_Left
Proto_Lockon_Run_Backward_Right
Proto_Lockon_Run_Backward_End

Proto_Lockon_Run_Left_Start
Proto_Lockon_Run_Left
Proto_Lockon_Run_Left_End

Proto_Lockon_Run_Right_Start
Proto_Lockon_Run_Right
Proto_Lockon_Run_Right_End
```

---

## 3.6 LockOnMove.LockOnSprint

### 状态说明

`LockOnSprint` 表示锁定视角下的冲刺奔跑。

适用条件：

```text
1. IsInCombat=true。
2. IsLockOn=true。
3. MoveMagnitude > MoveThreshold。
4. SprintIntent=true。
5. 当前不是 EvadeInputPressed 当帧。
6. 角色持续朝向 Boss。
```

对应动画资源：

```text
Proto_Lockon_Sprint_Forward_Start
Proto_Lockon_Sprint_Forward
Proto_Lockon_Sprint_Forward_End

Proto_Lockon_Sprint_Backward_Start
Proto_Lockon_Sprint_Backward_Start_L
Proto_Lockon_Sprint_Backward
Proto_Lockon_Sprint_Backward_End
Proto_Lockon_Sprint_Backward_End_L

Proto_Lockon_Sprint_Left_Start
Proto_Lockon_Sprint_Left
Proto_Lockon_Sprint_Left_diagonal_D
Proto_Lockon_Sprint_Left_diagonal_T
Proto_Lockon_Sprint_Left_End

Proto_Lockon_Sprint_Right_Start
Proto_Lockon_Sprint_Right
Proto_Lockon_Sprint_Right_diagonal_D
Proto_Lockon_Sprint_Right_diagonal_T
Proto_Lockon_Sprint_Right_End
```

---

# 4. 进入条件

`Locomotion` 的核心进入条件是：

```text
MoveMagnitude > MoveThreshold
```

---

## 4.1 从 Idle 进入 Locomotion

|来源状态|条件|目标状态|
|---|---|---|
|Idle|MoveMagnitude > MoveThreshold|Locomotion|

细分目标：

|条件|目标子状态|
|---|---|
|`IsInCombat=false` 且 `IsLockOn=false` 且 `SprintIntent=false`|`FreeMove.Run`|
|`IsInCombat=false` 且 `IsLockOn=false` 且 `SprintIntent=true` 且非 `EvadeInputPressed` 当帧|`FreeMove.Sprint`|
|`IsInCombat=true` 且 `IsLockOn=false` 且 `SprintIntent=false`|`FreeMove.BattleRun`|
|`IsInCombat=true` 且 `IsLockOn=false` 且 `SprintIntent=true` 且非 `EvadeInputPressed` 当帧|`FreeMove.BattleSprint`|
|`IsInCombat=true` 且 `IsLockOn=true` 且 `SprintIntent=false`|`LockOnMove.LockOnRun`|
|`IsInCombat=true` 且 `IsLockOn=true` 且 `SprintIntent=true` 且非 `EvadeInputPressed` 当帧|`LockOnMove.LockOnSprint`|

---

## 4.2 从 Evade 进入 Locomotion

| 来源状态  | 条件                                                           | 目标状态              |
| ----- | ------------------------------------------------------------ | ----------------- |
| Evade | EvadeEnd 且 MoveMagnitude > MoveThreshold                     | Locomotion        |
| Evade | EvadeEnd 且 SprintIntent=true 且 MoveMagnitude > MoveThreshold | Locomotion.Sprint |

细分目标：

```text
EvadeEnd
+ SprintIntent=true
+ MoveMagnitude > MoveThreshold
+ IsLockOn=false
+ IsInCombat=false
→ FreeMove.Sprint

EvadeEnd
+ SprintIntent=true
+ MoveMagnitude > MoveThreshold
+ IsLockOn=false
+ IsInCombat=true
→ FreeMove.BattleSprint

EvadeEnd
+ SprintIntent=true
+ MoveMagnitude > MoveThreshold
+ IsLockOn=true
→ LockOnMove.LockOnSprint

EvadeEnd
+ SprintIntent=false
+ MoveMagnitude > MoveThreshold
+ IsLockOn=false
+ IsInCombat=false
→ FreeMove.Run

EvadeEnd
+ SprintIntent=false
+ MoveMagnitude > MoveThreshold
+ IsLockOn=false
+ IsInCombat=true
→ FreeMove.BattleRun

EvadeEnd
+ SprintIntent=false
+ MoveMagnitude > MoveThreshold
+ IsLockOn=true
→ LockOnMove.LockOnRun
```

---

## 4.3 从其他动作状态进入 Locomotion

| 来源状态        | 条件                                                         | 目标状态       |
| ----------- | ---------------------------------------------------------- | ---------- |
| Attack      | AttackEnd 或 ResetWindow，且 MoveMagnitude > MoveThreshold    | Locomotion |
| Guard       | GuardReleased / GuardEnd，且 MoveMagnitude > MoveThreshold   | Locomotion |
| Skill       | SkillEnd 或 ResetWindow，且 MoveMagnitude > MoveThreshold     | Locomotion |
| HitReaction | HitEnd 或 ResetWindow，且 MoveMagnitude > MoveThreshold       | Locomotion |
| Knockdown   | KnockdownEnd 或 ResetWindow，且 MoveMagnitude > MoveThreshold | Locomotion |
| Recover     | RecoverEnd 或 ResetWindow，且 MoveMagnitude > MoveThreshold   | Locomotion |

---

## 4.4 通用进入条件

```text
1. 玩家 HP > 0。
2. MoveMagnitude > MoveThreshold。
3. 当前没有更高优先级状态。
4. 当前没有处于动作锁定区间。
5. 当前状态允许回到移动，或当前动作已结束。
```

---

# 5. 退出条件

|输入 / 事件|条件|下一个状态|说明|
|---|---|---|---|
|MoveInput 归零|MoveMagnitude <= MoveThreshold|Idle|停止移动|
|LightAttackPressed|CanAttack=true|Attack|移动中轻攻击|
|HeavyAttackPressed|CanAttack=true|Attack|移动中重攻击|
|EvadePressed|CanEvade=true|Evade|移动中闪避|
|GuardHeld|CanGuard=true|Guard|移动中防御|
|Skill1Pressed|BetaEnergy >= SkillCost|Skill|移动中释放技能|
|RecoverInput|HealingReagentCount > 0 且 CurrentHP < MaxHP|Recover|移动中回血|
|EnemyAttackResolvedAsHit|普通命中|HitReaction|普通受击|
|EnemyAttackResolvedAsKnockdown|强命中 / 击倒|Knockdown|大硬直 / 倒地|
|HP <= 0|任意时刻|Dead|死亡|

---

# 6. 可响应输入

`Locomotion` 是自由响应状态，因此所有合法输入都应立即响应，不使用输入缓存。

|输入|响应方式|说明|
|---|---|---|
|MoveInput|持续响应|控制移动方向、移动速度和动画参数|
|LookInput|持续响应|控制相机或锁定视角偏移|
|LockOnPressed|立即响应|切换锁定状态，不改变顶层状态|
|LightAttackPressed|立即响应|进入 Attack|
|HeavyAttackPressed|立即响应|进入 Attack|
|EvadePressed|立即响应|进入 Evade|
|EvadeHeld|持续读取|只用于计算 SprintIntent，不直接触发 Sprint|
|SprintIntent|条件响应|决定是否进入 Sprint 子状态|
|GuardHeld|立即响应|进入 Guard|
|Skill1Pressed|资源足够时响应|进入 Skill|
|RecoverInput|条件满足时响应|进入 Recover|

---

# 7. 状态区间

`Locomotion` 是循环型基础状态，不需要标准动作状态中的：

```text
StartPhase
ActivePhase
RecoveryPhase
ResetWindow
```

但 Locomotion 的动画表现可以包含：

```text
MoveStart
MoveLoop
MoveTurn
MoveStop
SprintStart
SprintLoop
SprintTurn
SprintStop
Brake
RollLean
```

这些属于动画表现层，不作为顶层逻辑状态。

---

# 8. 移动规则

## 8.1 自由视角移动规则

自由视角下，Move 输入相对于当前相机方向计算。

```text
1. 读取 MoveInput = Vector2(x, y)。
2. 获取 CameraForward 和 CameraRight。
3. 忽略 CameraForward / CameraRight 的 Y 轴分量。
4. 计算 MoveDirection = CameraForward * y + CameraRight * x。
5. 如果 MoveMagnitude > MoveThreshold，则角色朝 MoveDirection 平滑转向。
6. 角色沿 MoveDirection 移动。
```

### 自由视角移动方向

|输入|移动方向|
|---|---|
|W|相机前方|
|S|相机后方|
|A|相机左方|
|D|相机右方|
|W + A|相机左前方|
|W + D|相机右前方|
|S + A|相机左后方|
|S + D|相机右后方|

---

## 8.2 锁定视角移动规则

锁定视角下，角色默认朝向锁定目标，Move 输入转换为相对 Boss 的八方向移动。

```text
1. 玩家锁定 Boss 后，ControlMode = LockOn。
2. 角色持续朝向 Boss。
3. MoveInput 不改变角色朝向，只改变移动方向。
4. W：朝 Boss 前进。
5. S：远离 Boss 后退。
6. A：围绕 Boss 向左移动。
7. D：围绕 Boss 向右移动。
8. WA / WD / SA / SD：执行斜方向移动。
```

### 锁定八方向

| 输入    | Direction8   |
| ----- | ------------ |
| W     | Forward      |
| W + D | ForwardRight |
| D     | Right        |
| S + D | BackRight    |
| S     | Back         |
| S + A | BackLeft     |
| A     | Left         |
| W + A | ForwardLeft  |

---

# 9. 速度模式规则

## 9.1 速度模式

|速度模式|说明|
|---|---|
|Run|非战斗自由视角普通移动|
|Sprint|非战斗自由视角冲刺奔跑|
|BattleRun|战斗自由视角普通移动|
|BattleSprint|战斗自由视角冲刺奔跑|
|LockOnRun|锁定视角普通移动|
|LockOnSprint|锁定视角冲刺奔跑|

---

## 9.2 速度模式判断

|条件|速度模式|
|---|---|
|`IsInCombat=false`，`IsLockOn=false`，`SprintIntent=false`|FreeMove.Run|
|`IsInCombat=false`，`IsLockOn=false`，`SprintIntent=true`，非 `EvadeInputPressed` 当帧|FreeMove.Sprint|
|`IsInCombat=true`，`IsLockOn=false`，`SprintIntent=false`|FreeMove.BattleRun|
|`IsInCombat=true`，`IsLockOn=false`，`SprintIntent=true`，非 `EvadeInputPressed` 当帧|FreeMove.BattleSprint|
|`IsInCombat=true`，`IsLockOn=true`，`SprintIntent=false`|LockOnMove.LockOnRun|
|`IsInCombat=true`，`IsLockOn=true`，`SprintIntent=true`，非 `EvadeInputPressed` 当帧|LockOnMove.LockOnSprint|

---

# 10. 转向规则

## 10.1 FreeMove 转向

```text
1. FreeMove 下，角色朝 MoveDirection 平滑旋转。
2. MoveMagnitude <= MoveThreshold 时，不更新移动方向。
3. LookInput 只影响相机，不强制改变角色朝向。
4. Sprint 状态下如果 TurnAngle 超过 SprintTurnThreshold，可以播放 SprintTurn 或 SprintRoll 动画。
5. Run 状态下如果 TurnAngle 超过 RunTurnThreshold，可以播放 RunTurn 动画。
```

---

## 10.2 LockOnMove 转向

```text
1. LockOnMove 下，角色持续朝向 Boss。
2. 角色朝向由 Boss 位置决定，不由 MoveDirection 决定。
3. MoveDirection 只决定移动方向和动画 Blend Tree 参数。
4. 如果 Boss 死亡或解除锁定，则切回 FreeMove。
5. `MoveX / MoveY / MoveMagnitude` 不直接使用原始输入值，而是由 `PlayerMovementMotor` 将 `CharacterController.Move()` 实际产生的水平速度投影到锁定坐标系后写入，避免方向切换时动画在离散输入值之间硬切。
```

---

# 11. LockOn 规则

`LockOnPressed` 不改变顶层状态，只改变 `ControlMode`。

```text
Locomotion + LockOnPressed
→ 顶层状态仍然是 Locomotion
→ ControlMode 在 Free / LockOn 之间切换
```

规则：

```text
1. 如果当前没有锁定目标，按下 LockOnPressed 尝试锁定 Boss。
2. 如果当前已经锁定 Boss，按下 LockOnPressed 解除锁定。
3. Boss 死亡后自动解除锁定。
4. 玩家死亡后自动解除锁定。
5. 如果 Boss 超出锁定距离，可自动解除锁定。
```

---

# 12. 攻击 / 防御 / 无敌规则

|项目|规则|
|---|---|
|攻击判定|无|
|Hitbox|关闭|
|防御判定|无|
|Guard 判定|无|
|PerfectGuard|不可触发，必须进入 Guard|
|PerfectEvade|不可触发，必须进入 Evade|
|无敌帧|无|
|霸体|无|
|受击|可以被 Boss 命中|

---

# 13. 输入缓存规则

`Locomotion` 不使用输入缓存。

原因：

```text
1. Locomotion 是自由响应状态。
2. 合法输入应该立即执行。
3. 不合法输入直接失败并播放反馈。
4. 缓存主要用于 Attack / Evade / Guard / Skill 等动作状态。
```

---

## 输入失败处理

| 输入            | 失败条件                     | 处理                        |
| ------------- | ------------------------ | ------------------------- |
| Skill1Input   | BetaEnergy 不足            | 不进入 Skill，播放能量不足反馈        |
| RecoverInput  | HealingReagentCount <= 0 | 不进入 Recover，播放道具不足反馈      |
| RecoverInput  | CurrentHP >= MaxHP       | 不进入 Recover，播放不可使用反馈      |
| LockOnPressed | Boss 不可锁定                | 不切换 ControlMode，可播放锁定失败反馈 |
| EvadePressed  | CanEvade=false           | 不进入 Evade，可播放失败反馈或无反馈     |

---

# 14. 状态切换规则

## 14.1 状态切换图

```text
Locomotion
├── MoveMagnitude <= MoveThreshold
│   └── Idle
├── LightAttackPressed
│   └── Attack
├── HeavyAttackPressed
│   └── Attack
├── EvadePressed
│   └── Evade
├── GuardHeld
│   └── Guard
├── Skill1Pressed + BetaEnergy 足够
│   └── Skill
├── RecoverInput + HealingReagentCount > 0 + CurrentHP < MaxHP
│   └── Recover
├── EnemyAttackResolvedAsHit
│   └── HitReaction
├── EnemyAttackResolvedAsKnockdown
│   └── Knockdown
└── HP <= 0
    └── Dead
```

---

## 14.2 状态切换表

| 当前状态       | 输入 / 事件                        | 条件                                                                        | 下一个状态             | 说明              |
| ---------- | ------------------------------ | ------------------------------------------------------------------------- | ----------------- | --------------- |
| Locomotion | MoveInput 归零                   | MoveMagnitude <= MoveThreshold                                            | Idle              | 停止移动            |
| Locomotion | LockOnPressed                  | Boss 可锁定 / 已锁定                                                            | Locomotion        | 只切换 ControlMode |
| Locomotion | LightAttackPressed             | CanAttack=true                                                            | Attack            | 移动轻攻击           |
| Locomotion | HeavyAttackPressed             | CanAttack=true                                                            | Attack            | 移动重攻击           |
| Locomotion | EvadePressed                   | CanEvade=true                                                             | Evade             | 移动闪避            |
| Locomotion | GuardHeld                      | CanGuard=true                                                             | Guard             | 移动防御            |
| Locomotion | Skill1Pressed                  | BetaEnergy >= SkillCost                                                   | Skill             | 移动释放技能          |
| Locomotion | RecoverInput                   | HealingReagentCount > 0 且 CurrentHP < MaxHP                               | Recover           | 移动回血            |
| Locomotion | EnemyAttackResolvedAsHit       | 普通命中                                                                      | HitReaction       | 普通受击            |
| Locomotion | EnemyAttackResolvedAsKnockdown | 强命中 / 击倒                                                                  | Knockdown         | 大硬直 / 倒地        |
| Locomotion | HP <= 0                        | 任意时刻                                                                      | Dead              | 死亡              |

---

# 15. 动画需求

## 15.1 FreeMove.Run

```text
Proto_Run_Start
Proto_Run
Proto_Run_End_L
Proto_Run_End_R
Proto_Run_Brake
Proto_Run_Turn_L
Proto_Run_Turn_R
Proto_Run_Roll_L
Proto_Run_Roll_R
```

---

## 15.2 FreeMove.Sprint

```text
Proto_sprint_start
Proto_Sprint
Proto_Sprint_End
Proto_sprint_turn
Proto_sprint_turn_L
Proto_Sprint_Roll_L
Proto_Sprint_Roll_R
Proto_Sprint_Run
```

---

## 15.3 FreeMove.BattleRun

```text
Proto_Battle_Run_Start
Proto_Battle_Run
Proto_Battle_Run_End
Proto_Battle_Run_End_R
Proto_Battle_Run_Turn_L
Proto_Battle_Run_Turn_R
Proto_Battle_Run_Roll_L
Proto_Battle_Run_Roll_R
```

---

## 15.4 FreeMove.BattleSprint

```text
Proto_Battle_Sprint_Start
Proto_Battle_Sprint
Proto_Battle_Sprint_Backward
Proto_Battle_Sprint_Left
Proto_Battle_Sprint_Right
Proto_Battle_Sprint_End
Proto_Battle_sprint_turn
Proto_Battle_Sprint_Turn_L
Proto_Battle_Sprint_Roll_L
Proto_Battle_Sprint_Roll_R
Proto_Sprint_Sprint_Run
```

---

## 15.5 LockOnMove.LockOnRun

```text
Proto_Lockon_Run_Forward_Start
Proto_Lockon_Run_Forward
Proto_Lockon_Run_Forward_Left
Proto_Lockon_Run_Forward_Right
Proto_Lockon_Run_Forward_End

Proto_Lockon_Run_Backward_Start
Proto_Lockon_Run_Backward
Proto_Lockon_Run_Backward_Left
Proto_Lockon_Run_Backward_Right
Proto_Lockon_Run_Backward_End

Proto_Lockon_Run_Left_Start
Proto_Lockon_Run_Left
Proto_Lockon_Run_Left_End

Proto_Lockon_Run_Right_Start
Proto_Lockon_Run_Right
Proto_Lockon_Run_Right_End
```

---

## 15.6 LockOnMove.LockOnSprint

```text
Proto_Lockon_Sprint_Forward_Start
Proto_Lockon_Sprint_Forward
Proto_Lockon_Sprint_Forward_End

Proto_Lockon_Sprint_Backward_Start
Proto_Lockon_Sprint_Backward_Start_L
Proto_Lockon_Sprint_Backward
Proto_Lockon_Sprint_Backward_End
Proto_Lockon_Sprint_Backward_End_L

Proto_Lockon_Sprint_Left_Start
Proto_Lockon_Sprint_Left
Proto_Lockon_Sprint_Left_diagonal_D
Proto_Lockon_Sprint_Left_diagonal_T
Proto_Lockon_Sprint_Left_End

Proto_Lockon_Sprint_Right_Start
Proto_Lockon_Sprint_Right
Proto_Lockon_Sprint_Right_diagonal_D
Proto_Lockon_Sprint_Right_diagonal_T
Proto_Lockon_Sprint_Right_End
```

---

# 16. Animator 参数

| 参数名                      | 类型         | 用途                    |
| ------------------------ | ---------- | --------------------- |
| `PlayerState`            | Int / Enum | 当前顶层状态                |
| `ControlMode`            | Int / Enum | Free / LockOn         |
| `IsInCombat`             | Bool       | 是否处于战斗模式              |
| `IsLockOn`               | Bool       | 是否锁定 Boss             |
| `IsMoving`               | Bool       | MoveMagnitude 是否大于阈值  |
| `MoveX`                  | Float      | 实际水平速度投影后的横向动画参数      |
| `MoveY`                  | Float      | 实际水平速度投影后的纵向动画参数      |
| `MoveMagnitude`          | Float      | 实际水平速度归一化后的动画混合强度     |
| `MoveAngle`              | Float      | 移动方向角度                |
| `Direction8`             | Int / Enum | 八方向移动                 |
| `EvadeHeld`              | Bool       | EvadeInput 物理按键是否仍然按住 |
| `SprintIntent`           | Bool       | 是否满足长按奔跑意图            |
| `IsSprinting`            | Bool       | 当前是否处于 Sprint 移动模式    |
| `TurnAngle`              | Float      | 当前需要转向的角度             |
| `TriggerMoveStart`       | Trigger    | 移动起步                  |
| `TriggerMoveStop`        | Trigger    | 移动停止                  |
| `TriggerSprintStart`     | Trigger    | 冲刺开始                  |
| `TriggerSprintStop`      | Trigger    | 冲刺停止                  |
| `TriggerRunTurnLeft`     | Trigger    | 普通移动左转                |
| `TriggerRunTurnRight`    | Trigger    | 普通移动右转                |
| `TriggerSprintTurnLeft`  | Trigger    | 冲刺左转                  |
| `TriggerSprintTurnRight` | Trigger    | 冲刺右转                  |
| `TriggerBrake`           | Trigger    | 急停 / 刹车               |
| `TriggerRollLeanLeft`    | Trigger    | 左倾斜移动                 |
| `TriggerRollLeanRight`   | Trigger    | 右倾斜移动                 |

---

# 17. Blend Tree 设计

## 17.1 FreeMove Blend Tree

`FreeMove` 可以使用速度模式切换或 Blend Tree。

推荐：

```text
IsInCombat=false + SprintIntent=false → Run
IsInCombat=false + SprintIntent=true  → Sprint
IsInCombat=true  + SprintIntent=false → BattleRun
IsInCombat=true  + SprintIntent=true  → BattleSprint
```

`BattleSprint` 存在前、后、左、右动画，因此战斗冲刺可以使用 2D Blend Tree。

---

## 17.2 LockOnMove Blend Tree

`LockOnMove` 使用 2D Blend Tree。

### LockOnRun

| 动画            | MoveX | MoveY |
| ------------- | ----: | ----: |
| Forward       |     0 |     1 |
| ForwardLeft   |    -1 |     1 |
| ForwardRight  |     1 |     1 |
| Backward      |     0 |    -1 |
| BackwardLeft  |    -1 |    -1 |
| BackwardRight |     1 |    -1 |
| Left          |    -1 |     0 |
| Right         |     1 |     0 |

### LockOnSprint

| 动画               | MoveX | MoveY |
| ---------------- | ----: | ----: |
| Forward          |     0 |     1 |
| Backward         |     0 |    -1 |
| Left             |    -1 |     0 |
| Right            |     1 |     0 |
| Left_diagonal_T  |    -1 |     1 |
| Left_diagonal_D  |    -1 |    -1 |
| Right_diagonal_T |     1 |     1 |
| Right_diagonal_D |     1 |    -1 |

---

# 18. 动画事件

`Locomotion` 不强依赖动画事件，但需要支持表现和调试事件。

| 事件名                     | 用途        |
| ----------------------- | --------- |
| `Footstep`              | 脚步声       |
| `MoveStartEnd`          | 移动起步结束    |
| `MoveStopEnd`           | 移动停止结束    |
| `SprintStartEnd`        | 冲刺起步结束    |
| `SprintStopEnd`         | 冲刺停止结束    |
| `TurnEnd`               | 转向动画结束    |
| `BrakeEnd`              | 急停 / 刹车结束 |
| `RollLeanEnd`           | 倾斜移动结束    |
| `EvadeToSprintStartEnd` | 闪避转冲刺起步结束 |

---

# 19. 数值参数

| 参数                       |             建议值 | 说明                                        |
| ------------------------ | --------------: | ----------------------------------------- |
| `MoveThreshold`          |           `0.1` | 进入 Locomotion 的最小移动输入                     |
| `SprintHoldThreshold`    |         `0.25s` | EvadeInput 持续按住超过该时间后设置 SprintIntent=true |
| `RunSpeed`               |       `4.5 m/s` | 非战斗普通移动速度                                 |
| `SprintSpeed`            |       `6.5 m/s` | 非战斗冲刺速度                                   |
| `BattleRunSpeed`         |       `4.3 m/s` | 战斗自由移动速度                                  |
| `BattleSprintSpeed`      |       `6.2 m/s` | 战斗自由冲刺速度                                  |
| `LockOnRunSpeed`         |       `3.8 m/s` | 锁定普通移动速度                                  |
| `LockOnSprintSpeed`      |       `5.8 m/s` | 锁定冲刺速度                                    |
| `BackwardSpeedRate`      |           `0.8` | 后退速度倍率                                    |
| `StrafeSpeedRate`        |           `0.9` | 横移速度倍率                                    |
| `DiagonalSpeedRate`      |          `0.95` | 斜向移动速度倍率                                  |
| `Acceleration`           |            `20` | 加速度                                       |
| `Deceleration`           |            `25` | 减速度                                       |
| `FreeMoveRotateSpeed`    |        `720°/s` | 自由移动转向速度                                  |
| `LockOnRotateSpeed`      |        `900°/s` | 锁定朝向 Boss 的旋转速度                           |
| `RunTurnThreshold`       |           `90°` | 普通移动触发转向动画的角度                             |
| `SprintTurnThreshold`    |          `100°` | 冲刺触发转向动画的角度                               |
| `BrakeSpeedThreshold`    |       `5.5 m/s` | 速度高于该值且输入归零时可触发刹车                         |
| `MoveToIdleBlendTime`    | `0.10s - 0.20s` | 移动到 Idle 的混合时间                            |
| `MoveToAttackBlendTime`  | `0.03s - 0.08s` | 移动到攻击的混合时间                                |
| `MoveToEvadeBlendTime`   | `0.03s - 0.06s` | 移动到闪避的混合时间                                |
| `MoveToGuardBlendTime`   | `0.03s - 0.08s` | 移动到防御的混合时间                                |
| `MoveToSkillBlendTime`   | `0.05s - 0.10s` | 移动到技能的混合时间                                |
| `MoveToRecoverBlendTime` | `0.08s - 0.15s` | 移动到回血的混合时间                                |

---

# 20. 验收标准

## 20.1 基础移动验收

```text
1. Idle 中按下 WASD，MoveMagnitude > MoveThreshold 时进入 Locomotion。
2. Locomotion 中松开 WASD，MoveMagnitude <= MoveThreshold 时进入 Idle。
3. Locomotion 中 HP > 0，且没有更高优先级状态时，才能保持移动。
4. Locomotion 不使用输入缓存，所有合法输入立即响应。
```

---

## 20.2 SprintIntent 验收

```text
1. Idle 中 EvadeInputPressed 当帧必须进入 Evade，不允许直接进入 Sprint。
2. EvadeInputPressed 当帧即使 EvadeHeld=true 且 MoveMagnitude > MoveThreshold，也不能进入 Sprint。
3. EvadeInput 持续按住时间达到 SprintHoldThreshold 后，SprintIntent=true。
4. EvadeInputReleased 后，EvadeHeld=false，SprintIntent=false。
5. Evade 结束时，如果 SprintIntent=true 且 MoveMagnitude > MoveThreshold，可以进入 Sprint。
6. Evade 结束时，如果 SprintIntent=true 但 MoveMagnitude <= MoveThreshold，应回到 Idle。
7. 回到 Idle 后，如果 SprintIntent=true，且玩家再次输入 Move，可以直接进入 Sprint。
8. SprintIntent=false 时，Locomotion 只能进入 Run / BattleRun / LockOnRun，不能进入 Sprint。
```

---

## 20.3 自由视角移动验收

```text
1. FreeMove 下 W 朝相机前方移动。
2. FreeMove 下 S 朝相机后方移动。
3. FreeMove 下 A / D 分别朝相机左 / 右移动。
4. FreeMove 下角色朝 MoveDirection 平滑转向。
5. FreeMove 下 LookInput 只控制相机，不直接旋转角色。
6. 非战斗模式下 Run 播放 Proto_Run。
7. 非战斗模式下 Sprint 播放 Proto_Sprint。
8. 战斗模式下 BattleRun 播放 Proto_Battle_Run。
9. 战斗模式下 BattleSprint 播放 Proto_Battle_Sprint 或对应方向 Sprint 动画。
```

---

## 20.4 锁定移动验收

```text
1. LockOnMove 下角色持续朝向 Boss。
2. LockOnMove 下 W 表示朝 Boss 前进。
3. LockOnMove 下 S 表示远离 Boss 后退。
4. LockOnMove 下 A / D 表示围绕 Boss 左右移动。
5. LockOnMove 下 WA / WD / SA / SD 可以正确映射到斜方向移动。
6. LockOnMove 下移动不改变角色面朝方向。
7. LockOnRun 播放对应的 Proto_Lockon_Run_* 动画。
8. LockOnSprint 播放对应的 Proto_Lockon_Sprint_* 动画。
```

---

## 20.5 状态切换验收

```text
1. Locomotion 中 LightAttackPressed 且 CanAttack=true 时进入 Attack。
2. Locomotion 中 HeavyAttackPressed 且 CanAttack=true 时进入 Attack。
3. Locomotion 中 EvadePressed 且 CanEvade=true 时进入 Evade。
4. Locomotion 中 GuardHeld 且 CanGuard=true 时进入 Guard。
5. Locomotion 中 Skill1Pressed 且 BetaEnergy 足够时进入 Skill。
6. Locomotion 中 RecoverInput 且道具数量 > 0、HP 未满时进入 Recover。
7. Locomotion 中 RecoverInput 但 HP 已满时，不进入 Recover。
8. Locomotion 中 RecoverInput 但道具数量不足时，不进入 Recover。
9. Locomotion 中 Skill1Pressed 但 BetaEnergy 不足时，不进入 Skill。
10. Locomotion 中 LockOnPressed 只切换 ControlMode，不改变顶层状态。
```

---

## 20.6 受击与死亡验收

```text
1. Locomotion 中被普通攻击最终解析命中时，进入 HitReaction。
2. Locomotion 中被强攻击、击倒攻击或破防攻击最终解析命中时，进入 Knockdown。
3. Locomotion 中 HP <= 0 时，立即进入 Dead。
4. 进入 Dead 时停止 Locomotion 移动，并清除所有输入缓存。
```

---

## 20.7 动画验收

```text
1. FreeMove.Run 能正常播放 Proto_Run_Start、Proto_Run、Proto_Run_End_L/R。
2. FreeMove.Sprint 能正常播放 Proto_sprint_start、Proto_Sprint、Proto_Sprint_End。
3. FreeMove.BattleRun 能正常播放 Proto_Battle_Run_Start、Proto_Battle_Run、Proto_Battle_Run_End。
4. FreeMove.BattleSprint 能正常播放 Proto_Battle_Sprint_Start、Proto_Battle_Sprint、Proto_Battle_Sprint_End。
5. LockOnRun 八方向动画能根据实际水平速度驱动的 MoveX / MoveY 正确混合，W/A/S/D 切换不应硬跳。
6. LockOnSprint 八方向动画能根据实际水平速度驱动的 MoveX / MoveY 正确混合。
7. Run / Sprint 转向动画能在 TurnAngle 达到阈值时正确播放。
8. Brake 动画能在高速移动突然停止时正确播放。
9. RollLean 动画能在高速转向或镜头快速旋转时正确播放。
10. Locomotion 到 Attack / Evade / Guard / Skill 的过渡不能有明显卡顿。
11. Sprint 到 Run 的过渡自然，释放 EvadeInput 后不会突然停顿。
12. LockOnMove 中角色朝向 Boss 时不出现明显抖动。
13. MoveMagnitude 在阈值附近变化时，不应频繁 Idle / Locomotion 抖动。
```

---

# 21. 设计备注

```text
1. Locomotion 是自由响应状态，不使用输入缓存。
2. EvadeHeld 只表示按键是否按住，不代表可以冲刺。
3. SprintIntent 才是进入 Sprint 的判断依据。
4. EvadeInputPressed 当帧必须优先进入 Evade，不能直接进入 Sprint。
5. LockOnPressed 只改变 ControlMode，不改变顶层状态。
6. FreeMove 和 LockOnMove 的核心区别是角色朝向规则。
7. Locomotion 不处理完美闪避、完美防御、攻击判定和受击硬直。
8. Locomotion 可以完整接入起步、循环、停止、转向、刹车、倾斜动画，但这些属于动画表现层，不作为顶层逻辑状态。
```
