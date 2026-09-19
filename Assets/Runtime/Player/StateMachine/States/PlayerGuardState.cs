// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Windows;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Guard 机制可测版。包含 Start、Loop、Release 三段，以及 PerfectGuard / GuardHit 判定窗口。
    /// </summary>
    public sealed class PlayerGuardState : PlayerStateBase
    {
        public const string GuardTimelineId = "Guard";

        private enum GuardMode
        {
            Start,
            Loop,
            GuardHit,
            PerfectGuard,
            Release
        }

        private const float MinimumGuardStartDuration = 0.15f;

        private static bool missingGuardTimelineLogged;
        private static bool missingGuardHitReactionDurationLogged;
        private static bool missingPerfectGuardReactionDurationLogged;
        private static bool invalidPerfectGuardChainActiveDurationLogged;
        private PlayerGuardActionData guardData;
        private GuardMode mode;
        private float modeElapsed;
        private int consumedGuardReactionVersion;

        public PlayerGuardState() : base(PlayerStateId.Guard)
        {
        }

        public override void Enter(PlayerStateContext context)
        {
            guardData = null;
            EnterStart(context, false);
        }

        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            modeElapsed += deltaTime;

            if (context.GuardReactionRequestVersion != consumedGuardReactionVersion)
            {
                EnterReaction(context);
            }

            if (mode == GuardMode.Start)
            {
                return TickStart(context);
            }

            if (mode == GuardMode.Loop)
            {
                return TickLoop(context);
            }

            if (mode == GuardMode.GuardHit || mode == GuardMode.PerfectGuard)
            {
                return TickReaction(context, deltaTime);
            }

            return TickRelease(context);
        }


        public override void Exit(PlayerStateContext context)
        {
            guardData = null;
            context.ClearDefenseRuntime();
            context.ClearActionWindows();
            context.ClearGuardReaction();
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }


        private PlayerStateId TickStart(PlayerStateContext context)
        {
            if (!TryGetGuardData(out PlayerGuardActionData data) ||
                !TryResolveGuardStartDuration(data.RuntimeSpec, out float startDuration))
            {
                return ReturnToIdleOrLocomotion(context);
            }

            GuardStartFrameData guard = EvaluateStartFrame(data, modeElapsed);
            context.CurrentPhase = PlayerStatePhase.Start;
            context.IsGuardBlockActive = guard.GuardBlock;
            context.IsPerfectGuardWindow = guard.PerfectGuard;
            ClearGuardWalk(context);

            if (!context.Input.GuardHeld && modeElapsed >= MinimumGuardStartDuration)
            {
                if (context.HasMoveInput)
                {
                    return PlayerStateId.Locomotion;
                }

                EnterRelease(context);
                return PlayerStateId.None;
            }

            if (modeElapsed >= startDuration)
            {
                EnterLoop(context);
            }

            return PlayerStateId.None;
        }

        private PlayerStateId TickLoop(PlayerStateContext context)
        {
            context.CurrentPhase = PlayerStatePhase.Loop;
            context.IsGuardBlockActive = true;
            context.IsPerfectGuardWindow = false;
            context.IsGuardReentry = false;
            UpdateGuardWalk(context);

            if (!context.Input.GuardHeld)
            {
                if (context.HasMoveInput)
                {
                    return PlayerStateId.Locomotion;
                }

                EnterRelease(context);
            }

            return PlayerStateId.None;
        }


        private PlayerStateId TickReaction(PlayerStateContext context, float deltaTime)
        {
            bool hasDuration = mode == GuardMode.PerfectGuard
                ? TryResolvePerfectGuardDuration(out float duration)
                : TryResolveGuardHitDuration(out duration);
            if (!hasDuration)
            {
                context.ClearGuardReaction();
                return ReturnToIdleOrLocomotion(context);
            }

            context.CurrentPhase = PlayerStatePhase.Active;
            context.IsGuardBlockActive = true;
            context.IsPerfectGuardWindow = false;
            ClearGuardWalk(context);
            context.ClearActionWindows();
            TickPerfectGuardChainWindow(context, deltaTime);

            if (mode == GuardMode.GuardHit && TryEnterReleaseFromGuardHit(context))
            {
                return PlayerStateId.None;
            }

            if (mode == GuardMode.PerfectGuard && TryEnterReleaseFromPerfectGuard(context))
            {
                return PlayerStateId.None;
            }

            if (modeElapsed < duration)
            {
                return PlayerStateId.None;
            }

            context.ClearGuardReaction();
            if (context.Input.GuardHeld)
            {
                EnterLoop(context);
                return PlayerStateId.None;
            }

            if (mode == GuardMode.GuardHit && TryGetGuardData(out PlayerGuardActionData data))
            {
                EnterReleaseFromGuardHit(context, data);
            }
            else
            {
                EnterRelease(context);
            }

            return PlayerStateId.None;
        }

        /// <summary>
        /// 在 GuardHit 的 Release 许可窗口内响应松开 Guard，并进入真实 GuardRelease。
        /// </summary>
        /// <param name="context">当前玩家状态上下文；读取 Guard 持有状态并写入 Release 运行时。</param>
        /// <returns>成功进入 GuardRelease 时返回 <c>true</c>；窗口未开启、仍按住 Guard 或缺少数据时返回 <c>false</c>。</returns>
        private bool TryEnterReleaseFromGuardHit(PlayerStateContext context)
        {
            if (!TryGetGuardData(out PlayerGuardActionData data))
            {
                return false;
            }

            if (context.Input.GuardHeld || !data.GuardHitCancelData.CanCancelToGuardRelease(modeElapsed))
            {
                return false;
            }

            EnterReleaseFromGuardHit(context, data);
            return true;
        }

        /// <summary>
        /// 在 PerfectGuard 的 Release 许可窗口内响应松开 Guard，先进入真实 GuardRelease 再开放后续动作。
        /// </summary>
        /// <param name="context">当前玩家状态上下文；读取 Guard 持有状态并写入 Release 运行时。</param>
        /// <returns>成功进入 GuardRelease 时返回 <c>true</c>；窗口未开启、仍按住 Guard 或缺少数据时返回 <c>false</c>。</returns>
        /// <remarks>松手同帧的 Attack 不缓存；必须在 GuardRelease 的 AttackReset 窗口内重新按下。</remarks>
        private bool TryEnterReleaseFromPerfectGuard(PlayerStateContext context)
        {
            if (!TryGetGuardData(out PlayerGuardActionData data))
            {
                return false;
            }

            if (context.Input.GuardHeld || !data.PerfectGuardCancelData.CanCancelToGuardRelease(modeElapsed))
            {
                return false;
            }

            EnterRelease(context);
            return true;
        }

        private void EnterReaction(PlayerStateContext context)
        {
            consumedGuardReactionVersion = context.GuardReactionRequestVersion;
            mode = context.CurrentGuardReaction == PlayerGuardReactionId.PerfectGuard
                ? GuardMode.PerfectGuard
                : GuardMode.GuardHit;
            modeElapsed = 0f;
            context.CurrentPhase = PlayerStatePhase.Active;
            context.IsGuardBlockActive = true;
            context.IsPerfectGuardWindow = false;
            context.IsGuardReentry = false;
            context.ClearPerfectGuardChainWindow();
            ClearGuardWalk(context);
            context.ClearActionWindows();
        }

        private PlayerStateId TickRelease(PlayerStateContext context)
        {
            if (!TryGetGuardData(out PlayerGuardActionData data) ||
                !TryResolveGuardReleaseDuration(data, out float releaseDuration))
            {
                return ReturnToIdleOrLocomotion(context);
            }

            GuardReleaseFrameData release = EvaluateReleaseFrame(data, modeElapsed);
            if (context.Input.GuardPressed || context.Input.GuardHeld)
            {
                EnterStart(context, true);
                return PlayerStateId.None;
            }

            float actionEndTime = context.Input.Time + Mathf.Max(0f, releaseDuration - modeElapsed);
            context.CurrentPhase = PlayerStatePhase.Recovery;
            context.IsGuardBlockActive = false;
            context.IsPerfectGuardWindow = false;
            context.ClearPerfectGuardChainWindow();
            ClearGuardWalk(context);
            context.IsResetWindow = release.CanResetAttack;
            context.IsEvadeCancelWindow = release.CanCancelToEvade;
            context.IsSkillCancelWindow = release.CanCancelToSkill;

            PlayerActionInputRouter.CaptureEvadeInput(context, release.CanBufferEvade, actionEndTime);
            PlayerActionInputRouter.CaptureSkillInput(context, release.CanBufferSkill, actionEndTime);

            if (context.HasMoveInput)
            {
                return PlayerStateId.Locomotion;
            }

            if (release.CanCancelToEvade && PlayerActionInputRouter.TryConsumeEvade(context, true))
            {
                return PlayerStateId.Evade;
            }

            if (release.CanCancelToSkill &&
                PlayerActionInputRouter.TryConsumeSkill1(context, true))
            {
                return PlayerStateId.Skill;
            }

            if (release.CanResetAttack &&
                PlayerActionInputRouter.TryConsumeAttack(context, true, false, out _))
            {
                return PlayerStateId.Attack;
            }

            return modeElapsed >= releaseDuration ? ReturnToIdleOrLocomotion(context) : PlayerStateId.None;
        }

        private void EnterRelease(PlayerStateContext context)
        {
            mode = GuardMode.Release;
            modeElapsed = 0f;
            context.CurrentPhase = PlayerStatePhase.Recovery;
            context.ClearDefenseRuntime();
            context.ClearActionWindows();
            context.ClearGuardReaction();
        }

        /// <summary>
        /// 从 GuardHit 进入真实 GuardRelease，并把松手同帧的 Evade / Skill 输入写入 Release 起始缓存窗口。
        /// </summary>
        /// <param name="context">当前玩家状态上下文；写入 Release 状态并更新动作输入缓存。</param>
        /// <param name="data">已解析的 Guard Provider Data；用于读取 Release 时长和起始缓存能力。</param>
        /// <remarks>Attack 不在此处缓存；玩家必须在 GuardRelease 的 AttackReset 窗口内重新按下攻击。</remarks>
        private void EnterReleaseFromGuardHit(PlayerStateContext context, PlayerGuardActionData data)
        {
            EnterRelease(context);
            if (!TryResolveGuardReleaseDuration(data, out float releaseDuration))
            {
                return;
            }

            GuardReleaseFrameData release = EvaluateReleaseFrame(data, 0f);
            float actionEndTime = context.Input.Time + releaseDuration;
            PlayerActionInputRouter.CaptureEvadeInput(context, release.CanBufferEvade, actionEndTime);
            PlayerActionInputRouter.CaptureSkillInput(context, release.CanBufferSkill, actionEndTime);
        }

        private void EnterStart(PlayerStateContext context, bool isReentry)
        {
            context.ClearActionWindowsAndBuffers();
            mode = GuardMode.Start;
            modeElapsed = 0f;
            consumedGuardReactionVersion = context.GuardReactionRequestVersion;
            context.ClearGuardReaction();
            context.CurrentPhase = PlayerStatePhase.Start;
            context.IsGuardReentry = isReentry;
            context.ClearPerfectGuardChainWindow();
            ClearGuardWalk(context);
        }

        private void EnterLoop(PlayerStateContext context)
        {
            mode = GuardMode.Loop;
            modeElapsed = 0f;
            context.CurrentPhase = PlayerStatePhase.Loop;
            context.IsGuardBlockActive = true;
            context.IsPerfectGuardWindow = false;
            context.IsGuardReentry = false;
            context.ClearPerfectGuardChainWindow();
            UpdateGuardWalk(context);
        }

        private void TickPerfectGuardChainWindow(PlayerStateContext context, float deltaTime)
        {
            if (mode != GuardMode.PerfectGuard || !TryGetGuardData(out PlayerGuardActionData data))
            {
                context.ClearPerfectGuardChainWindow();
                return;
            }

            if (context.IsPerfectGuardChainWindow)
            {
                context.PerfectGuardChainElapsed += deltaTime;
                if (context.PerfectGuardChainElapsed >= data.PerfectGuardChainActiveDuration)
                {
                    context.ClearPerfectGuardChainWindow();
                }

                return;
            }

            GuardChainFrameData chain = EvaluateChainFrame(data, modeElapsed);
            if (context.Input.GuardPressed && chain.ChainInput && data.PerfectGuardChainActiveDuration > 0f)
            {
                context.IsPerfectGuardChainWindow = true;
                context.PerfectGuardChainElapsed = 0f;
                return;
            }

            if (chain.ChainInput && data.PerfectGuardChainActiveDuration <= 0f && !invalidPerfectGuardChainActiveDurationLogged)
            {
                invalidPerfectGuardChainActiveDurationLogged = true;
                Debug.LogError($"Combat Timeline action '{data.ActionId}' has invalid PerfectGuardChainActiveDuration. Guard chain will not open.");
            }
        }

        private static GuardStartFrameData EvaluateStartFrame(PlayerGuardActionData data, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = data.RuntimeSpec.Evaluate(elapsed);
            return new GuardStartFrameData(snapshot.GuardBlock, snapshot.PerfectGuard);
        }

        private static GuardReleaseFrameData EvaluateReleaseFrame(PlayerGuardActionData data, float elapsed)
        {
            PlayerGuardReactionCancelData cancelData = data.GuardReleaseCancelData;
            return new GuardReleaseFrameData(
                cancelData.CanBufferEvade(elapsed),
                cancelData.CanBufferSkill(elapsed),
                cancelData.CanCancelToEvade(elapsed),
                cancelData.CanCancelToSkill(elapsed),
                cancelData.CanResetAttack(elapsed));
        }

        private static GuardChainFrameData EvaluateChainFrame(PlayerGuardActionData data, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = data.RuntimeSpec.Evaluate(elapsed);
            return new GuardChainFrameData(snapshot.PerfectGuardChainInput);
        }

        private bool TryGetGuardData(out PlayerGuardActionData data)
        {
            if (guardData != null)
            {
                data = guardData;
                return true;
            }

            if (CombatTimelineProvider.TryGetPlayerGuard(GuardTimelineId, out guardData))
            {
                data = guardData;
                return true;
            }

            data = null;
            if (!missingGuardTimelineLogged)
            {
                missingGuardTimelineLogged = true;
                Debug.LogError($"Combat Timeline action '{GuardTimelineId}' is missing. Guard state will return to a safe movement state.");
            }

            return false;
        }

        private static bool TryResolveGuardStartDuration(CombatActionRuntimeSpec runtimeSpec, out float duration)
        {
            duration = 0f;
            bool found = AccumulateWindowEnd(runtimeSpec, CombatTimelineCapabilityId.Defense_GuardBlock, ref duration);
            found |= AccumulateWindowEnd(runtimeSpec, CombatTimelineCapabilityId.Defense_PerfectGuard, ref duration);
            if (!found)
            {
                Debug.LogError($"Combat Timeline action '{GuardTimelineId}' is missing Guard Start defense windows.");
            }

            return found;
        }

        private static bool TryResolveGuardReleaseDuration(PlayerGuardActionData data, out float duration)
        {
            duration = data.GuardReleaseCancelData.GetMaxEndTime();
            bool found = duration > 0f;
            if (!found)
            {
                Debug.LogError($"Combat Timeline action '{GuardTimelineId}' is missing Guard Release cancel/input windows.");
            }

            return found;
        }

        private bool TryResolvePerfectGuardDuration(out float duration)
        {
            duration = 0f;
            if (!TryGetGuardData(out PlayerGuardActionData data))
            {
                return false;
            }

            duration = data.PerfectGuardReactionDuration;
            if (duration > 0f)
            {
                return true;
            }

            if (!missingPerfectGuardReactionDurationLogged)
            {
                missingPerfectGuardReactionDurationLogged = true;
                Debug.LogError($"Combat Timeline action '{data.ActionId}' is missing PerfectGuardReaction marker duration.");
            }

            return false;
        }

        private bool TryResolveGuardHitDuration(out float duration)
        {
            duration = 0f;
            if (!TryGetGuardData(out PlayerGuardActionData data))
            {
                return false;
            }

            duration = data.GuardHitReactionDuration;
            if (duration > 0f)
            {
                return true;
            }

            if (!missingGuardHitReactionDurationLogged)
            {
                missingGuardHitReactionDurationLogged = true;
                Debug.LogError($"Combat Timeline action '{data.ActionId}' is missing GuardHitReaction marker duration.");
            }

            return false;
        }

        private static bool AccumulateWindowEnd(CombatActionRuntimeSpec runtimeSpec, CombatTimelineCapabilityId capabilityId, ref float endTime)
        {
            ActionWindow[] windows = runtimeSpec.GetWindows(capabilityId);
            if (windows.Length == 0)
            {
                return false;
            }

            for (int i = 0; i < windows.Length; i++)
            {
                endTime = Mathf.Max(endTime, windows[i].EndTime);
            }

            return true;
        }

        /// <summary>
        /// 更新 Guard / Walk 状态，并将结果写回上下文、组件或调试数据。
        /// </summary>
        private static void UpdateGuardWalk(PlayerStateContext context)
        {
            if (!context.HasMoveInput)
            {
                context.IsGuardWalking = false;
                context.GuardMoveDirection = context.LastGuardMoveDirection;
                return;
            }

            PlayerGuardMoveDirectionId direction = ResolveGuardMoveDirection(context.Input.Move);
            context.IsGuardWalking = direction != PlayerGuardMoveDirectionId.None;
            if (!context.IsGuardWalking)
            {
                context.GuardMoveDirection = context.LastGuardMoveDirection;
                return;
            }

            context.GuardMoveDirection = direction;
            context.LastGuardMoveDirection = direction;
        }


        private static PlayerGuardMoveDirectionId ResolveGuardMoveDirection(Vector2 move)
        {
            if (move.sqrMagnitude <= 0.0001f)
            {
                return PlayerGuardMoveDirectionId.None;
            }

            return Mathf.Abs(move.x) > Mathf.Abs(move.y)
                ? move.x < 0f ? PlayerGuardMoveDirectionId.Left : PlayerGuardMoveDirectionId.Right
                : move.y < 0f ? PlayerGuardMoveDirectionId.Backward : PlayerGuardMoveDirectionId.Forward;
        }

        private readonly struct GuardStartFrameData
        {
            public GuardStartFrameData(bool guardBlock, bool perfectGuard)
            {
                GuardBlock = guardBlock;
                PerfectGuard = perfectGuard;
            }

            public bool GuardBlock { get; }
            public bool PerfectGuard { get; }
        }

        private readonly struct GuardReleaseFrameData
        {
            public GuardReleaseFrameData(bool canBufferEvade, bool canBufferSkill, bool canCancelToEvade, bool canCancelToSkill, bool canResetAttack)
            {
                CanBufferEvade = canBufferEvade;
                CanBufferSkill = canBufferSkill;
                CanCancelToEvade = canCancelToEvade;
                CanCancelToSkill = canCancelToSkill;
                CanResetAttack = canResetAttack;
            }

            public bool CanBufferEvade { get; }
            public bool CanBufferSkill { get; }
            public bool CanCancelToEvade { get; }
            public bool CanCancelToSkill { get; }
            public bool CanResetAttack { get; }
        }

        private readonly struct GuardChainFrameData
        {
            public GuardChainFrameData(bool chainInput)
            {
                ChainInput = chainInput;
            }

            public bool ChainInput { get; }
        }

        private static void ClearGuardWalk(PlayerStateContext context)
        {
            context.IsGuardWalking = false;
            context.GuardMoveDirection = PlayerGuardMoveDirectionId.None;
        }
    }
}
