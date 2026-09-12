# Boss AI 方案设计

更新时间：2026-08-05
适用范围：Project EVE Demo / Raven Boss
文档状态：Raven 玩法设计唯一正式入口

## 1. 文档定位、来源与编辑规则

本文档以外部《渡鸦招式参数.md》为 Raven 玩法设计主来源，并补充项目架构、当前实现状态和验收规则。合并的目的不是摘要原文，而是在不减少信息的前提下建立项目内唯一入口。

### 1.1 事实优先级

发生冲突时按以下顺序处理：

1. 用户在原文之后明确确认的设计修订。
2. 外部《渡鸦招式参数.md》的完整原始内容。
3. 不改变玩法语义的项目架构与数据映射。
4. 当前代码、ScriptableObject、Timeline、Animator、场景和测试所反映的实现事实。
5. 历史方案和旧实现。

当前代码与高优先级设计不一致时，必须记录为“实现差距”，不得用当前实现覆盖目标规则。

### 1.2 内容来源标记

- **原文规则**：来自《渡鸦招式参数.md》，只允许排版、明确错字和术语格式微调。
- **后续确认**：用户在原文之后明确确认的设计修订。
- **项目映射**：把玩法术语映射到当前项目类型、资产和运行时模块，不改变玩法语义。
- **当前实现**：当前工程已经存在的代码或资产事实，不代表可以覆盖设计。
- **待确认**：原文未定义或存在歧义，保留原意并显式记录，不擅自选择解释。

### 1.3 后续已确认修订

以下规则是对原文的补充或精确化：

1. 普通非攻击中立状态只有持剑前进 `Approach` 和侧移观察 `Strafe`，不包含 `Idle`、`Retreat` 或独立 `Wait` 状态。
2. Approach 与 Strafe 都必须实际维持至少 `0.3s`，之后才算完成一次非攻击中立行为。
3. 中距离没有其他优先条件时，Approach 与 Strafe 固定按 `1:1` 权重随机。
4. `MustEnterNeutral`、连续攻击上限、压力硬限制等触发强制中立时，禁止检查或启动任何攻击。
5. 原文中的“侧向移动等待”仍是 Strafe 行为，不建立独立 Wait 状态。
6. RapidMoveBack、EvadeBackRush、EvadeBackSwordAura 等招式内部后撤仍然保留；删除的只是普通中立 Retreat 状态。
7. 旧 Retreat/Wait 和“连续 Strafe 后硬编码强制攻击”属于待删除的实现差距，不能继续作为设计规则。
8. BurstAreaSlash 起手播放 `M_Raven_BurstAreaSlash`，随后由 Animator 自动转场到 `M_Raven_BurstAreaSlashEnd`，不使用代码驱动的多段动画轨道。

## 2. Raven 核心玩法规则

本章完整保留《渡鸦招式参数.md》的玩法内容。后续修订紧随相关规则标注，不删除原始信息。

### 2.1 后摇机制

#### 2.1.1 自然后摇

- 自然后摇 `NaturalRecoveryEndTime` 从最后一次攻击判定结束后开始生效。
- 如果玩家没有触发正确破解奖励 `ConfiguredRewardDuration`，按自然后摇计算：
  - 自然后摇期间 Boss 维持攻击后摇状态。
  - 自然后摇结束后开始战术评估，进入中立状态或攻击状态。
  - 中立状态只包括侧移、前进，不包括 Idle。

项目公式映射：

```text
NaturalRecoveryEndTime
  = AttackStartTime
  + LastHitNodeEndTime
  + NaturalRecoveryDuration
```

#### 2.1.2 正确破解奖励后摇

- 如果触发奖励窗口：

```text
RewardEndTime = PlayerAttackableTime + ConfiguredRewardDuration
```

- PerfectGuard 后玩家允许攻击的最小间隔为 `0.26s`，为包含完美防御动画过渡：

```text
RewardEndTime
  = PerfectGuardStartTime
  + 0.26
  + ConfiguredRewardDuration
```

- PerfectEvade 后玩家允许攻击的最小间隔为 `1.0s`，为保持同样的过渡语义：

```text
RewardEndTime
  = PerfectEvadeStartTime
  + 1.0
  + ConfiguredRewardDuration
```

- 奖励窗口内 Boss 保持后摇状态。
- 如果奖励窗口超过攻击动画时长，Boss 可由 Animator 自动切换到 Idle 动画，但状态仍保持 Recovery。
- 普通未破盾 PerfectGuard 触发 HitJustParry 时，受击动画可由 Animator 自动切换到 Idle 动画，但 `BossStateId` 仍保持 `HitStagger`，直到 `Reaction_CanReturn` 起点已到且完美防御后摇锁结束。护盾归零不走该等待路径。
- 奖励窗口结束后才开始战术评估，进入中立状态或攻击状态；中立状态仍不包括 Idle。

#### 2.1.3 Recovery 总规则

1. Idle 状态只在游戏开始时用于初始化。战斗开始后不再把 Idle 当作战术状态；只有 Recovery 动画先结束时允许播放 Idle 动画。
2. 护盾未破时，如果一次攻击的最后一击可被防御，只有最后一击被 PerfectGuard 且对应 HitNode 开启 PG Stagger 才能让 Boss 进入普通 HitJustParry；护盾归零是更高优先级例外，任意可 PerfectGuard 的 HitNode 都会同帧进入 `ShieldBreakStun` 并播放 HitJustParry。
3. 普通 HitJustParry 完整动画时长为 `1.88333s`，最早可在 `0.8s` 返回，但实际返回仍受玩家奖励窗口限制。破盾流程的 `0.8s` 仅是 `ShieldBreakStun` 内切换 Weak 动画的衔接点，不参与返回或 Recovery 判断。
4. Boss 最终后摇结束时间为：

```text
FinalRecoveryEndTime
  = Max(NaturalRecoveryEndTime, all RewardEndTime)
```

5. 后摇期间禁止所有主动战术转换，但允许死亡、强制硬直、失衡等高优先级强制状态打断。

#### 2.1.4 攻击冷却起点

- `BossActionKind.Attack` 的冷却不再从动作起手计算，而是从最后一个 HitNode 结束时开始计算：

```text
CooldownStartTime
  = AttackStartTime
  + LastHitNodeEndTime

NextSelectableTime
  = CooldownStartTime
  + Cooldown
```

- 攻击仍必须先通过 Recovery 生命周期门槛，因此实际最早重新参与选招的时刻为 `Max(NextSelectableTime, FinalRecoveryEndTime)`。
- `BossActionKind.Reposition` 没有攻击 HitNode，继续从动作开始时计算冷却，不套用攻击冷却公式。
- 只有 Action Runner 成功启动后才提交冷却与重复计数；配置无效或启动失败不得消耗冷却。

### 2.2 Raven 招式总表

下表维护当前目标列和数值。项目 ActionId 映射在 7.2 节单独维护，不取代本表。

| ID                                                 | 招式       | P1/P2/P3 权重 | 距离    | 冷却        | 压力  | 强度       | 后续策略                  | 自然后摇  | 动画后摇   | 正确破解奖励                           | Allow 攻击池     |
| -------------------------------------------------- | -------- | ----------- | ----- | --------- | --- | -------- | --------------------- | ----- | ------ | -------------------------------- | ------------- |
| M_Raven_Slash                                      | 高速反手剑    | 30/18/18    | 0～2.6 | 2s        | 7   | Low      |                       | 0.6s  | 2.2s   | 最后一击 PG 或 PE：0.8s                | CloseDuelPool |
| M_Raven_SlashChain                                 | 延迟上挑二连   | 30/18/18    | 0～2.6 | 2s        | 11  | Low      |                       | 0.73s | 1.13s  | 最后一击 PG 或 PE：1.0s                | CloseDuelPool |
| M_Raven_MoveCombo                                  | 左闪三连击    | 28/28/30    | 0～3.5 | 4s        | 15  | Medium   |                       | 0.8s  | 1.117s | 最后一击 PG 或 PE：1.1s                | CloseDuelPool |
| M_Raven_MoveChainCombo                             | 右闪六连击    | 28/28/31    | 0～3.5 | 4s        | 24  | High     |                       | 1.0s  | 1.08s  | 最后一击 PG 或 PE：1.3s                | CloseDuelPool |
| M_Raven_BetaChargeCombo                            | 黄光居合三连   | 0/14/18     | 0～3.5 | 6s        | 24  | Critical | 必须进入中立；必须先完成一次非攻击中立行为 | 1.2s  | 1.4s   | 最后一击 PE：1.5s；三个攻击全部 PE：2.0s      | SpecialPool   |
| M_Raven_EvadeBackRush                              | 后跳突进二连   | 20/20/22    | 0～2.6 | 4s        | 14  | Medium   |                       | 0.8s  | 1.05s  | 最后一击 PG 或 PE：1.0s                | HitEscapePool |
| M_Raven_ChaseGrab                                  | 黄光瞬移下砸   | 15/20/23    | 3.5～9 | 6s        | 17  | Medium   |                       | 0.8s  | 2.2s   | 最后一击 PE：1.0s                     | GapClosePool  |
| M_Raven_ChaseCombo                                 | 红光四连突进   | 15/20/24    | 3.5～9 | 6s        | 25  | High     |                       | 1.0s  | 1.13s  | 最后一击 PG 或 PE：1.3s；全部攻击 PG：1.8s   | GapClosePool  |
| M_Raven_SlashCombo                                 | 站立红四连＋黄光 | 10/15/18    | 0～3.5 | 6s        | 30  | Critical | 必须进入中立；必须先完成一次非攻击中立行为 | 1.2s  | 1.73s  | 最后一击 PE：1.5s；前四击 PG＋最后一击 PE：2.0s | SpecialPool   |
| M_Raven_BurstAreaSlash + M_Raven_BurstAreaSlashEnd | 次元斩      | 0/8/12      | 0～3.5 | 10s       | 30  | Critical | 必须进入中立；必须先完成一次非攻击中立行为 | 1.3s  | 1.37s  | 最后一击 PE：1.5s；全部攻击 PE：2.0s        | SpecialPool   |
| M_Raven_SwordAuraCombo                             | 剑气三连击    | 0/12/15     | 6～11  | 10s       | 24  | High     |                       | 1.0s  | 2.05s  | 最后一击 PE：1.4s                     | RangedPool    |
| M_Raven_EvadeBackSwordAura                         | 后跳接剑气    | 0/16/18     | 0～3.5 | 6s        | 17  | Medium   |                       | 0.9s  | 1.55s  | 最后一击 PE：1.1s                     | HitEscapePool |
| M_Raven_RapidMoveBack                              | 纯后撤      | 15/15/15    | 0～2.6 | 4s        | 0   | 防守       | 必须进入侧移状态              |       |        |                                  | HitEscapePool |
| M_Raven_Caution_Lw / M_Raven_Caution_Rw            | 中立：侧向观察  |             |       | 最短维持 0.3s |     |          |                       |       |        |                                  |               |
| M_Raven_Caution_Fw                                 | 中立：持剑接近  |             |       | 最短维持 0.3s |     |          |                       |       |        |                                  |               |

### 2.3 阶段与基础攻击语义

阶段血量范围：

- P1：`100%～60%`。
- P2：`60%～25%`。
- P3：`25%～0%`。

转阶段固定招式：

- 首次进入 P2 与 P3 时各排队一次 `Raven_BurstAreaSlash`，并在当前动作或反应安全结束后的首个 Brain 帧优先执行。
- 该转阶段请求不受普通随机权重、近期使用、压力门控或已有冷却阻止，但仍走正式 ActionSet / ActionRunner / Timeline / Motion / HitNode 链路，并正常记录压力、冷却和后续中立。
- 不允许为抢转阶段演出直接打断正在执行的 Attack、Recovery、HitStagger、Knockdown 或 ShieldBreakStun；死亡不触发，同一阶段不重复。一次伤害跨过两条阈值时，两次请求依次保留。

攻击类型规则：

1. 黄光攻击节点只能通过 PerfectEvade 躲避。
2. 剑气只能通过 PerfectEvade 躲避。
3. 当前 Demo 只有攻击的最后一个黄色攻击或剑气节点命中后让玩家 Knockdown；其他节点只造成普通 HitReaction。
4. 红光攻击无法被普通 Guard，只能被 PerfectGuard 或 PerfectEvade。

项目补充：次元斩采用与剑气、黄光相同的 PerfectEvade-only 语义；只有最后节点 Knockdown，前置节点为 HitReaction。

### 2.4 Boss 受击时的反击机制

原文规则：

1. Boss 受最大受击次数影响，一轮固定最多受击三次。
2. 受玩家奖励窗口影响，奖励窗口内不允许进行战术决策。
3. 每次受击提高受击反制评分，受击反制评分越高，反击概率越大。
4. 允许反击时，只要不处于强制硬直期间，就可以交给 Boss Brain 进行战术决策。

后续确认细化：

- `[后续确认]` 外部原文曾称“逃脱评分”，项目正式术语统一命名为“受击反制评分”。它不是单纯控制 HitEscapePool 的逃脱开关，而是控制 HitStagger 可返回后是否允许 Boss 启动反制动作；反制动作包括 HitEscapePool 的纯后撤 / 后撤反击，也包括 ActionSet 未禁止受击上下文的近身反击和其他攻击池动作。
- 初次 HitReaction 计为第 1 次。
- `1.25s` 没有新命中后重置本轮受击序列。
- 第 1/2/3 次普通硬直允许返回时，受击反制概率分别为 `25% / 60% / 100%`。
- 每个命中计数只抽取一次受击反制评分。
- 正确破解奖励窗口和明确强制硬直期间禁止受击反制决策。
- 死亡、Knockdown 和明确强制硬直可以打断 Recovery 锁；普通战术状态不能打断。
- `[实现约定]` 原文未说明受击反制评分失败后的叶子状态。当前实现不由 StateMachine 直接切换 Strafe；HitStagger 到达 `Reaction_CanReturn` 起点后统一交给 BossBrain。若受击反制评分未通过，本次不启动动作候选，Brain 按普通中立规则决定 Approach 或 Strafe；若评分通过，Brain 再按压力、动作池和池内权重决定 HitEscapePool、近身反击、后撤反击或其他允许受击上下文的动作。

### 2.5 Boss 压力机制

#### 2.5.1 定义与目标

压力表示：当前最近几秒钟内，Boss 已经给玩家施加了多少操作、认知和空间压力。

`CurrentPressure` 越高：

- 下一招越应该降级。
- 越不能继续使用长连段、高危攻击和强制反制。

#### 2.5.2 各阶段压力区间

| 阶段 | 目标压力区间 | 软上限 | 硬上限 |
| --- | --- | --- | --- |
| P1 | 18～50 | 60 | 70 |
| P2 | 38～60 | 70 | 82 |
| P3 | 46～66 | 76 | 86 |

- 正常战斗大部分时间应处于目标压力区间。
- 达到软上限后：禁止高危技能；长连段大幅降权；提高横移、后撤和短攻击权重。
- 预测下一招会超过硬上限时，该招式直接不可选。

后续确认：普通中立不再包含 Retreat。上述“提高后撤权重”保留为原始设计信息，运行时应映射到 HitEscapePool 中的纯后撤或后跳招式，而不是恢复普通中立 Retreat 状态。

#### 2.5.3 各阶段压力模式

| 阶段 | 进入降压模式 | 退出降压模式 |
| --- | --- | --- |
| P1 | ≥60 | ≤42 |
| P2 | ≥70 | ≤52 |
| P3 | ≥76 | ≤58 |

#### 2.5.4 压力增加

- 技能确定并进入起手：增加该动作压力成本的 `70%`。
- 进入第一个有效攻击判定帧：增加剩余 `30%`。

| 事件 | 压力变化 |
| --- | --- |
| 玩家被击倒 | +10 |
| 玩家普通受击 | +4 |
| 玩家被打到场地边缘 | 额外 +6～8 |

项目映射：玩家距场地边缘 `0.75～1.5m` 时预测压力额外 `+6`；不超过 `0.75m` 时额外 `+8`。

`[后续确认]` 命中加压按单个 `AttackInstanceId` 聚合并在命中时即时补差：普通受击先把本次攻击累计值提升到 `+4`；同次攻击之后造成 Knockdown / Dead 时只再补 `+6`，使最终上限达到 `+10`。重复 HitNode、多 Collider 或同等级后续命中不再增加压力；新攻击开始后，上一攻击的迟到 Detached 结果不得写入压力或破解奖励。

#### 2.5.5 玩家正确应对后的减压

| 玩家行为 | 压力变化 |
| --- | --- |
| PerfectGuard 普通一击 | -3 |
| 完整 PerfectGuard 一套多节点普通连段 | 逐节点减压之外额外 -4 |
| 完整 PerfectGuard 一套多节点红光连段 | 逐节点减压之外额外 -6 |
| PerfectEvade 黄光攻击 | -6 |
| PerfectEvade 普通一击 | -3 |

项目补充：剑气和次元斩的 PerfectEvade 按黄光同档处理，单节点 `-6`。

#### 2.5.6 自然衰减

| 自然衰减状态 | 衰减速度 |
| --- | --- |
| Boss 正在攻击 | 0/秒 |
| 玩家处于受击硬直 | 0/秒 |
| 双方中立观察 | -10/秒 |
| Boss 横移观察、后撤 | -10/秒 |
| Boss 持剑接近 | -6/秒 |
| Boss 攻击 Recovery | -8/秒 |
| Boss 普通受击 | -6/秒 |
| Boss 倒地 | -10/秒 |

原文“后侧”按明确语义错字修正为“后撤”。

选择横移观察、持剑接近时不立刻降低压力，而是在行为持续期间自然衰减。纯后撤 `RapidMoveBack` 是例外：它是专用减压 Action，成功启动时一次性 `-20`，执行期间再按 `-10/秒` 自然衰减；启动失败不减压，成功后被打断也不返还。

`[后续确认]` 纯后撤 RapidMoveBack 的显式动作类型为 `BossActionKind.Reposition`。所有 Reposition 成功启动时默认一次性 `-20`，并在执行期间按 `-10/秒` 自然衰减；它与普通中立 Strafe 使用相同持续衰减速度，但仍是具有 Timeline、冷却和动作池历史的正式 Action。Recovery 按 `-8/秒` 自然衰减；玩家处于 HitReaction / Knockdown 时，所有自然衰减继续暂停，但不会撤销已提交的一次性减压。

### 2.6 三层 AI 决策

中立状态下不要每帧持续选招；每隔 `0.15～0.25s` 进行一次战术评估。

#### 2.6.1 第一层：攻击或中立

1. 上一次攻击的后续策略要求必须进入中立状态时，选择中立行为。
2. 玩家当前仍处于 Knockdown、尚未起身时，选择中立行为。
3. Boss 当前压力达到硬限制，或预测下一招会超过当前阶段硬上限时，选择中立行为。
4. 连续攻击次数达到上限时选择中立行为：P1 最多 2 次、P2 最多 3 次、P3 最多 4 次。连续攻击指多次攻击之间没有穿插中立行为。
5. 其他情况选择攻击行为。
6. 攻击行为选择失败时，重新选择中立行为。

后续确认补充：

- 正确破解奖励窗口内必须保持 Recovery，不进入本层战术选择。
- 降压模式禁止启动所有 Attack；合法 Reposition 可作为专用减压动作启动，Reposition 不可选时进入 Approach / Strafe。
- 强制中立时完全禁止攻击；第二层中提到的 Ranged/GapClose 检查只适用于“允许攻击但选招失败”的普通评估，不得绕过 MustEnterNeutral、奖励锁、压力硬限制或连续攻击上限。
- 只有 Approach 或 Strafe 实际持续达到 `0.3s`，才算完成一次非攻击中立行为并清零连续攻击次数。

#### 2.6.2 第二层：中立行为选择

攻击后摇和前进状态下：

- 距离超过 `11m`：持剑前进到 `11m` 内。
- 距离超过 `9m`：判断剑气三连击是否可以选择；如果不可选，继续前进。
- 距离超过 `3.5m`：判断黄光瞬移下砸或红光四连突进是否可以选择；如果不可选：
  1. 判断是否只因冷却问题且很快恢复；如果是，开始侧向移动等待。
  2. 否则判断玩家是否持续后退；如果是，开始前进。
  3. 否则根据权重随机选择前进或侧移。
- 距离小于 `3.5m` 且没有技能可用：进行侧移。

侧移状态下：

- 距离超过 `3.5m` 且没有可选攻击：玩家持续后退时开始前进，否则维持侧移。
- 距离小于 `3.5m`：维持侧移并等待技能选择。

后续确认补充：

- 普通中立只有 Approach/Strafe；“侧向移动等待”仍然是 Strafe，不建立 Wait。
- Approach 与 Strafe 都必须最短维持 `0.3s`。
- “根据权重随机”固定为 Approach:Strafe = `1:1`。
- 冷却“很快恢复”的项目初始阈值为剩余冷却不超过 `0.75s`；必须是候选在其他硬条件均满足时仅被冷却阻止。
- 原文没有定义距离恰好等于 `3.5m` 时的中立分支。`[实现约定]` 为保证边界稳定且不出现空分支，普通中立选择将 `<= 3.5m` 归入近距离 Strafe，只有 `> 3.5m` 才进入中距离分支；该约定不改变动作池表中招式自身的 `3.5m` 边界资格。

#### 2.6.3 第三层：攻击行为选择

1. 确定攻击选择池：
   1. 先根据距离排除攻击池。
   2. 再根据 Boss 是否处于普通受击状态排除攻击池。
   3. 再根据 Boss 上一次攻击对应的攻击池是否超过最近三次攻击允许出现次数排除攻击池。
   4. 再根据压力模式排除攻击池。
   5. 对剩余攻击池中的所有攻击执行硬性条件判断，排除不可用攻击和空攻击池。
2. 对剩余攻击池和池内攻击进行硬限制与偏好选择：
   1. 对剩余可用攻击池进行偏好性选择。
   2. 对选中攻击池中的剩余可用攻击进行偏好性选择。

项目补充：最终在合格候选内按权重随机，不截断 Top 3；所有偏好都不能绕过硬过滤。

### 2.7 动作池规则表

| 动作池                 | 包含动作                     | 距离硬限制 | 普通受击硬限制       | 压力模式硬限制 | 压力偏好   | 玩家状态偏好   | 最近三次攻击池上限 |
| ------------------- | ------------------------ | ----- | ------------- | ------- | ------ | -------- | --------- |
| HitEscapePool：受击逃脱池 | 纯后撤、后跳接剑气、后跳突进二连         | 0～3.5 | 必须处于普通受击反制上下文 | Attack 禁止；Reposition 允许 | 无偏好    | 无偏好      | 1         |
| CloseDuelPool：近战决斗池 | 高速反手剑、延迟上挑二连、左闪三连击、右闪六连击 | 0～3.5 | 不限制           | Attack 禁止 | 无偏好    | 无偏好      | 不限制       |
| GapClosePool：追击接近池  | 黄光瞬移下砸、红光四连突进            | 3.5～9 | 普通受击时禁止       | Attack 禁止 | 偏好中低压力 | 偏好玩家经常后撤 | 不限制       |
| SpecialPool：高危特殊池   | 黄光居合三连、站立红四连＋黄光、次元斩      | 0～3.5 | 不限制           | 降压模式禁止  | 偏好低压力  | 偏好玩家经常防御 | 2         |
| RangedPool：远程控制池    | 剑气三连击                    | 6～11  | 普通受击时禁止       | 降压模式禁止  | 偏好中压力  | 无偏好      | 1         |

HitEscapePool 通常只能在普通受击允许返回且受击反制评分通过时使用。唯一上下文例外是降压模式中的 `BossActionKind.Reposition`：它可以绕过 HitStagger 上下文要求，但仍必须通过 Enabled、阶段、距离、冷却、MotionProfile、空间预筛选和最近使用限制；同池 Attack 不得借此例外启动。受击反制评分通过后，并不强制选择 HitEscapePool；CloseDuelPool、SpecialPool 等未禁止 HitStagger 上下文的动作池也可以参与同一次反制候选。

`[后续确认]` 当前 Demo 未给任何 Raven 动作配置“频繁闪避”偏好，该统计不会影响决策；为避免保留无效数据链，CloseDuelPool 的原“偏好玩家经常闪避”已删除。GapClosePool 的持续后退偏好和 SpecialPool 的频繁防御偏好仍直接参与动作池权重。

### 2.8 单个攻击行为硬限制表

| 攻击       | Distance | Forward/Backward Clearance | AngleLimit | Cooldown | AllowedPhases | 压力模式 | 玩家状态 | Boss 状态 | 压力值       | 最近三次攻击内允许次数 |
| -------- | -------- | -------------------------- | ---------- | -------- | ------------- | ---- | ---- | ------- | --------- | ----------- |
| 高速反手剑    | 0～2.6    | 0.5/-                      | 70°        | 2s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 延迟上挑二连   | 0～2.6    | 0.5/-                      | 20°        | 2s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 左闪三连击    | 0～3.5    | 0.5/-                      | 90°        | 4s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 右闪六连击    | 0～3.5    | 0.5/-                      | 180°       | 4s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 黄光居合三连   | 0～3.5    | 0.5/-                      | 90°        | 6s       | P2、P3         | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 后跳突进二连   | 0～2.6    | 0/2.2                      | 20°        | 4s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 黄光瞬移下砸   | 3.5～9    | 0.5/-                      | 45°        | 6s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 红光四连突进   | 3.5～9    | 0.5/-                      | 40°        | 6s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 站立红四连＋黄光 | 0～3.5    | 0.5/-                      | 40°        | 6s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 次元斩      | 0～3.5    | 0.5/-                      | 180°       | 10s      | P2、P3         | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 剑气三连击    | 6～11     | 0/0                        | 90°        | 10s      | P2、P3         | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 后跳接剑气    | 0～3.5    | 0/2.2                      | 45°        | 6s       | P2、P3         | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |
| 纯后撤      | 0～2.6    | 0/2.2                      | 30°        | 4s       | P1、P2、P3      | 无    | 无    | 无       | 预测不得超过硬上限 | 1           |

### 2.9 单个攻击行为偏好表

| 攻击       | P1/P2/P3 权重 | Boss 状态偏好        | 压力偏好   |
| -------- | ----------- | ---------------- | ------ |
| 高速反手剑    | 30/18/18    | 无                | 无偏好    |
| 延迟上挑二连   | 30/18/18    | 偏好上一攻击为高速反手剑     | 无偏好    |
| 左闪三连击    | 28/28/30    | 无                | 无偏好    |
| 右闪六连击    | 28/28/31    | 无                | 偏好中低压力 |
| 黄光居合三连   | 0/14/18     | 偏好低护盾值、低血量、接近转阶段 | 偏好中低压力 |
| 后跳突进二连   | 20/20/22    | 无                | 偏好中低压力 |
| 黄光瞬移下砸   | 15/20/23    | 无                | 无偏好    |
| 红光四连突进   | 15/20/24    | 无                | 无偏好    |
| 站立红四连＋黄光 | 10/15/18    | 无                | 偏好中低压力 |
| 次元斩      | 0/8/12      | 无                | 偏好低压力  |
| 剑气三连击    | 0/12/15     | 无                | 偏好中压力  |
| 后跳接剑气    | 0/16/18     | 无                | 偏好中低压力 |
| 纯后撤      | 15/15/15    | 无                | 偏好高压力  |

2026-09-11 的出现率校准只放宽上述权重、距离、角度、冷却和池级近期上限；三招仍保持 P1 权重为 0，也不能绕过阶段、压力硬上限、降压模式、受击上下文、MotionProfile 或空间预筛选。

## 3. 后续确认的 AI 细化参数

本章补充原文未给出精确数值的运行时初值。它们不能覆盖第 2 章的原始规则。

### 3.1 玩家行为记忆

- 统计窗口：`8s`。
- Guard 累计至少 2 秒：频繁防御。
- Boss 与玩家距离连续增加至少 `0.6s`：持续后退。
- Boss 只能读取玩家已经进入的公开状态和滚动历史，不读取当前帧按键。

`[当前实现]` `BossDecisionMemory` 的玩家行为部分只保留上述两项实际决策输入，不再统计频繁闪避。`BossPlayerTacticalSnapshot` 只传递玩家当前状态/持续时间、频繁防御和持续后退；它是本帧不可变事实，不并入跨帧 `BossDecisionMemory`。`BossActor.UpdateDecisionMemory` 每帧只解析一次玩家状态，依次更新玩家行为、压力与降压模式并返回该快照。低血、刚从受击返回和最近 PerfectGuard/PerfectEvade 收益没有正式消费者，已从快照链删除。攻击恢复追踪器内部的 Perfect 防御奖励截止时间仍继续约束 Boss 最终 Recovery，不属于玩家战术快照。

### 3.2 Boss 自身偏好阈值

- 低 HP：剩余 HP 不高于 `35%`。
- 低护盾：当前项目暂映射为剩余 Poise 不高于 `30%`。
- 接近转阶段：距离下一阶段阈值不超过 5 个百分点。

### 3.3 偏好倍率

- 普通偏好匹配：`×1.5`。
- 压力偏好匹配：`×1.4`。
- 压力明显不匹配：`×0.65`。
- 所有偏好乘积上限：`×2.5`。

## 4. 项目架构与数据映射

### 4.1 模块职责

| 模块 | 职责 | 不得承担 |
| --- | --- | --- |
| BossActor | Boss 唯一 MonoBehaviour 入口；采样目标事实并按顺序提交状态、动作、移动、战斗和动画副作用 | 不保存第二套玩法配置 |
| BossBrain / BossActionSelector | 第一层攻击或中立、动作池过滤、池内权重和中立选择 | 不直接驱动动画、移动或命中 |
| BossActionSet | 阶段权重、动作池、压力成本、强度、偏好、后续策略和 AI 调参 | 不保存 HitNode 或 Recovery 奖励条件 |
| BossStateMachine | 保存唯一 `BossStateId`、状态计时和 Tick 决策 | 不直接执行系统副作用，不保存派生状态镜像 |
| BossActionRunner | 通过统一 `StartAction / Tick / EndAction` 推进 Attack 与 Reposition | 不决定选招资格，不通过 HitNode 是否为空推断动作类型 |
| Combat Timeline | HitNode、攻击时序、自然后摇和正确破解奖励规则 | 不保存 AI 权重与动作池 |
| MotionProfile / Emission | Boss 身体位移、MotionWarp 和独立攻击判定的空间配置 | 不读取 VFX 决定伤害 |
| VFX 路由与人工绑定 | 持续拖尾按 SourcePart 固定路由；直刺、剑气和其他一次性表现继续人工绑定 | 不反向生成 HitNode 或战斗数据 |

### 4.2 状态唯一来源

`BossStateId` 是状态机、Actor、Blackboard、Brain、动画、移动和反应系统共享的唯一运行时状态事实。当前项目不维护 `BossHfsmStateId`、`CurrentTopState`、`RepositionMode`、`ActionPhase` 或 `ReactionMode` 等可由 `BossStateId` 确定性推导的镜像。

| 状态 | 说明 |
| --- | --- |
| None | 未进入战斗或已停止 AI |
| Idle | 只用于战斗初始化和目标丢失；Recovery 可播放 Idle 动画但不进入 Idle 状态 |
| Approach / Strafe | 仅有的普通非攻击中立行为 |
| Action | Attack 与 RapidMoveBack 等 Reposition 的共同执行状态；显式 `BossActionKind` 决定是否创建攻击实例 |
| Recovery | 仅 Attack 在最后 HitNode 后进入的恢复锁；Reposition 完成后直接进入 Strafe |
| HitStagger / Knockdown / ShieldBreakStun | 普通受击硬直、倒地与护盾击破长眩晕 |
| Dead | 最高优先级终止状态 |

业务需要判断“行动中”“反应中”或“重定位中”时，直接检查对应 `BossStateId` 集合，不缓存第二份分类状态。只有未来出现独立父状态生命周期、父状态 Enter/Exit 或无法由单个 `BossStateId` 表达的新状态组合时，才重新评估真正的分层状态机。

死亡状态规则：

- HP 清空后的 Reaction 决策与执行计划必须以 Dead 为最高优先级，并停止当前 Action、Recovery 与位移；Dead 不得返回 Brain 或中立状态。
- `BossAnimationBridge` 进入 Dead 时独立、强制从起始帧播放 Raven Base Layer 状态 `CH_P_EVE_51|Eve_Stand_Dead2`，不得与 Idle/Recovery 共用 `M_Raven_BattleIdle01`。
- `Reaction_Boss_Dead` 的运行时 `AnimationStateName` 和 Animation Preview 状态名必须与上述死亡状态一致，预览不得循环；Animator 状态无自动退出，动画结束后保持死亡姿势。

Boss 独立防御护盾规则：

- 护盾固定为 20 格，只由玩家成功 PerfectGuard 扣除，每个正式命中结果扣 1 格；普通攻击、技能、PerfectEvade、GuardHit、Poise 与黄光不可格挡命中均不得扣除。
- 前 19 格扣除后仅在 HitNode 显式开启 `TriggersPerfectGuardBossStagger` 时进入短 HitStagger；最后一格归零时无论是否为连段末击、是否开启该标志，都在同一帧停止当前 Action、Recovery、剩余 HitNode 和位移，直接进入 `ShieldBreakStun` 并播放 `Result_Hit_JustParry`。
- `ShieldBreakStun` 持续 6 秒，状态计时与破盾 VFX 从护盾归零帧开始；到达 `Boss_HitStagger` 的 `Reaction_CanReturn=0.8s` 时只执行一次 `Result_ShieldBreak_Stun / Result_Weak_S` 动画切换，不等待被打断攻击的 Recovery。长眩晕期间普通攻击和技能前两段可继续扣 HP，但不得刷新或结束眩晕；只有 `SkillAttack + Knockdown` 可提前切入现有 Knockdown，护盾保持全空直到起身。
- 自然眩晕结束或技能击倒起身后护盾回满并返回 Strafe。死亡不回满；目标丢失、停止 AI 等异常退出立即清理眩晕并回满。
- Animator 状态 `Result_ShieldBreak_Stun` 使用 `CH_M_NA_53_Preview|Result_Weak_S` Motion，状态速度 `2.5`、无自动退出，动画约 `0.633s` 完成后保持末帧；6 秒状态时长与退出只由代码控制。

Skill1 三段受击规则：

- `Skill1_Hit1 / Skill1_Hit2` 的解析结果为 `SkillAttack + HitReaction`。Reaction Gate 允许时，第一段进入 HitStagger，第二段重播受击动画并把当前 HitStagger 计时重置为 0；两段都正常扣 HP，不提供无敌、霸体或伤害减免。
- 最近一次反应来自 `SkillAttack + HitReaction` 时，`BossActor.CanReturnFromReaction()` 使用 `max(Reaction_CanReturn 起点, 0.95s)`。Hit2 到 Hit3 的最大正常间隔约 `0.8s`，因此 Boss 不会在第三击前交回 Brain、移动或反击；后续未命中时仍会在 0.95 秒后解除，避免永久锁死。
- Skill HitReaction 不消耗普通攻击连续受击预算；普通 Light/Heavy HitStagger 仍使用原 Reaction Timeline 阈值与预算规则。
- `Skill1_Hit3` 保持 Knockdown，可从 HitStagger 切入 Knockdown。Boss 已处于 Knockdown 时前两段不得把它拉回站立；`ShieldBreakStun` 中前两段只扣血并保持长眩晕，第三段仍可进入 Knockdown。
- Reaction Gate 仍高于技能反应意图：Raven 黄光抓取等显式 Block Skill 的承诺段只扣血，不进入或刷新 HitStagger。Dead 始终最高优先级。

### 4.3 原文术语与项目类型

| 原文术语 | 项目类型或字段 |
| --- | --- |
| P1/P2/P3 权重 | BossActionSetEntry 的阶段权重 |
| Allow 攻击池 | BossActionPoolId / BossActionPoolPolicy |
| 后续必须进入中立 | BossActionFollowUpPolicy.MustEnterNeutral |
| 后续必须侧移 | BossActionFollowUpPolicy.MustStrafe |
| 自然后摇 | BossAttackTimelineAsset.NaturalRecoveryDuration |
| 正确破解奖励 | HitNodeId + AllowedOutcomes 奖励条件集合 |
| 前后净空 | Motion SelectionProfile 的 Forward/Backward Clearance |
| 黄光/红光/普通攻击 | CombatHitNodeData 的 Guard/PG/PE 能力组合 |
| 剑气/次元斩 | BossAttackSourcePart.Detached + 独立判定 Prefab |

### 4.4 ActionId 映射与当前启用状态

| 原文动画                         | ActionId                 | 当前启用 |
| ---------------------------- | ------------------------ | ---- |
| M_Raven_Slash                | Raven_Slash              | 是    |
| M_Raven_SlashChain           | Raven_SlashChain         | 是    |
| M_Raven_MoveCombo            | Raven_MoveCombo          | 是    |
| M_Raven_MoveChainCombo       | Raven_MoveChainCombo     | 是    |
| M_Raven_BetaChargeCombo      | Raven_BetaChargeCombo    | 是    |
| M_Raven_EvadeBackRush        | Raven_EvadeBackRush      | 是    |
| M_Raven_ChaseGrab            | Raven_ChaseGrab          | 是    |
| M_Raven_ChaseCombo           | Raven_ChaseCombo         | 是    |
| M_Raven_SlashCombo           | Raven_SlashCombo         | 是    |
| M_Raven_BurstAreaSlash + End | Raven_BurstAreaSlash     | 是    |
| M_Raven_SwordAuraCombo       | Raven_SwordAuraCombo     | 是    |
| M_Raven_EvadeBackSwordAura   | Raven_EvadeBackSwordAura | 是    |
| M_Raven_RapidMoveBack        | Raven_RapidMoveBack      | 是    |

## 5. Combat、Motion 与表现补充

### 5.1 Recovery 数据表达

- Timeline 用 `HitNodeId + AllowedOutcomes` 条件集合表达正确破解奖励。
- 一条奖励规则的全部条件满足时才生效。
- 同一攻击多条规则同时满足时取最晚 RewardEndTime。
- Detached 剑气或区域判定仍携带原 `AttackInstanceId` 用于命中去重，但 Recovery 只追踪当前攻击；不保存跨攻击的历史恢复追踪器，也不支持上一攻击在下一攻击开始后补写破解奖励。
- Attack 进入最后 HitNode 后摇后可以进入 Recovery，但动作剩余计时、Motion 尾段和 Animator 自身状态机仍可继续推进。

### 5.2 HitNode 与玩家反应

| 类型 | CanBeGuarded | CanBePerfectGuarded | CanBePerfectEvaded | 说明 |
| --- | --- | --- | --- | --- |
| 普通攻击 | 是 | 是 | 是 | Guard、PG、Evade、PE 均有效 |
| ChaseCombo 红光 | 否 | 是 | 是 | 全部节点普通 Guard 无效；仅最后节点 PG 可触发 HitJustParry |
| 黄光 | 否 | 否 | 是 | 只能 PE，不产生 GuardHit 或 PG 奖励 |
| 剑气/次元斩 | 否 | 否 | 是 | 只能 PE；战斗判定与 VFX 解耦 |

SlashCombo 是“前四次普通可防御节点＋最终黄光”，不等同于 ChaseCombo 全红光语义。

### 5.3 新招 HitNode 初值

以下时间和伤害仍是迁移初值，必须逐帧校准：

| 动作 | 初始 HitNode 中心时间 | 空间来源 | 初始 Damage/PoiseDamage/GuardDamage |
| --- | --- | --- | --- |
| BetaChargeCombo | 2.20 / 3.80 / 5.783s | Weapon | 前两段 24/18/0；末段 34/28/0 |
| BurstAreaSlash | 2.45 / 3.73s | Detached Area / Detached Slam | 范围爆发 24/18/0；下砸 34/28/0 |
| SwordAuraCombo | 起点 0.5667 / 0.8333 / 2.0667s | Detached Linear | 前两段 24/18/0；末段 34/28/0 |
| EvadeBackSwordAura | 起点 1.7833s | Detached Linear | 末段 34/28/0 |
| RapidMoveBack | 无 | 无 | 无 |

### 5.4 Motion

- 出招资格读取 MotionProfile SelectionProfile。
- RootMotionWarped 使用窗口级原始总位移预算和 Scale/Skew Warp。
- Animator Root Motion 只由 `BossRootMotionRelay.OnAnimatorMove` 提取并转交 MovementSystem；运行时不调用 `Animator.Update / Rebind`，也不切换 UpdateMode 或 applyRootMotion。Raven 当前 14 个动作使用 `Assets/Animator/RavenAuthored/` 下的单 Clip：骨骼、Root Motion、BodyColor、Visibility 和 Animation Event 一起由 Base Layer 求值，不再使用 BodyColor 同步 Override 层或 AvatarMask。正式应用必须同时满足 MovementSystem 当前层为 Action、MotionProfile 模式为 RootMotionWarped；CodeDriven、Reaction、Idle 和 Recovery 收到的动画 delta 均不得移动 Boss。
- CodeDrivenWarped 用于后撤、追击、攻击落点适配和 Orbit。
- 所有身体位移最终经过 CharacterController.Move 与环境碰撞裁剪。
- 玩家身体不是 Boss 攻击、后撤或突进的硬阻挡；玩家侧软互斥维护最低距离。
- Warp 超出能力时允许自然 miss，不瞬移、不无限追踪。
- 零位移 `FaceTarget` CodeMove 只在配置窗口内以受限角速度更新朝向，不提交 CharacterController 位移，也不在 Active / Recovery 开启全局吸附。
- SwordAuraCombo 使用 `0.2167～0.8333s` 与 `1.7167～2.0667s` 两段 `720°/s` 跟踪窗口；EvadeBackSwordAura 保留动画 Root Motion 后撤，并在 `1.4333～1.7833s` 使用同速跟踪窗口。
- 每枚 Detached 剑气在 HitNode 首帧按当时 Boss 水平正前方锁定直线方向；生成后不随 Boss 后续转向，也不追踪玩家。
- RapidMoveBack 继续使用固定后撤 CodeMove；它不调用 AttackExecutor，AttackInstanceId 恒为 0。

### 5.5 Animator 动画衔接

- BossAttack 与 BossReposition 只通过 Timeline 资产级 AnimationStateName 指定起手状态。
- BossActionRunner 在动作开始时调用一次 BossAnimationBridge.PlayAttack。
- 单动画招式保持当前 Animator 状态自然播放。
- BurstAreaSlash 起手播放 M_Raven_BurstAreaSlash，随后由 Raven.controller 的 Exit Time 自动转场到 M_Raven_BurstAreaSlashEnd。
- Animation Preview 可以为连续动画保留多个预览 Clip，但只负责编辑器逐帧采样，不是运行时数据源。

### 5.6 Detached 战斗判定与 VFX

- Boss 根对象人工绑定 `ActionId + HitNodeId` 到独立判定 Prefab。
- Linear Prefab 使用 `TriggerVolume` 保存速度、方向和生命周期；瞬时 Area Prefab 使用 `InstantPulse` 在 HitNode 首帧按 SphereCollider 世界中心与半径查询一次。
- 生成后不挂到 Boss 骨骼，攻击结束后可按自身生命周期继续存在。
- 一次攻击内按 `AttackInstanceId + HitNodeId + Target` 去重。
- VFX 继续使用独立人工 HitNode 绑定；没有 VFX 不影响战斗判定，没有战斗判定也不能由粒子反推命中。
- `BurstAreaSlash_1` 的 `Spatial section` 以 `LeadTimeSeconds = 0.25s` 提前生成；伤害仍只由约 `2.383s` 的 HitNode 首帧 `InstantPulse` 结算。粒子继续可见或循环时不得再次伤害后来进入的玩家。
- `BurstAreaSlash` 正式只有 `_1` 范围爆发和 `_3` 下砸两段伤害；旧 `_2` 绑定与三段初值作废。

### 5.7 Raven 单武器左右手切换

- Raven 场景只保留一个 `RavenMonster_Weapon`，默认挂在右手 `SC_WeaponConstraint`。
- `BetaChargeCombo_2 / MoveSlash_2 / ChaseSlash_4 / SlashCombo_2` 激活时，同一武器重挂到左手 `SC_BurstStanceConstraint`；HitNode 结束、攻击中断、死亡或停止 AI 时恢复右手。
- 换父节点后固定恢复 canonical 武器初始化时的本地 Position、Rotation 与 Scale，不保持世界坐标；不同手的握持差异由 Socket 姿态承担。
- 左右手持武器统一使用 `SourcePart=Weapon`；腿击和 Detached 不触发换手。换手发生在粒子、拖尾与正式 Hitbox 查询之前，且换手帧先停止 SmoothTrail 采样。
- 持续拖尾由 `BossAttackExecutor` 直接按 SourcePart 路由：`Weapon -> RavenMonster_Weapon/SlashTrailVFX`、`LeftFoot -> Ab-L-Calf-Tw1/SlashTrailVFX`、`RightFoot -> Ab-R-Calf-Tw1/SlashTrailVFX`。同帧多个节点按采样器聚合；`Detached / Body / Grab / LeftHand / RightHand / None` 不触发固定拖尾。
- 粒子绑定具有更高表现优先级：只要某个 `AttackId + HitNodeId` 存在于 `BossHitNodeParticleVfxController`，该节点就不收集 SourcePart 固定拖尾，即使它是 Weapon 或足部节点；同帧其他未绑定粒子的节点仍可独立启用自己的固定拖尾。
- `BossSlashTrailVfxController` 与拖尾 Timeline / HitNode 人工清单已删除；直刺粒子仍由 `BossHitNodeParticleVfxController` 人工绑定，不复用持续拖尾路径。
- 手工 Hitbox Anchor 必须 activeInHierarchy 且 Collider enabled；隐藏武器或禁用 Collider 不得参与 OverlapBox。

### 5.8 Raven 可动画身体覆盖色

- `BossBodyColorController.bodyColor` 直接录制在 `RavenAuthored` 完整动作 Clip 的 Animator 根组件曲线上；RGB 是 HDR 覆盖色，Alpha 是效果权重。
- 只有 MoveChainCombo、MoveCombo、RapidMoveBack 三个动作继承旧人工表现曲线和 Animation Event，关键帧时间保持绝对秒数；表现曲线短于源动作表示只编辑动作前段，不缩放、不延长动作。其余 11 招在源动作全长内保持 `BodyColor=(0,0,0,0)、Visibility=1`。
- 七个身体材质统一使用 `Project EVE/Raven Body Overlay`，在 URP Lit 主 ForwardLit 完成贴图、光照与发光之后执行 `lerp(finalColor, BodyColor.rgb, BodyColor.a)`，因此原贴图黑色区域也能完整覆盖。
- 独立 `visibility` 曲线按 `1=完全显示、0=完全隐藏` 控制身体、唯一武器和 `FX_BossWeaponIdleGlow`；身体保持原始 Opaque / TransparentCutout 与 ZWrite，通过 4x4 屏幕空间 Bayer 阈值逐像素裁剪，避免多层头发透明排序错误；武器 authored Alpha、LineRenderer 起止 Alpha、BaseNode Alpha 和 Light authored 强度分别乘以 Visibility。
- Visibility 为零时只禁用对应 Renderer / LineRenderer / Light，不关闭武器或光效 GameObject，因此换手、Collider、HitNode 与挂点生命周期保持不变；重新大于零时恢复 authored 启用状态。
- 所有材质运行时属性都按材质槽写入 `MaterialPropertyBlock`，不得调用 `Renderer.material`、`new Material` 或运行时 `Shader.Find`。SlashTrailVFX、Detached、粒子、HitNode、伤害和 Boss AI 不读取该表现参数。

## 6. 当前实现状态与差距

### 6.1 已实现或已配置

- BossActor 唯一入口与 `BossStateId` 单一状态源。
- 五个动作池、阶段压力、70%/30% 压力提交和自然衰减框架。
- 8 秒频繁防御记忆、持续后退计时和偏好倍率框架；未参与决策的频繁闪避统计及玩家战斗窗口镜像已删除。
- Recovery 最大截止时间和 Timeline 奖励条件。
- 普通受击三次预算与 25%/60%/100% 受击反制评分。
- 统一 Action 生命周期、资产级起手动画状态和 Detached 判定体；Attack 与 Reposition 共用 `BossStateId.Action`，不存在独立位移动作状态。
- 13 个 Timeline、ActionSet 条目和 MotionProfile 配置。
- Demo 场景 Detached 绑定和场地边缘探针。

### 6.2 已配置并启用，待持续人工验收

- Raven_BetaChargeCombo
- Raven_BurstAreaSlash
- Raven_SwordAuraCombo
- Raven_EvadeBackSwordAura
- Raven_RapidMoveBack

当前 5 招均已在 ActionSet 中启用。逐帧 HitNode、Motion、投射速度、区域范围、VFX、动画衔接和实战出现率仍需持续人工验收；2026-09-11 已先对 BetaChargeCombo、SwordAuraCombo、EvadeBackSwordAura 做局部出现率校准。

### 6.3 当前已知实现差距

- `[当前实现]` 普通中立 Retreat、Wait 决策和 Strafe 后硬编码强制攻击链路已删除；`M_Raven_Caution_Bw` 不再作为 Animator 状态或场景字段保留。
- `[当前实现]` Approach/Strafe 使用 ActionSet 的 `NeutralMinDuration = 0.3s`，完成后才清零连续攻击与 `MustEnterNeutral`；中距离普通选择固定 `1:1`，强制中立在攻击池检查前返回中立决策。
- `[当前实现]` Brain 入口不再二次检查攻击奖励锁；进入 Brain 即表示 Recovery、HitStagger、HitJustParry 奖励锁等生命周期边界已经允许战术决策。正确破解奖励仍由 Recovery / `CanReturnFromReaction` 阻止提前进入 Brain。
- `[当前实现]` HitStagger 可返回后走统一三层决策。受击反制评分通过时，HitEscapePool 与其他未禁止受击上下文的动作池共同进入候选，不再由 HitEscapePool 优先抢占；评分未通过时本次不启动动作候选，Brain 只按普通中立规则选择 Approach 或 Strafe。
- `[当前实现]` 降压模式禁止所有 `BossActionKind.Attack`。HitEscapePool 的 Reposition 可绕过 HitStagger 上下文要求作为正式减压动作，但仍遵守其他硬过滤；不可选时回退 Approach / Strafe。`MustEnterNeutral`、玩家仍 Knockdown、当前压力达到阶段硬上限或连续攻击达到阶段上限时，仍先于动作池检查执行普通中立。Brain 与 Selector 使用 `BossDecisionMemory.PressureDecayMode` 的进入/退出阈值滞回结果，不用当前压力瞬时重算降压模式。
- 原文单招最近使用限制、上一招偏好、低护盾/低血/接近转阶段偏好和逐招压力偏好必须重新逐项对照 ActionSet，不能因为综合表存在就视为已经迁移。
- `[当前实现]` RapidMoveBack 成功启动时一次性减压 `20`，动作期再按 `-10/秒` 自然减压；不创建 AttackInstance、不提交 70%/30% 攻击压力，也不增加连续攻击次数，但会记录冷却、动作与动作池历史。启动失败不减压，成功后被打断不返还；PressureDecayMode 由下一次正式模式更新按退出阈值关闭。Attack Recovery 按 `-8/秒` 自然减压。
- `[当前实现]` 单次攻击命中压力由当前 Recovery Tracker 按 AttackInstanceId 聚合：HitReaction 累计上限 `+4`，Knockdown / Dead 累计上限 `+10`，升级只补差值；上一攻击的迟到 Detached 结果不会污染新攻击。
- `[当前实现]` HitStagger 删除超时强制 Strafe 兜底；`Reaction_CanReturn` 运行时语义是允许交回 Brain 的起点，不要求持续停留在有限窗口内。受击动画回 Idle 只由 Animator 负责，`BossStateId` 不因此离开 HitStagger。
- 新 5 招的节点时间、Detached 参数和 Motion 数据仍需人工验证；其中三招的选招门槛已于 2026-09-11 完成第一轮出现率校准。
- Raven.controller 由用户手工维护，代码只按状态名播放；Burst 自动转场需要持续回归。
- 当前实现状态只描述工程事实，不改变第 2 章目标规则。

## 7. 验收标准

### 7.1 数据与编辑器

- 13 个 ActionId 在 Catalog、ActionSet、MotionProfile 和 Timeline 间引用完整。
- Timeline 不保存 AI Weight；ActionSet 不复制 HitNode 或 Recovery 条件。
- 每个 Detached HitNode 有有效 Emitter 绑定；骨骼 HitNode 有对应 Anchor。
- Burst 起手和 End Animator 状态及自动 Exit Time 转场有效。
- 新 5 招保持 enabled，并在完整人工验收前视为仍可继续调参的内容。

### 7.2 AI

- P1/P2/P3 连续攻击上限严格为 2/3/4，只有完成至少 `0.3s` 的中立行为才清零。
- 强制中立期间任何攻击池都不得启动。
- 中距离普通中立选择严格为 Approach/Strafe `1:1`。
- 预测压力超过硬上限时动作恒不可选。
- 降压模式禁止所有 Attack；合法 RapidMoveBack 可在非 HitStagger 上下文启动，成功启动立即 `-20`，执行期间继续 `-10/秒`，不可选时回退 Approach / Strafe。
- HitEscape 只在普通受击反制上下文使用；受击反制评分通过不等于必须选择 HitEscapePool。
- 动作池最近三次限制、单招最近一次限制、MustEnterNeutral 和 MustStrafe 生效。
- Boss 不读取玩家当前帧输入。

### 7.3 战斗

- ChaseCombo 普通 Guard 失败，PG/PE 成功，仅最后节点 PG 触发 HitJustParry。
- 黄光、剑气和次元斩只能 PE；只有末节点 Knockdown。
- SlashCombo 前四节点允许 Guard/PG，最终黄光只能 PE。
- 自然后摇与正确破解奖励从正确时间起算并取最大截止时间。
- 同一攻击的重复 HitReaction 总共只加 `4`；普通命中后再击倒总共只加 `10`，先击倒后普通命中也不得超过 `10`。
- 动画先结束只切 Idle 动画，不切 Idle 状态。
- HitJustParry 动画自动回 Idle 时仍保持 HitStagger；只有 `Reaction_CanReturn` 起点已到且恢复锁解除后才交回 Brain。
- Detached 实例脱离 Boss 后继续运动、命中并按原 AttackInstanceId 去重。
- RapidMoveBack 不生成攻击实例或 HitNode，成功启动只提交一次 `-20` 且不增加连续攻击次数，完成后至少 Strafe `0.3s`。

### 7.4 人工逐招验收顺序

1. BetaChargeCombo
2. RapidMoveBack
3. EvadeBackSwordAura
4. SwordAuraCombo
5. BurstAreaSlash

每招依次验证动画、HitNode、Motion、命中、防御结果、恢复奖励、VFX 和 AI 选择；当前保持启用，单招验收通过后冻结对应参数。

### 7.5 边界反例

- 玩家刚按 Guard 但尚未进入 Guard 状态：Boss 不应读取按键并即时改选黄光。
- 强制中立期间即使 Ranged/GapClose 完全合格：仍不得启动攻击。
- Decay 中 EvadeBackRush 即使与 RapidMoveBack 同属 HitEscapePool：仍不得启动，因为它是 Attack。
- 普通 Approach / Strafe：不得获得 Reposition 的一次性 `-20`。
- SwordAura 的 ParticleSystem 没有播放：投射物战斗判定仍应存在。
- 下一攻击开始前，上一攻击的 Detached 判定必须结束；不得依赖跨攻击的迟到命中补写破解奖励。
- 上一攻击的 Detached 结果在新 AttackInstance 开始后到达：不得增加新攻击压力，也不得触发 PG/PE 减压或奖励窗口。
- RapidMoveBack 正在执行：不得创建 AttackInstance、增加攻击压力或激活 HitNode。
- Recovery 动画结束但奖励窗口仍在：可以播放 Idle 动画，但不得进入 Idle 状态或重新选招。
- 压力超过 Special 允许范围：不能因为基础权重最高而绕过硬上限。
- MotionWarp 无法抵达玩家：允许攻击挥空，不得通过瞬移补偿。
- 普通中立只允许 Approach/Strafe；玩家行为记忆中的“持续后退”和招式内部后撤不得误删。

## 8. 原文覆盖矩阵

覆盖矩阵用于证明《渡鸦招式参数.md》的规则没有在重新分类时被摘要删除。

| 原文范围 | 原文内容 | 本文落点 | 处理方式 | 覆盖结果 |
| --- | --- | --- | --- | --- |
| 2～12 | 自然后摇、奖励后摇、PG/PE 时间和战术返回 | 2.1.1～2.1.2 | 原意保留＋公式格式化 | 完整 |
| 15～18 | Idle、HitJustParry、最大截止和强制打断 | 2.1.3 | 原意保留＋调用语义补充 | 完整 |
| 23～39 | 13 招＋2 个中立动作总表及全部原始列 | 2.2 | 逐行保留＋排版微调 | 15/15 行 |
| 41～43 | P1/P2/P3 血量范围 | 2.3 | 原意保留 | 3/3 条 |
| 45～48 | 黄光、剑气、倒地、红光规则 | 2.3 | 原意保留＋次元斩补充 | 4/4 条 |
| 55～58 | Boss 受击反击机制 | 2.4 | 原意保留＋后续概率细化 | 4/4 条 |
| 64～68 | 压力定义和高压力行为目标 | 2.5.1 | 原意保留 | 完整 |
| 72～77 | 阶段目标、软上限、硬上限和软上限后行为 | 2.5.2 | 原意保留＋普通 Retreat 修订注释 | 3/3 行 |
| 81～85 | 降压模式进入/退出阈值 | 2.5.3 | 逐行保留 | 3/3 行 |
| 90～97 | 70%/30% 与玩家受击、倒地、边缘加压 | 2.5.4 | 原意保留＋边缘距离映射 | 完整 |
| 99～105 | PG/PE 和完整连段减压 | 2.5.5 | 逐行保留＋剑气/次元斩补充 | 5/5 行 |
| 107～117 | 7 类自然衰减和禁止洗压力 | 2.5.6 | 逐行保留＋错字修正＋待确认注释 | 7/7 行 |
| 125～130 | 第一层六步决策 | 2.6.1 | 逐条保留＋强制中立后续修订 | 6/6 条 |
| 135～146 | Approach/Recovery 与 Strafe 的全部分支 | 2.6.2 | 逐分支保留＋1:1、0.3s、0.75s 注释 | 完整 |
| 151～159 | 第三层池过滤与两级偏好选择 | 2.6.3 | 顺序完整保留 | 完整 |
| 164～170 | 五个动作池完整规则表 | 2.7 | 逐行逐列保留；CloseDuel 频繁闪避偏好按后续确认删除并在表后留有修订依据 | 5/5 行 |
| 175～189 | 单招硬限制表 | 2.8 | 独立表逐行逐列保留 | 13/13 行 |
| 194～208 | 单招偏好表 | 2.9 | 独立表逐行逐列保留 | 13/13 行 |
| 214 | 中立选招间隔 | 2.6 | 原意保留 | 完整 |

覆盖结论：原文所有章节、编号规则、表格列和表格行均已在正文中获得明确落点，没有“未合并”项。项目补充均位于原文规则之后，并以来源性质区分。
