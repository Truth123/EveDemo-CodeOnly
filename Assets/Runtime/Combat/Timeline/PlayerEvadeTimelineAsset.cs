// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "PlayerEvadeTimeline", menuName = "Project EVE/Combat/Player Evade Timeline")]
    public sealed class PlayerEvadeTimelineAsset : CombatTimelineActionAsset
    {
        /// <summary>
        /// 将当前对象转换为 Player Evade 状态进入时使用的动作数据。
        /// </summary>
        public PlayerEvadeActionData ToEvadeActionData()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            return new PlayerEvadeActionData(ActionId, TotalDuration, runtimeSpec);
        }
    }
}
