# Boss AGENTS.md

本文件约束 `Assets/Runtime/Boss` 下的 Boss AI、攻击执行和位移代码。根目录 `AGENTS.md` 仍然适用。

## 职责边界

- Boss AI 负责出招选择、节奏、距离和状态切换。
- BossAttackDefinition 保存招式静态数据和 RuntimeSpec 引用，不执行命中结算。
- BossAttackExecutor 执行当前招式的 HitNode 检测和发出命中数据，不拥有出招选择逻辑。
- BossMovementSystem 是正式移动请求入口；BossMotionController / BossMotionWarpProfile 负责底层位移执行和位移参数；不要把完整 motion 参数复制到 BossAttackDefinition。

## 数据模型规则

- Boss 不新增专属 HitNode runtime 数据类。Boss 与 Player 共用 `CombatHitNodeData`。
- HitNode 的完整语义来自 Combat Timeline Provider Data / BossAttackDefinition；当前帧激活只由 RuntimeSpec / Snapshot 表达。
- Boss 是否进入受击反应由当前状态 Timeline 的 `BossReactionGateWindow` 决定；不要在 Boss 代码里按 Attack / Recovery / HitStagger / Knockdown 写死打断规则，也不要恢复旧 `SkillInterruptArmor` 或 `CanBeInterruptedBy()` 作为 Boss gate。
- Boss 接收的攻击类型必须使用通用 `CombatAttackType` 的玩家攻击来源语义，例如 `LightAttack / HeavyAttack / SkillAttack`，不要新增 Boss 专属攻击类型。
- 不要因为 Boss 消费者不同而复制 Player 已有字段。
- Boss 命中去重保持 `AttackInstanceId + HitNodeId + TargetHurtbox` 粒度。

## 新增功能规则

- 新增 Boss 普通攻击优先补 Timeline 资产、BossMotionWarpProfile、BossActionSet 和必要动画绑定，不修改 Brain / Runner / Combat 框架。
- 特殊提示攻击、抓取、处决、演示动作先使用 `BossActionKind` 表达动作类型；只有真正实现玩法时才增加对应执行器，不提前添加空系统。
- 缺 BossAttack Timeline 或关键 Capability 时记录错误并不出招，不补默认攻击。
- 新增 Boss 攻击能力前，先检查 Player/Boss 是否已有通用 Combat 数据结构可复用。

## 可读性规则

- Actor、Definition、Runner、Executor、Motion、Combat 之间不要互相复制字段作为“方便访问”的缓存。
- 如果一个字段只是另一个配置源的镜像，优先删除镜像并从唯一数据源读取。
