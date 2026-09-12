// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player.Attacks
{
    /// <summary>
    /// 普通攻击输入类型。用于区分轻攻击、重攻击以及轻重派生规则。
    /// </summary>
    public enum AttackInputType
    {
        /// <summary>没有攻击输入。</summary>
        None = 0,
        /// <summary>轻攻击输入。</summary>
        Light = 1,
        /// <summary>重攻击输入。</summary>
        Heavy = 2
    }

    /// <summary>
    /// 当前攻击判定结果。第 3 阶段先提供窗口主干，真实命中由后续 Hitbox/Hurtbox 阶段写入。
    /// </summary>
    public enum AttackContactResult
    {
        /// <summary>尚未进入命中结算。</summary>
        None = 0,
        /// <summary>攻击命中目标。</summary>
        OnHit = 1,
        /// <summary>攻击判定结束但没有命中。</summary>
        OnWhiff = 2
    }
}
