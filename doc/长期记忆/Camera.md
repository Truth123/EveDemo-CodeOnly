# Camera 长期记忆

更新时间：2026-08-12

## 当前状态

- Lock-on 视觉提示由 Demo UI 只读 `PlayerStateContext.LockOnTarget`，但 Raven 提示本体已是 `Bip001` 的世界空间粒子子节点，不再由相机投影或保存屏幕坐标。Camera/LockOnController 不保存第二份目标状态；取消锁定和目标失效会随上下文立即隐藏。
- Demo 标题镜头使用场景根节点 `TitleCameraAnchor` 的固定世界位姿 `Position=(0,2.3,5.6) / Rotation=(7,61,0)`；点击开始后以 `2.4s` SmoothStep 插值到 `PlayerCameraRig` 根据 Boss 锁定目标计算的战斗位姿。
- 开场过渡期间 `PlayerCameraRig` 保持关闭；到位后先同步 Rig 的内部 yaw、pitch、pivot 与速度缓存，再启用控制，避免交接帧跳变。
- 相机支持自由视角、锁定视角和基础战斗跟随。
- 快速位移动作后的关注点跟随已增加缓冲、退出滞后和速度上限，降低闪避 / Skill 后镜头猛追。
- Attack 锁定限角和快速位移镜头缓冲已接入。
- Lock-on 转向 V1 中，相机只提供观察和锁定目标参考，不直接驱动 Player / Boss gameplay 朝向；Player 和 Boss 的朝向由各自状态层与动作窗口层控制。

## 关键规则

- 标题镜头锚点不得挂在玩家或 Boss 的运行时移动层级下；标题构图必须使用稳定世界坐标。
- 开场镜头未到位前不得提前启用 PlayerCameraRig、玩家锁定或 Boss AI。
- CameraShake 只叠加最终相机位置，不影响关注点、锁定目标或玩家位移。
- 锁定相机不等于全状态强制面向；相机目标变化不得覆盖攻击、闪避、防御受击或 Boss 攻击承诺段的朝向规则。
- 快速位移跟随由进入阈值、退出阈值、保持时间、落后距离和最大速度共同控制。
- 相机手感参数属于 Play Mode 调参项，修改时需要回归 Evade、PerfectEvade、Skill 和 Attack 连段。

## 主要代码入口

- `Assets/Runtime/Camera`

## 下一步入口

- Play Mode 验证自由视角 Evade / PerfectEvade / Skill 快速位移结束时是否仍有猛追感。
- 根据手感微调快速跟随阈值和速度上限。

## 已知风险

- 相机参数对动作位移速度和锁定目标距离敏感，不能只静态检查。
