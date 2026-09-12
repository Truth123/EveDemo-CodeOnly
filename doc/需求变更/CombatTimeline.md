# Combat Timeline 需求变更

更新时间：2026-09-08

本文档记录 Combat Timeline 模块仍影响后续实现判断的需求变更。完整历史可追溯 `doc/需求变更记录.md`。

## 活跃规则

- Runtime Capability 窗口不保存通用 `Priority`。同一 Capability 的布尔窗口按“任一激活即成立”合并；Cancel 输入消费、防御/Armor 结算和 Boss Gate 阻断顺序由对应业务代码显式决定，不通过 Timeline 数值做跨业务仲裁。Reaction 需要单一时间锚点时读取该 Capability 开始时间最早的窗口。
- RuntimeSpec 必须解析 PlayerMotion 类型的 `CombatMotionReferenceClip`，并在 Snapshot 只读暴露 `ActivePlayerMotionIds`；状态负责在窗口进入沿请求一次位移，Timeline 不复制 `PlayerActionMotionProfile` 的距离、目标与碰撞参数。
- Combat Timeline 是“动作能力时间线”，不是状态类分支集合。
- Editor / Asset 层可以使用 `[SerializeReference]` 多态 clip；运行时已先通过最小 RuntimeSpec / CapabilitySnapshot 解析层解耦 PlayerAttack。
- Phase 只负责阅读、动画桥接和 Debug；功能判断归类为 Input、Cancel、Hit、Defense、Armor、Motion、Reaction 或受控 Special capability。
- 窗口多态按窗口族拆分，不按每个具体能力单独建类。
- `CombatTimelineWindowRole` 和旧 Channel/Target 维度已删除；运行时主查询键改为显式 `CombatTimelineCapabilityId`。
- PlayerAttack 运行时数据只来自 `PlayerAttackTimelineAsset`，并通过 `CombatActionRuntimeSpec.Evaluate()` 生成能力快照。
- 公共状态视图层已删除；PlayerAttack、PlayerSkill、PlayerEvade、PlayerGuard 改为 Provider Data + RuntimeSpec Snapshot + 状态本地 FrameData，底层窗口语义仍统一为 `CombatTimelineCapabilityId`。HitNode 运行时详情统一为 `CombatHitNodeData`，Snapshot 只暴露 `ActiveHitNodeIds / ActivePlayerMotionIds`。
- Armor 增加 `Uninterruptible` capability 入口，PlayerCombatReceiver 使用通用 reaction armor 判断受击是否打断。
- `CombatTimelineActionAsset.TotalDuration` 由 `TotalFrames / FrameRate` 派生。Combat Timeline Editor 修改 `Duration / Total Frames / Frame Rate` 时不重采样、不平移、不裁剪已有 clip 的秒级 `StartTime / EndTime`；`FrameRate` 变化保持当前秒级总时长并重算 `TotalFrames`，clip 起止帧号只作为显示值随当前帧率重新计算。
- Animation Preview 允许超出动作总时长；非 Animation Preview clip 仍严格受总时长约束。多 Animation Preview 预览选择顺序为选中 clip > 最近成功播放 clip > 当前可见顶层 clip，选中但未绑定 Preview Clip 时明确 warning 且不静默 fallback。
- Reaction Timeline 的资产级字段只保留 Reaction 类型；恢复节奏唯一来源是 `Reaction_Stun / Reaction_Recovery / Reaction_CanReturn` 窗口，Player Motion 来源是 Reaction / Motion clip，动画预览唯一来源是 `Animation Preview` Track。`CombatReactionTimelineAsset` 不再保存资产级 direction、timing、motion 或 preview fallback 字段。
- 当前 Demo 不再支持缺 Combat Timeline 资产兼容运行；缺关键 Timeline / Capability 时记录错误并拒绝动作或退回安全状态，不补默认窗口、默认 HitNode、默认 Reaction 或默认 BossAttack。RuntimeSpec / Validation 的最小测试必须覆盖 capability active/inactive、HitNode id 激活和缺必需能力报错。
- Combat Timeline Editor 不再提供 默认导入入口，默认模板资产和导入器已删除，当前 Demo 资产直接维护。
- Guard 专属链式完美防御语义修正：`Defense_PerfectGuardChainInput` 改为 `Input_PerfectGuardChain`；`Defense_PerfectGuardChainActive` 不再是 Timeline Clip / Snapshot capability，而是 Guard 状态在重按 Guard 后打开的动态窗口。`Perfect Guard` Track 另有独立 `Cancel_ToGuardRelease`，只控制松手进入真实 Release，不直接开放动作。
- GuardHit 后重新精防不再使用专用 Timeline 输入窗口；`Input_GuardHitPerfectGuardRetry` 已删除。GuardHit Track 只保留反应 Marker 与 `Cancel_ToGuardRelease`，不允许 Evade / Skill Buffer 或 Evade / Skill / AttackReset Cancel；所有动作派生由 PlayerGuardState 的真实 GuardRelease 阶段处理。
- Combat Timeline Editor 的全局 Clip 快捷键必须尊重 IMGUI 文本焦点：Inspector 文本 / 数值字段拥有焦点时，复制、剪切、粘贴、全选和删除交还给字段处理，不触发 Timeline Clip 复制 / 粘贴 / 删除；Timeline 资产结构和 Clip Inspector 修改必须接入 Unity Undo。
- Player Dead Reaction Timeline 是死亡动画预览 / 标识资产，不承载 `Reaction_Stun / Reaction_Recovery / Reaction_CanReturn` 功能窗口；Dead 生命周期由 Player 状态机和动画桥接处理，Validation 不应因缺恢复窗口报错，但如果 Dead 资产配置恢复 / 取消窗口仍应提示。
- Reaction Timeline Config 现在保留 `RuntimeSpec`，Reaction 状态可按帧读取 CapabilitySnapshot；Player HitReaction / Knockdown 的恢复接动作许可由 Cancel capability 表达，Phase 仍只做动画 / 调试展示。Knockdown 的移动返回复用通用 `Cancel_MovementReturn`，不新增 Reaction 专用窗口类型。
- BossReaction Timeline 可通过 `BossCodeMove` motion reference 暴露 Boss motion action/window 引用；Combat Timeline 只保存引用和校验缺失字段，不读取 `BossMotionWarpProfile`、不执行位移。
- Combat Timeline 使用 Boss 专用 `BossReactionGateWindowClip`，用 `AttackTypes / Policy / BlockedOutcome` 表达 Boss 状态级受击门控；RuntimeSpec 通过 `EvaluateBossReactionGate(attackType, outcome, elapsed)` 查询，不把 Boss gate 塞进 CapabilitySnapshot。旧 `CombatInterruptWindowClip / CombatInterruptWindow / CanBeInterruptedBy()` 已删除。
- `Reaction_Boss_HitStagger.asset` 使用 `BossReactionGateWindowClip` 在 0.1s - 1.0s 开放 Light / Heavy 普通受击刷新；Timeline 不保存连续受击次数或反制 AI 意图，防锁死预算由 BossActor / BossTacticalMemory 控制。
- HitNode 的 `CombatReactionIntent` 必须显式配置为 `HitReaction / Knockdown / DamageOnly`；`None` 是无效打点，Validation 报 Error。`CombatAttackType` 只表达来源通道，不能再作为反应结果推导来源。
- `CombatTimelineProvider` 缓存转换后的 runtime data，避免热路径重复 `BuildRuntimeSpec()`。Play Mode 中 Timeline 资产修改不会自动刷新运行时数据，必须通过 Combat Timeline Editor 的 `Refresh Runtime` 手动重建缓存。
- Combat Timeline Editor 支持 Play Mode `Follow Runtime`：只在当前选中 Timeline `ActionId` 匹配 Player / Boss 运行时播放快照时，用运行时 elapsed 更新 playhead；该功能是编辑器观察工具，不改变 Timeline 资产、Provider cache 或战斗语义。
- BossAttack Timeline 不再保存 `Selection / MotionMode`；BossAttack Runtime Definition 也不保存 motion mode。Boss 出招距离、空间预筛选和位移模式统一由 `BossMotionWarpProfile` 维护。
- `Raven_MoveCombo.asset` 与 `Raven_MoveChainCombo.asset` 是普通 BossAttack Timeline 资产，只保存 HitNode、BossReactionGate、MotionProfile 窗口引用和 Animation Preview；侧移 / Orbit 的具体移动参数仍归属 `RavenBossMotionWarpProfile.asset`。
- `Raven_ChaseGrab.asset` 与 `Raven_SlashCombo.asset` 是普通 BossAttack Timeline 资产。黄光不可格挡不新增 Combat Timeline 字段或 clip 类型，只通过现有 HitNode 的 `CanBeGuarded=false / CanBePerfectGuarded=false / CanBePerfectEvaded=true` authoring 组合表达。

## 近期变更入口

- 2026-09-08：删除 `CombatTimelineCapabilityDescriptor / CombatTimelineCapabilityWindow` 的 `Priority`，并清理无消费者的 RuntimeSpec 查询门面与公开集合属性；`TryGetBestWindow` 收敛为 `TryGetEarliestWindow`。正式保留 `Evaluate / HasCapability / GetWindows / ToHitNodeData / EvaluateBossReactionGate` 等现有消费入口，玩法和资产数据不变。Unity 定向回归 7/7 通过，完整夹具 63/64；唯一失败为既有 PlayerCombatReceiver 资源状态断言，与本次改动无关。

- 2026-08-05：Skill1 Motion Track 新增 `0.50～0.80 / 0.90～1.18 / 1.65～1.98s` 三个 `Skill1Forward` 引用；RuntimeSpec/Snapshot 新增 PlayerMotion 窗口求值，具体吸附参数继续只在 PlayerActionMotionProfile。

- 2026-07-27：`Reaction_Player_Knockdown.asset` 新增 `Cancel_MovementReturn 4.0～5.0s` 并接入运行时；Snapshot 的 `CanMoveCancel` 只在窗口内许可实时 MoveInput 返回 Locomotion，不改变 Reaction Phase、动作取消窗口或输入缓存语义。

- 2026-07-17：删除 `Boss Animation` Track、`BossAnimationStateClip` 和运行时动画段转换。BossAttack / BossReposition 只以资产级 `AnimationStateName` 指定起手状态，后续动画衔接不再属于 Combat Timeline；Animation Preview 继续只负责编辑器采样。BossReposition Validation 要求 `AnimationStateName` 且禁止 HitNode。
- 2026-07-16：Combat Timeline 增加 `BossReposition` ActionKind；Reposition 不创建攻击实例且禁止 HitNode。
- 2026-07-16：BossAttack 移除序列化 Weight / RecoveryTime，改为 `NaturalRecoveryDuration` 与 Timeline 正确破解奖励规则。Detached HitNode 在 PlayMode Validation 中检查 `BossDetachedAttackEmitter` 人工绑定，不再错误要求骨骼 Anchor。

- 2026-07-15：Perfect Guard Track 新增 `Cancel_ToGuardRelease`（`0.2167s - 0.50s`）。`PlayerGuardActionData` 增加按 `Perfect Guard` Track 隔离解析的 `PerfectGuardCancelData`，避免与 GuardHit 同名 capability 混读；Validation 强制要求该 Release 窗口并拒绝 PerfectGuard 动作 Buffer / Cancel。

- 2026-07-15：GuardHit 取消能力收敛到真实 GuardRelease。`Guard.asset` 的 GuardHit Track 删除 `Cancel_ToEvade / Cancel_ToSkill / Cancel_AttackReset`，只保留反应 Marker 与 `Cancel_ToGuardRelease`；Validation 禁止在 GuardHit 配置 Evade / Skill Buffer 及三类动作 Cancel。公共 Capability 枚举与 `PlayerGuardReactionCancelData` 保留，继续按 GuardHit / Release 轨道隔离解析。

- 2026-07-09：新增 Raven BossAttack Timeline 资产 `Raven_MoveCombo.asset` 与 `Raven_MoveChainCombo.asset`。`Raven_MoveCombo` 配置 3 个 Weapon HitNode，`Raven_MoveChainCombo` 配置 4 个 Weapon HitNode；两者的 Motion 轨只引用 `RavenBossMotionWarpProfile` 中同名 CodeMove 窗口，不复制位移数值。本阶段不修改 Combat Timeline runtime/API。
- 2026-07-10：新增 Raven 黄光不可格挡 BossAttack Timeline 资产 `Raven_ChaseGrab.asset` 与 `Raven_SlashCombo.asset`。`ChaseGrab_Impact` 与 `SlashCombo_YellowIai` 使用 `CanBeGuarded=false / CanBePerfectGuarded=false / CanBePerfectEvaded=true`，前者为中远距离追击下砸，后者为四连普通横斩后的黄光居合；本阶段不修改 Combat Timeline runtime/API。
- 2026-07-01：GuardHit 后重新精防改为显式 Release 窗口。删除 `Input_GuardHitPerfectGuardRetry`、相关 runtime data 和 Validation；`Guard.asset` 的 GuardHit 轨道新增 `Cancel_ToGuardRelease`，默认 0.18s - 0.83s，只有该窗口内松开 Guard 才进入真实 GuardRelease。
- 2026-07-02：清理 BossAttack Timeline 旧位移字段。删除 `BossAttackTimelineAsset.Selection / MotionMode`，`BossAttackDefinition` 不再携带 motion mode；Raven BossAttack Timeline 资产移除旧序列化键。Combat Timeline 继续只保存攻击语义、HitNode、Gate、冷却、权重、角度和恢复时间，位移模式与出招距离归属 `BossMotionWarpProfile`。
- 2026-07-13：删除 Reaction 资产级 fallback。`CombatReactionTimelineAsset.ConfigureReaction()` 只接收 Reaction 类型，Reaction 资产 YAML 清理旧 `reactionDirection / stunDuration / recoveryStartTime / canReturnTime / reactionMotionId / previewAnimationClip / clipStartOffset / clipSpeed / loopPreview` 键；运行时 timing / motion / preview 分别只读取 Reaction、Motion 和 Animation Preview 窗口。
- 2026-07-15：Combat Timeline 全局帧模型编辑改为保留 clip 秒级窗口。`CombatTimelineActionAsset.SetFrameRatePreserveDuration()` 替代旧的保持总帧数路径；编辑器调整 Frame Rate 时保持当前 Duration 秒值并重算 TotalFrames，调整 Duration / TotalFrames 时也不改写已有 clip 的 Start / End。普通 clip 超出新总时长时继续由 Validation 报错，不在编辑器中静默截断数据。

- 2026-07-01：清理 Boss 受击门控迁移期旧路径。删除旧 `CombatInterruptWindowClip`、`CombatInterruptWindow`、`CanBeInterruptedBy()` 和 `Armor_SkillInterruptArmor`，`BossReactionGateResult.BlockedBySkillInterruptArmor` 改为 `BlockedByBossReactionGate`；Combat Timeline Editor 的 Interrupt 轨只提供 `Boss Gate / Allow Light Heavy Skill` 与 `Boss Gate / Block Skill Knockdown`，Raven BossAttack 资产中的空 `Skill Armor` 轨已删除，BossAttack / BossReaction 不再配置 Armor 轨。

- 2026-07-01：Combat Timeline Editor 新增运行时 playhead 跟随。`PlayerStateMachine / BossActor` 暴露只读 `CombatTimelineRuntimePlaybackSnapshot`，Editor 工具栏新增 `Follow Runtime` 按钮；Play Mode 中选中资产与当前运行时 `ActionId` 匹配时，白色 playhead 跟随当前状态 / 招式 elapsed，否则保持等待提示。

- 2026-06-30：实施 Combat Timeline Runtime Cache。Provider 保留资产索引，并新增 Player / Boss typed runtime cache；`ResetRuntimeDataCache()` 只清转换结果，`RebuildRuntimeDataCache()` 预热全部已加载 Timeline，Editor 工具栏新增 `Refresh Assets` 与 `Refresh Runtime`。

- 2026-06-30：为 `Reaction_Boss_HitStagger.asset` 添加普通攻击刷新 Gate。Light / Heavy 在 0.1s - 1.0s 可刷新 Boss HitStagger；连续刷新预算、`ReactionPressureLimitReached` 和 Brain 压力响应属于 Boss 运行时语义，不写入 Timeline。

- 2026-06-30：实施 Combat 命中语义收敛。`CombatAttackType` 删除旧击倒 / 破防效果值，只保留 `LightAttack / HeavyAttack / SkillAttack` 来源语义；`CombatReactionIntent` 改为 `None / HitReaction / Knockdown / DamageOnly`；`CombatHitOutcome` 新增 `DamageOnly`。`CombatHitResolver` 不再从攻击类型推导 Knockdown，`ReactionIntent=None` 不造成伤害。Skill1 前两段 HitNode 迁移为 `DamageOnly`，第三段保持 `Knockdown`。

- 2026-06-30：新增 Boss 受击门控窗口。`BossReactionGateWindowClip` 支持在 Boss Attack / Boss Reaction Timeline 上配置 `AllowInterrupt` 或 `BlockInterrupt`，首版 `BlockedOutcome` 支持 `Any / Knockdown`；Editor 新增 `Boss Gate / Allow Light Heavy Skill` 和 `Boss Gate / Block Skill Knockdown` 添加项，并支持 Inspector、复制粘贴、Undo 和空攻击类型 Validation warning。RuntimeSpec 新增 `BossReactionGateWindows` 与 `EvaluateBossReactionGate()`；Block gate 优先于 Allow gate。

- 2026-06-29：Boss HitStagger / Knockdown Reaction Timeline 接入 `BossCodeMove` 引用；`CombatTimelineReactionConfig` 增加 `BossMotionActionId / BossMotionWindowName / HasBossCodeMove`，`CombatReactionTimelineAsset.ToReactionConfig()` 从 `CombatMotionReferenceClip(MotionKind=BossCodeMove)` 读取引用。Validation 对 BossReaction 中缺 `BossAttackId` 或 `ProfileWindowName` 的 BossCodeMove clip 报错，但仍不校验 MotionWarpProfile 资产内容。

- 2026-06-29：Player HitReaction Timeline 按 Knockdown 同构整理为 Phase / Reaction / Cancel 分轨，`PlayerHitReactionState` 改为按 RuntimeSpec Snapshot 读取 Phase 与 Cancel capability。

- 2026-06-29：Player Knockdown Timeline 增加 Phase / Reaction / Cancel 分轨语义，起身取消改由 `Cancel_ToEvade / Cancel_ToSkill / Cancel_ToGuard / Cancel_AttackReset` 控制。

- 2026-06-29：Player Dead Timeline 收敛为无功能窗口的死亡预览资产；`CombatReactionTimelineAsset.ToReactionConfig()` 与 Reaction Validation 对 Dead 不再要求 Stun / Recovery / CanReturn。

- 2026-06-29：Combat Timeline Editor 修复 Inspector 复制粘贴误触顶层 Clip 粘贴，并补齐 Clip Inspector、删除、粘贴、拖拽等编辑路径的 Unity Undo / dirty 边界。

- 2026-06-24：Combat Timeline Editor 多 Animation Preview 采样优先级修正为选中 clip > 最近成功播放 clip > 当前可见顶层 clip。

- 2026-06-24：新增 RuntimeSpec / HitNode / Validation 最小 EditMode 测试护栏，作为后续 Editor 拆分前的行为基线。

- 2026-06-24：HitNode 数据模型收敛，删除 CombatTimelineHitNodeData / CombatTimelineActiveHitNode / BossAttackHitNode。

- 2026-06-24：`CombatHitNodeData` 和 `PlayerSkillActionData` 拆为独立文件，保持 `ProjectEVE.Combat.Timeline` namespace 与现有 RuntimeSpec / Provider API 不变。

- 2026-06-23：Combat Timeline Empty/Default 玩法兜底 清理。

- 2026-06-23：Combat Timeline Channel/Target 维度清理，保留 CapabilityKind 仅作窗口族分组。

- 2026-06-23：Combat Timeline 显式 CapabilityId 迁移，删除旧窗口角色字段与兼容查询。

- 2026-06-23：Combat Timeline 状态消费层简化为 State-local FrameData。
- 2026-06-23：Combat Timeline 全状态基础迁移，覆盖 PlayerSkill / PlayerEvade / PlayerGuard / Reaction / BossAttack。
- 2026-06-23：Combat Timeline RuntimeSpec / CapabilitySnapshot 最小实现与 PlayerAttack 试点迁移。
- 2026-06-23：Combat Timeline 窗口多态与职责边界细化。
- 2026-06-23：Combat Timeline 架构草案与窗口能力分层。
- 2026-06-23：Combat Timeline 总时长改为 TotalFrames / FrameRate 派生。
- 2026-06-23：PlayerAttack Timeline 数据源去硬编码。
- 2026-06-23：Animation Preview Clip 溢出显示与源区间编辑。
- 2026-06-22：战斗时间轴数据整理第一步。

## 关联文档与代码

- `doc/Combat Timeline 架构设计.md`
- `doc/主角状态机详设/Attack 状态需求设计.md`
- `Assets/Runtime/Combat/Timeline`
- `Assets/Editor/ProjectEVE/CombatTimeline`

## 遗留风险

- RuntimeSpec / CapabilitySnapshot 已扩展到 PlayerSkill、PlayerEvade、PlayerGuard、Reaction 与 BossAttack。
- 旧资产缺失能力窗口的兼容路径已删除；后续新增动作必须先补齐 Timeline 资产和关键 Capability。
- Editor 交互仍需人工验证。
