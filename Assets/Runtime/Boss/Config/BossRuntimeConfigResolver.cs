// 文件说明：集中 Boss Actor 运行期配置读取入口。
// 所属模块：Boss 配置。
// 运行影响：只读取 Boss Reaction Timeline，不驱动动画、位移、命中或状态切换。

using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Boss.Config
{
    /// <summary>
    /// Boss 运行期配置解析器。用于收敛 BossActor 中对 CombatTimelineProvider 的直接读取。
    /// </summary>
    public sealed class BossRuntimeConfigResolver
    {
        public const string BossHitStaggerTimelineId = "Boss_HitStagger";
        public const string BossKnockdownTimelineId = "Boss_Knockdown";

        private bool missingBossHitStaggerTimelineLogged;
        private bool missingBossKnockdownTimelineLogged;

        /// <summary>读取 Boss 站立硬直 Timeline 配置；缺失时按旧行为只记录一次错误。</summary>
        public bool TryGetHitStaggerConfig(out CombatTimelineReactionConfig config)
        {
            if (CombatTimelineProvider.TryGetBossReaction(BossHitStaggerTimelineId, out config))
            {
                return true;
            }

            if (!missingBossHitStaggerTimelineLogged)
            {
                missingBossHitStaggerTimelineLogged = true;
                Debug.LogError($"Combat Timeline reaction '{BossHitStaggerTimelineId}' is missing. Boss hit stagger will be refused.");
            }

            return false;
        }

        /// <summary>读取 Boss 击倒 Timeline 配置；缺失时按旧行为只记录一次错误。</summary>
        public bool TryGetKnockdownConfig(out CombatTimelineReactionConfig config)
        {
            if (CombatTimelineProvider.TryGetBossReaction(BossKnockdownTimelineId, out config))
            {
                return true;
            }

            if (!missingBossKnockdownTimelineLogged)
            {
                missingBossKnockdownTimelineLogged = true;
                Debug.LogError($"Combat Timeline reaction '{BossKnockdownTimelineId}' is missing. Boss knockdown will be refused.");
            }

            return false;
        }
    }
}
