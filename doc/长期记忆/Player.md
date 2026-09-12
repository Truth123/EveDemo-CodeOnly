# Player 长期记忆

更新时间：2026-08-13

## 当前状态

- 2026-08-13：普通防御从 15% HP 削血改为完全免伤。可防攻击命中有效 Guard 时仍进入 GuardHit、播放既有反馈并保留底层 GuardDamage 数据，但不扣 HP；本次攻击的致死判断延后到防御/无敌之后，低血量成功格挡不会被提前判死。不可防攻击仍造成完整伤害，PerfectGuard / PerfectEvade 继续为 0 HP 伤害。
- 2026-08-13：Demo 玩家保持正式基线 `300/300 HP、0/32 BE`；为降低难度，Boss 已进一步下调为 `1500 HP / 15 护盾`。双方正式 Timeline 伤害不变：玩家轻链总伤害 54、重链 50、Skill1 200；Raven 单段伤害 18～36、最高完整连段 132。普通防御、PerfectGuard 与 PerfectEvade 均为 0 HP 伤害；完整战斗时长需按新耐久重新实机确认。
- 2026-08-12：标题阶段由 `PrepareTitlePresentation()` 清空输入/窗口/锁定并保持非战斗 Idle；开场镜头到位后由 `BeginCombatIdleAndLockOn()` 同帧进入战斗 Idle，优先锁定场景 Boss，失败时才回退到既有最佳目标选择。镜头过渡期间不接受玩家输入，也不会提前进入 Lock-on。
- 2026-08-12：Player Skill 已消费 `Cancel_MovementReturn`。当前 `Skill_Skill1.asset` 在 `3.500～6.000s` 开放移动取消；窗口内有实时移动输入时从 Skill 转入 Locomotion，窗口外或无输入不触发，也不缓存移动输入。动作取消优先级保持 `Evade > Skill > Attack > Guard > Move`，Skill 自然结束逻辑不变。窗口边界、无输入和动作优先级用例连同原三段 Motion 回归均通过；Unity Console 0 error。
- 2026-08-07：Eve 头发从“数学运行但肉眼近似静止”调整为可读的克制摆动。参数改为 `4.0Hz / 阻尼0.86 / 惯性0.20 / 重力1.15`，B02～B09 动态权重改为 `0.12 / 0.24 / 0.38 / 0.52 / 0.66 / 0.78 / 0.88 / 0.94`；Attack/Skill/HitReaction 使用 0.85，Evade 使用 0.65。头发碰撞半径收缩为 `0.035～0.015m`、间隙改为 `0.005m`，让中心链更贴近作者轮廓；上背、胸腹、骨盆和腿部代理禁止向上托举，接触只向身体外侧或向下修正，使重力能沿身体表面滑落。
- 2026-08-06：Eve 在保留 69 个 `EveAuthored` 动画和用户头发基础姿态的前提下，恢复玩家专用轻量二级运动。`PlayerHairSecondaryMotionController` 只处理 `Ab-TL-HairB01～09`：B01 完全服从动画，B02～B09 渐进叠加 `5.2Hz / 阻尼0.92 / 惯性0.15 / 重力0.45`，并按 PlayerState 降低极端动作权重。求解器使用碰撞安全动画目标、发段胶囊与头肩背胸腹骨盆双腿数学代理；投影不生成速度，接触移除法向速度，深穿透清零速度。只回写旋转，不改动画、Root Motion、位置或缩放；Raven 仍完全使用原动画发骨曲线。
- 2026-08-06：Skill1 三段已接入场景 `CH_P_EVE_51/Trans/SkillEffect`。三个 `Skill1Forward` Motion 窗口首次激活时由 `PlayerSkillState` 重播同一套主粒子；`PlayerWeaponHitbox` 只在正式结果为 `DamageOnly / HitReaction / Knockdown / Dead` 时通知 `hitEffect`，每个 HitNode 最多播放一次并在窗口结束时关闭。Skill 退出、中断或组件禁用会清理全部粒子；14 个 ParticleSystem 均关闭 PlayOnAwake。表现控制不进入 Timeline、CombatHitData 或 PlayerStateContext。
- 2026-08-05：Skill1 改为三段短窗口锁定吸附。Timeline 在 `0.50～0.80 / 0.90～1.18 / 1.65～1.98s` 激活 `Skill1Forward`，`PlayerSkillState` 每个窗口只请求一次；Profile 使用 `LockOnTarget`、`0.28s / 1.85m / MaxDistance 3m / MaxAngle 60° / StopDistance 1.15m` 与前快后慢累计曲线。运行时只追当前 Lock-on 目标，窗口内持续校正水平朝向和径向距离，丢目标或越界立即停止，最终仍经 CharacterController、Boss Separation 与侧碰裁剪；无锁时不搜索目标。新增 Timeline/状态/位移定向用例均通过。
- 2026-08-05：玩家战斗资源收敛为 HP 与 BE。默认/Demo 为 `BE=0 / MaxBE=32`，Skill1 与 `PlayerSkillTimelineAsset` 默认费用为 8，PerfectGuard/PerfectEvade 各奖励 2。普通攻击只把 `DamageOnly / HitReaction / Knockdown / Dead` 视为 BE 有效命中，每个 `CurrentAttackInstanceId` 最多计数一次，每两次增加 1 BE；实际 OnWhiff 清零未兑换计数，连段结束、取消或被打断不清零。Skill、多 Hurtbox、PG/PE 与无敌结果不计数。`CombatResourceSet` 已删除 GuardValue/MaxGuardValue；GuardHit 保留反馈但不消费 SH、不再破防倒地。其 15% HP 削血旧规则已于 2026-08-13 废止。
- 2026-08-05：GuardRelease 整个生命周期固定使用 `PlayerStatePhase.Recovery`；AttackReset 只通过 `IsResetWindow` 表达，不再把释放动画阶段切成 `Reset`，避免 GuardHit / PerfectGuard 进入 Release 时错过 GuardEnd。`PlayerAnimationBridge` 的重复起手、PerfectEvade 与战斗 Reaction 已统一改用固定秒数 `CrossFadeInFixedTime`，分别为 `0.02s / 0.10s / 0.10s`，PerfectEvade 起播偏移上限为固定 `0.125s`。真实 Demo 零移动 GuardLoop → GuardHit → 快速松防御回归测得混合 `0.102s`、ResetWindow 期间 Recovery 保持且 GuardEnd 成功；Console 0 error。Test Runner 的既有发现/初始化问题仍阻止正式定向用例执行。
- 2026-07-27：Player Knockdown 已消费 `Cancel_MovementReturn`。当前 `Reaction_Player_Knockdown.asset` 在 `4.0～5.0s` 开放移动返回；窗口内有实时移动输入时从 Knockdown 转入 Locomotion，窗口外或无输入不触发，也不缓存移动输入。动作取消优先级保持 `Evade > Skill > Attack > Guard > Move`。
- 玩家已具备输入快照、移动、锁定移动、状态机主干、攻击连段、Guard、Evade、Skill、受击、击倒、死亡等核心状态。
- 2026-07-15 已删除回血道具 / Recover 顶层状态：玩家状态机不再注册 `PlayerRecoverState`，输入层不再采集 Recover Action，资源集合不再保存 `HealingReagentCount`，Demo HUD 不再显示回复道具数量。
- PlayerAttack 运行时窗口来源已收敛到 `PlayerAttackTimelineAsset`，并已作为 Combat Timeline CapabilitySnapshot 试点消费归一化能力；缺 Timeline 不再创建空 RuntimeSpec。
- PlayerSkill、PlayerEvade、PlayerGuard 已迁移到 RuntimeSpec 主路径，并通过状态本地 FrameData 消费窗口能力；Guard 仍保留 Start / Loop / Reaction / Release 内部 mode。
- 2026-06-24 Guard Timeline 语义已修正：GuardHit / PerfectGuardReaction 时长来自 `Guard.asset` Marker；`Input_PerfectGuardChain` 是固定输入窗口；ChainActive 由重按 Guard 后在状态内动态打开，旧 `Player_GuardHit` Reaction Timeline 已删除。
- 2026-07-15 GuardHit 取消语义已收敛到真实 GuardRelease：`Guard.asset` 的 `GuardHit` 轨道只保留 `GuardHitReaction` 与 `Cancel_ToGuardRelease`，GuardHit 不直接响应或缓存 Evade / Skill / Attack；按住 Guard 时维持防御并可再次受击重播，反应结束后按住回 GuardLoop，否则进入 GuardRelease。
- GuardHit 进入 Release 的同帧 Evade / Skill 会写入现有 Release Buffer，并等待 Release Cancel 窗口消费；Attack 不缓存，必须在 Release AttackReset 窗口内重新按下。Release 中重按 Guard 仍走 GuardStart 的 PerfectGuardWindow，松手到重按之间存在真实防御空档。
- 2026-07-15 PerfectGuard 新增独立 `Cancel_ToGuardRelease`（当前 `0.2167s - 0.50s`）：窗口内松开 Guard 会清除 PerfectGuard Chain 并进入真实 GuardRelease，随后可在现有 Release AttackReset（当前从 `0.10s` 开始）重新按下攻击；PerfectGuard 不直接跳 Attack，也不缓存松手同帧 Attack。
- 2026-07-01 Lock-on 转向 V1 已落地：`Idle / Locomotion` 继续由移动 Motor 面向锁定 Boss；`GuardStart / GuardLoop / GuardRelease` 即使没有 GuardWalk 输入也会用防御转向速度面向 Boss；`GuardHit / PerfectGuard` 不自动转向，保留防御反应动画方向。
- PerfectGuard V1 规则已固定：成功时 HP 伤害和 GuardDamage 都为 0，奖励 BetaEnergy 2，触发 Guard 内部 PerfectGuard 反应和接触防御反馈；每次正式 PG 都扣 Boss 独立护盾 1 格，未破时才由 HitNode `TriggersPerfectGuardBossStagger` 决定短硬直。
- Boss 黄光不可格挡规则已接入：对应 HitNode 使用 `CanBeGuarded=false / CanBePerfectGuarded=false / CanBePerfectEvaded=true`，因此玩家 Guard / PerfectGuard 窗口内仍会吃 `HitReaction / Knockdown`，不会奖励 BetaEnergy、不会播放 PerfectGuard VFX / SFX；PerfectEvade 仍可成功规避。
- 2026-07-08：Player 旧程序化攻击出手 VFX 已清理。`PlayerAttackState` / `PlayerSkillState` 不再解析或调用旧运行时拖尾 / 弧光控制脚本，相关脚本、测试和 `Assets/Prefabs/Combat/AttackVfx` 旧 prefab 已删除；玩家武器拖尾只保留用户手动新增的 `SlashTrailVFX` / 场景资产表现，不参与 HitNode 命中结算、取消窗口或 Skill HitNode 语义。
- 2026-06-24 PlayerSkill 状态已移除 `CurrentSkillCost` 静态属性；进入 Skill 前由 `PlayerSkillActionResolver.TryGetSkill1()` 获取 Provider Data，状态内部用 `currentSkill.SkillCost` 消耗资源，并按 Attack 风格拆分 phase/window、buffer、cancel/restart helper。Attack / Skill 热路径已改为每帧单次 `EvaluateFrame`，FrameData 传入窗口更新、输入缓存和取消派生判断。
- `PlayerActionInputRouter` 统一处理即时输入、缓存写入、缓存消费和 Skill 资源检查。
- 玩家动作位移已分层：普通移动、Evade、Skill、Attack Root Motion 由不同组件协作，最终进入 `PlayerMovementMotor` / `CharacterController` 路径。
- 2026-06-24 已删除 `PlayerActionMotionProfile.CreateRuntimeDefault()` 与 `PlayerMovementMotor` 运行时默认 Profile 路径；缺 `EveActionMotionProfile.asset` 绑定时动作位移请求失败并记录错误。
- Player 与 Boss 的身体站位通过 `PlayerBossSeparation` 软互斥，不参与伤害结算。其 Boss 身体中心使用独立 `BossBodyCenter` 场景引用，不能依赖取消锁定时会清空的 `PlayerStateContext.LockOnTarget`；因此锁定和未锁定状态执行相同的靠近裁剪与穿透轻推。Boss 攻击、后撤、突进是高优先级动作位移，不被玩家身体硬阻挡；玩家侧负责被裁剪或轻推回最低身体距离。

## 关键规则

- 状态机共享数据集中在 `PlayerStateContext`。
- Guard 不进入普通动作缓存；只在允许窗口读取 `GuardPressed || GuardHeld`。
- 普通 GuardHit 不奖励 BetaEnergy，不触发 Boss PerfectGuardStagger；PerfectGuard 与 PerfectEvade 的表现语义分离，前者是接触防御 HitStop，后者是无伤规避 BulletTime。
- 黄光不可格挡不是 Guard 变体：Guard / PerfectGuard 不成立，只能依靠 Evade / PerfectEvade 或离开攻击范围处理。
- Attack / Evade / Guard Release / Skill 派生窗口遵循“即时输入优先、缓存兜底”。
- PerfectEvade 是 Evade 内部模式，不是顶层状态；触发后使用专用固定时间轴，并通过 Combat Feedback 层请求短子弹时间表现。
- Skill1 消耗、三段 HitNode、SuperArmor、Cancel / Buffer 窗口均来自 `Skill1` Timeline；前两段 HitNode 为 `CombatReactionIntent.HitReaction`，Gate 允许时重新播放并刷新 Boss 受击；第三段为 `CombatReactionIntent.Knockdown`，可击倒 Boss。
- 受击打断现在读取通用反应霸体能力：Skill SuperArmor 与 Attack Uninterruptible 都通过 `HasActiveReactionArmor` 阻止 HitReaction / Knockdown，但不阻止 HP 伤害。
- Player Dead 是终止态，死亡后由 `PlayerDeadState` / `PlayerAnimationBridge` 清理动作窗口并播放死亡动画；`Reaction_Player_Dead` 不再承载 Stun / Recovery / CanReturn，只作为死亡 Timeline 预览 / 标识资产。
- Player HitReaction 已改为 Timeline Cancel capability 驱动：`Reaction_Player_HitReaction.asset` 分为 Phase / Reaction / Cancel 轨，`Cancel_ToEvade / Cancel_ToSkill / Cancel_ToGuard / Cancel_AttackReset` 分别决定受击恢复末段能否转闪避、技能、防御和攻击，`Reset` Phase 不再直接开放所有动作。
- Player Knockdown 起身取消已改为 Timeline Cancel capability 驱动：`Cancel_ToEvade / Cancel_ToSkill / Cancel_ToGuard / Cancel_AttackReset / Cancel_MovementReturn` 分别决定能否起身接闪避、技能、防御、攻击和普通移动，`Reset` Phase 不再直接开放任何取消。
- Player 连续受击会通过 `CombatReactionAnimationRequestVersion` 重新请求 HitReaction / Knockdown 动画，即使连续两次命中解析到同一个方向和同一个 Animator state，也会重新从 0 CrossFade；GuardHit / PerfectGuard 仍使用 Guard 自己的反应版本。
- Player `SM_Reaction` 的 HitReaction / Knockdown / Dead 不再保留 Base Layer `AnyState` 兜底连线；受击、击倒和死亡动画选择统一由 `PlayerAnimationBridge.CrossFadeCombatReactionIfNeeded` 根据状态机事实触发。
- PlayerAttack Phase 只用于 `CurrentPhase`、动画和 Debug；功能窗口由 Timeline functional clips 驱动。
- 玩家武器拖尾是纯表现资产：当前只保留 `SlashTrailVFX` / 场景配置，不由 Player 状态机运行时重建旧拖尾或弧光，也不作为伤害窗口、取消窗口或 HitNode 数据来源。
- Lock-on 目标是朝向参考，不是全状态强制转向；`Attack / Skill / Evade / HitReaction / Knockdown / Dead` 不被通用锁定朝向覆盖。
- Player-Boss 身体互斥只维护站位：玩家普通移动 / 防御 / 闪避 / 攻击 / Skill 按状态安全距离裁剪朝 Boss 内部的位移，移动后只做最低身体半径轻推；Boss 是否命中玩家仍由 Boss Hitbox / HitNode 决定。

## 主要代码入口

- `Assets/Runtime/Player/StateMachine/PlayerStateMachine.cs`
- `Assets/Runtime/Player/StateMachine/PlayerStateContext.cs`
- `Assets/Runtime/Player/StateMachine/States/PlayerAttackState.cs`
- `Assets/Runtime/Player/StateMachine/States/PlayerGuardState.cs`
- `Assets/Runtime/Player/StateMachine/States/PlayerEvadeState.cs`
- `Assets/Runtime/Player/StateMachine/States/PlayerSkillState.cs`
- `Assets/Runtime/Player/StateMachine/Buffers/PlayerActionInputRouter.cs`
- `Assets/Runtime/Player/Combat/PlayerWeaponHitbox.cs`

## 下一步入口

- 回归 PlayerAttack 连段、Reset 重起手、MoveCancel、Evade / Skill / Guard Cancel。
- Play Mode 回归 PlayerSkill、PlayerEvade、PlayerGuard 的 Provider Data / FrameData 主路径，并覆盖缺 ActionMotionProfile、Attack Uninterruptible、动作位移取消和相邻状态清理。
- 继续补 Player 状态类文件头和关键函数注释。

## 已知风险

- NearMiss 外圈检测仍未实现。
- PlayerAttack / Skill / Evade / Guard 删除默认玩法兜底和 ActionMotionProfile 默认配置后仍需 Play Mode 全量回归。
- 方向锁存、动画桥接和输入缓存是高联动区域，修改时必须同步验证相邻状态。
