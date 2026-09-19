// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using System;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "CombatTimelineCatalog", menuName = "Project EVE/Combat/Timeline Catalog")]
    public sealed class CombatTimelineCatalog : ScriptableObject
    {
        [SerializeField] private string catalogId;
        [SerializeField] private CombatTimelineOwner owner;
        [SerializeField] private CombatTimelineActionAsset[] timelines = Array.Empty<CombatTimelineActionAsset>();

        public string CatalogId => catalogId;
        public CombatTimelineOwner Owner => owner;
        public CombatTimelineActionAsset[] Timelines => timelines;


        public void Configure(string nextCatalogId, CombatTimelineOwner nextOwner, CombatTimelineActionAsset[] nextTimelines)
        {
            catalogId = nextCatalogId;
            owner = nextOwner;
            timelines = nextTimelines ?? Array.Empty<CombatTimelineActionAsset>();
        }

        /// <summary>
        /// 尝试执行 Get / Timeline，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        public bool TryGetTimeline(string actionId, out CombatTimelineActionAsset timeline)
        {
            if (!string.IsNullOrEmpty(actionId) && timelines != null)
            {
                for (int i = 0; i < timelines.Length; i++)
                {
                    CombatTimelineActionAsset candidate = timelines[i];
                    if (candidate != null && candidate.ActionId == actionId)
                    {
                        timeline = candidate;
                        return true;
                    }
                }
            }

            timeline = null;
            return false;
        }
    }
}
