# Combat Runtime

本目录承载通用战斗运行时代码，负责阵营、伤害数据、命中解析、资源组件、反馈事件和 Combat Timeline 运行时数据入口。

## 职责

- 定义 `CombatHitData`、`CombatHitResult`、`CombatAttackType`、`CombatReactionIntent`、`CombatHitOutcome` 等通用战斗数据。
- 通过 `CombatHitResolver` 解析防御、完美防御、闪避、伤害、削韧和死亡结果。
- 提供 `CombatResourceComponent` 维护 HP / BetaEnergy 等资源。
- 提供 `CombatHurtbox` 作为通用受击盒入口。
- 提供 Combat Timeline 资产查询、校验和运行时转换。

## 主要子目录

- `HitDetection`：Hurtbox 和通用命中检测入口。
- `Timeline`：Combat Timeline ActionAsset、clip 类型、Provider 和 Validation。

## 边界

- 不直接控制 Player 或 Boss 状态机。
- 不负责 Animator 播放。
- 不负责具体 VFX / SFX 播放，只发出反馈事件或返回命中结果。
- `CombatAttackType` 只表达攻击来源通道；击倒、只扣血等反应语义必须由 `CombatReactionIntent` 显式配置，并由 `CombatHitResolver` 输出 `CombatHitOutcome`。

## 相关文档

- `doc/长期记忆/Player.md`
- `doc/长期记忆/Boss.md`
- `doc/长期记忆/CombatTimeline.md`
- `doc/需求变更/Player.md`
- `doc/需求变更/Boss.md`
- `doc/需求变更/CombatTimeline.md`
