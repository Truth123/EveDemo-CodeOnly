// 文件说明：验证 Skill1 三段主粒子、命中特效和正式 SkillEffect Prefab 的生命周期契约。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅验证玩家技能表现接线。

using System.Reflection;
using NUnit.Framework;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.States;
using UnityEditor;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class PlayerSkillEffectControllerTests
    {
        private const string SkillEffectPrefabPath = "Assets/Prefabs/Player/SkillEffect.prefab";

        private GameObject effectRoot;
        private GameObject hitEffect;
        private ParticleSystem mainParticle;
        private ParticleSystem hitParticle;
        private PlayerSkillEffectController controller;

        [SetUp]
        public void SetUp()
        {
            effectRoot = new GameObject("SkillEffect");
            mainParticle = effectRoot.AddComponent<ParticleSystem>();
            ConfigureParticle(mainParticle);

            hitEffect = new GameObject("hitEffect");
            hitEffect.transform.SetParent(effectRoot.transform, false);
            hitParticle = hitEffect.AddComponent<ParticleSystem>();
            ConfigureParticle(hitParticle);
            hitEffect.SetActive(false);

            controller = effectRoot.AddComponent<PlayerSkillEffectController>();
        }

        [TearDown]
        public void TearDown()
        {
            if (effectRoot != null)
            {
                Object.DestroyImmediate(effectRoot);
            }
        }

        [Test]
        public void PlaySegment_RestartsMainParticlesAndKeepsHitEffectInactive()
        {
            bool played = controller.PlaySegment();

            Assert.That(played, Is.True);
            Assert.That(mainParticle.isPlaying, Is.True);
            Assert.That(hitEffect.activeSelf, Is.False);
            Assert.That(hitParticle.isPlaying, Is.False);
        }

        [Test]
        public void PlayHitEffect_PlaysOnlyOnceUntilHitWindowEnds()
        {
            controller.PlaySegment();

            bool firstPlay = controller.PlayHitEffect();
            bool repeatedPlay = controller.PlayHitEffect();

            Assert.That(firstPlay, Is.True);
            Assert.That(repeatedPlay, Is.False);
            Assert.That(hitEffect.activeSelf, Is.True);
            Assert.That(hitParticle.isPlaying, Is.True);

            controller.EndHitWindow();

            Assert.That(hitEffect.activeSelf, Is.False);
            Assert.That(hitParticle.isPlaying, Is.False);
            Assert.That(controller.PlayHitEffect(), Is.True);
        }

        [Test]
        public void StopAll_ClearsMainAndHitParticlesDuringInterruption()
        {
            controller.PlaySegment();
            controller.PlayHitEffect();

            controller.StopAll();

            Assert.That(mainParticle.isPlaying, Is.False);
            Assert.That(mainParticle.particleCount, Is.Zero);
            Assert.That(hitParticle.isPlaying, Is.False);
            Assert.That(hitEffect.activeSelf, Is.False);
        }

        [Test]
        public void PlayerSkillState_ReplaysEffectAtEachMotionWindowStart()
        {
            CombatTimelineProvider.ResetCache();
            PlayerStateContext context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 32f,
                MaxBetaEnergy = 32f
            });
            context.SetState(PlayerStateId.Skill);
            PlayerSkillState state = new PlayerSkillState(controller);

            try
            {
                state.Enter(context);

                context.StateElapsedTime = 0.49f;
                state.Tick(context, 0f);
                Assert.That(mainParticle.isPlaying, Is.False);

                context.StateElapsedTime = 0.60f;
                state.Tick(context, 0f);
                Assert.That(mainParticle.isPlaying, Is.True);
                mainParticle.time = 0.25f;

                context.StateElapsedTime = 0.70f;
                state.Tick(context, 0f);
                Assert.That(mainParticle.time, Is.EqualTo(0.25f).Within(0.0001f));

                context.StateElapsedTime = 0.85f;
                state.Tick(context, 0f);
                mainParticle.time = 0.25f;
                context.StateElapsedTime = 1.00f;
                state.Tick(context, 0f);
                Assert.That(mainParticle.time, Is.EqualTo(0f).Within(0.0001f));

                context.StateElapsedTime = 1.40f;
                state.Tick(context, 0f);
                mainParticle.time = 0.25f;
                context.StateElapsedTime = 1.80f;
                state.Tick(context, 0f);
                Assert.That(mainParticle.time, Is.EqualTo(0f).Within(0.0001f));

                state.Exit(context);
                Assert.That(mainParticle.isPlaying, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
            }
        }

        [TestCase(CombatHitOutcome.DamageOnly, true)]
        [TestCase(CombatHitOutcome.HitReaction, true)]
        [TestCase(CombatHitOutcome.Knockdown, true)]
        [TestCase(CombatHitOutcome.Dead, true)]
        [TestCase(CombatHitOutcome.None, false)]
        [TestCase(CombatHitOutcome.IgnoredByInvincible, false)]
        [TestCase(CombatHitOutcome.PerfectEvade, false)]
        [TestCase(CombatHitOutcome.PerfectGuard, false)]
        public void SkillHitVisualOutcome_OnlyAcceptsRealDamageContacts(
            CombatHitOutcome outcome,
            bool expected)
        {
            MethodInfo method = typeof(PlayerWeaponHitbox).GetMethod(
                "IsSkillHitVisualOutcome",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            Assert.That((bool)method.Invoke(null, new object[] { outcome }), Is.EqualTo(expected));
        }

        [Test]
        public void SkillEffectPrefab_HasControllerAndDisablesPlayOnAwake()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SkillEffectPrefabPath);

            Assert.That(prefab, Is.Not.Null);
            Assert.That(prefab.GetComponent<PlayerSkillEffectController>(), Is.Not.Null);
            Assert.That(prefab.transform.Find("hitEffect"), Is.Not.Null);
            Assert.That(prefab.transform.Find("hitEffect").gameObject.activeSelf, Is.False);

            ParticleSystem[] particleSystems = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(particleSystems.Length, Is.EqualTo(14));
            for (int i = 0; i < particleSystems.Length; i++)
            {
                Assert.That(
                    particleSystems[i].main.playOnAwake,
                    Is.False,
                    $"{particleSystems[i].name} 不应在场景加载时自动播放。");
            }
        }

        /// <summary>将测试粒子设为非循环且不自动播放，模拟正式 SkillEffect 的控制语义。</summary>
        /// <param name="particleSystem">需要交给控制器显式重播的测试粒子。</param>
        private static void ConfigureParticle(ParticleSystem particleSystem)
        {
            particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.5f;
            main.startLifetime = 0.5f;
        }
    }
}
