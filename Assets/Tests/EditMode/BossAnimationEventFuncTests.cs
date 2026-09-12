using NUnit.Framework;
using ProjectEVE.Boss.Combat;
using System.Reflection;
using UnityEditor;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossAnimationEventFuncTests
    {
        private const string MissingParticleError =
            "BossAnimationEventFunc requires Fast Move Particle for OnFastMoveParticleStart.";
        private const string MissingParticleStopError =
            "BossAnimationEventFunc requires Fast Move Particle for OnFastMoveParticleStop.";
        private const string MissingYellowCueError =
            "BossAnimationEventFunc requires Yellow Attack Cue VFX for OnYellowAttackParticleStart.";
        private const string MissingRedCueError =
            "BossAnimationEventFunc requires Attack Cue VFX for OnRedAttackParticleStart.";
        private const string MissingAcceleratedRedCueError =
            "BossAnimationEventFunc requires Attack Cue VFX for OnRedAttackParticle053Start.";
        private const string MissingSoundSourceError =
            "BossAnimationEventFunc requires Animation Event Audio Source for OnPlayRedStartSound.";
        private const string MissingRedStartSoundError =
            "BossAnimationEventFunc requires Red Start Sound for OnPlayRedStartSound.";

        private GameObject receiverObject;
        private GameObject particleRootObject;
        private GameObject particleChildObject;
        private BossAnimationEventFunc receiver;
        private ParticleSystem particleRoot;
        private ParticleSystem particleChild;
        private GameObject yellowCueRootObject;
        private GameObject redCueRootObject;
        private GameObject acceleratedRedCueRootObject;
        private ParticleSystem yellowCueParticle;
        private ParticleSystem yellowCueChildParticle;
        private Light yellowCueLight;
        private ParticleSystem redCueParticle;
        private ParticleSystem acceleratedRedCueParticle;
        private Light redCueLight;
        private Light acceleratedRedCueLight;
        private BossAttackCueVfxController attackCueController;
        private AudioSource animationEventAudioSource;
        private AudioClip redStartSound;
        private AudioClip yellowStartSound;
        private AudioClip redChargeSound;

        [SetUp]
        public void SetUp()
        {
            receiverObject = new GameObject("BossAnimationEventFuncTests_Receiver");
            receiver = receiverObject.AddComponent<BossAnimationEventFunc>();
            animationEventAudioSource = receiverObject.AddComponent<AudioSource>();
            animationEventAudioSource.playOnAwake = false;
            redStartSound = AudioClip.Create("RedStart", 4410, 1, 44100, false);
            yellowStartSound = AudioClip.Create("YellowStart", 4410, 1, 44100, false);
            redChargeSound = AudioClip.Create("RedCharge", 4410, 1, 44100, false);

            particleRootObject = new GameObject("star1");
            particleRoot = particleRootObject.AddComponent<ParticleSystem>();
            ConfigureParticle(particleRoot);

            particleChildObject = new GameObject("star");
            particleChildObject.transform.SetParent(particleRootObject.transform, false);
            particleChild = particleChildObject.AddComponent<ParticleSystem>();
            ConfigureParticle(particleChild);

            yellowCueRootObject = new GameObject("FX_Boss_YellowUnblockableCue");
            yellowCueRootObject.transform.SetParent(receiverObject.transform, false);
            yellowCueParticle = yellowCueRootObject.AddComponent<ParticleSystem>();
            ConfigureParticle(yellowCueParticle);
            GameObject yellowCueChildObject = new GameObject("HaloRing");
            yellowCueChildObject.transform.SetParent(yellowCueRootObject.transform, false);
            yellowCueChildParticle = yellowCueChildObject.AddComponent<ParticleSystem>();
            ConfigureParticle(yellowCueChildParticle);
            yellowCueLight = yellowCueRootObject.AddComponent<Light>();
            redCueRootObject = new GameObject("FX_Boss_RedFrenzyCue");
            redCueRootObject.transform.SetParent(receiverObject.transform, false);
            redCueParticle = redCueRootObject.AddComponent<ParticleSystem>();
            ConfigureParticle(redCueParticle);
            redCueLight = redCueRootObject.AddComponent<Light>();
            acceleratedRedCueRootObject = new GameObject("FX_Boss_RedFrenzyCue_053s");
            acceleratedRedCueRootObject.transform.SetParent(receiverObject.transform, false);
            acceleratedRedCueParticle = acceleratedRedCueRootObject.AddComponent<ParticleSystem>();
            ConfigureParticle(acceleratedRedCueParticle);
            acceleratedRedCueLight = acceleratedRedCueRootObject.AddComponent<Light>();

            attackCueController = receiverObject.AddComponent<BossAttackCueVfxController>();
            attackCueController.BindYellowEffectRoot(yellowCueRootObject);
            attackCueController.BindRedEffectRoot(redCueRootObject);
            attackCueController.BindAcceleratedRedEffectRoot(acceleratedRedCueRootObject);

            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("fastMoveParticle").objectReferenceValue = particleRoot;
            serializedReceiver.FindProperty("attackCueVfx").objectReferenceValue = attackCueController;
            serializedReceiver.FindProperty("animationEventAudioSource").objectReferenceValue =
                animationEventAudioSource;
            serializedReceiver.FindProperty("redStartSound").objectReferenceValue = redStartSound;
            serializedReceiver.FindProperty("yellowStartSound").objectReferenceValue = yellowStartSound;
            serializedReceiver.FindProperty("redChargeSound").objectReferenceValue = redChargeSound;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
        }

        [TearDown]
        public void TearDown()
        {
            if (particleRootObject != null)
            {
                Object.DestroyImmediate(particleRootObject);
            }

            if (receiverObject != null)
            {
                Object.DestroyImmediate(receiverObject);
            }

            if (redStartSound != null)
            {
                Object.DestroyImmediate(redStartSound);
            }

            if (yellowStartSound != null)
            {
                Object.DestroyImmediate(yellowStartSound);
            }

            if (redChargeSound != null)
            {
                Object.DestroyImmediate(redChargeSound);
            }
        }

        [Test]
        public void OnFastMoveParticleStart_PlaysRootAndChildParticleSystems()
        {
            receiver.OnFastMoveParticleStart();

            Assert.That(particleRoot.isPlaying, Is.True);
            Assert.That(particleChild.isPlaying, Is.True);
        }

        [Test]
        public void OnFastMoveParticleStart_RestartsAnEffectThatIsAlreadyPlaying()
        {
            receiver.OnFastMoveParticleStart();
            particleRoot.time = 0.25f;
            particleChild.time = 0.25f;
            Assert.That(particleRoot.time, Is.GreaterThan(0f));

            receiver.OnFastMoveParticleStart();

            Assert.That(particleRoot.time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(particleChild.time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(particleRoot.isPlaying, Is.True);
            Assert.That(particleChild.isPlaying, Is.True);
        }

        [Test]
        public void OnFastMoveParticleStart_ReportsMissingParticleWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("fastMoveParticle").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(receiver.OnFastMoveParticleStart);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(MissingParticleError));
        }

        [Test]
        public void OnFastMoveParticleStop_StopsAndClearsRootAndChildParticleSystems()
        {
            receiver.OnFastMoveParticleStart();
            particleRoot.Emit(3);
            particleChild.Emit(2);

            receiver.OnFastMoveParticleStop();

            Assert.That(particleRoot.isPlaying, Is.False);
            Assert.That(particleChild.isPlaying, Is.False);
            Assert.That(particleRoot.particleCount, Is.Zero);
            Assert.That(particleChild.particleCount, Is.Zero);
        }

        [Test]
        public void OnFastMoveParticleStop_ReportsMissingParticleWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("fastMoveParticle").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(receiver.OnFastMoveParticleStop);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(MissingParticleStopError));
        }

        [Test]
        public void OnYellowAttackParticleStart_PlaysConfiguredCueParticlesAndLight()
        {
            receiver.OnYellowAttackParticleStart();

            Assert.That(yellowCueRootObject.activeSelf, Is.True);
            Assert.That(yellowCueParticle.isPlaying, Is.True);
            Assert.That(yellowCueChildParticle.isPlaying, Is.True);
            Assert.That(yellowCueLight.enabled, Is.True);
            Assert.That(attackCueController.IsCuePlaying, Is.True);
        }

        [Test]
        public void OnYellowAttackParticleStart_RestartsCueFromBeginning()
        {
            receiver.OnYellowAttackParticleStart();
            yellowCueParticle.time = 0.25f;
            yellowCueChildParticle.time = 0.25f;

            receiver.OnYellowAttackParticleStart();

            Assert.That(yellowCueParticle.time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(yellowCueChildParticle.time, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(yellowCueParticle.isPlaying, Is.True);
            Assert.That(yellowCueChildParticle.isPlaying, Is.True);
        }

        [Test]
        public void OnYellowAttackParticleStart_ReportsMissingControllerWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("attackCueVfx").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(receiver.OnYellowAttackParticleStart);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(MissingYellowCueError));
        }

        [Test]
        public void OnRedAttackParticleStart_PlaysRedCueAndStopsYellowCue()
        {
            receiver.OnYellowAttackParticleStart();

            receiver.OnRedAttackParticleStart();

            Assert.That(yellowCueRootObject.activeSelf, Is.False);
            Assert.That(yellowCueLight.enabled, Is.False);
            Assert.That(redCueRootObject.activeSelf, Is.True);
            Assert.That(redCueParticle.isPlaying, Is.True);
            Assert.That(redCueLight.enabled, Is.True);
            Assert.That(attackCueController.ActiveRoot, Is.SameAs(redCueRootObject));
        }

        [Test]
        public void OnRedAttackParticleStart_ReportsMissingControllerWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("attackCueVfx").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(receiver.OnRedAttackParticleStart);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(MissingRedCueError));
        }

        [Test]
        public void OnRedAttackParticle053Start_PlaysAcceleratedCueAndStopsRegularRedCue()
        {
            receiver.OnRedAttackParticleStart();

            receiver.OnRedAttackParticle053Start();

            Assert.That(redCueRootObject.activeSelf, Is.False);
            Assert.That(redCueLight.enabled, Is.False);
            Assert.That(acceleratedRedCueRootObject.activeSelf, Is.True);
            Assert.That(acceleratedRedCueParticle.isPlaying, Is.True);
            Assert.That(acceleratedRedCueLight.enabled, Is.True);
            Assert.That(
                attackCueController.ActiveRoot,
                Is.SameAs(acceleratedRedCueRootObject));
        }

        [Test]
        public void OnRedAttackParticle053Start_ReportsMissingControllerWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("attackCueVfx").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(receiver.OnRedAttackParticle053Start);
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(MissingAcceleratedRedCueError));
        }

        [Test]
        public void SoundAnimationEvents_HavePublicVoidParameterlessSignatures()
        {
            AssertAnimationEventSignature(nameof(BossAnimationEventFunc.OnPlayRedStartSound));
            AssertAnimationEventSignature(nameof(BossAnimationEventFunc.OnPlayYellowStartSound));
            AssertAnimationEventSignature(nameof(BossAnimationEventFunc.OnPlayRedChargeSound));
        }

        [Test]
        public void SoundAnimationEvents_WithConfiguredSourceAndClips_DoNotThrow()
        {
            Assert.DoesNotThrow(receiver.OnPlayRedStartSound);
            Assert.DoesNotThrow(receiver.OnPlayYellowStartSound);
            Assert.DoesNotThrow(receiver.OnPlayRedChargeSound);
        }

        [Test]
        public void OnPlayRedStartSound_ReportsMissingSourceWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("animationEventAudioSource").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();

            AssertLoggedError(receiver.OnPlayRedStartSound, MissingSoundSourceError);
        }

        [Test]
        public void OnPlayRedStartSound_ReportsMissingClipWithoutThrowing()
        {
            SerializedObject serializedReceiver = new SerializedObject(receiver);
            serializedReceiver.FindProperty("redStartSound").objectReferenceValue = null;
            serializedReceiver.ApplyModifiedPropertiesWithoutUndo();

            AssertLoggedError(receiver.OnPlayRedStartSound, MissingRedStartSoundError);
        }

        /// <summary>
        /// 将测试粒子设为与 Demo star1 相同的非循环、非自动播放语义。
        /// </summary>
        /// <param name="particleSystem">需要配置为事件驱动播放的测试粒子系统。</param>
        private static void ConfigureParticle(ParticleSystem particleSystem)
        {
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = false;
            main.playOnAwake = false;
            main.duration = 0.5f;
        }

        /// <summary>
        /// 验证指定函数满足 Unity Animation Event 所需的公开、无参数、void 签名。
        /// </summary>
        /// <param name="methodName">需要校验的动画事件函数名。</param>
        private static void AssertAnimationEventSignature(string methodName)
        {
            MethodInfo method = typeof(BossAnimationEventFunc).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(method, Is.Not.Null);
            Assert.That(method.ReturnType, Is.EqualTo(typeof(void)));
            Assert.That(method.GetParameters(), Is.Empty);
        }

        /// <summary>
        /// 执行动画事件并验证其只记录预期错误而不会抛出异常。
        /// </summary>
        /// <param name="animationEvent">需要执行的动画事件回调。</param>
        /// <param name="expectedError">预期收到的完整 Console 错误文本。</param>
        private static void AssertLoggedError(System.Action animationEvent, string expectedError)
        {
            string receivedError = null;
            Application.LogCallback captureError = (condition, stackTrace, type) =>
            {
                if (type == LogType.Error)
                {
                    receivedError = condition;
                }
            };
            Application.logMessageReceived += captureError;

            try
            {
                Assert.DoesNotThrow(() => animationEvent());
            }
            finally
            {
                Application.logMessageReceived -= captureError;
            }

            Assert.That(receivedError, Is.EqualTo(expectedError));
        }
    }
}
