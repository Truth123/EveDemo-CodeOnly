// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// 玩家状态基类。具体状态只实现自身规则，不直接查找场景对象。
    /// </summary>
    public abstract class PlayerStateBase
    {
        /// <summary>
        /// 创建 PlayerStateBase 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        protected PlayerStateBase(PlayerStateId stateId)
        {
            StateId = stateId;
        }

        /// <summary>该状态对应的顶层状态 ID。</summary>
        public PlayerStateId StateId { get; }

        /// <summary>
        /// 状态进入回调，用于初始化阶段、窗口和临时数据。
        /// </summary>
        public virtual void Enter(PlayerStateContext context)
        {
        }

        /// <summary>
        /// 状态每帧更新回调，用于处理窗口、输入消费和状态流转。返回 None 表示保持当前状态。
        /// </summary>
        public virtual PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            return PlayerStateId.None;
        }

        /// <summary>
        /// 状态退出回调，用于关闭判定、清理缓存和还原临时标记。
        /// </summary>
        public virtual void Exit(PlayerStateContext context)
        {
        }

        /// <summary>
        /// 根据移动输入决定动作结束后回到 Idle 还是 Locomotion。
        /// </summary>
        protected static PlayerStateId ReturnToIdleOrLocomotion(PlayerStateContext context)
        {
            return context.HasMoveInput ? PlayerStateId.Locomotion : PlayerStateId.Idle;
        }
    }
}
