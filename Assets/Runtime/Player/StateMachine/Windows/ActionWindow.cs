// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using System;

namespace ProjectEVE.Player.Windows
{
    /// <summary>
    /// 动作时间窗，表达连招、闪避取消、技能取消、防御取消和重置区间等规则。
    /// </summary>
    [Serializable]
    public struct ActionWindow
    {
        /// <summary>窗口名称，主要用于调试显示和配置识别。</summary>
        public string Name;
        /// <summary>窗口开始时间，相对于当前状态或动作节点的进入时间。</summary>
        public float StartTime;
        /// <summary>窗口结束时间，相对于当前状态或动作节点的进入时间。</summary>
        public float EndTime;

        /// <summary>
        /// 创建一个动作时间窗。
        /// </summary>
        public ActionWindow(string name, float startTime, float endTime)
        {
            Name = name;
            StartTime = startTime;
            EndTime = endTime;
        }

        /// <summary>
        /// 执行 Disabled 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public static ActionWindow Disabled(string name)
        {
            return new ActionWindow(name, float.PositiveInfinity, float.NegativeInfinity);
        }

        /// <summary>
        /// 判断某个状态经过时间是否落在窗口内。
        /// </summary>
        public bool Contains(float elapsedTime)
        {
            return elapsedTime >= StartTime && elapsedTime <= EndTime;
        }
    }
}
