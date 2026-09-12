// 文件说明：集中 Boss 被玩家命中后的反应优先级决策、执行计划和 Knockdown 阶段计划。
// 所属模块：Boss 反应系统。
// 运行影响：决定 Boss 是否进入死亡、站立硬直、击倒或 PerfectGuard 反制，并生成无副作用执行计划。

using ProjectEVE.Boss.AI;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using System;

namespace ProjectEVE.Boss.Reaction
{
    /// <summary>
    /// Boss 反应决策结果。只表达正式流程要执行的反应类型，不保存调试候选或评分。
    /// </summary>
    public enum BossReactionDecisionId
    {
        /// <summary>不进入新的 Boss 反应。</summary>
        None = 0,
        /// <summary>进入站立受击硬直。</summary>
        HitStagger = 1,
        /// <summary>进入 Skill 击倒流程。</summary>
        Knockdown = 2,
        /// <summary>进入 PerfectGuard 反制硬直。</summary>
        PerfectGuardStagger = 3,
        /// <summary>进入死亡流程。</summary>
        Dead = 4,
        /// <summary>护盾击破后立即进入长眩晕，并先播放 PerfectGuard 硬直动画。</summary>
        ShieldBreakStun = 5
    }

    /// <summary>
    /// 玩家命中 Boss 时用于决策的最小输入事实。
    /// </summary>
    public readonly struct BossPlayerHitReactionInput
    {
        /// <summary>创建玩家命中 Boss 后的反应判定输入。</summary>
        /// <param name="currentState">Boss 当前叶子状态。</param>
        /// <param name="outcome">通用 Combat Resolver 已产生的命中结果。</param>
        /// <param name="resourceIsDead">Boss 资源是否已在本次结算后死亡。</param>
        /// <param name="gateResult">当前 Attack 或 Reaction Timeline 的受击门控结果。</param>
        /// <param name="requiresReactionGate">true 表示当前是 Attack Action，需要服从攻击 Timeline gate；Reposition Action 传 false。</param>
        /// <param name="attackType">本次玩家攻击类型，用于限制护盾眩晕只能被 Skill Knockdown 提前结束。</param>
        public BossPlayerHitReactionInput(
            BossStateId currentState,
            CombatHitOutcome outcome,
            bool resourceIsDead,
            BossReactionGateResult gateResult,
            bool requiresReactionGate = false,
            CombatAttackType attackType = CombatAttackType.LightAttack)
        {
            CurrentState = currentState;
            Outcome = outcome;
            ResourceIsDead = resourceIsDead;
            GateResult = gateResult;
            RequiresReactionGate = requiresReactionGate;
            AttackType = attackType;
        }

        /// <summary>Boss 当前叶子状态。</summary>
        public BossStateId CurrentState { get; }
        /// <summary>通用命中解析结果。</summary>
        public CombatHitOutcome Outcome { get; }
        /// <summary>Boss 资源组件是否已经死亡。</summary>
        public bool ResourceIsDead { get; }
        /// <summary>当前状态 Timeline 对本次玩家命中的受击门控结果。</summary>
        public BossReactionGateResult GateResult { get; }
        /// <summary>当前 Action 是否为需要读取攻击 Timeline Gate 的战斗 Attack。</summary>
        public bool RequiresReactionGate { get; }
        /// <summary>本次玩家攻击类型。</summary>
        public CombatAttackType AttackType { get; }
    }

    /// <summary>
    /// Boss 反应决策输出。BossActor 根据它执行正式副作用。
    /// </summary>
    public readonly struct BossReactionDecision
    {
        private BossReactionDecision(BossReactionDecisionId id, bool interrupts, bool blockedByBossGate, string rejectReason)
        {
            Id = id;
            Interrupts = interrupts;
            BlockedByBossGate = blockedByBossGate;
            RejectReason = rejectReason ?? string.Empty;
        }

        /// <summary>本次反应类型。</summary>
        public BossReactionDecisionId Id { get; }
        /// <summary>本次决策是否会中断当前 Boss 行为。</summary>
        public bool Interrupts { get; }
        /// <summary>本次玩家命中是否被 Boss 受击门控中的 Block gate 阻止反应。</summary>
        public bool BlockedByBossGate { get; }
        /// <summary>正式运行时保留的拒绝原因。</summary>
        public string RejectReason { get; }

        /// <summary>创建一次会中断当前行为的决策。</summary>
        public static BossReactionDecision Interrupt(BossReactionDecisionId id, string rejectReason = "")
        {
            return new BossReactionDecision(id, true, false, rejectReason);
        }

        /// <summary>创建一次不进入反应的决策。</summary>
        public static BossReactionDecision Reject(string rejectReason, bool blockedByBossGate = false)
        {
            return new BossReactionDecision(BossReactionDecisionId.None, false, blockedByBossGate, rejectReason);
        }
    }

    /// <summary>
    /// Boss 反应执行计划中的动画类型。
    /// </summary>
    public enum BossReactionAnimationId
    {
        /// <summary>不播放反应动画。</summary>
        None = 0,
        /// <summary>播放普通受击动画。</summary>
        HitStagger = 1,
        /// <summary>播放 PerfectGuard 反制硬直动画。</summary>
        PerfectGuardStagger = 2,
        /// <summary>播放击倒起始动画。</summary>
        KnockdownStart = 3
    }

    /// <summary>
    /// Boss Knockdown Tick 后的状态提交方式。
    /// </summary>
    public enum BossKnockdownStateCommitId
    {
        /// <summary>不提交状态变化。</summary>
        None = 0,
        /// <summary>Knockdown 时间线完成后回到 Idle。</summary>
        TransitionToIdle = 1
    }

    /// <summary>
    /// Boss 反应执行计划。只描述 BossActor 要执行的副作用，不直接调用组件。
    /// </summary>
    public readonly struct BossReactionExecutionPlan
    {
        private BossReactionExecutionPlan(
            bool hasExecution,
            bool stopRuntime,
            BossStateId targetState,
            BossReactionAnimationId animation,
            BossHitDirectionId direction,
            bool resetKnockdownRuntime)
        {
            HasExecution = hasExecution;
            StopRuntime = stopRuntime;
            TargetState = targetState;
            Animation = animation;
            Direction = direction;
            ResetKnockdownRuntime = resetKnockdownRuntime;
        }

        /// <summary>空执行计划。</summary>
        public static readonly BossReactionExecutionPlan None = new BossReactionExecutionPlan(
            false,
            false,
            BossStateId.None,
            BossReactionAnimationId.None,
            BossHitDirectionId.None,
            false);

        /// <summary>是否需要执行反应副作用。</summary>
        public bool HasExecution { get; }
        /// <summary>是否停止当前动作和位移运行时。</summary>
        public bool StopRuntime { get; }
        /// <summary>反应目标状态。</summary>
        public BossStateId TargetState { get; }
        /// <summary>需要播放的反应动画类型。</summary>
        public BossReactionAnimationId Animation { get; }
        /// <summary>反应方向。</summary>
        public BossHitDirectionId Direction { get; }
        /// <summary>是否重置 Knockdown 阶段运行时标记。</summary>
        public bool ResetKnockdownRuntime { get; }
        /// <summary>提交执行前是否需要读取站立硬直 Timeline 配置。</summary>
        public bool RequiresHitStaggerConfig => TargetState == BossStateId.HitStagger;
        /// <summary>提交执行前是否需要读取击倒 Timeline 配置。</summary>
        public bool RequiresKnockdownConfig => TargetState == BossStateId.Knockdown;

        /// <summary>创建死亡执行计划。</summary>
        public static BossReactionExecutionPlan Dead()
        {
            return new BossReactionExecutionPlan(
                true,
                true,
                BossStateId.Dead,
                BossReactionAnimationId.None,
                BossHitDirectionId.None,
                false);
        }

        /// <summary>创建站立硬直执行计划。</summary>
        public static BossReactionExecutionPlan HitStagger(BossHitDirectionId direction, bool perfectGuardStagger)
        {
            return new BossReactionExecutionPlan(
                true,
                true,
                BossStateId.HitStagger,
                perfectGuardStagger ? BossReactionAnimationId.PerfectGuardStagger : BossReactionAnimationId.HitStagger,
                direction,
                false);
        }

        /// <summary>创建击倒执行计划。</summary>
        public static BossReactionExecutionPlan Knockdown(BossHitDirectionId direction)
        {
            return new BossReactionExecutionPlan(
                true,
                true,
                BossStateId.Knockdown,
                BossReactionAnimationId.KnockdownStart,
                direction,
                true);
        }

        /// <summary>创建停止当前运行时并立即进入防御护盾击破长眩晕的执行计划。</summary>
        /// <returns>目标状态为 ShieldBreakStun、初始动画为 PerfectGuardStagger 的执行计划。</returns>
        public static BossReactionExecutionPlan ShieldBreakStun()
        {
            return new BossReactionExecutionPlan(
                true,
                true,
                BossStateId.ShieldBreakStun,
                BossReactionAnimationId.PerfectGuardStagger,
                BossHitDirectionId.Front,
                false);
        }
    }

    /// <summary>
    /// Boss Knockdown 阶段 Tick 的最小输入事实。
    /// </summary>
    public readonly struct BossKnockdownTickInput
    {
        public BossKnockdownTickInput(
            float stateElapsed,
            float stunDuration,
            float recoveryStartTime,
            float totalDuration,
            bool loopPlayed,
            bool endPlayed)
        {
            StateElapsed = stateElapsed;
            StunDuration = stunDuration;
            RecoveryStartTime = recoveryStartTime;
            TotalDuration = totalDuration;
            LoopPlayed = loopPlayed;
            EndPlayed = endPlayed;
        }

        /// <summary>Knockdown 状态已经经过的时间。</summary>
        public float StateElapsed { get; }
        /// <summary>击倒起身前的眩晕持续时间。</summary>
        public float StunDuration { get; }
        /// <summary>击倒结束动画开始时间。</summary>
        public float RecoveryStartTime { get; }
        /// <summary>击倒反应总时长。</summary>
        public float TotalDuration { get; }
        /// <summary>Loop 动画是否已提交过。</summary>
        public bool LoopPlayed { get; }
        /// <summary>End 动画是否已提交过。</summary>
        public bool EndPlayed { get; }
    }

    /// <summary>
    /// Boss Knockdown 阶段 Tick 的执行计划。只描述副作用，不直接调用组件。
    /// </summary>
    public readonly struct BossKnockdownTickPlan
    {
        private BossKnockdownTickPlan(
            bool playLoopAnimation,
            bool playEndAnimation,
            bool markLoopPlayed,
            bool markEndPlayed,
            bool transitionToIdle)
        {
            PlayLoopAnimation = playLoopAnimation;
            PlayEndAnimation = playEndAnimation;
            MarkLoopPlayed = markLoopPlayed;
            MarkEndPlayed = markEndPlayed;
            StateCommit = transitionToIdle
                ? BossKnockdownStateCommitId.TransitionToIdle
                : BossKnockdownStateCommitId.None;
        }

        /// <summary>无操作计划。</summary>
        public static readonly BossKnockdownTickPlan None = new BossKnockdownTickPlan(false, false, false, false, false);

        /// <summary>是否播放 Knockdown Loop 动画。</summary>
        public bool PlayLoopAnimation { get; }
        /// <summary>是否播放 Knockdown End 动画。</summary>
        public bool PlayEndAnimation { get; }
        /// <summary>是否标记 Loop 动画已提交。</summary>
        public bool MarkLoopPlayed { get; }
        /// <summary>是否标记 End 动画已提交。</summary>
        public bool MarkEndPlayed { get; }
        /// <summary>是否切回 Idle。</summary>
        public bool TransitionToIdle => StateCommit == BossKnockdownStateCommitId.TransitionToIdle;
        /// <summary>Knockdown 阶段后的状态提交方式。</summary>
        public BossKnockdownStateCommitId StateCommit { get; }

        /// <summary>创建 Knockdown 阶段 Tick 计划。</summary>
        public static BossKnockdownTickPlan Create(
            bool playLoopAnimation,
            bool playEndAnimation,
            bool transitionToIdle)
        {
            return new BossKnockdownTickPlan(
                playLoopAnimation,
                playEndAnimation,
                playLoopAnimation,
                playEndAnimation,
                transitionToIdle);
        }
    }

    /// <summary>
    /// Boss 被玩家命中后的最小正式反应决策系统。它只判断优先级，不执行任何副作用。
    /// </summary>
    public static class BossReactionSystem
    {
        /// <summary>
        /// 根据玩家命中结果决定 Boss 应进入的正式反应。
        /// </summary>
        public static BossReactionDecision DecidePlayerHit(
            in BossPlayerHitReactionInput input,
            Func<bool> hasHitStaggerConfig,
            Func<bool> hasKnockdownConfig)
        {
            if (input.CurrentState == BossStateId.Dead)
            {
                return BossReactionDecision.Reject("AlreadyDead");
            }

            if (input.Outcome == CombatHitOutcome.Dead || input.ResourceIsDead)
            {
                return BossReactionDecision.Interrupt(BossReactionDecisionId.Dead);
            }

            if (input.CurrentState == BossStateId.ShieldBreakStun)
            {
                if (input.AttackType == CombatAttackType.SkillAttack && input.Outcome == CombatHitOutcome.Knockdown)
                {
                    if (hasKnockdownConfig == null || !hasKnockdownConfig())
                    {
                        return BossReactionDecision.Reject("MissingBossKnockdownTimeline");
                    }

                    return BossReactionDecision.Interrupt(BossReactionDecisionId.Knockdown);
                }

                return BossReactionDecision.Reject("ShieldBreakStunKeepsCurrentState");
            }

            if (IsNonReactionOutcome(input.Outcome))
            {
                return BossReactionDecision.Reject($"IgnoredOutcome:{input.Outcome}");
            }

            if (input.RequiresReactionGate || RequiresReactionGate(input.CurrentState))
            {
                BossReactionDecision gateDecision = DecideGate(input.GateResult);
                if (gateDecision.Id != BossReactionDecisionId.None || !string.IsNullOrEmpty(gateDecision.RejectReason))
                {
                    return gateDecision;
                }
            }

            switch (input.Outcome)
            {
                case CombatHitOutcome.Knockdown:
                    if (hasKnockdownConfig == null || !hasKnockdownConfig())
                    {
                        return BossReactionDecision.Reject("MissingBossKnockdownTimeline");
                    }

                    return BossReactionDecision.Interrupt(BossReactionDecisionId.Knockdown);
                case CombatHitOutcome.HitReaction:
                    if (hasHitStaggerConfig == null || !hasHitStaggerConfig())
                    {
                        return BossReactionDecision.Reject("MissingBossHitStaggerTimeline");
                    }

                    return BossReactionDecision.Interrupt(BossReactionDecisionId.HitStagger);
                default:
                    return BossReactionDecision.Reject($"IgnoredOutcome:{input.Outcome}");
            }
        }

        /// <summary>
        /// 根据 Boss HitNode 被玩家 PerfectGuard 的事实决定进入短硬直、护盾击破眩晕或保持当前状态。
        /// </summary>
        /// <param name="currentState">Boss 当前叶子状态。</param>
        /// <param name="hitNodeTriggersBossStagger">当前 HitNode 是否允许在护盾未破时触发短硬直。</param>
        /// <param name="shieldBroken">本次 PerfectGuard 扣除后护盾是否归零。</param>
        /// <param name="hasHitStaggerConfig">按需检查短硬直 Timeline 是否存在的函数。</param>
        /// <returns>护盾归零时返回 ShieldBreakStun；否则按 HitNode flag 和配置返回短硬直或拒绝结果。</returns>
        public static BossReactionDecision DecidePerfectGuardBossResponse(
            BossStateId currentState,
            bool hitNodeTriggersBossStagger,
            bool shieldBroken,
            Func<bool> hasHitStaggerConfig)
        {
            if (currentState == BossStateId.Dead)
            {
                return BossReactionDecision.Reject("AlreadyDead");
            }

            if (shieldBroken)
            {
                return BossReactionDecision.Interrupt(
                    BossReactionDecisionId.ShieldBreakStun,
                    "BossShieldDefenseBroken");
            }

            if (!hitNodeTriggersBossStagger)
            {
                return BossReactionDecision.Reject("PerfectGuardHitNodeDoesNotStaggerBoss");
            }

            if (hasHitStaggerConfig == null || !hasHitStaggerConfig())
            {
                return BossReactionDecision.Reject("MissingBossHitStaggerTimeline");
            }

            return BossReactionDecision.Interrupt(BossReactionDecisionId.PerfectGuardStagger, "PerfectGuardBossStagger");
        }

        /// <summary>
        /// 根据反应决策生成正式执行计划。该方法不调用组件，不读取配置。
        /// </summary>
        public static BossReactionExecutionPlan CreateExecutionPlan(
            BossReactionDecision decision,
            BossHitDirectionId direction)
        {
            switch (decision.Id)
            {
                case BossReactionDecisionId.Dead:
                    return BossReactionExecutionPlan.Dead();
                case BossReactionDecisionId.HitStagger:
                    return BossReactionExecutionPlan.HitStagger(direction, false);
                case BossReactionDecisionId.PerfectGuardStagger:
                    return BossReactionExecutionPlan.HitStagger(direction, true);
                case BossReactionDecisionId.Knockdown:
                    return BossReactionExecutionPlan.Knockdown(direction);
                case BossReactionDecisionId.ShieldBreakStun:
                    return BossReactionExecutionPlan.ShieldBreakStun();
                default:
                    return BossReactionExecutionPlan.None;
            }
        }

        /// <summary>
        /// 根据 Knockdown 时间线和已提交阶段生成本帧 Knockdown 执行计划。
        /// </summary>
        public static BossKnockdownTickPlan EvaluateKnockdownTick(in BossKnockdownTickInput input)
        {
            bool playLoop = !input.LoopPlayed && input.StateElapsed >= input.StunDuration;
            bool playEnd = !input.EndPlayed && input.StateElapsed >= input.RecoveryStartTime;
            bool transitionToIdle = input.StateElapsed >= input.TotalDuration;

            if (!playLoop && !playEnd && !transitionToIdle)
            {
                return BossKnockdownTickPlan.None;
            }

            return BossKnockdownTickPlan.Create(playLoop, playEnd, transitionToIdle);
        }

        private static BossReactionDecision DecideGate(BossReactionGateResult gateResult)
        {
            switch (gateResult.Id)
            {
                case BossReactionGateResultId.Allowed:
                    return default;
                case BossReactionGateResultId.BlockedByBossReactionGate:
                    return BossReactionDecision.Reject("BlockedByBossReactionGate", true);
                case BossReactionGateResultId.BlockedByClosedWindow:
                default:
                    return BossReactionDecision.Reject("BossReactionGateClosed");
            }
        }

        private static bool RequiresReactionGate(BossStateId currentState)
        {
            return currentState == BossStateId.HitStagger ||
                currentState == BossStateId.Knockdown;
        }

        private static bool IsNonReactionOutcome(CombatHitOutcome outcome)
        {
            return outcome == CombatHitOutcome.None ||
                outcome == CombatHitOutcome.IgnoredByInvincible ||
                outcome == CombatHitOutcome.DamageOnly ||
                outcome == CombatHitOutcome.PerfectGuard ||
                outcome == CombatHitOutcome.GuardHit ||
                outcome == CombatHitOutcome.PerfectEvade;
        }
    }
}
