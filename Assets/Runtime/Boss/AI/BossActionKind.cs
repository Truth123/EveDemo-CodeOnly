// 文件说明：定义 Boss Action 的高层行为类别。
// 所属模块：Boss AI。
// 运行影响：只用于动作池过滤和执行路由，不直接驱动动画、位移或命中。

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Boss Action 的高层行为类别。Attack 与 Reposition 已有正式执行路径，其他类别仍为扩展入口。
    /// </summary>
    public enum BossActionKind
    {
        /// <summary>常规攻击，走 BossActionRunner / BossCombatSystem。</summary>
        Attack = 0,
        /// <summary>带特殊提示的攻击，后续可接黄光、蓝光等表现和规则。</summary>
        SpecialCueAttack = 1,
        /// <summary>抓取动作，后续接抓取判定和抓取表现。</summary>
        Grab = 2,
        /// <summary>处决动作，后续接处决条件和演出。</summary>
        Execution = 3,
        /// <summary>场景演示动作，后续接 Timeline / Cinemachine。</summary>
        Cinematic = 4,
        /// <summary>不创建攻击实例，由 ActionRunner 驱动动画和 MotionProfile 的重定位动作。</summary>
        Reposition = 5,
        /// <summary>反应动作，当前仍由 BossReactionSystem 处理。</summary>
        Reaction = 6
    }
}
