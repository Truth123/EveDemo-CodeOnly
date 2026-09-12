// 文件说明：统一解析 Player Skill1 Timeline 动作数据，避免相邻状态依赖 Skill 状态静态字段。
// 所属模块：玩家状态机。
// 运行影响：影响进入 Skill 前的资源检查、输入消费和缺 Timeline 报错路径。

using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Player
{
    /// <summary>
    /// Player Skill 动作数据解析入口。当前 Demo 只实现 Skill1。
    /// </summary>
    public static class PlayerSkillActionResolver
    {
        public const string Skill1ActionId = "Skill1";

        private static bool missingSkillTimelineLogged;

        /// <summary>
        /// 获取 Skill1 的 Timeline Provider Data；缺失时只记录一次错误并拒绝动作。
        /// </summary>
        public static bool TryGetSkill1(out PlayerSkillActionData data)
        {
            if (CombatTimelineProvider.TryGetPlayerSkill(Skill1ActionId, out data))
            {
                return true;
            }

            if (!missingSkillTimelineLogged)
            {
                missingSkillTimelineLogged = true;
                Debug.LogError($"Combat Timeline action '{Skill1ActionId}' is missing. Skill actions will be rejected.");
            }

            return false;
        }
    }
}