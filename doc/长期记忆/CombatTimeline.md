# Combat Timeline 长期记忆

更新时间：2026-09-08

## 当前状态

- 2026-09-08：`CombatActionRuntimeSpec` 已删除未产生业务效果的通用 `Priority` 数据链。Capability 窗口只保存 `CapabilityId + StartTime + EndTime`；同类布尔窗口在 `Evaluate()` 中按“任一激活即 true”合并，取消输入、防御、Armor 与 Boss Gate 的冲突顺序继续由各自业务消费者决定。Reaction 单窗口时间锚点改用语义明确的 `TryGetEarliestWindow()`。同时删除无调用的 `TryGetWindow / GetActiveWindows / TryResolvePhase / RuntimeSpec.TryGetHitNode` 和未使用公开集合/元数据属性，HitNode/Motion 激活查询收为 RuntimeSpec 私有实现，不改变 Timeline 资产或战斗表现。Unity 脚本校验与 Console 为 0 error，定向 RuntimeSpec/Reaction/Guard/BossGate 用例 7/7 通过；完整夹具 63/64，通过外唯一失败是既有 PlayerCombatReceiver 测试预期 PerfectGuard、实际 Dead，与本次窗口 API 无关。
- 2026-08-05：RuntimeSpec 现会解析 `CombatMotionReferenceClip` 的 PlayerMotion 窗口，并通过 `CombatActionCapabilitySnapshot.ActivePlayerMotionIds` 暴露当前激活的只读 ID；Snapshot 不复制 Profile 参数。Skill1 使用三个 `Skill1Forward` 窗口驱动分段吸附，状态在每个窗口进入沿只请求一次。
- 2026-07-27：`Cancel_MovementReturn` 已用于 Player Knockdown 起身移动取消。`PlayerKnockdownState` 通过 Reaction RuntimeSpec Snapshot 读取 `CanMoveCancel`；当前资产窗口为 `4.0～5.0s`，只许可实时 MoveInput 返回 Locomotion，不新增专用 capability 或输入缓存。
- 2026-07-16：Combat Timeline Editor 的 Validation 区改为在 Layout 事件冻结消息快照，避免校验数据在 Repaint 前变化造成 GUILayout 控件数量不一致异常。

- 2026-07-17：删除 Boss Animation Track、Clip 类型与运行时动画段数据。BossAttack / BossReposition 只读取资产级 `AnimationStateName` 作为起手 Animator 状态，后续衔接归 Animator Controller；Animation Preview 仍只用于编辑器采样。BossReposition Validation 改为要求 `AnimationStateName` 且禁止 HitNode。13 套 Raven Timeline Validation 0 error，相关 PlayMode 7/7 通过。
- 2026-07-16：BossAttack 新增 `NaturalRecoveryDuration` 和数据驱动正确破解奖励；序列化单一 Weight 已删除，阶段权重只归 BossActionSet。新增 `BossReposition` ActionKind，Validation 禁止其包含 HitNode。
- 2026-07-16：BossAttack HitNode 支持 `SourcePart=Detached`。Timeline 只保存攻击时序与战斗语义；独立判定 Prefab、挂点、速度、方向和生命周期由 Boss 场景组合层人工绑定，VFX 不反向决定命中。

- Combat Timeline 已从旧通用大资产整理为强类型 `CombatTimelineActionAsset` 体系。
- 当前具体资产类型包括 PlayerAttack、PlayerSkill、PlayerEvade、PlayerGuard、PlayerReaction、BossAttack、BossReaction。
- `CombatTimelineTrack` 通过 `[SerializeReference]` 保存强类型 clip。
- `CombatTimelineProvider` 是运行时查询入口，Combat Timeline 资产是 Player / Boss 战斗数据唯一来源。
- `CombatTimelineProvider` 同时缓存 Timeline 资产索引和转换后的 runtime data；首次查询某个动作会构建 `RuntimeSpec / Provider Data`，后续查询复用同一份解析结果。
- PlayerAttack 运行时窗口已去硬编码，起手节点由资产中的 `AttackInputType + ComboIndex == 1` 决定。
- Editor 第一版已支持多轨时间轴、拖动、拖边、Frames/Seconds、Animation Preview、Validation；多 Animation Preview 播放优先级为选中 clip > 最近成功播放 clip > 当前可见顶层 clip。
- 已新增最小 `CombatActionRuntimeSpec / CombatActionCapabilitySnapshot` 只读解析层，当前通过显式 `CombatTimelineCapabilityId` 输出 Capability。
- PlayerAttack 已作为试点从 RuntimeSpec 生成 `AttackDefinition`，运行时窗口和 Phase 通过 CapabilitySnapshot 消费。
- 2026-06-23 已将 PlayerSkill、PlayerEvade、PlayerGuard、Reaction config 与 BossAttackDefinition 的运行时消费迁移到 RuntimeSpec / CapabilitySnapshot 主路径。
- RuntimeSpec 已支持多窗口查询、ActiveHitNodeIds、ActivePlayerMotionIds、Reaction CanReturn、PerfectGuardChain 与 Armor/Uninterruptible 快照；HitNode 详情统一为 `CombatHitNodeData`，不再拆 Player/Boss 专用数据类。
- 2026-06-23 已删除公共状态视图层，PlayerSkill、PlayerEvade、PlayerGuard 和 PlayerAttack 改为状态本地 FrameData 消费 RuntimeSpec Snapshot。
- Combat Timeline Editor 添加菜单和 Inspector 已围绕窗口族 / CapabilityId 展示，不再保留旧窗口角色字段。
- 2026-06-29 Combat Timeline Editor 已修复 IMGUI 快捷键焦点隔离：右侧 Inspector 文本 / 数值字段复制、剪切、粘贴、全选和删除不再触发 Timeline Clip 复制 / 粘贴 / 删除；Clip Inspector 修改、删除、粘贴、添加、拖拽 / 缩放等资产编辑路径统一记录 Unity Undo 并刷新 dirty / Validation。
- 2026-06-29 Player Dead Timeline 已收敛为死亡动画预览 / 标识资产，不再配置 `Reaction_Stun / Reaction_Recovery / Reaction_CanReturn`；Dead 生命周期由 Player 状态机和动画桥接处理，Reaction Validation 对 Dead 不要求恢复窗口。
- 2026-06-29 Reaction Config 已携带 `CombatActionRuntimeSpec`；Player HitReaction / Knockdown 不再把 Reset 阶段当作动作许可，改为读取显式 Cancel capability。Knockdown 现额外消费 `Cancel_MovementReturn` 作为起身移动返回许可。
- 2026-07-13 Reaction Timeline 删除资产级 fallback；`CombatReactionTimelineAsset` 只保存 Reaction 类型，不再保存 direction、stun / recovery / can-return timing、reaction motion 或 preview clip / offset / speed / loop 字段。Reaction 节奏来自 Reaction capability 窗口，Player Motion 来自 Reaction / Motion clip，预览只由 `Animation Preview` Track 的 `CombatAnimationClipWindow` 提供。
- 2026-07-15 Combat Timeline Editor 调整全局帧模型编辑语义：修改 `Duration / Total Frames / Frame Rate` 不重采样、不平移、不裁剪 Timeline 内 clip 的 `StartTime / EndTime` 秒值；帧率变化保持当前秒级总时长并重算 `TotalFrames`，起止帧号只作为当前帧率下的显示结果同步变化。普通 clip 若因总时长缩短而越界，数据保持原样并由 Validation 报错。
- 2026-06-29 BossReaction Timeline 已能通过 `BossCodeMove` motion reference 暴露 Boss motion action/window 引用；`CombatTimelineReactionConfig.HasBossCodeMove` 只表达引用存在，真正位移仍由 `BossActor -> BossMovementSystem -> BossMotionController -> BossMotionWarpProfile` 执行。
- 2026-07-01 Boss 受击门控清理完成：`BossReactionGateWindowClip.AttackTypes / Policy / BlockedOutcome` 是 Boss 进入反应的唯一 Timeline gate，`CombatActionRuntimeSpec.EvaluateBossReactionGate(attackType, outcome, elapsed)` 返回 `Allowed / BlockedByClosedWindow / BlockedByBossReactionGate`。旧 `CombatInterruptWindowClip`、`CombatInterruptWindow`、`CanBeInterruptedBy()` 与 `Armor_SkillInterruptArmor` 已删除；BossAttack / BossReaction 资产不再配置 Armor 轨，保留非 Boss 通用 `Armor_SuperArmor / Armor_Uninterruptible`。
- 2026-06-30 Combat HitNode 反应语义已显式化：`CombatAttackType` 只表达攻击来源通道，`CombatReactionIntent` 只表达作者配置的 `HitReaction / Knockdown / DamageOnly`，`CombatHitOutcome` 只表达 Resolver 输出。`CombatHitNodeClip` 默认 `ReactionIntent=HitReaction`，Validation 会把 `ReactionIntent=None` 报为 Error；Skill1 前两段 HitNode 为 `DamageOnly`，第三段为 `Knockdown`。
- 2026-06-30 Combat Timeline Provider 已新增 runtime data cache：`TryGetPlayerAttack / Skill / Evade / Guard / Reaction` 和 `TryGetBossAttack / Reaction` 不再每次重新 `BuildRuntimeSpec()`；Play Mode 中修改 Timeline 后需在 Combat Timeline Editor 点击 `Refresh Runtime` 才会清理并重建 runtime cache。
- 2026-06-30 `Reaction_Boss_HitStagger.asset` 已新增 Light / Heavy Allow gate，时间为 0.1s - 1.0s，用于允许普通攻击在 HitStagger 前段刷新 Boss 受击。连续刷新上限不属于 Timeline 数据，而由 BossActor 的压力预算控制。
- 2026-07-01 Combat Timeline Editor 已新增 Play Mode `Follow Runtime` 按钮：窗口打开且选中 Timeline 的 `ActionId` 与 Player / Boss 当前运行时动作匹配时，白色 playhead 会跟随 `StateElapsedTime` / `AttackElapsed` 移动；不匹配时只显示等待状态，不切换选中资产也不影响玩法。
- 2026-07-02 BossAttack Timeline 删除旧 `Selection / MotionMode` 字段；BossAttack Runtime Definition 只保存 Timeline 语义、HitNode、Gate、冷却、权重、角度和恢复时间。Boss 攻击位移模式与出招距离统一由 `BossMotionWarpProfile` 维护。
- 2026-07-09 新增 BossAttack Timeline 资产 `Raven_MoveCombo.asset` 与 `Raven_MoveChainCombo.asset`。二者继续遵守 BossAttack Timeline 只保存 HitNode、BossReactionGate、MotionProfile 引用和 Animation Preview 的规则；具体 CodeDrivenWarped 位移数值仍只在 `RavenBossMotionWarpProfile.asset` 中维护。
- 2026-07-10 新增 BossAttack Timeline 资产 `Raven_ChaseGrab.asset` 与 `Raven_SlashCombo.asset`。黄光不可格挡不新增 Timeline clip 类型或 Combat 字段，仍由 `CombatHitNodeClip` 的 `CanBeGuarded=false / CanBePerfectGuarded=false / CanBePerfectEvaded=true` 表达；Motion 轨只引用 `RavenBossMotionWarpProfile.asset` 中同名窗口。

- 2026-06-24 已收敛 HitNode 数据模型：保留 `CombatHitNodeClip` 作为 authoring clip，运行时唯一 HitNode 数据为 `CombatHitNodeData`，Snapshot 只返回 `ActiveHitNodeIds`；`CombatTimelineHitNodeData`、`CombatTimelineActiveHitNode`、`BossAttackHitNode` 已删除。`CombatHitNodeData` 已拆到独立文件，namespace/API 不变。
- 2026-06-24 已新增最小测试护栏：RuntimeSpec capability / ActiveHitNodeIds、Validation 缺必需能力报错、Animation Preview 溢出允许规则。

## 架构方向

- Combat Timeline 定位为“动作能力时间线”，不是状态类分支集合。
- 运行时已落地最小 `CombatActionRuntimeSpec / CombatActionCapabilitySnapshot`；当前由 `RuntimeSpec.Evaluate()` 直接完成能力合并，不保留独立 `CapabilityResolver` 预留层。
- Editor / Asset 层可以使用多态 clips；CapabilitySnapshot 作为底层归一化语言，状态机通过 Provider Data + State-local FrameData 消费。
- Phase 只用于阅读、动画桥接和 Debug，不作为功能判断主来源。
- `CombatTimelineWindowRole` 已删除；窗口语义由 `CombatTimelineCapabilityId` 表达，旧 Channel/Target 维度也已删除。

## 关键规则

- 窗口多态按窗口族拆分：InputBuffer、Cancel、HitNode、Defense、Armor、Motion、Reaction、Interrupt、Marker。
- 不为 EvadeCancel、SkillCancel、PerfectGuard 等每个具体能力单独建类。
- Guard 是带 Start / Loop / Reaction / Release 内部 mode 的持续状态，不强行套普通固定时长 Action 模型。
- Guard Timeline 中 `Perfect Guard` Track 保存 `PerfectGuardReaction` Marker、`Input_PerfectGuardChain` 与 `Cancel_ToGuardRelease`；`GuardHit` Track 保存 `GuardHitReaction` Marker 与 `Cancel_ToGuardRelease`。两个反应轨道的 Release 许可分别解析到 Provider Data，禁止通过全局 RuntimeSpec 混读同名 capability；`Defense_PerfectGuardChainActive` 静态 capability 已移除。
- 2026-07-15 GuardHit 轨道语义已收敛：`GuardHit` Track 只允许反应 Marker 与 `Cancel_ToGuardRelease`，Evade / Skill Buffer 和 Evade / Skill / AttackReset Cancel 只属于 `Release` Track；Validation 会拒绝 GuardHit 上的动作窗口，连续精防仍通过真实 GuardRelease 再进入 GuardStart。
- Dead Reaction Timeline 不参与受击恢复节奏；不要为了通过普通 Reaction 校验给 `Reaction_Player_Dead.asset` 添加 Recovery / CanReturn / Cancel 窗口。
- HitReaction 受击恢复取消和 Knockdown 起身取消都属于 Cancel capability，不属于 Phase；`Reaction_Player_HitReaction.asset` 与 `Reaction_Player_Knockdown.asset` 应保持 Phase / Reaction / Cancel 分轨，缺某个 Cancel 窗口表示对应动作不可用。Knockdown 的 `Cancel_MovementReturn` 只控制移动输入返回 Locomotion，不自动开放其他动作。
- HitNode 必须显式声明 `CombatReactionIntent`；攻击来源 `CombatAttackType` 不再推导击倒、破防或普通受击。
- Provider Data 是运行时静态数据缓存。运行中不会自动监听 Timeline 资产变更；Editor 侧通过 `Refresh Runtime` 手动应用已保存资产改动。
- Runtime Follow 只用于编辑器观察运行时进度：Player / Boss 通过 `CombatTimelineRuntimePlaybackSnapshot` 暴露只读 `ActionId + ElapsedTime`，Editor 只在选中资产匹配时更新 playhead，不参与状态切换、命中结算或 Timeline 缓存刷新。
- BossReaction 的 `BossCodeMove` clip 只保存 `BossMotionWarpProfile` action/window 引用；Timeline 校验缺字段，不直接读取 MotionWarpProfile 或生成默认位移。
- BossReaction 的 `BossReactionGateWindow` 只表达当前时间点是否开放反应，不表达连续受击预算、压力反制或 AI 意图；这些运行时节奏规则留在 BossActor / BossBrain。
- BossAttack Timeline 不保存 Boss 出招距离、空间预筛选或 motion mode；这些字段属于 `BossMotionWarpProfile.BossAttackMotionConfig`，Timeline 只通过 `ActionId` 与 MotionProfile 关联。
- BossAttack / BossReaction 不使用 Armor 轨表达受击门控；如果需要 Boss 反应门控，必须使用 `BossReactionGateWindowClip`。
- 黄光不可格挡仍是普通 HitNode authoring 组合，不是新窗口族：不要新增 YellowAttack clip、Unblockable 字段或 Boss 专属 HitNode 类型；Guard / PerfectGuard / PerfectEvade 能否成立继续由现有布尔位进入 `CombatHitResolver`。
- 总时长由 `TotalFrames / FrameRate` 派生，避免秒值和帧值分叉。编辑器修改 `Duration / Total Frames / Frame Rate` 时只改变 Timeline 的全局采样模型，不重采样 clip；clip 的 `StartTime / EndTime` 秒值是窗口事实，起止帧号由当前 `FrameRate` 即时换算。
- Animation Preview 可以超出动作总时长；非 Animation Preview clip 仍受总时长约束。拖动 playhead 预览时，选中的 Animation Preview clip 优先播放；没有选中时使用最近成功播放 clip，再 fallback 到当前可见顶层 clip。
- Reaction 资产不得恢复资产级 direction、timing、motion 或 Animation Preview fallback；缺少有效 `CombatAnimationClipWindow.PreviewAnimationClip` 时只提示编辑器预览不可用，不影响 Reaction 运行时节奏。

## 主要代码入口

- `Assets/Runtime/Combat/Timeline/CombatTimelineActionAsset.cs`
- `Assets/Runtime/Combat/Timeline/CombatTimelineTypes.cs`
- `Assets/Runtime/Combat/Timeline/CombatTimelineProvider.cs`
- `Assets/Runtime/Combat/Timeline/CombatTimelineValidation.cs`
- `Assets/Runtime/Combat/Timeline/PlayerAttackTimelineAsset.cs`
- `Assets/Runtime/Combat/Timeline/BossAttackTimelineAsset.cs`
- `Assets/Editor/ProjectEVE/CombatTimeline/CombatTimelineEditorWindow.cs`
- 默认导入器已删除；当前 Demo 直接维护 `Assets/ScriptableObjects/CombatTimelines` 资产。

## 下一步入口

- 为 Attack Uninterruptible 等新增窗口补 Timeline 资产数据，并继续保持 CapabilityId 显式语义。
- Play Mode 回归 PlayerSkill、PlayerEvade、PlayerGuard、Reaction、BossAttack 的 RuntimeSpec 消费路径。
- 继续补 Editor clone、时间钳制、拖动/拖边交互、Animation Preview 采样辅助的测试，再进入 Editor 拆分。
- Editor 后续交互验证应继续覆盖 Inspector 文本焦点快捷键、Clip copy/paste、Undo/Redo、Animation Preview 选中优先级和拖拽 / 拖边。

## 已知风险

- RuntimeSpec / CapabilitySnapshot 已扩展到 PlayerSkill、PlayerEvade、PlayerGuard、Reaction 与 BossAttack；玩法 兜底 已删除，缺关键 Timeline / Capability 时记录错误并拒绝动作或退回安全状态。
- Attack Uninterruptible 入口已在代码侧可用，但仍需要补资产窗口并做受击不断测试。
- Editor IMGUI 鼠标拖动、Inspector 快捷键焦点隔离、Undo/Redo 和 Animation Preview 采样仍需人工验证。

- 2026-06-23 已删除 CombatTimelineWindowRole，Timeline clip 和资产字段迁移为 CombatTimelineCapabilityId；RuntimeSpec、Validation、Editor 和 Importer 均以显式能力 ID 作为主语义。
- 2026-06-23 已删除无消费者的 Runtime CapabilityWindow 派生字段和 Motion/Marker 空 兜底；保留 CombatTimelineCapabilityKind 作为窗口族校验与标签分组。

- 2026-06-23 已删除 Combat Timeline Empty/Default 玩法兜底：移除 运行时默认窗口配置、空 RuntimeSpec 工厂、Skill/Boss/Reaction 硬编码默认数据、默认导入器和 PlayerAttack 默认模板目录。
