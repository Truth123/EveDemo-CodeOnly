// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Player.Movement;
using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// HitReaction 主干状态。作为强制状态，进入时清空动作缓存。
    /// </summary>
    public sealed class PlayerHitReactionState : PlayerStateBase
    {
        public const string TimelineId = "Player_HitReaction";
        private static bool missingTimelineLogged;
        private bool missingConfig;
        private CombatTimelineReactionConfig currentConfig;

        public PlayerHitReactionState() : base(PlayerStateId.HitReaction)
        {
        }

        public override void Enter(PlayerStateContext context)
        {
            context.ClearActionWindowsAndBuffers();
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

        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            if (missingConfig)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            CombatTimelineReactionConfig config = currentConfig;
            HitReactionFrameData frame = EvaluateFrame(config, context.StateElapsedTime);
            ApplyFrame(context, frame);

            PlayerStateId transition = TryCancelWindowTransition(context, frame);
            if (transition != PlayerStateId.None)
            {
                return transition;
            }

            return context.StateElapsedTime >= config.TotalDuration ? ReturnToIdleOrLocomotion(context) : PlayerStateId.None;
        }

        public override void Exit(PlayerStateContext context)
        {
            missingConfig = false;
            currentConfig = default;
            context.ClearActionWindows();
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 解析 Frame 数据，并把 Timeline Snapshot 转成 HitReaction 本地业务字段。
        /// </summary>
        private static HitReactionFrameData EvaluateFrame(CombatTimelineReactionConfig config, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = config.RuntimeSpec != null
                ? config.RuntimeSpec.Evaluate(elapsed)
                : default;
            PlayerStatePhase phase = snapshot.Phase != PlayerStatePhase.None
                ? snapshot.Phase
                : ResolveFallbackPhase(config, elapsed);

            return new HitReactionFrameData(
                phase,
                snapshot.CanCancelToEvade,
                snapshot.CanCancelToSkill,
                snapshot.CanResetAttack,
                snapshot.CanCancelToGuard);
        }

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

        private static void ApplyFrame(PlayerStateContext context, HitReactionFrameData frame)
        {
            context.CurrentPhase = frame.Phase;
            context.IsEvadeCancelWindow = frame.CanCancelToEvade;
            context.IsSkillCancelWindow = frame.CanCancelToSkill;
            context.IsResetWindow = frame.CanResetAttack;
            context.IsGuardCancelWindow = frame.CanCancelToGuard;
        }

        private static PlayerStateId TryCancelWindowTransition(PlayerStateContext context, HitReactionFrameData frame)
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
                Debug.LogError($"Combat Timeline reaction '{TimelineId}' is missing. HitReaction will return immediately.");
            }

            return false;
        }

        private readonly struct HitReactionFrameData
        {
            public HitReactionFrameData(
                PlayerStatePhase phase,
                bool canCancelToEvade,
                bool canCancelToSkill,
                bool canResetAttack,
                bool canCancelToGuard)
            {
                Phase = phase;
                CanCancelToEvade = canCancelToEvade;
                CanCancelToSkill = canCancelToSkill;
                CanResetAttack = canResetAttack;
                CanCancelToGuard = canCancelToGuard;
            }

            public PlayerStatePhase Phase { get; }
            public bool CanCancelToEvade { get; }
            public bool CanCancelToSkill { get; }
            public bool CanResetAttack { get; }
            public bool CanCancelToGuard { get; }
        }
    }
}
