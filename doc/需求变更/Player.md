# Player 需求变更

更新时间：2026-08-13

## 活跃规则

- Demo 正式战斗基线为玩家 `300/300 HP、0/32 BE`、Boss `1500 HP / 15 护盾`；不得把 `100000 HP` 或超过 MaxBE 的测试资源保存回正式场景。玩家与 Boss 的 Timeline 伤害继续作为唯一数据源，不增加阶段伤害倍率或运行时难度乘区。
- Skill1 的 `Cancel_MovementReturn` 当前为 `3.500～6.000s`。`PlayerSkillState` 只在该窗口内以实时 MoveInput 返回 Locomotion，不缓存移动输入；动作取消优先级为 `Evade > Skill > Attack > Guard > Move`。窗口外、无移动输入或更高优先级动作成立时不触发移动取消，SkillEnd 的自然返回规则不变。
- Skill1 三段共用 `CH_P_EVE_51/Trans/SkillEffect`：主粒子在 `0.500 / 0.900 / 1.650s` 三个 Motion 窗口起点分别重播；`hitEffect` 默认关闭，只接受 `DamageOnly / HitReaction / Knockdown / Dead` 正式结果，每段最多播放一次，并在 `1.000 / 1.367 / 2.333s` HitNode 结束时关闭。普通攻击、未命中、无敌忽略和 PerfectEvade 不触发；Skill 退出或中断必须清理。
- Skill1 三段 HitNode 为 `HitReaction / HitReaction / Knockdown`，真实时间为 `0.833～1.000 / 1.200～1.367 / 2.000～2.333s`。前两段可让 Gate 允许的 Boss 重新进入受击并刷新计时，第三段击倒；Skill 命中仍不计普通攻击 BE 奖励。
- Skill1 动作位移由 Timeline 的三个 `Skill1Forward` 窗口分段请求，只吸附当前 Lock-on 目标。Profile 阈值为 `3m / 60°`，每段 `0.28s / 最多 1.85m / 停距 1.15m`；无锁、越距、越角、丢目标或侧碰时不强制命中，也不自动搜索目标。
- 玩家资源只保留 HP、BE；默认/Demo BE 为 `0/32`。Skill1 费用为 8，PerfectGuard/PerfectEvade 各奖励 2。普通攻击每两个不同 `CurrentAttackInstanceId` 的有效命中增加 1 BE，只有真实 OnWhiff 清除未兑换计数；Skill、重复 Hurtbox、防守结果和无敌结果不计数。
- `CombatResourceSet` 不再包含 GuardValue/MaxGuardValue。普通 GuardHit 完全免除 HP 伤害并播放既有 GuardHit 反馈，不消费 SH、不会因防御值归零进入 Knockdown；GuardDamage/AppliedGuardDamage 仅保留为底层 Timeline 与命中结果结构。不可防攻击不进入该规则。
- GuardRelease 全程保持 `PlayerStatePhase.Recovery`；AttackReset、EvadeCancel 和 SkillCancel 等功能窗口继续独立写入 Context，开启 AttackReset 时只设置 `IsResetWindow=true`，不切换动画 Phase。Player 代码触发的重复起手、PerfectEvade 和战斗 Reaction 动画统一使用固定秒数 `CrossFadeInFixedTime`，现有数值分别为 `0.02s / 0.10s / 0.10s`，PerfectEvade 最大起播偏移为 `0.125s`。
- PlayerAttack 运行时窗口来源收敛到 Combat Timeline 资产，并通过 RuntimeSpec / CapabilitySnapshot 试点读取窗口能力。
- Skill / Evade / Guard 运行时窗口读取 RuntimeSpec 输出的状态本地 FrameData；状态不直接遍历原始 Timeline clips，也不直接书写底层 CapabilityKind / Target 查询。Skill 消耗不再通过 `PlayerSkillState` 静态属性暴露，进入前预查统一走 Skill1 Provider Data 解析入口。
- Attack Phase 只驱动 `CurrentPhase`；功能判断读取 functional clips。
- Attack / Evade / Guard Release / Skill 派生窗口采用“即时输入优先、缓存兜底”。
- 玩家不再支持回血道具 / Recover 顶层状态；Gameplay 输入资产、输入快照、状态机注册、资源字段和 Demo HUD 都不保留该功能。
- Guard 是 Hold 语义，不写入普通动作缓存。
- GuardHit / PerfectGuardReaction 继续作为 Guard 内部反应；时长来自 Guard Timeline Marker，PerfectGuardChainActive 是运行时动态窗口，不再作为静态 Timeline Clip。
- GuardHit 反应期间只允许通过 `Guard.asset` 的 `Cancel_ToGuardRelease` 进入真实 GuardRelease；GuardHit 不直接响应或缓存 Evade / Skill / Attack。持续按住 Guard 时保持防御能力，再次受击会重播 GuardHit；自然结束后仍按住回 GuardLoop，否则进入 GuardRelease。
- GuardHit 进入 Release 的同帧 Evade / Skill 转存到现有 Release Buffer，等待 Release Cancel 窗口消费；Attack 同帧输入忽略，必须在 Release AttackReset 窗口内重新按下。Release 中重按 Guard 仍走 GuardStart 的 PerfectGuardWindow，持续按住 Guard 不会自动连弹。
- PerfectGuard 在自身 `Cancel_ToGuardRelease` 窗口内允许松开 Guard 提前进入真实 GuardRelease；窗口前松开不打断反应。松手同帧 Attack 不缓存，必须进入 Release 后在 AttackReset 窗口重新按下，PerfectGuard 不新增直连 Attack 路径。
- Lock-on 转向 V1：玩家非承诺状态把锁定目标作为朝向参考；`Idle / Locomotion / GuardStart / GuardLoop / GuardRelease` 会平滑面向 Boss，其中原地 Guard 不再依赖 GuardWalk 输入触发转向。`GuardHit / PerfectGuard / Attack / Skill / Evade / HitReaction / Knockdown / Dead` 不走通用锁定朝向覆盖。
- PerfectGuard 成功时不扣 HP，奖励 BetaEnergy 2，触发 Guard 内部反应和接触防御反馈；每次正式 PerfectGuard 都扣 Boss 独立护盾 1 格，护盾未破时是否短硬直由 Boss HitNode `TriggersPerfectGuardBossStagger` 决定。
- Boss 黄光不可格挡 HitNode 使用 `CanBeGuarded=false / CanBePerfectGuarded=false / CanBePerfectEvaded=true`，因此 Guard / PerfectGuard 窗口内不会产生 GuardHit / PerfectGuard，而是按 HitNode 的 `HitReaction / Knockdown` 结算；PerfectEvade 仍可成功规避。
- PerfectEvade 是 Evade 内部模式，触发后进入专用固定时间轴，并由 Combat Feedback 层请求短子弹时间；它不使用命中 HitStop。
- Skill1 消耗 8 BetaEnergy；前两段 HitNode 为 `HitReaction`，第三段 HitNode 为 `Knockdown`。
- Player 与 Boss 身体互斥只维护站位，不参与伤害结算；互斥目标来自 `PlayerBossSeparation.BossBodyCenter` 的独立场景绑定，不得依赖 Lock-on 状态或 `PlayerStateContext.LockOnTarget`。取消锁定只能改变镜头、朝向与明确要求锁定目标的动作位移，不能允许玩家穿过 Boss。
- Boss 动作位移不再被玩家 target 身体硬阻挡；玩家侧 `PlayerBossSeparation` 是维护最低身体距离的主要规则，不能把玩家身体碰撞当作 Boss 攻击、后撤或突进的硬阻挡。
- Player 动作位移不再创建运行时默认 `PlayerActionMotionProfile`；缺显式 Profile 绑定时忽略动作位移请求并记录错误。
- Player Dead 是终止态：死亡后由 `PlayerDeadState` / `PlayerAnimationBridge` 处理死亡标记、动作窗口清理和死亡动画，不从 `Reaction_Player_Dead` 读取 Stun / Recovery / CanReturn；Dead Timeline 只保留死亡动画预览 / 标识用途。
- Player HitReaction 恢复末段接动作不再由旧硬编码 `Reset` 阶段开放；`PlayerHitReactionState` 每帧读取 Reaction Timeline RuntimeSpec，只有对应 `Cancel_ToEvade / Cancel_ToSkill / Cancel_ToGuard / Cancel_AttackReset` 激活时才允许受击恢复取消。
- Player Knockdown 起身取消不再由 `Reset` 阶段硬编码开放；`PlayerKnockdownState` 每帧读取 Reaction Timeline RuntimeSpec，只有对应 `Cancel_ToEvade / Cancel_ToSkill / Cancel_ToGuard / Cancel_AttackReset / Cancel_MovementReturn` 激活时才允许起身接闪避、技能、防御、攻击或普通移动。移动返回要求窗口内有实时 MoveInput，不缓存，且低于四类动作取消。
- HitReaction / Knockdown 的动画重播由 `CombatReactionAnimationRequestVersion` 驱动。每次有效请求普通受击或击倒都会递增版本，让同方向、同 Animator state 的连续受击也能重新 CrossFade 到起点；该版本不改变伤害、状态进入或取消窗口。
- `SM_Reaction` 中 HitReaction / Knockdown / Dead 动画不再通过 Base Layer `AnyState` 兜底进入；Animator Controller 只保留攻击、闪避、技能和 Guard 主流程所需的 `AnyState` 入口，Reaction 动画入口由 `PlayerAnimationBridge` 代码 CrossFade 统一控制。
- Player 攻击出手视觉表现不再由旧运行时拖尾 / 弧光控制脚本驱动；玩家武器拖尾只保留场景 / Prefab 中手动配置的 `SlashTrailVFX`，状态机不读取其结果，也不让它影响 HitNode、取消、输入缓存或 Skill 多段语义。
- Player Animator 的正式 Motion 必须引用 `Assets/Animator/EveAuthored` 内的可编辑 `.anim`，不得继续直接引用第三方 Eve FBX 子 Clip。69 个动画中的 `Ab-TL-HairB01～09` 曲线是动作基础姿态和唯一动画目标；运行时只允许在 Animator 求值后叠加 Eve 专用轻量弹簧与身体安全修正，并且只回写旋转。其他骨骼、Root Motion、长度、帧率和 Animation Event 必须与源 Clip 保持一致。

## 近期变更入口

- 2026-08-13：降低 Demo Boss 耐久为 `1500 HP / 15 护盾`，玩家资源与双方攻击伤害不变；覆盖同日较早的 Boss `2000 HP / 20 护盾` 基线记录。
- 2026-08-13：普通 GuardHit 从 15% HP 削血改为完全免伤。`CombatHitResolver` 仍返回 GuardHit 并保留 GuardDamage，但 AppliedHpDamage 固定为 0；致死判断延后到防御/无敌之后。黄光等不可防攻击、PerfectGuard、PerfectEvade、GuardHit 动画与反馈均保持原语义。
- 2026-08-13：恢复 Demo 正式战斗资源基线。玩家初始 HP 从 `100000` 调回 `300`，BE 从非法 `40/32` 调回 `0/32`；Boss `2000 HP`、20 护盾、6 秒破盾眩晕及双方所有攻击伤害均保持不变。新增场景资源与双方伤害矩阵回归测试；3～4 分钟战斗时长仍待人工实机验收。
- 2026-08-13：修复取消 Lock-on 后玩家可穿过 Boss。`PlayerBossSeparation` 新增独立 `BossBodyCenter`，Demo 绑定 Boss 根 Transform；靠近裁剪和移动后轻推不再以 `LockOnTarget != null` 为前提。未修改安全距离、状态切向滑动、Boss 动作位移或伤害检测。锁定/未锁定等价裁剪测试及相邻 Lock-on/移动回归 `9/9` 通过，Console 0 error。
- 2026-08-12：接通 Player Skill 的移动取消窗口。用户在 `Skill_Skill1.asset` 新增 `Cancel_MovementReturn 3.500～6.000s`；运行时通过 RuntimeSpec / SkillFrameData 写入 `IsMoveCancelWindow`，窗口内有实时移动输入时转 Locomotion。未新增输入缓存、Capability、Timeline Clip 类型或控制器，也未修改 Skill HitNode、伤害、动作位移、动画和自然结束规则。窗口边界、无输入、动作优先级和原三段 Motion 回归用例均通过，Unity Console 0 error。
- 2026-08-07：根据实机“几乎不摆动、重力不可读”的反馈提高 Eve 发梢动态混合与动作状态权重，改用较低频率、较高重力和 20% 惯性；同时缩小发链碰撞包络，并禁止躯干下部代理产生向上修正。保持动画目标、无能量去穿透、单链范围及 Raven 边界不变。
- 2026-08-06：在不修改 69 个 `EveAuthored` 动画的前提下，为 Eve 恢复玩家专用单链轻量头发处理。B01 保持动画控制，B02～B09 渐进叠加低惯性、高阻尼和重力；状态权重在极端动作下降。身体代理与碰撞安全动画目标负责无能量去穿透，投影不形成弹射速度。Raven、世界、Boss、武器和发束自碰撞不纳入。
- 2026-08-06：撤销 Eve / Raven 运行时发骨弹簧。复制 Player Controller 使用的 69 个唯一 FBX 子 Clip 到 `Assets/Animator/EveAuthored`，重映射 41 个直接状态与 5 个 BlendTree/31 个 Motion 槽位；逐帧烘焙 Eve `Ab-TL-HairB01～09` 为向下贴背姿态，头发之外的曲线、事件和动画元数据保持不变。
- 2026-08-06：为 Skill1 接入三段共用 SkillEffect。新增纯表现 `PlayerSkillEffectController`，由 PlayerSkillState 通知 Motion 窗口起播、PlayerWeaponHitbox 通知真实命中和 HitNode 结束；Prefab 与 Demo 场景显式绑定，14 个粒子关闭 PlayOnAwake。未修改 Timeline、伤害、吸附、BE 或 Boss Reaction。

- 2026-08-05：实施 Skill1 三段受击锁定与分段吸附。Hit1/Hit2 从 DamageOnly 改为 HitReaction，Hit3 保持 Knockdown；Timeline 新增三段 PlayerMotion 窗口，PlayerSkillState 在每段进入沿请求一次 LockOnTarget 动作位移，超出 `3m/60°`、无锁或碰撞时不吸附。

- 2026-08-05（削血部分已于 2026-08-13 废止）：实施玩家 BE 与 SH 删除规则。资源改为 HP/BE，BE 初始/上限 `0/32`，Skill1 消耗 8，PG/PE 各加 2；新增跨连段普通攻击命中计数，每两次有效实例加 1 BE、真实挥空清零。删除 GuardValue 资源、玩家破防 Knockdown、Animator GuardValue 同步及 BU/SH HUD；保留底层 GuardDamage 数据结构。

- 2026-08-05：修复快速松开防御后 GuardHit 无法进入 GuardEnd。GuardRelease 不再因 AttackReset 窗口把 `CurrentPhase` 改成 `Reset`；动画释放事实稳定保持 Recovery，动作重置仍由 `IsResetWindow` 控制。同时 PlayerAnimationBridge 全部改用固定秒数 CrossFade，避免长源动画把 `0.10` 放大为约 `0.5s`。

- 2026-07-27：接通 Knockdown 移动返回窗口。用户在 `Reaction_Player_Knockdown.asset` 新增 `Cancel_MovementReturn 4.0～5.0s`；运行时在该窗口内检测到移动输入时转入 Locomotion。窗口外、无移动输入或更高优先级动作取消成立时不走移动返回。

- 2026-07-15：PerfectGuard 增加 Release 派生。`Perfect Guard` Track 新增 `Cancel_ToGuardRelease`（`0.2167s - 0.50s`），Provider Data 按轨道隔离解析；`PlayerGuardState` 只在该窗口内响应松手并进入真实 GuardRelease，Attack 必须在 Release AttackReset 内重新按下。Validation 强制要求该窗口并禁止 PerfectGuard 配置动作 Buffer / Cancel。

- 2026-07-15：GuardHit 动作取消收敛到 GuardRelease。删除 GuardHit 轨道的 `Cancel_ToEvade / Cancel_ToSkill / Cancel_AttackReset`，运行时只读取 `Cancel_ToGuardRelease`；松手同帧 Evade / Skill 转存到 Release Buffer，Attack 不缓存。Validation 新增 GuardHit 动作 Buffer / Cancel 禁配错误，确保所有动作派生经过真实 GuardRelease。

- 2026-06-30：Skill1 HitNode 反应意图迁移到显式 Combat 语义。`Skill1_Hit1 / Skill1_Hit2` 改为 `CombatReactionIntent.DamageOnly`，只造成 HP 伤害和命中反馈；`Skill1_Hit3` 保持 `CombatReactionIntent.Knockdown`，由 BossReactionGateWindow 决定是否真正进入 Boss Knockdown。

- 2026-06-30：PerfectEvade 增加 Combat Feedback 子弹时间表现；状态机判定、BetaEnergy 奖励和 Evade Timeline 窗口不变。

- 2026-07-01：PerfectGuard V1 标准落地；普通 GuardHit 不给 PerfectGuard Beta 奖励，不触发 Boss PerfectGuardStagger，PerfectGuard 不复用 PerfectEvade BulletTime。

- 2026-07-01：修复玩家连续同方向受击动画不重播。`PlayerStateMachine.RequestHitReaction / RequestKnockdown` 递增 `CombatReactionAnimationRequestVersion`，`PlayerAnimationBridge` 对 HitReaction / Knockdown 使用版本号识别新受击事件，避免只按 `lastReactionStateHash` 去重导致动画不从头播放。

- 2026-07-01：GuardHitReaction 增加反应期取消。`PlayerGuardTimelineAsset` 从 `GuardHit` 轨道解析专属 Cancel 窗口，`PlayerGuardState` 在 GuardHit 期间只按 `Cancel_ToEvade / Cancel_ToSkill / Cancel_AttackReset` 三个区间允许即时取消；GuardRelease 仍只读 Release 轨道窗口，避免两个 Guard 内部阶段互相污染。

- 2026-07-01：GuardHit 后重新精防改为显式 Release 窗口。删除 `Input_GuardHitPerfectGuardRetry` 和 GuardHit 内部 Retry 判定，新增 `Cancel_ToGuardRelease`；GuardHit 只有在该窗口内松开 Guard 才进入 GuardRelease，Release 中重按 Guard 复用现有 GuardStart PerfectGuardWindow。该方案有真实防御空档。

- 2026-07-01：Lock-on 转向 V1 落地。`PlayerMovementMotor` 在 Guard 原地阶段补充锁定面向 Boss，排除 GuardHit / PerfectGuard；Attack / Skill / Evade 等动作状态继续由各自状态或动作位移控制朝向。

- 2026-07-02：同步 Boss / Player 身体占位规则。Boss 攻击、后撤、突进不被玩家身体硬卡住，玩家与 Boss 的贴近距离由玩家侧移动前裁剪和移动后软推离维护；伤害仍只走 Hitbox / Hurtbox / Receiver 链路。

- 2026-07-08：清理 Player 旧攻击出手 VFX。`PlayerAttackState` 和 `PlayerSkillState` 删除旧运行时拖尾 / 弧光控制脚本引用与调用；删除对应脚本、EditMode 测试以及旧 `Assets/Prefabs/Combat/AttackVfx` 程序化拖尾 / 弧光 prefab。当前玩家武器拖尾由用户新增的 `SlashTrailVFX` / 场景资产承担，Combat Timeline、HitNode、伤害和取消窗口语义不变。
- 2026-07-10：接入 Boss 黄光不可格挡结算规则。该规则不修改 Player Guard / PerfectGuard 状态机，只依赖 Boss HitNode 的 `CanBeGuarded / CanBePerfectGuarded / CanBePerfectEvaded` 布尔位；玩家应对从防御切换为 Evade / PerfectEvade。

- 2026-07-13：删除 Player Animator 中 Reaction 相关 Base Layer `AnyState` 兜底连线。`Anim_HitReaction_Light`、`Anim_Knockdown_Start_F`、`Anim_Knockdown_Loop`、`Anim_Knockdown_End`、`Anim_Dead` 的进入统一依赖 `PlayerAnimationBridge` 代码 CrossFade；攻击、闪避、技能和 Guard 主流程 AnyState 连线保持不变。
- 2026-07-15：删除 Player 回血道具 / Recover 状态。移除 `PlayerStateId.Recover`、`PlayerRecoverState`、`TimedReturnState`、`RecoverPressed` 输入快照、Gameplay `Recover` Action、`HealingReagentCount` 资源字段和 Demo HUD HealCount；Boss 玩家战术快照不再包含 Recover，也不再因玩家 Recover 提高追击或黄光评分。

- 2026-06-29：Player HitReaction 按 Knockdown 同构收敛到 Combat Timeline Phase / Reaction / Cancel 分轨；受击恢复转动作由 Cancel capability 控制，Phase 只用于动画 / 调试展示。

- 2026-06-29：Player Knockdown 起身取消窗口收敛到 Combat Timeline Cancel capability；`Phase` 只用于动画 / 调试展示。

- 2026-06-29：Player Dead Timeline 移除恢复 / 返回功能窗口，Combat Timeline Validation 对 Dead 不再要求普通 Reaction 窗口。

- 2026-06-23：Player Skill / Evade / Guard 状态消费层收敛到 State-local FrameData。
- 2026-06-23：Player Skill / Evade / Guard 迁移到 Combat Timeline RuntimeSpec / CapabilitySnapshot。
- 2026-06-23：PlayerAttack RuntimeSpec / CapabilitySnapshot 试点迁移。
- 2026-06-23：PlayerAttack Timeline 数据源去硬编码。
- 2026-06-21：Attack 移动取消窗口。
- 2026-06-17：锁定侧向闪避改为绕 Boss 弧线位移。
- 2026-06-16：Skill1 三段 HitNode 接入。
- 2026-06-16：PerfectGuardChainWindow 连续弹反接入。
- 2026-06-16：Guard Release 重入防御与 Evade 恢复移动修复。
- 2026-06-12：玩家 Attack / Skill 位移分层。

- 2026-08-12：Demo 标题阶段必须清空玩家锁定、动作窗口和输入快照，并保持 `IsInCombat=false / Idle`；开场镜头到位后才允许同帧设置 `IsInCombat=true`、回到 Idle 并优先锁定指定 Boss。指定目标不可用时可回退既有最佳目标选择，过渡中不得提前接收移动或动作输入。

## 关联文档与代码

- `doc/主角状态机汇总.md`
- `doc/主角状态机详设/*.md`
- `Assets/Runtime/Player`
- `Assets/Runtime/Combat/Timeline`

## 遗留风险

- PlayerAttack / Skill / Evade / Guard CapabilitySnapshot 迁移后需要完整 Play Mode 回归。
- NearMiss 外圈检测未实现。
- Guard / Evade / Skill 与 Animator 参数联动仍需谨慎修改。
