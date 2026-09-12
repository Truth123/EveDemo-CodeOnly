// 文件说明：定义 Boss 状态和招式数据。
// 所属模块：Boss AI 数据。
// 运行影响：影响 Boss 出招选择、HitNode 语义和调试数据来源。

using ProjectEVE.Combat.Timeline;
using System;

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Boss 一整招的数据定义。它负责动画、出招选择、冷却和时间轴，不直接执行命中结算。
    /// </summary>
    [Serializable]
    public sealed class BossAttackDefinition
    {
        /// <summary>招式唯一 ID，例如 Raven_Slash。</summary>
        public string AttackId;
        /// <summary>调试显示名称。</summary>
        public string DisplayName;
        /// <summary>Raven.controller 中要 CrossFade 的动画状态名。</summary>
        public string AnimationStateName;
        /// <summary>出招前允许的面向目标夹角。</summary>
        public float AngleLimit;
        /// <summary>招式冷却时间。</summary>
        public float Cooldown;
        /// <summary>招式总持续时间。</summary>
        public float TotalDuration;
        /// <summary>最后一个 HitNode 结束后至少维持的自然后摇时间。</summary>
        public float NaturalRecoveryDuration;
        /// <summary>一招内的命中节点。Boss 和 Player 共用唯一运行时 HitNode 数据结构。</summary>
        public CombatHitNodeData[] HitNodes;
        /// <summary>Combat Timeline 归一化运行时规格，BossAttack 通过它读取 HitNode、位移引用和 Boss 受击门控等能力。</summary>
        public CombatActionRuntimeSpec RuntimeSpec;
        /// <summary>正确破解组合与对应奖励时长。</summary>
        public BossDefenseRewardRule[] DefenseRewardRules;

        /// <summary>最后一个 HitNode 的结束时间；无 HitNode 时返回动作总时长。</summary>
        public float LastHitNodeEndTime
        {
            get
            {
                float endTime = 0f;
                for (int i = 0; i < HitNodes.Length; i++)
                {
                    endTime = Math.Max(endTime, HitNodes[i].Window.EndTime);
                }

                return endTime > 0f ? endTime : TotalDuration;
            }
        }

        /// <summary>
        /// 创建 BossAttackDefinition 实例，并准备 Boss AI 数据 模块需要的初始状态。
        /// </summary>
        public BossAttackDefinition(
            string attackId,
            string displayName,
            string animationStateName,
            float angleLimit,
            float cooldown,
            float totalDuration,
            float naturalRecoveryDuration,
            CombatHitNodeData[] hitNodes,
            CombatActionRuntimeSpec runtimeSpec = null,
            BossDefenseRewardRule[] defenseRewardRules = null)
        {
            AttackId = attackId;
            DisplayName = displayName;
            AnimationStateName = animationStateName;
            AngleLimit = angleLimit;
            Cooldown = cooldown;
            TotalDuration = totalDuration;
            NaturalRecoveryDuration = naturalRecoveryDuration;
            HitNodes = hitNodes ?? Array.Empty<CombatHitNodeData>();
            RuntimeSpec = runtimeSpec ?? throw new ArgumentNullException(nameof(runtimeSpec));
            DefenseRewardRules = defenseRewardRules ?? Array.Empty<BossDefenseRewardRule>();
        }
    }
}
