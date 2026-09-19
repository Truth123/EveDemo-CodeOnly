// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Idle 状态。当前阶段只作为自由输入和进入动作状态的兜底状态。
    /// </summary>
    public sealed class PlayerIdleState : PlayerStateBase
    {

        public PlayerIdleState() : base(PlayerStateId.Idle)
        {
        }


        public override void Enter(PlayerStateContext context)
        {
            context.CurrentPhase = PlayerStatePhase.Loop;
        }


        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            return context.HasMoveInput ? PlayerStateId.Locomotion : PlayerStateId.None;
        }
    }
}
