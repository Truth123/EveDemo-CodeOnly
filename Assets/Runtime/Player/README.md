# Player Runtime

本目录承载玩家侧运行时代码，重点是第三人称移动、输入到状态机的消费、玩家动作状态、动画桥接、玩家攻击命中和受击响应。

## 职责

- 维护玩家状态机和状态共享上下文。
- 在 Demo 标题阶段保持清空锁定的非战斗 Idle，并在开场镜头到位后一次性进入战斗 Idle、优先锁定指定 Boss。
- 将输入快照转换为 Attack / Evade / Guard / Skill 等动作请求。
- 在 Attack / Skill / Reaction Timeline 明确开放 `Cancel_MovementReturn` 时，允许对应动作后段用实时移动输入返回 Locomotion；该路径不缓存输入，优先级低于动作取消。当前 Skill1 窗口为 `3.500～6.000s`。
- 驱动玩家移动、动作位移、Root Motion 转发和 Boss 软互斥站位。
- 管理玩家武器 Hitbox、玩家受击接收和动画参数桥接。

## 主要子目录

- `StateMachine`：玩家顶层状态机、状态类、输入缓存、攻击定义和窗口消费。
- `Movement`：普通移动、动作位移、Root Motion 和 Player-Boss 站位互斥；玩家侧负责被 Boss 高优先级动作推回最低身体距离，Boss 动作位移不由玩家身体硬阻挡。
- `Combat`：玩家武器命中和玩家受击入口。
- `Animation`：玩家 Animator 参数和动作动画桥接；所有代码触发的动画混合使用固定秒数 `CrossFadeInFixedTime`。Eve 马尾由可编辑动画提供基础轮廓，再由玩家专用轻量弹簧在 `LateUpdate` 叠加重力、低惯性与数学身体代理防穿模。

## 边界

- 不在本目录直接维护 Boss AI 出招选择。
- 不在状态类中直接遍历 Timeline 原始 clips；动作状态应消费 RuntimeSpec / CapabilitySnapshot。
- 不在玩家移动层处理伤害结算，伤害仍走 Combat HitDetection / Receiver。
- 不用玩家身体碰撞阻止 Boss 攻击、后撤或突进；玩家移动层只维护站位和最低安全距离。
- GuardRelease 全程使用动画阶段 `Recovery`；AttackReset 等功能窗口只写入独立窗口事实，不得把 GuardRelease 动画阶段改为 `Reset`。
- 头发运行时只处理 Eve 的 `Ab-TL-HairB01～09`，只回写发骨旋转；不得覆盖 `EveAuthored` 曲线，也不得扩展到 Raven、战斗碰撞、武器或场景障碍。

## 相关文档

- `doc/长期记忆/Player.md`
- `doc/需求变更/Player.md`
- `doc/主角状态机汇总.md`
- `doc/主角状态机详设/*.md`
