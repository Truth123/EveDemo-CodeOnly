# Combat AGENTS.md

本文件约束 `Assets/Runtime/Combat` 下的通用战斗、命中、伤害和防御解析代码。根目录 `AGENTS.md` 仍然适用。

## 通用战斗数据

- Combat 层的数据结构应表达 Player 与 Boss 都能理解的战斗语义。
- 不为 Player/Boss 分别复制字段相同的命中、伤害、防御或反应数据类。
- 如果差异只是消费者不同，优先由消费者解释同一个通用数据结构。
- 只有字段结构、校验规则或生命周期不同，才新增独立数据类型。

## Hit / Receiver 边界

- Hitbox / Hurtbox 负责空间检测和目标定位，不保存完整动作语义。
- CombatHitResolver 负责防御、无敌、霸体、受击结果等解析规则，不反查 Timeline 资产。
- Receiver 负责把解析结果应用到角色状态、HP 和反馈，不拥有攻击窗口数据。
- Timeline 数据进入 Combat 层前应已经转换为 runtime data 或 Context 字段。

## 复用规则

- 新增命中或防御能力前，先检查 `CombatHitData`、`CombatHitResult`、`CombatHitResolver` 和 Timeline capability 是否已有表达方式。
- 不新增只转发、只改名、只复制字段的中间结构。
- 若新增结构表达 Player 与 Boss 都需要理解的通用战斗语义，应放在 Combat 层或其明确子模块。
- 若新增结构只是某个状态为解释当前帧 Snapshot 而整理的业务视图，应留在该状态本地。不要仅因为多个消费者读取同一数据，就自动新增公共 DTO；先判断它是否真的是通用语义。