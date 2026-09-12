// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Player.Windows;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "PlayerGuardTimeline", menuName = "Project EVE/Combat/Player Guard Timeline")]
    public sealed class PlayerGuardTimelineAsset : CombatTimelineActionAsset
    {
        private const string GuardHitTrackName = "GuardHit";
        private const string PerfectGuardTrackName = "Perfect Guard";
        private const string ReleaseTrackName = "Release";

        public const string GuardHitReactionMarkerId = "GuardHitReaction";
        public const string PerfectGuardReactionMarkerId = "PerfectGuardReaction";

        [SerializeField] private int perfectGuardChainActiveFrames = 10;

        public float PerfectGuardChainActiveDuration => Mathf.Max(0f, perfectGuardChainActiveFrames) / FrameRate;

        /// <summary>
        /// 将当前对象转换为 Player Guard 状态进入时使用的动作数据。
        /// </summary>
        public PlayerGuardActionData ToGuardActionData()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            return new PlayerGuardActionData(
                ActionId,
                TotalDuration,
                runtimeSpec,
                ResolveRequiredMarkerDuration(GuardHitReactionMarkerId),
                ResolveRequiredMarkerDuration(PerfectGuardReactionMarkerId),
                PerfectGuardChainActiveDuration,
                ResolveCancelDataFromTrack(GuardHitTrackName),
                ResolveCancelDataFromTrack(PerfectGuardTrackName),
                ResolveCancelDataFromTrack(ReleaseTrackName));
        }

        /// <summary>
        /// 查找 Guard 内部 reaction marker 的持续时间。Marker 是 Guard 专属时长数据，不进入 RuntimeSpec capability。
        /// </summary>
        public bool TryResolveMarkerDuration(string markerId, out float duration)
        {
            duration = 0f;
            foreach (CombatMarkerClip marker in EnumerateClips<CombatMarkerClip>())
            {
                if (marker == null || marker.MarkerId != markerId)
                {
                    continue;
                }

                duration = Mathf.Max(duration, marker.EndTime);
            }

            return duration > 0f;
        }

        private float ResolveRequiredMarkerDuration(string markerId)
        {
            return TryResolveMarkerDuration(markerId, out float duration) ? duration : 0f;
        }

        /// <summary>
        /// 解析指定 Guard 内部轨道的专属取消窗口，避免不同 Guard mode 误读同名能力窗口。
        /// </summary>
        private PlayerGuardReactionCancelData ResolveCancelDataFromTrack(string trackName)
        {
            List<ActionWindow> evadeBuffers = new List<ActionWindow>();
            List<ActionWindow> skillBuffers = new List<ActionWindow>();
            List<ActionWindow> evadeCancels = new List<ActionWindow>();
            List<ActionWindow> skillCancels = new List<ActionWindow>();
            List<ActionWindow> attackResets = new List<ActionWindow>();
            List<ActionWindow> guardReleaseCancels = new List<ActionWindow>();

            CombatTimelineTrack[] sourceTracks = Tracks;
            for (int i = 0; i < sourceTracks.Length; i++)
            {
                CombatTimelineTrack track = sourceTracks[i];
                if (track == null ||
                    track.Clips == null ||
                    !string.Equals(track.Name, trackName, System.StringComparison.Ordinal))
                {
                    continue;
                }

                for (int j = 0; j < track.Clips.Length; j++)
                {
                    CombatTimelineClip clip = track.Clips[j];
                    if (clip == null)
                    {
                        continue;
                    }

                    ActionWindow window = clip.ToActionWindow();
                    switch (clip.CapabilityId)
                    {
                        case CombatTimelineCapabilityId.Input_EvadeBuffer:
                            evadeBuffers.Add(window);
                            break;
                        case CombatTimelineCapabilityId.Input_SkillBuffer:
                            skillBuffers.Add(window);
                            break;
                        case CombatTimelineCapabilityId.Cancel_ToEvade:
                            evadeCancels.Add(window);
                            break;
                        case CombatTimelineCapabilityId.Cancel_ToSkill:
                            skillCancels.Add(window);
                            break;
                        case CombatTimelineCapabilityId.Cancel_AttackReset:
                            attackResets.Add(window);
                            break;
                        case CombatTimelineCapabilityId.Cancel_ToGuardRelease:
                            guardReleaseCancels.Add(window);
                            break;
                    }
                }
            }

            return new PlayerGuardReactionCancelData(
                evadeBuffers.ToArray(),
                skillBuffers.ToArray(),
                evadeCancels.ToArray(),
                skillCancels.ToArray(),
                attackResets.ToArray(),
                guardReleaseCancels.ToArray());
        }
    }
}
