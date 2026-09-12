// 文件说明：Boss AI 唯一 MonoBehaviour 入口，负责正式 Tick 调度和系统协作。
// 所属模块：Boss Actor。
// 运行影响：影响 Boss 行为推进、阶段转换固定招式、护盾击破眩晕、出招提交、Root Motion 和死亡切换。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Actions;
using ProjectEVE.Boss.Brain;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Config;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Boss.Reaction;
using ProjectEVE.Boss.State;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using ProjectEVE.Player.Combat;
using UnityEngine;

namespace ProjectEVE.Boss.Actor
{
    /// <summary>
    /// Raven Boss 正式 Actor。负责目标获取、状态推进、出招选择和系统协作。
    /// </summary>
    [RequireComponent(typeof(BossAttackExecutor))]
    [RequireComponent(typeof(BossAnimationBridge))]
    [RequireComponent(typeof(BossMotionController))]
    public sealed class BossActor : MonoBehaviour
    {
        private const float ReactionHitSequenceWindow = 1.25f;
        private const int MaxReactionHitsPerSequence = 3;
        private const float PlayerMovingAwayVelocityThreshold = 0.25f;
        private const float SkillHitStaggerMinimumReturnTime = 0.95f;
        private const string HitStaggerRefreshLimitRejectReason = "HitStaggerRefreshLimitReached";

        private enum BossStateTransitionReason
        {
            StartupAutoStart = 0,
            StartAiDead = 1,
            StartAiIdle = 2,
            StopAi = 3,
            TickDetectedDeath = 4,
            TargetLost = 5,
            ActionStarted = 7,
            AttackCompleted = 8,
            KnockdownMissingConfig = 9,
            KnockdownCompleted = 10,
            BrainReposition = 11,
            ReactionDead = 15,
            ReactionNewState = 16,
            ReactionSameStateRefresh = 17,
            ShieldBreakStunCompleted = 18
        }

        private readonly struct BossStateTransitionRequest
        {
            /// <summary>
            /// 创建一次 Boss 状态切换请求，显式记录触发切换的运行时理由。
            /// </summary>
            /// <param name="reason">本次切换请求的来源语义。</param>
            /// <param name="nextState">请求进入的 Boss 叶子状态。</param>
            /// <param name="playStateAnimation">是否播放目标状态的通用状态动画。</param>
            public BossStateTransitionRequest(
                BossStateTransitionReason reason,
                BossStateId nextState,
                bool playStateAnimation)
            {
                Reason = reason;
                NextState = nextState;
                PlayStateAnimation = playStateAnimation;
            }

            /// <summary>本次切换请求的来源语义。</summary>
            public BossStateTransitionReason Reason { get; }
            /// <summary>请求进入的 Boss 叶子状态。</summary>
            public BossStateId NextState { get; }
            /// <summary>是否播放目标状态的通用状态动画。</summary>
            public bool PlayStateAnimation { get; }
        }

        /// <summary>玩家 Transform。未绑定时从 PlayerCombatReceiver 自动获取。</summary>
        [SerializeField] private Transform target;
        /// <summary>玩家受击接收器。</summary>
        [SerializeField] private PlayerCombatReceiver targetReceiver;
        /// <summary>Boss 资源组件，用于死亡判断。</summary>
        [SerializeField] private CombatResourceComponent resourceComponent;
        /// <summary>Boss 攻击执行器。</summary>
        [SerializeField] private BossAttackExecutor attackExecutor;
        /// <summary>Boss 动画桥接器。</summary>
        [SerializeField] private BossAnimationBridge animationBridge;
        /// <summary>Boss 位移总控，负责代码位移、Root Motion 和碰撞裁剪。</summary>
        [SerializeField] private BossMotionController motionController;
        /// <summary>是否在 Play Mode 开始后自动运行 Boss AI。</summary>
        [SerializeField] private bool autoStart = true;
        /// <summary>Approach 水平速度。</summary>
        [SerializeField] private float approachSpeed = 2.4f;
        /// <summary>Strafe 水平速度。</summary>
        [SerializeField] private float strafeSpeed = 1.2f;
        /// <summary>旋转朝向玩家的速度。</summary>
        [SerializeField] private float rotateSpeed = 720f;
        /// <summary>是否输出 Boss 状态切换日志。</summary>
        [SerializeField] private bool logStateChanges;
        /// <summary>Boss 可选动作池。未绑定时不自动创建默认攻击池。</summary>
        [SerializeField] private BossActionSet actionSet;
        /// <summary>Boss AI 使用的运行时韧性上限；当前只影响低护盾偏好，不新增 UI。</summary>
        [SerializeField] private float maxPoise = 100f;
        /// <summary>Boss 独立防御护盾上限；只由正式 PerfectGuard 扣除。</summary>
        [Min(1)] [SerializeField] private int maxShieldDefense = 20;
        /// <summary>护盾归零后的独立眩晕持续时间，单位秒。</summary>
        [Min(0f)] [SerializeField] private float shieldBreakStunDuration = 6f;
        /// <summary>护盾击破长眩晕的三阶段视觉控制器；只负责粒子和非武器灯光。</summary>
        [SerializeField] private BossShieldBreakStunVfxController shieldBreakStunVfx;
        [Header("Arena Pressure Probe")]
        [SerializeField] private Transform arenaCenter;
        [Min(0f)] [SerializeField] private float arenaRadius;
        [Min(0f)] [SerializeField] private float arenaNearEdgeDistance = 1.5f;
        [Min(0f)] [SerializeField] private float arenaCriticalEdgeDistance = 0.75f;

        private readonly BossStateMachine stateMachine = new BossStateMachine();
        private readonly BossMovementSystem movementSystem = new BossMovementSystem();
        private readonly BossActionRunner actionRunner = new BossActionRunner();
        private readonly BossDecisionMemory decisionMemory = new BossDecisionMemory();
        private BossAttackRecoveryTracker attackRecoveryTracker = new BossAttackRecoveryTracker();
        private readonly BossRuntimeConfigResolver runtimeConfigResolver = new BossRuntimeConfigResolver();
        private PlayerStateMachine targetStateMachine;
        private int strafeDirection = 1;
        private BossHitDirectionId lastReceivedHitDirection = BossHitDirectionId.None;
        private CombatAttackType lastReceivedAttackType;
        private CombatHitOutcome lastReceivedHitOutcome = CombatHitOutcome.None;
        private bool hasLastReceivedPlayerHit;
        private bool lastPlayerHitInterrupted;
        private bool lastPlayerHitBlockedByBossGate;
        private string lastPlayerHitRejectReason = string.Empty;
        private bool knockdownLoopPlayed;
        private bool knockdownEndPlayed;
        private bool recoveryIdleAnimationPlayed;
        private bool attackPressureResolutionCommitted;
        private BossCombatPhaseId observedCombatPhase = BossCombatPhaseId.Phase1;
        private int pendingPhaseTransitionBurstCount;
        private bool combatPhaseTrackingInitialized;
        private int evaluatedReactionCounterHitCount;
        private bool reactionCounterRequested;
        private float nextBrainDecisionTime;
        private float currentPoise;
        private int currentShieldDefense;
        private bool shieldBreakWeakAnimationPlayed;
        private bool refillShieldAfterKnockdown;

        /// <summary>当前 Boss 状态。</summary>
        public BossStateId CurrentState => stateMachine.CurrentState;
        /// <summary>Boss AI 当前是否处于运行状态。Dead 和 None 都视为未运行。</summary>
        public bool IsRunning => CurrentState != BossStateId.None && CurrentState != BossStateId.Dead;
        /// <summary>当前 Action ID。</summary>
        public string CurrentActionId => actionRunner.CurrentActionId;
        /// <summary>当前 Action 的显式类别；仅在 ActionRunner 正在运行时有效。</summary>
        public BossActionKind CurrentActionKind => actionRunner.CurrentActionKind;
        /// <summary>当前 Boss 状态已运行时间。</summary>
        public float StateElapsed => stateMachine.StateElapsed;
        /// <summary>当前 HitNode ID。</summary>
        public string CurrentHitNodeId => attackExecutor != null ? attackExecutor.CurrentHitNodeId : string.Empty;
        /// <summary>当前 Action 已经过时间，单位为秒。</summary>
        public float ActionElapsed => actionRunner.CurrentElapsed;
        /// <summary>当前是否已绑定有效玩家目标。</summary>
        public bool HasTarget => target != null && targetReceiver != null;
        /// <summary>当前 Boss 与玩家目标的水平距离。未绑定有效目标时返回 float.MaxValue。</summary>
        public float TargetDistance => HasTarget ? CurrentTargetDistance : float.MaxValue;
        /// <summary>当前 Boss 正前方与玩家目标方向的水平夹角。未绑定有效目标时返回 180 度。</summary>
        public float TargetAngle => HasTarget ? AngleToTarget() : 180f;
        /// <summary>Boss 当前 HP 比例，缺少资源时按 1。</summary>
        public float HpRatio => resourceComponent != null && resourceComponent.MaxHp > 0f
            ? Mathf.Clamp01(resourceComponent.CurrentHp / resourceComponent.MaxHp)
            : 1f;
        /// <summary>Boss 当前运行时韧性比例，供低护盾动作偏好读取。</summary>
        public float PoiseRatio => maxPoise > 0f ? Mathf.Clamp01(currentPoise / maxPoise) : 1f;
        /// <summary>Boss 当前独立防御护盾格数。</summary>
        public int CurrentShieldDefense => currentShieldDefense;
        /// <summary>Boss 独立防御护盾最大格数。</summary>
        public int MaxShieldDefense => maxShieldDefense;
        /// <summary>Boss 当前是否处于护盾击破长眩晕。</summary>
        public bool IsShieldBreakStunned => CurrentState == BossStateId.ShieldBreakStun;
        /// <summary>当前 HP 距离下一阶段阈值的比例差；已经跨入最终阶段时返回 1。</summary>
        public float DistanceToNextPhaseRatio
        {
            get
            {
                float hp = HpRatio;
                if (hp > 0.6f) return hp - 0.6f;
                if (hp > 0.25f) return hp - 0.25f;
                return 1f;
            }
        }
        /// <summary>Boss 靠近配置场地边缘时追加到动作预测和提交的压力。</summary>
        public float ArenaEdgePressureBonus
        {
            get
            {
                if (arenaCenter == null || arenaRadius <= 0f)
                {
                    return 0f;
                }

                Vector3 offset = transform.position - arenaCenter.position;
                offset.y = 0f;
                float remaining = arenaRadius - offset.magnitude;
                if (remaining <= arenaCriticalEdgeDistance) return 8f;
                if (remaining <= arenaNearEdgeDistance) return 6f;
                return 0f;
            }
        }
        /// <summary>最近一次正式提交的 Boss 招式 ID。</summary>
        public string LastSelectedActionId => decisionMemory.LastSelectedActionId;
        /// <summary>当前 Boss 主动进攻压力，范围为 0–100，供战斗调试 UI 只读显示。</summary>
        public float TempoPressure => decisionMemory.TempoPressure;
        /// <summary>当前是否处于带滞回的降压模式，供战斗调试 UI 只读显示。</summary>
        public bool PressureDecayMode => decisionMemory.PressureDecayMode;
        /// <summary>最近一次正式提交招式所属的 ActionSet 动作池；尚未选招或配置失效时返回 None。</summary>
        public BossActionPoolId LastSelectedActionPool
        {
            get
            {
                string actionId = LastSelectedActionId;
                if (actionSet == null || string.IsNullOrEmpty(actionId))
                {
                    return BossActionPoolId.None;
                }

                for (int i = 0; i < actionSet.Actions.Count; i++)
                {
                    BossActionSetEntry entry = actionSet.Actions[i];
                    if (entry.Enabled && entry.ActionId == actionId)
                    {
                        return entry.Pool;
                    }
                }

                return BossActionPoolId.None;
            }
        }
        /// <summary>最近一次正式提交招式的连续重复次数。</summary>
        public int RepeatedSelectedActionCount => decisionMemory.RepeatedSelectedActionCount;
        /// <summary>最近一次 Boss 正式命中结果。</summary>
        public CombatHitOutcome LastBossHitOutcome => attackExecutor != null ? attackExecutor.LastOutcome : CombatHitOutcome.None;
        /// <summary>是否已经产生过正式 Boss 命中。</summary>
        public bool HasLastBossHit => attackExecutor != null && attackExecutor.HasLastHit;
        /// <summary>是否已经记录过玩家对 Boss 的命中。</summary>
        public bool HasLastReceivedPlayerHit => hasLastReceivedPlayerHit;
        /// <summary>最近一次玩家命中 Boss 的攻击类型。</summary>
        public CombatAttackType LastReceivedPlayerAttackType => lastReceivedAttackType;
        /// <summary>最近一次玩家命中 Boss 的解析结果。</summary>
        public CombatHitOutcome LastReceivedPlayerHitOutcome => lastReceivedHitOutcome;
        /// <summary>最近一次玩家命中是否打断 Boss。</summary>
        public bool LastPlayerHitInterrupted => lastPlayerHitInterrupted;
        /// <summary>最近一次玩家命中是否被 Boss 受击门控中的 Block gate 阻止反应。</summary>
        public bool LastPlayerHitBlockedByBossGate => lastPlayerHitBlockedByBossGate;
        /// <summary>最近一次玩家命中未打断或特殊处理的原因。</summary>
        public string LastPlayerHitRejectReason => lastPlayerHitRejectReason;
        /// <summary>最近一次 Boss 受击方向。</summary>
        public BossHitDirectionId LastReceivedHitDirection => lastReceivedHitDirection;
        /// <summary>当前 Brain / Selector 输入 Blackboard。</summary>
        public BossBlackboard Blackboard => CreateBrainBlackboard();
        /// <summary>当前正式 Brain 高层意图。</summary>
        public BossBrainIntentId BrainIntent => BossBrain.Evaluate(CreateBrainBlackboard());

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            currentPoise = Mathf.Max(0f, maxPoise);
            RefillShieldDefense();
            BindReferences();
            ResetCombatPhaseTracking();
        }

        /// <summary>
        /// 在首次启用前完成启动流程，确保依赖对象和初始状态可用。
        /// </summary>
        private void Start()
        {
            if (currentPoise <= 0f)
            {
                currentPoise = Mathf.Max(0f, maxPoise);
            }

            if (currentShieldDefense <= 0)
            {
                RefillShieldDefense();
            }

            RequestStateTransition(
                BossStateTransitionReason.StartupAutoStart,
                autoStart ? BossStateId.Idle : BossStateId.None);
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            maxPoise = Mathf.Max(0f, maxPoise);
            maxShieldDefense = Mathf.Max(1, maxShieldDefense);
            shieldBreakStunDuration = Mathf.Max(0f, shieldBreakStunDuration);
            if (!Application.isPlaying)
            {
                currentShieldDefense = maxShieldDefense;
            }
            arenaRadius = Mathf.Max(0f, arenaRadius);
            arenaCriticalEdgeDistance = Mathf.Max(0f, arenaCriticalEdgeDistance);
            arenaNearEdgeDistance = Mathf.Max(arenaCriticalEdgeDistance, arenaNearEdgeDistance);
        }

        /// <summary>每帧由 BossActor 作为唯一入口推进正式运行时。</summary>
        private void Update()
        {
            TickActor(Time.deltaTime);
        }

        /// <summary>由 Demo UI 启动 Boss AI；不重置资源、位置或冷却。</summary>
        public void StartBossAi()
        {
            BindReferences();
            if (resourceComponent != null && resourceComponent.IsDead)
            {
                RequestStateTransition(BossStateTransitionReason.StartAiDead, BossStateId.Dead);
                return;
            }

            if (CurrentState == BossStateId.None)
            {
                RequestStateTransition(BossStateTransitionReason.StartAiIdle, BossStateId.Idle);
            }
        }

        /// <summary>
        /// 以所有已启用攻击均进入自身完整冷却的状态启动 Boss AI，供正式开场提交时调用。
        /// </summary>
        public void StartBossAiWithInitialAttackCooldowns()
        {
            BindReferences();
            if (resourceComponent != null && resourceComponent.IsDead)
            {
                StartBossAi();
                return;
            }

            BeginInitialAttackCooldowns(Time.time);
            StartBossAi();
        }

        /// <summary>
        /// 只为 ActionSet 中已启用的 Attack 启动冷却；Reposition 保持可用，且不污染选招历史。
        /// </summary>
        /// <param name="cooldownStartTime">所有初始攻击共同开始倒计时的运行时秒数。</param>
        private void BeginInitialAttackCooldowns(float cooldownStartTime)
        {
            if (actionSet == null)
            {
                return;
            }

            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry entry = actionSet.Actions[i];
                if (!entry.Enabled || entry.Kind != BossActionKind.Attack ||
                    !CombatTimelineProvider.TryGetBossAttack(entry.ActionId, out BossAttackDefinition definition))
                {
                    continue;
                }

                decisionMemory.BeginCooldown(definition, cooldownStartTime);
            }
        }

        /// <summary>由 Demo UI 停止 Boss AI；结束当前攻击和位移，但不重置场景。</summary>
        public void StopBossAi()
        {
            actionRunner.EndAction();
            movementSystem.StopAllMotion();
            attackRecoveryTracker = new BossAttackRecoveryTracker();
            RequestStateTransition(BossStateTransitionReason.StopAi, BossStateId.None, false);
        }

        /// <summary>
        /// 获取当前 Boss 状态匹配的 Combat Timeline 播放事实，供编辑器运行时 playhead 跟随。
        /// </summary>
        public bool TryGetCombatTimelinePlaybackSnapshot(out CombatTimelineRuntimePlaybackSnapshot snapshot)
        {
            switch (CurrentState)
            {
                case BossStateId.Action:
                    return TryCreatePlaybackSnapshot(
                        CurrentState,
                        CurrentActionId,
                        ActionElapsed,
                        out snapshot);
                case BossStateId.HitStagger:
                    return TryCreatePlaybackSnapshot(
                        CurrentState,
                        BossRuntimeConfigResolver.BossHitStaggerTimelineId,
                        StateElapsed,
                        out snapshot);
                case BossStateId.Knockdown:
                    return TryCreatePlaybackSnapshot(
                        CurrentState,
                        BossRuntimeConfigResolver.BossKnockdownTimelineId,
                        StateElapsed,
                        out snapshot);
                default:
                    snapshot = default;
                    return false;
            }
        }

        /// <summary>由 Animator Root Motion Relay 转发动画根位移，统一进入正式 MovementSystem。</summary>
        public void HandleAnimatorRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            movementSystem.HandleRootMotion(deltaPosition, deltaRotation);
        }

        /// <summary>显式推进 BossActor 一帧。</summary>
        public void TickActor(float deltaTime)
        {
            TickRuntime(deltaTime);
        }

        /// <summary>
        /// 获取指定 Action 剩余冷却时间。未知 Action 或冷却结束时返回 0。
        /// </summary>
        /// <param name="actionId">需要查询的 Action ID。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <returns>剩余冷却秒数；未记录或已恢复时返回 0。</returns>
        public float GetActionCooldownRemaining(string actionId, float now)
        {
            return decisionMemory.GetActionCooldownRemaining(actionId, now);
        }

        /// <summary>
        /// 只读检查当前条件是否允许选择指定 Action。该方法不修改正式选招、冷却或 Motion 拒绝状态。
        /// </summary>
        /// <param name="actionDefinition">需要检查的 Timeline 运行时定义。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <returns>true 表示目标、冷却、角度和 Motion 条件均允许选择。</returns>
        public bool CanSelectActionReadOnly(BossAttackDefinition actionDefinition, float now)
        {
            if (actionDefinition == null || !HasTarget)
            {
                return false;
            }

            if (!decisionMemory.CanSelectReadOnly(actionDefinition, now))
            {
                return false;
            }

            if (AngleToTarget() > actionDefinition.AngleLimit)
            {
                return false;
            }

            if (!movementSystem.CanStartActionReadOnly(actionDefinition, target))
            {
                return false;
            }

            return true;
        }

        /// <summary>
        /// 忽略冷却检查招式当前的目标角度与 Motion 空间条件，供“仅因短冷却受阻”判断使用。
        /// </summary>
        /// <param name="actionDefinition">需要检查的 Boss Action 定义。</param>
        /// <returns>true 表示目标存在，且角度与 Motion 空间条件均允许启动该招式。</returns>
        public bool CanSelectActionIgnoringCooldownReadOnly(BossAttackDefinition actionDefinition)
        {
            if (actionDefinition == null ||
                !HasTarget ||
                AngleToTarget() > actionDefinition.AngleLimit)
            {
                return false;
            }

            return movementSystem.CanStartActionReadOnly(actionDefinition, target);
        }

        /// <summary>
        /// 创建 Boss 运行时 Timeline 播放快照；无 ActionId 的状态不参与编辑器跟随。
        /// </summary>
        private static bool TryCreatePlaybackSnapshot(
            BossStateId state,
            string actionId,
            float elapsedTime,
            out CombatTimelineRuntimePlaybackSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                snapshot = default;
                return false;
            }

            snapshot = new CombatTimelineRuntimePlaybackSnapshot(
                "Boss",
                state.ToString(),
                actionId,
                elapsedTime);
            return true;
        }

        /// <summary>
        /// 推进 Boss 正式运行时。由 BossActor 作为唯一 MonoBehaviour 入口调度。
        /// </summary>
        private void TickRuntime(float deltaTime)
        {
            if (CurrentState == BossStateId.None)
            {
                return;
            }

            BindReferences();
            stateMachine.Tick(deltaTime);

            if (resourceComponent != null && resourceComponent.IsDead)
            {
                RequestStateTransition(BossStateTransitionReason.TickDetectedDeath, BossStateId.Dead);
                return;
            }

            if (target == null || targetReceiver == null)
            {
                decisionMemory.ResetTacticalState();
                decisionMemory.ResetPlayerBehavior();
                movementSystem.StopAllMotion();
                RequestStateTransition(BossStateTransitionReason.TargetLost, BossStateId.Idle);
                return;
            }

            movementSystem.SetTarget(target);
            BossCombatPhaseId currentPhase = ResolveCombatPhase();
            ObserveCombatPhaseTransition(currentPhase);
            BossPlayerTacticalSnapshot playerSnapshot = UpdateDecisionMemory(deltaTime, currentPhase);
            if (AllowsActorFacingRotation(CurrentState))
            {
                RotateToTarget(deltaTime);
            }

            TickReactionMotion(deltaTime);
            EvaluateAndCommitStateTick(deltaTime, playerSnapshot);
        }

        /// <summary>
        /// 先由 StateMachine 无副作用评估本帧决策，再由 BossActor 提交正式系统副作用。
        /// </summary>
        /// <param name="deltaTime">本帧推进秒数。</param>
        /// <param name="playerSnapshot">本帧已经采样好的玩家战术事实，供压力和 Brain 决策共用。</param>
        private void EvaluateAndCommitStateTick(float deltaTime, BossPlayerTacticalSnapshot playerSnapshot)
        {
            bool canReturnFromReaction = CanReturnFromReaction();
            BossStateTickDecisionId decision = stateMachine.EvaluateTick(canReturnFromReaction);
            CommitStateTickDecision(decision, deltaTime, playerSnapshot);
        }

        /// <summary>
        /// 提交 StateMachine Tick 决策对应的正式系统调用。StateMachine 不直接驱动动画、移动或命中。
        /// </summary>
        /// <param name="decision">StateMachine 本帧返回的无副作用推进枚举。</param>
        /// <param name="deltaTime">本帧推进秒数。</param>
        /// <param name="playerSnapshot">本帧已经采样好的玩家战术事实，进入 Brain 时直接复用。</param>
        private void CommitStateTickDecision(
            BossStateTickDecisionId decision,
            float deltaTime,
            BossPlayerTacticalSnapshot playerSnapshot)
        {
            switch (decision)
            {
                case BossStateTickDecisionId.EvaluateTactics:
                    EvaluateAndCommitBrainDecision(deltaTime, playerSnapshot);
                    break;
                case BossStateTickDecisionId.TickAction:
                    TickAction(deltaTime);
                    break;
                case BossStateTickDecisionId.TickRecovery:
                    TickRecovery(deltaTime, playerSnapshot);
                    break;
                case BossStateTickDecisionId.TickKnockdown:
                    TickKnockdown();
                    break;
                case BossStateTickDecisionId.TickShieldBreakStun:
                    TickShieldBreakStun();
                    break;
            }
        }

        /// <summary>
        /// 推进护盾击破长眩晕；到达 HitStagger CanReturn 时切换 Weak 动画，持续时间结束后恢复全部护盾并回到 Strafe。
        /// </summary>
        private void TickShieldBreakStun()
        {
            shieldBreakStunVfx?.TickStun(StateElapsed, shieldBreakStunDuration);
            TickShieldBreakStunAnimation();
            if (StateElapsed < shieldBreakStunDuration)
            {
                return;
            }

            RefillShieldDefense();
            RequestStateTransition(BossStateTransitionReason.ShieldBreakStunCompleted, BossStateId.Strafe);
        }

        /// <summary>
        /// 以 Boss_HitStagger 的 CanReturn 起点作为纯动画衔接点，只在本次长眩晕中播放一次 Weak 动画。
        /// </summary>
        private void TickShieldBreakStunAnimation()
        {
            if (shieldBreakWeakAnimationPlayed ||
                !runtimeConfigResolver.TryGetHitStaggerConfig(out CombatTimelineReactionConfig config) ||
                config.RuntimeSpec == null ||
                StateElapsed < config.CanReturnTime)
            {
                return;
            }

            shieldBreakWeakAnimationPlayed = true;
            animationBridge?.PlayShieldBreakStun();
        }

        /// <summary>
        /// 请求 Brain 做战术决策，并由 Actor 提交移动、攻击或等待副作用。
        /// </summary>
        /// <param name="deltaTime">本帧推进秒数。</param>
        /// <param name="playerSnapshot">本帧唯一玩家战术快照，避免重复采样和重复写入玩家返回时间。</param>
        private void EvaluateAndCommitBrainDecision(float deltaTime, BossPlayerTacticalSnapshot playerSnapshot)
        {
            if (TryStartPendingPhaseTransitionBurst())
            {
                return;
            }

            float now = Time.time;
            if (now < nextBrainDecisionTime && IsRepositionState(CurrentState))
            {
                ContinueCurrentReposition(deltaTime);
                return;
            }

            EvaluateReactionCounterOnce();
            BossAiTuning tuning = actionSet != null ? actionSet.Tuning : new BossAiTuning();
            if (IsRepositionState(CurrentState) && StateElapsed >= tuning.NeutralMinDuration)
            {
                CommitTempoReset();
            }

            BossBrainDecision decision = BossBrain.Decide(
                CreateBrainBlackboard(),
                CreateBrainDecisionInput(playerSnapshot),
                this,
                actionSet,
                now);
            nextBrainDecisionTime = now + Random.Range(
                Mathf.Max(0.01f, tuning.DecisionIntervalMin),
                Mathf.Max(tuning.DecisionIntervalMin, tuning.DecisionIntervalMax));
            CommitBrainDecision(decision, deltaTime);
        }

        /// <summary>Brain 决策间隔内继续当前普通重定位，避免横移或接近出现停顿。</summary>
        /// <param name="deltaTime">本帧移动秒数。</param>
        private void ContinueCurrentReposition(float deltaTime)
        {
            switch (CurrentState)
            {
                case BossStateId.Approach:
                    CommitRepositionMove(BossStateId.Approach, deltaTime);
                    break;
                case BossStateId.Strafe:
                    CommitRepositionMove(BossStateId.Strafe, deltaTime);
                    break;
            }
        }

        /// <summary>
        /// 组装 Brain 战术决策所需的阈值输入。
        /// </summary>
        /// <param name="playerSnapshot">本帧已经采样好的玩家战术事实。</param>
        /// <returns>包含阶段、压力、降压模式、最近动作和玩家事实的无副作用 Brain 输入。</returns>
        private BossBrainDecisionInput CreateBrainDecisionInput(BossPlayerTacticalSnapshot playerSnapshot)
        {
            return new BossBrainDecisionInput(
                reactionCounterRequested,
                ResolveCombatPhase(),
                decisionMemory.TempoPressure,
                decisionMemory.PressureDecayMode,
                decisionMemory.ConsecutiveAttackActionCount,
                decisionMemory.TempoResetRequired,
                decisionMemory.RecentActionIds,
                playerSnapshot);
        }

        /// <summary>
        /// 提交 Brain 战术决策。Brain 不直接访问 Movement、ActionRunner 或配置 Resolver。
        /// </summary>
        private void CommitBrainDecision(BossBrainDecision decision, float deltaTime)
        {
            bool commitsReactionCounter = ShouldCommitReactionCounter(decision);
            bool startsFromHitStagger = CurrentState == BossStateId.HitStagger;

            switch (decision.Id)
            {
                case BossBrainDecisionId.StartAction:
                    if (StartAction(decision.ActionDefinition, decision.ActionKind))
                    {
                        ClearReactionCounterRequestIfNeeded(commitsReactionCounter);
                    }
                    break;
                case BossBrainDecisionId.MoveApproach:
                    CommitRepositionMove(BossStateId.Approach, deltaTime);
                    ClearReactionCounterRequestIfNeeded(startsFromHitStagger);
                    break;
                case BossBrainDecisionId.MoveStrafe:
                    CommitRepositionMove(BossStateId.Strafe, deltaTime);
                    ClearReactionCounterRequestIfNeeded(startsFromHitStagger);
                    break;
            }
        }

        /// <summary>
        /// 切换或维持重定位状态，并在同一帧提交对应移动请求。
        /// </summary>
        private void CommitRepositionMove(
            BossStateId repositionState,
            float deltaTime,
            BossStateTransitionReason reason = BossStateTransitionReason.BrainReposition)
        {
            if (!IsRepositionState(repositionState))
            {
                return;
            }

            if (CurrentState != repositionState)
            {
                RequestStateTransition(reason, repositionState);
            }

            switch (repositionState)
            {
                case BossStateId.Approach:
                    movementSystem.MoveReposition(DirectionToTarget(), approachSpeed, deltaTime);
                    break;
                case BossStateId.Strafe:
                    movementSystem.MoveReposition(Vector3.Cross(DirectionToTarget(), Vector3.up).normalized * strafeDirection, strafeSpeed, deltaTime);
                    break;
            }
        }

        /// <summary>
        /// 推进当前 Attack 或 Reposition Action，并按显式类别提交完成状态。
        /// </summary>
        /// <param name="deltaTime">本帧推进秒数。</param>
        private void TickAction(float deltaTime)
        {
            BossActionRunnerTickResult runnerResult = actionRunner.Tick(deltaTime);
            BossActionKind actionKind = actionRunner.CurrentActionKind;
            BossAttackDefinition action = actionRunner.CurrentActionDefinition;

            if (actionKind == BossActionKind.Reposition)
            {
                if (!runnerResult.IsCompleted && !runnerResult.IsInterrupted)
                {
                    return;
                }

                actionRunner.EndAction();
                RequestStateTransition(BossStateTransitionReason.BrainReposition, BossStateId.Strafe);
                return;
            }

            if (CurrentState == BossStateId.Action &&
                action != null &&
                actionRunner.CurrentElapsed >= action.LastHitNodeEndTime)
            {
                recoveryIdleAnimationPlayed = false;
                RequestStateTransition(
                    new BossStateTransitionRequest(BossStateTransitionReason.AttackCompleted, BossStateId.Recovery, false));
                return;
            }

            if (runnerResult.IsCompleted && CurrentState == BossStateId.Action)
            {
                CompleteAttackVisualTail();
                RequestStateTransition(
                    new BossStateTransitionRequest(BossStateTransitionReason.AttackCompleted, BossStateId.Recovery, false));
            }
        }

        /// <summary>在 Recovery 中继续推进动画和位移尾段，并在绝对恢复锁结束后立即评估战术。</summary>
        /// <param name="deltaTime">本帧推进秒数。</param>
        /// <param name="playerSnapshot">本帧已经采样好的玩家战术事实，恢复结束后进入 Brain 时复用。</param>
        private void TickRecovery(float deltaTime, BossPlayerTacticalSnapshot playerSnapshot)
        {
            if (actionRunner.IsRunning)
            {
                BossActionRunnerTickResult result = actionRunner.Tick(deltaTime);
                if (result.IsCompleted)
                {
                    CompleteAttackVisualTail();
                }
            }

            if (!actionRunner.IsRunning && !recoveryIdleAnimationPlayed && IsRecoveryLocked(Time.time))
            {
                recoveryIdleAnimationPlayed = true;
                animationBridge?.PlayState(BossStateId.Recovery);
            }

            if (IsRecoveryLocked(Time.time))
            {
                return;
            }

            CompleteAttackVisualTail();
            attackRecoveryTracker = new BossAttackRecoveryTracker();
            if (actionSet != null)
            {
                decisionMemory.UpdatePressureMode(actionSet.GetPressureProfile(ResolveCombatPhase()));
            }
            EvaluateAndCommitBrainDecision(deltaTime, playerSnapshot);
        }

        private void CompleteAttackVisualTail()
        {
            if (!attackPressureResolutionCommitted)
            {
                decisionMemory.ReducePressure(attackRecoveryTracker.ResolveCompleteGuardPressureReduction());
                attackPressureResolutionCommitted = true;
            }

            if (actionRunner.IsRunning)
            {
                actionRunner.EndAction();
            }
        }

        /// <summary>
        /// 推进 Knockdown 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        private void TickKnockdown()
        {
            if (!runtimeConfigResolver.TryGetKnockdownConfig(out CombatTimelineReactionConfig config))
            {
                RequestStateTransition(BossStateTransitionReason.KnockdownMissingConfig, BossStateId.Strafe);   // 异常，忽略
                return;
            }

            BossKnockdownTickPlan plan = BossReactionSystem.EvaluateKnockdownTick(
                new BossKnockdownTickInput(
                    StateElapsed,
                    config.StunDuration,
                    config.RecoveryStartTime,
                    config.TotalDuration,
                    knockdownLoopPlayed,
                    knockdownEndPlayed));
            CommitKnockdownTickPlan(plan);
        }

        /// <summary>
        /// 提交 Knockdown 阶段 Tick 计划。
        /// </summary>
        private void CommitKnockdownTickPlan(BossKnockdownTickPlan plan)
        {
            if (plan.MarkLoopPlayed)
            {
                knockdownLoopPlayed = true;
            }

            if (plan.PlayLoopAnimation)
            {
                animationBridge?.PlayKnockdownLoop();
            }

            if (plan.MarkEndPlayed)
            {
                knockdownEndPlayed = true;
            }

            if (plan.PlayEndAnimation)
            {
                animationBridge?.PlayKnockdownEnd();
            }

            switch (plan.StateCommit)
            {
                case BossKnockdownStateCommitId.TransitionToIdle:
                    RequestStateTransition(BossStateTransitionReason.KnockdownCompleted, BossStateId.Strafe);
                    break;
            }
        }

        /// <summary>
        /// 组装正式 Brain / Selector 所需的最小 Blackboard 输入。
        /// </summary>
        private BossBlackboard CreateBrainBlackboard()
        {
            bool hasTarget = HasTarget;
            bool isActionExecuting = actionRunner.IsRunning;

            return new BossBlackboard(
                CurrentState,
                IsRunning,
                CurrentActionId,
                StateElapsed,
                CurrentHitNodeId,
                ActionElapsed,
                hasTarget,
                hasTarget ? TargetDistance : float.MaxValue,
                hasTarget ? TargetAngle : 180f,
                decisionMemory.LastSelectedActionId,
                decisionMemory.RepeatedSelectedActionCount,
                isActionExecuting,
                isActionExecuting ? actionRunner.CurrentActionKind : BossActionKind.Attack,
                isActionExecuting && actionRunner.CurrentActionKind == BossActionKind.Attack && attackExecutor != null
                    ? attackExecutor.CurrentAttackInstanceId
                    : 0,
                CanReturnFromReaction());
        }

        /// <summary>
        /// 每帧只解析一次玩家状态，依次更新玩家行为、压力和降压模式，并返回同帧决策快照。
        /// </summary>
        /// <param name="deltaTime">本帧推进与统计秒数。</param>
        /// <param name="currentPhase">本帧已经解析好的 Boss 战斗阶段，用于读取对应压力阈值。</param>
        /// <returns>更新记忆后生成的玩家只读快照；缺少玩家状态机时返回空快照。</returns>
        private BossPlayerTacticalSnapshot UpdateDecisionMemory(
            float deltaTime,
            BossCombatPhaseId currentPhase)
        {
            PlayerStateMachine playerStateMachine = ResolveTargetStateMachine();
            BossPlayerTacticalSnapshot playerSnapshot;
            if (playerStateMachine == null)
            {
                decisionMemory.ResetPlayerBehavior();
                playerSnapshot = BossPlayerTacticalSnapshot.Empty;
            }
            else
            {
                PlayerStateContext context = playerStateMachine.Context;
                BossAiTuning tuning = actionSet != null ? actionSet.Tuning : new BossAiTuning();
                decisionMemory.TickPlayerBehavior(
                    Time.time,
                    deltaTime,
                    context.CurrentState,
                    IsPlayerMovingAwayFromBoss(context),
                    tuning.PlayerBehaviorWindow,
                    tuning.FrequentGuardDuration,
                    tuning.RetreatSustainDuration);
                playerSnapshot = new BossPlayerTacticalSnapshot(
                    context.CurrentState,
                    context.StateElapsedTime,
                    decisionMemory.IsFrequentGuarder,
                    decisionMemory.IsSustainedRetreat);
            }

            decisionMemory.TickPressure(
                CurrentState,
                CurrentState == BossStateId.Action && actionRunner.IsRunning
                    ? actionRunner.CurrentActionKind
                    : (BossActionKind?)null,
                playerSnapshot.IsInReaction,
                deltaTime);
            if (actionSet != null)
            {
                decisionMemory.UpdatePressureMode(actionSet.GetPressureProfile(currentPhase));
            }

            return playerSnapshot;
        }

        /// <summary>
        /// 解析当前目标玩家状态机，目标重绑后会重新从 PlayerCombatReceiver 父级查找。
        /// </summary>
        /// <returns>当前目标玩家状态机；缺少目标或组件时返回 null。</returns>
        private PlayerStateMachine ResolveTargetStateMachine()
        {
            if (targetStateMachine != null)
            {
                return targetStateMachine;
            }

            if (targetReceiver == null)
            {
                return null;
            }

            targetStateMachine = targetReceiver.GetComponentInParent<PlayerStateMachine>();
            return targetStateMachine;
        }

        /// <summary>
        /// 判断玩家当前水平速度是否正在远离 Boss。
        /// </summary>
        /// <param name="context">玩家状态机上下文。</param>
        /// <returns>true 表示玩家有明确远离 Boss 的水平速度。</returns>
        private bool IsPlayerMovingAwayFromBoss(PlayerStateContext context)
        {
            if (context.PlayerTransform == null)
            {
                return false;
            }

            Vector3 awayFromBoss = context.PlayerTransform.position - transform.position;
            awayFromBoss.y = 0f;
            Vector3 velocity = context.ActualHorizontalVelocity;
            velocity.y = 0f;
            if (awayFromBoss.sqrMagnitude <= 0.0001f ||
                velocity.sqrMagnitude <= PlayerMovingAwayVelocityThreshold * PlayerMovingAwayVelocityThreshold)
            {
                return false;
            }

            return Vector3.Dot(velocity.normalized, awayFromBoss.normalized) > 0.45f;
        }

        /// <summary>
        /// 启动统一 Boss Action；成功后提交选择记忆，Attack 从最后 HitNode 结束时开始冷却，Reposition 从动作开始时冷却。
        /// </summary>
        /// <param name="actionDefinition">所选动作的 Timeline 运行时定义。</param>
        /// <param name="actionKind">来自 ActionSet 的显式动作类别。</param>
        /// <returns>true 表示配置通过校验并进入 Action 状态；false 表示拒绝启动。</returns>
        private bool StartAction(BossAttackDefinition actionDefinition, BossActionKind actionKind)
        {
            if (actionDefinition == null ||
                !TryFindActionSetEntry(actionDefinition.AttackId, actionKind, out BossActionSetEntry actionSetEntry))
            {
                return false;
            }

            if (actionKind == BossActionKind.Reposition &&
                actionSetEntry.FollowUpPolicy != BossActionFollowUpPolicy.MustStrafe)
            {
                Debug.LogError(
                    $"Boss Reposition '{actionDefinition.AttackId}' requires FollowUpPolicy.MustStrafe.",
                    this);
                return false;
            }

            if (actionRunner.IsRunning)
            {
                actionRunner.EndAction();
            }

            float actionStartTime = Time.time;
            BossActionRunnerStartResult startResult = actionRunner.StartAction(actionDefinition, actionKind, target);
            if (!startResult.Succeeded)
            {
                if (actionKind == BossActionKind.Attack)
                {
                    attackRecoveryTracker = new BossAttackRecoveryTracker();
                }

                return false;
            }

            float cooldownStartTime = actionKind == BossActionKind.Attack
                ? actionStartTime + actionDefinition.LastHitNodeEndTime
                : actionStartTime;
            decisionMemory.CommitSelection(actionDefinition, cooldownStartTime);

            decisionMemory.RecordAction(
                actionDefinition.AttackId,
                actionKind,
                actionSetEntry.PressureCost + (actionKind == BossActionKind.Attack ? ArenaEdgePressureBonus : 0f),
                actionSetEntry.FollowUpPolicy == BossActionFollowUpPolicy.MustEnterNeutral);

            if (actionKind == BossActionKind.Attack)
            {
                attackPressureResolutionCommitted = false;
                recoveryIdleAnimationPlayed = false;
                BossAttackRecoveryTracker tracker = new BossAttackRecoveryTracker();
                tracker.Begin(actionDefinition, startResult.AttackInstanceId, actionStartTime);
                attackRecoveryTracker = tracker;
            }

            RequestStateTransition(BossStateTransitionReason.ActionStarted, BossStateId.Action, false);
            return true;
        }

        /// <summary>
        /// 记录按 HP 进入的新战斗阶段，并为跨过的每个阶段边界排队一次 BurstAreaSlash。
        /// </summary>
        /// <param name="currentPhase">本帧根据当前 HP 解析出的战斗阶段。</param>
        private void ObserveCombatPhaseTransition(BossCombatPhaseId currentPhase)
        {
            if (!combatPhaseTrackingInitialized)
            {
                observedCombatPhase = currentPhase;
                combatPhaseTrackingInitialized = true;
                return;
            }

            int phaseDelta = (int)currentPhase - (int)observedCombatPhase;
            if (phaseDelta > 0)
            {
                pendingPhaseTransitionBurstCount += phaseDelta;
                observedCombatPhase = currentPhase;
                return;
            }

            if (phaseDelta < 0)
            {
                observedCombatPhase = currentPhase;
                pendingPhaseTransitionBurstCount = 0;
            }
        }

        /// <summary>
        /// 在当前动作或反应安全结束后的首个 Brain 帧优先启动一次阶段转换 BurstAreaSlash。
        /// </summary>
        /// <returns>true 表示本帧已成功启动固定招式；false 表示没有待执行请求或当前仍不能启动。</returns>
        private bool TryStartPendingPhaseTransitionBurst()
        {
            if (pendingPhaseTransitionBurstCount <= 0 ||
                actionRunner.IsRunning ||
                decisionMemory.TempoResetRequired)
            {
                return false;
            }

            if (!CombatTimelineProvider.TryGetBossAttack(
                    RavenBossAttackIds.BurstAreaSlashId,
                    out BossAttackDefinition burstAreaSlash) ||
                !TryFindActionSetEntry(
                    RavenBossAttackIds.BurstAreaSlashId,
                    BossActionKind.Attack,
                    out _))
            {
                pendingPhaseTransitionBurstCount = 0;
                Debug.LogError(
                    $"Boss phase transition requires enabled action '{RavenBossAttackIds.BurstAreaSlashId}' and its Combat Timeline.",
                    this);
                return false;
            }

            bool startsFromHitStagger = CurrentState == BossStateId.HitStagger;
            if (!StartAction(burstAreaSlash, BossActionKind.Attack))
            {
                pendingPhaseTransitionBurstCount = 0;
                return false;
            }

            pendingPhaseTransitionBurstCount--;
            ClearReactionCounterRequestIfNeeded(startsFromHitStagger);
            return true;
        }

        /// <summary>以当前 HP 阶段作为观察基线，并清除尚未执行的阶段转换招式请求。</summary>
        private void ResetCombatPhaseTracking()
        {
            observedCombatPhase = ResolveCombatPhase();
            pendingPhaseTransitionBurstCount = 0;
            combatPhaseTrackingInitialized = true;
        }

        /// <summary>由 AttackExecutor 在本次攻击首次进入有效 HitNode 时提交剩余 30% 压力。</summary>
        public void NotifyBossAttackFirstActiveHit()
        {
            decisionMemory.RecordFirstActiveHit();
        }

        /// <summary>接收单个 Boss HitNode 的正式结算结果，更新压力和正确破解奖励。</summary>
        public void NotifyBossAttackHitResult(BossCombatProcessResult result)
        {
            if (!result.HasHit)
            {
                return;
            }

            if (!attackRecoveryTracker.TryRecordOutcome(
                result.AttackInstanceId,
                result.HitNodeId,
                result.LastOutcome,
                ResolveDefenseStartTime(result.LastOutcome),
                out float pressureIncrease))
            {
                return;
            }

            decisionMemory.AddPressure(pressureIncrease);
            switch (result.LastOutcome)
            {
                case CombatHitOutcome.PerfectGuard:
                    decisionMemory.ReducePressure(3f);
                    break;
                case CombatHitOutcome.PerfectEvade:
                    if (attackRecoveryTracker.TryGetHitNode(result.HitNodeId, out CombatHitNodeData node))
                    {
                        decisionMemory.ReducePressure(!node.CanBeGuarded && !node.CanBePerfectGuarded ? 6f : 3f);
                    }
                    break;
            }
        }

        /// <summary>检查当前攻击的自然后摇或正确破解奖励是否仍在锁定战术决策。</summary>
        /// <param name="now">当前 Unity 运行时间，单位秒。</param>
        /// <returns>true 表示 Recovery/战术决策仍必须等待；false 表示可以交回 Brain。</returns>
        private bool IsRecoveryLocked(float now)
        {
            return attackRecoveryTracker.IsLocked(now);
        }

        /// <summary>把 PG/PE 命中时刻换算为玩家本次 Guard 或 Evade 的起始时刻。</summary>
        /// <param name="outcome">当前 HitNode 的正式防御结果。</param>
        /// <returns>PG/PE 返回对应玩家状态起始秒数；其他结果返回当前秒数。</returns>
        private float ResolveDefenseStartTime(CombatHitOutcome outcome)
        {
            if (outcome != CombatHitOutcome.PerfectGuard && outcome != CombatHitOutcome.PerfectEvade)
            {
                return Time.time;
            }

            PlayerStateMachine playerStateMachine = ResolveTargetStateMachine();
            if (playerStateMachine == null)
            {
                return Time.time;
            }

            return Time.time - Mathf.Max(0f, playerStateMachine.Context.StateElapsedTime);
        }

        /// <summary>处理玩家攻击命中 Boss 后的 AI 反应。</summary>
        public void ReceivePlayerHit(in CombatHitData hit, in CombatHitResult result, Component source)
        {
            if (hit.AttackerTeam != CombatTeam.Player)
            {
                return;
            }

            hasLastReceivedPlayerHit = true;
            lastReceivedAttackType = hit.AttackType;
            lastReceivedHitOutcome = result.Outcome;
            if (result.Outcome != CombatHitOutcome.None)
            {
                currentPoise = Mathf.Clamp(currentPoise - Mathf.Max(0f, hit.PoiseDamage), 0f, Mathf.Max(0f, maxPoise));
            }
            lastReceivedHitDirection = ResolveReceivedHitDirection(hit, source);
            lastPlayerHitInterrupted = false;
            lastPlayerHitBlockedByBossGate = false;
            lastPlayerHitRejectReason = string.Empty;

            bool requiresReactionGate = CurrentState == BossStateId.Action &&
                (!actionRunner.IsRunning || actionRunner.CurrentActionKind == BossActionKind.Attack);
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                CurrentState,
                result.Outcome,
                resourceComponent != null && resourceComponent.IsDead,
                EvaluateCurrentBossReactionGate(hit.AttackType, result.Outcome),
                requiresReactionGate,
                hit.AttackType);
            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(
                input,
                HasBossHitStaggerConfig,
                HasBossKnockdownConfig);
            ApplyPlayerHitReactionDecision(decision, lastReceivedHitDirection);
        }

        /// <summary>
        /// 结算一次正式 PerfectGuard 对 Boss 的统一影响：扣一格独立护盾，并在归零时进入长眩晕。
        /// </summary>
        /// <param name="hitNode">本次被 PerfectGuard 的 Boss HitNode，用于决定护盾未破时是否触发短硬直。</param>
        public void ResolvePerfectGuardAgainstBoss(CombatHitNodeData hitNode)
        {
            if (CurrentState == BossStateId.Dead || currentShieldDefense <= 0)
            {
                return;
            }

            currentShieldDefense = Mathf.Max(0, currentShieldDefense - 1);
            bool shieldBroken = currentShieldDefense == 0;
            BossReactionDecision decision = BossReactionSystem.DecidePerfectGuardBossResponse(
                CurrentState,
                hitNode.TriggersPerfectGuardBossStagger,
                shieldBroken,
                HasBossHitStaggerConfig);
            hasLastReceivedPlayerHit = true;
            lastReceivedAttackType = hitNode.AttackType;
            lastReceivedHitOutcome = CombatHitOutcome.PerfectGuard;
            lastReceivedHitDirection = BossHitDirectionId.Front;
            lastPlayerHitInterrupted = decision.Interrupts;
            lastPlayerHitBlockedByBossGate = false;
            lastPlayerHitRejectReason = decision.RejectReason;

            ExecuteReactionDecision(decision, lastReceivedHitDirection);
        }

        /// <summary>
        /// 根据 ReactionSystem 的决策执行玩家命中 Boss 后的既有副作用。
        /// </summary>
        private void ApplyPlayerHitReactionDecision(BossReactionDecision decision, BossHitDirectionId direction)
        {
            if (TryRejectHitStaggerRefreshBySequenceLimit(decision))
            {
                return;
            }

            BossStateId previousState = CurrentState;
            lastPlayerHitInterrupted = decision.Interrupts;
            lastPlayerHitBlockedByBossGate = decision.BlockedByBossGate;
            lastPlayerHitRejectReason = decision.RejectReason;

            ExecuteReactionDecision(decision, direction);
            RecordReactionHitSequenceIfNeeded(previousState, decision);
        }

        /// <summary>
        /// 执行 Reaction 决策对应的正式副作用：停止当前动作、清理位移、播放反应动画并切换状态。
        /// </summary>
        private void ExecuteReactionDecision(BossReactionDecision decision, BossHitDirectionId direction)
        {
            BossReactionExecutionPlan plan = BossReactionSystem.CreateExecutionPlan(decision, direction);
            CommitReactionExecutionPlan(plan);
        }

        /// <summary>
        /// 提交 ReactionSystem 生成的正式副作用计划。
        /// </summary>
        private void CommitReactionExecutionPlan(BossReactionExecutionPlan plan)
        {
            if (!plan.HasExecution)
            {
                return;
            }

            if (plan.TargetState == BossStateId.Dead || plan.TargetState == BossStateId.Knockdown)
            {
                decisionMemory.ResetTacticalState();
            }
            else
            {
                CommitTempoReset();
            }

            CombatTimelineReactionConfig reactionConfig = default;
            bool hasReactionConfig = false;

            if (plan.RequiresHitStaggerConfig)
            {
                runtimeConfigResolver.TryGetHitStaggerConfig(out CombatTimelineReactionConfig hitStaggerConfig);
                reactionConfig = hitStaggerConfig;
                hasReactionConfig = true;
            }

            if (plan.RequiresKnockdownConfig)
            {
                runtimeConfigResolver.TryGetKnockdownConfig(out CombatTimelineReactionConfig knockdownConfig);
                reactionConfig = knockdownConfig;
                hasReactionConfig = true;
            }

            if (plan.StopRuntime)
            {
                CommitReactionRuntimeStop();
            }

            if (plan.ResetKnockdownRuntime)
            {
                knockdownLoopPlayed = false;
                knockdownEndPlayed = false;
            }

            PlayReactionAnimation(plan);
            CommitReactionState(plan);
            StartReactionMotionIfConfigured(hasReactionConfig, reactionConfig);
        }

        /// <summary>
        /// 根据反应计划播放对应动画。
        /// </summary>
        private void PlayReactionAnimation(BossReactionExecutionPlan plan)
        {
            switch (plan.Animation)
            {
                case BossReactionAnimationId.HitStagger:
                    animationBridge?.PlayHitReaction(plan.Direction);
                    break;
                case BossReactionAnimationId.PerfectGuardStagger:
                    animationBridge?.PlayPerfectGuardStagger();
                    break;
                case BossReactionAnimationId.KnockdownStart:
                    animationBridge?.PlayKnockdownStart(plan.Direction);
                    break;
            }
        }

        /// <summary>
        /// 提交反应状态变化；重复进入同一反应状态时刷新状态计时。
        /// </summary>
        private void CommitReactionState(BossReactionExecutionPlan plan)
        {
            if (plan.TargetState == BossStateId.None)
            {
                return;
            }

            if (plan.TargetState == BossStateId.Dead)
            {
                RequestStateTransition(BossStateTransitionReason.ReactionDead, BossStateId.Dead, true);
                return;
            }

            if (plan.TargetState == CurrentState)
            {
                RequestStateTransition(
                    BossStateTransitionReason.ReactionSameStateRefresh,
                    plan.TargetState,
                    false);
                return;
            }

            RequestStateTransition(BossStateTransitionReason.ReactionNewState, plan.TargetState, false);
        }

        /// <summary>如果当前处于反应状态，则推进 Reaction 层代码位移。</summary>
        private void TickReactionMotion(float deltaTime)
        {
            if (CurrentState != BossStateId.HitStagger &&
                CurrentState != BossStateId.Knockdown)
            {
                return;
            }

            movementSystem.TickReactionMotion(deltaTime, StateElapsed);
        }

        /// <summary>按 Reaction Timeline 中声明的 BossCodeMove 引用启动反应位移。</summary>
        private void StartReactionMotionIfConfigured(
            bool hasReactionConfig,
            CombatTimelineReactionConfig config)
        {
            if (!hasReactionConfig || !config.HasBossCodeMove)
            {
                return;
            }

            movementSystem.BeginReactionMotion(
                config.BossMotionActionId,
                config.BossMotionWindowName,
                target);
        }

        /// <summary>
        /// 只检查 Boss 站立硬直 Timeline 是否存在，供 ReactionSystem 决策按需调用。
        /// </summary>
        private bool HasBossHitStaggerConfig()
        {
            return runtimeConfigResolver.TryGetHitStaggerConfig(out _);
        }

        /// <summary>
        /// 只检查 Boss 击倒 Timeline 是否存在，供 ReactionSystem 决策按需调用。
        /// </summary>
        private bool HasBossKnockdownConfig()
        {
            return runtimeConfigResolver.TryGetKnockdownConfig(out _);
        }

        /// <summary>
        /// 按当前 Boss 状态 Timeline 查询玩家命中是否允许进入 Boss 反应。
        /// </summary>
        private BossReactionGateResult EvaluateCurrentBossReactionGate(
            CombatAttackType attackType,
            CombatHitOutcome outcome)
        {
            switch (CurrentState)
            {
                case BossStateId.Action:
                    if (actionRunner.CurrentActionKind != BossActionKind.Attack)
                    {
                        return BossReactionGateResult.Allowed;
                    }

                    if (actionRunner.CurrentActionDefinition == null ||
                        actionRunner.CurrentActionDefinition.RuntimeSpec == null)
                    {
                        return BossReactionGateResult.BlockedByClosedWindow;
                    }

                    return actionRunner.CurrentActionDefinition.RuntimeSpec.EvaluateBossReactionGate(
                        attackType,
                        outcome,
                        ActionElapsed);
                case BossStateId.HitStagger:
                    if (!runtimeConfigResolver.TryGetHitStaggerConfig(out CombatTimelineReactionConfig hitStaggerConfig) ||
                        hitStaggerConfig.RuntimeSpec == null)
                    {
                        return BossReactionGateResult.BlockedByClosedWindow;
                    }

                    return hitStaggerConfig.RuntimeSpec.EvaluateBossReactionGate(
                        attackType,
                        outcome,
                        StateElapsed);
                case BossStateId.Knockdown:
                    if (!runtimeConfigResolver.TryGetKnockdownConfig(out CombatTimelineReactionConfig knockdownConfig) ||
                        knockdownConfig.RuntimeSpec == null)
                    {
                        return BossReactionGateResult.BlockedByClosedWindow;
                    }

                    return knockdownConfig.RuntimeSpec.EvaluateBossReactionGate(
                        attackType,
                        outcome,
                        StateElapsed);
                default:
                    return BossReactionGateResult.Allowed;
            }
        }

        /// <summary>
        /// 判断当前 HitStagger 是否已经到达可交回 Brain 的时间点；Skill HitReaction 额外保证至少 0.95 秒锁定。
        /// </summary>
        /// <returns>true 表示来源对应的最早返回时间已到且没有 PerfectGuard 恢复锁；false 表示仍保持 HitStagger。</returns>
        private bool CanReturnFromReaction()
        {
            if (CurrentState != BossStateId.HitStagger)
            {
                return false;
            }

            if (!runtimeConfigResolver.TryGetHitStaggerConfig(out CombatTimelineReactionConfig config) ||
                config.RuntimeSpec == null)
            {
                return false;
            }

            float canReturnTime = config.CanReturnTime;
            if (lastReceivedAttackType == CombatAttackType.SkillAttack &&
                lastReceivedHitOutcome == CombatHitOutcome.HitReaction)
            {
                canReturnTime = Mathf.Max(canReturnTime, SkillHitStaggerMinimumReturnTime);
            }

            if (StateElapsed < canReturnTime)
            {
                return false;
            }

            // 完美防御触发 HitJustParry 时，仍等待当前被打断攻击的自然后摇与破解奖励，避免 Boss 反而更早恢复。
            if (lastReceivedHitOutcome == CombatHitOutcome.PerfectGuard &&
                IsRecoveryLocked(Time.time))
            {
                return false;
            }

            return true;
        }

        private bool TryRejectHitStaggerRefreshBySequenceLimit(BossReactionDecision decision)
        {
            if (!UsesHitStaggerRefreshBudget(decision))
            {
                return false;
            }

            if (decisionMemory.CanRecordReactionHitInSequence(
                    Time.time,
                    ReactionHitSequenceWindow,
                    MaxReactionHitsPerSequence))
            {
                return false;
            }

            lastPlayerHitInterrupted = false;
            lastPlayerHitBlockedByBossGate = false;
            lastPlayerHitRejectReason = HitStaggerRefreshLimitRejectReason;
            decisionMemory.MarkReactionHitSequenceLimitReached(
                Time.time,
                ReactionHitSequenceWindow);
            reactionCounterRequested = true;
            return true;
        }

        private bool UsesHitStaggerRefreshBudget(BossReactionDecision decision)
        {
            return CurrentState == BossStateId.HitStagger &&
                decision.Id == BossReactionDecisionId.HitStagger &&
                lastReceivedHitOutcome == CombatHitOutcome.HitReaction &&
                IsNormalPlayerAttack(lastReceivedAttackType);
        }

        private void RecordReactionHitSequenceIfNeeded(BossStateId previousState, BossReactionDecision decision)
        {
            if (decision.Id != BossReactionDecisionId.HitStagger ||
                lastReceivedHitOutcome != CombatHitOutcome.HitReaction ||
                !IsNormalPlayerAttack(lastReceivedAttackType))
            {
                return;
            }

            decisionMemory.RecordReactionHit(
                Time.time,
                ReactionHitSequenceWindow);
            evaluatedReactionCounterHitCount = 0;
            reactionCounterRequested = false;
        }

        /// <summary>每个受击计数只在普通硬直允许返回时抽取一次 25% / 60% / 100% 受击反制评分。</summary>
        private void EvaluateReactionCounterOnce()
        {
            // 如果受击次数计算的评分达到上限，也即不再允许受击，此时玩家攻击命中不会重新刷新受击状态时间，会很快到到可以CanReturn 的时间，从而满足下面的条件，开始重新选招
            if (CurrentState != BossStateId.HitStagger ||
                !CanReturnFromReaction())
            {
                return;
            }

            int hitCount = decisionMemory.RecentReactionHitCount;
            if (hitCount <= 0 || evaluatedReactionCounterHitCount == hitCount)
            {
                return;
            }

            evaluatedReactionCounterHitCount = hitCount;
            BossAiTuning tuning = actionSet != null ? actionSet.Tuning : new BossAiTuning();
            float chance = hitCount >= 3
                ? tuning.ThirdHitCounterChance
                : hitCount == 2 ? tuning.SecondHitCounterChance : tuning.FirstHitCounterChance;
            reactionCounterRequested = Random.value <= Mathf.Clamp01(chance);
        }

        private bool ShouldCommitReactionCounter(BossBrainDecision decision)
        {
            return CurrentState == BossStateId.HitStagger &&
                CanReturnFromReaction() &&
                reactionCounterRequested &&
                decision.Id == BossBrainDecisionId.StartAction;
        }

        /// <summary>从 HitStagger 交回 Brain 并提交动作或中立后，清掉本次普通受击反制抽签结果。</summary>
        /// <param name="shouldClear">true 表示本帧已经从 HitStagger 提交了下一步战术决策。</param>
        private void ClearReactionCounterRequestIfNeeded(bool shouldClear)
        {
            if (!shouldClear)
            {
                return;
            }

            evaluatedReactionCounterHitCount = 0;
            reactionCounterRequested = false;
        }

        /// <summary>
        /// 记录一次非攻击中立行为；压力只由每秒自然衰减降低。
        /// </summary>
        private void CommitTempoReset()
        {
            if (decisionMemory.ConsecutiveAttackActionCount <= 0 && !decisionMemory.TempoResetRequired)
            {
                return;
            }

            decisionMemory.RecordTempoReset();
        }

        private static bool IsNormalPlayerAttack(CombatAttackType attackType)
        {
            return attackType == CombatAttackType.LightAttack ||
                attackType == CombatAttackType.HeavyAttack;
        }

        private void CommitReactionRuntimeStop()
        {
            if (actionRunner.IsRunning)
            {
                actionRunner.EndAction();
            }

            movementSystem.StopAllMotion();
        }

        /// <summary>
        /// 接收显式状态切换请求，并统一仲裁同状态刷新和真实状态切换。
        /// </summary>
        /// <param name="reason">本次切换请求的来源语义。</param>
        /// <param name="nextState">请求进入的 Boss 叶子状态。</param>
        /// <param name="playStateAnimation">是否播放目标状态的通用状态动画。</param>
        private void RequestStateTransition(
            BossStateTransitionReason reason,
            BossStateId nextState,
            bool playStateAnimation = true)
        {
            RequestStateTransition(new BossStateTransitionRequest(reason, nextState, playStateAnimation));
        }

        /// <summary>
        /// 接收显式状态切换请求，并统一仲裁同状态刷新和真实状态切换。
        /// </summary>
        /// <param name="request">包含来源理由、目标状态和动画播放策略的切换请求。</param>
        private void RequestStateTransition(BossStateTransitionRequest request)
        {
            if (request.Reason == BossStateTransitionReason.ReactionSameStateRefresh)
            {
                stateMachine.ResetStateElapsed();
                return;
            }

            CommitStateTransition(request.NextState, request.PlayStateAnimation);
        }

        /// <summary>
        /// 提交顶层状态切换，并执行必要的 ActionRunner、Movement、Strafe 和动画副作用。
        /// </summary>
        private void CommitStateTransition(BossStateId nextState, bool playStateAnimation = true)
        {
            if (!stateMachine.TransitionTo(nextState, out BossStateId previousState))
            {
                return;
            }

            CommitStateTransitionSideEffects(previousState, nextState, playStateAnimation);

            if (logStateChanges)
            {
                Debug.Log($"Boss state -> {nextState}", this);
            }
        }

        /// <summary>
        /// 提交状态切换副作用。状态事实由 BossStateMachine 维护，系统调用由 BossActor 统一提交。
        /// </summary>
        private void CommitStateTransitionSideEffects(
            BossStateId previousState,
            BossStateId nextState,
            bool playStateAnimation)
        {
            HandleShieldDefenseTransition(previousState, nextState);
            UpdateShieldBreakStunPresentation(previousState, nextState);

            if ((previousState == BossStateId.Action || previousState == BossStateId.Recovery) &&
                nextState != BossStateId.Action &&
                nextState != BossStateId.Recovery &&
                actionRunner.IsRunning)
            {
                if (actionRunner.CurrentActionKind == BossActionKind.Attack)
                {
                    CompleteAttackVisualTail();
                }
                else
                {
                    actionRunner.EndAction();
                }
            }

            if (IsRepositionState(previousState))
            {
                movementSystem.EndReposition();
            }

            if (IsReactionState(previousState) && nextState != previousState)
            {
                movementSystem.EndReactionMotion();
            }

            if (ShouldStopAllMotionOnEnter(nextState))
            {
                movementSystem.StopAllMotion();
            }

            if (ShouldResetTacticalMemoryOnEnter(nextState))
            {
                decisionMemory.ResetTacticalState();
            }

            if (nextState == BossStateId.Strafe)
            {
                strafeDirection *= -1;
            }

            if (!playStateAnimation)
            {
                return;
            }

            if (nextState == BossStateId.Strafe)
            {
                animationBridge?.PlayStrafe(strafeDirection);
            }
            else
            {
                animationBridge?.PlayState(nextState);
            }
        }

        /// <summary>
        /// 在正式进入或离开 ShieldBreakStun 时重置动画阶段标记，并启动或清理三阶段视觉。
        /// </summary>
        /// <param name="previousState">本次切换前的 Boss 叶子状态。</param>
        /// <param name="nextState">本次切换后的 Boss 叶子状态。</param>
        private void UpdateShieldBreakStunPresentation(BossStateId previousState, BossStateId nextState)
        {
            if (previousState == BossStateId.ShieldBreakStun && nextState != BossStateId.ShieldBreakStun)
            {
                shieldBreakWeakAnimationPlayed = false;
                shieldBreakStunVfx?.StopStun();
            }

            if (nextState == BossStateId.ShieldBreakStun && previousState != BossStateId.ShieldBreakStun)
            {
                shieldBreakWeakAnimationPlayed = false;
                shieldBreakStunVfx?.BeginStun();
            }
        }

        private static bool IsRepositionState(BossStateId state)
        {
            return state == BossStateId.Approach ||
                state == BossStateId.Strafe;
        }

        private static bool IsReactionState(BossStateId state)
        {
            return state == BossStateId.HitStagger ||
                state == BossStateId.Knockdown ||
                state == BossStateId.ShieldBreakStun;
        }

        private static bool ShouldStopAllMotionOnEnter(BossStateId state)
        {
            return state == BossStateId.None ||
                state == BossStateId.Dead ||
                state == BossStateId.HitStagger ||
                state == BossStateId.Knockdown ||
                state == BossStateId.ShieldBreakStun;
        }

        /// <summary>
        /// 按护盾眩晕退出方向管理回满时机；转入 Knockdown 时延迟到起身，其余异常退出立即回满。
        /// </summary>
        /// <param name="previousState">本次切换前的 Boss 叶子状态。</param>
        /// <param name="nextState">本次切换后的 Boss 叶子状态。</param>
        private void HandleShieldDefenseTransition(BossStateId previousState, BossStateId nextState)
        {
            if (previousState == BossStateId.ShieldBreakStun)
            {
                if (nextState == BossStateId.Knockdown)
                {
                    refillShieldAfterKnockdown = true;
                    return;
                }

                if (nextState != BossStateId.Dead)
                {
                    RefillShieldDefense();
                }
            }

            if (previousState == BossStateId.Knockdown && refillShieldAfterKnockdown)
            {
                if (nextState != BossStateId.Dead)
                {
                    RefillShieldDefense();
                }

                refillShieldAfterKnockdown = false;
            }
        }

        /// <summary>把 Boss 独立防御护盾恢复到 Inspector 配置上限。</summary>
        private void RefillShieldDefense()
        {
            currentShieldDefense = Mathf.Max(1, maxShieldDefense);
        }

        private static bool ShouldResetTacticalMemoryOnEnter(BossStateId state)
        {
            return state == BossStateId.None ||
                state == BossStateId.Dead;
        }

        /// <summary>
        /// 按 Boss 当前血量解析软阶段，阶段只影响 Brain 选招资格和权重。
        /// </summary>
        /// <returns>当前 Boss 战斗软阶段；缺少资源组件时返回 Phase1。</returns>
        private BossCombatPhaseId ResolveCombatPhase()
        {
            if (resourceComponent == null || resourceComponent.MaxHp <= 0f)
            {
                return BossCombatPhaseId.Phase1;
            }

            float hpRatio = Mathf.Clamp01(resourceComponent.CurrentHp / resourceComponent.MaxHp);
            if (hpRatio <= 0.25f)
            {
                return BossCombatPhaseId.Desperation;
            }

            if (hpRatio <= 0.6f)
            {
                return BossCombatPhaseId.Phase2;
            }

            return BossCombatPhaseId.Phase1;
        }

        /// <summary>按动作 ID 与执行类别查找已启用的 ActionSet 条目。</summary>
        /// <param name="actionId">Timeline 动作 ID。</param>
        /// <param name="kind">要求匹配的正式执行类别。</param>
        /// <param name="entry">找到时写回动作配置。</param>
        /// <returns>true 表示存在同 ID、同类别且已启用的条目。</returns>
        private bool TryFindActionSetEntry(string actionId, BossActionKind kind, out BossActionSetEntry entry)
        {
            if (actionSet == null || string.IsNullOrEmpty(actionId))
            {
                entry = default;
                return false;
            }

            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry candidate = actionSet.Actions[i];
                if (candidate.Kind != kind ||
                    !candidate.Enabled ||
                    candidate.ActionId != actionId)
                {
                    continue;
                }

                entry = candidate;
                return true;
            }

            entry = default;
            return false;
        }

        private static bool AllowsActorFacingRotation(BossStateId state)
        {
            return state == BossStateId.Idle ||
                state == BossStateId.Approach ||
                state == BossStateId.Strafe;
        }

        private float CurrentTargetDistance
        {
            get
            {
                if (target == null)
                {
                    return float.MaxValue;
                }

                Vector3 offset = target.position - transform.position;
                offset.y = 0f;
                return offset.magnitude;
            }
        }

        /// <summary>
        /// 执行 Direction / To / Target 相关逻辑，并维护 Boss Actor 模块的运行时一致性。
        /// </summary>
        private Vector3 DirectionToTarget()
        {
            if (target == null)
            {
                return transform.forward;
            }

            Vector3 direction = target.position - transform.position;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : transform.forward;
        }

        /// <summary>
        /// 解析 Received / Hit / Direction 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private BossHitDirectionId ResolveReceivedHitDirection(in CombatHitData hit, Component source)
        {
            Vector3 attackSourceOffset = Vector3.zero;
            if (source != null)
            {
                attackSourceOffset = source.transform.position - transform.position;
            }

            attackSourceOffset.y = 0f;
            if (attackSourceOffset.sqrMagnitude <= 0.0001f && hit.HitDirection.sqrMagnitude > 0.0001f)
            {
                attackSourceOffset = -hit.HitDirection;
                attackSourceOffset.y = 0f;
            }

            if (attackSourceOffset.sqrMagnitude <= 0.0001f && target != null)
            {
                attackSourceOffset = target.position - transform.position;
                attackSourceOffset.y = 0f;
            }

            if (attackSourceOffset.sqrMagnitude <= 0.0001f)
            {
                return BossHitDirectionId.Front;
            }

            Vector3 direction = attackSourceOffset.normalized;
            float forwardDot = Vector3.Dot(transform.forward, direction);
            float rightDot = Vector3.Dot(transform.right, direction);
            if (Mathf.Abs(rightDot) > Mathf.Abs(forwardDot))
            {
                return rightDot >= 0f ? BossHitDirectionId.Right : BossHitDirectionId.Left;
            }

            return forwardDot >= 0f ? BossHitDirectionId.Front : BossHitDirectionId.Back;
        }

        /// <summary>
        /// 执行 Angle / To / Target 相关逻辑，并维护 Boss Actor 模块的运行时一致性。
        /// </summary>
        private float AngleToTarget()
        {
            return Vector3.Angle(transform.forward, DirectionToTarget());
        }

        /// <summary>
        /// 执行 Rotate / To / Target 相关逻辑，并维护 Boss Actor 模块的运行时一致性。
        /// </summary>
        private void RotateToTarget(float deltaTime)
        {
            Vector3 direction = DirectionToTarget();
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * deltaTime);
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (resourceComponent == null)
            {
                resourceComponent = GetComponent<CombatResourceComponent>();
            }

            if (attackExecutor == null)
            {
                attackExecutor = GetComponent<BossAttackExecutor>();
            }

            if (animationBridge == null)
            {
                animationBridge = GetComponent<BossAnimationBridge>();
            }

            if (motionController == null)
            {
                motionController = GetComponent<BossMotionController>();
            }

            movementSystem.Bind(motionController);
            actionRunner.Bind(attackExecutor, movementSystem, animationBridge);

            if (targetReceiver == null)
            {
                targetReceiver = FindFirstObjectByType<PlayerCombatReceiver>();
            }

            if (target == null && targetReceiver != null)
            {
                target = targetReceiver.transform;
            }

            targetStateMachine = targetReceiver != null
                ? targetReceiver.GetComponentInParent<PlayerStateMachine>()
                : null;
        }

    }
}
