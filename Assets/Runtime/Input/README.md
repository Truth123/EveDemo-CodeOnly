# Input Runtime

本目录承载 Unity 新输入系统接入和玩家输入快照生成逻辑。状态机不应直接依赖 Unity InputAction 的瞬时状态，而应消费稳定的输入快照。

## 职责

- 读取玩家移动、视角、攻击、防御、闪避、技能、锁定等输入。
- 在进入状态机前生成当前帧输入快照。
- 保持输入采样和状态机消费的边界清晰。

## 边界

- 不在输入层判断连段、取消窗口或资源是否足够。
- 不在输入层写入动作缓存；动作缓存由 Player StateMachine 的 Buffer / Router 处理。
- 不直接驱动 Animator。

## 相关文档

- `doc/长期记忆/Input.md`
- `doc/需求变更/Input.md`
- `doc/输入系统需求设计.md`
