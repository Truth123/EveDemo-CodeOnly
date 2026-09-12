# 战斗 UI 需求设计

更新时间：2026-09-05

本文档记录 Project EVE Demo 求职演示用战斗 UI 的设计规则。目标是参考《剑星》Boss 战与训练场 UI 的信息组织方式，结合当前项目已实现功能做相似但不复制原素材的 UGUI 界面。

## 1. 设计目标

1. 正式战斗 HUD 用于演示，不显示状态机、HitNode、MotionWarp 等开发字段。
2. 左上角训练 / 快捷键调试面板不再创建；开发 Debug 仅保留 `EveDebugOverlay` 和右上角 Boss AI 信息，进入战斗时默认隐藏，需要调试时通过场景中的 `showDebugOnStart` 配置重新开启。
3. UI 不反向驱动战斗判定、伤害、资源、输入窗口或 Boss AI 出招逻辑。
4. 不导入或复制《剑星》原始 UI 贴图；首版使用 UGUI 文本、Image、分段条和静态按键卡片。
5. 标题与结算只借鉴《剑星》的暗色、留白、冷青高亮和信息层级，不使用其 Logo、截图或 UI 贴图。

## 2.1 同场景标题界面

- `Demo.unity` 加载后首先进入 `Title`，使用实时 Eve、场景灯光和环境特效作为动态背景，不新增主菜单场景。
- 标题为“伊芙计划”，副标题为“渡鸦战斗演示”；按钮为“开始战斗”和“退出游戏”。
- Eve 下的 `TitleCameraAnchor` 提供右侧头肩近景，标题镜头使用约 `38°` FOV；左侧保留标题和菜单安全区。
- 标题状态保持 `Time.timeScale = 1`，但禁用 `PlayerInputReader` 和 `PlayerCameraRig`、停止 Boss AI、隐藏战斗 HUD / 开发调试面板 / 暂停菜单，并显示、解锁鼠标。
- 标题背景保留渐变和局部冷青规则线，不创建全屏扫描线平铺层。
- 点击开始后标题 UI 在 `0.35s` 内淡出，镜头用 `2.4s` 可见缓动到锁定战斗机位；镜头到位前玩家输入、锁定、PlayerCameraRig 和 Boss AI 保持关闭，到位后才统一开始战斗。

## 2.2 战斗结算界面

- 展示状态由 `DemoBattleUiController` 私有维护为 `Title / OpeningTransition / Combat / PendingVictory / PendingDefeat / Result`；暂停仅允许在 `Combat` 中开启。
- `BossActor.CurrentState == Dead` 判定胜利，`PlayerStateMachine.Context.CurrentState == Dead` 判定失败；同帧双方死亡时失败优先。
- 首次判定后立刻关闭战斗输入并隐藏 HUD，保持非缩放时间运行约 `4.8s` 播放死亡演出；之后取消 HitStop、令 `Time.timeScale = 0`，用 `0.35s` 淡入结算页。
- 胜利显示“作战完成 / 目标已清除：渡鸦”，失败显示“作战失败 / 伊芙机能停止”；两者均显示冻结的战斗时间。
- 战斗时间从“开始战斗”完成切换时开始，以非缩放时间累计，暂停期间不累计，在首次死亡判定时冻结。
- 结算按钮为“再次挑战 / 返回标题 / 退出游戏”。再次挑战重载 Demo 并用一次性静态启动意图跳过标题；返回标题正常重载 Demo；该意图在 SubsystemRegistration 时清理。

## 2.3 战斗背景音乐

- 标题页点击“开始战斗”前不播放背景音乐；点击后进入 `OpeningTransition` 的同帧立即播放 `Assets/Audio/186. Raven (Instrumental).flac`，不得等待开场镜头过渡结束。
- 背景音乐使用独立的二维 `AudioSource`，音量固定为 `0.22`、持续循环，不复用玩家命中反馈或 Raven Animation Event 的音源。
- 首次进入 `PendingVictory / PendingDefeat` 时立即停止并复位播放位置；返回标题、重载场景、退出游戏或组件禁用时也必须停止。
- 暂停菜单只暂停战斗逻辑，不停止背景音乐；继续游戏后不得重复叠播或从头重启。

## 2. 参考图拆解

主战斗 HUD：

- 上中：Boss 名称与长分段 Boss HP 条，下方显示独立防御护盾。
- 左下：玩家资源区只包含 `BE / HP` 两行分段资源条。
- 右下：默认显示标题为“H 键隐藏”的静态操作卡片，以两列三行完整展示鼠标与键盘的主要战斗按键，不表达可用状态；Combat 中可用 H 单独隐藏或恢复。

开发调试 UI：

- 不创建左上角 `TrainingPromptPanel`，不显示 `TAB + Key` 或 `ESC` 快捷键提示。
- 右上开发调试面板继续显示 Boss 当前压力、降压模式、最近选择动作池、动作和距离。
- 右上 Boss AI 信息与可选 `EveDebugOverlay` 进入战斗时默认隐藏；暂停菜单不提供调试开关，需要调试时由开发者启用场景中的 `showDebugOnStart` 配置。正式战斗 HUD 不受影响。
- `ESC` 仍可直接打开或关闭暂停菜单。

## 3. 当前项目资源映射

| UI 项 | 当前数据来源 | 首版显示规则 |
| --- | --- | --- |
| Boss HP | `BossController -> CombatResourceComponent.CurrentHp / MaxHp` | 上中 `:: Raven ::` 下方 96 段分段条 |
| Boss Shield Defense | `BossActor.CurrentShieldDefense / MaxShieldDefense` | 按 `MaxShieldDefense` 动态生成一一对应的蓝色方块；当前 Demo 为 15 格、每 5 格分组，眩晕及随后 Knockdown 起身前保持全空 |
| Boss AI 调试 | `BossActor.TempoPressure / PressureDecayMode / LastSelectedActionPool / LastSelectedActionId` | 右上开发信息，进入战斗时默认隐藏；开发调试时由 `showDebugOnStart` 开启 |
| BE | `PlayerStateMachine.Context.Resources.BetaEnergy / MaxBetaEnergy` | 左下 32 段分段条 |
| HP | `PlayerStateMachine.Context.Resources.CurrentHp / MaxHp` | 左下 72 段分段条 |
| Lock-on | `PlayerStateMachine.Context.IsLockOn / LockOnTarget` | Raven 的 `Bip001` 直接子节点显示始终通过深度测试的清晰白芯、短柔边单粒子，并限制近距离最大屏幕尺寸；取消、暂停、非战斗或目标失效时清空隐藏 |
| 战斗操作提示 | 静态文本 + 展示层 H 快捷键 | 标题为“H 键隐藏”；显示鼠标左键轻攻击、鼠标右键重攻击、鼠标中键锁定、Shift 闪避、E 防御、1 释放技能；H 仅切换卡片自身，不驱动 gameplay |

## 4. 快捷键与暂停

Demo 展示层只保留暂停与操作说明显隐快捷键，直接读取 `Keyboard.current`，不写入 `PlayerInputSnapshot`，不影响玩家状态机输入语义。原左上角对应的 `TAB + 1 / 2 / H / C` 调试功能不再响应；独立 H 只负责切换右下操作说明。

| 输入 | 行为 |
| --- | --- |
| `ESC` | 打开 / 关闭暂停菜单 |
| `H` | 仅在 Combat（含暂停覆盖）中隐藏 / 恢复右下操作说明；标题与结算阶段忽略 |

暂停菜单规则：

1. 进入暂停前取消当前 `CombatHitStop`，避免 HitStop 恢复时覆盖暂停 `Time.timeScale = 0`。
2. 暂停时显示鼠标，`Time.timeScale = 0`，战斗逻辑不继续推进。
3. 继续游戏时恢复 `Time.timeScale = 1` 和进入暂停前的 `Time.fixedDeltaTime`。
4. 菜单按钮只包含继续、重新开始和退出游戏；不提供 Boss 启停或调试信息显隐入口。

## 5. 实现边界

1. 正式 UI 首版采用运行时创建 UGUI Canvas，不使用 UI Toolkit。
2. 旧 `CombatHudOverlay` 作为 OnGUI fallback 保留，但 Demo UI 启动时默认关闭，避免重复显示资源条。
3. `EveDebugOverlay` 不并入正式 HUD，只随 Demo 的 `showDebugOnStart` 开发配置显隐；暂停菜单、旧反馈测试触发器和左上角快捷键均不提供入口。
4. Boss AI 右上角面板只读正式运行时数据，不缓存第二套压力或选招结果，不反向驱动 AI。
5. 当前只实现同一 Demo 场景内的轻量标题与结算，不扩展为独立主菜单场景；不实现设置页、存档、按键重绑定、手柄图标自动切换、背包、技能树、排行榜或完整战斗统计。
6. 中文使用项目内 `Noto Sans SC`，战斗时间数字使用 `Oxanium SemiBold`；两份字体均随 OFL 授权文件保存。背景渐变和结算页扫描线为项目原创静态 Sprite；标题页不再显示扫描线。
7. Build Settings 只启用 `Assets/Scenes/Demo.unity` 并设为索引 0；重载流程按场景名加载 Demo，不依赖无关示例场景。
8. 右下操作提示只监听独立 H 的单次按下以切换自身显隐；不响应点击、不显示按键高亮、技能冷却或可用性，也不跟随 Input System 重绑定。所有 Graphic 关闭射线命中，不新增 InputAction、配置类或独立控制器。

## 6. 验证清单

1. 进入 Demo 后只显示标题界面：Boss 未启动、玩家输入和游戏相机控制关闭、动态背景继续运行、鼠标可操作。
2. Boss HP、Boss 当前 15 格护盾、玩家 HP 与 BE 必须和唯一运行时数据源一致；Boss 护盾 UI 段数读取 `BossActor.MaxShieldDefense`，BU / SH 不创建也不显示。
3. 运行时不创建左上角 `TrainingPromptPanel`；旧 `TAB + 1 / 2 / H / C` 不触发重载、Boss 开关、调试显隐或退出，H 单独按下只允许切换右下操作说明。
4. 右上 Boss AI 面板保留但进入战斗时默认隐藏；开发者把场景 `showDebugOnStart` 改为 true 后，压力、`NORMAL / DECAY`、动作池和动作 ID 随正式运行时结果刷新。
5. `ESC` 暂停后玩家、Boss、MotionWarp 和 HitNode 不继续推进；继续后恢复。
6. 暂停菜单只创建继续、重新开始和退出三个按钮，三者均可点击；不得创建 Boss 开关或调试信息按钮。
7. 1920x1080、2560x1440 和超宽窗口下 UI 不遮挡核心战斗视野。
8. 开始切换后恢复战斗镜头、输入、Boss AI、HUD 和锁定鼠标；标题、等待结算和结算状态不能打开暂停菜单。
9. Boss / Player 死亡分别得到正确结果，同帧死亡为失败；结算必须等待死亡动画窗口，随后停止战斗与计时。
10. 再次挑战直接进入战斗，返回标题重新显示标题页；场景重载或组件禁用后不得残留暂停、HitStop、光标或输入状态。
11. 标题背景不得出现全屏淡蓝横线；战斗中锁定 Raven 时柔光点必须作为 `Bip001` 直接子节点随骨骼运动，并在身体遮挡下仍可靠可见；不得创建 Overlay 白点或固定在屏幕中心。取消锁定、暂停和退出 Combat 后立即清空隐藏。
12. 标题页点击前保持静音；点击开始进入 OpeningTransition 后 Raven Instrumental 必须立即以 0.22 音量循环播放，不得等待镜头到位。暂停/继续不重启，首次判定胜负时立即停止。
13. 进入战斗后右下角默认显示标题为“H 键隐藏”的两列三行操作卡片，六项主按键与动作名称必须完整准确；按 H 后仅隐藏该卡片，再按 H 恢复，HUD 其他区域与玩家输入不受影响。旧 `SkillWheel` 不再创建，标题与结算阶段随 HUD 隐藏且不响应 H。
