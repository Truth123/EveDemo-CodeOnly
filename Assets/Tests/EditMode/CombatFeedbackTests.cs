// 文件说明：验证 Combat Feedback V1 的事件映射、profile 选择、HitStop 与 PerfectEvade 子弹时间。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中构造临时对象，不影响正式场景。

using System.Reflection;
using NUnit.Framework;
using ProjectEVE.Combat;
using ProjectEVE.Feedback;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class CombatFeedbackTests
    {
        [Test]
        public void CombatFeedbackEvent_FromHit_IncludesAttackType()
        {
            CombatHitData hit = CreateHit(CombatTeam.Player, CombatAttackType.HeavyAttack);
            CombatHitResult result = new CombatHitResult(CombatHitOutcome.HitReaction, 10f, 0f);

            CombatFeedbackEvent feedbackEvent = CombatFeedbackEvent.FromHit(hit, result, null, null);

            Assert.That(feedbackEvent.Kind, Is.EqualTo(CombatFeedbackKind.PlayerHit));
            Assert.That(feedbackEvent.AttackType, Is.EqualTo(CombatAttackType.HeavyAttack));
            Assert.That(feedbackEvent.Outcome, Is.EqualTo(CombatHitOutcome.HitReaction));
        }

        [Test]
        public void CombatFeedbackEvent_DamageOnlyProducesHitFeedback()
        {
            CombatHitData hit = CreateHit(CombatTeam.Player, CombatAttackType.SkillAttack);
            CombatHitResult result = new CombatHitResult(CombatHitOutcome.DamageOnly, 6f, 0f);

            CombatFeedbackEvent feedbackEvent = CombatFeedbackEvent.FromHit(hit, result, null, null);

            Assert.That(feedbackEvent.Kind, Is.EqualTo(CombatFeedbackKind.PlayerHit));
            Assert.That(feedbackEvent.AttackType, Is.EqualTo(CombatAttackType.SkillAttack));
        }

        [Test]
        public void CombatFeedbackEvent_NoneAndInvincibleProduceNoFeedback()
        {
            CombatHitData hit = CreateHit(CombatTeam.Boss, CombatAttackType.LightAttack);

            CombatFeedbackEvent noneEvent = CombatFeedbackEvent.FromHit(
                hit,
                new CombatHitResult(CombatHitOutcome.None, 0f, 0f),
                null,
                null);
            CombatFeedbackEvent invincibleEvent = CombatFeedbackEvent.FromHit(
                hit,
                new CombatHitResult(CombatHitOutcome.IgnoredByInvincible, 0f, 0f),
                null,
                null);

            Assert.That(noneEvent.Kind, Is.EqualTo(CombatFeedbackKind.None));
            Assert.That(invincibleEvent.Kind, Is.EqualTo(CombatFeedbackKind.None));
        }

        [Test]
        public void CombatFeedbackEvent_PerfectGuardMapsToPerfectGuardFeedback()
        {
            CombatHitData hit = CreateHit(CombatTeam.Boss, CombatAttackType.HeavyAttack);
            CombatHitResult result = new CombatHitResult(CombatHitOutcome.PerfectGuard, 0f, 0f);

            CombatFeedbackEvent feedbackEvent = CombatFeedbackEvent.FromHit(hit, result, null, null);

            Assert.That(feedbackEvent.Kind, Is.EqualTo(CombatFeedbackKind.PerfectGuard));
            Assert.That(feedbackEvent.AttackerTeam, Is.EqualTo(CombatTeam.Boss));
            Assert.That(feedbackEvent.AttackType, Is.EqualTo(CombatAttackType.HeavyAttack));
            Assert.That(feedbackEvent.Outcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
            Assert.That(feedbackEvent.AppliedHpDamage, Is.Zero);
        }

        [Test]
        public void CombatFeedbackPlayer_ProfileSelectsByKindAndAttackType()
        {
            CombatFeedbackPlayer.CombatFeedbackProfile[] profiles =
            {
                new CombatFeedbackPlayer.CombatFeedbackProfile(
                    CombatFeedbackKind.PlayerHit,
                    CombatAttackType.LightAttack,
                    0.02f,
                    0.2f,
                    false,
                    0f,
                    0f,
                    34f),
                new CombatFeedbackPlayer.CombatFeedbackProfile(
                    CombatFeedbackKind.PlayerHit,
                    CombatAttackType.HeavyAttack,
                    0.07f,
                    0.08f,
                    true,
                    0.04f,
                    0.1f,
                    40f)
            };

            CombatFeedbackPlayer.CombatFeedbackPlaybackSettings settings =
                CombatFeedbackPlayer.ResolvePlaybackSettings(
                    CombatFeedbackKind.PlayerHit,
                    CombatAttackType.HeavyAttack,
                    profiles);

            Assert.That(settings.HitStopDuration, Is.EqualTo(0.07f).Within(0.0001f));
            Assert.That(settings.HitStopTimeScale, Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(settings.EnableCameraShake, Is.True);
            Assert.That(settings.ShakeAmplitude, Is.EqualTo(0.04f).Within(0.0001f));
            Assert.That(settings.ShakeFrequency, Is.EqualTo(40f).Within(0.0001f));
        }

        [Test]
        public void CombatFeedbackPlayer_ProfileCanDisableCameraShake()
        {
            CombatFeedbackPlayer.CombatFeedbackProfile[] profiles =
            {
                new CombatFeedbackPlayer.CombatFeedbackProfile(
                    CombatFeedbackKind.PlayerHit,
                    CombatAttackType.SkillAttack,
                    0.03f,
                    0.12f,
                    false,
                    0.2f,
                    0.2f,
                    34f)
            };

            CombatFeedbackPlayer.CombatFeedbackPlaybackSettings settings =
                CombatFeedbackPlayer.ResolvePlaybackSettings(
                    CombatFeedbackKind.PlayerHit,
                    CombatAttackType.SkillAttack,
                    profiles);

            Assert.That(settings.HitStopDuration, Is.EqualTo(0.03f).Within(0.0001f));
            Assert.That(settings.EnableCameraShake, Is.False);
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectGuardDefaultProfileMatchesV1()
        {
            CombatFeedbackPlayer.CombatFeedbackProfile[] profiles = CreateDefaultFeedbackProfiles();

            CombatFeedbackPlayer.CombatFeedbackPlaybackSettings settings =
                CombatFeedbackPlayer.ResolvePlaybackSettings(
                    CombatFeedbackKind.PerfectGuard,
                    CombatAttackType.HeavyAttack,
                    profiles);

            Assert.That(settings.HitStopDuration, Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(settings.HitStopTimeScale, Is.EqualTo(0.06f).Within(0.0001f));
            Assert.That(settings.EnableCameraShake, Is.True);
            Assert.That(settings.ShakeAmplitude, Is.EqualTo(0.075f).Within(0.0001f));
            Assert.That(settings.ShakeDuration, Is.EqualTo(0.12f).Within(0.0001f));
            Assert.That(settings.ShakeFrequency, Is.EqualTo(34f).Within(0.0001f));
        }

        [Test]
        public void CombatFeedbackPlayer_BossHitUsesBossSpecificVfxWhenBound()
        {
            GameObject playerObject = new GameObject("CombatFeedbackPlayer");
            GameObject fallbackObject = new GameObject("FallbackHitVfx");
            GameObject bossObject = new GameObject("BossHitVfx");
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            ParticleSystem fallbackVfx = fallbackObject.AddComponent<ParticleSystem>();
            ParticleSystem bossVfx = bossObject.AddComponent<ParticleSystem>();
            try
            {
                SetPrivateField(player, "hitVfx", fallbackVfx);
                SetPrivateField(player, "bossHitVfx", bossVfx);

                ResolveVisualAssets(player, CombatFeedbackKind.BossHit, out ParticleSystem resolvedVfx, out _);

                Assert.That(resolvedVfx, Is.EqualTo(bossVfx));
            }
            finally
            {
                Object.DestroyImmediate(bossObject);
                Object.DestroyImmediate(fallbackObject);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void CombatFeedbackPlayer_BossHitFallsBackToGenericHitVfx()
        {
            GameObject playerObject = new GameObject("CombatFeedbackPlayer");
            GameObject fallbackObject = new GameObject("FallbackHitVfx");
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            ParticleSystem fallbackVfx = fallbackObject.AddComponent<ParticleSystem>();
            try
            {
                SetPrivateField(player, "hitVfx", fallbackVfx);

                ResolveVisualAssets(player, CombatFeedbackKind.BossHit, out ParticleSystem resolvedVfx, out _);

                Assert.That(resolvedVfx, Is.EqualTo(fallbackVfx));
            }
            finally
            {
                Object.DestroyImmediate(fallbackObject);
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void CombatHitStop_ClampsDurationAndTimeScale()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject gameObject = new GameObject("CombatHitStopClampTest");
            CombatHitStop hitStop = gameObject.AddComponent<CombatHitStop>();
            try
            {
                hitStop.Request(999f, 0f);

                FieldInfo restoreField = typeof(CombatHitStop).GetField(
                    "hitStopRestoreAtUnscaledTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(restoreField, Is.Not.Null);

                float restoreAt = (float)restoreField.GetValue(hitStop);
                float remaining = restoreAt - Time.unscaledTime;

                Assert.That(Time.timeScale, Is.EqualTo(0.03f).Within(0.0001f));
                Assert.That(remaining, Is.InRange(0f, 0.13f));
            }
            finally
            {
                hitStop.CancelActiveHitStop();
                Object.DestroyImmediate(gameObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatHitStop_SlowMotionAppliesAndRecoversWithUnscaledTime()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject gameObject = new GameObject("CombatHitStopSlowMotionTest");
            CombatHitStop hitStop = gameObject.AddComponent<CombatHitStop>();
            try
            {
                hitStop.RequestSlowMotion(0.25f, 0.2f, 0.18f);

                Assert.That(Time.timeScale, Is.EqualTo(0.2f).Within(0.0001f));

                SetPrivateField(hitStop, "slowMotionRecoverUntilUnscaledTime", Time.unscaledTime - 0.01f);
                InvokePrivateUpdate(hitStop);

                Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale).Within(0.0001f));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixedDeltaTime).Within(0.0001f));
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(gameObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatHitStop_ContactHitStopDuringSlowMotionReturnsToSlowMotion()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject gameObject = new GameObject("CombatHitStopOverlapTest");
            CombatHitStop hitStop = gameObject.AddComponent<CombatHitStop>();
            try
            {
                hitStop.RequestSlowMotion(0.25f, 0.2f, 0.18f);
                hitStop.Request(0.05f, 0.08f);

                Assert.That(Time.timeScale, Is.EqualTo(0.08f).Within(0.0001f));

                SetPrivateField(hitStop, "hitStopRestoreAtUnscaledTime", Time.unscaledTime - 0.01f);
                InvokePrivateUpdate(hitStop);

                Assert.That(Time.timeScale, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(gameObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatHitStop_CancelAllTimeDilationRestoresOriginalTimeScale()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject gameObject = new GameObject("CombatHitStopCancelAllTest");
            CombatHitStop hitStop = gameObject.AddComponent<CombatHitStop>();
            try
            {
                hitStop.RequestSlowMotion(0.25f, 0.2f, 0.18f);
                hitStop.Request(0.05f, 0.08f);

                hitStop.CancelAllTimeDilation();

                Assert.That(Time.timeScale, Is.EqualTo(originalTimeScale).Within(0.0001f));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(originalFixedDeltaTime).Within(0.0001f));
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(gameObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectGuardUsesContactFeedbackNotBulletTime()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject hitStopObject = new GameObject("PerfectGuardContactHitStop");
            GameObject playerObject = new GameObject("CombatFeedbackPlayer");
            CombatHitStop hitStop = hitStopObject.AddComponent<CombatHitStop>();
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            try
            {
                SetPrivateField(player, "hitStop", hitStop);

                InvokeHandleFeedback(player, CreatePerfectGuardFeedbackEvent());

                Assert.That((bool)GetPrivateField(hitStop, "hitStopActive"), Is.True);
                Assert.That((bool)GetPrivateField(hitStop, "slowMotionActive"), Is.False);
                Assert.That(Time.timeScale, Is.EqualTo(0.06f).Within(0.0001f));
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(hitStopObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectEvadePlaysConfiguredSfx()
        {
            GameObject playerObject = new GameObject("PerfectEvadeSfxFeedback");
            AudioSource audioSource = playerObject.AddComponent<AudioSource>();
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            AudioClip voiceClip = AudioClip.Create(
                "PerfectEvadeSfx",
                4410,
                1,
                44100,
                false);
            try
            {
                SetPrivateField(player, "enableHitStop", false);
                SetPrivateField(player, "enableCameraShake", false);
                SetPrivateField(player, "enableVfx", false);
                SetPrivateField(player, "enableSfx", true);
                SetPrivateField(player, "audioSource", audioSource);
                SetPrivateField(player, "enablePerfectEvadeBulletTime", false);
                SetPrivateField(player, "enablePerfectEvadeDirectionLines", false);
                SetPrivateField(player, "perfectEvadeClip", voiceClip);

                InvokeHandleFeedback(player, CreatePerfectEvadeFeedbackEvent());

                Assert.That(audioSource.isPlaying, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(voiceClip);
            }
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectGuardDoesNotPlayPerfectEvadeClip()
        {
            GameObject playerObject = new GameObject("PerfectGuardWithoutEvadeSfx");
            AudioSource audioSource = playerObject.AddComponent<AudioSource>();
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            AudioClip voiceClip = AudioClip.Create(
                "PerfectEvadeSfx",
                4410,
                1,
                44100,
                false);
            try
            {
                SetPrivateField(player, "enableHitStop", false);
                SetPrivateField(player, "enableCameraShake", false);
                SetPrivateField(player, "enableVfx", false);
                SetPrivateField(player, "enableSfx", true);
                SetPrivateField(player, "audioSource", audioSource);
                SetPrivateField(player, "perfectGuardClip", null);
                SetPrivateField(player, "perfectEvadeClip", voiceClip);

                InvokeHandleFeedback(player, CreatePerfectGuardFeedbackEvent());

                Assert.That(audioSource.isPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(voiceClip);
            }
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectEvadeRequestsSlowMotion()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject hitStopObject = new GameObject("PerfectEvadeHitStop");
            GameObject playerObject = new GameObject("CombatFeedbackPlayer");
            CombatHitStop hitStop = hitStopObject.AddComponent<CombatHitStop>();
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            try
            {
                SetPrivateField(player, "hitStop", hitStop);

                InvokeHandleFeedback(player, CreatePerfectEvadeFeedbackEvent());

                Assert.That(Time.timeScale, Is.EqualTo(0.2f).Within(0.0001f));
                Assert.That((bool)GetPrivateField(hitStop, "slowMotionActive"), Is.True);
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(hitStopObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        [Test]
        public void CombatFeedbackPlayer_PerfectEvadeDoesNotRequestHitStop()
        {
            float originalTimeScale = Time.timeScale;
            float originalFixedDeltaTime = Time.fixedDeltaTime;
            GameObject hitStopObject = new GameObject("PerfectEvadeNoHitStop");
            GameObject playerObject = new GameObject("CombatFeedbackPlayer");
            CombatHitStop hitStop = hitStopObject.AddComponent<CombatHitStop>();
            CombatFeedbackPlayer player = playerObject.AddComponent<CombatFeedbackPlayer>();
            try
            {
                CombatFeedbackPlayer.CombatFeedbackProfile[] legacyProfiles =
                {
                    new CombatFeedbackPlayer.CombatFeedbackProfile(
                        CombatFeedbackKind.PerfectEvade,
                        CombatAttackType.LightAttack,
                        0.02f,
                        0.08f,
                        true,
                        0.2f,
                        0.2f,
                        34f)
                };

                SetPrivateField(player, "hitStop", hitStop);
                SetPrivateField(player, "feedbackProfiles", legacyProfiles);

                InvokeHandleFeedback(player, CreatePerfectEvadeFeedbackEvent());

                Assert.That((bool)GetPrivateField(hitStop, "hitStopActive"), Is.False);
                Assert.That((bool)GetPrivateField(hitStop, "slowMotionActive"), Is.True);
                Assert.That(Time.timeScale, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                hitStop.CancelAllTimeDilation();
                Object.DestroyImmediate(playerObject);
                Object.DestroyImmediate(hitStopObject);
                Time.timeScale = originalTimeScale;
                Time.fixedDeltaTime = originalFixedDeltaTime;
            }
        }

        private static CombatFeedbackEvent CreatePerfectGuardFeedbackEvent()
        {
            return new CombatFeedbackEvent(
                CombatFeedbackKind.PerfectGuard,
                CombatTeam.Boss,
                CombatAttackType.HeavyAttack,
                CombatHitOutcome.PerfectGuard,
                Vector3.zero,
                Vector3.forward,
                0f,
                null,
                null);
        }

        private static CombatFeedbackEvent CreatePerfectEvadeFeedbackEvent()
        {
            return new CombatFeedbackEvent(
                CombatFeedbackKind.PerfectEvade,
                CombatTeam.Boss,
                CombatAttackType.LightAttack,
                CombatHitOutcome.PerfectEvade,
                Vector3.zero,
                Vector3.forward,
                0f,
                null,
                null);
        }

        private static void InvokeHandleFeedback(CombatFeedbackPlayer player, CombatFeedbackEvent feedbackEvent)
        {
            MethodInfo method = typeof(CombatFeedbackPlayer).GetMethod(
                "HandleFeedback",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(player, new object[] { feedbackEvent });
        }

        private static void ResolveVisualAssets(
            CombatFeedbackPlayer player,
            CombatFeedbackKind kind,
            out ParticleSystem vfx,
            out AudioClip clip)
        {
            MethodInfo method = typeof(CombatFeedbackPlayer).GetMethod(
                "ResolveVisualAssets",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);

            object[] args = { kind, null, null, null };
            method.Invoke(player, args);
            vfx = (ParticleSystem)args[1];
            clip = (AudioClip)args[2];
        }

        private static void InvokePrivateUpdate(CombatHitStop hitStop)
        {
            MethodInfo method = typeof(CombatHitStop).GetMethod(
                "Update",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(hitStop, null);
        }

        private static CombatFeedbackPlayer.CombatFeedbackProfile[] CreateDefaultFeedbackProfiles()
        {
            MethodInfo method = typeof(CombatFeedbackPlayer).GetMethod(
                "CreateDefaultProfiles",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (CombatFeedbackPlayer.CombatFeedbackProfile[])method.Invoke(null, null);
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(target, value);
        }

        private static CombatHitData CreateHit(CombatTeam team, CombatAttackType attackType)
        {
            return new CombatHitData
            {
                AttackId = 1,
                AttackerTeam = team,
                AttackType = attackType,
                ReactionIntent = CombatReactionIntent.HitReaction,
                Damage = 10f,
                PoiseDamage = 0f,
                GuardDamage = 0f,
                HitPosition = Vector3.zero,
                HitDirection = Vector3.forward,
                CanBeGuarded = true,
                CanBePerfectGuarded = true,
                CanBePerfectEvaded = true
            };
        }
    }
}
