// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Player.Movement;
using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Knockdown 主干状态。作为强制状态，进入时清空动作缓存。
    /// </summary>
    public sealed class PlayerKnockdownState : PlayerStateBase
    {
        public const string TimelineId = "Player_Knockdown";
        private static bool missingTimelineLogged;
        private bool missingConfig;
        private CombatTimelineReactionConfig currentConfig;

        /// <summary>
        /// 创建 PlayerKnockdownState 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        public PlayerKnockdownState() : base(PlayerStateId.Knockdown)
        {
        }

        /// <summary>
        /// 执行 Enter 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Enter(PlayerStateContext context)
        {
            context.ClearActionWindowsAndBuffers();
            context.PrepareKnockdown();
            context.CurrentPhase = PlayerStatePhase.Start;
            missingConfig = !TryGetConfig(out CombatTimelineReactionConfig config);
            if (missingConfig)
            {
                currentConfig = default;
                context.CurrentPhase = PlayerStatePhase.Reset;
                return;
            }

            currentConfig = config;
            PlayerActionMotionId motionId = config.MotionId;
            if (motionId != PlayerActionMotionId.None)
            {
                context.RequestActionMotion(motionId, context.ResolveLastHitKnockbackDirection());
            }
        }

        /// <summary>
        /// 推进 Tick 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            if (missingConfig)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            CombatTimelineReactionConfig config = currentConfig;
            KnockdownFrameData frame = EvaluateFrame(config, context.StateElapsedTime);
            ApplyFrame(context, frame);

            PlayerStateId transition = TryCancelWindowTransition(context, frame);
            if (transition != PlayerStateId.None)
            {
                return transition;
            }

            return context.StateElapsedTime >= config.TotalDuration ? ReturnToIdleOrLocomotion(context) : PlayerStateId.None;
        }

        /// <summary>
        /// 执行 Exit 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Exit(PlayerStateContext context)
        {
            missingConfig = false;
            currentConfig = default;
            context.ClearActionWindows();
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 解析 Frame 数据，并把 Timeline Snapshot 转成 Knockdown 本地业务字段。
        /// </summary>
        private static KnockdownFrameData EvaluateFrame(CombatTimelineReactionConfig config, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = config.RuntimeSpec != null
                ? config.RuntimeSpec.Evaluate(elapsed)
                : default;
            PlayerStatePhase phase = snapshot.Phase != PlayerStatePhase.None
                ? snapshot.Phase
                : ResolveFallbackPhase(config, elapsed);

            return new KnockdownFrameData(
                phase,
                snapshot.CanCancelToEvade,
                snapshot.CanCancelToSkill,
                snapshot.CanResetAttack,
                snapshot.CanCancelToGuard,
                snapshot.CanMoveCancel);
        }

        /// <summary>
        /// 缺少 Phase Clip 时按 Reaction 窗口保留旧表现阶段，避免动画桥接丢失阶段事实。
        /// </summary>
        private static PlayerStatePhase ResolveFallbackPhase(CombatTimelineReactionConfig config, float elapsed)
        {
            if (elapsed < config.StunDuration)
            {
                return PlayerStatePhase.Start;
            }

            if (elapsed < config.RecoveryStartTime)
            {
                return PlayerStatePhase.Loop;
            }

            if (elapsed < config.CanReturnTime)
            {
                return PlayerStatePhase.Recovery;
            }

            return PlayerStatePhase.Reset;
        }

        /// <summary>
        /// 写入 Knockdown 本帧窗口事实，供动画、调试和输入派生读取。
        /// </summary>
        private static void ApplyFrame(PlayerStateContext context, KnockdownFrameData frame)
        {
            context.CurrentPhase = frame.Phase;
            context.IsEvadeCancelWindow = frame.CanCancelToEvade;
            context.IsSkillCancelWindow = frame.CanCancelToSkill;
            context.IsResetWindow = frame.CanResetAttack;
            context.IsGuardCancelWindow = frame.CanCancelToGuard;
            context.IsMoveCancelWindow = frame.CanMoveCancel;
        }

        /// <summary>
        /// 尝试执行 Cancel / Window / Transition，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private static PlayerStateId TryCancelWindowTransition(PlayerStateContext context, KnockdownFrameData frame)
        {
            if (frame.CanCancelToEvade && PlayerActionInputRouter.TryConsumeEvade(context, true))
            {
                return PlayerStateId.Evade;
            }

            if (frame.CanCancelToSkill && PlayerActionInputRouter.TryConsumeSkill1(context, true))
            {
                return PlayerStateId.Skill;
            }

            if (frame.CanResetAttack && PlayerActionInputRouter.TryConsumeAttack(context, true, false, out _))
            {
                return PlayerStateId.Attack;
            }

            if (frame.CanCancelToGuard && PlayerActionInputRouter.TryConsumeGuard(context, true))
            {
                return PlayerStateId.Guard;
            }

            if (frame.CanMoveCancel && context.HasMoveInput)
            {
                return PlayerStateId.Locomotion;
            }

            return PlayerStateId.None;
        }

        private static bool TryGetConfig(out CombatTimelineReactionConfig config)
        {
            if (CombatTimelineProvider.TryGetPlayerReaction(TimelineId, out config))
            {
                return true;
            }

            if (!missingTimelineLogged)
            {
                missingTimelineLogged = true;
                Debug.LogError($"Combat Timeline reaction '{TimelineId}' is missing. Knockdown will return immediately.");
            }

            return false;
        }

        private readonly struct KnockdownFrameData
        {
            public KnockdownFrameData(
                PlayerStatePhase phase,
                bool canCancelToEvade,
                bool canCancelToSkill,
                bool canResetAttack,
                bool canCancelToGuard,
                bool canMoveCancel)
            {
                Phase = phase;
                CanCancelToEvade = canCancelToEvade;
                CanCancelToSkill = canCancelToSkill;
                CanResetAttack = canResetAttack;
                CanCancelToGuard = canCancelToGuard;
                CanMoveCancel = canMoveCancel;
            }

            public PlayerStatePhase Phase { get; }
            public bool CanCancelToEvade { get; }
            public bool CanCancelToSkill { get; }
            public bool CanResetAttack { get; }
            public bool CanCancelToGuard { get; }
            public bool CanMoveCancel { get; }
        }
    }
}
