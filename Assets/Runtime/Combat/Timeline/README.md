# Combat Timeline Runtime

本目录是 Combat Timeline 的运行时数据层，负责把强类型 Timeline 资产转换为 Player / Boss 状态机可消费的数据。

## 职责

- 定义 `CombatTimelineActionAsset` 和各类强类型动作资产。
- 定义 Phase、Input、Cancel、HitNode、Defense、Armor、Motion、Reaction、Animation Preview、Marker 等 clip 类型。
- 通过 `CombatTimelineProvider` 查询 Timeline 资产并缓存转换后的运行时定义。
- 通过 `CombatActionRuntimeSpec / CombatActionCapabilitySnapshot` 把 authoring clips 归一化为显式 `CombatTimelineCapabilityId` 能力；Snapshot 只返回激活 HitNodeId，完整 HitNode 详情由唯一运行时数据 `CombatHitNodeData` 保存在 Provider Data 中。
- 通过 `CombatTimelineValidation` 校验动作资产是否满足运行时必需窗口。

## 当前架构方向

- Timeline 是“动作能力时间线”，不是状态类分支集合。
- Editor / Asset 层可以使用多态 clip。
- PlayerAttack、PlayerSkill、PlayerEvade、PlayerGuard、Reaction、BossAttack 与 BossReposition 已进入主路径。Boss 动作运行时只读取资产级 `AnimationStateName` 作为起手状态；Animation Preview 只服务编辑器采样。
- Provider 会缓存 `RuntimeSpec / Provider Data`；Play Mode 中修改 Timeline 后需通过 Combat Timeline Editor 的 `Refresh Runtime` 手动重建运行时缓存。
- Phase 只用于阅读、动画桥接和 Debug；功能判断读取 capability。
- `Duration / Total Frames / Frame Rate` 只定义 Timeline 的全局采样模型；编辑器调整这些字段时不重采样 clip，clip 的秒级 `StartTime / EndTime` 保持为窗口事实，帧号显示按当前 `FrameRate` 换算。
- `CombatReactionTimelineAsset` 只保存 Reaction 类型；恢复节奏来自 `Reaction_Stun / Reaction_Recovery / Reaction_CanReturn` 窗口，Player Motion 来自 `CombatReactionWindowClip.ReactionMotionId` 或 Player Motion 引用，预览只来自 `Animation Preview` Track。
- Player Reaction 的恢复派生由显式 Cancel capability 控制；Knockdown 可使用通用 `Cancel_MovementReturn` 开放实时移动输入返回 Locomotion，Phase 本身不授予移动许可。
- BossReactionGateWindow 只表达 Boss 当前状态在某个时间段是否开放反应；连续受击预算、压力上限和恢复反制属于 BossActor / BossBrain 运行时规则，不写入 Timeline。
- BossAttack Timeline 保存 HitNode、起手动画状态名、自然恢复和正确破解奖励；不保存 AI Weight 或运行时动画分段。BossReposition 继续作为资产数据语义存在且不允许 HitNode，但运行时与 BossAttack 共用 `BossStateId.Action` 和 `BossActionRunner.StartAction / Tick / EndAction`。Detached HitNode 只声明战斗来源，Prefab、发射挂点、速度与生命周期由 Boss 场景组合层人工绑定。

## 边界

- 不直接移动 Player 或 Boss。
- 不直接播放动画或 VFX。
- 不保存 Boss MotionWarp 具体位移数值；Boss 位移参数仍由 `BossMotionWarpProfile` 维护。

## 相关文档

- `doc/长期记忆/CombatTimeline.md`
- `doc/需求变更/CombatTimeline.md`
- `doc/Combat Timeline 架构设计.md`
