// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "PlayerSkillTimeline", menuName = "Project EVE/Combat/Player Skill Timeline")]
    public sealed class PlayerSkillTimelineAsset : CombatTimelineActionAsset
    {
        [SerializeField] private float skillCost = 8f;

        public float SkillCost => skillCost;


        public void ConfigureSkill(float nextSkillCost)
        {
            skillCost = Mathf.Max(0f, nextSkillCost);
        }

        /// <summary>
        /// 将当前对象转换为 Player Skill 状态进入时使用的动作数据。
        /// </summary>
        public PlayerSkillActionData ToSkillActionData()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            CombatHitNodeData[] hitNodes = runtimeSpec.ToHitNodeData();
            return new PlayerSkillActionData(ActionId, skillCost, TotalDuration, runtimeSpec, hitNodes);
        }
    }
}
