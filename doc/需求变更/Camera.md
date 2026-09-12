# Camera 需求变更

更新时间：2026-08-12

## 活跃规则

- Lock-on 柔光点属于只读反馈：锁定语义只能读取当前 `PlayerStateContext.LockOnTarget`；Demo Raven 的表现节点直接挂在 `Bip001` 下，不再由 Camera 计算或投影坐标。它不得反写正式目标点，也不得参与目标选择、自动解锁距离、相机姿态或玩家/Boss gameplay 朝向。
- Demo 标题镜头固定使用场景根 `TitleCameraAnchor` 的世界位姿 `Position=(0,2.3,5.6) / Rotation=(7,61,0)`；开始战斗后必须用 `2.4s` 可见平滑过渡到现有锁定相机位姿，不使用黑屏切换。
- 过渡终点由 `PlayerCameraRig.TryGetLockOnCameraPose()` 依据既有锁定构图参数计算；交接前调用 `SynchronizeFromCurrentPose(true)`，禁止在 Rig 启用帧重新跳回旧缓存位姿。
- 快速位移镜头跟随加入进入/退出阈值、保持时间、落后距离和最大速度限制。
- Attack 锁定限角不应造成 120 / 180 度大角度吸附。
- CameraShake 只叠加最终相机位置，不影响关注点或玩家位移。
- Lock-on 转向 V1：相机只维护观察和锁定参考，不直接写 Player / Boss 朝向；攻击承诺段和防御反应方向不能被相机锁定逻辑覆盖。

## 近期变更入口

- 2026-08-12：标题固定镜头、2.4 秒开场推镜与锁定战斗相机无跳变交接。
- 2026-06-21：相机快速位移跟随震荡修复。
- 2026-06-21：Attack 锁定限角与快速位移镜头缓冲。
- 2026-07-01：确认 Lock-on 转向职责边界。Player / Boss 朝向由状态层和动作窗口层控制，相机不驱动 gameplay 转身。

## 关联文档与代码

- `Assets/Runtime/Camera`
- `doc/战斗闭环调参与反馈计划.md`

## 遗留风险

- 镜头手感必须 Play Mode 验证，静态检查不足。
