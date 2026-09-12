# Input 需求变更

更新时间：2026-06-23

## 活跃规则

- 输入进入状态机前形成快照。
- 动作输入响应集中到 `PlayerActionInputRouter`。
- Attack / Evade / Skill 支持缓存；Guard 不进入普通缓存。
- Skill 取消窗口统一检查资源。

## 近期变更入口

- 2026-06-07：第 7 阶段统一输入响应与 Evade.PerfectEvade 动画模式修正。
- 2026-06-02：Attack 缓存、Combo 与 Cancel 窗口规则调整。

## 关联文档与代码

- `doc/输入系统需求设计.md`
- `Assets/Runtime/Input`
- `Assets/Runtime/Player/StateMachine/Buffers`

## 遗留风险

- 输入缓存和状态切换互相影响，修改后必须回归取消窗口和 Animator 参数。
