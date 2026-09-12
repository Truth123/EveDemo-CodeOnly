// 文件说明：验证 Boss 黄光与红光攻击预警的互斥播放、重播和清理。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅验证攻击提示的一次性生命周期。

using NUnit.Framework;
using ProjectEVE.Boss.Combat;
using System.Reflection;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossAttackCueVfxControllerTests
    {
        private GameObject controllerObject;
        private GameObject yellowRoot;
        private GameObject redRoot;
        private GameObject acceleratedRedRoot;
        private ParticleSystem yellowParticle;
        private ParticleSystem redParticle;
        private ParticleSystem acceleratedRedParticle;
        private Light yellowLight;
        private Light redLight;
        private Light acceleratedRedLight;
        private BossAttackCueVfxController controller;

        [SetUp]
        public void SetUp()
        {
            controllerObject = new GameObject("BossAttackCue_Test");
            controller = controllerObject.AddComponent<BossAttackCueVfxController>();

            yellowRoot = CreateCueRoot("FX_YellowCue", out yellowParticle, out yellowLight);
            redRoot = CreateCueRoot("FX_RedCue", out redParticle, out redLight);
            acceleratedRedRoot = CreateCueRoot(
                "FX_RedCue_053s",
                out acceleratedRedParticle,
                out acceleratedRedLight);
            controller.BindYellowEffectRoot(yellowRoot);
            controller.BindRedEffectRoot(redRoot);
            controller.BindAcceleratedRedEffectRoot(acceleratedRedRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (controllerObject != null)
            {
                Object.DestroyImmediate(controllerObject);
            }
        }

        [Test]
        public void PlayYellowCue_ActivatesOnlyYellowRoot()
        {
            bool played = controller.PlayYellowCue();

            Assert.That(played, Is.True);
            Assert.That(yellowRoot.activeSelf, Is.True);
            Assert.That(yellowParticle.isPlaying, Is.True);
            Assert.That(yellowLight.enabled, Is.True);
            Assert.That(redRoot.activeSelf, Is.False);
            Assert.That(controller.ActiveRoot, Is.SameAs(yellowRoot));
        }

        [Test]
        public void PlayRedCue_StopsYellowAndActivatesOnlyRedRoot()
        {
            controller.PlayYellowCue();

            bool played = controller.PlayRedCue();

            Assert.That(played, Is.True);
            Assert.That(yellowRoot.activeSelf, Is.False);
            Assert.That(yellowLight.enabled, Is.False);
            Assert.That(redRoot.activeSelf, Is.True);
            Assert.That(redParticle.isPlaying, Is.True);
            Assert.That(redLight.enabled, Is.True);
            Assert.That(controller.ActiveRoot, Is.SameAs(redRoot));
        }

        [Test]
        public void PlayRedCue_RestartsAnEffectThatIsAlreadyPlaying()
        {
            controller.PlayRedCue();
            redParticle.time = 0.25f;

            controller.PlayRedCue();

            Assert.That(redParticle.time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(redParticle.isPlaying, Is.True);
        }

        [Test]
        public void PlayAcceleratedRedCue_StopsRegularRedAndActivatesOnlyAcceleratedRoot()
        {
            controller.PlayRedCue();

            bool played = controller.PlayAcceleratedRedCue();

            Assert.That(played, Is.True);
            Assert.That(redRoot.activeSelf, Is.False);
            Assert.That(redLight.enabled, Is.False);
            Assert.That(acceleratedRedRoot.activeSelf, Is.True);
            Assert.That(acceleratedRedParticle.isPlaying, Is.True);
            Assert.That(acceleratedRedLight.enabled, Is.True);
            Assert.That(controller.ActiveRoot, Is.SameAs(acceleratedRedRoot));
        }

        [Test]
        public void AcceleratedParticle_VisibleDurationUsesSimulationSpeed()
        {
            ParticleSystem.MainModule main = redParticle.main;
            main.startDelay = 0.2f;
            main.startLifetime = 0.8f;
            main.simulationSpeed = 2f;
            MethodInfo calculateVisibleDuration = typeof(BossAttackCueVfxController)
                .GetMethod(
                    "CalculateVisibleDuration",
                    BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(calculateVisibleDuration, Is.Not.Null);
            float visibleDuration = (float)calculateVisibleDuration.Invoke(
                null,
                new object[] { redRoot });

            Assert.That(visibleDuration, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void StopCue_HidesActiveRootAndDisablesLight()
        {
            controller.PlayRedCue();

            controller.StopCue();

            Assert.That(redRoot.activeSelf, Is.False);
            Assert.That(redLight.enabled, Is.False);
            Assert.That(controller.IsCuePlaying, Is.False);
            Assert.That(controller.ActiveRoot, Is.Null);
        }

        [Test]
        public void OnDisable_StopsCueDuringInterruption()
        {
            controller.PlayYellowCue();

            controller.enabled = false;

            Assert.That(yellowRoot.activeSelf, Is.False);
            Assert.That(yellowLight.enabled, Is.False);
            Assert.That(controller.IsCuePlaying, Is.False);
        }

        /// <summary>创建带粒子和灯光的测试提示根，并挂到控制器对象下。</summary>
        /// <param name="name">测试提示根名称。</param>
        /// <param name="particleSystem">写回创建的根粒子系统。</param>
        /// <param name="cueLight">写回创建的提示灯光。</param>
        /// <returns>创建并完成非循环配置的提示根对象。</returns>
        private GameObject CreateCueRoot(
            string name,
            out ParticleSystem particleSystem,
            out Light cueLight)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(controllerObject.transform, false);
            particleSystem = root.AddComponent<ParticleSystem>();
            ConfigureParticle(particleSystem);
            cueLight = root.AddComponent<Light>();
            return root;
        }

        /// <summary>将测试粒子设为与正式提示一致的非循环、非自动播放语义。</summary>
        /// <param name="particleSystem">需要配置为事件驱动播放的测试粒子系统。</param>
        private static void ConfigureParticle(ParticleSystem particleSystem)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.68f;
            main.startLifetime = 0.68f;
        }
    }
}
