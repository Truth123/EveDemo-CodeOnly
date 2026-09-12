// 文件说明：接收 Raven Animator 的表现型 Animation Event，并控制显式绑定场景特效与音效的播放、停止。
// 所属模块：Boss Animation。
// 运行影响：只触发动画帧表现，不参与 HitNode、伤害、位移或状态切换。

using UnityEngine;
using UnityEngine.Serialization;
using ProjectEVE.Boss.Combat;

namespace ProjectEVE
{
    /// <summary>
    /// 接收挂在 Raven 动画 Clip 上的无参数 Animation Event，并驱动对应场景粒子与音效表现。
    /// </summary>
    public sealed class BossAnimationEventFunc : MonoBehaviour
    {
        [SerializeField] private ParticleSystem fastMoveParticle;
        [FormerlySerializedAs("yellowAttackCueVfx")]
        [SerializeField] private BossAttackCueVfxController attackCueVfx;

        [Header("Animation Event SFX")]
        [SerializeField] private AudioSource animationEventAudioSource;
        [SerializeField] private AudioClip redStartSound;
        [SerializeField] private AudioClip yellowStartSound;
        [SerializeField] private AudioClip redChargeSound;

        /// <summary>
        /// 在 MoveChainCombo 快速移动事件帧清除旧粒子，并从头播放根节点及全部子粒子系统。
        /// </summary>
        public void OnFastMoveParticleStart()
        {
            if (fastMoveParticle == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Fast Move Particle for " +
                    $"{nameof(OnFastMoveParticleStart)}.",
                    this);
                return;
            }

            ParticleSystem[] particleSystems =
                fastMoveParticle.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                particleSystem.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Play(false);
            }
        }

        /// <summary>
        /// 在快速移动特效的结束事件帧立即停止并清空根节点及全部子粒子，允许动画提前结束 star1 表现。
        /// </summary>
        public void OnFastMoveParticleStop()
        {
            if (fastMoveParticle == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Fast Move Particle for " +
                    $"{nameof(OnFastMoveParticleStop)}.",
                    this);
                return;
            }

            ParticleSystem[] particleSystems =
                fastMoveParticle.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                particleSystem.Stop(
                    false,
                    ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }

        /// <summary>
        /// 在黄光攻击的 Animation Event 帧从头播放不可格挡预警，并由黄光控制器负责灯光和一次性生命周期。
        /// </summary>
        public void OnYellowAttackParticleStart()
        {
            if (attackCueVfx == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Yellow Attack Cue VFX for " +
                    $"{nameof(OnYellowAttackParticleStart)}.",
                    this);
                return;
            }

            attackCueVfx.PlayYellowCue();
        }

        /// <summary>
        /// 在可防御连续猛攻的 Animation Event 帧从头播放红光预警；该回调只驱动表现，不修改攻击规则。
        /// </summary>
        public void OnRedAttackParticleStart()
        {
            if (attackCueVfx == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Attack Cue VFX for " +
                    $"{nameof(OnRedAttackParticleStart)}.",
                    this);
                return;
            }

            attackCueVfx.PlayRedCue();
        }

        /// <summary>
        /// 在动画事件帧从头播放完整过程压缩到 0.53 秒的红光预警；只驱动表现，不修改攻击规则。
        /// </summary>
        public void OnRedAttackParticle053Start()
        {
            if (attackCueVfx == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Attack Cue VFX for " +
                    $"{nameof(OnRedAttackParticle053Start)}.",
                    this);
                return;
            }

            attackCueVfx.PlayAcceleratedRedCue();
        }

        /// <summary>
        /// 播放 Assets/Audio/stellarblade_voice_effect_redstart.wav
        /// </summary>
        public void OnPlayRedStartSound()
        {
            PlaySound(redStartSound, "Red Start Sound", nameof(OnPlayRedStartSound));
        }


        /// <summary>
        /// 播放 Assets/Audio/stellarblade_voice_effect_150_yellowattack.wav
        /// </summary>
        public void OnPlayYellowStartSound()
        {
            PlaySound(
                yellowStartSound,
                "Yellow Start Sound",
                nameof(OnPlayYellowStartSound));
        }


        /// <summary>
        /// 播放 Assets/Audio/stellarblade_voice_effect_redcharge.wav
        /// </summary>
        public void OnPlayRedChargeSound()
        {
            PlaySound(redChargeSound, "Red Charge Sound", nameof(OnPlayRedChargeSound));
        }

        /// <summary>
        /// 通过 Boss 专用音源以 OneShot 方式播放动画事件音效，允许相邻事件声音自然重叠。
        /// </summary>
        /// <param name="clip">当前动画事件需要播放的音频片段。</param>
        /// <param name="clipLabel">缺少音频绑定时用于 Console 报错的 Inspector 字段名称。</param>
        /// <param name="eventName">触发本次播放的 Animation Event 函数名。</param>
        private void PlaySound(AudioClip clip, string clipLabel, string eventName)
        {
            if (animationEventAudioSource == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires Animation Event Audio Source for " +
                    $"{eventName}.",
                    this);
                return;
            }

            if (clip == null)
            {
                Debug.LogError(
                    $"{nameof(BossAnimationEventFunc)} requires {clipLabel} for {eventName}.",
                    this);
                return;
            }

            animationEventAudioSource.PlayOneShot(clip);
        }
    }
}
