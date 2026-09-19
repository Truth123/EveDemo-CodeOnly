// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Dead 状态。只允许外部战斗重置流程离开。
    /// </summary>
    public sealed class PlayerDeadState : PlayerStateBase
    {
        /// <summary>死亡 Timeline 资产 ID。死亡生命周期仍由 Dead 状态自身维护。</summary>
        public const string TimelineId = "Player_Dead";


        public PlayerDeadState() : base(PlayerStateId.Dead)
        {
        }


        public override void Enter(PlayerStateContext context)
        {
            context.IsDead = true;
            context.CurrentPhase = PlayerStatePhase.Loop;
            context.PrepareDead();
            context.ClearActionWindowsAndBuffers();
        }
    }
}
