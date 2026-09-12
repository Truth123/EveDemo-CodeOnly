// 文件说明：维护 Unity 新输入系统读取和玩家输入快照。
// 所属模块：输入系统。
// 运行影响：影响状态机输入来源、移动视角输入和动作按键采样。

namespace ProjectEVE.Input
{
    /// <summary>
    /// 玩家输入被解释后的命令类型。状态机消费的是这些语义命令，而不是原始按键。
    /// </summary>
    public enum InputCommandType
    {
        /// <summary>无有效命令。</summary>
        None = 0,
        /// <summary>轻攻击输入，用于轻攻击起手或轻攻击连招派生。</summary>
        LightAttack = 10,
        /// <summary>重攻击输入，用于重攻击起手或重攻击连招派生。</summary>
        HeavyAttack = 11,
        /// <summary>闪避输入，用于普通闪避或可取消窗口中的闪避派生。</summary>
        Evade = 20,
        /// <summary>防御输入。Guard 是 Hold 语义，通常不进入普通缓存。</summary>
        Guard = 30,
        /// <summary>第一技能输入，当前版本用于消耗 BetaEnergy 释放 Skill1。</summary>
        Skill1 = 40,
        /// <summary>第二技能输入，当前版本预留。</summary>
        Skill2 = 41,
        /// <summary>锁定输入，只切换 ControlMode，不改变顶层状态。</summary>
        LockOn = 60
    }

    /// <summary>
    /// 输入缓存槽位。不同槽位可并存，同槽位通常以后输入覆盖前输入。
    /// </summary>
    public enum InputCommandSlot
    {
        /// <summary>攻击槽：轻攻击、重攻击共用。</summary>
        Attack = 0,
        /// <summary>闪避槽：用于动作后段缓存闪避。</summary>
        Evade = 1,
        /// <summary>技能槽：Skill1 / Skill2 共用。</summary>
        Skill = 2
    }
}
