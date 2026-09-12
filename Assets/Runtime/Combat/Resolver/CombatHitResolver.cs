// 文件说明：维护通用命中解析规则。
// 所属模块：战斗结算。
// 运行影响：影响防御、闪避、伤害、削韧、死亡等战斗结果。

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 战斗命中解析器。集中实现需求文档中的受击优先级，避免各状态重复判断命中结果。
    /// </summary>
    public static class CombatHitResolver
    {
        /// <summary>
        /// 按非法打点、既有死亡、PerfectGuard、PerfectEvade、闪避无敌、普通防御、致死伤害和作者反应意图的顺序解析命中。
        /// </summary>
        /// <param name="hit">攻击方提供的伤害、防御许可与反应意图数据。</param>
        /// <param name="defender">受击方当前 HP、防御、闪避及无敌窗口状态。</param>
        /// <returns>本次命中的最终结果，以及应应用的 HP 和底层 GuardDamage 数值。</returns>
        public static CombatHitResult Resolve(in CombatHitData hit, in DefenderCombatState defender)
        {
            if (hit.ReactionIntent == CombatReactionIntent.None)
            {
                return new CombatHitResult(CombatHitOutcome.None, 0f, 0f);
            }

            // 已经归零的角色保持 Dead；本次攻击是否致死必须在防御和无敌判定之后决定。
            if (defender.CurrentHp <= 0f)
            {
                return new CombatHitResult(CombatHitOutcome.Dead, hit.Damage, 0f);
            }

            // Guard 状态下先判定 PerfectGuard，再判定普通防御。
            if (defender.IsGuarding && defender.IsPerfectGuardWindow && hit.CanBePerfectGuarded)
            {
                return new CombatHitResult(CombatHitOutcome.PerfectGuard, 0f, 0f);
            }

            // PerfectEvade 支持两条路径：无敌帧拦截命中，或外圈 NearMiss 候选。
            if (defender.IsEvading && defender.IsPerfectEvadeWindow && hit.CanBePerfectEvaded)
            {
                if (defender.IsEvadeInvincible || defender.HasNearMissCandidate)
                {
                    return new CombatHitResult(CombatHitOutcome.PerfectEvade, 0f, 0f);
                }
            }

            // 普通闪避无敌只忽略伤害，不触发完美闪避奖励。
            if (defender.IsEvading && defender.IsEvadeInvincible)
            {
                return new CombatHitResult(CombatHitOutcome.IgnoredByInvincible, 0f, 0f);
            }

            // 普通防御完全阻止 HP 伤害；GuardDamage 只保留为底层命中结果数据和 GuardHit 反馈语义。
            if (defender.IsGuarding && defender.IsGuardBlockActive && hit.CanBeGuarded)
            {
                return new CombatHitResult(CombatHitOutcome.GuardHit, 0f, hit.GuardDamage);
            }

            if (hit.Damage >= defender.CurrentHp)
            {
                return new CombatHitResult(CombatHitOutcome.Dead, hit.Damage, 0f);
            }

            switch (hit.ReactionIntent)
            {
                case CombatReactionIntent.HitReaction:
                    return new CombatHitResult(CombatHitOutcome.HitReaction, hit.Damage, 0f);
                case CombatReactionIntent.Knockdown:
                    return new CombatHitResult(CombatHitOutcome.Knockdown, hit.Damage, 0f);
                case CombatReactionIntent.DamageOnly:
                    return new CombatHitResult(CombatHitOutcome.DamageOnly, hit.Damage, 0f);
                case CombatReactionIntent.None:
                default:
                    return new CombatHitResult(CombatHitOutcome.None, 0f, 0f);
            }
        }
    }
}
