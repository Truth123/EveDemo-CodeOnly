// 文件说明：定义 Boss 状态、招式数据和 Raven 招式目录。
// 所属模块：Boss AI 数据。
// 运行影响：影响 Boss 出招选择、HitNode 语义和调试数据来源。

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Boss 当前运行时状态，是状态机、Actor、Blackboard 和 Brain 的唯一状态事实源。
    /// </summary>
    public enum BossStateId
    {
        /// <summary>未初始化或无效状态。</summary>
        None = 0,
        /// <summary>战斗待机，等待评估下一步行为。</summary>
        Idle = 1,
        /// <summary>距离过远时靠近玩家。</summary>
        Approach = 2,
        /// <summary>近中距离试探移动。</summary>
        Strafe = 3,
        /// <summary>执行当前 Boss Action；具体战斗副作用由 BossActionKind 决定。</summary>
        Action = 5,
        /// <summary>招式结束硬直。</summary>
        Recovery = 6,
        /// <summary>受击轻硬直入口，第一版只预留。</summary>
        HitStagger = 7,
        /// <summary>玩家 Skill 或指定强反制导致的倒地流程。</summary>
        Knockdown = 8,
        /// <summary>Boss HP 归零。</summary>
        Dead = 9,
        /// <summary>防御护盾归零后的独立长眩晕。</summary>
        ShieldBreakStun = 10
    }

    /// <summary>
    /// Boss 受击动画方向。方向以 Boss 自身朝向为基准，表示攻击来源所在象限。
    /// </summary>
    public enum BossHitDirectionId
    {
        None = 0,
        Front = 1,
        Back = 2,
        Left = 3,
        Right = 4
    }
}
