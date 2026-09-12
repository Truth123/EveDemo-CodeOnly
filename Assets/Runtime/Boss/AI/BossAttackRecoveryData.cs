// 文件说明：定义 Boss 招式的自然后摇与正确破解奖励数据。
// 所属模块：Boss AI 数据。
// 运行影响：决定恢复锁截止时间和奖励规则匹配。

using ProjectEVE.Combat;
using System;
using UnityEngine;

namespace ProjectEVE.Boss.AI
{
    [Flags]
    public enum BossDefenseOutcomeMask
    {
        None = 0,
        PerfectGuard = 1 << 0,
        PerfectEvade = 1 << 1
    }

    [Serializable]
    public struct BossDefenseRewardCondition
    {
        public string HitNodeId;
        public BossDefenseOutcomeMask AllowedOutcomes;

        public bool Matches(string hitNodeId, CombatHitOutcome outcome)
        {
            if (!string.Equals(HitNodeId, hitNodeId, StringComparison.Ordinal))
            {
                return false;
            }

            return outcome switch
            {
                CombatHitOutcome.PerfectGuard => (AllowedOutcomes & BossDefenseOutcomeMask.PerfectGuard) != 0,
                CombatHitOutcome.PerfectEvade => (AllowedOutcomes & BossDefenseOutcomeMask.PerfectEvade) != 0,
                _ => false
            };
        }
    }

    [Serializable]
    public struct BossDefenseRewardRule
    {
        public string Name;
        [Min(0f)] public float RewardDuration;
        public BossDefenseRewardCondition[] Conditions;
    }
}
