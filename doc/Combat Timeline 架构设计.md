# Combat Timeline 架构设计

更新时间：2026-08-05
状态：架构草案
维护者：Codex

本文档定义 Project EVE Demo 中 Combat Timeline 的长期职责边界、窗口语义、运行时解析方向和编辑器扩展规则。它不是替代玩家状态机或 Boss AI 文档，而是作为后续优化 Combat Timeline Editor 与运行时代码解耦的架构入口。

## 1. 设计目标

Combat Timeline 的目标不是把玩家状态机、Boss AI、命中结算、位移系统全部塞进一个编辑器，而是把“一个战斗动作在时间上开放哪些能力”用可视化、可校验、可复用的数据表达出来。

它需要支持：

1. 多种窗口：阶段、Input Buffer、Cancel、Hitbox、无敌、Root Motion、Code Move、防御、完美防御、受击硬直、状态专属窗口。
2. 多种动作结构：普通攻击是有限时长动作，Guard 有 Start / Loop / Release，PerfectEvade 是 Evade 内部模式，Reaction 是受击硬直。
3. 多个同类窗口：一个动作可有多个输入缓存窗口、多个取消窗口、多个 HitNode、多个 motion 段。
4. 运行时代码不随窗口数量增加而无限扩张。
5. 状态机仍保留顶层状态语义，不退化成一个难以调试的 GenericActionState。

## 2. 项目层级定位

项目层级：战斗系统求职 Demo，介于 prototype 与 small-game 之间。

架构原则：

1. 保留现有强类型 `CombatTimelineActionAsset` 方向。
2. 保留玩家顶层状态机作为 gameplay 真相。
3. Combat Timeline 作为动作数据和窗口能力的 authoring 层。
4. 新抽象只用于减少状态类耦合，不引入大型通用 Ability 框架。
5. 优先支持 Boss 战斗原型需要的动作、命中、位移和反馈调参。

## 3. 核心分层

推荐分为五层：

```text
Combat Timeline Editor
    ↓ 编辑 / 预览 / 校验
CombatTimelineActionAsset
    ↓ 资产数据，按动作保存 tracks / clips
Provider Data + CombatActionRuntimeSpec
    ↓ 缓存动作静态数据及解析后的 phase / capability / hit node / motion refs
CombatActionCapabilitySnapshot
    ↓ RuntimeSpec.Evaluate(elapsed) 输出当前帧激活结果
State-local FrameData
    ↓ Attack / Evade / Guard / Skill 等状态在本地转成业务字段
Combat Consumers
    Hitbox / Hurtbox / CombatHitResolver / MovementMotor / AnimationBridge / DebugOverlay
```

### 3.1 Editor 层

职责：显示动作列表和时间轴；创建、拖动、缩放、复制、删除 clip；预览 Animation Preview；校验窗口缺失、越界、冲突和非法重叠。

不负责：运行时状态切换、命中结算、角色移动、Animator Controller 运行逻辑、MotionProfile 完整参数复制。

Editor 修改 Timeline 资产时必须接入 Unity Undo；全局 Timeline 快捷键必须尊重 Inspector 文本焦点，文本 / 数值字段复制、剪切、粘贴、全选或删除时不得触发 Clip 复制、粘贴或删除。

### 3.2 Asset 层

当前强类型资产方向应保留：

```text
CombatTimelineActionAsset
├── PlayerAttackTimelineAsset
├── PlayerSkillTimelineAsset
├── PlayerEvadeTimelineAsset
├── PlayerGuardTimelineAsset
├── PlayerReactionTimelineAsset
├── BossAttackTimelineAsset
└── BossReactionTimelineAsset
```

资产保存 authoring 数据：Owner、ActionKind、ActionId、DisplayName、TotalFrames、FrameRate、AnimationStateName、Tracks、强类型 clip，以及少量动作级参数，例如连段节点、技能消耗、Boss 出招冷却。

资产不保存当前帧状态、命中去重表、输入缓存、当前 Guard mode 或运行时能力快照。

`TotalDuration` 由 `TotalFrames / FrameRate` 派生，但 clip 窗口事实以秒级 `StartTime / EndTime` 为准。编辑器修改动作级 `Duration / Total Frames / Frame Rate` 时不得重采样、平移或裁剪已有 clip；起止帧号只是当前 `FrameRate` 下的显示结果，可以随帧率变化重新计算。

### 3.3 Runtime Spec 层

后续建议新增纯 C# 运行时规格层：

```text
CombatActionRuntimeSpec
├── Identity
├── Duration / FrameRate
├── PhaseSegments
├── CapabilityWindows
├── HitNodes
├── MotionRefs
├── AnimationPreviewRefs
└── ValidationSummary
```

这层是关键解耦点。状态类不应每帧遍历资产里的全部 editor clips，也不应依赖字符串窗口名称。RuntimeSpec 以显式 `CombatTimelineCapabilityId` 作为窗口语义 ID，再输出底层能力快照：

```text
CombatActionRuntimeSpec.Evaluate(elapsed)
    → CombatActionCapabilitySnapshot
```

示例：

```text
CombatActionCapabilitySnapshot
{
    Phase = Active
    CanBufferAttack = true
    CanBufferEvade = true
    CanCancelToSkill = false
    AttackHitboxActive = true
    Invincible = false
    SuperArmor = false
    RootMotionEnabled = true
    ActiveHitNodeIds = [...]
}
```

`CombatActionRuntimeSpec.Evaluate()` 是当前唯一的通用能力解析入口，不拆成大量 `InputBufferWindowSystem`、`CancelWindowSystem`、`InvincibleWindowSystem` 这类小系统。它只负责按 `CapabilityId + TimeRange` 合并同类窗口并输出当前帧能力；输入消费、命中结算、防御解析和位移执行仍由现有消费者负责。

`CombatActionRuntimeSpec` 和 `CombatActionCapabilitySnapshot` 已作为底层归一化入口落地，并从 PlayerAttack 试点推进到 PlayerSkill、PlayerEvade、PlayerGuard、Reaction 与 BossAttack。公共状态视图 facade 已删除；状态类不直接遍历原始 clips，而是在本地 `EvaluateFrame(...)` 中把 `CombatActionCapabilitySnapshot` 转成状态私有 FrameData。当前 Demo 不再预留独立 `CapabilityResolver` 类型，只有出现真实且无法由 RuntimeSpec 清晰表达的合并规则时才重新评估。
当前 Demo 已采用严格数据来源规则：Player / Boss 战斗动作的 Phase、Duration、HitNode、Reaction 时长、Cancel、Defense、Armor 等玩法时间窗只来自 Combat Timeline 资产。运行时不再创建 Empty RuntimeSpec，不再使用 运行时默认窗口配置，不再在缺 Timeline、缺窗口、缺 Reaction 或缺 BossAttack 时补硬编码默认玩法数据。缺关键资产时记录错误并拒绝动作或退回安全状态；`Array.Empty<T>()` 仅保留为集合 API 的安全空结果，不表达默认玩法。

### 3.4 State Runtime Interpreter 层

状态类保留动作语义：

1. `PlayerAttackState` 负责连段切换、攻击输入消费、攻击方向锁存。
2. `PlayerEvadeState` 负责普通 Evade 与 PerfectEvade 内部模式切换。
3. `PlayerGuardState` 负责 Guard Start / Loop / Reaction / Release 的 mode 切换。
4. `PlayerSkillState` 负责技能资源消耗和技能重入。
5. Reaction / Knockdown 状态负责强制受击恢复节奏。

状态类应消费自己的 State-local FrameData，而不是逐个遍历原始 clips 或依赖公共状态视图：

```text
skill.CanCancelToEvade
evade.Invincible
guardStart.GuardBlock
guardRelease.CanCancelToSkill
bossAttack.BossReactionGate
skill.ActiveHitNode
```

### 3.5 状态本地 FrameData 原则

Capability 是底层归一化语言，用于归一化 authoring clip、Validation、Debug 和多窗口合并。状态类可以读取 `CombatActionCapabilitySnapshot`，但应立即在本状态内部转换为私有 `FrameData`，例如 `SkillFrameData`、`EvadeFrameData`、`GuardReleaseFrameData`。这些 FrameData 不放进 Combat Timeline 公共层，避免再次形成第二套解释 facade。

### 3.6 Combat Consumers 层

Combat Timeline 的输出最终被以下系统消费：

| 系统 | 消费内容 |
| --- | --- |
| `PlayerStateContext` | 当前帧能力开关和调试数据 |
| `PlayerActionInputRouter` | buffer / cancel capability |
| `PlayerWeaponHitbox` | Hitbox / HitNode capability |
| `BossAttackExecutor` | Boss HitNode capability |
| `CombatHitResolver` | Defense / Armor capability 的解析结果 |
| `PlayerMovementMotor` / `BossMotionController` | Motion capability 和 MotionProfile 引用 |
| `BossActor` / `BossMovementSystem` | BossReaction 的 BossCodeMove 引用 |
| `PlayerAnimationBridge` / `BossAnimationBridge` | State / Phase / ActionId / Reaction |
| `EveDebugOverlay` | 当前能力快照 |

## 4. 核心术语

### 4.1 Action

Action 是 Combat Timeline 的顶层编辑单位，例如 `L1`、`H1`、`Skill1`、`Evade_Normal`、`Evade_Perfect`、`Guard`、`Player_HitReaction`、`Raven_Slash`。

一个 Action 不一定等于一个顶层状态：`L1 / L2 / H1` 都属于 Attack；`Evade_Normal / Evade_Perfect` 都属于 Evade；GuardHit / PerfectGuardReaction 是 Guard 内部反应时长，保存在 `Guard` Timeline 的 Marker 中，不再作为独立 PlayerReaction Timeline。

### 4.2 Phase

Phase 是粗粒度阶段，用于阅读、动画桥接和 Debug。

允许用途：显示 Start / Active / Recovery / Reset / Loop；同步 Animator 阶段参数；Debug Overlay 展示当前动作阶段。

禁止用途：直接决定 Hitbox、Input Buffer、Cancel、Invincible、Armor、防御是否开启。功能判断必须走 functional clip 或 capability。

### 4.3 Clip

Clip 是编辑器里的可视时间段。Clip 是 authoring 数据，不等于最终运行时行为。多个 clip 可解析成同一种能力，单个 clip 也可携带附加参数。

主要 clip 类型：Phase、InputBuffer、Cancel、HitNode、Defense、Armor、PlayerMotion、BossMotionWarp、BossCodeMove、RootMotionSuppress、Reaction、Interrupt、AnimationPreview、Marker。

### 4.4 Window

Window 是具有 Start / End 的时间区间。Window 不应只靠名称区分，而应具备明确类型和语义。

推荐模型：

```text
Window = TimeRange + CapabilityId + Params
```

示例：

```text
0.24 - 0.38
CapabilityId = Input_AttackBuffer
```

这样可以支持多个同类窗口：多个 AttackBuffer、多个 Hitbox、多个 CodeMove 段、多个 RootMotionSuppress 段、多个 PerfectGuardChain 窗口、状态专属窗口。

窗口的本质不是某个状态类的分支名，而是一个明确的运行时能力 ID。旧 `CombatTimelineWindowRole` 已删除，运行时核心判断依赖 `CombatTimelineCapabilityId + TimeRange + Params`；`CombatTimelineCapabilityKind` 仅作为从 ID 派生出的窗口族分组，用于兼容性校验和编辑器标签。

### 4.5 Capability

Capability 是运行时真正消费的能力。

| 分类 | 能力示例 | 主要消费者 |
| --- | --- | --- |
| Phase | CurrentPhase | AnimationBridge / Debug |
| Input | BufferAttack / BufferEvade / BufferSkill | PlayerActionInputRouter |
| Cancel | CancelToEvade / CancelToSkill / CancelToGuard / RestartAttack / ReturnMove | State |
| Hit | PlayerHitbox / BossHitNode / MultiHit | Hitbox / AttackExecutor |
| Defense | Invincible / PerfectEvade / GuardBlock / PerfectGuard | CombatHitResolver |
| Armor / Gate | SuperArmor / Uninterruptible / BossReactionGate | CombatReceiver / BossActor |
| Motion | PlayerCodeMove / RootMotionEnable / RootMotionSuppress / BossWarp | MovementMotor / BossMotionController |
| Reaction | Stun / Recovery / CanReturn / ReactionMotion | Reaction State |
| Event | VFX / SFX / HitStop / CameraShake marker | Feedback |
| Special | State-owned custom window | Owning state only |

新增窗口时应优先映射到已有 Capability。只有无法归类时才新增 CapabilityKind。

### 4.6 窗口多态建模原则

Combat Timeline 的 clip 数据可以使用多态，但多态应表达“窗口族”，不应表达每个具体能力 ID。

推荐窗口族：

| 窗口族 | 典型参数 | 不建议拆成 |
| --- | --- | --- |
| `InputBufferWindowClip` | InputChannel、BufferPolicy、LifetimePolicy | `AttackBufferClip`、`EvadeBufferClip`、`SkillBufferClip` |
| `CancelWindowClip` | CancelTarget、InputChannel、ResourceGate | `EvadeCancelClip`、`SkillCancelClip`、`GuardCancelClip` |
| `HitNodeWindowClip` | HitNodeId、SourcePart、Damage、ReactionIntent | `LightHitClip`、`CombatReactionIntent.KnockdownClip`、`SkillHitClip` |
| `DefenseWindowClip` | DefenseKind、RequiresIncomingHit、RequiresNearMiss | `InvincibleClip`、`PerfectGuardClip`、`GuardBlockClip` |
| `ArmorWindowClip` | ArmorKind、DamagePolicy、InterruptPolicy | `SuperArmorClip`、`UninterruptibleClip` |
| `MotionWindowClip` | MotionKind、MotionId、ProfileWindowName | `RootMotionClip`、`CodeMoveClip` |
| `ReactionWindowClip` | ReactionType、Stun、Recovery、CanReturn | `HitStunClip`、`KnockdownClip` |
| `InterruptWindowClip` | AllowedAttackTypes、InterruptPolicy | `LightInterruptClip`、`SkillInterruptClip` |
| `MarkerWindowClip` | MarkerKind、PayloadRef | `VfxMarkerClip`、`SfxMarkerClip`、`CameraShakeClip` |

基础 clip 类型只应保存通用字段，例如 `Name`、`StartTime`、`EndTime`、`Enabled` 或少量调试字段。不同窗口族的独有字段应下沉到各自子类，避免基类变成所有窗口字段的集合。

如果某个窗口族内部的字段差异已经非常大，可以继续拆出更小的子类；但拆分标准是“数据结构和校验规则不同”，不是“能力 ID 名称不同”。

### 4.7 Authoring 多态，Runtime 归一化

Editor / Asset 层可以使用 `[SerializeReference]` 保存多态 clip，方便 Inspector 只显示当前窗口族需要的字段。

运行时不应直接消费这些 authoring clip。资产加载或状态进入动作时，应把多态 clip 解析成归一化的 RuntimeSpec：

```text
CombatTimelineActionAsset
    -> polymorphic CombatTimelineClip[]
    -> CombatActionRuntimeSpec
    -> CombatActionCapabilitySnapshot
```

这样既保留编辑器数据的表达力，又避免 `PlayerAttackState`、`PlayerGuardState`、`PlayerSkillState` 等状态类直接依赖 Unity 序列化对象、窗口名称或底层能力 ID。

## 5. 多窗口设计规则

### 5.1 允许多个同类窗口

同类窗口不应被单窗口查询限制为只返回第一个。RuntimeSpec 当前支持：

```text
GetWindows(CombatTimelineCapabilityId.Input_AttackBuffer)
Evaluate(elapsed) -> CombatActionCapabilitySnapshot
```

合并规则：

1. 同类布尔能力：任一窗口激活即为 true。
2. 输入缓存窗口：按显式 CapabilityId 区分，不同输入缓存能力可同时开启。
3. Cancel 窗口：可同时开启，最终由输入消费优先级决定。
4. HitNode：允许多个同时激活，但必须按 HitNodeId / AttackInstanceId 去重。
5. Motion 窗口：默认不允许同一 owner 同一时间有两个互斥 motion；若未来允许，必须由位移业务明确混合或仲裁规则。
6. Defense / Armor：允许重叠，受击解析器按固定玩法规则处理，不读取 Timeline 通用优先级。

### 5.2 同轨重叠不是天然错误

| 情况 | 是否允许 | 规则 |
| --- | --- | --- |
| 多个 HitNode 重叠 | 允许 | 多段或多部位命中 |
| InputBuffer 与 Cancel 重叠 | 允许 | 缓存和消费可同时存在 |
| Invincible 与 PerfectEvade 重叠 | 允许 | PerfectEvade 可要求同时处于无敌或 NearMiss |
| GuardBlock 与 PerfectGuard 重叠 | 允许 | PerfectGuard 优先于普通 Guard |
| 两个互斥 Motion 窗口重叠 | 默认不允许 | Validation 报错 |
| Phase 重叠 | 默认不允许 | Phase 用于阅读和动画桥接 |

### 5.3 Window 名称只用于阅读

名称可用于显示，如 `L1_AttackBuffer_01`、`Skill1_Hit_03`、`Guard_ChainInput`，但运行时核心判断应依赖强类型字段：CapabilityId、InputChannel、CancelTarget、HitNodeId、MotionId、DefenseKind、ArmorKind。

`CombatTimelineWindowRole` 已删除。RuntimeSpec 以 `CombatTimelineCapabilityId` 作为查询入口；窗口族和 `CombatTimelineCapabilityKind` 只用于 UI 分组、校验和调试。

### 5.4 不拆大量窗口系统

窗口数量增加后，不应按窗口类型拆出大量独立系统。推荐只新增一层中间解析：

```text
Timeline Clip 数据
    -> CombatActionRuntimeSpec
    -> CombatActionCapabilitySnapshot
    -> 现有状态和消费者
```

职责边界：

1. RuntimeSpec 负责把 authoring 数据整理成运行时结构。
2. RuntimeSpec 的 `Evaluate()` 负责当前时间点的布尔能力合并，并输出 Snapshot。
3. 状态类负责状态语义、输入消费顺序和顶层状态切换。
4. `PlayerActionInputRouter` 继续负责输入缓存和取消消费。
5. Hitbox / Hurtbox / CombatReceiver 继续负责命中与受击解析及防御优先级。
6. Movement / MotionController 继续负责最终位移执行。

不推荐新增 `InputBufferWindowSystem`、`CancelWindowSystem`、`HitboxWindowSystem`、`InvincibleWindowSystem`、`ArmorWindowSystem`、`RootMotionWindowSystem` 等碎片化系统。窗口是数据，能力解析是中间层，真正的 gameplay 行为仍由现有状态和消费者执行。

## 6. 推荐窗口分类

### 6.1 Phase 窗口

字段：Phase、StartTime、EndTime。

规则：Phase 应覆盖动作主要有效时长；Phase 不参与功能判断；Guard 的 Loop 可表达为特殊 Phase 或 mode segment。

### 6.2 Input Buffer 窗口

字段：InputChannel、BufferPolicy、LifetimePolicy、StartTime、EndTime。

示例：Attack Active 内开启 AttackBuffer / EvadeBuffer；Skill 后段开启 EvadeBuffer；Guard Release 开启 SkillBuffer。

Guard 不进入普通缓存。Guard 使用 Hold 查询或 Guard-specific response window。

### 6.3 Cancel 窗口

字段：CancelTarget、InputChannel、ConsumesBufferedInput、AcceptsImmediateInput、RequiresResource、StartTime、EndTime。

示例：CancelToEvade、CancelToSkill、CancelToGuard、RestartAttack、MoveCancel、ReturnToLocomotion。

取消窗口只表达“具备资格”。最终同帧多个输入同时满足时，仍由统一输入消费优先级决定。

### 6.4 Hitbox / HitNode 窗口

字段：HitNodeId、HitIndex、SourcePart、HitboxGroup、Damage、PoiseDamage、GuardDamage、ReactionIntent、CanBeGuarded、CanBePerfectGuarded、CanBePerfectEvaded、MaxHitsPerTarget、EffectiveRange、EffectiveAngle、StartTime、EndTime。

规则：一段攻击可有多个 HitNode；同一 HitNode 可对应多个检测体；命中去重粒度由攻击实例、HitNodeId、目标 Hurtbox 组成；检测体只负责空间检测，HitNode 负责战斗语义。

运行时 HitNode 只有一份数据结构：CombatHitNodeData。CombatHitNodeClip 只作为 Unity authoring clip 保留；Provider Data / BossAttackDefinition / PlayerSkillActionData 持有 CombatHitNodeData[]；CombatActionCapabilitySnapshot 只返回 ActiveHitNodeIds，不复制伤害、来源部位或防御字段。

### 6.5 Defense 窗口

字段：DefenseKind、RequiresIncomingHit、RequiresNearMiss、StartTime、EndTime。

DefenseKind 示例：Invincible、PerfectEvade、GuardBlock、PerfectGuard。PerfectGuardChainActive 是 Guard 状态按输入事件打开的运行时窗口，不作为静态 Defense Clip。

规则：PerfectGuard 优先于 GuardBlock；PerfectEvade 优先于普通 Invincible；GuardBlock 可以在 Guard Loop 中持续开启；Guard Chain 是 Guard 内部反应窗口，不新增顶层状态。

### 6.6 Armor / Interrupt 窗口

字段：ArmorKind、StillTakesHpDamage、StillAllowsDead、BlocksReactionIntent、StartTime、EndTime。

ArmorKind 示例：SuperArmor、Uninterruptible、IgnoreHitReaction、SkillInterruptArmor。

规则：Dead 永远最高优先级；SuperArmor 可承受 HP 伤害但阻止 HitReaction / Knockdown；Uninterruptible 可作为普通攻击不可断窗口；Boss 是否进入受击反应不再由 Armor_SkillInterruptArmor 与 InterruptWindow 双查询决定，而由 BossReactionGateWindow 统一表达，不改变玩家命中伤害。

### 6.7 Motion 窗口

字段：MotionKind、PlayerMotionId、BossAttackId、ProfileWindowName、StartTime、EndTime。

MotionKind 示例：PlayerCodeMove、RootMotion、RootMotionSuppress、BossMotionWarp、BossCodeMove。

规则：玩家移动出口仍是 `PlayerMovementMotor`；Boss 移动出口仍是 `BossMotionController`；Timeline 只保存 motion id、Boss motion action id 或 profile window name，不复制完整 MotionProfile 参数；RootMotionSuppress 只抑制位移来源，不改变 HitNode、状态切换或动画播放。BossReaction 的 `BossCodeMove` 只暴露 `BossMotionActionId / ProfileWindowName` 给 `BossActor`，真正位移节奏由 `BossMotionWarpProfile` 决定。

`CombatMotionReferenceClip` 使用 `MotionKind=PlayerMotion` 时，RuntimeSpec 将当前激活的 `PlayerMotionId` 写入 Snapshot 的只读 `ActivePlayerMotionIds`。状态只在窗口进入沿发出动作位移请求，具体距离、曲线、目标模式与碰撞规则仍唯一归属 `PlayerActionMotionProfile`。Skill1 当前使用三个 `Skill1Forward` 窗口，现有 Evade、Reaction 和固定方向位移配置不因该接口改变。

### 6.8 Reaction 窗口

字段：ReactionType、StartTime、EndTime、ReactionMotionId。Reaction 节奏由 `Reaction_Stun / Reaction_Recovery / Reaction_CanReturn` 窗口的时间区间解析，Player Motion 可由 Reaction 窗口或 Player Motion 引用提供。

规则：Reaction Timeline 管理顶层受击后的时间节奏，不管理命中判定；HitReaction / Knockdown 等可恢复反应必须提供 Stun / Recovery / CanReturn 功能窗口；HitReaction 受击恢复接动作和 Knockdown 起身接动作都由 Cancel capability 表达，Phase 只做动画 / 调试展示；BossReaction 可以附带 BossCodeMove motion reference，但不直接执行移动；Dead Timeline 只用于死亡动画预览 / 标识，不配置恢复或返回窗口，不改变死亡最高优先级；GuardHit 是 Guard 内部 reaction，不再使用独立 PlayerReactionTimelineAsset 作为时长数据源。

Reaction Timeline 资产级只保存 Reaction 类型，不保存 Direction、StunDuration、RecoveryStartTime、CanReturnTime、ReactionMotionId 或 Animation Preview fallback。Reaction 编辑器预览只读取 `Animation Preview` Track 的 `CombatAnimationClipWindow`，缺少该窗口或缺少 Preview Clip 时只影响编辑器姿态预览，不改变 Reaction 运行时节奏。

### 6.9 Interrupt 窗口

字段：AllowedAttackTypes、StartTime、EndTime。

规则：旧 `CombatInterruptWindowClip` 表达通用状态级打断白名单，暂保留但 Boss 不再消费。Boss 的 `Attack / HitStagger / Knockdown` 使用 `BossReactionGateWindowClip` 决定是否刷新或切换受击状态：`AllowInterrupt` 表达开放反应，`BlockInterrupt` 表达同时间段阻止特定命中结果，Block 优先于 Allow。没有匹配 Allow gate 表示不可进入 Boss 反应；命中造成的 HP 伤害仍由 Receiver / CombatHitResolver 处理。

### 6.10 Special 窗口

状态专属窗口可以存在，但必须受约束。

允许条件：该窗口只对一个状态有意义；无法归类到 Input / Cancel / Hit / Defense / Armor / Motion / Reaction；名称和消费者明确；Validation 能识别缺失或冲突。

示例：GuardReentryWindow、PerfectEvadeCrossFadeMapWindow、ExecutionAlignWindow、GrabSyncWindow。

Special 窗口不应成为常规窗口的逃生口。能归类为能力的，应优先归类为能力。

## 7. Guard 的特殊结构

Guard 不适合被建模成单纯固定时长动作。它应被视为带内部 mode 的顶层状态：

```text
Guard
├── Start
├── Loop
├── Reaction.GuardHit
├── Reaction.PerfectGuard
└── Release
```

### 7.1 Guard Start

Start 是有限时长动作段。

能力：GuardBlock 从某个时间点开始生效；PerfectGuard 在起手早期生效；松开 Guard 且满足最小起手时间后进入 Release 或 Locomotion。

### 7.2 Guard Loop

Loop 是持续模式，不应强行受 `TotalDuration` 限制。

能力：GuardBlock 持续开启；PerfectGuard 默认关闭；Guard Walk 可持续运行；GuardHeld=false 时离开 Loop。

表达方式：Timeline 可用 Loop phase 表达视觉和编辑器预览；运行时不应每帧依赖 Loop clip 的 EndTime 判断是否退出；Loop 退出条件来自输入和受击事件。

Combat Timeline 不接管 Guard Loop 的生命周期。Guard Loop 是否继续存在由 `PlayerGuardState` 的内部 mode、`GuardHeld` 和命中事件决定；Timeline 只提供 Loop 期间开放的 Defense、Cancel、Reaction、Motion 等能力。

### 7.3 Guard Reaction

GuardHit 和 PerfectGuard 是 Guard 内部 reaction。

能力：播放反应动画；持续 GuardBlock；PerfectGuard 期间开放 `Input_PerfectGuardChain`；ChainInput 成功后由 `PlayerGuardState` 按配置时长动态打开 ChainActive；反应结束后根据 GuardHeld 回 Loop 或 Release。

GuardHit / PerfectGuardReaction 时长从 `Guard` Timeline 的 Marker 读取；ChainActive 是按键事件触发后的相对持续时间，不作为 Timeline Clip / Capability。

### 7.4 Guard Release

Release 是有限时长动作段。

能力：GuardBlock 关闭；可有 EvadeBuffer / SkillBuffer；可有 CancelToEvade / CancelToSkill / RestartAttack / CancelToGuard；可自然返回 Idle / Locomotion。

Guard Release 中重新按下或继续持有 Guard 可回到 Guard Start，这属于 Guard 内部 mode 转换，不需要新增顶层状态。

## 8. Timeline 与状态机的关系

状态机负责“是否能进某状态、当前状态怎么解释输入、何时切换顶层状态”。Timeline 负责“当前动作时间点开放哪些能力”。

状态类不应直接遍历原始 Timeline clips，也不应把底层 `CombatTimelineCapabilityId` 当作主要运行时接口。底层 RuntimeSpec 仍生成 `CombatActionCapabilitySnapshot`；普通状态类在自己的 `EvaluateFrame(...)` 中把 Snapshot 转成状态本地 FrameData，再根据自身状态语义决定如何消费能力。

顶层状态语义仍然保留：

1. Attack 仍负责连段、攻击实例、朝向锁存和下一攻击选择。
2. Evade 仍负责普通闪避与 PerfectEvade 内部模式。
3. Guard 仍负责 Start / Loop / Reaction / Release mode。
4. Skill 仍负责资源、释放和技能实例。
5. Reaction 仍负责受击硬直、击倒、恢复和死亡优先级。

Attack 示例：

```text
PlayerAttackState
    读取 L1 RuntimeSpec
    Evaluate(elapsed)
    写入 CurrentPhase / Hitbox / Buffer / Cancel / Armor / Motion capability
    按输入优先级消费 capability
    必要时切到 Evade / Skill / Guard / Attack / Locomotion
```

Skill 示例：

```text
PlayerSkillState
    进入时消耗 Beta
    读取 Skill1 RuntimeSpec
    RuntimeSpec.Evaluate(elapsed)
    Snapshot 给出 ActiveHitNodeIds 与 ActivePlayerMotionIds
    状态从 Provider Data 的 CombatHitNodeData[] 取完整 HitNode 数据
    每个 PlayerMotion 窗口只在进入沿请求一次 Profile 位移
    受击时由 CombatReceiver 查询 Armor capability
```

Guard 示例：

```text
PlayerGuardState
    自己维护 Start / Loop / Reaction / Release mode
    每个 mode 可以绑定不同 runtime segment 或 action id
    Timeline 提供 Defense / Cancel / Reaction / Motion capability
    GuardHeld 和命中事件决定 mode 切换
```

## 9. 数据所有权

ScriptableObject 适合保存 CombatTimelineActionAsset、CombatTimelineCatalog、PlayerActionMotionProfile、BossMotionWarpProfile、Feedback profile。

纯 C# Runtime Model 适合保存 CombatActionRuntimeSpec、CombatActionCapabilitySnapshot、解析后的窗口数组、当前动作实例运行时数据和唯一运行时 HitNode 数据 `CombatHitNodeData`。Snapshot 只保存当前激活的 HitNodeId 与 PlayerMotionId，不保存 HitNode 详情或 MotionProfile 参数。

`CombatTimelineProvider` 缓存转换后的 Provider Data / RuntimeSpec。运行中修改 Timeline 资产不会自动刷新缓存；Combat Timeline Editor 的 `Refresh Runtime` 用于手动清理并重建 runtime cache。

MonoBehaviour / StateContext 适合保存当前顶层状态、状态经过时间、动作实例 ID、当前帧能力开关、资源、输入快照和动作位移请求。

## 10. 通信规则

推荐：Provider 在进入状态或选择动作时提供 Provider Data；状态机每帧读取 RuntimeSpec Snapshot 并转成状态本地 FrameData；状态机写入 PlayerStateContext；Hitbox / Receiver / Movement / Animation 读取 PlayerStateContext；Feedback 通过事件总线接收命中结果；Editor 只写资产。

避免：Hitbox、MovementMotor、CombatHitResolver、AnimationBridge 直接反查 Timeline 资产；状态类每帧遍历 editor clip 并按字符串判断。

## 11. Validation 规则

Validation 应围绕 `CombatTimelineCapabilityId` 检查，而不是围绕 clip 名称检查。通用校验：非 AnimationPreview clip 默认不能超过动作总时长；StartTime 不得大于 EndTime；Phase 不应重叠；必需 capability 缺失时报错；互斥 motion 窗口重叠时报错；未知 Special window 应报警告并标明消费者。

窗口多态校验：每个窗口族必须具备自身必需字段；CapabilityId 与窗口族语义冲突时应报警告；同一资产中不应重复定义同一运行时能力。

PlayerAttack 校验：必须有 Phase、Hitbox 或 HitNode、Reset 或 Return；Combo 节点引用必须存在；`AttackInputType + ComboIndex == 1` 起手节点不能重复。

Guard 校验：Start 必须有 GuardBlock；Start 应有 PerfectGuard；Release 应有返回窗口；必须存在 `Input_PerfectGuardChain`、`GuardHitReaction` Marker、`PerfectGuardReaction` Marker，并且 `PerfectGuardChainActiveDuration` 必须大于 0。

Motion 校验：PlayerMotionId 必须能在 PlayerActionMotionProfile 中找到；BossReaction 的 BossCodeMove 必须填写 Boss motion action id 和 ProfileWindowName；同一 owner 同一时间不应有两个互斥 code move。Combat Timeline 校验只检查引用字段，不直接读取 BossMotionWarpProfile 内容。

HitNode 校验：HitNodeId 在同一动作内必须唯一；Boss HitNode 的 SourcePart 必须能找到对应 `BossAttackHitboxAnchor`，否则警告；MaxHitsPerTarget 必须大于 0；伤害数值不应为负。

## 12. Debug 显示原则

Debug Overlay 应显示运行时能力快照，而不是 editor clip 原始数据。

推荐显示：ActionId、Elapsed / TotalDuration、Phase、Active Input Buffers、Active Cancels、Active HitNodes、Defense Capabilities、Armor Capabilities、Motion Source、RootMotion / CodeMove delta、Reaction Config、Validation status。

这样调试时能回答：这一帧为什么可以或不可以取消、为什么有或没有命中、为什么被打断或没被打断。

Debug 不应主要显示 clip name 或 track name。主视图应显示 `CapabilitySnapshot` 的结果和对应 `CapabilityId`：哪些输入缓存开放、哪些取消能力开放、哪些 defense / armor 能力生效、哪个 motion source 正在驱动。

## 13. 迁移策略

不建议一次性重写 Combat Timeline 和所有状态。推荐小步迁移。

1. 建立架构契约：由本文档定义术语和方向，不改运行时代码。
2. 新增 RuntimeSpec / CapabilitySnapshot 只读解析层：先支持 Phase、InputBuffer、Cancel、Hitbox / HitNode、Defense、Armor。
3. 保持 authoring clip 多态，但运行时只消费归一化能力，不让状态类直接遍历原始 clips。
4. 让 PlayerAttack 继续作为试点：保持现有行为，继续以 RuntimeSpec Snapshot 作为唯一窗口消费路径。
5. 迁移 Skill SuperArmor：把 `IsSkillSuperArmor` 特殊判断改成通用 Armor capability。
6. 新增 Attack Uninterruptible 试点：给某个 Attack 节点增加不可断窗口，验证攻击被命中时仍扣 HP 但不进 HitReaction。
7. 迁移 Guard：保留 `PlayerGuardState` 内部 mode，Start / Release / ChainInput 窗口来自 Guard Timeline capability，GuardHit / PerfectGuardReaction 时长来自 Guard Timeline Marker，ChainActive 由 Guard 状态按输入事件动态打开。
8. 完善 Editor UI：按 Capability 分类显示，Inspector 显示强类型参数，Validation 基于 capability 规则。当前 Demo 不再提供 默认导入入口 或代码生成默认模板，Timeline 资产直接维护。

## 14. 当前不做内容

暂不做完整商业级 Gameplay Ability System、通用节点图、复杂条件表达式编辑器、运行时动态创建 Timeline、多角色共享复杂动作蓝图、完全代码驱动 Animator、把所有状态压成一个 GenericActionState。

原因：当前项目目标是 Boss 战斗 Demo；顶层状态语义仍然清晰；过度通用会增加调试成本；现有 Attack / Guard / Evade / Skill 已有可运行路径，应小步迁移。

## 15. 判断标准

后续新增窗口或功能时，按以下问题判断放在哪里：

1. 是否只是视觉预览？放 AnimationPreview。
2. 是否决定输入能否被缓存？放 InputBuffer capability。
3. 是否决定能否切状态？放 Cancel capability。
4. 是否产生攻击命中？放 HitNode capability。
5. 是否改变防守解析？放 Defense capability。
6. 是否阻止受击打断？放 Armor capability。
7. 是否改变位移来源？放 Motion capability。
8. 是否表达受击硬直？放 Reaction capability。
9. 是否只属于某个状态且无法归类？才使用受控 Special window。

如果一个新窗口不能回答“谁消费它、消费后改变什么运行时能力”，就不应加入正式 Timeline 数据。

## 16. 推荐结论

Combat Timeline 后续应从“很多窗口的编辑器”升级为“动作能力时间线”。

关键方向：

1. Phase 只负责阅读、动画桥接和 Debug。
2. 窗口数据使用窗口族级多态，不按每个具体能力 ID 拆类。
3. 功能窗口统一解析为 Capability。
4. 状态机保留顶层状态语义，并用状态本地 FrameData 消费 RuntimeSpec 输出的 Snapshot。
5. Hitbox、受击、防御、位移、反馈系统只读取状态上下文或专用运行时数据，不直接读取 Timeline 资产。
6. Guard 作为特殊持续状态处理，不能强行套普通有限动作模型。
7. 允许很多窗口，但必须通过显式 CapabilityId、窗口族、时间范围和参数管理复杂度；冲突优先级归属对应业务消费者。
8. 不拆大量窗口系统，只保留 RuntimeSpec / CapabilitySnapshot 这层中间解析。

这套设计能支持后续新增不可断攻击、攻击无敌帧、多段 Hitbox、多个输入缓存窗口、Root Motion / Code Move 混合、防御 Loop、PerfectGuard 连弹、受击恢复和状态专属窗口，同时避免每新增一种窗口都修改多个状态类。
