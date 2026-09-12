# Combat Timeline AGENTS.md

本文件约束 `Assets/Runtime/Combat/Timeline` 下的 Combat Timeline 运行时与数据结构实现。根目录 `AGENTS.md` 仍然适用。

## 核心边界

- Combat Timeline 是动作能力时间线，不是状态机替代品。
- 固定数据流：`ActionAsset / Clip -> Provider Data -> RuntimeSpec -> Snapshot -> State-local FrameData -> State / Hitbox / Movement / Receiver`。
- Provider Data 保存完整动作静态数据；RuntimeSpec 只做时间窗口求值；Snapshot 只表达当前帧激活结果；状态本地 FrameData 负责业务解释。
- 状态类不得直接遍历原始 Timeline clips。
- 不新增公共 RuntimeView facade，除非多个状态已经出现稳定、完全相同且难以维护的重复解释逻辑。

## 数据模型规则

- 同一战斗语义只能有一份 runtime 数据结构。
- 不要因为 Player/Boss、定义期/当前帧、Provider/Executor 消费者不同而复制字段相同的数据类。
- Snapshot 不保存完整静态数据。复杂数据只保存激活 ID 或索引，完整数据从 Provider Data / Definition 获取。
- HitNode 固定模型：`CombatHitNodeClip` 只用于 Unity authoring；`CombatHitNodeData` 是唯一 runtime HitNode 数据；Snapshot 只保存 `ActiveHitNodeIds`。

## 多态规则

- Clip 多态按窗口族拆分，不按能力名字拆分。
- 推荐窗口族：InputBuffer、Cancel、HitNode、Defense、Armor、Motion、Reaction、Marker。
- 不为 `EvadeCancel`、`SkillCancel`、`GuardCancel` 这类仅 `CapabilityId` 不同的能力新增子类。
- 新增子类的条件是字段结构、校验规则或生命周期确实不同。

## Fallback 规则

- 当前 Demo 中 Timeline 资产是 Player / Boss 战斗数据唯一来源。
- 禁止新增默认窗口、默认 HitNode、默认 Reaction、默认 BossAttack、Empty RuntimeSpec 等玩法 fallback。
- 缺关键资产或关键 Capability 时记录错误并拒绝动作或退回安全状态。
- `Array.Empty<T>()` 只允许作为集合 API 的安全空返回，不代表默认玩法行为。
