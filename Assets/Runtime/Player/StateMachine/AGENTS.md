# Player StateMachine AGENTS.md

本文件约束 `Assets/Runtime/Player/StateMachine` 下的玩家状态机代码。根目录 `AGENTS.md` 仍然适用。

## 状态结构一致性

- 相似状态必须保持相似代码骨架、命名和数据流。
- 基于 Timeline 的状态优先使用统一结构：进入时获取 Provider Data；每帧 `RuntimeSpec.Evaluate(elapsed)`；状态本地 `EvaluateFrame(...)` 转 FrameData；写入 `PlayerStateContext`；处理输入、取消、返回和退出。
- Attack、Skill、Evade、Guard 即使业务不同，也应保持相近组织方式。业务差异放在状态本地解释和状态切换规则中。
- 不为了复用强行合并成万能状态，也不允许每个状态完全按新功能重写一套风格。

## FrameData 规则

- 状态本地 FrameData 只保存本状态本帧真正需要的业务字段。
- FrameData 不复制完整静态数据；复杂静态数据从 Provider Data / Definition 获取。
- FrameData 优先作为状态类内部私有结构，只有多个状态稳定复用且语义一致时才提取公共类型。

## 新增代码规则

- 新增状态、helper 或数据结构前，必须先检查 Attack / Skill / Evade / Guard 是否已有相似生命周期和命名。
- 如果新类型只是复制已有字段、转发查询或改名，禁止新增。
- 新增公共基类、接口或 helper 必须显著减少重复逻辑，并且不让状态阅读路径变得更绕。

## Context 规则

- `PlayerStateContext` 保存跨状态共享运行时状态；不要把单一状态私有临时变量塞入 Context。
- 修改 Context 字段时必须检查相邻状态读写路径，尤其是 Attack、Skill、Evade、Guard、Reaction。
- 状态退出时必须清理自己写入的临时 Context 字段，避免影响下一个状态。
