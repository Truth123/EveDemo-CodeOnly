// 文件说明：定义 Player Skill Timeline Provider 返回的运行时动作静态数据。
// 所属模块：Combat Timeline。
// 运行影响：影响 PlayerSkill 状态进入、资源消耗、HitNode 详情读取和 RuntimeSpec 求值。

using System;

namespace ProjectEVE.Combat.Timeline
{
    /// <summary>
    /// Player Skill 状态进入时获取的动作数据。静态 HitNode 详情归属当前动作，不通过 Provider 每帧反查。
    /// </summary>
    public sealed class PlayerSkillActionData
    {
        public PlayerSkillActionData(string actionId, float skillCost, float totalDuration, CombatActionRuntimeSpec runtimeSpec, CombatHitNodeData[] hitNodes)
        {
            ActionId = actionId ?? string.Empty;
            SkillCost = Math.Max(0f, skillCost);
            TotalDuration = Math.Max(0f, totalDuration);
            RuntimeSpec = runtimeSpec ?? throw new ArgumentNullException(nameof(runtimeSpec));
            HitNodes = hitNodes ?? Array.Empty<CombatHitNodeData>();
        }

        public string ActionId { get; }
        public float SkillCost { get; }
        public float TotalDuration { get; }
        public CombatActionRuntimeSpec RuntimeSpec { get; }
        public CombatHitNodeData[] HitNodes { get; }

        public bool TryGetHitNode(string hitNodeId, out CombatHitNodeData hitNode)
        {
            for (int i = 0; i < HitNodes.Length; i++)
            {
                if (string.Equals(HitNodes[i].HitNodeId, hitNodeId, StringComparison.Ordinal))
                {
                    hitNode = HitNodes[i];
                    return true;
                }
            }

            hitNode = default;
            return false;
        }
    }
}