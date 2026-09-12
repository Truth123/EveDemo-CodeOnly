// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Locomotion 状态。移动细节由 PlayerMovementMotor 处理。
    /// </summary>
    public sealed class PlayerLocomotionState : PlayerStateBase
    {
        /// <summary>
        /// 创建 PlayerLocomotionState 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        public PlayerLocomotionState() : base(PlayerStateId.Locomotion)
        {
        }

        /// <summary>
        /// 执行 Enter 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Enter(PlayerStateContext context)
        {
            context.CurrentPhase = PlayerStatePhase.Loop;
        }

        /// <summary>
        /// 推进 Tick 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            return context.HasMoveInput ? PlayerStateId.None : PlayerStateId.Idle;
        }
    }
}
