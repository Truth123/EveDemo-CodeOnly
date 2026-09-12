# Boss AI 目标架构设计

更新时间：2026-06-28
状态：项目级通用框架重定稿已落地 / BossActor 已成为唯一 MonoBehaviour 入口 / BossController 已删除 / ActionSet 动作池已接入 / 后续不再沿 Controller facade 收敛路线推进
维护者：Codex

本文档定义 Project EVE Demo 的 Raven 风格 Boss AI 目标架构。它只保留能指导评审和实现的内容：目标体验、模块边界、运行流程、数据归属、迁移顺序和少量待确认决策。

本文档不是商业通用 Boss 框架，也不再围绕旧 `BossController` 做逐行改造说明。当前目标是把项目落实为清晰、可读、可扩展的单 Boss 战架构：Actor 是入口，系统各自负责决策、状态、动作、移动、命中、反应和表现。

## 0. 当前重定稿

当前项目级通用框架固定为：

```text
BossActor
  -> Perception / Blackboard
  -> BossStateMachine
  -> BossBrain
  -> BossActionRunner
  -> BossMovementSystem
  -> BossCombatSystem
  -> BossReactionSystem
  -> BossPresentation
```

落地状态：

- `BossActor` 是 Boss 唯一 MonoBehaviour 入口，负责正式 Tick、外部启停、Root Motion、玩家命中、PerfectGuard 反制、目标事实和状态事实查询。
- `BossController` 已删除，不再保留 facade、兼容自驱动 Tick 或测试反射入口。
- 原 Controller 中仍有价值的职责已迁移到已有系统：状态事实和 Tick 决策归 `BossStateMachine`，Reaction Timeline 和 forced attack 配置读取归 `BossRuntimeConfigResolver`，反应决策和状态提交计划归 `BossReactionSystem`，攻击生命周期归 `BossActionRunner`，位移请求归 `BossMovementSystem`。
- `BossActionKind` 与 `BossActionSet` 已作为最小扩展模型落地。普通攻击、特殊提示攻击、抓取、处决、演示动作统一视为 Boss Action；当前只执行 `Attack`，未实现 Kind 只识别并拒绝执行。
- Demo 场景 Raven Boss 绑定 `RavenBossActionSet`，当前动作池为 `Raven_Slash / Raven_SlashChain / Raven_EvadeBackRush / Raven_ChaseCombo`。

后续扩展普通招式的原则：

- 只补 Combat Timeline、BossMotionWarpProfile、BossActionSet 和动画绑定。
- 不修改 Brain / Runner / Combat 框架。
- 不创建默认 Timeline、默认 MotionProfile 或 fallback hitbox。
- Grab / Execution / Cinematic 到真正实现玩法时再增加对应执行器，不提前增加空系统。

## 1. 设计目标

项目层级：小型动作战斗 Demo / 求职作品原型。

Boss AI 要服务以下体验：

- Raven 风格高速近战压迫：近身斩击、后撤反击、中距离追击。
- 玩家应对闭环：Guard、PerfectGuard、Evade、PerfectEvade、Skill 都能在正式 Boss 攻击中被验证。
- 可被打断与不可被打断并存：不同攻击或窗口可配置 SkillInterruptArmor / Uninterruptible。
- 招式选择可解释：Debug 能说明为什么选某招、为什么拒绝某招、为什么被打断或没有被打断。
- 数据来源单一：Combat Timeline 管攻击窗口和 HitNode，MotionProfile 管位移和出招距离，不复制镜像字段。
- 分层清晰：决策、动作执行、移动、命中、受击反应、表现各自负责自己的事。

非目标：

- 不做开放世界怪物生态 AI。
- 不做完整行为树编辑器。
- 不把 Combat Timeline 扩展成 AI 决策编辑器。
- 不为了未来多 Boss 做大型框架。
- 不自动生成可玩的默认招式、默认 Hitbox 或默认 Motion 配置。

## 2. 总体架构

目标 Boss 是一个由 `BossActor` 组合的 Encounter Actor：

```text
BossActor = 生命周期入口 + 感知 + 决策 + 状态机 + 动作执行 + 移动 + 战斗交互 + 表现 + 调试
```

推荐模块：

| 模块                                  | 职责                                          | 不负责                            |
| ----------------------------------- | ------------------------------------------- | ------------------------------ |
| `BossActor`                         | 依赖绑定、初始化顺序、每帧 Tick 调度、暂停/死亡/重置入口。           | 具体选招、HitNode 检测、MotionWarp 计算。 |
| `BossBlackboard / BossPerception`   | 记录玩家距离、角度、状态、Boss 阶段、冷却、动作历史和空间事实。          | 切状态、播放动画、结算伤害。                 |
| `BossBrain / BossActionSelector`    | 决定高层意图并从动作库选出具体招式。                          | 执行动作时间线、移动角色。                  |
| `BossStateMachine`                  | 管理顶层互斥流程：思考、重定位、出招、受击、演出、死亡。                | 计算招式评分、执行命中盒查询。                |
| `BossActionRunner`                  | 执行一个已承诺动作，推进 elapsed，读取 RuntimeSpec，输出本帧窗口。 | 选择下一招、直接写 Transform。           |
| `BossMovementSystem`                | 普通移动、动作位移、RootMotion、MotionWarp、碰撞修正的统一出口。  | 决定为什么要接近或后撤。                   |
| `BossCombat / Interrupt / Reaction` | HitNode 检测、命中去重、玩家防御交互、Boss 被打断/硬直/倒地判定。    | 普通选招、VFX/SFX 触发细节。             |
| `BossPresentation`                   | 动画、提示、反馈、镜头事件。                              | 反向决定战斗语义；默认不新增调试遥测。           |

依赖方向固定：

```text
BossActor
  -> Perception / Blackboard
  -> Brain / Selector
  -> StateMachine
  -> ActionRunner
  -> Movement / Combat / Interrupt / Reaction
  -> Presentation / Debug

Data assets -> Runtime 读取
Presentation -> 只消费 Runtime 事件
```

## 3. 顶层状态模型

目标状态不按当前零散枚举扩张，而按 Boss 流程分类：

| 状态           | 用途                                                                | 典型退出                                          |
| ------------ | ----------------------------------------------------------------- | --------------------------------------------- |
| `Inactive`   | 未进入战斗或被禁用。                                                        | 战斗触发后进入 `Intro` 或 `Thinking`。                 |
| `Intro`      | 入场、开场镜头、锁控制。                                                      | 演出完成后进入 `Thinking`。                           |
| `Thinking`   | 更新感知，评估阶段、距离和下一步意图。                                               | 进入 `Acting`、`Reposition`、`Scripted` 或 `Dead`。 |
| `Reposition` | Approach / Strafe / Retreat / Teleport 等非攻击空间调整。                  | 达到距离/角度/时间条件后回 `Thinking`。                    |
| `Acting`     | 执行一个 Boss Action，例如 Slash、ChaseCombo。                             | 动作结束、被打断、死亡、转阶段。                              |
| `Reacting`   | HitStagger、PerfectGuardStagger、Knockdown、ExecutionVulnerable 等反应。 | 反应结束后回 `Thinking` 或进入 `Dead`。                 |
| `Scripted`   | 转阶段、处决、特殊演出。                                                      | 演出结束后回 `Thinking`、`Dead` 或战斗结束。               |
| `Dead`       | 死亡流程。                                                             | 无。                                            |

关键规则：

- `Dead` 优先级最高。
- `Scripted` 高于普通攻击和重定位。
- `Acting` 表示 Boss 已承诺执行一个动作，普通选招不能在同一动作中途随意改判。
- `Reacting` 由 Combat / Interrupt 结果触发，不由普通 Brain 直接选择。
- `Reposition` 是为了让下一次决策更合理，不直接承担攻击命中语义。

## 4. 动作模型

Boss Action 是一次可承诺执行的动作，不等于一个状态，也不等于一个 HitNode。

```text
BossActionDefinition
  -> 选择数据：距离、角度、冷却、权重、重复惩罚、阶段可用性
  -> Timeline Key：Combat Timeline ActionAsset
  -> Motion Key：BossMotionWarpProfile entry
  -> Presentation Key：动画、提示、镜头、反馈映射
```

当前 Raven P1 Core 首批动作：

| ActionId              | 用途              | 玩家应对                  |
| --------------------- | --------------- | --------------------- |
| `Raven_Slash`         | 近距离基础压力，低冷却单击。  | Guard / PerfectGuard。 |
| `Raven_SlashChain`    | 近距离二连，测试连续防御。   | Guard / PerfectGuard。 |
| `Raven_EvadeBackRush` | 后撤接突进二连，制造空间变化。 | 读后撤，防御或闪避突进。          |
| `Raven_ChaseCombo`    | 中距离红光追击四连。      | 连续 PerfectGuard 或规避。  |

后续动作池：

- `Raven_MoveCombo`、`Raven_MoveChainCombo` 可作为 P1 Later 压力招。
- 黄光抓取、蓝光、P2 大招、处决、转阶段演出先作为 Reserved，不进入第一批实现。

动作生命周期：

```text
Select -> Commit -> Startup -> Active -> Recovery -> Complete
                 \-> Interrupted -> Reacting
                 \-> PhaseBreak / Scripted
                 \-> Dead
```

## 5. 选招与决策

第一版推荐使用轻量 Utility Scoring，不引入行为树编辑器。

输入事实：

- 玩家距离层：Close / Mid / Far，玩家一直贴身绕背，玩家一直拉远
- 玩家朝向和角度：Boss 面向玩家夹角（正前方、侧面、背后）。如果角度过大，先转身再选招
- 玩家状态：Idle/Move/Attack/Evade/Guard/Skill/HitReaction/Knockdown/recover,  还要再具体看状态的阶段，比如攻击的前摇、命中、后摇等选择不同的行为，被防御/完美防御时如何选择，闪避时如何选择，恢复时如何选择等。还可有看玩家最近的状态，总结最近的习惯选择出招
- Boss 状态：阶段、冷却、上一招、连续重复次数、当前压力值，血量，防御值
- 空间条件：前方/后方是否有足够位移空间。


常见的boss 选择关注表：

| 玩家状态             | Boss 应关注什么  | Boss 常见应对     |
| ---------------- | ----------- | ------------- |
| Idle             | 玩家是否在等待反击   | 慢步压迫、假动作、起手攻击 |
| Move             | 玩家移动方向、距离变化 | 调整站位、突进、横扫    |
| Sprint / Run     | 玩家是否逃跑      | 追击、远程、跳劈      |
| Attack Startup   | 玩家攻击前摇      | 抢招、后撤、霸体      |
| Attack Active    | 玩家正在出刀      | 防御、闪避、弹反、霸体   |
| Attack Recovery  | 玩家后摇        | 惩罚、重击、抓取      |
| Guard            | 是否长时间防御     | 抓取、破防、延迟重击    |
| PerfectGuard     | 玩家刚成功防反     | 暂停压制、后撤、换节奏   |
| Evade Start      | 玩家刚闪避       | 保持招式，不强制追踪    |
| Evade Invincible | 玩家无敌中       | 延迟攻击、二段追击准备   |
| Evade Recovery   | 玩家闪避后摇      | 追击、延迟斩        |
| Skill Startup    | 玩家技能前摇      | 快速打断、后撤       |
| Skill Active     | 玩家技能生效      | 闪避、防御、霸体      |
| Skill Recovery   | 玩家技能后摇      | 惩罚            |
| HitReaction      | 玩家被打中       | 连段追击或重置距离     |
| KnockDown        | 玩家倒地        | 压起身、蓄力、后撤     |
| Healing / Item   | 玩家喝药        | 突进、远程、跳劈      |
| Dead             | 战斗结束        | 停止 AI         |


选择流程：

1. `BossPerception` 更新 facts。
2. `BossBrain` 输出意图：Attack、Reposition、PhaseTransition、Scripted、Idle。
3. Attack 意图进入 `BossActionSelector`。
4. Selector 按阶段、距离、角度、冷却、空间、重复限制过滤候选。
5. 对动作可以再划分不同的意图，然后根据当前条件选择意图，再对意图内的候选动作按权重、压力值、历史惩罚评分。
6. 选出动作后进入 `Acting`。
7. 没有可用攻击时才进入 Approach / Strafe / Retreat。

仅在用户明确要求 Debug 能力时才记录：

- 本帧意图。
- 候选动作列表。
- 每个动作被拒绝的原因。
- 最终动作的评分和选择原因。

## 6. Combat Timeline 的作用

Combat Timeline 只表达动作内部战斗窗口，不负责 AI 决策。

它负责：

- HitNode 激活窗口。
- SkillInterruptArmor / Uninterruptible / Invincible 等能力窗口。
- 攻击 phase、startup / active / recovery 等动作内部时间信息。
- 动画预览和策划可视化。

它不负责：

- Boss 是否想攻击。
- Boss 应该选哪一招。
- Boss 是否要靠近、后撤、绕侧。
- 阶段切换和演出流程。
- MotionWarp 数值和出招距离筛选。

`CombatHitNodeData` 继续作为 Boss / Player 共用 runtime HitNode 数据。Snapshot 只暴露激活的 HitNode id；具体伤害、防御、完美防御、完美闪避、击倒、PerfectGuard 反制等语义由 HitNode 数据解释。

## 7. 移动系统边界

Boss 移动拆成四类，最终都通过一个出口写 CharacterController / Transform：

| 类型 | 例子 | 数据来源 |
| --- | --- | --- |
| Locomotion | Approach、Strafe、Retreat。 | Reposition 配置 / 代码参数。 |
| Action Motion | 突进、后撤、攻击中修正。 | `BossMotionWarpProfile`。 |
| Root Motion | 动画自带位移。 | Animator / RootMotionRelay。 |
| Collision Resolve | 防穿模、重叠恢复。 | MovementSystem 运行时计算。 |

规则：

- `BossMotionWarpProfile` 是动作位移和出招距离的唯一来源。
- `BossActionDefinition` 不复制 MotionWarp 参数。
- 缺 MotionProfile 或缺 attack config 时拒绝选择该招式。
- Attack / Reposition / Reaction 不直接写 Transform，只提交 movement request。

## 8. 命中、打断与反应

Boss 对玩家的攻击：

- `BossActionRunner` 根据 RuntimeSpec 得到激活 HitNode。
- `BossCombatSystem` 通过手动配置的 `BossAttackHitboxAnchor + BoxCollider` 查询命中。
- 命中去重粒度固定为：`AttackInstanceId + HitNodeId + TargetHurtbox`。
- HitNode 决定伤害、防御、PerfectGuard、PerfectEvade、Knockdown、Boss 反制等语义。
- 缺 anchor 时只 warning 且该 HitNode 不命中，不生成 fallback 检测盒。

玩家对 Boss 的攻击：

- `BossInterruptSystem` 接收玩家命中事件。
- 若 Boss 已死亡，忽略普通反应。
- 若当前动作处于 SkillInterruptArmor / Uninterruptible 窗口，则不进入打断反应。
- 若命中满足可打断条件，产生 `BossReactionRequest`。
- `BossReactionSystem` 把请求转成 HitStagger、PerfectGuardStagger、Knockdown、ExecutionVulnerable 或 Death。

默认策略：

- Light / Heavy 默认不打断攻击中的 Boss，除非 ActionPolicy 显式允许。
- Skill 可以打断没有 SkillInterruptArmor 的 Boss 攻击，并可触发 Knockdown。
- PerfectGuard 是否让 Boss 硬直由 HitNode 的显式配置决定。

## 9. 数据与资产归属

| 数据                                      | 归属                                            | 说明                     |
| --------------------------------------- | --------------------------------------------- | ---------------------- |
| Boss 动作目录、标签、冷却、权重、阶段可用性                | `BossActionDatabase` / `BossActionDefinition` | 决策和选择数据。               |
| HitNode、Armor、Uninterruptible、攻击时间窗口    | Combat Timeline ActionAsset / RuntimeSpec     | 动作内部战斗窗口。              |
| 出招距离、位移、Warp、RootMotion 预算              | `BossMotionWarpProfile`                       | 位移唯一来源。                |
| 阶段阈值、阶段动作池倍率                            | `BossPhaseProfile`                            | 阶段系统读取。                |
| HitStagger、Knockdown、Death、Execution 反应 | `BossReactionProfile`                         | 反应配置。                  |
| 动画、提示、镜头、VFX、SFX key                    | `BossPresentationProfile`                     | 表现层读取。                 |
| 每场战斗运行时决策事实                           | `BossBlackboard`                              | 不能写入 ScriptableObject。 |

禁止：

- Boss 专属重复 HitNode runtime 类型。
- ActionDefinition 复制 MotionProfile 字段。
- Runtime 创建可玩的默认 Profile。
- Editor 自动补出可玩的 Timeline / HitNode / Hitbox / Motion 数据。

## 10. 当前项目落地状态

当前不再是“迁移不直接替换 Controller”的阶段。`BossController` 已删除，正式入口和运行链路如下：

```text
BossActor.Update / TickActor
  -> BindReferences / target facts
  -> BossStateMachine.EvaluateTick
  -> BossBrain.SelectAction
  -> BossActionRunner.Start / Tick / End
  -> BossMovementSystem
  -> BossAttackExecutor
  -> BossCombatSystem
  -> BossReactionSystem
  -> BossAnimationBridge / presentation consumers
```

当前职责归属：

1. **Actor 入口**：`BossActor` 是唯一 MonoBehaviour 入口，负责正式 Tick、外部启停、Root Motion、玩家命中、PerfectGuard 反制和运行时事实查询。
2. **Perception / Blackboard**：保留最小事实采样，用于 Brain 输入；不再新增 Shadow-only Preview / Candidate 链路。
3. **Brain / Selector**：`BossBrain` 是对外决策入口，内部使用 `BossActionSelector`；Selector 从 `BossActionSet` 读取动作池，不再硬编码 Raven 初始四招。
4. **Action Model**：`BossActionKind` 表达 `Attack / SpecialCueAttack / Grab / Execution / Cinematic / Reposition / Reaction`。当前只允许 `Attack` 进入 Runner，未实现 Kind 明确拒绝执行。
5. **ActionRunner**：`BossActionRunner` 负责攻击 Start / Tick / End 生命周期，持有当前攻击定义和攻击实例 ID。
6. **Movement**：`BossMovementSystem` 负责 Reposition、ActionMotion 和 Animator Root Motion 请求入口；底层仍由 `BossMotionController` 执行 CharacterController / MotionWarp / 碰撞裁剪。
7. **Combat**：`BossCombatSystem` 负责正式命中查询、Receiver / Hurtbox 映射、HitNode 语义过滤、Hurtbox 粒度去重、`CombatHitData` 构造、`ReceiveHit()` 和 PerfectGuard 反制触发。
8. **Reaction**：`BossReactionSystem` 负责玩家命中 Boss 后的 Dead、HitStagger、Knockdown、PerfectGuard Boss Stagger 决策和提交计划。
9. **Config**：`BossRuntimeConfigResolver` 负责 Boss Reaction Timeline 与 Strafe 后强制攻击配置读取；缺配置仍拒绝或报错，不创建 fallback。
10. **Presentation**：表现层通过 `BossAnimationBridge`、UI、RootMotionRelay、CombatReceiver 等消费者引用 `BossActor`，不反向决定战斗语义。

当前资产状态：

- `Assets/ScriptableObjects/Boss/RavenBossActionSet.asset` 已绑定 Demo 场景 Boss。
- 当前 ActionSet 只包含 `Raven_Slash / Raven_SlashChain / Raven_EvadeBackRush / Raven_ChaseCombo` 四个 `Attack`。
- 新增普通攻击时应补 Timeline、MotionWarpProfile、ActionSet 和动画绑定，不改 Brain / Runner / Combat 框架。

后续验收重点：

- Unity 编译和 Console project error 为 0。
- Demo 场景 Boss 无 Missing Script。
- Boss 四招 MotionWarp、HitNode、PerfectGuard、SkillInterruptArmor、Skill 击倒、死亡和缺配置守卫回归。

### Blackboard / Snapshot 使用规则

迁移阶段里频繁出现的 `Snapshot` 和 `Preview` 是 Shadow-only 迁移合同，不是 Debug 字段，也不是最终正式 API 的命名承诺。

`Snapshot` 表示“某一帧只读采样到的事实”。在历史 Shadow 阶段，它曾用于把旧入口的运行时状态固定成稳定输入；当前正式阶段已停止把这些事实拆成多个公开 Snapshot。Brain / Selector 的正式输入统一为 `BossBlackboard`，由 `BossActor` 每次决策前直接组装。

`Preview` 表示“Shadow 模块根据当前只读输入推导出的下一步结果预览”。它只回答“如果后续由新架构接管，本帧会倾向于什么意图、选择或命令”，不提交正式行为，不调用旧系统执行入口，不驱动动画、位移、命中或状态切换。`BossActor` Authoritative Switch 完成后，第一批旧 Preview / Candidate / Commit 合同已删除，后续不再新增同类 Shadow-only 观察层。

这样做的收益：

- 历史迁移期可以在旧入口继续掌控正式行为时，逐步验证新 Brain / Selector / ActionRunner 的输入输出是否合理。
- 读写边界清楚，Shadow 代码默认只读，降低小步迁移破坏正式 Boss 战的风险。
- PlayMode 测试可以直接断言某一帧事实、意图、选择和命令，避免为了测决策而真正启动攻击或移动。
- 当前正式接管后，Brain 默认读取 `BossBlackboard`，Runner 和系统入口消费正式 `Request / Result / Plan` 类型，不再散读旧兼容入口。

代价和风险：

- 类型数量会增加，过度使用会让目录显得碎片化。
- 如果 Snapshot 复制了配置字段或长期保存镜像数据，会违反“数据来源单一”。
- 如果 Preview 长期只预览、不推进 Authoritative 接管，会形成两套并行但都不完整的逻辑。
- 如果刷新顺序不清楚，可能出现同一帧内 Runtime / Blackboard / Brain / Selector 事实不同步。

后续实施约束：

- 只有跨模块边界稳定、需要被多个正式系统消费的事实才允许新增输入合同；单个类内部临时变量不建 Snapshot。
- 正式 Brain 输入优先扩展 `BossBlackboard` 的直接字段，不再恢复 `BossRuntimeContext / BossPerceptionSnapshot / BossSelectionRuntimeSnapshot / BossActionExecutionSnapshot / BossBlackboardSnapshot` 这类公开嵌套层。
- Preview 只用于 Shadow 阶段建立输出合同；当模块进入正式接管后，应按语义重命名或收敛为 `Intent / Selection / Command / Request / Result` 等正式类型。
- 不为了调试而新增 Snapshot / Preview 字段；候选评分、拒绝原因、解释文本、Debug UI 只有用户明确要求时才增加。
- 每新增一个跨模块合同，都必须说明来源、刷新时机、消费者和何时可以删除或合并。

### 阶段性收敛规则与后续总路线

Shadow 链路曾经从运行时事实采样推进到 `BossCombatHitCommitRequestSnapshot`。该合同是最后一个 Shadow-only Combat 合同；在 `BossCombatSystem / BossActionRunner / BossMovementSystem / BossActor` 完成正式接管后，第一批过时 Shadow-only Preview / Candidate / Commit 类型已删除。后续任务不能继续新增 Shadow-only Combat Candidate / Preview 层，必须围绕正式接管和旧入口清理推进。

当前已完成边界：

- `BossActor` 已能只读绑定旧 Boss 组件并采样 Runtime / Perception / Selection / ActionExecution。
- `BossBlackboard` 已成为正式 Brain / Selector 的输入合同；旧 Shadow Snapshot 组与薄结果包装已删除。
- 正式 Brain / Selector 已能输出高层意图并从 Raven 初始攻击池选择攻击。
- 正式 ActionRunner 已接管动作 Start / Tick / End 生命周期。
- 正式 CombatSystem 已接管命中查询、语义过滤、Hurtbox 粒度去重和命中提交。
- `BossActor` 已成为真实场景正式 Tick 调度入口；`BossController` 已删除。

当前主要风险：

- 如果继续只新增 Snapshot / Preview，会形成第二套长期并行但不接管的 Shadow 系统；从 Hit Commit Request 完成后，该做法默认禁止，且第一批旧合同已删除。
- 如果过早切换 Authoritative，会同时影响选招、动作生命周期、MotionWarp、HitNode、Player Guard / Evade / Skill 交互，回归面过大。
- 如果把配置镜像进 Snapshot，会破坏数据来源单一：Combat Timeline 管攻击窗口和 HitNode，`BossMotionWarpProfile` 管位移和出招距离。
- 如果为观察方便新增 Debug 字段、候选解释或日志，会违背当前项目约束；Debug 能力只在用户明确要求时添加。
- 如果后续重新引入 facade 或 Shadow-only Preview 链路，会再次形成双轨，维护成本会上升。

### 架构审视与状态模型对齐规则

当前路线整体仍服务 3D 动作 Boss 战架构：Combat Timeline 负责动作窗口，`BossMotionWarpProfile` 负责动作位移和出招距离，`BossCombatSystem` 负责命中执行，`BossActionRunner` 负责攻击生命周期。这些边界符合 Raven 风格高速近战 Boss 的核心要求。

当前实现使用 `BossStateId` 作为唯一状态事实，不再额外缓存顶层分类和子模式。`BossStateMachine` 保存当前状态与状态计时，`BossBlackboard` 只传递同一个 `CurrentState`，`BossBrain` 直接根据具体状态判断 Action、Recovery、Reaction 与 Approach/Strafe 分支。

这次收敛删除了 `BossHfsmStateId`、`BossHfsmStateMapper`、`CurrentTopState`、`CurrentRepositionMode`、`CurrentActionPhase` 和 `CurrentReactionMode`。这些字段均可由 `BossStateId` 确定性推导，重复保存会形成双事实源，却没有父状态独立 Enter/Exit、父状态 Tick 或嵌套迁移等真正 HFSM 行为。

当前状态合同如下：

| `BossStateId` | 运行时语义 |
| --- | --- |
| `None / Dead` | 未运行或终止 |
| `Idle` | 初始化或目标缺失时的战术评估 |
| `Approach / Strafe` | 普通非攻击中立移动 |
| `Action` | 统一 Action 执行；具体类别由 `BossActionKind` 决定 |
| `Recovery` | Attack 最后一击后的恢复锁 |
| `HitStagger / Knockdown / ShieldBreakStun` | 受击反应生命周期 |

只有未来出现无法由单一状态表达的独立父状态生命周期、父状态级 Enter/Exit 或嵌套迁移时，才重新评估真正的分层状态机；不得仅为缩短条件判断恢复派生枚举。

2026-06-28 已完成冗余中间层删除：`BossRuntimeRefs`、旧 Shadow Snapshot 组、Boss 专用 DebugSnapshot、Boss Debug Overlay 窗口、`BossBrainIntentSnapshot` 和 `BossActionSelectionResult` 已删除。后续不得为了“看起来分层”恢复只转发的结果壳或 Debug 常驻字段；正式链路优先保持 `BossActor -> BossBlackboard -> BossBrain / BossActionSelector -> BossActionRunner / Movement / Combat / Reaction / StateMachine`。

2026-06-28 已执行 `BossStateTransitionEffects` 简化路线：状态切换副作用不再经过独立 plan 类型，`BossActor.CommitStateTransition()` 直接提交 ActionRunner、Movement、Strafe 运行时和动画副作用。`BossStateMachine` 仍保持无副作用状态事实与 Tick 决策。后续只有当状态进入 / 退出规则复杂到 `BossActor` 难以阅读时，才考虑引入真正的状态节点或明确的 enter / exit 模型，不恢复只包装字段的中间层。

2026-06-28 已执行 Boss AI 删除、简化、合并优化：删除 `BossActor` Authoritative 迁移期开关、`BossTestHitEmitter` 测试命中器、`BossRootMotionRelay` 到 `BossMotionController` 的兼容回退，`BossBrainIntentId` 合并进 `BossBrain.cs`，`BossCombatProcessResult` 不再暴露语义拒绝原因字符串。后续正式 Runtime 不再保留测试命中组件或 Shadow / Authoritative 双轨开关。

本项目暂不引入玩家状态机那种 class-per-state Boss 子状态框架。Boss 的多数生命周期已经由稳定系统承担：`BossActionRunner` 管动作生命周期，`BossMovementSystem` 管 Reposition / ActionMotion / RootMotion，`BossReactionSystem` 管反应优先级与提交计划，`BossCombatSystem` 管命中执行。`BossStateMachine` 只保存唯一状态、计时和 Tick 决策，不把每个动作窗口拆成状态类。

后续任务必须遵守以下纠偏规则：

- `BossActor` 必须保持正式 Tick 入口；不得恢复 `BossController` 或等价 facade。
- `BossStateMachine` 不得保存可由 `BossStateId` 推导的第二套分类状态；`BossStateId` 是所有运行时模块共享的唯一状态事实。
- `Approach / Strafe` 是正式 `BossStateId`，同时也是 Movement 使用的普通中立策略，不再镜像为 `RepositionMode`。
- `Action / Recovery` 的退出由 `BossActionRunner` 和动作结果驱动，但状态事实仍只写入 `BossStateId`。
- `HitStagger / Knockdown / ShieldBreakStun / Dead` 的优先级由 `BossReactionSystem` 决定，提交结果仍写入同一个 `BossStateId`。
- `BossActor` 内的状态相关方法只能提交状态切换后的副作用；真正状态切换必须通过 `BossStateMachine.TransitionTo()` 完成，避免 Actor 重新变成状态机。
- 下一步 `MovementSystem` 不能只是把 `BossMotionController` 包一层转发；它必须解决真实职责：动作位移、Root Motion、Warp、碰撞裁剪的仲裁，以及唯一 `CharacterController.Move()` 出口。
- 在 `MovementSystem` 拆分时直接按 `BossStateId` 维护移动边界：`Approach / Strafe` 提交普通移动请求，`Action` 提交动作位移请求，反应状态可以停止或覆盖当前动作位移。
- 在 Brain / Selector 正式接管前，不再新增新的 Shadow-only 观察层；确需新增输入合同，必须直接服务正式接管并说明旧入口何时删除。

后续总路线不再继续“Controller 收敛”。当前只允许围绕以下方向推进：

1. **真实 Boss 行为回归**：验证四招出招、MotionWarp、HitNode、PerfectGuard、SkillInterruptArmor、Skill 击倒、死亡和缺配置守卫。
2. **可读性封版**：整理 `BossActor` 内部过长方法、命名和注释，但不新增等价 Controller facade。
3. **ActionSet 扩展**：普通攻击通过 Timeline、MotionProfile、ActionSet 和动画绑定扩展，不修改 Brain / Runner / Combat 框架。
4. **特殊动作实现**：当真正做特殊提示攻击、抓取、处决或演示动作时，才按 `BossActionKind` 增加对应执行器和 Presentation 事件。
5. **文档与测试清理**：历史 Shadow / Controller 记录保留为追溯，当前 README、索引和测试入口必须指向 Actor / Systems。

后续每个小步任务都必须归入上面 5 个方向之一。如果一个任务需要恢复 Controller、增加 Shadow-only Preview 链路或新增空系统，默认视为发散任务，先重新规划。

## 11. 当前代码目录

不要一次性创建大量空文件。当前 Boss 运行时代码目录为：

```text
Assets/Runtime/Boss
├── Actor
├── AI
├── Actions
├── Animation
├── Brain
├── Config
├── State
├── Movement
├── Combat
├── Reaction
```

- `Actor/BossActor.cs`：唯一 MonoBehaviour 入口。
- `AI/BossActionKind.cs`、`AI/BossActionSet.cs`：动作类型和动作池配置。
- `Brain/BossBrain.cs`、`Brain/BossActionSelector.cs`：决策入口和内部选择器。
- `State/BossStateMachine.cs`：顶层状态事实和无副作用 Tick 决策。
- `Actions/BossActionRunner.cs`：攻击生命周期 Runner。
- `Movement/BossMovementSystem.cs`：正式移动请求入口，底层暂时适配 `BossMotionController`。
- `Combat/BossCombatSystem.cs`：正式 Boss 命中执行系统。
- `Reaction/BossReactionSystem.cs`：玩家命中 Boss 后的反应决策和提交计划。
- `Config/BossRuntimeConfigResolver.cs`：Boss 运行时配置读取边界。

后续文件必须跟随真实迁移需求创建，避免空架构先行。

## 12. 待确认决策

实现前只保留这些会改变代码方向的问题：

| 编号  | 问题                           | 推荐默认                                                                           |
| --- | ---------------------------- | ------------------------------------------------------------------------------ |
| A01 | Light / Heavy 是否能打断攻击中 Boss？ | 默认不能；只允许特定 ActionPolicy 显式开启。                                                  |
| A02 | BossBrain 用什么形式？             | Utility Scoring，不上行为树编辑器。                                                      |
| A03 | 首批动作池？                       | 只启用 `Raven_Slash`、`Raven_SlashChain`、`Raven_EvadeBackRush`、`Raven_ChaseCombo`。 |
| A04 | 入口方式？                        | `BossActor` 是唯一正式入口；不恢复 `BossController` 或等价 facade。                              |
| A05 | 缺关键配置怎么办？                    | Error 并拒绝动作或 Encounter，不创建玩法 fallback。                                         |
| A06 | Motion 数据归属？                 | `BossMotionWarpProfile` 唯一来源。                                                  |

推荐确认语句：

```text
采用推荐默认：BossActor 保持唯一入口，新增普通攻击只改 Timeline / MotionProfile / ActionSet / 动画绑定。
```

## 13. 交付边界

本文档到此为止，不继续扩写新的审计、准入、确认或附录章节。

后续工作应该是二选一：

1. 用户修改上面的待确认决策。
2. 按第 10 章从 Shadow Shell 开始小步实现。
