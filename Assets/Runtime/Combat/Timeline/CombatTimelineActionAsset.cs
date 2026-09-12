// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Movement;
using ProjectEVE.Player.Windows;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    public abstract class CombatTimelineActionAsset : ScriptableObject
    {
        [SerializeField] private CombatTimelineOwner owner;
        [SerializeField] private CombatTimelineActionKind actionKind;
        [SerializeField] private string actionId;
        [SerializeField] private string displayName;
        [SerializeField] private string animationStateName;
        [SerializeField, HideInInspector] private int totalFrames;
        [SerializeField] private float totalDuration = 1f;
        [SerializeField] private float frameRate = 24f;
        [SerializeField] private CombatTimelineTrack[] tracks = Array.Empty<CombatTimelineTrack>();

        public CombatTimelineOwner Owner => owner;
        public CombatTimelineActionKind ActionKind => actionKind;
        public string ActionId => actionId;
        public string DisplayName => displayName;
        public string AnimationStateName => animationStateName;
        public int TotalFrames
        {
            get
            {
                EnsureFrameModel();
                return totalFrames;
            }
        }

        public float TotalDuration
        {
            get
            {
                EnsureFrameModel();
                return totalFrames / FrameRate;
            }
        }

        public float FrameRate => frameRate;
        public CombatTimelineTrack[] Tracks => tracks;

        /// <summary>
        /// 执行 Configure / Identity 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        public void ConfigureIdentity(
            CombatTimelineOwner nextOwner,
            CombatTimelineActionKind nextActionKind,
            string nextActionId,
            string nextDisplayName,
            string nextAnimationStateName,
            float nextTotalDuration,
            float nextFrameRate = 60f)
        {
            owner = nextOwner;
            actionKind = nextActionKind;
            actionId = nextActionId;
            displayName = nextDisplayName;
            animationStateName = nextAnimationStateName;
            frameRate = Mathf.Max(1f, nextFrameRate);
            totalFrames = SecondsToFrames(nextTotalDuration, frameRate);
            totalDuration = totalFrames / frameRate;
        }

        /// <summary>
        /// 设置 Tracks 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        public void SetTracks(CombatTimelineTrack[] nextTracks)
        {
            tracks = nextTracks ?? Array.Empty<CombatTimelineTrack>();
        }

        /// <summary>
        /// 设置 Total / Frames 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        public void SetTotalFrames(int nextTotalFrames)
        {
            totalFrames = Mathf.Max(1, nextTotalFrames);
            totalDuration = totalFrames / FrameRate;
        }

        /// <summary>
        /// 设置 Duration / From / Seconds 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        public void SetDurationFromSeconds(float nextTotalDuration)
        {
            totalFrames = SecondsToFrames(nextTotalDuration, FrameRate);
            totalDuration = totalFrames / FrameRate;
        }

        /// <summary>
        /// 修改采样帧率并保持 Timeline 的秒级总时长不变。
        /// </summary>
        /// <param name="nextFrameRate">新的每秒帧数，低于 1 时会钳制为 1。</param>
        public void SetFrameRatePreserveDuration(float nextFrameRate)
        {
            EnsureFrameModel();
            float durationSeconds = TotalDuration;
            frameRate = Mathf.Max(1f, nextFrameRate);
            totalFrames = SecondsToFrames(durationSeconds, frameRate);
            totalDuration = totalFrames / frameRate;
        }

/// <summary>
        /// 执行 Enumerate / Clips 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        public IEnumerable<CombatTimelineClip> EnumerateClips(CombatTimelineClipKind clipKind)
        {
            foreach (CombatTimelineClip clip in EnumerateAllClips())
            {
                if (clip.ClipKind == clipKind)
                {
                    yield return clip;
                }
            }
        }

        public IEnumerable<TClip> EnumerateClips<TClip>() where TClip : CombatTimelineClip
        {
            foreach (CombatTimelineClip clip in EnumerateAllClips())
            {
                if (clip is TClip typedClip)
                {
                    yield return typedClip;
                }
            }
        }

        /// <summary>
        /// 执行 Enumerate / All / Clips 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        public IEnumerable<CombatTimelineClip> EnumerateAllClips()
        {
            if (tracks == null)
            {
                yield break;
            }

            for (int i = 0; i < tracks.Length; i++)
            {
                CombatTimelineTrack track = tracks[i];
                if (track?.Clips == null)
                {
                    continue;
                }

                for (int j = 0; j < track.Clips.Length; j++)
                {
                    CombatTimelineClip clip = track.Clips[j];
                    if (clip != null)
                    {
                        yield return clip;
                    }
                }
            }
        }

        /// <summary>
        /// 构建 Combat Timeline 的归一化 RuntimeSpec，作为状态运行时读取能力的入口。
        /// </summary>
        public CombatActionRuntimeSpec BuildRuntimeSpec()
        {
            return CombatActionRuntimeSpec.FromTimeline(this);
        }
        
        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            EnsureFrameModel();
            totalDuration = totalFrames / FrameRate;
        }

        /// <summary>
        /// 确保 Frame / Model 可用，不满足时创建、刷新或钳制必要的运行时数据。
        /// </summary>
        private void EnsureFrameModel()
        {
            frameRate = Mathf.Max(1f, frameRate);
            if (totalFrames > 0)
            {
                return;
            }

            totalFrames = SecondsToFrames(totalDuration, frameRate);
            totalDuration = totalFrames / frameRate;
        }

        /// <summary>
        /// 执行 Seconds / To / Frames 相关逻辑，并维护 Combat Timeline 模块的运行时一致性。
        /// </summary>
        private static int SecondsToFrames(float seconds, float samplesPerSecond)
        {
            float safeFrameRate = Mathf.Max(1f, samplesPerSecond);
            return Mathf.Max(1, Mathf.CeilToInt(Mathf.Max(0.01f, seconds) * safeFrameRate));
        }
    }

}
