# Feedback / UI / VFX / SFX 长期记忆

更新时间：2026-09-05

## 当前状态

- 2026-09-05：Demo 右下操作卡片标题由“战斗操作”改为“H 键隐藏”，`DemoBattleUiController` 在 Combat（含暂停覆盖）中直接读取 H 单次按下，仅切换 `ControlGuide` 根对象；标题、开场过渡和结算阶段忽略 H。该入口不进入 `PlayerInputSnapshot`，不恢复旧 `TAB + H` 调试显隐，也不影响 HUD 其他区域或六项战斗功能。Unity 脚本校验 0 error，Console 无项目编译错误，`DemoBattleUiControllerTests` PlayMode 回归 `11/11` 通过。
- 2026-09-02：Demo 战斗 HUD 右下角旧五按钮圆形 `SkillWheel` 已替换为常驻的两列三行“战斗操作”卡片，完整显示鼠标左键轻攻击、鼠标右键重攻击、鼠标中键锁定、Shift 闪避、E 防御和 1 释放技能。卡片沿用现有深色半透明与冷青视觉，全部 Graphic 关闭射线命中；不显示高亮、冷却或可用性，当时不监听输入，2026-09-05 后仅增加独立 H 显隐。旧圆形 Sprite 的运行时生成代码一并清理。Unity 编译与 Console 0 error，`DemoBattleUiControllerTests` 当时 PlayMode 回归 `10/10` 通过，并完成当前 Game View 的实机截图检查。
- 2026-09-02：ESC 暂停菜单不再创建 Boss 启停按钮和 Boss 调试信息显隐按钮，仅保留继续、重新开始、退出，并把面板高度从 430 收紧到 310。按钮专属文本缓存、逐帧刷新和 `DemoBattleUiController.ToggleBoss` 私有入口已清理；BossActor 正式能力与右上调试面板本体均保留，后者通过场景 `showDebugOnStart` 开发配置启用。Unity 编译与脚本诊断 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `9/9` 通过。
- 2026-09-02：右上角 Boss AI 调试面板的 `PRESSURE / DISTANCE / POOL / ACTION` 创建与逐帧只读刷新继续保留，但 `showDebugOnStart` 的代码默认值和 Demo 场景值均改为 false，进入战斗时不再自动显示。暂停菜单入口已由同日后续需求删除；后续开发调试通过场景 `showDebugOnStart` 配置重新开启。Unity 重编译后 Console 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `9/9` 通过。
- 2026-09-02：Demo 不再创建左上角 `TrainingPromptPanel`，对应的 `TAB + 1 / 2 / H / C` 组合键也不再触发重载、Boss 开关、调试显隐或退出。`ESC` 暂停、右上 Boss AI 只读信息、可选 `EveDebugOverlay` 与暂停菜单的显式按钮均保留；该清理不改变玩家输入快照、战斗状态机或 Boss AI。Unity 强制重编译后 Console 0 error，`DemoBattleUiControllerTests` PlayMode 回归 `8/8` 通过。
- 2026-09-02：`Assets/Audio/186. Raven (Instrumental).flac` 的首播从镜头过渡完成后的 Combat 前移到标题页点击“开始战斗”、进入 OpeningTransition 的同帧，因此 2.4 秒开场镜头旋转期间已有 BGM。标题页点击前保持静音；重试直接进入 Combat 仍会播放，首次胜负判定、返回标题、重载、退出和组件禁用仍立即停止，暂停期间继续且不重复启动。独立二维 AudioSource、`0.22` 音量、Streaming 与 Background Loading 配置不变。
- 2026-08-13：运行时 `BossHUD` 继续使用顶部中央锚点，HP 条与护盾条自身的 anchored X 均改为 0，不再把半宽度误写进中心枢轴的位置而整体偏左。条宽直接沿用 `SegmentedBarView.Configure` 生成的实际宽度；护盾格数动态读取 `BossActor.MaxShieldDefense`，当前 Demo 为 15 格、每 5 格分组。
- 2026-08-13：Lock-on 提示保持为 Raven `Bip001/FX_LockOnMarker` 直接子节点上的单粒子 Billboard，并继续使用固定 `ZTest Always / ZWrite Off` 的私有 Shader。为解决上一版整颗柔化后基本不可见，`LockOnSoftDot` 已重绘为半径 36% 的稳定白芯、36%～62% 的短柔边和全透明外圈；粒子为 `0.30m / Alpha 0.90`，Renderer `Max Particle Size=0.025`，因此远处保留约 6px 清晰核心，近处也不会膨胀成大光斑。控制器只在 Combat、未暂停且正式目标为 Raven 时重播，取消锁定、目标失效、暂停或其他展示状态立即 Stop/Clear 并隐藏。
- 2026-08-12：Demo 标题场景保留原实时 UI，但标题镜头已固定到场景根 `TitleCameraAnchor`，Eve 由默认关闭、仅标题阶段启用的暖色 Spot Light `TitleCharacterKeyLight` 轻微提亮。点击开始时标题 UI 用 `0.35s` 淡出，黑屏遮罩保持隐藏；镜头在 `2.4s` 内可见移动到战斗构图，到位后才切 HUD、输入、锁定与 Boss AI。该补光不参与战斗照明，也不会在 Combat 状态持续开启。
- 2026-08-12：`buff2D_stagger` 的 `xuanwo / star` 透明贴图现按用户提供 GIF 的实际运动复刻为 `FX_Boss_StaggerOrbit3D.prefab`。此前“两个压扁镜像月牙 + 三颗星以 `0.11` Orbital Y 缓慢移动”的五面片方案已删除。当前层级固定为 `VortexRing / Star_Back / Star_Right / Star_Front`：旋涡使用新的水平 XZ 双面 Quad，在头顶中心绕本地 Y 轴旋转；三颗星使用 Billboard，在同一 XZ 圆周上以相同角速度公转。四个非循环 ParticleSystem 均只 Burst 一颗、持续 6 秒，统一周期约 `1.2s/圈`，因此战斗相机中会自然得到 GIF 的扁椭圆、亮弧连续绕圈与前后星点变化。旧镜像 Quad 资产已删除；资源仍由 `BossShieldBreakStunVfxController` 统一开始和清空，未新增脚本、Shader、Spine 运行时、武器表现或玩法逻辑。
- 2026-08-12：完成 Raven 护盾击破长眩晕三阶段视觉。`FX_Boss_ShieldBreakStun.prefab` 含 `BreakBurst / StunLoop / RecoveryWarning`：进入时为青白核心爆闪、三重冲击环、24 枚外飞碎片及破裂电弧；持续阶段为低频身体电弧、上升碎屑、脚下圆阵、五段断裂半环、冷色雾和低强度身体点光；最后 `0.7s` 增加向内收束碎片、加速电弧、收缩圆阵和恢复点光。材质为 PerfectGuard 既有纹理的专用副本，只调整为冰青/深蓝主题，没有新增纹理或 Shader。按用户边界不包含任何武器失能、熄灭、重新点亮、武器 Renderer/Light 或 BodyColor 控制。
- 2026-08-12：用户更正 `stellarblade_voice_effect_perfact_evade.wav` 的触发语义为玩家 PerfectEvade。上一轮为 PerfectGuard 新增的字段和追加播放逻辑已全部撤销；Demo 直接把该 WAV 绑定到既有 `CombatFeedbackPlayer.perfectEvadeClip`，音量为 `1`，继续通过统一 `ResolveVisualAssets` 与玩家 AudioSource 的 `PlayOneShot` 播放。PerfectGuard 只保留 `hit_perfect.mp3`，不会读取 PerfectEvade 音效。该更正不影响 PerfectEvade BulletTime、判定、CameraShake、奖励或 Boss 反应；定向用例确认 PerfectEvade 播放、PerfectGuard 不播放，完整 `CombatFeedbackTests 18/18` 通过。
- 2026-08-11：Raven 新增三条可由动画作者手工放置的无参数 SFX Animation Event：`OnPlayRedStartSound / OnPlayYellowStartSound / OnPlayRedChargeSound`。`BossAnimationEventFunc` 通过显式绑定的 Boss 专用 3D `AudioSource.PlayOneShot` 播放对应 redstart、yellowattack、redcharge WAV，允许相邻声音重叠且不截断前一个 OneShot。Demo 音源位于 `CH_M_NA_53_Preview`，PlayOnAwake/Loop 关闭，Spatial Blend=1、Doppler=0、Min/Max Distance=`1/25m`；三个 Clip 均为序列化资产引用，不做运行时路径查找。缺绑定时记录明确错误并安全跳过；没有自动修改具体 Animation Clip，也不反向驱动战斗逻辑。Editor 域定向用例 `16/16` 通过。
- 2026-08-10：Raven `star1` 快速移动粒子新增无参数 Animation Event 入口 `OnFastMoveParticleStop`。事件会立即停止并清空显式绑定根及全部子 ParticleSystem，允许动画作者在自然结束前手工截断；既有 `OnFastMoveParticleStart` 可在停止后再次从头播放。没有自动向 Clip 写入结束事件，Demo 场景绑定、粒子参数、HitNode、伤害与位移均未修改。内存定向验证确认根/子粒子均完成“播放—清空停止—再次播放”。
- 2026-08-10：0.53 秒红光已接入独立 Animation Event 入口 `OnRedAttackParticle053Start`。事件接收器只转发到 `BossAttackCueVfxController.PlayAcceleratedRedCue`；控制器以独立 `acceleratedRedEffectRoot` 管理该版本，并与黄光、1 秒红光共享互斥、重播、自动结束和中断清理。Demo 在 `Bip001-Spine2` 下预置默认隐藏的 `FX_Boss_RedFrenzyCue_053s` Prefab 实例，沿用 1 秒版本地 TRS；开发者可在任意 Raven 动画帧手工选择新事件。没有自动向具体 Clip 写入事件，原 `OnRedAttackParticleStart` 与 1 秒红光保持不变。定向调用确认 15 个粒子全部启动且理论结束时间为 `0.530s`。
- 2026-08-10：在不替换现有 1 秒 `FX_Boss_RedFrenzyCue.prefab` 和场景绑定的前提下，新增独立 `FX_Boss_RedFrenzyCue_053s.prefab`。新版本保持四组结构、15 个粒子系统、全部作者延迟/生命周期/运动曲线/外力/Noise/Trail/材质不变，只统一设置 `Simulation Speed=1.886793`，让完整过程在真实 `0.53s` 内结束。攻击提示控制器的可见时长计算现会除以各粒子的 Simulation Speed，因此加速版本接入后可在 `0.53s` 自动隐藏，原版仍为 `1s`。固定种子三个阶段的粒子数量与平均半径和原版对应阶段一致。
- 2026-08-10：Raven 远程剑气的唯一实战视觉模板是 Demo 场景 `Boss/FX_Boss_RangedSwordAura_SwordSlash6`，专用 Prefab 副本只保留为资源参考。当前已撤销“根/Slashes/Slashes2 全部延长到 `2.2s`”的错误改法：根与 `Pivot/Slashes`、`Pivot/Slashes2` 均恢复 `0.3s`，只有不可见高速载体 `Pivot` 延长为 `2.2s`，继续通过 Rate over Distance 生成向远处接力的短寿命刀幕。`BossDetachedAttackBinding.VisualTravelDriver` 指向 `Pivot`；运行时视觉克隆保持世界空间，Emitter 依据模板缩放把绑定 `Speed` 换算为 Pivot 的本地 Start Speed，避免视觉内部位移与 Detached 根位移叠加。当前三连速度为 `18m/s`、后撤剑气为 `20m/s`。`BossDetachedLinearHit.prefab` 只保留判定组件；四条剑气继续通过 `LocalEulerAngles.z` 同步滚转视觉与 BoxCollider，飞行方向不变。下方 2026-08-09 的“专用 Prefab 作为运行资源”记录已被本条取代。
- 2026-08-09：Raven 远程剑气最终改用用户提供的 `Assets/Prefabs/Boss/Sword Slash 6.prefab` 作为造型来源。正式运行资源为 `Assets/VFX/Boss/RangedSwordAura/FX_Boss_RangedSwordAura_SwordSlash6.prefab`：完整保留源资源的五个 Particle System、Slash3 Mesh、Distortion、Local Simulation、生命周期与运动参数，仅调整粒子颜色，并让三个斩击 Renderer 使用私有 `M_Boss_RangedSwordAura_SwordSlash6.mat`；私有材质以 `_Desaturation=1` 去除原紫红基色，再用冰青 `_AddColor` 形成白热核心、冰青高光和深蓝青外缘。源 Prefab 与 Hovl Studio 共享材质未改。`BossDetachedLinearHit.prefab` 只替换视觉子 Prefab，Collider、Rigidbody、飞行、命中与伤害链路均保持原语义；此前自制四层/月牙 Mesh 方案已废止并撤回。
- 2026-08-09：`TrailConvergence` 六条轨迹按参考图收敛为上方确定扇形。起点按 X `-1.70 / -1.05 / -0.38 / 0.38 / 1.05 / 1.70m` 排列，Y 为 `1.53～2.05m`，只保留小幅前后错层；Shape 从 `1m / 25°` 的随机 Cone 收窄为 `0.01m / 0.75°`。六条固定种子路线分别使用不同低频单层 Noise，并通过编辑期离线校准在 `0.98s` 汇入同一个胸口核心，不再受根部 External Forces 推离。Start Size 为 `0.085～0.13m`，Trail Lifetime 为 `0.52～0.60s`，Min Vertex Distance 为 `0.055m`，Width 曲线峰值 `1.20`，因此轨迹比旧版更长、更粗且连续。该改动未触碰其他红光表现组、动画事件、HairColor 或战斗规则。
- 2026-08-09：`ShardField` 五组碎片的变化过程已统一。原 SideWingLeft 内的 `ConvergenceForceField` 移为 ShardField 共享辅助节点，保持 Gravity `-3`、Drag `10`、Range `0.08～3m`；五组各自通过 External Forces List 唯一引用它，并按 `1 / 1.12 / 1 / 0.78 / 0.80` 匹配不同初速度。五组从 `0s` 同步起播，Lifetime 统一为 `0.94～1.0s`，Size、向心力与 Limit Velocity（Dampen `0.6`）采用同一阶段曲线，最长碎片不越过 `1.0s`；前四组使用 Orbital `±1.76 / ±1.12 / ±8` 和 Offset `±0.12 / ±0.09 / ±0.07m`，AccentFragments 为 25% 范围。Burst `30/30/30/30/20`、各组速度/Shape/Start Size/颜色/Noise/材质保持作者值。固定种子验证五组均在 `0.45～0.80s` 向核心连续收束，并同时存在正反弧线；红光事件与 HairColor 保持用户手调值。
- 2026-08-09：红光动画触发与红发颜色采用用户最终手调时间：`OnRedAttackParticleStart` 位于 `M_Raven_SlashCombo` Clip Local Time `0s`；HairColor 保留 `0 / 0.125 / 0.208333 / 1.5 / 2.125 / 2.625 / 15.875s` 等作者键位。旧方案的事件 `2.05s` 与曲线 `1.90～4.55s` 已废止，测试和文档不得再据此覆盖动画资产。
- 2026-08-09：红光 `ShardField` 五个粒子系统已修正 4×4 图集采样。旧 Prefab 关闭 UV Module 且为 `1×1`，导致每颗粒子渲染整张含 16 枚碎片的纹理，形成重复矩形卡片；现改为启用 Texture Sheet Animation、`4×4`、每粒子随机固定单帧、`Frame over Time=0`。碎片继续使用原专用图集和运动时序，Trail/Core/RedSignature 与 1 秒生命周期不变。
- 2026-08-07：独立 `FX_Boss_RedFrenzyCue.prefab` 已按 24 帧参考时序重构为四个直接表现组。`ShardField` 由左右主翼、上方冠层、下方散片和黄绿强调碎片构成立体放射场，同批粒子先展开后受根节点 Force Field 回收；`TrailConvergence` 包含 6 条从不同外围位置错时汇入核心的 Noise Trail；`CoreChargePulse` 从 0 秒保持小型青白核心并连续蓄亮，在约 0.76 秒与仍存 Trail 同时达到 PeakFlash/LensLine 峰值；`RedSignature` 保留胸口红点，头发红色由动画专用 HairColor 提供。专用加法材质与原创纹理不复用黄光、PerfectGuard、BossHit 或青蓝刀光；所有粒子 Loop/PlayOnAwake 关闭，从事件起完整持续 `1.00s` 后恰好清理。统一 `BossAttackCueVfxController` 保证红黄互斥、从头重播和中断清理。
- 2026-08-07：Eve 头发可见性调优后，发梢最多混入 94% 动态结果，低频高重力保证下垂，20% 世界空间惯性提供可见但克制的移动滞后。身体碰撞半径和安全间隙缩小以减少悬空；上背以下接触法线不会向上托举头发，仍保留无能量投影、深接触清速和 62% 切向滑动。该修正不接入 Raven、Unity Physics 或战斗逻辑。
- 2026-08-06：Eve 使用“作者动画轮廓 + 轻量运行时修正”的头发表现：69 个 `EveAuthored` Clip 继续提供动作基础姿态，玩家专用单链求解器仅增加重力下垂、15% 世界空间滞后和快速回稳。身体防穿模使用内部数学代理，不创建 Collider；动画目标若在身体内，先生成安全目标并以无能量投影校正，避免弹飞。Raven 不接入该系统，仍只使用原动画曲线；不使用 Cloth、Rigidbody/Joint 或玩法状态回调。
- 2026-08-06：Eve Skill1 使用 `Assets/Prefabs/Player/SkillEffect.prefab` 的三段共用表现。主粒子在三个吸附窗口起点清空重播，固定作者位置不实例化、不追踪 HitPosition；`hitEffect` 只响应正式伤害接触并在每段 HitNode 结束时关闭。Prefab 与 Demo 场景均绑定 `PlayerSkillEffectController`，14 个非循环粒子关闭 PlayOnAwake，技能中断会清理残留。
- 2026-08-06：Demo 已在原场景内完成《剑星》视觉语言的标题与胜负结算流程。`DemoBattleUiController` 私有区分 `Title / Combat / PendingVictory / PendingDefeat / Result`，标题显示实时 Eve 右侧近景、暗色渐变/扫描线与冷青菜单；开始切换负责镜头、输入、Boss AI、HUD 和鼠标状态。Boss / Player Dead 分别触发胜利 / 失败，同帧死亡按失败；死亡演出等待约 4.8 秒后以 TimeScale 0 显示结果、冻结战斗时间，并支持再次挑战、返回标题和退出。字体为带 OFL 的 Noto Sans SC 与 Oxanium SemiBold，装饰纹理为项目原创静态 Sprite。Build Settings 只启用 Demo；UI PlayMode 定向测试 5/5 通过，并完成 1920×1080、2560×1440、3440×1440 视觉检查。
- 2026-08-05：Demo 战斗 HUD 删除 BU 占位图标/资源行与玩家 SH 行，只保留 BE、HP 两行；BE 固定 32 段。Boss 蓝条改为读取 `BossActor.CurrentShieldDefense / MaxShieldDefense` 的 20 个一一对应方块，每 5 格分组，护盾击破到 Knockdown 起身期间保持全空。旧 OnGUI HUD 与 Debug Overlay 同步删除 GuardValue 显示。
- 2026-08-05 黄光 cue 改为 Animation Event 驱动。`OnYellowAttackParticleStart` 只调用显式绑定的黄光控制器；控制器激活 `FX_Boss_YellowUnblockableCue`、递归清空并重播粒子、开启灯光，并按子粒子的最大 `StartDelay + StartLifetime` 自动隐藏。`BossAttackExecutor` 不再 Tick 旧时间窗，但在攻击开始、结束和组件禁用时仍负责清理，避免中断残留或同帧双重重播。
- 2026-08-05 HaloRing 参考图还原继续收敛：HaloRing 已从与 VerticalOrbitA/B 共享的规则偏橙圆环资源中拆出，独立使用 `T_Raven_YellowCue_OrganicHalo_512` 与 `M_Raven_YellowCue_OrganicHalo`。新纹理包含低频半径起伏、角向粗细/亮度差、局部内侧重影与宽柔外晕；材质为偏白柠檬黄，粒子尺寸轻微压扁并随机旋转约 ±3°。纵向轨道仍使用原 Halo 材质，横向能量带不受影响。
- 2026-08-05 Raven 黄光不可格挡 cue 完成第二版增强。`FX_Boss_YellowUnblockableCue.prefab` 在原 CoreFlash、HorizontalLensLine、RadialSparks、LightningArcs 基础上新增 HaloRing、VerticalOrbitA/B 与 HorizontalEnergyBand，全部为 PlayOnAwake=false、非循环且最长延迟加生命周期不超过 0.45 秒；HorizontalEnergyBand 使用专用 4×4 波形图集和独立加法材质。ChaseGrab 保留轻 HDR 黄色 BodyColor 和 Visibility=1；SlashCombo 当时的黄色曲线已被 2026-08-07 红光 HairColor 方案替代，最终 YellowIai 的不可防语义保持。Combat Volume 使用 Threshold 1.05 / Intensity 0.25 / Scatter 0.60 的 Bloom。
- 2026-08-04 Demo 右上角 Boss AI 调试面板新增 `DISTANCE`，按帧显示 `BossActor` 根节点与 `PlayerStateMachine` 根节点之间的世界空间 3D 直线距离，单位为米并保留两位小数；缺少任一引用时显示 `--`。该读数只用于诊断 Raven_Slash 等动作的选招距离与 MotionWarp 表现，不参与 AI、位移或攻击判定。
- 2026-08-04 Raven `MoveChainCombo` 的快速移动星光由合并后的 `RavenAuthored/M_Raven_MoveChainCombo.anim` Animation Event 驱动；事件沿用旧表现 Clip 的绝对时间并调用 `BossAnimationEventFunc.OnFastMoveParticleStart`。接收器只枚举场景显式绑定的 `star1` 根与子 ParticleSystem 并逐个 Stop/Clear/Play，三套非循环粒子可重复从头播放，不由 HitNode、伤害或 `BossHitNodeParticleVfxController` 触发。
- 2026-08-04 Raven 动画颜色与显隐拆分：`BodyColor` 继续在 URP Lit 最终输出后进行全身 HDR 覆盖；独立 `Visibility` 用 4x4 屏幕空间抖动裁剪七槽身体，避免将多层头发放入透明队列，并同步淡出唯一武器和 `FX_BossWeaponIdleGlow`。BaseNode 继续共享 `M_CombatVfx_Glow`，只由每 Renderer PropertyBlock 改 Alpha；零值仅禁用视觉组件，不关闭武器 GameObject。SlashTrailVFX、Detached、粒子与命中反馈仍独立。全链路不生成或复制材质。
- 2026-07-26 Demo 训练调试组新增右上角 Boss AI 面板，显示 `PRESSURE / POOL / ACTION`。压力行同时标出 `NORMAL / DECAY`；面板数据直接读取 `BossActor` 正式只读属性，可随 `TAB + H` 与训练提示一并隐藏，正式战斗 HUD 不受影响。
- 已接入 CombatFeedbackBus、HitStop、CameraShake 和正式 / 手动配置的关键反馈资源；旧测试 VFX / SFX 占位已清理。
- Combat Feedback V1 已聚焦 HitStop 与可选 CameraShake：`CombatFeedbackEvent` 携带 `AttackType`，`CombatFeedbackPlayer` 通过 Inspector profile 按 `CombatFeedbackKind + CombatAttackType` 选择 HitStop 和相机反馈参数。
- `DamageOnly` 命中可以触发普通命中反馈，用于 Skill 多段只扣血但仍有卡肉表现；它仍不进入 Player / Boss 受击反应。
- PerfectEvade 已按规避奖励反馈接入短子弹时间：`CombatFeedbackPlayer` 请求 `CombatHitStop.RequestSlowMotion()`，不触发命中 HitStop，也默认不 CameraShake。
- PerfectGuard V1 固化为接触防御成功反馈：默认 HitStop `0.06s`、TimeScale `0.06`、CameraShake `0.075 / 0.12s / 34Hz`，播放正式白金接触爆发 `FX_Feedback_PerfectGuard_Stellar.prefab` 和接触音 `Assets/Audio/hit_perfect.mp3`；它不使用 PerfectEvade 的 BulletTime，也不播放 PerfectEvade 专用音效。
- Stellar Blade 风格剑类攻击 VFX V1 曾作为程序化测试占位接入；截至 2026-07-08，Player/Boss 旧程序化攻击出手拖尾和弧光已清理，Player 只保留手动新增的 `SlashTrailVFX` / 场景资产武器拖尾。
- 2026-07-01 剑类攻击拖尾曾修正为追踪武器 Renderer 本地 bounds 推导出的剑尖采样点，避免 TrailRenderer 挂在角色根、武器根或 mesh pivot 后看起来不沿剑刃运动；该旧程序化玩家拖尾已在 2026-07-08 删除。
- 2026-07-03 Boss 默认武器外观改为纯场景特效对象：`CH_M_NA_53_Weapon.001/FX_BossWeaponIdleGlow` 下保留青蓝核心线、两侧淡光边、剑尖 / 剑柄能量点、点光和轻粒子；默认武器光效自动控制脚本和测试已删除，Play Mode 不再覆盖手动调整。
- 2026-07-07 Boss 攻击出手表现已从旧程序化 Raven 剑光切换为 `SlashTrailVFX`：Demo 场景保留 `FX_BossWeaponIdleGlow` 常驻武器光和 `SlashTrailVFX` 实时刀光轨迹；旧 `FX_Raven_Slash_Crescent` Prefab 与 RavenSlash 专用材质已删除。拖尾选择后续于 2026-07-15 改为 Boss 根对象人工绑定。
- 2026-07-08 `ChaseSlash_3_Effect.prefab` 已完成青蓝化调色并接入 Boss 直刺攻击表现。该 Prefab 引用 `Assets/Materials/Raven/ChaseSlash3` 下的专用材质副本，避免修改 Hovl Studio 导入包共享材质影响其他特效；原骨骼下展开副本后续于 2026-07-15 删除，改为 HitNode 首帧生成世界空间实例。
- 2026-07-15 Boss HitNode 出手 VFX 已收敛到人工绑定：拖尾和直刺控制器都位于 Boss 根对象，每条绑定先选择 Timeline 与 HitNode，再引用具体采样器、Prefab 和挂点；同一 HitNode 可配置多条。拖尾覆盖整个激活窗口，直刺只捕获首帧姿态且不跟随骨骼。Demo 的 `ChaseSlash_4` 沿 Boss 水平正前方从武器根部生成，`ChaseSlash_3` 按左小腿挂点朝向生成，均在 2.2 秒后销毁。
- 2026-07-22 Raven 远程剑气 VFX 已制作并挂到线性 Detached 判定体：新增 `FX_Boss_RangedSwordAura.prefab`，包含白核心、青蓝半透明剑气壳、水平镜头光、运动拖尾和碎片粒子；`BossDetachedLinearHit.prefab` 嵌入该视觉子节点。`SwordAuraCombo_1-3` 与 `EvadeBackSwordAura_1` 仍通过 `BossDetachedAttackEmitter` 绑定 HitNode 与独立判定体，视觉随判定体飞行，不反向决定命中。
- 2026-07-26 `Runtime_CombatVfx_Glow` 已收敛为唯一正式资产 `Assets/Materials/Feedback/M_CombatVfx_Glow.mat`。PerfectEvade 方向线通过 `CombatFeedbackPlayer` 显式传入该材质并使用 `LineRenderer.sharedMaterial`，Raven 武器常驻光效 Prefab 的 BaseNode/CoreLine/RightEdge/LeftEdge 也引用同一资产；旧运行时创建代码和 Demo 场景内嵌材质副本已清理。
- 2026-07-08 `Projectile_Hit_1.prefab` 已接入 Boss 成功命中玩家后的接触反馈。`CombatFeedbackPlayer` 新增 `bossHitVfx` 字段，`BossHit` 反馈优先播放该 ParticleSystem prefab，未绑定时回退通用 `hitVfx`；Demo 当前绑定 `Assets/Prefabs/Boss/Projectile_Hit_1.prefab`。同一反馈还可选触发 Boss 命中亮度 pulse，当前绑定 `FX_BossWeaponIdleGlow` 下 2 个 Light，短暂提高强度后还原。
- 2026-07-08 Player 旧攻击出手 VFX 清理完成：删除旧运行时拖尾 / 弧光控制脚本、对应 EditMode 测试和 `Assets/Prefabs/Combat/AttackVfx` 旧 prefab；`CombatVfxPrimitiveFactory` 保留，因为 `CombatFeedbackPlayer` 仍用它播放 PerfectEvade 方向线。
- 2026-07-08 PerfectGuard 正式接触爆发 VFX 已从零制作并替换 Demo 测试火花：新增透明程序生成贴图 `Assets/Textures/Feedback/PerfectGuard`、加法粒子材质 `Assets/Materials/Feedback/PerfectGuard` 和 prefab `Assets/Prefabs/Combat/Feedback/FX_Feedback_PerfectGuard_Stellar.prefab`。Prefab 由 CoreFlash、HorizontalLensLine、RadialSparks、LightningArcs、ImpactRing、WarmDustGlow 六层非循环 ParticleSystem 组成；Demo `CombatFeedbackPlayer.perfectGuardVfx` 已绑定该 prefab。
- 2026-07-09 Combat Feedback SFX 已替换为导入音频：Demo `CombatFeedbackPlayer` 绑定 `Assets/Audio/guard_hit.mp3` 给 `guardClip`，绑定 `Assets/Audio/hit_body.mp3` 给 `hitClip` 和 `knockdownClip`，绑定 `Assets/Audio/hit_perfect.mp3` 给 `perfectGuardClip`。因此 PlayerHit、BossHit、Knockdown、Dead 共用身体命中音效，GuardHit 使用普通防御音效，PerfectGuard 使用专用完美防御音效；PerfectEvade 在该阶段尚无 SFX，后续已由 2026-08-12 条目补齐。`CombatFeedbackPlayer` 通过每类 SFX 的 Inspector 音量缩放调用 `AudioSource.PlayOneShot(clip, volume)`，当前 Demo 中 `hitClipVolume = 0.5`、`knockdownClipVolume = 0.5`，让 `hit_body.mp3` 在普通命中、Boss 命中、倒地和死亡反馈中按 50% 播放。
- 2026-07-09 旧 Combat Feedback 测试资源已清理：删除旧测试 prefab、测试 wav、共享测试材质、测试触发器脚本和 `Assets/_Recovery`。Demo `CombatFeedbackPlayer` 已置空 Hit / GuardHit / Knockdown / PerfectEvade 的测试 VFX 引用，PerfectEvade 保留短子弹时间和程序化红色方向线但暂时没有音效。
- 2026-07-10 Raven 黄光不可格挡预警 VFX 已从零生成并接入：新增透明程序生成贴图 `Assets/Textures/Boss/YellowUnblockable`、加法粒子材质 `Assets/Materials/Raven/YellowUnblockable` 和 prefab `Assets/Prefabs/Boss/FX_Boss_YellowUnblockableCue.prefab`。Demo Boss 已挂 `BossYellowUnblockableCueVfxController` 并绑定该子特效，`Raven_ChaseGrab` 在 0.85s-1.45s、`Raven_SlashCombo` 在 2.55s-3.05s 播放黄光 cue。该 cue 不复用 PerfectGuard 白金爆发、不复用青蓝 `SlashTrailVFX`，也不触发 BossHit 接触 VFX。
- 2026-07-03 Demo 场景新增 `CombatEnvironment_StellarBladeInspired`：用暗色反射甲板、冷 / 紫红发光导线、外圈科幻构件、warm key、cool fill、cyan / magenta rim lights、Trilight 环境光、轻雾和全局 `SB_CombatLightingVolume` 保证玩家与 Boss 在战斗镜头中不被暗部吞掉。
- 2026-07-03 Demo 场景二次调参后改为更亮的大场地基线：视觉战斗面约 48m，底层 Plane 约 60m，降低 Fog / Vignette / Bloom 压暗，外围构件推远，给 Boss 超远程位移和后续远程技能留出可读空间。
- 2026-07-03 Demo 场景继续按 3 倍扩大：视觉战斗面约 144m，底层 Plane 约 180m，外围构件约 94m，新增 36m / 60m / 84m 距离读数标记；大场地灯光范围和反射探针已同步外扩。
- 2026-07-03 Demo 地面碰撞校正：`CombatEnvironment_StellarBladeInspired/Plane` 只保留 Ground 层 `MeshCollider` 作为玩家和 Boss 的硬地面；旧 `BoxCollider` 位于玩家胶囊体中部高度，会破坏 CharacterController 接地判定，已移除。
- Debug Overlay 支持显示 Player、Boss、窗口、Buffer、Hit 结果等调试信息。
- 战斗 HUD、训练提示和暂停菜单已有 Stellar Blade 风格测试版。

## 关键规则

- 2026-08-11 用户实战校准覆盖上一版离线包络估算：`Spatial section` 对应的 `BurstAreaSlash_1` 战斗有效半径为 `9.3m`。项目专用 `Assets/Prefabs/Boss/Combat/Spatial section.prefab` 必须停用 `Distortion` 子节点，避免共享 `URP_Distortion.shadergraph` 通过 Scene Color、Screen Position 和 `_Distortionpower=1000` 大幅偏移屏幕采样并显示玩家镜像；第三方共享材质、Shader Graph、源 Prefab和其余粒子层不得修改。
- 2026-08-10 规则覆盖下方旧剑气条目：Demo 场景 `Boss/FX_Boss_RangedSwordAura_SwordSlash6` 是唯一实战视觉模板，今后直接修改该场景对象即影响 SwordAuraCombo 三条与 EvadeBackSwordAura 一条。必须保留原始接力语义：`Pivot` 负责移动且存续 `2.2s`，根、Slashes、Slashes2 只存续 `0.3s`，Slashes 两层继续按距离生成，禁止用拉长刀幕寿命模拟飞行。配置 `VisualTravelDriver=Pivot` 时视觉克隆不得作为移动 Detached 的子物体；绑定 `Speed` 是 Collider 与 Pivot 的共同世界速度来源。`LocalEulerAngles.z` 只追加整个剑气与 Collider 的滚转，不修改速度方向。`BossDetachedLinearHit.prefab` 只保留判定，BurstAreaSlash 不绑定该模板；VFX 不得反向驱动命中、伤害、Timeline、PE 或 AI。
- Raven 远程剑气造型以用户提供的 `Assets/Prefabs/Boss/Sword Slash 6.prefab` 为唯一来源，不再维护 `BladeCore / BladeHalo / EnergyVeil / TornCrest` 自制四层或月牙 Mesh 方案。正式使用专用副本与私有 SwordSlash 材质完成青蓝化；允许调整粒子 Start Color、Color over Lifetime、私有材质 `_Desaturation / _AddColor`，不得修改源 Prefab、Hovl Studio 共享材质、Slash3 Mesh、五个粒子系统的结构/时序/Distortion，也不得新增运行时控制脚本。视觉只随 `BossDetachedLinearHit` 整体移动，不得反向驱动 Collider、命中、伤害、Timeline、PE、AI 或飞行轨迹。
- 当前旧测试反馈 VFX / SFX 已清理；PerfectGuard 接触爆发使用第一版正式自制粒子 prefab，BossHit 使用 `Projectile_Hit_1`，GuardHit / Hit / Knockdown / PerfectGuard 已绑定导入 mp3 音效，PerfectEvade 使用 `stellarblade_voice_effect_perfact_evade.wav`；`hit_body.mp3` 通过 `hitClipVolume` / `knockdownClipVolume` 在 Demo 中按 50% 播放。
- HitStop、CameraShake、VFX、SFX 应能独立关闭和替换。
- CameraShake 由 Feedback profile 显式选择，不是所有命中默认触发；V1 不做 Target Material Flash。
- PerfectEvade 是成功闪避表现，不属于命中受击反馈；不得复用 HitStop 语义实现卡肉。
- PerfectGuard 是成功接触防御表现，属于 Guard 结算输出；普通 GuardHit 不复用 PerfectGuard 的白金爆发资源、资源奖励和 Boss stagger 语义。
- 攻击出手剑光与命中接触反馈是两条链路：Player 武器拖尾当前只保留手动配置的 `SlashTrailVFX` / 场景资产；Boss 攻击执行器按 HitNode SourcePart 固定路由武器与左右小腿拖尾，但粒子控制器已绑定的节点排除固定拖尾；`CombatFeedbackPlayer` 只处理真实结算后的命中、Guard、PerfectGuard、PerfectEvade、BossHit 专用接触 VFX 和命中亮度 pulse。
- Boss 默认武器光效是场景 / Prefab 子对象表现，不代表攻击窗口开启，也不驱动伤害、命中、状态或位移。
- Combat VFX 通用发光材质必须使用正式静态资产；禁止恢复 `Shader.Find / new Material / Renderer.material` 运行时生成路径。PerfectEvade 与武器常驻光效可以共享材质资源，但各自的玩法触发与 Renderer 颜色仍保持独立。
- 没有显式需求时，新增可视特效应优先交付可手动调整的场景 / Prefab 对象，不添加会在 Play Mode 自动覆盖子对象参数的运行时控制脚本。
- Boss `SlashTrailVFX` 属于 Attack 专属表现资源，不是新战斗语义：人工绑定只读 Timeline / HitNode 激活结果选择采样器，不参与 HitNode 检测、伤害、打断、PerfectGuard 或位移。手部、腿部、武器 HitNode 均不会因 SourcePart 自动获得特效。
- `Raven_MoveCombo` / `Raven_MoveChainCombo` 的侧移、Orbit、闪现 CodeMove 窗口不是 HitNode，因此不能成为出手 VFX 绑定目标；各 `MoveSlash_* / MoveChainSlash_*` 是否采样刀光以 Demo 场景当前人工绑定为准。
- 黄光 cue 是攻击出手预警，不是命中接触反馈；黄光 HitNode 是否同时具有青蓝刀光也只由人工 VFX 绑定决定，不改变其不可格挡、PerfectEvade 或 PerfectGuard 结算语义。
- Boss 直刺 VFX 与横斩刀光分离：直刺首帧生成无父节点世界实例，横斩在整个 HitNode 窗口持续采样；同一 HitNode 可同时绑定两类效果，二者都不反向驱动命中、伤害或状态。
- Boss 命中接触 VFX 与攻击出手 VFX 分离：`Projectile_Hit_1` 只响应 `CombatFeedbackEvent` 的 `BossHit`，意味着 Boss 已经成功命中玩家并产生可反馈结果；挥空、HitNode 仅激活、横斩刀光或直刺粒子播放都不会单独触发该 prefab。
- Player V1 旧程序化攻击 VFX 已删除；Boss 武器横斩使用现有 SmoothTrail / VFX Graph 资产。当前仍不做 Target Material Flash、血液、断肢、全屏后处理或处决特效。
- Demo 战斗环境服务角色可读性和 Boss 战展示，不承载开放场景探索；后续调光应优先保持玩家 / Boss 轮廓分离，再调整氛围明暗。
- Demo 场地大小应优先满足 Boss 位移和技能射程验证；新增环境构件默认不加硬碰撞，避免干扰 MotionWarp、CodeMove 或远程技能落点。
- 当前 Demo 场地基线按超远程技能验证保留 144m 视觉面和 180m 底层 Plane；后续不要无意缩回 48m 小场地。
- Demo 硬地面来源应保持单一：底层 `Plane` 的 Ground 层 `MeshCollider` 负责接地，视觉甲板、读数标记、外围构件和装饰灯带默认不启用 Collider。
- Debug Overlay 是调试工具，不应承载正式 UI 逻辑。

## 主要代码入口

- `Assets/Runtime/Feedback`
- `Assets/Runtime/Feedback/VFX`
- `Assets/Runtime/Core/Debug`
- `Assets/Runtime/UI`

## 下一步入口

- 按真实命中链路回归 HitStop、CameraShake、VFX、SFX。
- Play Mode 回归 Player Light / Heavy、Skill1 与手动 `SlashTrailVFX` 武器拖尾观感，以及 Raven 人工绑定 HitNode 的拖尾启停、直刺首帧世界姿态、未绑定节点反例和残留销毁。
- 替换正式素材前先保证开关和调试可见性。

## 已知风险

- 反馈阶段容易掩盖底层命中和状态 bug，调参前应先确认 CombatHitResult 和状态切换正确。
