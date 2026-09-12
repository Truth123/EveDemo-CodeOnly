// 文件说明：根据 BossBlackboard 生成正式 Boss 高层意图与战术决策。
// 所属模块：Boss Brain。
// 运行影响：只读评估 Boss 顶层意图和战术选择，不驱动动画、移动或命中。

using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.AI;
using ProjectEVE.Player;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Brain
{
    /// <summary>
    /// Boss Brain 的高层意图。它只表示决策方向，不代表正式状态切换或具体招式执行。
    /// </summary>
    public enum BossBrainIntentId
    {
        /// <summary>无有效意图。</summary>
        None = 0,
        /// <summary>保持或进入空闲评估。</summary>
        Idle = 1,
        /// <summary>可以进入攻击决策阶段，但尚未选择具体招式。</summary>
        Attack = 2,
        /// <summary>需要进行非攻击空间调整。</summary>
        Reposition = 3,
        /// <summary>继续当前已承诺动作。</summary>
        ContinueAction = 4,
        /// <summary>继续或进入受击反应流程。</summary>
        React = 5
    }

    /// <summary>
    /// Boss Brain 输出的具体战术决策。Actor 负责把这些决策提交给状态机、移动和动作系统。
    /// </summary>
    public enum BossBrainDecisionId
    {
        /// <summary>无有效战术决策。</summary>
        None = 0,
        /// <summary>启动携带显式 BossActionKind 的 Action。</summary>
        StartAction = 1,
        /// <summary>靠近目标。</summary>
        MoveApproach = 3,
        /// <summary>横移试探。</summary>
        MoveStrafe = 4,
        /// <summary>继续当前已承诺动作或反应生命周期。</summary>
        ContinueCommittedState = 7
    }

    /// <summary>
    /// Boss Brain 单次战术决策所需的运行时事实。Actor 负责从状态、记忆和玩家快照组装这些输入。
    /// </summary>
    public readonly struct BossBrainDecisionInput
    {
        private static readonly IReadOnlyList<string> EmptyRecentValues = Array.Empty<string>();

        /// <summary>创建一次无副作用 Brain 决策所需的阶段、压力、受击反制许可和玩家战术事实。</summary>
        /// <param name="canUseReactionCounter">本次普通受击反制评分已通过，允许 HitStagger 返回后选择反制动作。</param>
        /// <param name="currentPhase">当前 Boss 软阶段。</param>
        /// <param name="tempoPressure">当前主动进攻压力，范围 0–100。</param>
        /// <param name="pressureDecayMode">当前阶段压力是否处于带滞回的降压模式。</param>
        /// <param name="consecutiveAttackActionCount">未穿插合格中立行为的连续攻击次数。</param>
        /// <param name="tempoResetRequired">上一动作是否要求先完成一次中立行为。</param>
        /// <param name="recentActionIds">最近正式启动的动作 ID，最新项在前。</param>
        /// <param name="playerTacticalSnapshot">玩家当前公开状态与滚动行为记忆快照。</param>
        public BossBrainDecisionInput(
            bool canUseReactionCounter,
            BossCombatPhaseId currentPhase,
            float tempoPressure,
            bool pressureDecayMode,
            int consecutiveAttackActionCount,
            bool tempoResetRequired,
            IReadOnlyList<string> recentActionIds,
            BossPlayerTacticalSnapshot playerTacticalSnapshot)
        {
            CanUseReactionCounter = canUseReactionCounter;
            CurrentPhase = currentPhase;
            TempoPressure = Mathf.Clamp(tempoPressure, 0f, 100f);
            PressureDecayMode = pressureDecayMode;
            ConsecutiveAttackActionCount = Mathf.Max(0, consecutiveAttackActionCount);
            TempoResetRequired = tempoResetRequired;
            RecentActionIds = recentActionIds ?? EmptyRecentValues;
            PlayerTacticalSnapshot = playerTacticalSnapshot;
        }

        /// <summary>当前是否允许 HitStagger 返回后选择任一未禁止受击上下文的反制动作。</summary>
        public bool CanUseReactionCounter { get; }
        /// <summary>当前 Boss 软阶段。</summary>
        public BossCombatPhaseId CurrentPhase { get; }
        /// <summary>当前主动进攻压力，范围 0 - 100。</summary>
        public float TempoPressure { get; }
        /// <summary>当前压力是否处于带进入/退出阈值滞回的降压模式。</summary>
        public bool PressureDecayMode { get; }
        /// <summary>当前连续成功启动的攻击动作次数。</summary>
        public int ConsecutiveAttackActionCount { get; }
        /// <summary>是否必须先提交一次节奏恢复行为。</summary>
        public bool TempoResetRequired { get; }
        /// <summary>最近正式启动的 Action ID，Attack 与 Reposition 都记录，最新项在前。</summary>
        public IReadOnlyList<string> RecentActionIds { get; }
        /// <summary>玩家已经进入的可见状态和滚动行为记忆。</summary>
        public BossPlayerTacticalSnapshot PlayerTacticalSnapshot { get; }
    }

    /// <summary>
    /// Boss Brain 战术决策结果。只有 StartAction 会携带动作定义和显式类别。
    /// </summary>
    public readonly struct BossBrainDecision
    {
        /// <summary>无操作决策。</summary>
        public static readonly BossBrainDecision None = new BossBrainDecision(BossBrainDecisionId.None, null, BossActionKind.Attack);
        /// <summary>靠近目标。</summary>
        public static readonly BossBrainDecision MoveApproach = new BossBrainDecision(BossBrainDecisionId.MoveApproach, null, BossActionKind.Attack);
        /// <summary>横移试探。</summary>
        public static readonly BossBrainDecision MoveStrafe = new BossBrainDecision(BossBrainDecisionId.MoveStrafe, null, BossActionKind.Attack);
        /// <summary>继续当前已承诺状态。</summary>
        public static readonly BossBrainDecision ContinueCommittedState = new BossBrainDecision(BossBrainDecisionId.ContinueCommittedState, null, BossActionKind.Attack);

        private BossBrainDecision(
            BossBrainDecisionId id,
            BossAttackDefinition actionDefinition,
            BossActionKind actionKind)
        {
            Id = id;
            ActionDefinition = actionDefinition;
            ActionKind = actionKind;
        }

        /// <summary>战术决策类型。</summary>
        public BossBrainDecisionId Id { get; }
        /// <summary>StartAction 决策携带的 Timeline 运行时定义。</summary>
        public BossAttackDefinition ActionDefinition { get; }
        /// <summary>StartAction 决策携带的显式动作类别。</summary>
        public BossActionKind ActionKind { get; }

        /// <summary>创建启动 Boss Action 的决策。</summary>
        /// <param name="actionDefinition">所选动作的 Timeline 运行时定义。</param>
        /// <param name="actionKind">所选 ActionSet 条目的显式动作类别。</param>
        /// <returns>定义有效时返回 StartAction；否则返回 None。</returns>
        public static BossBrainDecision StartAction(
            BossAttackDefinition actionDefinition,
            BossActionKind actionKind)
        {
            return actionDefinition != null
                ? new BossBrainDecision(BossBrainDecisionId.StartAction, actionDefinition, actionKind)
                : None;
        }
    }

    /// <summary>
    /// Boss 正式 Brain 的最小版本。当前根据唯一 Boss 状态和目标事实生成高层意图。
    /// </summary>
    public static class BossBrain
    {
        private const float CloseNeutralDistance = 3.5f;
        private const float FarNeutralDistance = 9f;

        /// <summary>
        /// 根据 Blackboard 评估 Boss 高层意图。该方法无副作用，不提交状态切换或动作执行。
        /// </summary>
        public static BossBrainIntentId Evaluate(BossBlackboard blackboard)
        {
            BossStateId state = blackboard.CurrentState;
            if (blackboard.IsActionExecuting || state == BossStateId.Action || state == BossStateId.Recovery)
            {
                return state == BossStateId.Recovery && !blackboard.IsActionExecuting
                    ? BossBrainIntentId.Attack
                    : BossBrainIntentId.ContinueAction;
            }

            if (state == BossStateId.HitStagger ||
                state == BossStateId.Knockdown ||
                state == BossStateId.ShieldBreakStun)
            {
                return blackboard.CanReturnFromReaction && blackboard.HasTarget
                    ? BossBrainIntentId.Attack
                    : BossBrainIntentId.React;
            }

            if (state == BossStateId.Approach || state == BossStateId.Strafe)
            {
                return BossBrainIntentId.Reposition;
            }

            if (state == BossStateId.None || state == BossStateId.Dead)
            {
                return BossBrainIntentId.None;
            }

            if (!blackboard.HasTarget)
            {
                return BossBrainIntentId.Idle;
            }

            if (state == BossStateId.Idle)
            {
                return BossBrainIntentId.Attack;
            }

            return BossBrainIntentId.Idle;
        }

        /// <summary>
        /// 从 Blackboard 到具体动作选择的正式 Brain 入口。该方法只读评估，不提交冷却或启动动作。
        /// </summary>
        public static BossAttackDefinition SelectAction(
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now)
        {
            BossBrainIntentId intent = Evaluate(blackboard);
            return BossActionSelector.Select(intent, blackboard, actor, actionSet, now);
        }

        /// <summary>
        /// 基于 Blackboard 与阈值生成正式战术决策。该方法不读取配置资产，也不提交状态或动作副作用。
        /// </summary>
        /// <param name="blackboard">当前 Boss 状态、目标和动作执行事实。</param>
        /// <param name="input">阶段、压力、反应与玩家战术事实。</param>
        /// <param name="actor">提供招式冷却、角度和 Motion 可行性只读查询的 BossActor。</param>
        /// <param name="actionSet">动作池、压力策略与 AI 调参的数据源。</param>
        /// <param name="now">当前 Unity 运行时间，单位秒。</param>
        /// <param name="random01">可选的 0–1 随机源；测试使用确定性值。</param>
        /// <returns>Actor 本帧应提交的攻击、Reposition 或继续状态决策。</returns>
        public static BossBrainDecision Decide(
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossActionSelectionRandom01 random01 = null)
        {
            BossBrainIntentId intent = Evaluate(blackboard);
            if (intent == BossBrainIntentId.ContinueAction || intent == BossBrainIntentId.React || intent == BossBrainIntentId.None)
            {
                return BossBrainDecision.ContinueCommittedState;
            }

            if (!blackboard.HasTarget)
            {
                return BossBrainDecision.ContinueCommittedState;
            }

            BossAiTuning tuning = actionSet != null ? actionSet.Tuning : new BossAiTuning();
            if ((blackboard.CurrentState == BossStateId.Approach || blackboard.CurrentState == BossStateId.Strafe) &&
                blackboard.StateElapsed < tuning.NeutralMinDuration)
            {
                return blackboard.CurrentState == BossStateId.Approach
                    ? BossBrainDecision.MoveApproach
                    : BossBrainDecision.MoveStrafe;
            }

            BossPhasePressureProfile pressureProfile = actionSet != null
                ? actionSet.GetPressureProfile(input.CurrentPhase)
                : BossPhasePressureProfile.CreateDefault(input.CurrentPhase);
            
            bool pressureDecayMode = input.PressureDecayMode;
            
            bool forceNeutral = ShouldRecoverTempo(input, pressureProfile);
            
            bool? gapCloseBlockedOnlyByShortCooldown = null;
            
            bool actionScanFoundGapCloseBlockedOnlyByShortCooldown = false;

            if (!forceNeutral &&
                BossActionSelector.TrySelectAction(
                    BossBrainIntentId.Attack,
                    blackboard,
                    actor,
                    actionSet,
                    now,
                    input,
                    pressureProfile,
                    pressureDecayMode,
                    out BossAttackDefinition selectedAction,
                    out BossActionKind selectedKind,
                    out actionScanFoundGapCloseBlockedOnlyByShortCooldown,
                    random01))
            {
                return BossBrainDecision.StartAction(selectedAction, selectedKind);
            }

            if (!forceNeutral)
            {
                gapCloseBlockedOnlyByShortCooldown = actionScanFoundGapCloseBlockedOnlyByShortCooldown;
            }

            return SelectNeutralDecision(
                blackboard,
                input,
                actor,
                actionSet,
                now,
                gapCloseBlockedOnlyByShortCooldown,
                pressureProfile,
                pressureDecayMode,
                random01);
        }

        /// <summary>判断本次战术评估是否必须先进入普通中立行为而不能检查攻击池。</summary>
        /// <param name="input">当前阶段、压力、连续攻击和玩家公开状态事实。</param>
        /// <param name="pressureProfile">当前阶段的压力硬上限和连续攻击上限。</param>
        /// <returns>true 表示本帧必须选择 Approach 或 Strafe；false 表示可以继续检查动作池。</returns>
        private static bool ShouldRecoverTempo(BossBrainDecisionInput input, BossPhasePressureProfile pressureProfile)
        {
            BossPlayerTacticalSnapshot player = input.PlayerTacticalSnapshot;
            if (player.CurrentState == PlayerStateId.Knockdown)
            {
                return true;
            }

            if (input.TempoResetRequired)
            {
                return true;
            }

            if (input.TempoPressure >= pressureProfile.HardLimit)
            {
                return true;
            }

            return input.ConsecutiveAttackActionCount >= Mathf.Max(1, pressureProfile.MaxConsecutiveAttacks);
        }

        /// <summary>按文档距离分支和当前中立状态选择 Approach 或 Strafe。</summary>
        /// <param name="blackboard">当前距离与 Boss 状态。</param>
        /// <param name="input">玩家持续后退和阶段压力事实。</param>
        /// <param name="actor">提供 GapClose 冷却外硬条件检查的 Actor。</param>
        /// <param name="actionSet">提供 GapClose 池与短冷却阈值的动作集。</param>
        /// <param name="now">当前 Unity 运行时间，单位秒。</param>
        /// <param name="gapCloseBlockedOnlyByShortCooldown">同一次动作扫描已得到的短冷却结果；为 null 时本函数按需检查。</param>
        /// <param name="pressureProfile">当前阶段压力配置，传入以避免本帧重复读取。</param>
        /// <param name="pressureDecayMode">当前压力是否已进入降压模式。</param>
        /// <param name="random01">中距离普通 1:1 选择使用的可选随机源。</param>
        /// <returns>只可能是 MoveApproach 或 MoveStrafe。</returns>
        private static BossBrainDecision SelectNeutralDecision(
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            bool? gapCloseBlockedOnlyByShortCooldown,
            BossPhasePressureProfile pressureProfile,
            bool pressureDecayMode,
            BossActionSelectionRandom01 random01)
        {
            float distance = blackboard.DistanceToTarget;
            if (blackboard.CurrentState == BossStateId.Strafe)
            {
                return distance > CloseNeutralDistance && input.PlayerTacticalSnapshot.IsSustainedRetreat
                    ? BossBrainDecision.MoveApproach
                    : BossBrainDecision.MoveStrafe;
            }

            if (distance > FarNeutralDistance)
            {
                return BossBrainDecision.MoveApproach;
            }

            if (distance <= CloseNeutralDistance)
            {
                return BossBrainDecision.MoveStrafe;
            }

            bool waitForGapClose = gapCloseBlockedOnlyByShortCooldown ??
                BossActionSelector.HasGapCloseBlockedOnlyByShortCooldown(
                    blackboard,
                    input,
                    actor,
                    actionSet,
                    now,
                    pressureProfile,
                    pressureDecayMode);
            if (waitForGapClose)
            {
                return BossBrainDecision.MoveStrafe;
            }

            if (input.PlayerTacticalSnapshot.IsSustainedRetreat)
            {
                return BossBrainDecision.MoveApproach;
            }

            float roll = Mathf.Clamp01(random01 != null ? random01() : UnityEngine.Random.value);
            return roll < 0.5f ? BossBrainDecision.MoveApproach : BossBrainDecision.MoveStrafe;
        }
    }
}
