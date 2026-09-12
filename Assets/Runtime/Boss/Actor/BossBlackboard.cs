// 文件说明：定义 Boss Brain / Selector 的正式只读输入合同。
// 所属模块：Boss Actor。
// 运行影响：只聚合本帧决策事实，不驱动动画、移动、命中或状态切换。

using ProjectEVE.Boss.AI;

namespace ProjectEVE.Boss.Actor
{
    /// <summary>
    /// Boss Brain / Selector 的正式输入合同。字段直接表达本帧事实，避免重复的中间事实模型。
    /// </summary>
    public readonly struct BossBlackboard
    {
        /// <summary>空 Blackboard，用于未绑定或无有效目标时的无副作用评估。</summary>
        public static readonly BossBlackboard Empty = new BossBlackboard(
            BossStateId.None,
            false,
            string.Empty,
            0f,
            string.Empty,
            0f,
            false,
            float.MaxValue,
            180f,
            string.Empty,
            0,
            false,
            BossActionKind.Attack,
            0,
            false);

        /// <summary>
        /// 创建正式 Blackboard，并保存 Brain / Selector 当前帧需要读取的全部事实。
        /// </summary>
        public BossBlackboard(
            BossStateId currentState,
            bool isRunning,
            string currentActionId,
            float stateElapsed,
            string currentHitNodeId,
            float currentActionElapsed,
            bool hasTarget,
            float distanceToTarget,
            float angleToTarget,
            string lastSelectedActionId,
            int repeatedSelectedActionCount,
            bool isActionExecuting,
            BossActionKind currentActionKind,
            int currentAttackInstanceId,
            bool canReturnFromReaction)
        {
            CurrentState = currentState;
            IsRunning = isRunning;
            CurrentActionId = currentActionId ?? string.Empty;
            StateElapsed = stateElapsed;
            CurrentHitNodeId = currentHitNodeId ?? string.Empty;
            CurrentActionElapsed = currentActionElapsed;
            HasTarget = hasTarget;
            DistanceToTarget = hasTarget ? distanceToTarget : float.MaxValue;
            AngleToTarget = hasTarget ? angleToTarget : 180f;
            LastSelectedActionId = lastSelectedActionId ?? string.Empty;
            RepeatedSelectedActionCount = repeatedSelectedActionCount;
            IsActionExecuting = isActionExecuting;
            CurrentActionKind = currentActionKind;
            CurrentAttackInstanceId = isActionExecuting && currentActionKind == BossActionKind.Attack
                ? currentAttackInstanceId
                : 0;
            CanReturnFromReaction = canReturnFromReaction;
        }

        /// <summary>当前 Boss 唯一状态事实。</summary>
        public BossStateId CurrentState { get; }
        public bool IsRunning { get; }
        public string CurrentActionId { get; }
        public float StateElapsed { get; }
        public string CurrentHitNodeId { get; }
        public float CurrentActionElapsed { get; }
        public bool HasTarget { get; }
        public float DistanceToTarget { get; }
        public float AngleToTarget { get; }
        public string LastSelectedActionId { get; }
        public int RepeatedSelectedActionCount { get; }
        public bool IsActionExecuting { get; }
        public BossActionKind CurrentActionKind { get; }
        public int CurrentAttackInstanceId { get; }
        public bool CanReturnFromReaction { get; }
    }
}
