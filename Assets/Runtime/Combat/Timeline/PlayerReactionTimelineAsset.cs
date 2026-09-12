// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "PlayerReactionTimeline", menuName = "Project EVE/Combat/Player Reaction Timeline")]
    public sealed class PlayerReactionTimelineAsset : CombatReactionTimelineAsset
    {
    }
}
