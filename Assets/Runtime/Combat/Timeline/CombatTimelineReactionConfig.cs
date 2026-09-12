// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    public readonly struct CombatTimelineReactionConfig
    {
        /// <summary>
        /// 创建 Reaction 运行时配置；预览字段承载 Animation Preview 窗口解析结果。
        /// </summary>
        /// <param name="reactionId">Reaction Timeline 的动作 ID。</param>
        /// <param name="reactionType">Reaction 的运行时类型。</param>
        /// <param name="totalDuration">Reaction Timeline 总时长，单位秒。</param>
        /// <param name="runtimeSpec">从 Timeline clips 构建出的能力窗口规格。</param>
        /// <param name="stunDuration">硬直阶段持续时间，单位秒。</param>
        /// <param name="recoveryStartTime">恢复阶段起点，单位秒。</param>
        /// <param name="canReturnTime">可返回阶段起点，单位秒。</param>
        /// <param name="motionId">Player Reaction 请求的动作 MotionId。</param>
        /// <param name="bossMotionActionId">Boss Reaction CodeMove 引用的 MotionProfile action id。</param>
        /// <param name="bossMotionWindowName">Boss Reaction CodeMove 引用的 MotionProfile window name。</param>
        /// <param name="animationStateName">Animation Preview 窗口或资产身份提供的 Animator state 名称。</param>
        /// <param name="previewAnimationClip">Animation Preview 窗口提供的预览 clip；缺失时为 null。</param>
        /// <param name="clipStartOffset">Animation Preview 窗口提供的源片段起点，单位秒。</param>
        /// <param name="clipSpeed">Animation Preview 窗口提供的采样速度倍率。</param>
        /// <param name="loopPreview">Animation Preview 窗口是否循环采样。</param>
        public CombatTimelineReactionConfig(
            string reactionId,
            CombatTimelineReactionType reactionType,
            float totalDuration,
            CombatActionRuntimeSpec runtimeSpec,
            float stunDuration,
            float recoveryStartTime,
            float canReturnTime,
            PlayerActionMotionId motionId,
            string bossMotionActionId,
            string bossMotionWindowName,
            string animationStateName,
            AnimationClip previewAnimationClip,
            float clipStartOffset,
            float clipSpeed,
            bool loopPreview)
        {
            ReactionId = reactionId;
            ReactionType = reactionType;
            TotalDuration = Mathf.Max(0.01f, totalDuration);
            RuntimeSpec = runtimeSpec;
            StunDuration = Mathf.Clamp(stunDuration, 0f, TotalDuration);
            RecoveryStartTime = Mathf.Clamp(recoveryStartTime, 0f, TotalDuration);
            CanReturnTime = Mathf.Clamp(canReturnTime, 0f, TotalDuration);
            MotionId = motionId;
            BossMotionActionId = bossMotionActionId ?? string.Empty;
            BossMotionWindowName = bossMotionWindowName ?? string.Empty;
            AnimationStateName = animationStateName ?? string.Empty;
            PreviewAnimationClip = previewAnimationClip;
            ClipStartOffset = Mathf.Max(0f, clipStartOffset);
            ClipSpeed = Mathf.Max(0.01f, clipSpeed);
            LoopPreview = loopPreview;
        }

        public string ReactionId { get; }
        public CombatTimelineReactionType ReactionType { get; }
        public float TotalDuration { get; }
        public CombatActionRuntimeSpec RuntimeSpec { get; }
        public float StunDuration { get; }
        public float RecoveryStartTime { get; }
        public float CanReturnTime { get; }
        public PlayerActionMotionId MotionId { get; }
        public string BossMotionActionId { get; }
        public string BossMotionWindowName { get; }
        public bool HasBossCodeMove => !string.IsNullOrEmpty(BossMotionActionId);
        public string AnimationStateName { get; }
        public AnimationClip PreviewAnimationClip { get; }
        public float ClipStartOffset { get; }
        public float ClipSpeed { get; }
        public bool LoopPreview { get; }
    }
}
