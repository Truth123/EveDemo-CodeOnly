// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Attacks;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Attack 状态。负责普通攻击连段、攻击窗口、输入缓存、取消窗口和自然返回。
    /// </summary>
    public sealed class PlayerAttackState : PlayerStateBase
    {
        private const float AttackBufferLifetime = 1.1f;
        private const float EvadeBufferLifetime = 1.1f;
        private const float SkillBufferLifetime = 1.1f;
        private const float AttackRotateSpeed = 1440f;
        private const float LockOnComboMaxTurnAngle = 30f;

        private AttackDefinition currentAttack;
        private float attackStartTime;
        private int nextAttackInstanceId;

        /// <summary>
        /// 创建 PlayerAttackState 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        public PlayerAttackState() : base(PlayerStateId.Attack)
        {
        }

        /// <summary>
        /// 执行 Enter 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Enter(PlayerStateContext context)
        {
            AttackInputType inputType = context.RequestedAttackInput != AttackInputType.None
                ? context.RequestedAttackInput
                : PlayerActionInputRouter.ResolveRequestedAttackInput(context);

            if (!CombatTimelineProvider.TryGetFirstPlayerAttack(inputType, out AttackDefinition definition) &&
                !CombatTimelineProvider.TryGetFirstPlayerAttack(AttackInputType.Light, out definition))
            {
                Debug.LogError($"PlayerAttackState cannot enter because no first PlayerAttack Timeline asset exists for {inputType} or Light.");
                context.RequestedAttackInput = AttackInputType.None;
                context.ClearAttackRuntime();
                currentAttack = null;
                return;
            }

            StartAttackNode(context, definition, false);
        }

        /// <summary>
        /// 推进 Tick 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            if (currentAttack == null)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            float elapsed = context.StateElapsedTime;
            AttackFrameData frame = EvaluateFrame(currentAttack, elapsed);
            UpdateAttackPhaseAndWindows(context, frame);
            UpdateAttackDirection(context, deltaTime);
            RecordBufferedInputs(context, frame);
            UpdateBufferedFlags(context);

            PlayerStateId cancelState = TryConsumeCancelOrCombo(context, frame);
            if (cancelState != PlayerStateId.None)
            {
                return cancelState;
            }

            if (elapsed >= currentAttack.TotalDuration)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            return PlayerStateId.None;
        }

        /// <summary>
        /// 执行 Exit 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public override void Exit(PlayerStateContext context)
        {
            context.InputBuffer.Clear();
            context.RequestedAttackInput = AttackInputType.None;
            context.ClearAttackRuntime();
            currentAttack = null;
        }

        /// <summary>
        /// 启动 Attack / Node 流程，并提交相关运行时状态或组件协作。
        /// </summary>
        private void StartAttackNode(PlayerStateContext context, AttackDefinition definition, bool limitLockOnTurn)
        {
            currentAttack = definition;
            attackStartTime = context.Input.Time;
            context.StateElapsedTime = 0f;
            context.InputBuffer.Clear();
            context.RequestedAttackInput = AttackInputType.None;
            context.ClearAttackRuntime();

            context.CurrentAttackNodeId = definition.NodeId;
            context.CurrentAttackInputType = definition.InputType;
            context.CurrentComboIndex = definition.ComboIndex;
            context.CurrentAttackInstanceId = CreateAttackInstanceId();
            context.CurrentAttackCombatType = definition.CombatAttackType;
            context.CurrentAttackDamage = definition.Damage;
            context.CurrentAttackPoiseDamage = definition.PoiseDamage;
            context.AttackDirection = ResolveAttackDirection(context, limitLockOnTurn);

            AttackFrameData frame = EvaluateFrame(currentAttack, context.StateElapsedTime);
            UpdateAttackPhaseAndWindows(context, frame);
        }

        /// <summary>
        /// 创建 Attack / Instance / Id 实例或数据，作为后续运行时流程的唯一标识或配置来源。
        /// </summary>
        private int CreateAttackInstanceId()
        {
            if (nextAttackInstanceId == int.MaxValue)
            {
                nextAttackInstanceId = 0;
            }

            return ++nextAttackInstanceId;
        }

        /// <summary>
        /// 尝试执行 Consume / Cancel / Or / Combo，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private PlayerStateId TryConsumeCancelOrCombo(PlayerStateContext context, AttackFrameData frame)
        {
            if (frame.CanCommitCancel)
            {
                if (PlayerActionInputRouter.TryConsumeEvade(context, true))
                {
                    return PlayerStateId.Evade;
                }

                if (PlayerActionInputRouter.TryConsumeSkill1(context, true))
                {
                    return PlayerStateId.Skill;
                }

                if (PlayerActionInputRouter.TryConsumeGuard(context, true))
                {
                    return PlayerStateId.Guard;
                }
            }

            if (frame.CanCancelToEvade && PlayerActionInputRouter.TryConsumeEvade(context, true))
            {
                return PlayerStateId.Evade;
            }

            if (frame.CanCancelToSkill &&
                PlayerActionInputRouter.TryConsumeSkill1(context, true))
            {
                return PlayerStateId.Skill;
            }

            if (frame.CanComboAttack && PlayerActionInputRouter.TryConsumeAttack(context, true, true, out AttackInputType comboInputType))
            {
                TryStartComboNode(context, comboInputType);
                return PlayerStateId.None;
            }

            if (frame.CanResetAttack && !frame.CanComboAttack && TryRestartAttackFromInput(context))
            {
                return PlayerStateId.None;
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

        /// <summary>
        /// 尝试执行 Start / Combo / Node，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private bool TryStartComboNode(PlayerStateContext context, AttackInputType inputType)
        {
            string nextNodeId = currentAttack.GetNextNodeId(inputType);
            if (string.IsNullOrEmpty(nextNodeId) ||
                !CombatTimelineProvider.TryGetPlayerAttack(nextNodeId, out AttackDefinition nextAttack))
            {
                return false;
            }

            StartAttackNode(context, nextAttack, true);
            return true;
        }

        /// <summary>
        /// 尝试执行 Restart / Attack / From / Input，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private bool TryRestartAttackFromInput(PlayerStateContext context)
        {
            if (!PlayerActionInputRouter.TryConsumeAttack(context, true, false, out AttackInputType inputType))
            {
                return false;
            }

            if (CombatTimelineProvider.TryGetFirstPlayerAttack(inputType, out AttackDefinition firstAttack))
            {
                StartAttackNode(context, firstAttack, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 执行 Record / Buffered / Inputs 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        private void RecordBufferedInputs(PlayerStateContext context, AttackFrameData frame)
        {
            float actionEndTime = attackStartTime + currentAttack.TotalDuration;

            PlayerActionInputRouter.CaptureAttackInput(context, frame.CanBufferAttack, actionEndTime, AttackBufferLifetime);
            PlayerActionInputRouter.CaptureEvadeInput(context, frame.CanBufferEvade, actionEndTime, EvadeBufferLifetime);
            PlayerActionInputRouter.CaptureSkillInput(context, frame.CanBufferSkill, actionEndTime, SkillBufferLifetime);
        }

        /// <summary>
        /// 更新 Attack / Phase / And / Windows 状态，并将结果写回上下文、组件或调试数据。
        /// </summary>
        private void UpdateAttackPhaseAndWindows(PlayerStateContext context, AttackFrameData frame)
        {
            context.IsCommitCancelWindow = frame.CanCommitCancel;
            context.IsAttackHitboxActive = frame.AttackHitboxActive;
            context.IsComboWindow = frame.CanComboAttack;
            context.IsEvadeCancelWindow = frame.CanCancelToEvade;
            context.IsSkillCancelWindow = frame.CanCancelToSkill;
            context.IsGuardCancelWindow = frame.CanCancelToGuard;
            context.IsResetWindow = frame.CanResetAttack;
            context.IsMoveCancelWindow = frame.CanMoveCancel;
            context.IsActionUninterruptible = frame.SuperArmor || frame.Uninterruptible;

            if (frame.Phase != PlayerStatePhase.None)
            {
                context.CurrentPhase = frame.Phase;
            }

            if (frame.AttackHitboxExpired &&
                context.AttackContactResult == AttackContactResult.None)
            {
                context.AttackContactResult = AttackContactResult.OnWhiff;
                context.ResetNormalAttackHitSequenceOnWhiff();
            }
        }
        private static AttackFrameData EvaluateFrame(AttackDefinition attack, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = attack.EvaluateCapabilities(elapsed);
            return new AttackFrameData(
                snapshot.Phase,
                snapshot.CanCommitCancel,
                snapshot.AttackHitboxActive,
                snapshot.CanBufferAttack,
                snapshot.CanBufferEvade,
                snapshot.CanBufferSkill,
                snapshot.CanComboAttack,
                snapshot.CanCancelToEvade,
                snapshot.CanCancelToSkill,
                snapshot.CanCancelToGuard,
                snapshot.CanResetAttack,
                snapshot.CanMoveCancel,
                snapshot.SuperArmor,
                snapshot.Uninterruptible,
                HasAttackHitboxExpired(attack, elapsed));
        }


        private static bool HasAttackHitboxExpired(AttackDefinition attack, float elapsed)
        {
            if (attack == null || attack.HitWindows == null || attack.HitWindows.Length == 0)
            {
                return false;
            }

            float latestEndTime = 0f;
            for (int i = 0; i < attack.HitWindows.Length; i++)
            {
                latestEndTime = Mathf.Max(latestEndTime, attack.HitWindows[i].EndTime);
            }

            return elapsed > latestEndTime;
        }

        /// <summary>
        /// 更新 Buffered / Flags 状态，并将结果写回上下文、组件或调试数据。
        /// </summary>
        private void UpdateBufferedFlags(PlayerStateContext context)
        {
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 解析 Attack / Direction 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static Vector3 ResolveAttackDirection(PlayerStateContext context, bool limitLockOnTurn)
        {
            if (context.IsLockOn && context.LockOnTarget != null && context.PlayerTransform != null)
            {
                Vector3 toTarget = context.LockOnTarget.position - context.PlayerTransform.position;
                toTarget.y = 0f;
                if (toTarget.sqrMagnitude > 0.0001f)
                {
                    return ResolveLockOnAttackDirection(context, toTarget.normalized, limitLockOnTurn);
                }
            }

            context.LastAttackDesiredTurnAngle = 0f;
            context.LastAttackAppliedTurnAngle = 0f;
            context.WasAttackDirectionClamped = false;

            if (context.MoveDirection.sqrMagnitude > 0.0001f)
            {
                return context.MoveDirection.normalized;
            }

            return context.PlayerTransform != null ? context.PlayerTransform.forward : Vector3.forward;
        }

        /// <summary>
        /// 解析 Lock / On / Attack / Direction 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static Vector3 ResolveLockOnAttackDirection(
            PlayerStateContext context,
            Vector3 targetDirection,
            bool limitLockOnTurn)
        {
            Vector3 currentForward = context.PlayerTransform.forward;
            currentForward.y = 0f;
            if (currentForward.sqrMagnitude <= 0.0001f)
            {
                currentForward = targetDirection;
            }
            else
            {
                currentForward.Normalize();
            }

            float desiredTurnAngle = Vector3.SignedAngle(currentForward, targetDirection, Vector3.up);
            float appliedTurnAngle = limitLockOnTurn
                ? Mathf.Clamp(desiredTurnAngle, -LockOnComboMaxTurnAngle, LockOnComboMaxTurnAngle)
                : desiredTurnAngle;
            bool clamped = !Mathf.Approximately(desiredTurnAngle, appliedTurnAngle);

            context.LastAttackDesiredTurnAngle = desiredTurnAngle;
            context.LastAttackAppliedTurnAngle = appliedTurnAngle;
            context.WasAttackDirectionClamped = clamped;

            if (!clamped)
            {
                return targetDirection;
            }

            Vector3 clampedDirection = Quaternion.AngleAxis(appliedTurnAngle, Vector3.up) * currentForward;
            clampedDirection.y = 0f;
            return clampedDirection.sqrMagnitude > 0.0001f ? clampedDirection.normalized : targetDirection;
        }
        private readonly struct AttackFrameData
        {
            public AttackFrameData(PlayerStatePhase phase, bool canCommitCancel, bool attackHitboxActive, bool canBufferAttack, bool canBufferEvade, bool canBufferSkill, bool canComboAttack, bool canCancelToEvade, bool canCancelToSkill, bool canCancelToGuard, bool canResetAttack, bool canMoveCancel, bool superArmor, bool uninterruptible, bool attackHitboxExpired)
            {
                Phase = phase;
                CanCommitCancel = canCommitCancel;
                AttackHitboxActive = attackHitboxActive;
                CanBufferAttack = canBufferAttack;
                CanBufferEvade = canBufferEvade;
                CanBufferSkill = canBufferSkill;
                CanComboAttack = canComboAttack;
                CanCancelToEvade = canCancelToEvade;
                CanCancelToSkill = canCancelToSkill;
                CanCancelToGuard = canCancelToGuard;
                CanResetAttack = canResetAttack;
                CanMoveCancel = canMoveCancel;
                SuperArmor = superArmor;
                Uninterruptible = uninterruptible;
                AttackHitboxExpired = attackHitboxExpired;
            }

            public PlayerStatePhase Phase { get; }
            public bool CanCommitCancel { get; }
            public bool AttackHitboxActive { get; }
            public bool CanBufferAttack { get; }
            public bool CanBufferEvade { get; }
            public bool CanBufferSkill { get; }
            public bool CanComboAttack { get; }
            public bool CanCancelToEvade { get; }
            public bool CanCancelToSkill { get; }
            public bool CanCancelToGuard { get; }
            public bool CanResetAttack { get; }
            public bool CanMoveCancel { get; }
            public bool SuperArmor { get; }
            public bool Uninterruptible { get; }
            public bool AttackHitboxExpired { get; }
        }

        /// <summary>
        /// 更新 Attack / Direction 状态，并将结果写回上下文、组件或调试数据。
        /// </summary>
        private static void UpdateAttackDirection(PlayerStateContext context, float deltaTime)
        {
            if (context.PlayerTransform == null || context.AttackDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Vector3 direction = context.AttackDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            context.PlayerTransform.rotation = Quaternion.RotateTowards(
                context.PlayerTransform.rotation,
                targetRotation,
                AttackRotateSpeed * deltaTime);
        }
    }
}
