// 文件说明：维护 Boss Attack 与 Reposition 共用的 Action 生命周期调度。
// 所属模块：Boss 动作执行。
// 运行影响：影响 Boss 动作生命周期、位移上下文、动画播放和战斗执行入口。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Movement;
using UnityEngine;

namespace ProjectEVE.Boss.Actions
{
    /// <summary>
    /// Boss 正式 Action Runner。BossActionKind 显式决定是否创建攻击实例和推进 HitNode。
    /// </summary>
    public sealed class BossActionRunner
    {
        private BossAttackExecutor attackExecutor;
        private BossMovementSystem movementSystem;
        private BossAnimationBridge animationBridge;
        private BossAttackDefinition currentAction;
        private BossActionKind currentActionKind;
        private float actionElapsed;
        private int nextAttackInstanceId;

        /// <summary>当前 Runner 是否持有正在执行的 Boss Action。</summary>
        public bool IsRunning => currentAction != null || (attackExecutor != null && attackExecutor.IsExecuting);
        /// <summary>当前动作的显式类别；仅在 IsRunning 为 true 时有效。</summary>
        public BossActionKind CurrentActionKind => currentActionKind;
        /// <summary>当前 Runner 执行的 Action ID。</summary>
        public string CurrentActionId => currentAction != null ? currentAction.AttackId : string.Empty;
        /// <summary>当前 Runner 持有的 Action 定义。</summary>
        public BossAttackDefinition CurrentActionDefinition => currentAction;
        /// <summary>当前 Action Timeline 已经过时间，单位为秒。</summary>
        public float CurrentElapsed => currentActionKind == BossActionKind.Attack
            ? attackExecutor != null ? attackExecutor.AttackElapsed : 0f
            : actionElapsed;

        /// <summary>绑定 Runner 需要驱动的既有运行时组件。</summary>
        public void Bind(
            BossAttackExecutor nextAttackExecutor,
            BossMovementSystem nextMovementSystem,
            BossAnimationBridge nextAnimationBridge)
        {
            attackExecutor = nextAttackExecutor;
            movementSystem = nextMovementSystem;
            animationBridge = nextAnimationBridge;
            attackExecutor?.BindMovementSystem(movementSystem);
        }

        /// <summary>
        /// 启动一次 Boss Action；Attack 创建战斗实例，Reposition 只推进动画、时间和 MotionProfile。
        /// </summary>
        /// <param name="actionDefinition">提供动作 ID、时长、动画状态和 RuntimeSpec 的 Timeline 定义。</param>
        /// <param name="actionKind">来自 BossActionSet 的显式动作类别，不能由 HitNode 数量推断。</param>
        /// <param name="target">动作位移计算使用的当前目标。</param>
        /// <returns>配置有效时返回启动结果；类别不支持或 HitNode 结构与类别冲突时返回失败。</returns>
        public BossActionRunnerStartResult StartAction(
            BossAttackDefinition actionDefinition,
            BossActionKind actionKind,
            Transform target)
        {
            if (actionDefinition == null)
            {
                currentAction = null;
                Debug.LogError($"{nameof(BossActionRunner)} cannot start a null action definition.");
                return BossActionRunnerStartResult.Failed;
            }

            int hitNodeCount = actionDefinition.HitNodes?.Length ?? 0;
            if (actionKind == BossActionKind.Attack && (attackExecutor == null || hitNodeCount == 0))
            {
                currentAction = null;
                Debug.LogError(
                    $"Boss Attack '{actionDefinition.AttackId}' requires an AttackExecutor and at least one HitNode.");
                return BossActionRunnerStartResult.Failed;
            }

            if (actionKind == BossActionKind.Reposition && hitNodeCount > 0)
            {
                currentAction = null;
                Debug.LogError($"Boss Reposition '{actionDefinition.AttackId}' must not contain HitNode data.");
                return BossActionRunnerStartResult.Failed;
            }

            if (actionKind != BossActionKind.Attack && actionKind != BossActionKind.Reposition)
            {
                currentAction = null;
                Debug.LogError($"Boss Action '{actionDefinition.AttackId}' uses unsupported kind {actionKind}.");
                return BossActionRunnerStartResult.Failed;
            }

            currentAction = actionDefinition;
            currentActionKind = actionKind;
            actionElapsed = 0f;
            movementSystem?.BeginActionMotion(actionDefinition, target);
            int attackInstanceId = 0;
            if (actionKind == BossActionKind.Attack)
            {
                attackInstanceId = CreateAttackInstanceId();
                attackExecutor.BeginAttack(actionDefinition, attackInstanceId, target);
            }
            else
            {
                attackExecutor?.EndAttack();
            }

            animationBridge?.PlayAttack(actionDefinition);
            return BossActionRunnerStartResult.Started(actionDefinition.AttackId, attackInstanceId);
        }

        /// <summary>推进当前正式 Action 生命周期。</summary>
        /// <param name="deltaTime">本帧经过时间，单位为秒；负值按 0 处理。</param>
        /// <returns>当前动作继续、完成或被外部系统中断的结果。</returns>
        public BossActionRunnerTickResult Tick(float deltaTime)
        {
            if (currentActionKind == BossActionKind.Reposition && currentAction != null)
            {
                actionElapsed += Mathf.Max(0f, deltaTime);
                movementSystem?.TickActionMotion(deltaTime, actionElapsed);
                return actionElapsed >= currentAction.TotalDuration
                    ? BossActionRunnerTickResult.Completed
                    : BossActionRunnerTickResult.Running;
            }

            if (attackExecutor == null)
            {
                currentAction = null;
                return BossActionRunnerTickResult.Completed;
            }

            if (!attackExecutor.IsExecuting)
            {
                currentAction = null;
                return BossActionRunnerTickResult.Completed;
            }

            bool completed = attackExecutor.Tick(deltaTime);
            if (completed)
            {
                return BossActionRunnerTickResult.Completed;
            }

            if (!attackExecutor.IsExecuting)
            {
                currentAction = null;
                return BossActionRunnerTickResult.Interrupted;
            }

            return BossActionRunnerTickResult.Running;
        }

        /// <summary>结束当前正式 Action 生命周期，并清理 Executor 与 Motion 上下文。</summary>
        public void EndAction()
        {
            attackExecutor?.EndAttack();
            movementSystem?.EndActionMotion();
            currentAction = null;
            currentActionKind = BossActionKind.Attack;
            actionElapsed = 0f;
        }

        private int CreateAttackInstanceId()
        {
            if (nextAttackInstanceId == int.MaxValue)
            {
                nextAttackInstanceId = 0;
            }

            return ++nextAttackInstanceId;
        }
    }

    /// <summary>
    /// BossActionRunner 启动 Action 后的正式提交结果。
    /// </summary>
    public readonly struct BossActionRunnerStartResult
    {
        /// <summary>启动失败结果。</summary>
        public static readonly BossActionRunnerStartResult Failed = new BossActionRunnerStartResult(false, string.Empty, 0);

        private BossActionRunnerStartResult(bool succeeded, string actionId, int attackInstanceId)
        {
            Succeeded = succeeded;
            ActionId = actionId ?? string.Empty;
            AttackInstanceId = attackInstanceId;
        }

        /// <summary>Action 生命周期是否已正式启动。</summary>
        public bool Succeeded { get; }
        /// <summary>已启动 Action ID。</summary>
        public string ActionId { get; }
        /// <summary>本次战斗攻击实例 ID；Reposition 恒为 0。</summary>
        public int AttackInstanceId { get; }

        /// <summary>创建启动成功结果。</summary>
        public static BossActionRunnerStartResult Started(string actionId, int attackInstanceId)
        {
            return new BossActionRunnerStartResult(true, actionId, attackInstanceId);
        }
    }

    /// <summary>
    /// BossActionRunner 单帧推进结果。
    /// </summary>
    public readonly struct BossActionRunnerTickResult
    {
        /// <summary>继续执行中。</summary>
        public static readonly BossActionRunnerTickResult Running = new BossActionRunnerTickResult(BossActionRunnerTickStatus.Running);
        /// <summary>自然完成或当前无动作。</summary>
        public static readonly BossActionRunnerTickResult Completed = new BossActionRunnerTickResult(BossActionRunnerTickStatus.Completed);
        /// <summary>被 PerfectGuard、受击、死亡等外部反应中断。</summary>
        public static readonly BossActionRunnerTickResult Interrupted = new BossActionRunnerTickResult(BossActionRunnerTickStatus.Interrupted);

        private BossActionRunnerTickResult(BossActionRunnerTickStatus status)
        {
            Status = status;
        }

        /// <summary>本帧 Runner 推进状态。</summary>
        public BossActionRunnerTickStatus Status { get; }
        /// <summary>当前动作是否自然完成或没有动作需要推进。</summary>
        public bool IsCompleted => Status == BossActionRunnerTickStatus.Completed;
        /// <summary>当前动作是否被外部反应中断。</summary>
        public bool IsInterrupted => Status == BossActionRunnerTickStatus.Interrupted;
    }

    /// <summary>
    /// BossActionRunner 单帧推进状态。
    /// </summary>
    public enum BossActionRunnerTickStatus
    {
        Running = 0,
        Completed = 1,
        Interrupted = 2
    }
}
