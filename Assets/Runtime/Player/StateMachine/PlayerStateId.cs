// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player
{
    /// <summary>
    /// 玩家顶层状态。数值按大致优先级留出间隔，方便后续插入新状态。
    /// </summary>
    public enum PlayerStateId
    {
        /// <summary>未初始化或无状态。</summary>
        None = 0,
        /// <summary>待机状态。</summary>
        Idle = 10,
        /// <summary>移动状态，包含自由移动、锁定移动和冲刺。</summary>
        Locomotion = 20,
        /// <summary>闪避状态，包含普通闪避和完美闪避。</summary>
        Evade = 30,
        /// <summary>普通攻击状态，管理轻重攻击连招。</summary>
        Attack = 40,
        /// <summary>防御状态，包含普通防御和完美防御。</summary>
        Guard = 50,
        /// <summary>技能释放状态，当前版本对应 Skill1。</summary>
        Skill = 60,
        /// <summary>普通受击状态。</summary>
        HitReaction = 70,
        /// <summary>击倒 / 倒地状态。</summary>
        Knockdown = 80,
        /// <summary>死亡状态，最高强制优先级。</summary>
        Dead = 100
    }

    /// <summary>
    /// 动作状态的通用阶段，用于表达起手、生效、后摇、重置等窗口。
    /// </summary>
    public enum PlayerStatePhase
    {
        /// <summary>无阶段。</summary>
        None = 0,
        /// <summary>起手阶段。</summary>
        Start = 10,
        /// <summary>生效阶段，例如攻击判定或技能判定开启。</summary>
        Active = 20,
        /// <summary>后摇阶段。</summary>
        Recovery = 30,
        /// <summary>重置区间，允许自然返回或响应后段派生。</summary>
        Reset = 40,
        /// <summary>循环保持阶段，例如 GuardLoop 或移动循环。</summary>
        Loop = 50
    }

    /// <summary>
    /// Evade 顶层状态内部模式。PerfectEvade 不新增顶层状态，只切换 Evade 内部时间轴和动画模式。
    /// </summary>
    public enum EvadeModeId
    {
        /// <summary>当前不在 Evade 或未初始化。</summary>
        None = 0,
        /// <summary>普通闪避时间轴。</summary>
        Normal = 1,
        /// <summary>完美闪避触发后的专用时间轴。</summary>
        Perfect = 2
    }

    /// <summary>
    /// 玩家控制模式。锁定只影响朝向、移动映射和相机，不直接改变顶层状态。
    /// </summary>
    public enum ControlMode
    {
        /// <summary>自由视角模式。</summary>
        Free = 0,
        /// <summary>锁定 Boss 模式。</summary>
        LockOn = 1
    }
}
