# Feedback / UI / VFX / SFX 需求变更

更新时间：2026-09-05

## 活跃规则

- Demo 战斗 HUD 右下角必须默认显示一张标题为“H 键隐藏”的两列三行静态操作卡片，仅展示六项主要绑定：鼠标左键轻攻击、鼠标右键重攻击、鼠标中键锁定、Shift 闪避、E 防御、1 释放技能。Combat（含暂停覆盖）中独立 H 只切换该卡片自身显隐；标题、开场过渡和结算阶段忽略。卡片所有 Graphic 必须关闭射线命中；除 H 显隐外不得监听实时输入、显示按键高亮、冷却、技能可用性或额外的 Tab / 右 Shift 兼容绑定，也不得新增 InputAction、按键提示控制器或配置系统。反例：即使提示卡片被隐藏或删除，HUD 其他区域、玩家输入和六项战斗功能仍必须正常运行。
- Demo 不得创建左上角 `TrainingPromptPanel`，也不得响应其原有的 `TAB + 1 / 2 / H / C` 组合键。`ESC` 仍负责打开 / 关闭暂停菜单；暂停菜单只创建继续、重新开始和退出，不得创建 Boss 启停或调试信息显隐按钮。右上 Boss AI 只读信息与可选 `EveDebugOverlay` 进入战斗时默认隐藏，开发调试仅通过场景 `showDebugOnStart` 配置开启。反例：删除暂停按钮不得删除 BossActor 正式启停能力或右上调试面板本体。
- Demo 战斗背景音乐固定使用 `Assets/Audio/186. Raven (Instrumental).flac`：标题页点击“开始战斗”前不播放；点击后进入 OpeningTransition 的同帧，由 `DemoBattleUiController` 通过独立二维 AudioSource 以 `0.22` 音量开始循环，不得等待镜头过渡完成。首次进入 PendingVictory / PendingDefeat 时立即停止。暂停不停止或重启音乐；返回标题、重载、退出和组件禁用均必须清理。不得复用 CombatFeedback 或 Raven Animation Event 音源，也不得新增全局音乐单例。反例：仅停留在标题页或打开暂停菜单时，不应触发一次新的音乐播放。
- Demo 标题页不得创建或显示全屏扫描线平铺层；菜单局部青色分隔线与渐变遮罩继续保留。Raven Lock-on 提示必须是 `Bip001` 的直接子对象，不得使用 Overlay Canvas、屏幕中心坐标或逐帧相机投影；外观必须具有稳定可读的白色圆芯和短柔边，不得整颗做成低透明度雾团。材质关闭深度写入并始终通过深度测试，使节点位于身体内部时仍显示在角色表面；Renderer 必须限制近距离屏幕最大尺寸。其显隐只由 `PlayerStateContext.IsLockOn / LockOnTarget` 单向驱动：仅在 Combat、未暂停且正式目标为 Raven 时显示，取消、目标无效、暂停或非 Combat 状态清空隐藏。提示不得缓存第二份锁定状态，也不得改变目标选择、距离判定、相机朝向、玩家转向或 Boss AI。反例：即使世界空间提示被禁用，现有锁定与战斗仍必须正常运行。
- Demo 标题使用场景内固定镜头与专用 Eve 补光：`TitleCharacterKeyLight` 只在 Title/OpeningTransition 启用，进入 Combat 后必须关闭。点击开始后标题组可短暂淡出，但不得用黑屏掩盖镜头切换；镜头需要在 `2.4s` 内持续可见地移动到战斗位姿，直到到位前 HUD、玩家输入、自动锁定和 Boss AI 都不得开启。
- `Assets/Textures/buff2D_stagger/images/xuanwo.png` 与 `star.png` 必须作为世界空间 3D 眩晕补充表现使用：运行资源为独立 `FX_Boss_StaggerOrbit3D.prefab`，并嵌套在 `FX_Boss_ShieldBreakStun/StunLoop` 下。运动必须复刻用户提供 GIF 的常规转圈语义：一个旋涡圆环位于头顶水平 XZ 面并绕本地 Y 轴连续自转，三颗 Billboard 星点围绕同一中心、同一 XZ 圆周与同一方向公转，统一周期约 `1.2s/圈`；战斗相机投影负责形成水平扁椭圆和前后层次。四个元素各由一个非循环、单 Burst、最大一颗且持续 6 秒的 Local Simulation ParticleSystem 表达。禁止恢复左右月牙横向铺开、三向交叉面、持续 Rate 发射、随机 3D 朝向、多个旋涡副本或各元素不同步慢转。该资源继续由既有 `BossShieldBreakStunVfxController` 递归开始、停止和清空，不得引入独立运行时控制器、Spine 运行时依赖，也不得影响武器、护盾、状态、动画、伤害、AI、Timeline 或 HitNode。反例：即使该补充 Prefab 被禁用，原三阶段长眩晕和所有战斗结算仍必须正常工作。
- Raven 护盾击破长眩晕视觉固定采用身体空间三阶段：进入瞬发 `BreakBurst`，状态全程维持 `StunLoop`，最后 `0.7s` 叠加 `RecoveryWarning`；状态退出必须清空全部粒子和专用点光。主题为白热核心、冰青电弧/碎片、深蓝雾与断裂圆环。按用户明确要求，不得增加武器失能、武器熄灭/重新点亮、武器 Renderer/Light 控制或 BodyColor 染色；也不得让特效反向驱动护盾、状态、伤害、AI、Timeline、HitNode 或动画。
- `Spatial section` 保持纯表现资产，当前 Demo 以 `1.5` 倍缩放、`2s` 生命周期播放；用户实战确认的 `BurstAreaSlash_1` 有效半径为 `9.3m`，战斗检测球与 Timeline `EffectiveRange` 必须同步保持 `9.3m`。项目专用 Prefab 的 `Distortion` 子节点必须停用，取消 Scene Color 折射造成的玩家镜面复制；不得修改第三方共享 `DistortionTriangle.mat`、`URP_Distortion.shadergraph`、源 Prefab或其他粒子层，也禁止让运行时代码读取 ParticleSystem 反向决定伤害。
- `star1` 快速移动粒子必须同时提供无参数 Animation Event `OnFastMoveParticleStart / OnFastMoveParticleStop`：Start 递归清除并从头播放显式绑定的根与子 ParticleSystem；Stop 递归执行 `StopEmittingAndClear`，允许动画作者在自然寿命结束前立即停止并清空。Stop 后必须允许再次 Start；两者都不得反向驱动 HitNode、伤害、位移、状态切换或攻击 Cue，也不得自动写入未指定 Animation Clip 的事件时间。
- Raven 动画音效必须提供无参数 Animation Event `OnPlayRedStartSound / OnPlayYellowStartSound / OnPlayRedChargeSound`，分别播放 redstart、yellowattack、redcharge WAV。三者必须复用 Raven 本体显式绑定的专用 3D AudioSource，并通过 `PlayOneShot` 允许声音重叠；不得复用玩家 CombatFeedback 音源、不得运行时按路径加载、不得自动写入未指定 Animation Clip。缺少音源或对应 Clip 时记录明确错误并跳过，不能影响动画、HitNode、伤害、位移或状态切换。
- 红光提示同时保留两个独立版本：现有 `FX_Boss_RedFrenzyCue.prefab` 继续作为 Demo 的完整 `1.00s` 根，`FX_Boss_RedFrenzyCue_053s.prefab` 作为独立的完整 `0.53s` 根，两者均以默认隐藏的场景实例显式绑定到 `BossAttackCueVfxController`，不得互相覆盖。加速版必须与原版保持相同层级、粒子结构和作者参数，只通过所有 Particle System 一致的 `Simulation Speed=1.886793` 整体缩放时间；禁止单独缩短部分 Lifetime、删除尾帧或重做轨迹。控制器自动隐藏必须计入 Simulation Speed，使两版分别在 `1.00s / 0.53s` 完成。1 秒版继续由 `OnRedAttackParticleStart` 触发；0.53 秒版只由可手工添加到动画帧的无参数事件 `OnRedAttackParticle053Start` 触发。不得自动修改未指定的 Animation Clip、事件时间或战斗规则。
- 2026-08-10 场景模板规则覆盖下方旧剑气条目：唯一实战视觉模板是 Demo 场景 `Boss/FX_Boss_RangedSwordAura_SwordSlash6`，专用 Prefab 副本只保留为资源参考，`BossDetachedLinearHit.prefab` 必须为无粒子子节点的纯判定 Prefab。SwordAuraCombo 三条及 EvadeBackSwordAura 一条共享场景模板；运行时隐藏模板本体，每次发射完整克隆当前本地 TRS、激活并清空重播所有粒子。必须保留原资源的移动接力：根、`Pivot/Slashes`、`Pivot/Slashes2` Start Lifetime 为 `0.3s`，不可见 `Pivot` 为 `2.2s`，Slashes 两层继续使用 Rate over Distance；不得把远处旧刀幕延长为常驻。四条绑定均显式以模板 `Pivot` 作为 `VisualTravelDriver`，视觉克隆保持世界空间，Emitter 按模板缩放把各绑定 `Speed` 换算为 Pivot 本地 Start Speed，使视觉和 Collider 世界速度一致且不叠加位移；当前三连为 `18m/s`，后撤剑气为 `20m/s`。`LocalEulerAngles.z` 仍是相对模板基础旋转的独立额外滚转，不改变生成瞬间锁定的速度方向。BurstAreaSlash 不得复用该模板。反例：即使所有视觉粒子停播，Detached 剑气仍必须正常直线飞行并结算命中。
- Raven 远程剑气必须以用户提供的 `Assets/Prefabs/Boss/Sword Slash 6.prefab` 为造型来源，不再继续自制 `BladeCore / BladeHalo / EnergyVeil / TornCrest` 四层或月牙 Mesh 主体。正式资产使用独立 Prefab 和材质副本，只允许调整五个既有粒子系统的 Start Color / Color over Lifetime，以及私有 SwordSlash 材质的 `_Desaturation / _AddColor`，形成“白热核心 > 冰青高光 > 深蓝青外缘”的参考图色彩层级；不得修改用户源 Prefab、Hovl Studio 共享材质、Slash3 Mesh、原粒子结构、时序、Distortion、Local Simulation 或运动参数。`BossDetachedLinearHit.prefab` 只替换视觉子 Prefab，不得修改 Timeline、HitNode、Collider、Rigidbody、速度、生命周期、伤害、PE、Boss AI 或命中反馈，也不得新增运行时控制脚本。反例：即使视觉子 Prefab 被禁用，Detached 剑气仍必须正常飞行并结算命中。
- Raven SlashCombo 红光提示必须使用独立 `FX_Boss_RedFrenzyCue.prefab` 与 `RedFrenzy` 专用纹理/材质，并保持四个直接表现组：`ShardField` 必须同时覆盖左右主翼、头顶冠层和腿部下方，且五组同批粒子均先展开再弧形回收；五组回收不得沿发射路径原路倒放，粒子之间必须具有正反旋向、不同弧面和小幅不同旋转中心。五组只共享 `ShardField/ConvergenceForceField` 这一不可见技术节点，各组的 Burst、初速度、Shape、Start Size、颜色、Noise、材质与空间构图必须保留作者差异；AccentFragments 的 Orbital/Offset 仅使用其他组 25% 范围。中后期回收必须表现为可见的向心与切向位移，Size/Alpha 不得在反向运动出现前先把粒子隐藏。五个碎片系统必须把专用 4×4 图集按单元切分，每颗粒子随机固定显示一枚碎片，禁止把含 16 枚碎片的完整图集作为一颗粒子的矩形卡片，也禁止生命周期内逐帧播放图集；`TrailConvergence` 必须有 6 条全部位于核心上方的长弧，起点按确定的横向槽位均匀展开并保留轻微深度错层，不得再用大半径 Cone 完全随机出生；六条路线可以使用不同低频 Noise，但必须于同一胸口核心点结束，轨迹宽度、寿命和采样密度应保证锁定镜头中清晰可读；`CoreChargePulse` 的小型青白核心必须从 0 秒已可见并连续增亮，约 0.76 秒的峰值/Lens Line 必须与最后汇聚重叠；`RedSignature` 只包含中心红点，角色红色仅允许覆盖头发材质槽 5/6。不得复用黄光、PerfectGuard、BossHit 或青蓝刀光资源。`OnRedAttackParticleStart` 使用用户手调的 Clip Local Time `0s`；所有粒子必须非循环、PlayOnAwake=false，并从该事件起完整持续 `1.00s`。HairColor 保持用户在动画中设置的 `0～2.625s` 键位，不得为了匹配旧 `2.05s` 方案或粒子寿命自动平移。红光必须与黄光互斥，并在攻击结束、中断、状态切换或组件禁用时立即清理。
- Eve 采用 `EveAuthored` 动画基础姿态与玩家专用轻量发骨修正：B01 完全服从动画，B02～B09 逐渐增加动态权重；运行时只回写旋转，并使用内部数学代理完成身体软防穿模。去穿透位移不得进入速度，接触不得产生向外反弹。Raven 继续只使用原始动画发骨曲线；不得引入 Cloth、Rigidbody/Joint、Unity Physics Collider，且头发表现不得修改 Player/Boss 状态、RootMotion、Timeline、Hitbox、Hurtbox、武器或特效。
- Eve Skill1 的 `SkillEffect` 是动作出手表现，三段共用同一场景对象并在 Motion 窗口起点重播；其中 `hitEffect` 是真实命中表现，只由 PlayerWeaponHitbox 的正式结果驱动，每段最多一次且在 HitNode 结束时禁用。它不得参与 HitNode、伤害、吸附或 Boss Reaction，也不得在进入场景时 PlayOnAwake 自动播放。
- Demo 标题与结算必须留在 `Demo.unity` 和现有 `DemoBattleUiController` 内，不新增独立主菜单场景或第二套 UI 框架。标题保持实时场景运行但关闭玩家输入、相机控制和 Boss AI；Combat 才允许暂停。Boss Dead 为胜利、Player Dead 为失败，同帧死亡按失败；首次死亡后等待约 4.8 秒播放演出，再取消 HitStop、暂停时间并显示结算。再次挑战通过一次性启动意图重载 Demo 后直接开战，返回标题正常重载；场景/组件退出必须恢复 TimeScale、FixedDeltaTime、光标和输入。视觉只借鉴暗色留白、冷青高亮与信息层级，不得导入《剑星》Logo、截图或 UI 贴图。
- Demo Player HUD 只显示 BE/HP，删除 BU 占位和 SH；Boss 蓝条按 `BossActor.MaxShieldDefense` 动态生成，当前为 15 格、每 5 格分组，并直接读取 BossActor 独立护盾。Boss HP 条和护盾条必须以屏幕顶部中央为自身 X 中心，不得用半宽度负偏移把整个条形推向左侧。旧 OnGUI HUD 和 Debug Overlay 不得继续显示 GuardValue。
- Raven 黄光不可格挡前摇必须由共享 `FX_Boss_YellowUnblockableCue.prefab` 提供核心闪光、横向镜头光、火花、电弧、圆形 Halo、双纵向椭圆轨道和横向能量带；所有粒子必须非循环、PlayOnAwake=false，最长延迟加生命周期不超过 0.45 秒。`BossAnimationEventFunc` 必须显式绑定 Boss 上的统一 `BossAttackCueVfxController`，由 `OnYellowAttackParticleStart` 调用 `PlayYellowCue`；控制器负责递归重播、灯光启停、自动结束、红黄互斥和中断清理，`BossAttackExecutor` 不得按 AttackId + elapsed 重复触发。HaloRing 与 HorizontalEnergyBand 的既有非规则圆环、多波形图集规则继续保持；ChaseGrab 使用黄色 BodyColor，SlashCombo 开场已改用红色提示，所有提示的 Visibility 均保持 1。黄光专用资源不得引用 PerfectGuard、BossHit、红光或青蓝刀光资源。
- Demo 右上角 Boss AI 调试面板必须显示 Boss 与玩家根节点的世界空间 3D 直线距离，单位为米并保留两位小数；该字段属于只读诊断信息，不得反向驱动 Raven_Slash MotionWarp、选招或战斗判定。
- `RavenAuthored/M_Raven_MoveChainCombo.anim` 合并 Clip 中的 `OnFastMoveParticleStart` Animation Event 只播放场景显式绑定的 `star1` 根 ParticleSystem；每次事件必须递归 Stop/Clear 后 Play，使根与子粒子从头重播。事件时间沿用旧表现 Clip 的绝对秒数，不得反向驱动 HitNode、伤害、位移或攻击粒子控制器，其他动画不得触发该特效。
- Raven `BodyColor` 必须是不受 BaseMap/EmissionMap 明暗影响的最终颜色覆盖；七个身体槽共享专用 Overlay Shader。独立 `Visibility` 按 `1=显示、0=隐藏` 控制身体、唯一武器和常驻 IdleGlow，不得复用 BodyColor Alpha；身体保持 Opaque / TransparentCutout 深度写入并使用抖动裁剪，武器与 BaseNode 使用每槽 PropertyBlock，禁止运行时调用 `Renderer.material / new Material / Shader.Find`。
- Demo 右上角 Boss AI 调试面板显示当前压力及降压模式、最近选择动作池和动作 ID；它默认隐藏并由场景 `showDebugOnStart` 开发配置开启，只观察正式 Boss 数据，不反向驱动战斗或 AI。
- 旧测试反馈 VFX / SFX 已清理；PerfectGuard 接触爆发已替换为第一版正式自制粒子 prefab，BossHit 使用 `Projectile_Hit_1`，GuardHit / Hit / Knockdown / PerfectGuard 已绑定导入 mp3 音效；PerfectEvade 的既有 `perfectEvadeClip` 必须绑定 `Assets/Audio/stellarblade_voice_effect_perfact_evade.wav`，音量为 `1`，`hit_body.mp3` 当前通过 `hitClipVolume` / `knockdownClipVolume` 按 50% 播放。
- HitStop、CameraShake、VFX、SFX 应能独立关闭。
- Combat Feedback V1 只强化 HitStop 与可选 CameraShake；是否触发相机反馈由 `CombatFeedbackPlayer` 的 `CombatFeedbackKind + CombatAttackType` profile 决定。
- `DamageOnly` 可以触发普通命中反馈，但不改变只扣 HP、不进入受击反应的战斗语义。
- PerfectEvade 属于规避奖励反馈：触发短子弹时间，不触发命中 HitStop，默认不 CameraShake，并通过 `CombatFeedbackPlayer.perfectEvadeClip` 播放 `Assets/Audio/stellarblade_voice_effect_perfact_evade.wav`。必须复用现有玩家 AudioSource 和统一 `PlayOneShot` 路由，不新增第二套音源或 PerfectGuard 专用兼容字段。
- PerfectGuard 属于接触防御成功反馈：触发强短 HitStop、短 CameraShake、白金接触爆发 `FX_Feedback_PerfectGuard_Stellar.prefab` 和 `Assets/Audio/hit_perfect.mp3`，不触发 BulletTime，也不得播放 PerfectEvade 专用音效。
- 剑类攻击出手过程表现与命中接触反馈分离：Player 武器拖尾当前只保留手动配置的 `SlashTrailVFX` / 场景资产；Boss 根对象人工绑定是 Boss HitNode 出手 VFX 的唯一选择源；命中接触反馈仍由 `CombatFeedbackPlayer` 处理。
- Boss 默认武器光效属于常驻外观表现：它以武器子对象层级存在，不代表攻击开始，不参与 HitNode 或伤害结算。
- 没有显式声明需要运行时程序化控制时，特效交付应默认保留开发者手动调整权，不新增会自动重建、刷新或覆盖子对象参数的控制脚本。
- Boss `SlashTrailVFX` 只属于 HitNode 出手表现：`BossAttackExecutor` 按已有 SourcePart 将 Weapon、LeftFoot、RightFoot 固定路由到唯一武器和左右小腿的三个 Behaviour 采样器，并在整个节点激活窗口按帧聚合；已由 `BossHitNodeParticleVfxController` 绑定的节点不同时启用固定拖尾，不新增战斗语义字段，不改变战斗结算。
- 2026-07-09：随 Boss 新招式接入扩展横斩刀光配置。`Raven_MoveCombo/MoveSlash_1-3` 和 `Raven_MoveChainCombo/MoveChainSlash_1-4` 会使用 `SlashTrailVFX`，但侧移 / Orbit / 闪现位移窗口不触发刀光；该配置后续于 2026-07-15 收敛为 Boss 根对象人工绑定。
- 2026-07-10：新增 Raven 黄光不可格挡预警 VFX。`BossYellowUnblockableCueVfxController` 按 AttackId + elapsed 控制 `FX_Boss_YellowUnblockableCue`，资源位于 `Assets/Textures/Boss/YellowUnblockable`、`Assets/Materials/Raven/YellowUnblockable` 和 `Assets/Prefabs/Boss/FX_Boss_YellowUnblockableCue.prefab`。该 cue 只服务预警表现，不触发 `Projectile_Hit_1`，不复用 PerfectGuard 白金爆发，也不参与 HitNode、伤害、PerfectGuard、BossReactionGate 或 MotionWarp。
- Boss 直刺攻击表现使用独立粒子资源；`BossHitNodeParticleVfxController` 的每条人工绑定显式选择 Timeline、HitNode、Prefab、挂点、偏移、缩放、生命周期和方向模式，只在节点首帧生成无父节点世界实例。同一 HitNode 可同时绑定拖尾和多条直刺，未绑定节点不产生出手 VFX。
- Boss 命中接触 VFX 使用 `CombatFeedbackPlayer.BossHit` 分支，必须由真实命中结果触发；`Projectile_Hit_1.prefab` 不由攻击出手、HitNode 激活、横斩刀光或直刺粒子直接播放。命中亮度 pulse 只提高显式绑定 Light 的强度，不直接改共享材质或粒子参数。
- Eve 攻击只保留用户手动新增的武器拖尾 / `SlashTrailVFX` 表现；Raven 攻击使用已完成的青蓝 `SlashTrailVFX`，旧红 / 紫红危险剑光和 Raven 月牙 Prefab 不再使用；PerfectEvade 只补红色方向线和子弹时间，不复用命中火花或 PerfectGuard 金属火花。
- 剑类攻击拖尾优先在场景 / Prefab 中绑定到真实武器采样点；不要恢复旧运行时自动生成拖尾 anchor 的程序化方案。
- Demo 战斗场景的环境光照以角色可读性为先：使用 warm key、cool fill、cyan / magenta rim lights、暗色反射甲板、低密度雾和轻 Bloom 分离玩家 / Boss 轮廓；不得为了氛围把两个角色重新压入大面积暗部。
- Demo 战斗场地以 Boss 技能空间为先：当前视觉战斗面约 48m，底层 Plane 约 60m；外围构件和发光标记服务距离读数，不应缩回小平台，也不应新增会阻挡 Boss 动作位移的硬碰撞。
- Demo 战斗场地以 Boss 技能空间为先：当前视觉战斗面约 144m，底层 Plane 约 180m；外围构件和发光标记服务距离读数，不应缩回小平台，也不应新增会阻挡 Boss 动作位移的硬碰撞。
- Demo 硬地面来源保持单一：底层 `Plane` 的 Ground 层 `MeshCollider` 负责角色接地，视觉甲板和外围环境构件默认不启用 Collider；不得再给场地添加会穿过 Player / Boss 胶囊体的高位硬碰撞面。
- 暂不做 Target Material Flash，也不新增正式 VFX 方案。
- Debug Overlay 只服务调试，不作为正式 UI 结构。

## 近期变更入口

- 2026-09-05：按用户要求将右下操作卡片标题改为“H 键隐藏”，并增加 Combat 内独立 H 显隐切换。实现只缓存现有 `ControlGuide` 根节点并切换 active 状态，不接入玩家输入快照、不恢复旧 `TAB + H` 调试入口，也不新增配置或控制器。Unity 脚本校验 0 error，Console 无项目编译错误，`DemoBattleUiControllerTests` PlayMode 回归 `11/11` 通过。
- 2026-09-02：按用户要求把 Demo 右下角不完整且语义不清的五按钮圆形 `SkillWheel` 替换为不可交互的静态“战斗操作”卡片。新卡片按鼠标 / 键盘分成两列三行并完整展示六项主要按键；旧圆形纹理生成和按钮构建代码删除，不新增输入监听、动态提示或资源。Unity 编译与 Console 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `10/10` 通过，并完成当前 Game View 的实机截图检查。
- 2026-09-02：按用户要求删除 ESC 暂停菜单中的 Boss 启停和 Boss 调试信息显隐按钮，只保留继续、重新开始、退出，并收紧面板高度；同时清理按钮专属文本刷新和已无调用方的 `ToggleBoss`。BossActor 正式能力、右上调试面板及其 `showDebugOnStart` 开发配置均保留。Unity 编译与脚本诊断 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `9/9` 通过。
- 2026-09-02：按用户要求将右上角 Boss AI 压力 / 距离调试面板改为进入战斗时默认隐藏。仅关闭 `showDebugOnStart` 的代码默认值与 Demo 场景值；面板创建和数据刷新保留。暂停菜单显式开关随后由同日后续需求删除，现通过场景配置重新开启。Unity 重编译后 Console 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `9/9` 通过。
- 2026-09-02：按用户要求删除 Demo 左上角运行时调试快捷键面板，以及 `TAB + 1 / 2 / H / C` 的重载、Boss 开关、调试显隐和退出响应；保留 `ESC` 暂停、右上 Boss AI 信息、可选 `EveDebugOverlay` 和暂停菜单的显式操作。Unity 强制重编译后 Console 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `8/8` 通过。
- 2026-09-02：根据实机反馈把 Raven Instrumental 首播从 `EnterCombatCore` 前移到 `StartCombatTransition` 进入 OpeningTransition 的同帧，点击开始后立即播放，不再等待 2.4 秒镜头过渡。`EnterCombatCore` 保留幂等播放以覆盖重试直接开战；停止、暂停、音量、音源和导入配置均未改变。
- 2026-08-31：将 `186. Raven (Instrumental).flac` 接入 Demo 正式战斗生命周期。新增独立二维循环 AudioSource，音量 0.22；Combat 开始播放，首次胜负判定停止，标题、开场过渡、重载、退出和组件禁用保持停止。音频导入改为 Streaming + Background Loading，避免 43.9 MB FLAC 以 Decompress On Load 常驻展开。
- 2026-08-13：修正 Boss HUD 偏左。HP/护盾条 anchored X 统一归零并沿用分段条实际宽度；护盾段数从 BossActor 最大护盾动态生成，随 Demo 配置从 20 改为 15。
- 2026-08-12：更正上一轮音效触发对象：`stellarblade_voice_effect_perfact_evade.wav` 改为只在玩家 PerfectEvade 播放。撤销临时新增的 PerfectGuard 语音字段和追加播放逻辑，Demo 将该 WAV 直接绑定到既有 `perfectEvadeClip`、音量 `1`；PerfectGuard 恢复为只播放 `hit_perfect.mp3`。Unity 编译、Console 与场景复核通过，Editor 域 `CombatFeedbackTests 18/18` 通过。
- 2026-08-11：补全 `BossAnimationEventFunc` 三个音效事件入口，并在 Demo `CH_M_NA_53_Preview` 新增专用 3D AudioSource，显式绑定 redstart、yellowattack、redcharge 三段 WAV。播放使用 `PlayOneShot`，缺绑定时安全报错；没有修改具体 Animation Clip 事件或战斗逻辑。脚本诊断、编译和场景复核通过，Editor 域定向用例 `16/16` 通过。
- 2026-08-11：按用户实战结果将 `BurstAreaSlash_1` 检测球与 Timeline 范围从 `14 → 9.3m`。同时在项目专用 `Spatial section.prefab` 停用 Distortion 子节点，消除 Scene Color 屏幕采样产生的玩家镜面复制；共享材质和源 Prefab 未改。
- 2026-08-10：对齐 `Spatial section` 最大表现范围与 `BurstAreaSlash_1` 检测范围。全生命周期高频采样得到最外可见半径 `13.98m`，Demo 检测球和 Timeline `EffectiveRange` 统一为 `14.0m`；VFX 资源本体未改。
- 2026-08-10：为 Raven `star1` 新增 `BossAnimationEventFunc.OnFastMoveParticleStop`，供动画作者在任意后续帧提前停止并清空根/子粒子；既有 Start 事件可在停止后再次重播。新增两项 EditMode 定向用例，但 Test Runner 因既有初始化超时执行 0 项；Unity 内存定向调用完整验证通过。未修改具体动画事件、Demo 场景或战斗数据。
- 2026-08-10：新增 `BossAnimationEventFunc.OnRedAttackParticle053Start`，用于在 Animation 窗口任意帧播放完整 0.53 秒红光。控制器新增独立加速红光绑定/播放入口，Demo 将 `_053s` Prefab 预置到与 1 秒版相同胸口骨骼并默认隐藏；黄光、1 秒红光、0.53 秒红光保持互斥和统一清理。未向具体 Clip 自动写事件。定向调用验证 15/15 粒子启动、结束时长 `0.530s`，脚本诊断 0 error/warning；EditMode Runner 仍发现 0 项。
- 2026-08-10：从现有 1 秒红光提示复制出独立 `FX_Boss_RedFrenzyCue_053s.prefab`，15 个粒子系统统一使用 `Simulation Speed=1.886793`，让完整表现过程在真实 `0.53s` 内完成；原 Prefab 和 Demo 引用未改。控制器可见时长计算增加 Simulation Speed 换算，固定种子阶段对比与 `0.530s` 自动结束检查通过；脚本诊断 0 error，EditMode Runner 因既有发现问题执行 0 项。
- 2026-08-10：提高 Raven 远程剑气速度。SwordAuraCombo 三条由 `12 → 18m/s`，EvadeBackSwordAura 由 `14 → 20m/s`；视觉 Pivot 与 Collider 继续共享绑定 Speed，生命周期、接力粒子、颜色、材质、角度、出生位置和战斗语义不变。
- 2026-08-10：纠正场景模板直出时破坏 `Sword Slash 6` 原始发射机制的问题。撤销根/Slashes/Slashes2 `2.2s` 常驻，恢复三者 `0.3s`，仅把 `Pivot` 延长到 `2.2s`；Slashes 两层重新表现为沿 Pivot 轨迹不断生成、旧片不断消失的空间接力。`BossDetachedAttackBinding` 新增可选 `VisualTravelDriver`，四条剑气均绑定模板 Pivot；自推进视觉在世界空间播放，并从绑定 `Speed` 换算粒子本地速度，防止与 Detached 父级移动叠加。BurstAreaSlash、各条 Z 角度、Timeline、HitNode、伤害和 AI 未改。固定时间采样、模板定向用例及 BossCombatSystemTests `21/21` 通过，脚本诊断 0 error。
- 2026-08-09：用户否决此前自制四层及月牙 Mesh 剑气方案，并提供 `Assets/Prefabs/Boss/Sword Slash 6.prefab` 作为最终造型。新增 `FX_Boss_RangedSwordAura_SwordSlash6.prefab` 与私有 `M_Boss_RangedSwordAura_SwordSlash6.mat`，仅用粒子颜色、材质去饱和和冰青附加色完成主题替换；源 Prefab 与第三方共享材质保持文件不变，原五个粒子系统、Mesh、Distortion、时序和 Local Simulation 均保留。`BossDetachedLinearHit.prefab` 只替换视觉子 Prefab，Collider、Rigidbody、用户当前关闭的命中组件及战斗链路均未改；被否决方案新增的月牙 Mesh、粒子材质和纹理已清理，旧受版本控制资源已恢复。
- 2026-08-09：将红光六条 Trail 从上下随机分布改为参考图式的上方均匀扇形。固定 X 槽位、窄 Cone 和轻微深度错层控制起点；不同低频单层 Noise 提供弧线差异，六条固定种子路线离线校准到同一胸口核心并在 `0.98s` 完成汇入。Start Size、Trail Lifetime、宽度峰值和采样密度同步提高，使轨迹更长、更明显；其他红光组、`0s` 动画事件、HairColor 与战斗语义不变。
- 2026-08-09：将 `SideWingRight / UpperCrown / LowerScatter / AccentFragments` 对齐 SideWingLeft 的扩散、弧线回流与核心淡出过程。`ConvergenceForceField` 上移为 ShardField 共享节点，五组分别使用 `1 / 1.12 / 1 / 0.78 / 0.80` 力场倍率和统一 Lifetime/Size/External Forces/Limit Velocity 曲线；AccentFragments 的 Orbital/Offset 缩为 25%。保留作者 Burst `30/30/30/30/20` 及各组 Start Speed、Shape、Start Size、颜色、Noise、材质。固定种子确认五组在 `0.45～0.80s` 连续向心且具双向切向轨迹；该变更不影响 Trail/Core/RedSignature、红光 `0s` 事件、HairColor 或战斗判定。
- 2026-08-07：Eve 头发调为肉眼可读但仍克制的轻量摆动：发梢动态权重提高到 0.94，使用 `4.0Hz / 阻尼0.86 / 惯性0.20 / 重力1.15`；碰撞包络收窄并让上背以下代理移除向上法线，头发在身体外侧受重力下滑，不以碰撞弹飞或被腰髋向上托起。
- 2026-08-06：在用户调整后的 `EveAuthored` 头发基础姿态上恢复 Eve 专用轻量混合摆动。新增 `PlayerHairSecondaryMotionController / PlayerHairSpringSolver`，以低惯性、高阻尼、重力、状态权重、碰撞安全动画目标和无能量身体投影避免飘飞与穿模；Raven 和非身体碰撞保持不变。
- 2026-08-06：撤销共享 `HairSecondaryMotionController / HairSpringSolver`、Demo 两个头发控制器、身体代理和定向测试。Eve 改为可编辑动画内的固定向下贴背马尾，Raven 恢复原始动画发骨；不再运行时计算重力、惯性或碰撞。
- 2026-08-06：头发调为重力主导、轻微惯性和快速稳定；Eve 惯性/重力改为 `0.24/0.90`，Raven 长发惯性 `0.21～0.25`、重力 `0.90`，脸侧为 `0.12/0.50`。新增胸腹胶囊和骨盆球，接触采用法向速度清除、`60%` 切向保留及最终接触复核；同时修复 Unity 6 `Vector3.RotateTowards` 幅值参数导致的方向约束失效，以及未关键帧发骨模拟反馈累积。
- 2026-08-07：Raven SlashCombo 红光连续猛攻前摇按参考时序二次重构。Prefab 改为四个直接表现组和根节点 Force Field：立体四向碎片先展开后回收，6 条外围 Trail 与持续核心蓄光并行，核心在约 0.76 秒达到峰值而非首次出现，并按用户确认从事件起完整持续 1 秒后清空。当时方案记录的 Clip `2.05s` 触发点与后移 HairColor 已于 2026-08-09 被用户手调的 `0s` 事件和开场红发曲线取代。统一 `BossAttackCueVfxController` 继续管理 Yellow/Red 互斥、自动隐藏和中断清理。该变更不修改前四击可防御/精防、最终 YellowIai 不可防/可精闪的既有战斗语义，也不包含武器刀光或地面尘土。
- 2026-08-06：为 Eve 与 Raven 增加共享轻量发骨弹簧表现。新增 `HairSecondaryMotionController / HairSpringSolver`，Demo 显式配置 Eve `1 条 / 9 骨` 与 Raven `16 条 / 133 骨`，提供骨长/弯曲约束、头肩背局部碰撞、缩放时间冻结、角色惯性和突变重置；不新增插件、物理关节、配置资产或玩法耦合。
- 2026-08-06：接入 Eve Skill1 三段 SkillEffect 与 hitEffect。新增粒子生命周期控制器，Prefab/场景关闭 14 个 PlayOnAwake，并由状态 Motion 窗口和武器正式命中结果分别驱动主粒子与命中子粒子；未新增 Timeline Clip、Animation Event 或运行时 Prefab 实例化。

- 2026-08-06：在现有 Demo UGUI 中新增标题、开始转场、胜利/失败等待与结算。新增 Eve `TitleCameraAnchor`、Noto Sans SC / Oxanium SemiBold 及其 OFL、原创渐变/扫描线 Sprite；Build Settings 收敛为仅启用 Demo。战斗计时从开始战斗累计非缩放时间、暂停期间不累计并在首次死亡判定冻结；结果页支持再次挑战、返回标题、退出。该变更只扩展演示层，不修改 Player/Boss Dead 状态、伤害、护盾、技能、AI 或死亡动画。

- 2026-08-05（数量已于 2026-08-13 下调）：战斗 HUD 资源显示收敛。Player 左下删除 BU 图标/行与 SH 行，重排为 BE/HP 两行；Boss 次级蓝条从 CombatResource GuardValue 改为 BossActor 独立 Shield Defense，一格对应一个方块并每 5 格分组。

- 2026-08-05：黄光 cue 从 `BossAttackExecutor` 的 AttackId + elapsed 窗口切换为 Raven Animation Event。当前接口已由 2026-08-07 的统一 `BossAttackCueVfxController.PlayYellowCue` 取代；事件触发后递归 Stop/Clear/Play 全部粒子、开启灯光，并按粒子最大延迟与生命周期自动隐藏。该变更只移动表现触发时机，不改变黄光战斗语义。
- 2026-08-05：HaloRing 从与纵向轨道共享的规则偏橙圆环资源拆分为独立 OrganicHalo 纹理/材质。新纹理增加低频轮廓起伏、角向粗细与亮度差、局部内侧重影和宽柔外晕，材质改为偏白柠檬黄；HaloRing 尺寸轻微压扁并增加约 ±3° 随机旋转。VerticalOrbitA/B、HorizontalEnergyBand 与战斗语义保持不变。
- 2026-08-05：HorizontalEnergyBand 从重复使用单张 Arc 纹理改为 4×4、16 种轮廓的柔边波形图集随机帧。最终 Burst 固定为 5，Texture Sheet 固定 Frame Over Time=0 并按粒子随机 Start Frame；图集使用宽低 Alpha 辉光、加厚软核心和极淡副丝，主波幅恢复到 24～36px、次级起伏 6～9px，粒子尺寸为 4.2×0.72，保证波浪和单根宽度在实机屏幕中可读。随机旋转仅约 ±0.35°，波动主要来自纹理内部而非把直线扇开；薄 Box 出生散布和低强度三轴 Noise 只提供轻微独立漂移。场景对象和共享 Prefab 同步使用独立 atlas 材质。该变更只调整黄光表现，不增加脚本或修改战斗语义。
- 2026-08-05：增强并恢复 Raven 黄光不可格挡前摇表现。新增黄光专用柔边圆环纹理、静态加法材质和 HaloRing / VerticalOrbitA/B / HorizontalEnergyBand 粒子层；Demo Boss 恢复 inactive Prefab 实例，当前由统一 `BossAttackCueVfxController` 管理。ChaseGrab 保留黄色 BodyColor；SlashCombo 开场表现于 2026-08-07 改为红色连续猛攻提示，最终 YellowIai 的不可防玩法语义保持不变。战斗 Volume 保持低强度 Bloom；Timeline、HitNode、伤害、防御、AI、MotionWarp 与冷却不受影响。
- 2026-08-04：Boss AI 调试面板由 `PRESSURE / POOL / ACTION` 扩展为 `PRESSURE / DISTANCE / POOL / ACTION`，并加高面板容纳第四行；距离直接取 `Vector3.Distance(bossActor.transform.position, playerStateMachine.transform.position)`。
- 2026-08-04：接通 Raven MoveChainCombo 快速移动粒子 Animation Event。`BossAnimationEventFunc` 在事件帧清除并递归播放 Demo 角色层级内的 `star1`，保持现有非循环、PlayOnAwake=false 与约 0.5 秒参数；不新增停止事件、运行时查找或 Prefab 实例化。
- 2026-08-04：Raven 身体高亮从 URP Lit 色值相乘改为主 ForwardLit 最终输出覆盖；七个身体材质统一使用 `Project EVE/Raven Body Overlay`。随后新增独立 Visibility；初版身体静态 Transparent 方案因多层头发不写深度而回退，现由原始 Opaque / TransparentCutout 材质使用 4x4 屏幕空间抖动裁剪。唯一武器保持透明 Alpha 渐隐，IdleGlow 的三条线、BaseNode 和两盏灯同步缩放并在零值关闭；不改变武器 RGB，不接管 SlashTrailVFX。
- 2026-07-29：删除 Boss 持续拖尾的 Timeline / HitNode 人工白名单与 `BossSlashTrailVfxController`。Demo 执行器固定绑定 `RavenMonster_Weapon/SlashTrailVFX`、`Ab-L-Calf-Tw1/SlashTrailVFX`、`Ab-R-Calf-Tw1/SlashTrailVFX`，由 HitNode SourcePart 选择；直刺粒子控制器和人工绑定保持不变。
- 2026-07-26：Combat VFX 发光材质静态化。新增唯一正式资产 `Assets/Materials/Feedback/M_CombatVfx_Glow.mat`，替换 PerfectEvade 方向线和 `RavenMonster_Weapon.prefab` 四个常驻光效 Renderer 的运行时/场景内嵌材质；`CombatVfxPrimitiveFactory` 改为接收显式材质并使用 `sharedMaterial`，缺配置时报错且不生成，不再提供 `Shader.Find` 或 `new Material` fallback。该变更只收敛表现资源生命周期，不修改 PerfectEvade 判定、子弹时间、Boss 武器外观触发或战斗结算。
- 2026-07-15：Boss HitNode 出手 VFX 改为 Boss 根对象人工绑定。新增两个自定义 Inspector，以 Timeline 限定 HitNode 下拉并检查失效引用；拖尾按采样器聚合完整 HitNode 窗口，直刺首帧捕获世界姿态后不跟随骨骼。Demo 保留现有人工拖尾清单但移除 `ChaseSlash_4` 拖尾，`ChaseSlash_4` 从武器根部沿 Boss 水平正前方生成，`ChaseSlash_3` 从左小腿按挂点朝向生成；两者使用同一 Prefab、缩放 0.3、生命周期 2.2 秒，原武器与腿部展开粒子副本已删除。
- 2026-07-22：制作 Raven 远程剑气表现资源并接入线性 Detached 判定体。新增 `Assets/VFX/Boss/RangedSwordAura/FX_Boss_RangedSwordAura.prefab`、`Assets/Materials/Raven/RangedSwordAura` 与 `Assets/Meshes/Boss/VFX`，并将视觉子节点嵌入 `Assets/Prefabs/Boss/Combat/BossDetachedLinearHit.prefab`；Demo 已有 `SwordAuraCombo_1-3`、`EvadeBackSwordAura_1` 的 HitNode 发射绑定继续复用该 Prefab。该变更只增加出手飞行表现，不修改 Combat Timeline、HitNode、Detached 命中、伤害、PerfectGuard / PerfectEvade 或 Boss AI。
- 2026-06-30：Combat Feedback V1 改为 profile 驱动 HitStop / CameraShake，事件携带 AttackType，DamageOnly 允许播放普通命中反馈。
- 2026-06-30：PerfectEvade 增加短子弹时间反馈；`CombatHitStop` 扩展为反馈时间缩放控制器，暂停前统一取消所有时间缩放。
- 2026-07-01：PerfectGuard V1 标准落地，默认 profile 固定为 HitStop `0.06s`、TimeScale `0.06`、CameraShake `0.075 / 0.12s / 34Hz`；PerfectGuard 使用接触防御反馈，不复用 PerfectEvade BulletTime。
- 2026-07-01：Stellar Blade 风格剑类攻击 VFX V1 曾作为程序化测试占位落地，Player / Boss 攻击出手可显示剑刃拖尾、挥砍弧光和 PerfectEvade 红色方向线；该 Player/Boss 出手方案已在后续清理或替换。
- 2026-07-01：修正 Player / Boss 剑光拖尾 anchor。自动定位从“武器节点名”升级为“武器 Renderer bounds 剑尖采样点”，当前 Demo 中 Player 采样到 `CH_W_Sword_01.001` 本地 `x=-1.27`，Boss 采样到 `CH_M_NA_53_Weapon.001` 本地 `z=1.55`。
- 2026-07-03：根据参考截图新增 Boss 默认武器青蓝常驻光效；最终保留为 Demo 场景 Boss 武器下的 `FX_BossWeaponIdleGlow` 子对象，包含 3 条 LineRenderer、2 个 Light 和粒子。
- 2026-07-03：删除 Boss 默认武器光效自动控制脚本和对应测试。该光效不再由 Play Mode 或组件生命周期自动重建 / 刷新，后续由开发者直接在场景 / Prefab 子对象上调整。
- 2026-07-03：根据 Raven_Slash 参考图新增过渡版 `FX_Raven_Slash_Crescent.prefab`，Demo Boss 旧程序化弧光曾只在 `Raven_Slash / Slash_1` 生成一次；该方案已于 2026-07-07 被 `SlashTrailVFX` 白名单方案替换并清理资源。
- 2026-07-07：接入 Boss `SlashTrailVFX` 并清理旧 Boss 攻击出手 VFX；早期 `BossSlashTrailVfxController` 白名单路径已于 2026-07-29 删除并改为 SourcePart 固定路由。旧 `FX_Raven_Slash_Crescent` Prefab 与 RavenSlash 专用材质删除，`CombatVfxPrimitiveFactory` 保留给命中 / PerfectEvade 反馈使用。
- 2026-07-08：清理 Player 旧攻击出手 VFX。`PlayerAttackState` / `PlayerSkillState` 不再驱动旧运行时拖尾或 Skill 弧光；删除旧控制脚本、EditMode 测试和 `Assets/Prefabs/Combat/AttackVfx` 旧 prefab。Player 武器拖尾只保留用户手动新增的 `SlashTrailVFX` / 场景资产表现。
- 2026-07-08：`ChaseSlash_3_Effect.prefab` 直刺粒子资源完成配色适配并接入运行时触发。为避免污染 Hovl Studio 共享材质，新增 `Assets/Materials/Raven/ChaseSlash3` 专用材质副本，并把 Prefab 粒子颜色统一到青蓝 / 冰蓝 / 白核心 / 冷蓝灰烟尘；原骨骼下专用特效根方案后续于 2026-07-15 改为人工绑定 Prefab、挂点与世界空间首帧生成。
- 2026-07-08：接入 Boss 命中接触特效 `Projectile_Hit_1.prefab`。`CombatFeedbackPlayer` 新增 `bossHitVfx` 专用字段，`BossHit` 反馈优先播放该字段，未绑定时回退旧 `hitVfx`；Demo 场景已绑定 `Assets/Prefabs/Boss/Projectile_Hit_1.prefab` 根 ParticleSystem，并把 `FX_BossWeaponIdleGlow` 下 2 个 Light 绑定为可选 Boss 命中亮度 pulse。
- 2026-07-08：PerfectGuard 正式 VFX 从零制作并替换测试火花。新增透明程序生成贴图 `Assets/Textures/Feedback/PerfectGuard`、加法粒子材质 `Assets/Materials/Feedback/PerfectGuard` 和非循环 prefab `Assets/Prefabs/Combat/Feedback/FX_Feedback_PerfectGuard_Stellar.prefab`；Demo `CombatFeedbackPlayer.perfectGuardVfx` 已指向新 prefab。该变更只替换表现资源，不修改 PerfectGuard 判定、BetaEnergy、HitStop、CameraShake、Boss stagger、SFX 或 `CombatFeedbackPlayer` API。
- 2026-07-09：绑定导入的 Combat Feedback SFX。Demo `CombatFeedbackPlayer.guardClip` 指向 `Assets/Audio/guard_hit.mp3`，`hitClip` 和 `knockdownClip` 指向 `Assets/Audio/hit_body.mp3`，`perfectGuardClip` 指向 `Assets/Audio/hit_perfect.mp3`；PerfectEvade 在该阶段尚无 SFX，后续已由 2026-08-12 条目补齐。`CombatFeedbackPlayer` 新增每类 SFX 音量缩放字段并通过 `AudioSource.PlayOneShot(clip, volume)` 播放，当前 Demo 将 `hitClipVolume` 与 `knockdownClipVolume` 设为 `0.5`，让 `hit_body.mp3` 按 50% 播放。该变更只替换音频表现，不修改命中结果、倒地、GuardHit、PerfectGuard、HitStop、CameraShake 或反馈语义。
- 2026-07-09：清理旧 Combat Feedback 测试资源。删除旧测试 prefab、测试 wav、共享测试材质、测试触发器脚本和 `Assets/_Recovery`；Demo `CombatFeedbackPlayer` 已置空 `hitVfx / guardVfx / perfectEvadeVfx / knockdownVfx / perfectEvadeClip`。该变更不新增替代正式 VFX，不影响 PerfectGuard 正式爆发、BossHit `Projectile_Hit_1`、Boss 横斩 `SlashTrailVFX`、Boss 直刺粒子、HitStop、CameraShake 或反馈结算语义。
- 2026-07-03：Demo 场景新增 `CombatEnvironment_StellarBladeInspired`、`SB_CombatLightingVolume` 和 `Assets/Materials/CombatEnvironment` 材质组；旧 Plane 改用暗色甲板材质，现有三盏灯重命名并调成 warm key / cool fill / amber area，额外补 cyan / magenta 轮廓光和反射探针。
- 2026-07-03：根据实机观感反馈二次调参：整体提亮，降低暗角 / 雾 / Bloom 压暗，扩大场地到可容纳 Boss 超远程位移和后续远程技能的尺度，并新增远距离读数标记。
- 2026-07-03：继续将 Demo 战斗场地扩大 3 倍，视觉面从约 48m 扩到约 144m，底层 Plane 从约 60m 扩到约 180m，距离读数标记调整为 36m / 60m / 84m。
- 2026-07-03：修正 Demo 场地接地碰撞。移除 `CombatEnvironment_StellarBladeInspired/Plane` 上错误高度的 `BoxCollider`，只保留 Ground 层 `MeshCollider`；Play Mode 验证 Player 启动后 `CharacterController.isGrounded=True`，不再因高位碰撞面影响接地。
- 2026-06-16：Stellar Blade 风格战斗 HUD、训练提示与暂停菜单。
- 2026-06-12：基础 VFX / SFX 测试占位绑定。
- 2026-06-12：第 9 阶段 Boss 战闭环校准与反馈基础。

## 关联文档与代码

- `doc/战斗UI需求设计.md`
- `doc/战斗闭环调参与反馈计划.md`
- `Assets/Runtime/Feedback`
- `Assets/Runtime/UI`
- `Assets/Runtime/Core/Debug`

## 遗留风险

- 反馈调参前必须确认命中结算和状态切换正确，否则容易掩盖底层问题。
