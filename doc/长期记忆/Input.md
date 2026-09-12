# Input 长期记忆

更新时间：2026-06-23

## 当前状态

- 项目已切换到 Unity 新输入系统。
- 输入在进入状态机前形成快照。
- 攻击、闪避、技能支持缓存；Guard 保持即时 Hold 语义。
- `PlayerActionInputRouter` 集中处理即时输入、缓存写入、缓存消费和资源检查。

## 关键规则

- Light / Heavy 同帧时优先 Light，避免误触重攻击。
- Attack / Evade / Skill 缓存有生命周期和动作结束时间上限。
- Guard 不进入普通缓存，只在合法窗口读取 `GuardPressed || GuardHeld`。
- Skill 消费统一检查资源，不允许资源不足时通过取消窗口进入 Skill。

## 主要代码入口

- `Assets/Runtime/Input`
- `Assets/Runtime/Player/StateMachine/Buffers/PlayerActionInputRouter.cs`
- `Assets/Runtime/Player/StateMachine/Buffers/InputCommandBuffer.cs`

## 下一步入口

- 后续迁移 RuntimeSpec 时，输入窗口应读取能力快照，而不是让状态类直接遍历 Timeline clips。

## 已知风险

- 输入缓存、状态切换和 Animator 参数高联动，修改后必须回归相邻状态和取消窗口。
