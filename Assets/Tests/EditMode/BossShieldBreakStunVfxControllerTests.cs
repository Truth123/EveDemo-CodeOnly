// 文件说明：验证 Boss 护盾击破长眩晕三阶段视觉的起播、恢复预警与中断清理。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅验证专用硬直表现生命周期。

using NUnit.Framework;
using ProjectEVE.Boss.Combat;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossShieldBreakStunVfxControllerTests
    {
        private GameObject controllerObject;
        private GameObject breakRoot;
        private GameObject loopRoot;
        private GameObject warningRoot;
        private ParticleSystem breakParticle;
        private ParticleSystem loopParticle;
        private ParticleSystem warningParticle;
        private BossShieldBreakStunVfxController controller;

        [SetUp]
        public void SetUp()
        {
            controllerObject = new GameObject("BossShieldBreakStunVfx_Test");
            controller = controllerObject.AddComponent<BossShieldBreakStunVfxController>();
            breakRoot = CreateRoot("Break", out breakParticle);
            loopRoot = CreateRoot("Loop", out loopParticle);
            warningRoot = CreateRoot("Warning", out warningParticle);
            controller.BindEffectRoots(breakRoot, loopRoot, warningRoot);
            controller.SetRecoveryWarningLeadTime(0.7f);
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
        public void BeginStun_PlaysBreakAndLoopWithoutRecoveryWarning()
        {
            bool started = controller.BeginStun();

            Assert.That(started, Is.True);
            Assert.That(breakRoot.activeSelf, Is.True);
            Assert.That(loopRoot.activeSelf, Is.True);
            Assert.That(warningRoot.activeSelf, Is.False);
            Assert.That(breakParticle.isPlaying, Is.True);
            Assert.That(loopParticle.isPlaying, Is.True);
            Assert.That(controller.IsStunPlaying, Is.True);
            Assert.That(controller.IsRecoveryWarningPlaying, Is.False);
        }

        [Test]
        public void TickStun_StartsWarningOnlyInsideFinalLeadTime()
        {
            controller.BeginStun();

            controller.TickStun(5.29f, 6f);
            Assert.That(warningRoot.activeSelf, Is.False);

            controller.TickStun(5.3f, 6f);
            Assert.That(warningRoot.activeSelf, Is.True);
            Assert.That(warningParticle.isPlaying, Is.True);
            Assert.That(controller.IsRecoveryWarningPlaying, Is.True);
        }

        [Test]
        public void StopStun_ClearsEveryStage()
        {
            controller.BeginStun();
            controller.TickStun(5.3f, 6f);

            controller.StopStun();

            Assert.That(breakRoot.activeSelf, Is.False);
            Assert.That(loopRoot.activeSelf, Is.False);
            Assert.That(warningRoot.activeSelf, Is.False);
            Assert.That(controller.IsStunPlaying, Is.False);
            Assert.That(controller.IsRecoveryWarningPlaying, Is.False);
        }

        /// <summary>创建带单个非自动播放粒子的测试阶段根。</summary>
        /// <param name="name">阶段根名称。</param>
        /// <param name="particleSystem">写回创建的粒子系统。</param>
        /// <returns>已挂在控制器对象下的阶段根。</returns>
        private GameObject CreateRoot(string name, out ParticleSystem particleSystem)
        {
            GameObject root = new GameObject(name);
            root.transform.SetParent(controllerObject.transform, false);
            particleSystem = root.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.playOnAwake = false;
            main.loop = true;
            return root;
        }
    }
}
