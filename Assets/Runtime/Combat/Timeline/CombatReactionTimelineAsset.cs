// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    public abstract class CombatReactionTimelineAsset : CombatTimelineActionAsset
    {
        [SerializeField] private CombatTimelineReactionType reactionType = CombatTimelineReactionType.None;

        public CombatTimelineReactionType ReactionType => reactionType;

        /// <summary>
        /// 配置 Reaction 资产级类型；恢复节奏、Motion 和动画预览都只从 Timeline 窗口读取。
        /// </summary>
        /// <param name="nextReactionType">Reaction 的运行时类型，例如普通受击、击倒或死亡。</param>
        public void ConfigureReaction(CombatTimelineReactionType nextReactionType)
        {
            reactionType = nextReactionType;
        }

        /// <summary>
        /// 解析当前 Reaction Timeline，生成状态机和编辑器可读取的运行时配置。
        /// </summary>
        /// <returns>包含 Reaction 节奏、RuntimeSpec、Motion 引用和 Animation Preview 窗口解析结果的配置。</returns>
        public CombatTimelineReactionConfig ToReactionConfig()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            bool requiresPhaseWindows = reactionType != CombatTimelineReactionType.Dead;
            float nextStunDuration = TotalDuration;
            float nextRecoveryStartTime = TotalDuration;
            float nextCanReturnTime = TotalDuration;
            if (requiresPhaseWindows)
            {
                nextStunDuration = ResolveRequiredWindowDuration(runtimeSpec, CombatTimelineCapabilityId.Reaction_Stun);
                nextRecoveryStartTime = ResolveRequiredWindowStart(runtimeSpec, CombatTimelineCapabilityId.Reaction_Recovery);
                nextCanReturnTime = ResolveRequiredWindowStart(runtimeSpec, CombatTimelineCapabilityId.Reaction_CanReturn);
            }

            PlayerActionMotionId nextMotionId = PlayerActionMotionId.None;
            AnimationClip nextClip = null;
            string nextAnimationStateName = AnimationStateName;
            float nextClipStartOffset = 0f;
            float nextClipSpeed = 1f;
            bool nextLoopPreview = false;
            string nextBossMotionActionId = string.Empty;
            string nextBossMotionWindowName = string.Empty;

            foreach (CombatTimelineClip clip in EnumerateAllClips())
            {
                if (clip is CombatReactionWindowClip reactionClip)
                {
                    if (reactionClip.ReactionMotionId != PlayerActionMotionId.None)
                    {
                        nextMotionId = reactionClip.ReactionMotionId;
                    }
                }
                else if (clip is CombatMotionReferenceClip motionClip &&
                    motionClip.MotionKind == CombatTimelineClipKind.PlayerMotion &&
                    motionClip.PlayerMotionId != PlayerActionMotionId.None)
                {
                    nextMotionId = motionClip.PlayerMotionId;
                }
                else if (clip is CombatMotionReferenceClip bossMotionClip &&
                    bossMotionClip.MotionKind == CombatTimelineClipKind.BossCodeMove &&
                    !string.IsNullOrEmpty(bossMotionClip.BossAttackId))
                {
                    nextBossMotionActionId = bossMotionClip.BossAttackId;
                    nextBossMotionWindowName = bossMotionClip.ProfileWindowName ?? string.Empty;
                }
                else if (clip is CombatAnimationClipWindow animationClip)
                {
                    if (animationClip.PreviewAnimationClip != null)
                    {
                        nextClip = animationClip.PreviewAnimationClip;
                    }

                    if (!string.IsNullOrEmpty(animationClip.AnimatorStateName))
                    {
                        nextAnimationStateName = animationClip.AnimatorStateName;
                    }

                    nextClipStartOffset = animationClip.ClipStartOffset;
                    nextClipSpeed = animationClip.ClipSpeed;
                    nextLoopPreview = animationClip.LoopPreview;
                }
            }

            return new CombatTimelineReactionConfig(
                ActionId,
                reactionType,
                TotalDuration,
                runtimeSpec,
                nextStunDuration,
                nextRecoveryStartTime,
                nextCanReturnTime,
                nextMotionId,
                nextBossMotionActionId,
                nextBossMotionWindowName,
                nextAnimationStateName,
                nextClip,
                nextClipStartOffset,
                nextClipSpeed,
                nextLoopPreview);
        }

        /// <summary>
        /// 读取必需 Reaction capability 的窗口时长。
        /// </summary>
        /// <param name="runtimeSpec">从当前 Timeline 构建出的运行时窗口规格。</param>
        /// <param name="capabilityId">要读取的 Reaction capability。</param>
        /// <returns>找到窗口时返回窗口持续时间，单位秒；缺失时记录错误并返回 0。</returns>
        private float ResolveRequiredWindowDuration(CombatActionRuntimeSpec runtimeSpec, CombatTimelineCapabilityId capabilityId)
        {
            if (runtimeSpec.TryGetEarliestWindow(capabilityId, out ProjectEVE.Player.Windows.ActionWindow window))
            {
                return window.EndTime - window.StartTime;
            }

            Debug.LogError($"Combat Timeline reaction '{ActionId}' is missing required capability '{capabilityId}'.");
            return 0f;
        }

        /// <summary>
        /// 读取必需 Reaction capability 的窗口起始时间。
        /// </summary>
        /// <param name="runtimeSpec">从当前 Timeline 构建出的运行时窗口规格。</param>
        /// <param name="capabilityId">要读取的 Reaction capability。</param>
        /// <returns>找到窗口时返回窗口起点，单位秒；缺失时记录警告并返回 0。</returns>
        private float ResolveRequiredWindowStart(CombatActionRuntimeSpec runtimeSpec, CombatTimelineCapabilityId capabilityId)
        {
            if (runtimeSpec.TryGetEarliestWindow(capabilityId, out ProjectEVE.Player.Windows.ActionWindow window))
            {
                return window.StartTime;
            }

            Debug.LogWarning($"Combat Timeline reaction '{ActionId}' is missing required capability '{capabilityId}'.");
            return 0f;
        }
    }
}
