// 文件说明：保存 Boss 唯一运行时状态、状态计时和最小 Tick 决策。
// 所属模块：Boss 状态机。
// 运行影响：集中 BossActor 的状态事实，不驱动动画、移动或命中。

using ProjectEVE.Boss.AI;

namespace ProjectEVE.Boss.State
{
    /// <summary>
    /// 保存 Boss 唯一状态事实，并根据该状态生成无副作用 Tick 决策。
    /// </summary>
    public sealed class BossStateMachine
    {
        /// <summary>当前 Boss 状态。它是动画、Movement、Reaction 和 Brain 读取的唯一状态事实源。</summary>
        public BossStateId CurrentState { get; private set; } = BossStateId.None;

        /// <summary>当前状态已经经过的时间。</summary>
        public float StateElapsed { get; private set; }

        /// <summary>
        /// 推进当前状态计时。未运行状态也允许计时由调用方决定是否推进。
        /// </summary>
        public void Tick(float deltaTime)
        {
            if (deltaTime > 0f)
            {
                StateElapsed += deltaTime;
            }
        }

        /// <summary>
        /// 切换到新状态。状态相同时返回 false，状态改变时重置状态计时并返回 true。
        /// </summary>
        public bool TransitionTo(BossStateId nextState, out BossStateId previousState)
        {
            previousState = CurrentState;
            if (CurrentState == nextState)
            {
                return false;
            }

            CurrentState = nextState;
            StateElapsed = 0f;
            return true;
        }

        /// <summary>
        /// 重置当前状态计时，用于同状态反应刷新。
        /// </summary>
        public void ResetStateElapsed()
        {
            StateElapsed = 0f;
        }

        /// <summary>
        /// 根据当前 BossStateId 生成本帧推进意图；只读状态事实，不调用组件或切换状态。
        /// </summary>
        /// <param name="canReturnFromReaction">当前 HitStagger 是否已经允许交回 Brain 执行战术决策。</param>
        /// <returns>本帧应由 BossActor 提交的推进类型。</returns>
        public BossStateTickDecisionId EvaluateTick(bool canReturnFromReaction)
        {
            switch (CurrentState)
            {
                case BossStateId.Idle:
                    return BossStateTickDecisionId.EvaluateTactics;
                case BossStateId.Approach:
                case BossStateId.Strafe:
                    return BossStateTickDecisionId.EvaluateTactics;
                case BossStateId.Action:
                    return BossStateTickDecisionId.TickAction;
                case BossStateId.Recovery:
                    return BossStateTickDecisionId.TickRecovery;
                case BossStateId.HitStagger:
                    return canReturnFromReaction
                        ? BossStateTickDecisionId.EvaluateTactics
                        : BossStateTickDecisionId.None;
                case BossStateId.Knockdown:
                    return BossStateTickDecisionId.TickKnockdown;
                case BossStateId.ShieldBreakStun:
                    return BossStateTickDecisionId.TickShieldBreakStun;
                default:
                    return BossStateTickDecisionId.None;
            }
        }
    }

    /// <summary>
    /// Boss 当前状态的 Tick 决策类型。
    /// </summary>
    public enum BossStateTickDecisionId
    {
        /// <summary>本帧无需额外状态推进。</summary>
        None = 0,
        /// <summary>请求 BossBrain 执行非承诺状态下的战术评估。</summary>
        EvaluateTactics = 1,
        /// <summary>推进当前 Attack 或 Reposition 动作。</summary>
        TickAction = 5,
        /// <summary>推进攻击视觉后摇，并在恢复锁结束时请求战术评估。</summary>
        TickRecovery = 8,
        /// <summary>推进 Knockdown 动画阶段。</summary>
        TickKnockdown = 6,
        /// <summary>推进防御护盾击破眩晕计时。</summary>
        TickShieldBreakStun = 9
    }
}
