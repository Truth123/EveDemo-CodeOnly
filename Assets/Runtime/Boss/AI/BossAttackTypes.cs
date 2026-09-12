// 文件说明：定义 Boss 状态、招式数据和 Raven 招式目录。
// 所属模块：Boss AI 数据。
// 运行影响：影响 Boss 出招选择、HitNode 语义和调试数据来源。

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Boss 攻击命中来源部位。部位只描述检测体来源，最终结算语义仍由 HitNode 决定。
    /// </summary>
    public enum BossAttackSourcePart
    {
        None = 0,
        Weapon = 1,
        LeftHand = 2,
        RightHand = 3,
        LeftFoot = 4,
        RightFoot = 5,
        Body = 6,
        Grab = 7,
        /// <summary>脱离 Boss 骨骼独立运动或驻留的投射物/区域判定。</summary>
        Detached = 8
    }

    /// <summary>
    /// Boss 攻击期间的位移来源。RootMotionWarped 用动画位移做表现，CodeDrivenWarped 用代码推进到攻击落点。
    /// </summary>
    public enum BossAttackMotionMode
    {
        None = 0,
        RootMotionWarped = 1,
        CodeDrivenWarped = 2
    }

}
