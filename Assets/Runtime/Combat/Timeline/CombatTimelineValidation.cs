// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Combat;
using ProjectEVE.Player;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    public enum CombatTimelineValidationSeverity
    {
        Info = 0,
        Warning = 1,
        Error = 2
    }

    public readonly struct CombatTimelineValidationMessage
    {
        /// <summary>
        /// 创建 CombatTimelineValidationMessage 实例，并准备 Combat Timeline 模块需要的初始状态。
        /// </summary>
        public CombatTimelineValidationMessage(CombatTimelineValidationSeverity severity, string message)
        {
            Severity = severity;
            Message = message;
        }

        public CombatTimelineValidationSeverity Severity { get; }
        public string Message { get; }
    }

    public static class CombatTimelineValidator
    {
        /// <summary>
        /// 校验 Validate 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        public static List<CombatTimelineValidationMessage> Validate(CombatTimelineActionAsset timeline, CombatTimelineCatalog catalog = null)
        {
            List<CombatTimelineValidationMessage> messages = new List<CombatTimelineValidationMessage>();
            if (timeline == null)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "Timeline asset is null."));
                return messages;
            }

            if (string.IsNullOrEmpty(timeline.ActionId))
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "ActionId is empty."));
            }

            float timelineTolerance = GetTimelineTimeTolerance(timeline);
            float latestClipEnd = 0f;
            CombatTimelineClip activeClip = null;
            foreach (CombatTimelineClip clip in timeline.EnumerateAllClips())
            {
                if (clip is CombatAnimationClipWindow animationClip)
                {
                    ValidateAnimationPreviewClip(animationClip, timeline, messages);
                    latestClipEnd = Mathf.Max(latestClipEnd, Mathf.Min(timeline.TotalDuration, animationClip.EndTime));
                }
                else
                {
                    if (clip.EndTime <= clip.StartTime)
                    {
                        messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: EndTime <= StartTime."));
                    }

                    if (clip.EndTime > timeline.TotalDuration + timelineTolerance)
                    {
                        messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, FormatClipExceedsDurationMessage(clip, timeline)));
                    }

                    latestClipEnd = Mathf.Max(latestClipEnd, clip.EndTime);
                }
                if (clip.CapabilityId == CombatTimelineCapabilityId.Hit_Attack && clip is not CombatPhaseClip)
                {
                    activeClip = clip;
                }

                if (!CombatTimelineCapabilityUtility.IsCapabilityCompatibleWithClip(clip))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: Capability {clip.CapabilityId} does not match clip family {clip.ClipKind}."));
                }
                if (clip.ClipKind == CombatTimelineClipKind.HitNode && string.IsNullOrEmpty(clip.Name))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "HitNode has empty id/name."));
                }

                if (clip is CombatHitNodeClip hitNodeClip &&
                    hitNodeClip.ReactionIntent == CombatReactionIntent.None)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: HitNode ReactionIntent is None."));
                }

                if (clip is BossReactionGateWindowClip bossGateClip &&
                    (bossGateClip.AttackTypes == null || bossGateClip.AttackTypes.Length == 0))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: Boss reaction gate has no attack types."));
                }

                if (clip.ClipKind == CombatTimelineClipKind.CancelWindow &&
                    activeClip != null &&
                    clip.StartTime + 0.001f < activeClip.EndTime &&
                    clip.CapabilityId != CombatTimelineCapabilityId.Cancel_Commit)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: cancel window starts before Active ends."));
                }

                if (clip.CapabilityId == CombatTimelineCapabilityId.Defense_PerfectGuard && clip.Duration > 0.35f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: PerfectGuard window is unusually long."));
                }

                if (clip.CapabilityId == CombatTimelineCapabilityId.Defense_PerfectEvade && clip.Duration > 0.35f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: PerfectEvade window is unusually long."));
                }
            }

            if (latestClipEnd > timeline.TotalDuration + timelineTolerance)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, FormatLastClipExceedsDurationMessage(latestClipEnd, timeline)));
            }

            if (timeline is PlayerAttackTimelineAsset playerAttack)
            {
                ValidatePlayerAttackTimeline(playerAttack, messages);
                if (catalog != null)
                {
                    ValidateNextNode(playerAttack.NextLightNodeId, "NextLight", catalog, messages);
                    ValidateNextNode(playerAttack.NextHeavyNodeId, "NextHeavy", catalog, messages);
                }
            }

            if (timeline.ActionKind == CombatTimelineActionKind.PlayerSkill)
            {
                ValidatePlayerSkillTimeline(timeline, messages);
            }

            if (timeline.ActionKind == CombatTimelineActionKind.PlayerEvade)
            {
                ValidatePlayerEvadeTimeline(timeline, messages);
            }

            if (timeline.ActionKind == CombatTimelineActionKind.PlayerGuard)
            {
                ValidatePlayerGuardTimeline(timeline, messages);
            }

            if (timeline.ActionKind == CombatTimelineActionKind.PlayerSkill && timeline.ActionId == "Skill1")
            {
                bool thirdKnockdown = false;
                foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
                {
                    if (clip.HitIndex == 3 && clip.ReactionIntent == CombatReactionIntent.Knockdown)
                    {
                        thirdKnockdown = true;
                    }
                }

                if (!thirdKnockdown)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, "Skill1 third HitNode is not Knockdown."));
                }
            }

            if (timeline.ActionKind == CombatTimelineActionKind.BossAttack)
            {
                ValidateBossTimeline(timeline, messages);
            }

            if (timeline.ActionKind == CombatTimelineActionKind.BossReposition)
            {
                ValidateBossRepositionTimeline(timeline, messages);
            }

            if (timeline.ActionKind == CombatTimelineActionKind.PlayerReaction ||
                timeline.ActionKind == CombatTimelineActionKind.BossReaction)
            {
                ValidateReactionTimeline(timeline, messages);
            }

            return messages;
        }

        /// <summary>
        /// 校验 Catalog 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        public static List<CombatTimelineValidationMessage> ValidateCatalog(CombatTimelineCatalog catalog)
        {
            List<CombatTimelineValidationMessage> messages = new List<CombatTimelineValidationMessage>();
            if (catalog == null)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "Catalog is null."));
                return messages;
            }

            HashSet<string> ids = new HashSet<string>();
            CombatTimelineActionAsset[] timelines = catalog.Timelines;
            if (timelines == null)
            {
                return messages;
            }

            for (int i = 0; i < timelines.Length; i++)
            {
                CombatTimelineActionAsset timeline = timelines[i];
                if (timeline == null)
                {
                    continue;
                }

                if (!ids.Add(timeline.ActionId))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Duplicate ActionId: {timeline.ActionId}."));
                }

                messages.AddRange(Validate(timeline, catalog));
            }

            return messages;
        }

        /// <summary>
        /// 执行 Format / Clip / Exceeds / Duration / Message 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        private static string FormatClipExceedsDurationMessage(CombatTimelineClip clip, CombatTimelineActionAsset timeline)
        {
            float frameRate = Mathf.Max(1f, timeline.FrameRate);
            int clipEndFrame = Mathf.RoundToInt(clip.EndTime * frameRate);
            return $"{clip.Name}: clip ends at {clipEndFrame}f ({clip.EndTime:0.###}s) but action duration is {timeline.TotalFrames}f ({timeline.TotalDuration:0.###}s).";
        }

        /// <summary>
        /// 执行 Format / Last / Clip / Exceeds / Duration / Message 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        private static string FormatLastClipExceedsDurationMessage(float latestClipEnd, CombatTimelineActionAsset timeline)
        {
            float frameRate = Mathf.Max(1f, timeline.FrameRate);
            int latestClipEndFrame = Mathf.RoundToInt(latestClipEnd * frameRate);
            return $"Last clip ends at {latestClipEndFrame}f ({latestClipEnd:0.###}s) but action duration is {timeline.TotalFrames}f ({timeline.TotalDuration:0.###}s).";
        }

        /// <summary>
        /// 获取 Timeline / Time / Tolerance 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
        /// </summary>
        private static float GetTimelineTimeTolerance(CombatTimelineActionAsset timeline)
        {
            float frameRate = timeline != null ? Mathf.Max(1f, timeline.FrameRate) : 60f;
            return Mathf.Max(0.0005f, 0.01f / frameRate);
        }

        /// <summary>
        /// 校验 Animation / Preview / Clip 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private static void ValidateAnimationPreviewClip(CombatAnimationClipWindow clip, CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            if (clip.StartTime < -0.0001f || clip.StartTime > timeline.TotalDuration + 0.0001f)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: Animation Preview Start must be within Timeline."));
            }

            float speed = Mathf.Max(0.01f, clip.ClipSpeed);
            float minSourceDuration = (1f / Mathf.Max(1f, timeline.FrameRate)) * speed;
            if (clip.PreviewAnimationClip == null || clip.PreviewAnimationClip.length <= 0.001f)
            {
                float sourceDuration = Mathf.Max(clip.ClipEndOffset - clip.ClipStartOffset, clip.EndTime - clip.StartTime);
                if (sourceDuration <= 0.001f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: Animation Preview clip is missing and has no source duration."));
                }

                return;
            }

            float clipLength = clip.PreviewAnimationClip.length;
            float sourceEnd = clip.ClipEndOffset > clip.ClipStartOffset + 0.0001f
                ? Mathf.Clamp(clip.ClipEndOffset, 0f, clipLength)
                : clipLength;

            if (clip.ClipStartOffset < -0.0001f || clip.ClipStartOffset > clipLength + 0.0001f)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: ClipStartOffset is outside AnimationClip length."));
            }

            if (clip.ClipEndOffset > clipLength + 0.0001f)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: ClipEndOffset is outside AnimationClip length."));
            }

            if (sourceEnd <= clip.ClipStartOffset + minSourceDuration - 0.0001f)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: Animation source range is shorter than one frame."));
            }
        }
        /// <summary>
        /// 校验 Next / Node 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private static void ValidateNextNode(string nodeId, string label, CombatTimelineCatalog catalog, List<CombatTimelineValidationMessage> messages)
        {
            if (string.IsNullOrEmpty(nodeId))
            {
                return;
            }

            if (!catalog.TryGetTimeline(nodeId, out _))
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{label} points to missing node: {nodeId}."));
            }
        }

        /// <summary>
        /// 校验 Player / Attack / Timeline 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private static void ValidatePlayerAttackTimeline(PlayerAttackTimelineAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Hit_Attack, "Hit_Attack", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_Commit, "Cancel_Commit", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_AttackBuffer, "Input_AttackBuffer", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_AttackCombo, "Cancel_AttackCombo", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_EvadeBuffer, "Input_EvadeBuffer", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToEvade, "Cancel_ToEvade", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_SkillBuffer, "Input_SkillBuffer", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToSkill, "Cancel_ToSkill", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToGuard, "Cancel_ToGuard", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_AttackReset, "Cancel_AttackReset", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_MovementReturn, "Cancel_MovementReturn", messages);
            List<CombatPhaseClip> phases = new List<CombatPhaseClip>();
            foreach (CombatPhaseClip phaseClip in timeline.EnumerateClips<CombatPhaseClip>())
            {
                phases.Add(phaseClip);
            }

            if (phases.Count == 0)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "PlayerAttack requires a Phase track for CurrentPhase."));
                return;
            }

            PlayerStatePhase[] requiredPhases = { PlayerStatePhase.Start, PlayerStatePhase.Active, PlayerStatePhase.Recovery, PlayerStatePhase.Reset };
            for (int i = 0; i < requiredPhases.Length; i++)
            {
                bool found = false;
                for (int j = 0; j < phases.Count; j++)
                {
                    if (phases[j].Phase == requiredPhases[i])
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"PlayerAttack missing Phase clip: {requiredPhases[i]}."));
                }
            }

            phases.Sort((left, right) => left.StartTime.CompareTo(right.StartTime));
            float tolerance = GetTimelineTimeTolerance(timeline);
            float cursor = 0f;
            for (int i = 0; i < phases.Count; i++)
            {
                CombatPhaseClip phase = phases[i];
                if (phase.StartTime > cursor + tolerance)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Phase gap before {phase.Name}: {cursor:0.###}s to {phase.StartTime:0.###}s."));
                }
                else if (phase.StartTime < cursor - tolerance)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Phase overlap at {phase.Name}: starts before previous phase ends."));
                }

                cursor = Mathf.Max(cursor, phase.EndTime);
            }

            if (cursor < timeline.TotalDuration - tolerance)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Phase track ends at {cursor:0.###}s but action duration is {timeline.TotalDuration:0.###}s."));
            }
        }

        /// <summary>
        /// 要求 RuntimeSpec 中存在指定能力，供 PlayerAttack 等已迁移路径校验。
        /// </summary>
        private static void RequireCapability(CombatActionRuntimeSpec runtimeSpec, CombatTimelineCapabilityId capabilityId, string label, List<CombatTimelineValidationMessage> messages)
        {
            if (!runtimeSpec.HasCapability(capabilityId))
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Missing capability: {label}."));
            }
        }
        /// <summary>
        /// 校验 PlayerSkill Timeline 的关键能力窗口。
        /// </summary>
        private static void ValidatePlayerSkillTimeline(CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Hit_Attack, "PlayerSkill Hit_Attack", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Armor_SuperArmor, "PlayerSkill Armor_SuperArmor", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_EvadeBuffer, "PlayerSkill Input_EvadeBuffer", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToEvade, "PlayerSkill Cancel_ToEvade", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToSkill, "PlayerSkill Cancel_ToSkill", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToGuard, "PlayerSkill Cancel_ToGuard", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_AttackReset, "PlayerSkill Cancel_AttackReset", messages);
        }

        /// <summary>
        /// 校验 PlayerEvade Timeline 的关键能力窗口。
        /// </summary>
        private static void ValidatePlayerEvadeTimeline(CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Defense_Invincible, "PlayerEvade Defense_Invincible", messages);
            if (timeline.ActionId == "Evade_Normal")
            {
                RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Defense_PerfectEvade, "PlayerEvade Defense_PerfectEvade", messages);
            }

            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_SkillBuffer, "PlayerEvade Input_SkillBuffer", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToSkill, "PlayerEvade Cancel_ToSkill", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_ToGuard, "PlayerEvade Cancel_ToGuard", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_AttackReset, "PlayerEvade Cancel_AttackReset", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Cancel_MovementReturn, "PlayerEvade Cancel_MovementReturn", messages);
        }

        /// <summary>
        /// 校验 PlayerGuard Timeline 的关键能力窗口。
        /// </summary>
        private static void ValidatePlayerGuardTimeline(CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            if (timeline is not PlayerGuardTimelineAsset guardTimeline)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "PlayerGuard timeline uses an invalid asset type."));
                return;
            }

            CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Defense_GuardBlock, "PlayerGuard Defense_GuardBlock", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Defense_PerfectGuard, "PlayerGuard Defense_PerfectGuard", messages);
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Input_PerfectGuardChain, "PlayerGuard Input_PerfectGuardChain", messages);
            RequireGuardMarker(guardTimeline, PlayerGuardTimelineAsset.GuardHitReactionMarkerId, "PlayerGuard GuardHitReaction marker", messages);
            RequireGuardMarker(guardTimeline, PlayerGuardTimelineAsset.PerfectGuardReactionMarkerId, "PlayerGuard PerfectGuardReaction marker", messages);
            if (guardTimeline.PerfectGuardChainActiveDuration <= 0f)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "PlayerGuard PerfectGuardChainActiveDuration must be greater than 0."));
            }

            PlayerGuardActionData guardData = guardTimeline.ToGuardActionData();
            RequireGuardCancelWindow(guardData.GuardReleaseCancelData.EvadeBufferWindows, "PlayerGuard Release Input_EvadeBuffer", messages);
            RequireGuardCancelWindow(guardData.GuardReleaseCancelData.SkillBufferWindows, "PlayerGuard Release Input_SkillBuffer", messages);
            RequireGuardCancelWindow(guardData.GuardReleaseCancelData.EvadeCancelWindows, "PlayerGuard Release Cancel_ToEvade", messages);
            RequireGuardCancelWindow(guardData.GuardReleaseCancelData.SkillCancelWindows, "PlayerGuard Release Cancel_ToSkill", messages);
            RequireGuardCancelWindow(guardData.GuardReleaseCancelData.AttackResetWindows, "PlayerGuard Release Cancel_AttackReset", messages);
            RequireGuardCancelWindow(guardData.GuardHitCancelData.GuardReleaseCancelWindows, "PlayerGuard GuardHit Cancel_ToGuardRelease", messages);
            RequireGuardCancelWindow(guardData.PerfectGuardCancelData.GuardReleaseCancelWindows, "PlayerGuard PerfectGuard Cancel_ToGuardRelease", messages);
            RejectGuardReactionActionWindows(guardData.GuardHitCancelData, "GuardHit", messages);
            RejectGuardReactionActionWindows(guardData.PerfectGuardCancelData, "PerfectGuard", messages);
        }

        private static void RequireGuardMarker(PlayerGuardTimelineAsset timeline, string markerId, string label, List<CombatTimelineValidationMessage> messages)
        {
            if (!timeline.TryResolveMarkerDuration(markerId, out _))
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Missing marker: {label}."));
            }
        }

        private static void RequireGuardCancelWindow(ProjectEVE.Player.Windows.ActionWindow[] windows, string label, List<CombatTimelineValidationMessage> messages)
        {
            if (windows.Length == 0)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Missing window: {label}."));
            }
        }

        /// <summary>
        /// 拒绝 Guard 反应轨道上的动作输入或动作取消窗口，确保所有动作派生先进入真实 GuardRelease。
        /// </summary>
        /// <param name="cancelData">从 GuardHit 或 PerfectGuard 轨道解析出的窗口集合。</param>
        /// <param name="trackLabel">用于错误消息的 Guard 内部轨道名称。</param>
        /// <param name="messages">写入 Validation 错误的结果集合。</param>
        private static void RejectGuardReactionActionWindows(
            PlayerGuardReactionCancelData cancelData,
            string trackLabel,
            List<CombatTimelineValidationMessage> messages)
        {
            RejectGuardReactionActionWindow(cancelData.EvadeBufferWindows, trackLabel, "Input_EvadeBuffer", messages);
            RejectGuardReactionActionWindow(cancelData.SkillBufferWindows, trackLabel, "Input_SkillBuffer", messages);
            RejectGuardReactionActionWindow(cancelData.EvadeCancelWindows, trackLabel, "Cancel_ToEvade", messages);
            RejectGuardReactionActionWindow(cancelData.SkillCancelWindows, trackLabel, "Cancel_ToSkill", messages);
            RejectGuardReactionActionWindow(cancelData.AttackResetWindows, trackLabel, "Cancel_AttackReset", messages);
        }

        /// <summary>
        /// 对单类动作窗口执行 Guard 反应轨道禁配校验。
        /// </summary>
        /// <param name="windows">从 Guard 反应轨道解析出的待检查窗口。</param>
        /// <param name="trackLabel">用于错误消息的 Guard 内部轨道名称。</param>
        /// <param name="capabilityLabel">用于错误消息的 Capability 名称。</param>
        /// <param name="messages">写入 Validation 错误的结果集合。</param>
        private static void RejectGuardReactionActionWindow(
            ProjectEVE.Player.Windows.ActionWindow[] windows,
            string trackLabel,
            string capabilityLabel,
            List<CombatTimelineValidationMessage> messages)
        {
            if (windows.Length > 0)
            {
                messages.Add(new CombatTimelineValidationMessage(
                    CombatTimelineValidationSeverity.Error,
                    $"PlayerGuard {trackLabel} track does not allow {capabilityLabel}; actions must derive from GuardRelease."));
            }
        }


        /// <summary>
        /// 校验 Boss / Timeline 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private static void ValidateBossTimeline(CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            RequireBossAnimationState(timeline, "BossAttack", messages);
            CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
            RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Hit_Attack, "BossAttack Hit_Attack", messages);
            bool hasHitNode = false;
            foreach (CombatHitNodeClip _ in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                hasHitNode = true;
                break;
            }

            if (!hasHitNode)
            {
                messages.Add(new CombatTimelineValidationMessage(
                    CombatTimelineValidationSeverity.Error,
                    "BossAttack requires at least one HitNode; use BossReposition for non-combat movement actions."));
            }

            if (runtimeSpec.BossReactionGateWindows.Count == 0)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, "BossAttack has no Boss Reaction Gate windows; committed states cannot be interrupted by player hits."));
            }

            foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                if (clip.EffectiveRange <= 0f || clip.EffectiveAngle <= 0f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: EffectiveRange/EffectiveAngle not fully configured."));
                }
            }

            if (Application.isPlaying)
            {
                BossAttackHitboxAnchor[] anchors = Object.FindObjectsByType<BossAttackHitboxAnchor>(FindObjectsSortMode.None);
                foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
                {
                    if (clip.SourcePart == BossAttackSourcePart.Detached)
                    {
                        BossDetachedAttackEmitter[] emitters = Object.FindObjectsByType<BossDetachedAttackEmitter>(FindObjectsSortMode.None);
                        bool hasDetachedBinding = false;
                        for (int i = 0; i < emitters.Length; i++)
                        {
                            if (emitters[i] != null && emitters[i].HasValidBinding(timeline.ActionId, clip.Name))
                            {
                                hasDetachedBinding = true;
                                break;
                            }
                        }

                        if (!hasDetachedBinding)
                        {
                            messages.Add(new CombatTimelineValidationMessage(
                                CombatTimelineValidationSeverity.Error,
                                $"{clip.Name}: no BossDetachedAttackEmitter binding for {timeline.ActionId}."));
                        }
                        continue;
                    }

                    bool found = false;
                    for (int i = 0; i < anchors.Length; i++)
                    {
                        if (anchors[i] != null && anchors[i].SourcePart == clip.SourcePart)
                        {
                            found = true;
                            break;
                        }
                    }

                    if (!found)
                    {
                        messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{clip.Name}: no BossAttackHitboxAnchor for SourcePart {clip.SourcePart}."));
                    }
                }
            }
            else
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Info, "Boss SourcePart anchor validation runs in PlayMode."));
            }
        }

        /// <summary>校验 Boss Reposition 不携带命中数据，并配置起手 Animator 状态。</summary>
        /// <param name="timeline">需要验证的 Reposition Timeline。</param>
        /// <param name="messages">写入错误和警告的结果集合。</param>
        private static void ValidateBossRepositionTimeline(
            CombatTimelineActionAsset timeline,
            List<CombatTimelineValidationMessage> messages)
        {
            RequireBossAnimationState(timeline, "BossReposition", messages);
            foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                messages.Add(new CombatTimelineValidationMessage(
                    CombatTimelineValidationSeverity.Error,
                    $"BossReposition must not contain HitNode: {clip.Name}."));
            }

        }

        /// <summary>要求 Boss 动作资产提供起手 Animator State，后续动画衔接交给 Animator Controller。</summary>
        /// <param name="timeline">需要检查动画状态名的 Boss 动作资产。</param>
        /// <param name="actionLabel">写入错误消息的动作类型标签。</param>
        /// <param name="messages">写入 Validation 错误的结果集合。</param>
        private static void RequireBossAnimationState(
            CombatTimelineActionAsset timeline,
            string actionLabel,
            List<CombatTimelineValidationMessage> messages)
        {
            if (string.IsNullOrEmpty(timeline.AnimationStateName))
            {
                messages.Add(new CombatTimelineValidationMessage(
                    CombatTimelineValidationSeverity.Error,
                    $"{actionLabel} requires Animation State Name."));
            }
        }

        /// <summary>
        /// 校验 Reaction / Timeline 配置，报告会影响运行时行为的缺失或冲突。
        /// </summary>
        private static void ValidateReactionTimeline(CombatTimelineActionAsset timeline, List<CombatTimelineValidationMessage> messages)
        {
            if (timeline is not CombatReactionTimelineAsset reactionTimeline)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "Reaction timeline uses an invalid asset type."));
                return;
            }

            CombatTimelineReactionConfig config = reactionTimeline.ToReactionConfig();
            if (config.ReactionType == CombatTimelineReactionType.None)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "ReactionType is None."));
            }

            bool isDeadReaction = config.ReactionType == CombatTimelineReactionType.Dead;
            if (!isDeadReaction)
            {
                CombatActionRuntimeSpec runtimeSpec = timeline.BuildRuntimeSpec();
                RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Reaction_Stun, "Reaction_Stun", messages);
                RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Reaction_Recovery, "Reaction_Recovery", messages);
                RequireCapability(runtimeSpec, CombatTimelineCapabilityId.Reaction_CanReturn, "Reaction_CanReturn", messages);

                if (config.StunDuration > config.RecoveryStartTime + 0.001f ||
                    config.RecoveryStartTime > config.CanReturnTime + 0.001f ||
                    config.CanReturnTime > config.TotalDuration + 0.001f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "Reaction phase order must be Stun <= RecoveryStart <= CanReturn <= TotalDuration."));
                }

                if (config.ReactionType == CombatTimelineReactionType.Knockdown &&
                    (config.StunDuration <= 0f || config.RecoveryStartTime < config.StunDuration || config.TotalDuration <= config.CanReturnTime))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, "Knockdown requires valid Start, Loop, Recovery and Reset phases."));
                }

                if (timeline.ActionKind == CombatTimelineActionKind.PlayerReaction &&
                    config.ReactionType == CombatTimelineReactionType.Knockdown)
                {
                    RequirePhaseClip(timeline, PlayerStatePhase.Start, "PlayerKnockdown Start", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Loop, "PlayerKnockdown Loop", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Recovery, "PlayerKnockdown Recovery", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Reset, "PlayerKnockdown Reset", messages);
                }

                if (timeline.ActionKind == CombatTimelineActionKind.PlayerReaction &&
                    config.ReactionType == CombatTimelineReactionType.HitReaction)
                {
                    RequirePhaseClip(timeline, PlayerStatePhase.Start, "PlayerHitReaction Start", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Loop, "PlayerHitReaction Loop", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Recovery, "PlayerHitReaction Recovery", messages);
                    RequirePhaseClip(timeline, PlayerStatePhase.Reset, "PlayerHitReaction Reset", messages);
                }

                if (config.StunDuration < 0.08f)
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, "Reaction stun is very short; hit feedback may be hard to read."));
                }

                if (timeline.ActionKind == CombatTimelineActionKind.BossReaction)
                {
                    ValidateBossReactionMotionReferences(timeline, messages);
                }
            }

            if (!TryResolveReactionAnimationPreviewClip(timeline, out AnimationClip previewClip, out bool loopPreview))
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, "Animation Preview clip is missing; edit-mode pose preview is unavailable."));
            }
            else if (previewClip.length + 0.001f < config.TotalDuration && !loopPreview)
            {
                messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, "Animation Preview clip length is shorter than reaction TotalDuration."));
            }

            if (isDeadReaction)
            {
                foreach (CombatTimelineClip clip in timeline.EnumerateAllClips())
                {
                    if (clip.ClipKind == CombatTimelineClipKind.CancelWindow ||
                        clip.CapabilityId == CombatTimelineCapabilityId.Reaction_CanReturn ||
                        clip.CapabilityId == CombatTimelineCapabilityId.Cancel_AttackReset)
                    {
                        messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Warning, $"{clip.Name}: Dead reaction should not configure recovery/cancel windows."));
                    }
                }
            }
        }

        /// <summary>
        /// 从 Animation Preview 窗口解析 Reaction 编辑器预览使用的 clip。
        /// </summary>
        /// <param name="timeline">要校验的 Reaction Timeline 资产。</param>
        /// <param name="previewClip">写回最后一个配置了 Preview Clip 的 Animation Preview 窗口。</param>
        /// <param name="loopPreview">写回该窗口是否循环预览。</param>
        /// <returns>找到可用于预览的 Animation Preview clip 时返回 true，否则返回 false。</returns>
        private static bool TryResolveReactionAnimationPreviewClip(
            CombatTimelineActionAsset timeline,
            out AnimationClip previewClip,
            out bool loopPreview)
        {
            previewClip = null;
            loopPreview = false;
            foreach (CombatAnimationClipWindow animationClip in timeline.EnumerateClips<CombatAnimationClipWindow>())
            {
                if (animationClip.PreviewAnimationClip == null ||
                    animationClip.PreviewAnimationClip.length <= 0.001f)
                {
                    continue;
                }

                previewClip = animationClip.PreviewAnimationClip;
                loopPreview = animationClip.LoopPreview;
            }

            return previewClip != null;
        }

        private static void RequirePhaseClip(
            CombatTimelineActionAsset timeline,
            PlayerStatePhase phase,
            string label,
            List<CombatTimelineValidationMessage> messages)
        {
            foreach (CombatPhaseClip phaseClip in timeline.EnumerateClips<CombatPhaseClip>())
            {
                if (phaseClip.Phase == phase)
                {
                    return;
                }
            }

            messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"Missing Phase clip: {label}."));
        }

        private static void ValidateBossReactionMotionReferences(
            CombatTimelineActionAsset timeline,
            List<CombatTimelineValidationMessage> messages)
        {
            foreach (CombatMotionReferenceClip motionClip in timeline.EnumerateClips<CombatMotionReferenceClip>())
            {
                if (motionClip.MotionKind != CombatTimelineClipKind.BossCodeMove)
                {
                    continue;
                }

                if (string.IsNullOrEmpty(motionClip.BossAttackId))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{motionClip.Name}: BossCodeMove requires Boss Motion Action Id."));
                }

                if (string.IsNullOrEmpty(motionClip.ProfileWindowName))
                {
                    messages.Add(new CombatTimelineValidationMessage(CombatTimelineValidationSeverity.Error, $"{motionClip.Name}: BossCodeMove requires Profile Window."));
                }
            }
        }
    }
}
