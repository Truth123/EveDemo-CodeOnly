// 文件说明：维护玩家 Animator 参数桥接和动作动画播放。
// 所属模块：玩家动画。
// 运行影响：影响玩家动画状态同步和动作表现。

using System.Collections.Generic;
using ProjectEVE.Player.Attacks;
using UnityEngine;

namespace ProjectEVE.Player.Animation
{
    /// <summary>
    /// Animator 参数桥接器。状态机只写上下文，动画层按存在的参数安全同步。
    /// </summary>
    public sealed class PlayerAnimationBridge : MonoBehaviour
    {
        /// <summary>玩家 Animator。未绑定时从当前物体和子物体自动查找。</summary>
        [SerializeField] private Animator animator;

        private readonly HashSet<int> parameterHashes = new HashSet<int>();

        private static readonly int PlayerStateHash = Animator.StringToHash("PlayerState");
        private static readonly int PlayerStatePhaseHash = Animator.StringToHash("PlayerStatePhase");
        private static readonly int ControlModeHash = Animator.StringToHash("ControlMode");
        private static readonly int IsInCombatHash = Animator.StringToHash("IsInCombat");
        private static readonly int IsLockOnHash = Animator.StringToHash("IsLockOn");
        private static readonly int MoveMagnitudeHash = Animator.StringToHash("MoveMagnitude");
        private static readonly int MoveXHash = Animator.StringToHash("MoveX");
        private static readonly int MoveYHash = Animator.StringToHash("MoveY");
        private static readonly int ActionMoveXHash = Animator.StringToHash("ActionMoveX");
        private static readonly int ActionMoveYHash = Animator.StringToHash("ActionMoveY");
        private static readonly int SprintIntentHash = Animator.StringToHash("SprintIntent");
        private static readonly int AttackInputTypeHash = Animator.StringToHash("AttackInputType");
        private static readonly int ComboIndexHash = Animator.StringToHash("ComboIndex");
        private static readonly int IsAttackHitboxActiveHash = Animator.StringToHash("IsAttackHitboxActive");
        private static readonly int IsEvadeInvincibleHash = Animator.StringToHash("IsEvadeInvincible");
        private static readonly int IsPerfectEvadeWindowHash = Animator.StringToHash("IsPerfectEvadeWindow");
        private static readonly int IsPerfectEvadeActiveHash = Animator.StringToHash("IsPerfectEvadeActive");
        private static readonly int PerfectEvadeDirectionHash = Animator.StringToHash("PerfectEvadeDirection");
        private static readonly int IsGuardBlockActiveHash = Animator.StringToHash("IsGuardBlockActive");
        private static readonly int IsPerfectGuardWindowHash = Animator.StringToHash("IsPerfectGuardWindow");
        private static readonly int IsGuardReentryHash = Animator.StringToHash("IsGuardReentry");
        private static readonly int IsGuardWalkingHash = Animator.StringToHash("IsGuardWalking");
        private static readonly int GuardMoveDirectionHash = Animator.StringToHash("GuardMoveDirection");
        private static readonly int IsSkillSuperArmorHash = Animator.StringToHash("IsSkillSuperArmor");
        private static readonly int IsSkillHitboxActiveHash = Animator.StringToHash("IsSkillHitboxActive");
        private static readonly int BetaEnergyHash = Animator.StringToHash("BetaEnergy");
        private static readonly int HitDirectionHash = Animator.StringToHash("HitDirection");
        private static readonly int GuardReactionHash = Animator.StringToHash("GuardReaction");
        private static readonly int KnockdownTypeHash = Animator.StringToHash("KnockdownType");
        private static readonly int DeadTypeHash = Animator.StringToHash("DeadType");
        private static readonly int HasBufferedAttackInputHash = Animator.StringToHash("HasBufferedAttackInput");
        private static readonly int HasBufferedEvadeInputHash = Animator.StringToHash("HasBufferedEvadeInput");
        private static readonly int HasBufferedSkillInputHash = Animator.StringToHash("HasBufferedSkillInput");
        private static readonly int[] AttackLight1StateHashes = CreateStateHashes("SM_Attack", "Anim_Attack_L1");
        private static readonly int[] AttackHeavy1StateHashes = CreateStateHashes("SM_Attack", "Anim_Attack_H1");
        private static readonly int[] PerfectEvadeForwardStateHashes = CreateStateHashes("SM_Evade", "Anim_PerfectEvade_F");
        private static readonly int[] PerfectEvadeBackwardStateHashes = CreateStateHashes("SM_Evade", "Anim_PerfectEvade_B");
        private static readonly int[] PerfectEvadeLeftStateHashes = CreateStateHashes("SM_Evade", "Anim_PerfectEvade_L");
        private static readonly int[] PerfectEvadeRightStateHashes = CreateStateHashes("SM_Evade", "Anim_PerfectEvade_R");
        private static readonly int[] HitReactionFallbackStateHashes = CreateStateHashes("SM_Reaction", "Anim_HitReaction_Light");
        private static readonly int[] HitReactionFrontStateHashes = CreateStateHashes("SM_Reaction", "Anim_HitReaction_F");
        private static readonly int[] HitReactionBackStateHashes = CreateStateHashes("SM_Reaction", "Anim_HitReaction_B");
        private static readonly int[] HitReactionLeftStateHashes = CreateStateHashes("SM_Reaction", "Anim_HitReaction_L");
        private static readonly int[] HitReactionRightStateHashes = CreateStateHashes("SM_Reaction", "Anim_HitReaction_R");
        private static readonly int[] GuardHitBackStateHashes = CreateStateHashes("SM_Guard", "Anim_Guard_Hit_Back");
        private static readonly int[] GuardHitLeftStateHashes = CreateStateHashes("SM_Guard", "Anim_Guard_Hit_Left");
        private static readonly int[] GuardHitRightStateHashes = CreateStateHashes("SM_Guard", "Anim_Guard_Hit_Right");
        private static readonly int[] PerfectGuardLeftStateHashes = CreateStateHashes("SM_Guard", "Anim_PerfectGuard_L");
        private static readonly int[] PerfectGuardRightStateHashes = CreateStateHashes("SM_Guard", "Anim_PerfectGuard_R");
        private static readonly int[] KnockdownStartBackwardStateHashes = CreateStateHashes("SM_Reaction", "Anim_Knockdown_Start_B");
        private static readonly int[] KnockdownStartForwardStateHashes = CreateStateHashes("SM_Reaction", "Anim_Knockdown_Start_F");
        private static readonly int[] KnockdownLoopStateHashes = CreateStateHashes("SM_Reaction", "Anim_Knockdown_Loop");
        private static readonly int[] KnockdownEndStateHashes = CreateStateHashes("SM_Reaction", "Anim_Knockdown_End");
        private static readonly int[] DeadStandStateHashes = CreateStateHashes("SM_Reaction", "Anim_Dead");
        private static readonly int[] DeadDownStartStateHashes = CreateStateHashes("SM_Reaction", "Anim_Dead_Down_Start");

        private const float PerfectEvadeCrossFadeDurationSeconds = 0.10f;
        private const float ReactionCrossFadeDurationSeconds = 0.10f;
        private const float RepeatStarterAttackCrossFadeDurationSeconds = 0.02f;
        private const float PerfectEvadeWindowStart = 0.0f;
        private const float PerfectEvadeWindowEnd = 0.125f;
        private const float PerfectEvadeMaxStartOffsetSeconds = 0.125f;

        private bool wasPerfectEvadeActive;
        private PerfectEvadeDirectionId lastPerfectEvadeDirection;
        private int lastReactionStateHash;
        private int lastGuardReactionVersion;
        private int lastCombatReactionAnimationRequestVersion;
        private int lastAttackInstanceId;
        private AttackInputType lastAttackInputType;
        private int lastAttackComboIndex;

        /// <summary>
        /// 创建 State / Hashes 实例或数据，作为后续运行时流程的唯一标识或配置来源。
        /// </summary>
        private static int[] CreateStateHashes(string subStateMachineName, string stateName)
        {
            return new[]
            {
                Animator.StringToHash($"Base Layer.{subStateMachineName}.{stateName}"),
                Animator.StringToHash($"Base Layer.{stateName}")
            };
        }

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }

            CacheParameters();
        }

        /// <summary>
        /// 状态切换时同步一次离散参数，后续可在这里接入 Trigger。
        /// </summary>
        public void OnStateChanged(PlayerStateContext context)
        {
            Tick(context);
        }

        /// <summary>
        /// 每帧同步连续参数，例如移动输入和锁定状态。
        /// </summary>
        public void Tick(PlayerStateContext context)
        {
            if (animator == null)
            {
                return;
            }

            SetInteger(PlayerStateHash, (int)context.CurrentState);
            SetInteger(PlayerStatePhaseHash, (int)context.CurrentPhase);
            SetInteger(ControlModeHash, (int)context.ControlMode);
            SetBool(IsInCombatHash, context.IsInCombat);
            SetBool(IsLockOnHash, context.IsLockOn);
            SetFloat(MoveMagnitudeHash, context.AnimationMoveMagnitude);
            SetFloat(MoveXHash, context.AnimationMove.x);
            SetFloat(MoveYHash, context.AnimationMove.y);
            SetFloat(ActionMoveXHash, context.ActionMove.x);
            SetFloat(ActionMoveYHash, context.ActionMove.y);
            SetBool(SprintIntentHash, context.SprintIntent);
            SetInteger(AttackInputTypeHash, (int)context.CurrentAttackInputType);
            SetInteger(ComboIndexHash, context.CurrentComboIndex);
            SetBool(IsAttackHitboxActiveHash, context.IsAttackHitboxActive);
            SetBool(IsEvadeInvincibleHash, context.IsEvadeInvincible);
            SetBool(IsPerfectEvadeWindowHash, context.IsPerfectEvadeWindow);
            SetBool(IsPerfectEvadeActiveHash, context.IsPerfectEvadeActive);
            SetInteger(PerfectEvadeDirectionHash, (int)context.PerfectEvadeDirection);
            SetBool(IsGuardBlockActiveHash, context.IsGuardBlockActive);
            SetBool(IsPerfectGuardWindowHash, context.IsPerfectGuardWindow);
            SetBool(IsGuardReentryHash, context.IsGuardReentry);
            SetBool(IsGuardWalkingHash, context.IsGuardWalking);
            SetInteger(GuardMoveDirectionHash, (int)context.GuardMoveDirection);
            SetBool(IsSkillSuperArmorHash, context.IsSkillSuperArmor);
            SetBool(IsSkillHitboxActiveHash, context.IsSkillHitboxActive);
            SetFloat(BetaEnergyHash, context.Resources.BetaEnergy);
            SetInteger(HitDirectionHash, (int)context.LastReceivedHitDirectionId);
            SetInteger(GuardReactionHash, (int)context.CurrentGuardReaction);
            SetInteger(KnockdownTypeHash, (int)context.CurrentKnockdownType);
            SetInteger(DeadTypeHash, (int)context.CurrentDeadType);
            SetBool(HasBufferedAttackInputHash, context.HasBufferedAttackInput);
            SetBool(HasBufferedEvadeInputHash, context.HasBufferedEvadeInput);
            SetBool(HasBufferedSkillInputHash, context.HasBufferedSkillInput);

            CrossFadeRepeatedStarterAttackIfNeeded(context);
            CrossFadePerfectEvadeIfNeeded(context);
            CrossFadeCombatReactionIfNeeded(context);
        }


        private void CacheParameters()
        {
            parameterHashes.Clear();

            if (animator == null)
            {
                return;
            }

            foreach (AnimatorControllerParameter parameter in animator.parameters)
            {
                parameterHashes.Add(parameter.nameHash);
            }
        }

        /// <summary>
        /// 设置 Integer 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetInteger(int hash, int value)
        {
            if (parameterHashes.Contains(hash))
            {
                animator.SetInteger(hash, value);
            }
        }

        /// <summary>
        /// 设置 Bool 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetBool(int hash, bool value)
        {
            if (parameterHashes.Contains(hash))
            {
                animator.SetBool(hash, value);
            }
        }

        /// <summary>
        /// 设置 Float 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetFloat(int hash, float value)
        {
            if (parameterHashes.Contains(hash))
            {
                animator.SetFloat(hash, value);
            }
        }

        /// <summary>
        /// 执行 Cross / Fade / Repeated / Starter / Attack / If / Needed 相关逻辑，并维护 玩家动画 模块的运行时一致性。
        /// </summary>
        private void CrossFadeRepeatedStarterAttackIfNeeded(PlayerStateContext context)
        {
            if (context.CurrentState != PlayerStateId.Attack || context.CurrentAttackInstanceId == 0)
            {
                lastAttackInstanceId = 0;
                lastAttackInputType = AttackInputType.None;
                lastAttackComboIndex = 0;
                return;
            }

            if (context.CurrentAttackInstanceId == lastAttackInstanceId)
            {
                return;
            }

            bool isRepeatedStarter =
                context.CurrentComboIndex == 1 &&
                lastAttackComboIndex == 1 &&
                context.CurrentAttackInputType == lastAttackInputType &&
                (context.CurrentAttackInputType == AttackInputType.Light ||
                 context.CurrentAttackInputType == AttackInputType.Heavy);

            if (isRepeatedStarter)
            {
                int stateHash = ResolveRepeatedStarterAttackStateHash(context.CurrentAttackInputType);
                if (stateHash != 0)
                {
                    animator.CrossFadeInFixedTime(stateHash, RepeatStarterAttackCrossFadeDurationSeconds, 0, 0f);
                }
            }

            lastAttackInstanceId = context.CurrentAttackInstanceId;
            lastAttackInputType = context.CurrentAttackInputType;
            lastAttackComboIndex = context.CurrentComboIndex;
        }

        /// <summary>
        /// 解析 Repeated / Starter / Attack / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveRepeatedStarterAttackStateHash(AttackInputType attackInputType)
        {
            switch (attackInputType)
            {
                case AttackInputType.Light:
                    return ResolveExistingStateHash(AttackLight1StateHashes);
                case AttackInputType.Heavy:
                    return ResolveExistingStateHash(AttackHeavy1StateHashes);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 执行 Cross / Fade / Perfect / Evade / If / Needed 相关逻辑，并维护 玩家动画 模块的运行时一致性。
        /// </summary>
        private void CrossFadePerfectEvadeIfNeeded(PlayerStateContext context)
        {
            if (context.CurrentState != PlayerStateId.Evade || !context.IsPerfectEvadeActive)
            {
                wasPerfectEvadeActive = false;
                lastPerfectEvadeDirection = PerfectEvadeDirectionId.None;
                return;
            }

            if (wasPerfectEvadeActive && lastPerfectEvadeDirection == context.PerfectEvadeDirection)
            {
                return;
            }

            int stateHash = ResolvePerfectEvadeStateHash(context.PerfectEvadeDirection);
            if (stateHash == 0)
            {
                wasPerfectEvadeActive = context.IsPerfectEvadeActive;
                lastPerfectEvadeDirection = context.PerfectEvadeDirection;
                return;
            }

            // 触发越晚，完美闪避动画越按固定秒数向后起播，减少“普通闪避短闪一下再硬切”的割裂感。
            float phase01 = Mathf.InverseLerp(
                PerfectEvadeWindowStart,
                PerfectEvadeWindowEnd,
                context.PerfectEvadeActivatedElapsed);
            float fixedTimeOffsetSeconds = Mathf.Clamp01(phase01) * PerfectEvadeMaxStartOffsetSeconds;
            animator.CrossFadeInFixedTime(
                stateHash,
                PerfectEvadeCrossFadeDurationSeconds,
                0,
                fixedTimeOffsetSeconds);

            wasPerfectEvadeActive = true;
            lastPerfectEvadeDirection = context.PerfectEvadeDirection;
        }

        /// <summary>
        /// 解析 Perfect / Evade / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolvePerfectEvadeStateHash(PerfectEvadeDirectionId direction)
        {
            switch (direction)
            {
                case PerfectEvadeDirectionId.Forward:
                    return ResolveExistingStateHash(PerfectEvadeForwardStateHashes);
                case PerfectEvadeDirectionId.Backward:
                    return ResolveExistingStateHash(PerfectEvadeBackwardStateHashes);
                case PerfectEvadeDirectionId.Left:
                    return ResolveExistingStateHash(PerfectEvadeLeftStateHashes);
                case PerfectEvadeDirectionId.Right:
                    return ResolveExistingStateHash(PerfectEvadeRightStateHashes);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 执行 Cross / Fade / Combat / Reaction / If / Needed 相关逻辑，并维护 玩家动画 模块的运行时一致性。
        /// </summary>
        private void CrossFadeCombatReactionIfNeeded(PlayerStateContext context)
        {
            int stateHash = ResolveReactionStateHash(context);
            if (stateHash == 0)
            {
                lastReactionStateHash = 0;
                return;
            }

            bool isNewGuardReaction = context.CurrentState == PlayerStateId.Guard &&
                context.GuardReactionRequestVersion != lastGuardReactionVersion;
            bool isNewCombatReaction = IsCombatReactionAnimationRequestState(context.CurrentState) &&
                context.CombatReactionAnimationRequestVersion != lastCombatReactionAnimationRequestVersion;
            if (!isNewGuardReaction && !isNewCombatReaction && lastReactionStateHash == stateHash)
            {
                return;
            }

            if (!animator.HasState(0, stateHash))
            {
                int fallbackHash = ResolveFallbackReactionStateHash(context);
                if (fallbackHash == 0 || fallbackHash == stateHash || !animator.HasState(0, fallbackHash))
                {
                    lastReactionStateHash = stateHash;
                    lastGuardReactionVersion = context.GuardReactionRequestVersion;
                    lastCombatReactionAnimationRequestVersion = context.CombatReactionAnimationRequestVersion;
                    return;
                }

                stateHash = fallbackHash;
            }

            if (!isNewGuardReaction && !isNewCombatReaction && lastReactionStateHash == stateHash)
            {
                return;
            }

            animator.CrossFadeInFixedTime(stateHash, ReactionCrossFadeDurationSeconds, 0, 0f);
            lastReactionStateHash = stateHash;
            lastGuardReactionVersion = context.GuardReactionRequestVersion;
            lastCombatReactionAnimationRequestVersion = context.CombatReactionAnimationRequestVersion;
        }

        /// <summary>
        /// 判断当前状态是否通过受击动画请求版本控制同动画重播。
        /// </summary>
        private static bool IsCombatReactionAnimationRequestState(PlayerStateId state)
        {
            return state == PlayerStateId.HitReaction || state == PlayerStateId.Knockdown;
        }

        /// <summary>
        /// 解析 Reaction / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveReactionStateHash(PlayerStateContext context)
        {
            switch (context.CurrentState)
            {
                case PlayerStateId.HitReaction:
                    return ResolveHitReactionStateHash(context.LastReceivedHitDirectionId);
                case PlayerStateId.Guard:
                    return ResolveGuardReactionStateHash(context.CurrentGuardReaction, context.LastReceivedHitDirectionId);
                case PlayerStateId.Knockdown:
                    return ResolveKnockdownStateHash(context.CurrentKnockdownType, context.CurrentPhase);
                case PlayerStateId.Dead:
                    return ResolveDeadStateHash(context.CurrentDeadType);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 解析 Hit / Reaction / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveHitReactionStateHash(PlayerHitDirectionId direction)
        {
            switch (direction)
            {
                case PlayerHitDirectionId.Front:
                    return ResolveExistingStateHash(HitReactionFrontStateHashes);
                case PlayerHitDirectionId.Back:
                    return ResolveExistingStateHash(HitReactionBackStateHashes);
                case PlayerHitDirectionId.Left:
                    return ResolveExistingStateHash(HitReactionLeftStateHashes);
                case PlayerHitDirectionId.Right:
                    return ResolveExistingStateHash(HitReactionRightStateHashes);
                default:
                    return ResolveExistingStateHash(HitReactionFallbackStateHashes);
            }
        }

        /// <summary>
        /// 解析 Guard / Reaction / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveGuardReactionStateHash(PlayerGuardReactionId reaction, PlayerHitDirectionId direction)
        {
            if (reaction == PlayerGuardReactionId.PerfectGuard)
            {
                return direction == PlayerHitDirectionId.Right
                    ? ResolveExistingStateHash(PerfectGuardRightStateHashes)
                    : ResolveExistingStateHash(PerfectGuardLeftStateHashes);
            }

            if (reaction != PlayerGuardReactionId.GuardHit)
            {
                return 0;
            }

            switch (direction)
            {
                case PlayerHitDirectionId.Left:
                    return ResolveExistingStateHash(GuardHitLeftStateHashes);
                case PlayerHitDirectionId.Right:
                    return ResolveExistingStateHash(GuardHitRightStateHashes);
                default:
                    return ResolveExistingStateHash(GuardHitBackStateHashes);
            }
        }

        /// <summary>
        /// 解析 Knockdown / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveKnockdownStateHash(PlayerKnockdownTypeId knockdownType, PlayerStatePhase phase)
        {
            switch (phase)
            {
                case PlayerStatePhase.Start:
                    return knockdownType == PlayerKnockdownTypeId.Forward
                        ? ResolveExistingStateHash(KnockdownStartForwardStateHashes)
                        : ResolveExistingStateHash(KnockdownStartBackwardStateHashes);
                case PlayerStatePhase.Loop:
                    return ResolveExistingStateHash(KnockdownLoopStateHashes);
                default:
                    return ResolveExistingStateHash(KnockdownEndStateHashes);
            }
        }

        /// <summary>
        /// 解析 Dead / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveDeadStateHash(PlayerDeadTypeId deadType)
        {
            if (deadType == PlayerDeadTypeId.Down)
            {
                return ResolveExistingStateHash(DeadDownStartStateHashes);
            }

            return ResolveExistingStateHash(DeadStandStateHashes);
        }

        /// <summary>
        /// 解析 Fallback / Reaction / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveFallbackReactionStateHash(PlayerStateContext context)
        {
            switch (context.CurrentState)
            {
                case PlayerStateId.HitReaction:
                    return ResolveExistingStateHash(HitReactionFallbackStateHashes);
                case PlayerStateId.Dead:
                    return ResolveExistingStateHash(DeadStandStateHashes);
                default:
                    return 0;
            }
        }

        /// <summary>
        /// 解析 Existing / State / Hash 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private int ResolveExistingStateHash(int[] candidateHashes)
        {
            for (int i = 0; i < candidateHashes.Length; i++)
            {
                int stateHash = candidateHashes[i];
                if (stateHash != 0 && animator.HasState(0, stateHash))
                {
                    return stateHash;
                }
            }

            return 0;
        }
    }
}
