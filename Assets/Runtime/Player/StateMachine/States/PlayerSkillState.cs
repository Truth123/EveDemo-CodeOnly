// 文件说明：维护玩家 Skill 状态的 Timeline 求值、输入缓存、取消派生和 Skill HitNode 上下文。
// 所属模块：玩家状态机。
// 运行影响：影响 Skill1 资源消耗、霸体、三段 HitNode、分段吸附、分段出手特效、取消窗口和调试显示。

using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Player.States
{
    /// <summary>
    /// Skill1 状态。进入时获取 Provider Data，每帧通过 RuntimeSpec 生成状态本地 FrameData。
    /// </summary>
    public sealed class PlayerSkillState : PlayerStateBase
    {
        private PlayerSkillActionData currentSkill;
        private float skillStartTime;
        private int nextSkillCastId;
        private bool failedToStart;
        private PlayerActionMotionId activeMotionWindowId;
        private readonly PlayerSkillEffectController skillEffectController;

        public PlayerSkillState(PlayerSkillEffectController skillEffectController = null) : base(PlayerStateId.Skill)
        {
            this.skillEffectController = skillEffectController;
        }

        public override void Enter(PlayerStateContext context)
        {
            currentSkill = null;
            failedToStart = false;
            activeMotionWindowId = PlayerActionMotionId.None;

            if (!PlayerSkillActionResolver.TryGetSkill1(out PlayerSkillActionData skillData))
            {
                failedToStart = true;
                context.CurrentPhase = PlayerStatePhase.Reset;
                return;
            }

            if (!StartSkillCast(context, skillData))
            {
                failedToStart = true;
                context.CurrentPhase = PlayerStatePhase.Reset;
            }
        }

        public override PlayerStateId Tick(PlayerStateContext context, float deltaTime)
        {
            _ = deltaTime;
            if (failedToStart || currentSkill == null)
            {
                return ReturnToIdleOrLocomotion(context);
            }

            float elapsed = context.StateElapsedTime;
            SkillFrameData frame = EvaluateFrame(currentSkill, elapsed);
            UpdateSkillPhaseAndWindows(context, frame);
            UpdateSkillMotionWindow(context, frame);
            RecordBufferedInputs(context, frame);

            PlayerStateId cancelState = TryConsumeCancelOrRestart(context, frame);
            if (cancelState != PlayerStateId.None)
            {
                return cancelState;
            }

            return elapsed >= frame.Duration
                ? ReturnToIdleOrLocomotion(context)
                : PlayerStateId.None;
        }

        public override void Exit(PlayerStateContext context)
        {
            skillEffectController?.StopAll();
            currentSkill = null;
            activeMotionWindowId = PlayerActionMotionId.None;
            context.ClearSkillRuntime();
            context.ClearActionWindows();
            PlayerActionInputRouter.RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 启动一次 Skill 释放，负责消耗资源、重置计时和写入 Skill 上下文。
        /// </summary>
        /// <param name="context">当前玩家状态上下文，提供资源与输入并接收 Skill 运行时字段。</param>
        /// <param name="skillData">Provider 解析出的 Skill1 静态数据与 RuntimeSpec。</param>
        /// <returns>资源足够且 Skill 数据有效时返回 true；否则返回 false。</returns>
        private bool StartSkillCast(PlayerStateContext context, PlayerSkillActionData skillData)
        {
            if (skillData == null || !context.TrySpendBetaEnergy(skillData.SkillCost))
            {
                return false;
            }

            currentSkill = skillData;
            failedToStart = false;
            skillStartTime = context.Input.Time;
            context.StateElapsedTime = 0f;
            context.ClearActionWindowsAndBuffers();

            context.CurrentPhase = PlayerStatePhase.Start;
            context.CurrentSkillCastId = CreateSkillCastId();
            context.CurrentSkillHitNodeId = string.Empty;
            context.CurrentSkillHitIndex = 0;
            context.CurrentSkillDamage = 0f;
            context.CurrentSkillPoiseDamage = 0f;
            context.CurrentSkillReactionIntent = CombatReactionIntent.None;
            context.SkillContactResult = AttackContactResult.None;
            context.ActionWorldDirection = Vector3.zero;
            context.HasActionWorldDirection = false;
            context.ActionInputMove = context.Input.Move.sqrMagnitude > 0.0001f
                ? context.Input.Move.normalized
                : Vector2.zero;
            context.ActionMove = context.Input.Move.sqrMagnitude > 0.0001f
                ? context.Input.Move.normalized
                : Vector2.up;
            activeMotionWindowId = PlayerActionMotionId.None;
            skillEffectController?.StopAll();
            return true;
        }

        /// <summary>
        /// 在 Timeline PlayerMotion 窗口首次激活时发起一次动作位移请求，并在窗口结束后允许下一窗口重新触发。
        /// </summary>
        /// <param name="context">当前玩家状态上下文，用于提交动作位移请求。</param>
        /// <param name="frame">当前 Skill 帧数据，包含激活的 Player Motion ID。</param>
        private void UpdateSkillMotionWindow(PlayerStateContext context, SkillFrameData frame)
        {
            if (frame.ActivePlayerMotionId == PlayerActionMotionId.None)
            {
                activeMotionWindowId = PlayerActionMotionId.None;
                return;
            }

            if (activeMotionWindowId == frame.ActivePlayerMotionId)
            {
                return;
            }

            activeMotionWindowId = frame.ActivePlayerMotionId;
            skillEffectController?.PlaySegment();
            context.RequestActionMotion(frame.ActivePlayerMotionId);
        }

        /// <summary>
        /// 创建 Skill Cast ID，用于多段 HitNode 命中去重。
        /// </summary>
        private int CreateSkillCastId()
        {
            if (nextSkillCastId == int.MaxValue)
            {
                nextSkillCastId = 0;
            }

            return ++nextSkillCastId;
        }

        /// <summary>
        /// 更新 Skill Phase、窗口和当前 HitNode，并写回 PlayerStateContext。
        /// </summary>
        private static void UpdateSkillPhaseAndWindows(PlayerStateContext context, SkillFrameData frame)
        {
            ApplyFrameData(context, frame);
        }

        private void RecordBufferedInputs(PlayerStateContext context, SkillFrameData frame)
        {
            float actionEndTime = skillStartTime + currentSkill.TotalDuration;

            PlayerActionInputRouter.CaptureEvadeInput(context, frame.CanBufferEvade, actionEndTime);
            PlayerActionInputRouter.CaptureSkillInput(context, frame.CanBufferSkill, actionEndTime);
        }

        private PlayerStateId TryConsumeCancelOrRestart(PlayerStateContext context, SkillFrameData frame)
        {
            if (frame.CanCancelToEvade && PlayerActionInputRouter.TryConsumeEvade(context, true))
            {
                return PlayerStateId.Evade;
            }

            if (frame.CanCancelToSkill && PlayerActionInputRouter.TryConsumeSkill(context, true, currentSkill))
            {
                if (!StartSkillCast(context, currentSkill))
                {
                    failedToStart = true;
                    return ReturnToIdleOrLocomotion(context);
                }

                return PlayerStateId.None;
            }

            if (frame.CanResetAttack && frame.CanRestartAttack &&
                PlayerActionInputRouter.TryConsumeAttack(context, true, false, out _))
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

        /// <summary>
        /// 求值 Skill RuntimeSpec，并把当前 Phase、HitNode、取消能力和 PlayerMotion ID 整理为状态本地帧数据。
        /// </summary>
        /// <param name="skillData">当前 Skill1 的 Provider Data。</param>
        /// <param name="elapsed">当前 Skill 已经过时间，单位为秒。</param>
        /// <returns>只包含本帧 Skill 状态实际消费字段的帧数据。</returns>
        private static SkillFrameData EvaluateFrame(PlayerSkillActionData skillData, float elapsed)
        {
            CombatActionCapabilitySnapshot snapshot = skillData.RuntimeSpec.Evaluate(elapsed);
            bool hasHitNode = false;
            CombatHitNodeData activeHitNode = default;
            if (snapshot.ActiveHitNodeIds.Length > 0 &&
                skillData.TryGetHitNode(snapshot.ActiveHitNodeIds[0], out CombatHitNodeData hitNode))
            {
                hasHitNode = true;
                activeHitNode = hitNode;
            }

            PlayerActionMotionId activePlayerMotionId = snapshot.ActivePlayerMotionIds.Length > 0
                ? snapshot.ActivePlayerMotionIds[0]
                : PlayerActionMotionId.None;

            return new SkillFrameData(
                snapshot.Phase,
                skillData.TotalDuration,
                hasHitNode,
                activeHitNode,
                snapshot.SuperArmor,
                snapshot.Uninterruptible,
                snapshot.CanBufferEvade,
                snapshot.CanBufferSkill,
                snapshot.CanCancelToEvade,
                snapshot.CanCancelToSkill,
                snapshot.CanCancelToGuard,
                snapshot.CanResetAttack,
                snapshot.CanResetAttack,
                snapshot.CanMoveCancel,
                activePlayerMotionId);
        }

        private static void ApplyFrameData(PlayerStateContext context, SkillFrameData skill)
        {
            context.CurrentPhase = skill.Phase;
            context.IsSkillSuperArmor = skill.SuperArmor;
            context.IsActionUninterruptible = skill.Uninterruptible;
            context.IsResetWindow = skill.CanResetAttack;
            context.IsEvadeCancelWindow = skill.CanCancelToEvade;
            context.IsSkillCancelWindow = skill.CanCancelToSkill;
            context.IsGuardCancelWindow = skill.CanCancelToGuard;
            context.IsMoveCancelWindow = skill.CanMoveCancel;
            ApplyActiveHitNode(context, skill);
        }

        private static void ApplyActiveHitNode(PlayerStateContext context, SkillFrameData skill)
        {
            if (skill.HasActiveHitNode)
            {
                CombatHitNodeData hitNode = skill.ActiveHitNode;
                context.IsSkillHitboxActive = true;
                context.CurrentSkillHitNodeId = hitNode.HitNodeId;
                context.CurrentSkillHitIndex = hitNode.HitIndex;
                context.CurrentSkillDamage = hitNode.Damage;
                context.CurrentSkillPoiseDamage = hitNode.PoiseDamage;
                context.CurrentSkillReactionIntent = hitNode.ReactionIntent;
                return;
            }

            context.IsSkillHitboxActive = false;
            context.CurrentSkillHitNodeId = string.Empty;
            context.CurrentSkillHitIndex = 0;
            context.CurrentSkillDamage = 0f;
            context.CurrentSkillPoiseDamage = 0f;
            context.CurrentSkillReactionIntent = CombatReactionIntent.None;
        }

        private readonly struct SkillFrameData
        {
            public SkillFrameData(
                PlayerStatePhase phase,
                float duration,
                bool hasActiveHitNode,
                CombatHitNodeData activeHitNode,
                bool superArmor,
                bool uninterruptible,
                bool canBufferEvade,
                bool canBufferSkill,
                bool canCancelToEvade,
                bool canCancelToSkill,
                bool canCancelToGuard,
                bool canResetAttack,
                bool canRestartAttack,
                bool canMoveCancel,
                PlayerActionMotionId activePlayerMotionId)
            {
                Phase = phase;
                Duration = duration;
                HasActiveHitNode = hasActiveHitNode;
                ActiveHitNode = activeHitNode;
                SuperArmor = superArmor;
                Uninterruptible = uninterruptible;
                CanBufferEvade = canBufferEvade;
                CanBufferSkill = canBufferSkill;
                CanCancelToEvade = canCancelToEvade;
                CanCancelToSkill = canCancelToSkill;
                CanCancelToGuard = canCancelToGuard;
                CanResetAttack = canResetAttack;
                CanRestartAttack = canRestartAttack;
                CanMoveCancel = canMoveCancel;
                ActivePlayerMotionId = activePlayerMotionId;
            }

            public PlayerStatePhase Phase { get; }
            public float Duration { get; }
            public bool HasActiveHitNode { get; }
            public CombatHitNodeData ActiveHitNode { get; }
            public bool SuperArmor { get; }
            public bool Uninterruptible { get; }
            public bool CanBufferEvade { get; }
            public bool CanBufferSkill { get; }
            public bool CanCancelToEvade { get; }
            public bool CanCancelToSkill { get; }
            public bool CanCancelToGuard { get; }
            public bool CanResetAttack { get; }
            public bool CanRestartAttack { get; }
            public bool CanMoveCancel { get; }
            public PlayerActionMotionId ActivePlayerMotionId { get; }
        }
    }
}
