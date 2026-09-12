// 文件说明：按 AttackInstanceId 聚合一次 Boss 攻击的 HitNode 结果、命中压力与绝对恢复截止时间。
// 所属模块：Boss AI 数据。
// 运行影响：阻止恢复期间主动战术转换，计算正确破解奖励窗口，并限制单次攻击的正向命中加压。

using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.AI
{
    public sealed class BossAttackRecoveryTracker
    {
        private readonly Dictionary<string, CombatHitOutcome> outcomes = new Dictionary<string, CombatHitOutcome>();
        private readonly Dictionary<string, float> outcomeTimes = new Dictionary<string, float>();
        private BossAttackDefinition attack;
        private int attackInstanceId;
        private float committedHitPressure;
        private float naturalRecoveryEndTime;
        private float rewardEndTime;

        public bool IsTracking => attack != null && attackInstanceId > 0;
        public float NaturalRecoveryEndTime => naturalRecoveryEndTime;
        public float RewardEndTime => rewardEndTime;
        public float FinalRecoveryEndTime => Mathf.Max(naturalRecoveryEndTime, rewardEndTime);
        public bool IsLocked(float now) => IsTracking && now < FinalRecoveryEndTime;

        /// <summary>
        /// 开始追踪一个正式攻击实例，并清空上一攻击的命中聚合与防御奖励结果。
        /// </summary>
        /// <param name="definition">本次攻击的 HitNode、自然后摇和防御奖励定义。</param>
        /// <param name="newAttackInstanceId">由 BossActionRunner 为本次 Attack 创建的正整数实例 ID。</param>
        /// <param name="attackStartTime">本次攻击开始的 Unity 运行时间，单位为秒。</param>
        public void Begin(BossAttackDefinition definition, int newAttackInstanceId, float attackStartTime)
        {
            attack = definition;
            attackInstanceId = Mathf.Max(0, newAttackInstanceId);
            committedHitPressure = 0f;
            outcomes.Clear();
            outcomeTimes.Clear();
            rewardEndTime = 0f;
            naturalRecoveryEndTime = definition == null
                ? attackStartTime
                : attackStartTime + definition.LastHitNodeEndTime + definition.NaturalRecoveryDuration;
        }

        /// <summary>
        /// 记录当前攻击实例的一次 HitNode 结果，并返回本次应即时补交的正向命中压力。
        /// </summary>
        /// <param name="sourceAttackInstanceId">命中结果携带的攻击实例 ID。</param>
        /// <param name="hitNodeId">产生正式结果的 HitNode ID。</param>
        /// <param name="outcome">玩家收到的正式战斗结果。</param>
        /// <param name="outcomeTime">结果发生或对应防御动作开始的 Unity 运行时间，单位为秒。</param>
        /// <param name="pressureIncrease">写回本次应增加的压力；普通受击最高累计 4，击倒或死亡最高累计 10。</param>
        /// <returns>true 表示结果属于当前攻击并已记录；false 表示实例不匹配或输入无效，调用方不得应用压力或防御奖励。</returns>
        public bool TryRecordOutcome(
            int sourceAttackInstanceId,
            string hitNodeId,
            CombatHitOutcome outcome,
            float outcomeTime,
            out float pressureIncrease)
        {
            pressureIncrease = 0f;
            if (!IsTracking ||
                sourceAttackInstanceId != attackInstanceId ||
                string.IsNullOrEmpty(hitNodeId) ||
                outcome == CombatHitOutcome.None)
            {
                return false;
            }

            outcomes[hitNodeId] = outcome;
            outcomeTimes[hitNodeId] = outcomeTime;
            float targetHitPressure = ResolveHitPressureTarget(outcome);
            pressureIncrease = Mathf.Max(0f, targetHitPressure - committedHitPressure);
            committedHitPressure = Mathf.Max(committedHitPressure, targetHitPressure);    // 保证一次攻击无论包含多少个 HitNode：普通受击最多累计增加 4, 击倒或死亡最多累计增加 10, 防止多段攻击重复增加压力
            EvaluateRewardRules();
            return true;
        }

        public bool TryGetHitNode(string hitNodeId, out CombatHitNodeData hitNode)
        {
            if (attack?.HitNodes != null)
            {
                for (int i = 0; i < attack.HitNodes.Length; i++)
                {
                    if (string.Equals(attack.HitNodes[i].HitNodeId, hitNodeId, StringComparison.Ordinal))
                    {
                        hitNode = attack.HitNodes[i];
                        return true;
                    }
                }
            }

            hitNode = default;
            return false;
        }

        /// <summary>返回完整多段 PerfectGuard 对压力的额外减值；不包含逐节点 −3。</summary>
        public float ResolveCompleteGuardPressureReduction()
        {
            if (attack?.HitNodes == null || attack.HitNodes.Length <= 1)
            {
                return 0f;
            }

            bool redCombo = true;
            for (int i = 0; i < attack.HitNodes.Length; i++)
            {
                CombatHitNodeData node = attack.HitNodes[i];
                if (!outcomes.TryGetValue(node.HitNodeId, out CombatHitOutcome outcome) || outcome != CombatHitOutcome.PerfectGuard)
                {
                    return 0f;
                }

                redCombo &= !node.CanBeGuarded && node.CanBePerfectGuarded;
            }

            return redCombo ? 6f : 4f;
        }

        public void Clear()
        {
            attack = null;
            attackInstanceId = 0;
            committedHitPressure = 0f;
            outcomes.Clear();
            outcomeTimes.Clear();
            naturalRecoveryEndTime = 0f;
            rewardEndTime = 0f;
        }

        /// <summary>
        /// 把一次命中结果映射为该攻击应达到的正向压力累计上限。
        /// </summary>
        /// <param name="outcome">当前正式战斗结果。</param>
        /// <returns>HitReaction 返回 4，Knockdown 或 Dead 返回 10，其他结果返回 0。</returns>
        private static float ResolveHitPressureTarget(CombatHitOutcome outcome)
        {
            return outcome switch
            {
                CombatHitOutcome.HitReaction => 4f,
                CombatHitOutcome.Knockdown => 10f,
                CombatHitOutcome.Dead => 10f,
                _ => 0f
            };
        }

        /// <summary>
        /// 将当前攻击多个hitnode的命中结果和预先定义的奖励条件进行匹配，匹配成功后提供破解奖励窗口时间，也就是增加后摇时长
        /// </summary>
        private void EvaluateRewardRules()
        {
            BossDefenseRewardRule[] rules = attack.DefenseRewardRules;
            for (int ruleIndex = 0; ruleIndex < rules.Length; ruleIndex++)
            {
                BossDefenseRewardRule rule = rules[ruleIndex];
                if (rule.Conditions == null || rule.Conditions.Length == 0)
                {
                    continue;
                }

                float triggerTime = 0f;
                CombatHitOutcome triggerOutcome = CombatHitOutcome.None;
                bool matches = true;
                for (int conditionIndex = 0; conditionIndex < rule.Conditions.Length; conditionIndex++)
                {
                    BossDefenseRewardCondition condition = rule.Conditions[conditionIndex];
                    if (!outcomes.TryGetValue(condition.HitNodeId, out CombatHitOutcome outcome) ||
                        !condition.Matches(condition.HitNodeId, outcome) ||
                        !outcomeTimes.TryGetValue(condition.HitNodeId, out float time))
                    {
                        matches = false;
                        break;
                    }

                    if (time >= triggerTime)
                    {
                        triggerTime = time;
                        triggerOutcome = outcome;
                    }
                }

                if (!matches)
                {
                    continue;
                }

                float transitionDuration = triggerOutcome == CombatHitOutcome.PerfectEvade ? 1f : 0.26f;
                rewardEndTime = Mathf.Max(rewardEndTime, triggerTime + transitionDuration + Mathf.Max(0f, rule.RewardDuration));
            }
        }
    }
}
