# Boss Runtime

本目录承载 Raven Boss 运行时代码。当前目标架构已收敛为 `BossActor` 唯一 MonoBehaviour 入口，Actor 调度 State / Brain / Action / Movement / Combat / Reaction / Animation 等系统。

## 职责

- 由 `BossActor` 驱动 Boss 正式 Tick、外部启停、Root Motion 转发、玩家命中入口和 PerfectGuard 反制入口。
- 由 `BossStateMachine` 保存唯一 `BossStateId` 状态事实、状态计时和无副作用 Tick 决策；Brain、Actor、动画、移动与反应系统不再维护派生 HFSM 镜像。
- 由 `BossBrain / BossActionSelector / BossActionSet` 按“攻击或中立 → 动作池 → 池内过滤和阶段权重”选择动作，并维护阶段压力与滚动玩家行为记忆。
- 由 `BossActionRunner` 通过统一 `StartAction / Tick / EndAction` 推进 Attack 与无攻击实例 Reposition；两类动作共同使用数值为 5 的 `BossStateId.Action`。
- 由 `BossMovementSystem` 统一接收 Reposition、ActionMotion 与 Animator Root Motion 请求。
- 由 `BossCombatSystem` 执行 Boss HitNode 查询、语义过滤、Hurtbox 去重和 `PlayerCombatReceiver.ReceiveHit()`。
- 由 `BossReactionSystem` 处理玩家命中 Boss 后的 Dead、HitStagger、Knockdown 与 PerfectGuard Boss Stagger 反应决策。

## 主要子目录

- `Actor`：Boss 唯一 MonoBehaviour 入口。负责引用绑定、正式 Tick 调度、外部 API、目标事实采样、正式 `BossBlackboard` 组装和系统提交顺序。`TickRuntime` 通过单一 `UpdateDecisionMemory` 每帧只解析一次玩家状态并顺序更新玩家行为、压力与降压模式，再把同一只读玩家快照交给 StateTick、Recovery 和 Brain。
- Demo 标题阶段使用显式停止的 Boss；开场镜头到位后由 `StartBossAiWithInitialAttackCooldowns` 启动。该入口只为启用的 Attack 写入各自正式 Timeline Cooldown，不污染最近动作历史，也不阻止 Reposition / Approach / Strafe。
- `Brain`：正式 `BossBrain / BossActionSelector`；`BossDecisionMemory` 由 Actor 独占，统一保存动作冷却与重复选择、主动压力与连续受击、玩家频繁防御与持续后退三组短期决策事实，并保留各组独立清理入口。阶段压力、池策略、P1/P2/P3 权重、偏好和后续策略只来自 `BossActionSet`。进入 Brain 表示 Recovery / HitStagger / 奖励锁边界已经解除；普通受击反制评分通过时，HitEscapePool 与其他未禁止受击上下文的动作池共同参与候选。Decay 模式禁止所有 Attack，合法 Reposition 可绕过 HitEscape 的 HitStagger 上下文要求，不可选时回退 Approach / Strafe。
- `Actions`：正式 `BossActionRunner`；动作类别只读取显式 `BossActionKind`，不能由 HitNode 是否为空推断。Attack 会创建攻击实例、推进 HitNode 并进入 Recovery；Reposition 只推进动作计时和 MotionProfile，实例 ID 恒为 0，成功启动时一次性减压 `20`，执行期间继续按 `-10/秒` 衰减，完成后由 Actor 强制进入至少 `0.3s` Strafe。两类动作都只在起手播放资产级 `AnimationStateName`，后续衔接由 Animator Controller 负责。
- `AI`：Boss 动作类型、动作池配置、唯一状态枚举和 Raven 招式目录。
- `State`：`BossStateMachine` 保存唯一 `BossStateId`、经过时间和无副作用 Tick 决策；它只返回本帧推进枚举，不直接选择 Strafe 或其他中立状态，状态切换副作用由 `BossActor` 统一提交。
- `Reaction`：`BossReactionSystem` 与反应执行计划。集中玩家命中 Boss 后的反应优先级、执行计划和 Knockdown 阶段 Tick 计划；具体状态提交和 HitStagger 连续刷新预算由 `BossActor` 内联处理。
- `Config`：`BossRuntimeConfigResolver`。只集中读取 Boss Reaction Timeline；普通攻击必须经过 ActionSet 与 Selector，不存在 Strafe 后硬编码强制攻击入口。
- `Combat`：骨骼 Hitbox Anchor 与 `BossDetachedAttackEmitter / BossDetachedHitVolume` 两条空间执行路径最终都复用 `BossCombatSystem`、Combat resolver、反馈与 `AttackInstanceId + HitNodeId + Target` 去重。Detached 显式区分移动剑气 `TriggerVolume` 与瞬时范围 `InstantPulse`；两种模式都继承 `BossAttackExecutor` 的 HitMask 与 OverlapNonAlloc 容量，独立判定不读取 VFX 或 ParticleSystem。`BossShieldBreakStunVfxController` 只在正式长眩晕进入/退出和最后 `0.7s` 预警边界管理三阶段身体特效；`StunLoop/StaggerOrbit3D` 由一个头顶水平旋涡 Mesh 粒子和三个同步公转 Billboard 星点组成，四者均为单 Burst 并由同一递归播放/清理流程管理，不接入武器表现或战斗结算。
- Raven 单武器换手由 Boss 根对象的 `BossWeaponHandController` 管理：默认右手，四个人工绑定的 Weapon HitNode 激活时切到左手，窗口结束和所有攻击中断路径回右手。换手只恢复作者本地 TRS，不保持世界坐标；执行器先换手并停止 SmoothTrail，再生成粒子和执行正式判定。
- Boss 持续拖尾不再使用 Timeline / HitNode 人工白名单或独立控制器。`BossAttackExecutor` 按当前激活 HitNode 的 `SourcePart` 固定选择三个 `SlashTrailVFX`：`Weapon` 使用唯一 `RavenMonster_Weapon` 下采样器，`LeftFoot / RightFoot` 分别使用 Boss 对应小腿骨骼下采样器；其他 SourcePart 不触发这三套拖尾。若 HitNode 已配置在 `BossHitNodeParticleVfxController`，粒子表现优先且该节点不启用固定拖尾。
- `BossCombatSystem` 跳过 inactive GameObject 或 disabled BoxCollider 的 Hitbox Anchor，防止隐藏检测体参与手工 OverlapBox。
- `Movement`：正式 `BossMovementSystem`、Boss MotionWarp、Root Motion 转发和环境碰撞裁剪；当前 MovementSystem 统一接收 Reposition、ActionMotion 与 Animator Root Motion 请求，并以 `BossMotionController` 作为底层执行适配层。`BossRootMotionRelay.OnAnimatorMove` 是 Animator 位移的唯一提取入口，运行时不手动求值、不 Rebind、也不切换 `applyRootMotion`；是否应用由 `BossMovementLayer.Action + RootMotionWarped` 双重门控。Raven 的 14 个动作已提取到 `Assets/Animator/RavenAuthored/`，骨骼、Root Motion、BodyColor 与 Visibility 位于同一 Base Layer Clip，不再使用会参与 Animator 混合的同步 Override 层或 AvatarMask。零位移 `FaceTarget` CodeMove 可在动作指定窗口内只更新朝向，不提交 CharacterController 位移。Boss 动作位移不被当前玩家 target 身体硬阻挡，`StopDistance` 只表示远处接近停靠距离。旧 Shadow 动作位移请求快照、Motion DebugSnapshot、`BossMotionController` 每帧遥测属性、Gizmo 和选招拒绝原因缓存均已删除；出招预筛选只返回可行性布尔值。

普通非攻击中立只允许 `Approach / Strafe`。两者实际持续至少 `BossActionSet.Tuning.NeutralMinDuration`（Raven 为 `0.3s`）后才清零连续攻击与 `MustEnterNeutral`；中距离普通回退固定按 `1:1` 选择，强制中立期间不检查攻击池。`RapidMoveBack / EvadeBackRush / EvadeBackSwordAura` 的动作内后撤，以及玩家持续后退记忆，属于不同语义并继续保留。

玩家战术快照只保存正式决策实际消费的可见事实：当前状态与持续时间、频繁防御和持续后退。当前正式决策把玩家 Knockdown 作为强制中立条件，用当前受击状态暂停压力自然衰减，并把频繁防御 / 持续后退用于 Special / GapClose 池偏好与 Approach / Strafe 选择；不读取玩家 HP、刚从受击返回记录、Perfect 防御收益镜像或当前帧原始输入。

HitStagger 到达 `Reaction_CanReturn` 起点后才交回 `BossBrain`。普通未破盾 PerfectGuard 触发 HitJustParry 时，还必须等待当前攻击恢复锁结束；受击动画由 Animator 自动回 Idle 不会改变 `BossStateId.HitStagger`。护盾最后一格归零时例外：`BossReactionSystem` 同帧停止当前运行时、直接提交 `ShieldBreakStun` 并以 `Result_Hit_JustParry` 作为初始动画，6 秒状态计时和 VFX 立即开始；`0.8s` 的 CanReturn 只用于在同一状态内一次性切换到 `Result_ShieldBreak_Stun / Result_Weak_S`，不再等待 Recovery 或交回 Brain。

BossActor 观察 HP 软阶段从 Phase1 进入 Phase2、再进入 Desperation 的正式边界，并为每条边界排队一次 `Raven_BurstAreaSlash`。请求只在现有 Attack / Recovery / Reaction 安全结束后的 Brain 帧启动，优先于普通随机选招且不受冷却、近期使用或压力门控阻止；动作本身仍完整复用 ActionSet、ActionRunner、Timeline、Motion、HitNode 和正式提交链路。死亡不触发，同一阶段不重复，一次伤害跨两条阈值时保留两次请求。

HitStagger 交回 Brain 后走统一三层决策：先判断强制中立，再由 `BossActionSelector` 按动作池过滤和加权选择 Attack 或 Reposition。受击反制评分未通过时不启动动作候选，由 Brain 按普通中立规则进入 Approach 或 Strafe；评分通过时可以选择 HitEscapePool、后撤反击、近身反击等未禁止受击上下文的动作。

Attack 与 Reposition 动作都进入 `BossStateId.Action`，由显式 `BossActionKind` 区分执行语义。Reposition 可被普通受击直接打断；Attack 才读取自身 Timeline 的 Boss Reaction Gate。Reposition 配置必须无 HitNode 且使用 `MustStrafe`，Attack 配置必须至少有一个 HitNode，结构错误时拒绝启动。
- `Animation`：`BossAnimationBridge` 负责正式状态播放，代码混合统一使用固定秒数 `CrossFadeInFixedTime`；`Result_ShieldBreak_Stun` 使用 `CH_M_NA_53_Preview|Result_Weak_S` Motion、速度 `2.5` 且无自动转场。Raven 七个主体材质固定使用官方 `Universal Render Pipeline/Lit`，避免 Windows Direct3D 12 移除旧自定义 Forward SubShader 后出现粉色。`BossBodyColorController` 把可动画 HDR `bodyColor / hairColor` 写入独立 `RavenBodyColorOverlay` 蒙皮渲染器的每槽 `MaterialPropertyBlock`；该层复用主体网格、RootBone 与完整骨骼顺序，使用不依赖 URP Lit 内部文件的 `Project EVE/Raven Body Color Overlay Pass`。BodyColor 覆盖全部七槽，HairColor 只叠加槽 5/6；颜色 Alpha 为零时覆盖 Renderer 关闭。独立 `visibility` 在零值关闭身体与覆盖层，并继续淡出唯一武器和 `FX_BossWeaponIdleGlow`。14 个 `RavenAuthored` Clip 直接绑定 Base Layer，SlashCombo 额外包含四条 HairColor 曲线。
- `BossAnimationEventFunc` 与 Raven Animator 同挂在 `CH_M_NA_53_Preview`。`OnFastMoveParticleStart` 清除并递归重播显式绑定的 `star1`，`OnFastMoveParticleStop` 可由后续动画帧立即停止并清空同一组粒子；`OnYellowAttackParticleStart / OnRedAttackParticleStart` 调用 Boss 上显式绑定的 `BossAttackCueVfxController`。`OnPlayRedStartSound / OnPlayYellowStartSound / OnPlayRedChargeSound` 通过同对象专用 3D AudioSource 的 `PlayOneShot` 播放显式绑定音频，允许声音重叠，缺绑定时只报错并跳过。控制器分别重播黄光不可防提示或 SlashCombo 红光连续猛攻提示，播放任一提示前会清除另一提示，并在攻击结束、中断、状态切换或组件禁用时统一清理。所有动画事件都只处理动画帧表现，不参与 HitNode、伤害、位移或动作生命周期。

## 边界

- 不自动重绑 `Raven.controller` 的 Animator State motion。
- 贴身攻击必须来自手动 `BossAttackHitboxAnchor`；剑气 / 区域斩必须来自 `ActionId + HitNodeId` 人工绑定的独立判定 Prefab。两者都不能由 VFX 反推战斗数据。
- Detached 仍携带 `AttackInstanceId` 做命中去重，Recovery Tracker 只接受当前攻击实例；上一攻击的独立判定不得在下一攻击开始后补写命中压力或破解奖励。
- 单次攻击的正向命中压力按实例聚合：普通受击累计上限 `+4`，击倒或死亡累计上限 `+10`，结果升级时只即时补交差值。Recovery 期间按 `-8/秒` 自然减压，玩家处于受击硬直时暂停。
- 不在 Boss 位移层处理玩家伤害语义，伤害语义由 HitNode 和 Combat resolver 决定。
- 不把玩家身体 Collider 当作 Boss 攻击、后撤或突进的硬阻挡；玩家贴近距离由 Player 软互斥维护，环境仍是 Boss 位移硬阻挡。
- SwordAuraCombo / EvadeBackSwordAura 只在 MotionProfile 发射前窗口跟踪玩家；Detached 剑气生成后锁定直线方向，其他攻击与 Recovery 不复用该朝向窗口。
- Raven 主体只由官方 URP Lit 渲染，并保持原始 Opaque / TransparentCutout、ZWrite 与队列；HDR RGB 由第二个同网格蒙皮 Renderer 以 `ZTest Equal / ZWrite Off` 透明覆盖，透明覆盖材质不采样主体贴图，也不投射阴影。Visibility 的身体部分仅以零值开关 Renderer，不再提供中间值 Bayer 裁剪；武器和 IdleGlow 仍按中间值缩放 Alpha / 强度并在零值关闭视觉组件。SlashTrailVFX、Detached、粒子、HitNode 和伤害不读取该参数。运行时不得为此使用 `Renderer.material / new Material / Shader.Find`。
- 不保留 Boss 专用 DebugSnapshot 或 Debug Overlay；需要调试时按具体问题临时增加并在交付后收敛。

## 相关文档

- `doc/Boss AI 方案设计.md`（当前唯一正式方案入口）
- `doc/长期记忆/Boss.md`
- `doc/需求变更/Boss.md`
