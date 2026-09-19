// 文件说明：维护 HitStop、PerfectEvade 子弹时间、CameraShake 和 VFX/SFX 事件播放。
// 所属模块：战斗反馈。
// 运行影响：影响命中反馈、完美闪避慢动作、相机反馈和反馈事件传递。

using ProjectEVE.CameraSystem.Rigs;
using ProjectEVE.Combat;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Feedback
{
    /// <summary>
    /// 战斗反馈播放层。可空引用设计，未绑定 VFX/SFX 时只跳过对应表现。
    /// </summary>
    public sealed class CombatFeedbackPlayer : MonoBehaviour
    {
        /// <summary>
        /// 单条命中反馈调参项。按反馈类型和攻击来源匹配，控制 HitStop 与相机反馈。
        /// </summary>
        [System.Serializable]
        public struct CombatFeedbackProfile
        {
            [SerializeField] private CombatFeedbackKind kind;
            [SerializeField] private CombatAttackType attackType;
            [SerializeField] private float hitStopDuration;
            [SerializeField] private float hitStopTimeScale;
            [SerializeField] private bool enableCameraShake;
            [SerializeField] private float shakeAmplitude;
            [SerializeField] private float shakeDuration;
            [SerializeField] private float shakeFrequency;

            /// <summary>创建 CombatFeedbackProfile 实例。</summary>
            public CombatFeedbackProfile(
                CombatFeedbackKind kind,
                CombatAttackType attackType,
                float hitStopDuration,
                float hitStopTimeScale,
                bool enableCameraShake,
                float shakeAmplitude,
                float shakeDuration,
                float shakeFrequency)
            {
                this.kind = kind;
                this.attackType = attackType;
                this.hitStopDuration = hitStopDuration;
                this.hitStopTimeScale = hitStopTimeScale;
                this.enableCameraShake = enableCameraShake;
                this.shakeAmplitude = shakeAmplitude;
                this.shakeDuration = shakeDuration;
                this.shakeFrequency = shakeFrequency;
            }

            /// <summary>反馈类型。</summary>
            public CombatFeedbackKind Kind => kind;
            /// <summary>攻击来源类型。</summary>
            public CombatAttackType AttackType => attackType;
            /// <summary>HitStop 持续时间。</summary>
            public float HitStopDuration => hitStopDuration;
            /// <summary>HitStop 期间的 TimeScale。</summary>
            public float HitStopTimeScale => hitStopTimeScale;
            /// <summary>是否启用该 profile 的相机反馈。</summary>
            public bool EnableCameraShake => enableCameraShake;
            /// <summary>相机反馈振幅。</summary>
            public float ShakeAmplitude => shakeAmplitude;
            /// <summary>相机反馈持续时间。</summary>
            public float ShakeDuration => shakeDuration;
            /// <summary>相机反馈频率。</summary>
            public float ShakeFrequency => shakeFrequency;

            /// <summary>判断该 profile 是否匹配当前反馈请求。</summary>
            public bool Matches(CombatFeedbackKind feedbackKind, CombatAttackType feedbackAttackType)
            {
                return kind == feedbackKind && attackType == feedbackAttackType;
            }

            /// <summary>返回钳制后的播放参数，避免 Inspector 输入非法值。</summary>
            public CombatFeedbackProfile Sanitized()
            {
                return new CombatFeedbackProfile(
                    kind,
                    attackType,
                    Mathf.Max(0f, hitStopDuration),
                    Mathf.Clamp(hitStopTimeScale, 0.01f, 1f),
                    enableCameraShake,
                    Mathf.Max(0f, shakeAmplitude),
                    Mathf.Max(0f, shakeDuration),
                    Mathf.Max(1f, shakeFrequency));
            }

            /// <summary>转换为运行时播放设置。</summary>
            public CombatFeedbackPlaybackSettings ToPlaybackSettings()
            {
                CombatFeedbackProfile profile = Sanitized();
                return new CombatFeedbackPlaybackSettings(
                    profile.HitStopDuration,
                    profile.HitStopTimeScale,
                    profile.EnableCameraShake,
                    profile.ShakeAmplitude,
                    profile.ShakeDuration,
                    profile.ShakeFrequency);
            }
        }

        /// <summary>
        /// 一次反馈事件解析后的播放参数。
        /// </summary>
        public readonly struct CombatFeedbackPlaybackSettings
        {
            /// <summary>创建 CombatFeedbackPlaybackSettings 实例。</summary>
            public CombatFeedbackPlaybackSettings(
                float hitStopDuration,
                float hitStopTimeScale,
                bool enableCameraShake,
                float shakeAmplitude,
                float shakeDuration,
                float shakeFrequency)
            {
                HitStopDuration = Mathf.Max(0f, hitStopDuration);
                HitStopTimeScale = Mathf.Clamp(hitStopTimeScale, 0.01f, 1f);
                EnableCameraShake = enableCameraShake;
                ShakeAmplitude = Mathf.Max(0f, shakeAmplitude);
                ShakeDuration = Mathf.Max(0f, shakeDuration);
                ShakeFrequency = Mathf.Max(1f, shakeFrequency);
            }

            /// <summary>HitStop 持续时间。</summary>
            public float HitStopDuration { get; }
            /// <summary>HitStop 期间的 TimeScale。</summary>
            public float HitStopTimeScale { get; }
            /// <summary>是否启用该次相机反馈。</summary>
            public bool EnableCameraShake { get; }
            /// <summary>相机反馈振幅。</summary>
            public float ShakeAmplitude { get; }
            /// <summary>相机反馈持续时间。</summary>
            public float ShakeDuration { get; }
            /// <summary>相机反馈频率。</summary>
            public float ShakeFrequency { get; }
        }

        [Header("Switches")]
        [SerializeField] private bool enableHitStop = true;
        [SerializeField] private bool enableCameraShake = true;
        [SerializeField] private bool enableVfx = true;
        [SerializeField] private bool enableSfx = true;

        [Header("References")]
        [SerializeField] private CombatHitStop hitStop;
        [SerializeField] private PlayerCameraRig cameraRig;
        [SerializeField] private AudioSource audioSource;

        [Header("HitStop / Camera Profiles")]
        [SerializeField] private CombatFeedbackProfile[] feedbackProfiles = CreateDefaultProfiles();

        [Header("Perfect Evade Bullet Time")]
        [SerializeField] private bool enablePerfectEvadeBulletTime = true;
        [SerializeField] private float perfectEvadeSlowTimeScale = 0.20f;
        [SerializeField] private float perfectEvadeSlowDuration = 0.25f;
        [SerializeField] private float perfectEvadeRecoverDuration = 0.18f;

        [Header("Perfect Evade Direction Lines")]
        [SerializeField] private bool enablePerfectEvadeDirectionLines = true;
        [SerializeField] private Material perfectEvadeLineMaterial;
        [SerializeField] private Color perfectEvadeLineColor = new Color(1f, 0.05f, 0.12f, 0.88f);
        [SerializeField] private float perfectEvadeLineDuration = 0.18f;
        [SerializeField] private float perfectEvadeLineLength = 1.35f;
        [SerializeField] private float perfectEvadeLineWidth = 0.035f;

        [Header("VFX")]
        [SerializeField] private ParticleSystem hitVfx;
        [SerializeField] private ParticleSystem bossHitVfx;
        [SerializeField] private ParticleSystem guardVfx;
        [SerializeField] private ParticleSystem perfectGuardVfx;
        [SerializeField] private ParticleSystem perfectEvadeVfx;
        [SerializeField] private ParticleSystem knockdownVfx;

        [Header("Boss Hit Brightness Pulse")]
        [SerializeField] private bool enableBossHitBrightnessPulse = true;
        [SerializeField] private Light[] bossHitPulseLights;
        [SerializeField] private float bossHitPulseIntensityMultiplier = 1.8f;
        [SerializeField] private float bossHitPulseDuration = 0.08f;

        [Header("SFX")]
        [SerializeField] private AudioClip hitClip;
        [SerializeField, Range(0f, 1f)] private float hitClipVolume = 1f;
        [SerializeField] private AudioClip guardClip;
        [SerializeField, Range(0f, 1f)] private float guardClipVolume = 1f;
        [SerializeField] private AudioClip perfectGuardClip;
        [SerializeField, Range(0f, 1f)] private float perfectGuardClipVolume = 1f;
        [SerializeField] private AudioClip perfectEvadeClip;
        [SerializeField, Range(0f, 1f)] private float perfectEvadeClipVolume = 1f;
        [SerializeField] private AudioClip knockdownClip;
        [SerializeField, Range(0f, 1f)] private float knockdownClipVolume = 1f;

        private Coroutine bossHitBrightnessPulseRoutine;
        private float[] bossHitPulseOriginalIntensities;
        private bool bossHitPulseHasOriginals;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            SanitizeSfxVolumes();
            BindReferences();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            SanitizeProfiles();
            SanitizeSfxVolumes();
        }

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            CombatFeedbackBus.FeedbackRequested += HandleFeedback;
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            CombatFeedbackBus.FeedbackRequested -= HandleFeedback;
            RestoreBossHitPulseLights();
        }

        /// <summary>
        /// 处理 Feedback 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
        private void HandleFeedback(CombatFeedbackEvent feedbackEvent)
        {
            CombatFeedbackPlaybackSettings playback = ResolvePlaybackSettings(
                feedbackEvent.Kind,
                feedbackEvent.AttackType,
                feedbackProfiles);
            ResolveVisualAssets(feedbackEvent.Kind, out ParticleSystem vfx, out AudioClip clip, out float clipVolume);

            bool isPerfectEvade = feedbackEvent.Kind == CombatFeedbackKind.PerfectEvade;
            if (isPerfectEvade)
            {
                PlayPerfectEvadeBulletTime();
                PlayPerfectEvadeDirectionLines(feedbackEvent);
            }
            else if (enableHitStop && hitStop != null && playback.HitStopDuration > 0f)
            {
                hitStop.Request(playback.HitStopDuration, playback.HitStopTimeScale);
            }

            if (!isPerfectEvade &&
                enableCameraShake &&
                playback.EnableCameraShake &&
                cameraRig != null &&
                playback.ShakeAmplitude > 0f &&
                playback.ShakeDuration > 0f)
            {
                cameraRig.AddShake(playback.ShakeAmplitude, playback.ShakeDuration, playback.ShakeFrequency);
            }

            if (enableVfx && vfx != null)
            {
                PlayVfx(vfx, feedbackEvent);
            }

            if (feedbackEvent.Kind == CombatFeedbackKind.BossHit)
            {
                PlayBossHitBrightnessPulse();
            }

            if (enableSfx && audioSource != null && clip != null)
            {
                audioSource.PlayOneShot(clip, clipVolume);
            }
        }

        /// <summary>
        /// 解析 Feedback / Profile 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        public static CombatFeedbackPlaybackSettings ResolvePlaybackSettings(
            CombatFeedbackKind kind,
            CombatAttackType attackType,
            IReadOnlyList<CombatFeedbackProfile> profiles)
        {
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    CombatFeedbackProfile profile = profiles[i];
                    if (profile.Matches(kind, attackType))
                    {
                        return profile.ToPlaybackSettings();
                    }
                }
            }

            return ResolveFallbackPlaybackSettings(kind, attackType);
        }

        /// <summary>
        /// 解析缺省播放参数，避免场景未配置 profile 时完全丢失反馈。
        /// </summary>
        private static CombatFeedbackPlaybackSettings ResolveFallbackPlaybackSettings(
            CombatFeedbackKind kind,
            CombatAttackType attackType)
        {
            switch (kind)
            {
                case CombatFeedbackKind.GuardHit:
                    return new CombatFeedbackPlaybackSettings(0.035f, 0.10f, true, 0.035f, 0.08f, 34f);
                case CombatFeedbackKind.PerfectGuard:
                    return new CombatFeedbackPlaybackSettings(0.06f, 0.06f, true, 0.075f, 0.12f, 34f);
                case CombatFeedbackKind.PerfectEvade:
                    return new CombatFeedbackPlaybackSettings(0f, 1f, false, 0f, 0f, 34f);
                case CombatFeedbackKind.Knockdown:
                case CombatFeedbackKind.Dead:
                    return new CombatFeedbackPlaybackSettings(0.075f, 0.07f, true, 0.10f, 0.16f, 34f);
                case CombatFeedbackKind.PlayerHit:
                case CombatFeedbackKind.BossHit:
                    return ResolveFallbackHitSettings(attackType);
                default:
                    return new CombatFeedbackPlaybackSettings(0f, 1f, false, 0f, 0f, 34f);
            }
        }

        /// <summary>
        /// 按攻击来源解析普通命中缺省播放参数。
        /// </summary>
        private static CombatFeedbackPlaybackSettings ResolveFallbackHitSettings(CombatAttackType attackType)
        {
            switch (attackType)
            {
                case CombatAttackType.HeavyAttack:
                    return new CombatFeedbackPlaybackSettings(0.05f, 0.10f, true, 0.025f, 0.08f, 34f);
                case CombatAttackType.SkillAttack:
                    return new CombatFeedbackPlaybackSettings(0.03f, 0.12f, false, 0f, 0f, 34f);
                default:
                    return new CombatFeedbackPlaybackSettings(0.035f, 0.12f, false, 0f, 0f, 34f);
            }
        }

        /// <summary>
        /// 解析 VFX / SFX 资源，保持视觉和音频占位资源的旧绑定方式。
        /// </summary>
        private void ResolveVisualAssets(
            CombatFeedbackKind kind,
            out ParticleSystem vfx,
            out AudioClip clip,
            out float clipVolume)
        {
            switch (kind)
            {
                case CombatFeedbackKind.GuardHit:
                    vfx = guardVfx;
                    clip = guardClip;
                    clipVolume = guardClipVolume;
                    break;
                case CombatFeedbackKind.PerfectGuard:
                    vfx = perfectGuardVfx;
                    clip = perfectGuardClip;
                    clipVolume = perfectGuardClipVolume;
                    break;
                case CombatFeedbackKind.PerfectEvade:
                    vfx = perfectEvadeVfx;
                    clip = perfectEvadeClip;
                    clipVolume = perfectEvadeClipVolume;
                    break;
                case CombatFeedbackKind.Knockdown:
                case CombatFeedbackKind.Dead:
                    vfx = knockdownVfx != null ? knockdownVfx : hitVfx;
                    clip = knockdownClip != null ? knockdownClip : hitClip;
                    clipVolume = knockdownClip != null ? knockdownClipVolume : hitClipVolume;
                    break;
                case CombatFeedbackKind.BossHit:
                    vfx = bossHitVfx != null ? bossHitVfx : hitVfx;
                    clip = hitClip;
                    clipVolume = hitClipVolume;
                    break;
                default:
                    vfx = hitVfx;
                    clip = hitClip;
                    clipVolume = hitClipVolume;
                    break;
            }
        }

        private static void PlayVfx(ParticleSystem prefab, CombatFeedbackEvent feedbackEvent)
        {
            Vector3 direction = feedbackEvent.HitDirection.sqrMagnitude > 0.0001f
                ? feedbackEvent.HitDirection.normalized
                : Vector3.forward;
            Quaternion rotation = Quaternion.LookRotation(direction, Vector3.up);
            ParticleSystem instance = Instantiate(prefab, feedbackEvent.HitPosition, rotation);
            instance.Play(true);

            ParticleSystem.MainModule main = instance.main;
            float lifetime = main.duration + main.startLifetime.constantMax + 0.25f;
            Destroy(instance.gameObject, lifetime);
        }

        /// <summary>
        /// 在 Boss 成功命中玩家时短暂提高已绑定灯光强度，给横斩或直刺特效补一帧亮度峰值。
        /// </summary>
        private void PlayBossHitBrightnessPulse()
        {
            if (!enableBossHitBrightnessPulse ||
                bossHitPulseLights == null ||
                bossHitPulseLights.Length == 0 ||
                bossHitPulseIntensityMultiplier <= 1f ||
                bossHitPulseDuration <= 0f)
            {
                return;
            }

            RestoreBossHitPulseLights();
            bossHitBrightnessPulseRoutine = StartCoroutine(BossHitBrightnessPulseRoutine());
        }

        /// <summary>
        /// 记录原始灯光强度，立即提亮，等待指定时长后恢复。
        /// </summary>
        private IEnumerator BossHitBrightnessPulseRoutine()
        {
            CaptureBossHitPulseLightIntensities();
            for (int i = 0; i < bossHitPulseLights.Length; i++)
            {
                Light pulseLight = bossHitPulseLights[i];
                if (pulseLight == null)
                {
                    continue;
                }

                pulseLight.intensity = bossHitPulseOriginalIntensities[i] * bossHitPulseIntensityMultiplier;
            }

            yield return new WaitForSecondsRealtime(bossHitPulseDuration);

            RestoreBossHitPulseLights();
        }

        /// <summary>
        /// 保存本次 Boss 命中亮度脉冲开始前的灯光强度，用于脉冲结束后还原。
        /// </summary>
        private void CaptureBossHitPulseLightIntensities()
        {
            int lightCount = bossHitPulseLights != null ? bossHitPulseLights.Length : 0;
            if (bossHitPulseOriginalIntensities == null || bossHitPulseOriginalIntensities.Length != lightCount)
            {
                bossHitPulseOriginalIntensities = new float[lightCount];
            }

            for (int i = 0; i < lightCount; i++)
            {
                Light pulseLight = bossHitPulseLights[i];
                bossHitPulseOriginalIntensities[i] = pulseLight != null ? pulseLight.intensity : 0f;
            }

            bossHitPulseHasOriginals = true;
        }

        /// <summary>
        /// 取消 Boss 命中亮度脉冲并把灯光恢复到脉冲开始前的强度。
        /// </summary>
        private void RestoreBossHitPulseLights()
        {
            if (bossHitBrightnessPulseRoutine != null)
            {
                StopCoroutine(bossHitBrightnessPulseRoutine);
                bossHitBrightnessPulseRoutine = null;
            }

            if (!bossHitPulseHasOriginals ||
                bossHitPulseLights == null ||
                bossHitPulseOriginalIntensities == null)
            {
                bossHitPulseHasOriginals = false;
                return;
            }

            int restoreCount = Mathf.Min(bossHitPulseLights.Length, bossHitPulseOriginalIntensities.Length);
            for (int i = 0; i < restoreCount; i++)
            {
                Light pulseLight = bossHitPulseLights[i];
                if (pulseLight == null)
                {
                    continue;
                }

                pulseLight.intensity = bossHitPulseOriginalIntensities[i];
            }

            bossHitPulseHasOriginals = false;
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (hitStop == null)
            {
                hitStop = FindFirstObjectByType<CombatHitStop>();
            }

            if (cameraRig == null)
            {
                cameraRig = FindFirstObjectByType<PlayerCameraRig>();
            }

            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
            }
        }

        /// <summary>
        /// PerfectEvade 是规避奖励反馈，只请求子弹时间，不走命中 HitStop 或 CameraShake。
        /// </summary>
        private void PlayPerfectEvadeBulletTime()
        {
            if (!enablePerfectEvadeBulletTime || hitStop == null)
            {
                return;
            }

            hitStop.RequestSlowMotion(
                Mathf.Max(0f, perfectEvadeSlowDuration),
                Mathf.Clamp(perfectEvadeSlowTimeScale, 0.01f, 1f),
                Mathf.Max(0f, perfectEvadeRecoverDuration));
        }

        /// <summary>
        /// PerfectEvade 的红色方向线只表达无伤规避成功，不触发命中火花或相机反馈。
        /// </summary>
        private void PlayPerfectEvadeDirectionLines(CombatFeedbackEvent feedbackEvent)
        {
            if (!enablePerfectEvadeDirectionLines)
            {
                return;
            }

            Vector3 position = feedbackEvent.Target != null
                ? feedbackEvent.Target.transform.position
                : feedbackEvent.HitPosition;
            Vector3 direction = feedbackEvent.HitDirection.sqrMagnitude > 0.0001f
                ? feedbackEvent.HitDirection.normalized
                : Vector3.forward;

            CombatVfxPrimitiveFactory.SpawnDirectionLines(
                "FX_PerfectEvade_RedLines",
                perfectEvadeLineMaterial,
                position,
                direction,
                perfectEvadeLineColor,
                perfectEvadeLineLength,
                perfectEvadeLineWidth,
                perfectEvadeLineDuration,
                3);
        }

        /// <summary>
        /// 钳制 Inspector profile 数值，避免运行时获得非法配置。
        /// </summary>
        private void SanitizeProfiles()
        {
            perfectEvadeSlowTimeScale = Mathf.Clamp(perfectEvadeSlowTimeScale, 0.01f, 1f);
            perfectEvadeSlowDuration = Mathf.Max(0f, perfectEvadeSlowDuration);
            perfectEvadeRecoverDuration = Mathf.Max(0f, perfectEvadeRecoverDuration);
            perfectEvadeLineDuration = Mathf.Max(0f, perfectEvadeLineDuration);
            perfectEvadeLineLength = Mathf.Max(0f, perfectEvadeLineLength);
            perfectEvadeLineWidth = Mathf.Max(0f, perfectEvadeLineWidth);
            bossHitPulseIntensityMultiplier = Mathf.Max(1f, bossHitPulseIntensityMultiplier);
            bossHitPulseDuration = Mathf.Max(0f, bossHitPulseDuration);

            if (feedbackProfiles == null)
            {
                return;
            }

            for (int i = 0; i < feedbackProfiles.Length; i++)
            {
                feedbackProfiles[i] = feedbackProfiles[i].Sanitized();
            }
        }

        /// <summary>
        /// 钳制 SFX 音量缩放，避免 Inspector 或序列化数据写入超过 PlayOneShot 支持范围的值。
        /// </summary>
        private void SanitizeSfxVolumes()
        {
            hitClipVolume = Mathf.Clamp01(hitClipVolume);
            guardClipVolume = Mathf.Clamp01(guardClipVolume);
            perfectGuardClipVolume = Mathf.Clamp01(perfectGuardClipVolume);
            perfectEvadeClipVolume = Mathf.Clamp01(perfectEvadeClipVolume);
            knockdownClipVolume = Mathf.Clamp01(knockdownClipVolume);
        }

        /// <summary>
        /// 创建默认 HitStop / CameraShake 调参表。后续可在 Inspector 中覆盖。
        /// </summary>
        private static CombatFeedbackProfile[] CreateDefaultProfiles()
        {
            return new[]
            {
                new CombatFeedbackProfile(CombatFeedbackKind.PlayerHit, CombatAttackType.LightAttack, 0.035f, 0.12f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PlayerHit, CombatAttackType.HeavyAttack, 0.05f, 0.10f, true, 0.025f, 0.08f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PlayerHit, CombatAttackType.SkillAttack, 0.03f, 0.12f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.BossHit, CombatAttackType.LightAttack, 0.035f, 0.12f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.BossHit, CombatAttackType.HeavyAttack, 0.05f, 0.10f, true, 0.025f, 0.08f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.BossHit, CombatAttackType.SkillAttack, 0.03f, 0.12f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.GuardHit, CombatAttackType.LightAttack, 0.035f, 0.10f, true, 0.035f, 0.08f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.GuardHit, CombatAttackType.HeavyAttack, 0.035f, 0.10f, true, 0.035f, 0.08f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.GuardHit, CombatAttackType.SkillAttack, 0.035f, 0.10f, true, 0.035f, 0.08f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectGuard, CombatAttackType.LightAttack, 0.06f, 0.06f, true, 0.075f, 0.12f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectGuard, CombatAttackType.HeavyAttack, 0.06f, 0.06f, true, 0.075f, 0.12f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectGuard, CombatAttackType.SkillAttack, 0.06f, 0.06f, true, 0.075f, 0.12f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectEvade, CombatAttackType.LightAttack, 0f, 1f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectEvade, CombatAttackType.HeavyAttack, 0f, 1f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.PerfectEvade, CombatAttackType.SkillAttack, 0f, 1f, false, 0f, 0f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Knockdown, CombatAttackType.LightAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Knockdown, CombatAttackType.HeavyAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Knockdown, CombatAttackType.SkillAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Dead, CombatAttackType.LightAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Dead, CombatAttackType.HeavyAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f),
                new CombatFeedbackProfile(CombatFeedbackKind.Dead, CombatAttackType.SkillAttack, 0.075f, 0.07f, true, 0.10f, 0.16f, 34f)
            };
        }
    }
}
