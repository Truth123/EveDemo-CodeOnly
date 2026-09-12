// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Evade 机制可测版。负责无敌、PerfectEvade 和后段派生窗口。
    /// </summary>
    public sealed class PlayerEvadeState : PlayerStateBase
    {
        public const string NormalTimelineId = "Evade_Normal";
        public const string PerfectTimelineId = "Evade_Perfect";

        private static bool missingNormalTimelineLogged;
        private static bool missingPerfectTimelineLogged;
        private PlayerEvadeActionData currentEvade;
        private EvadeModeId currentDataMode;
        private bool failedToStart;

        /// <summary>
        /// 创建 PlayerEvadeState 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        public PlayerEvadeState() : base(PlayerStateId.Evade)
        {
        }

        /// <summary>
        /// 执行 Enter 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Enter(PlayerStateContext context)
        {
            context.ClearActionWindowsAndBuffers();
            context.BeginNormalEvade();
            currentEvade = null;
            currentDataMode = EvadeModeId.Normal;
            failedToStart = !TryLoadEvadeData(EvadeModeId.Normal, out currentEvade);
            context.CurrentPhase = failedToStart ? PlayerStatePhase.Reset : PlayerStatePhase.Start;
            context.ActionWorldDirection = Vector3.zero;
            context.HasActionWorldDirection = false;
            context.ActionMove = ResolveActionMove(context);
            context.RequestActionMotion(ResolveEvadeMotionId(context.ActionMove));
        }

        /// <summary>
        /// 推进 Tick 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            if (failedToStart)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            EvadeModeId mode = context.CurrentEvadeMode == EvadeModeId.Perfect ? EvadeModeId.Perfect : EvadeModeId.Normal;
            if ((currentEvade == null || currentDataMode != mode) && !TryLoadEvadeData(mode, out currentEvade))
            {
                return ReturnToIdleOrLocomotion(context);
            }

            currentDataMode = mode;
            float elapsed = mode == EvadeModeId.Perfect
                ? Mathf.Max(0f, context.StateElapsedTime - context.PerfectEvadeActivatedElapsed)
                : context.StateElapsedTime;
            if (mode == EvadeModeId.Perfect)
            {
                context.PerfectEvadeModeElapsed = elapsed;
            }

            EvadeFrameData evade = EvaluateFrame(currentEvade, elapsed);
            float actionEndTime = context.Input.Time + Mathf.Max(0f, evade.Duration - elapsed);
            ApplyFrameData(context, evade, mode);

            PlayerActionInputRouter.CaptureSkillInput(context, evade.CanBufferSkill, actionEndTime);

            PlayerStateId transition = TryConsumeEvadeBranches(context, evade);
            if (transition != PlayerStateId.None)
            {
                return transition;
            }

            if (evade.CanReturnToMovement && context.HasMoveInput)
            {
                return PlayerStateId.Locomotion;
            }

            return elapsed >= evade.Duration ? ReturnToIdleOrLocomotion(context) : PlayerStateId.None;
        }

        /// <summary>
        /// 执行 Exit 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Exit(PlayerStateContext context)
        {
            currentEvade = null;
            context.ClearEvadeRuntime();
            context.ClearActionWindows();
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 尝试执行 Evade 派生分支，返回是否产生顶层状态切换。
        /// </summary>
        private static PlayerStateId TryConsumeEvadeBranches(PlayerStateContext context, EvadeFrameData evade)
        {
            if (evade.CanCancelToSkill &&
                PlayerActionInputRouter.TryConsumeSkill1(context, true))
            {
                return PlayerStateId.Skill;
            }

            if (evade.CanCancelToGuard && PlayerActionInputRouter.TryConsumeGuard(context, true))
            {
                return PlayerStateId.Guard;
            }

            if (evade.CanResetAttack &&
                PlayerActionInputRouter.TryConsumeAttack(context, true, false, out _))
            {
                return PlayerStateId.Attack;
            }

            return PlayerStateId.None;
        }

        private static EvadeFrameData EvaluateFrame(PlayerEvadeActionData evadeData, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = evadeData.RuntimeSpec.Evaluate(elapsed);
            return new EvadeFrameData(
                snapshot.Phase,
                evadeData.TotalDuration,
                snapshot.Invincible,
                snapshot.PerfectEvade,
                snapshot.CanBufferSkill,
                snapshot.CanCancelToSkill,
                snapshot.CanCancelToGuard,
                snapshot.CanResetAttack,
                snapshot.CanMoveCancel);
        }

        /// <summary>
        /// 将 EvadeFrameData 写回上下文，供受击、防御、Debug 和状态切换使用。
        /// </summary>
        private static void ApplyFrameData(PlayerStateContext context, EvadeFrameData evade, EvadeModeId mode)
        {
            context.CurrentEvadeMode = mode;
            context.CurrentPhase = evade.Phase;
            context.IsEvadeInvincible = evade.Invincible;
            context.IsPerfectEvadeWindow = evade.PerfectEvade;
            context.IsSkillCancelWindow = evade.CanCancelToSkill;
            context.IsGuardCancelWindow = evade.CanCancelToGuard;
            context.IsResetWindow = evade.CanResetAttack;
        }

        private static bool TryLoadEvadeData(EvadeModeId mode, out PlayerEvadeActionData evadeData)
        {
            string timelineId = mode == EvadeModeId.Perfect ? PerfectTimelineId : NormalTimelineId;
            if (CombatTimelineProvider.TryGetPlayerEvade(timelineId, out evadeData))
            {
                return true;
            }

            bool alreadyLogged = timelineId == NormalTimelineId ? missingNormalTimelineLogged : missingPerfectTimelineLogged;
            if (!alreadyLogged)
            {
                if (timelineId == NormalTimelineId)
                {
                    missingNormalTimelineLogged = true;
                }
                else
                {
                    missingPerfectTimelineLogged = true;
                }

                Debug.LogError($"Combat Timeline action '{timelineId}' is missing. Evade state will return to a safe movement state.");
            }

            return false;
        }

        /// <summary>
        /// 解析 Action / Move 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static Vector2 ResolveActionMove(PlayerStateContext context)
        {
            Vector2 move = context.Input.Move;
            if (move.sqrMagnitude > 0.0001f)
            {
                Vector2 normalizedMove = move.normalized;
                context.ActionInputMove = normalizedMove;
                return context.IsLockOn ? normalizedMove : Vector2.up;
            }

            context.ActionInputMove = Vector2.zero;
            return Vector2.down;
        }

        /// <summary>
        /// 解析 Evade / Motion / Id 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static PlayerActionMotionId ResolveEvadeMotionId(Vector2 actionMove)
        {
            if (actionMove.sqrMagnitude <= 0.0001f)
            {
                return PlayerActionMotionId.EvadeBackward;
            }

            return Mathf.Abs(actionMove.x) > Mathf.Abs(actionMove.y)
                ? actionMove.x < 0f ? PlayerActionMotionId.EvadeLeft : PlayerActionMotionId.EvadeRight
                : actionMove.y < 0f ? PlayerActionMotionId.EvadeBackward : PlayerActionMotionId.EvadeForward;
        }

        private readonly struct EvadeFrameData
        {
            public EvadeFrameData(
                PlayerStatePhase phase,
                float duration,
                bool invincible,
                bool perfectEvade,
                bool canBufferSkill,
                bool canCancelToSkill,
                bool canCancelToGuard,
                bool canResetAttack,
                bool canReturnToMovement)
            {
                Phase = phase;
                Duration = duration;
                Invincible = invincible;
                PerfectEvade = perfectEvade;
                CanBufferSkill = canBufferSkill;
                CanCancelToSkill = canCancelToSkill;
                CanCancelToGuard = canCancelToGuard;
                CanResetAttack = canResetAttack;
                CanReturnToMovement = canReturnToMovement;
            }

            public PlayerStatePhase Phase { get; }
            public float Duration { get; }
            public bool Invincible { get; }
            public bool PerfectEvade { get; }
            public bool CanBufferSkill { get; }
            public bool CanCancelToSkill { get; }
            public bool CanCancelToGuard { get; }
            public bool CanResetAttack { get; }
            public bool CanReturnToMovement { get; }
        }
    }
}
