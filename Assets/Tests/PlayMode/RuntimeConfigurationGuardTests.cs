// 文件说明：验证运行时缺关键 Profile 时拒绝动作而不是创建默认玩法配置。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class RuntimeConfigurationGuardTests
    {
        [Test]
        public void BossMotionController_RejectsAttackWhenMotionWarpProfileMissing()
        {
            GameObject boss = new GameObject("BossMotionGuardTest");
            GameObject target = new GameObject("Target");
            BossAttackTimelineAsset timeline = null;
            try
            {
                boss.AddComponent<CharacterController>();
                BossMotionController controller = boss.AddComponent<BossMotionController>();
                timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
                timeline.ConfigureIdentity(CombatTimelineOwner.Boss, CombatTimelineActionKind.BossAttack, "NoProfile_Attack", "No Profile", "Anim_NoProfile", 1f, 60f);
                BossAttackDefinition definition = timeline.ToBossAttackDefinition();

                bool canStart = controller.CanStartAttackReadOnly(definition, target.transform);

                Assert.That(canStart, Is.False);
            }
            finally
            {
                if (timeline != null)
                {
                    Object.DestroyImmediate(timeline);
                }

                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void PlayerMovementMotor_IgnoresActionMotionWhenProfileMissing()
        {
            GameObject player = new GameObject("PlayerMotionGuardTest");
            try
            {
                player.AddComponent<CharacterController>();
                PlayerMovementMotor motor = player.AddComponent<PlayerMovementMotor>();
                PlayerStateContext context = new PlayerStateContext
                {
                    CurrentState = PlayerStateId.Evade,
                    PlayerTransform = player.transform
                };
                context.RequestActionMotion(PlayerActionMotionId.EvadeForward);

                bool loggerEnabled = Debug.unityLogger.logEnabled;
                Debug.unityLogger.logEnabled = false;
                try
                {
                    motor.Tick(context, 0.016f);
                }
                finally
                {
                    Debug.unityLogger.logEnabled = loggerEnabled;
                }

                Assert.That(context.IsActionMotionActive, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void PlayerBossSeparation_MaintainsBossDistanceBeforeAndAfterLockOnCancellation()
        {
            GameObject player = new GameObject("PlayerBossSeparationTest");
            GameObject boss = new GameObject("PlayerBossSeparationBoss");
            try
            {
                CharacterController characterController = player.AddComponent<CharacterController>();
                PlayerBossSeparation separation = player.AddComponent<PlayerBossSeparation>();
                separation.BossBodyCenter = boss.transform;
                player.transform.position = Vector3.back;

                PlayerStateContext lockedContext = new PlayerStateContext
                {
                    CurrentState = PlayerStateId.Locomotion,
                    PlayerTransform = player.transform,
                    IsLockOn = true,
                    LockOnTarget = boss.transform
                };
                PlayerStateContext unlockedContext = new PlayerStateContext
                {
                    CurrentState = PlayerStateId.Locomotion,
                    PlayerTransform = player.transform,
                    IsLockOn = false,
                    LockOnTarget = null
                };

                Vector3 requestedDelta = Vector3.forward * 0.5f;
                Vector3 lockedDelta = separation.ClipHorizontalDelta(lockedContext, requestedDelta);
                Vector3 unlockedDelta = separation.ClipHorizontalDelta(unlockedContext, requestedDelta);

                Assert.That(lockedDelta.x, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(lockedDelta.z, Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(unlockedDelta.x, Is.EqualTo(lockedDelta.x).Within(0.0001f));
                Assert.That(unlockedDelta.z, Is.EqualTo(lockedDelta.z).Within(0.0001f));

                characterController.enabled = false;
                player.transform.position = Vector3.back * 0.3f;
                characterController.enabled = true;
                float beforeCorrectionZ = player.transform.position.z;
                separation.PostCorrect(unlockedContext);

                Assert.That(player.transform.position.z, Is.LessThan(beforeCorrectionZ));
                Assert.That(separation.LastPostCorrectionDelta.z, Is.LessThan(0f));
            }
            finally
            {
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void PlayerMovementMotor_StationaryGuardFacesLockOnTarget()
        {
            GameObject player = new GameObject("PlayerGuardFacingTest");
            GameObject target = new GameObject("PlayerGuardFacingTarget");
            try
            {
                player.AddComponent<CharacterController>();
                PlayerMovementMotor motor = player.AddComponent<PlayerMovementMotor>();
                target.transform.position = Vector3.right * 5f;

                PlayerStateContext context = new PlayerStateContext
                {
                    CurrentState = PlayerStateId.Guard,
                    PlayerTransform = player.transform,
                    IsLockOn = true,
                    LockOnTarget = target.transform,
                    CurrentGuardReaction = PlayerGuardReactionId.None
                };

                motor.Tick(context, 1f);

                Assert.That(Vector3.Angle(player.transform.forward, Vector3.right), Is.LessThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }

        [TestCase(PlayerGuardReactionId.GuardHit)]
        [TestCase(PlayerGuardReactionId.PerfectGuard)]
        public void PlayerMovementMotor_GuardReactionDoesNotAutoFaceLockOnTarget(PlayerGuardReactionId reaction)
        {
            GameObject player = new GameObject("PlayerGuardReactionFacingTest");
            GameObject target = new GameObject("PlayerGuardReactionFacingTarget");
            try
            {
                player.AddComponent<CharacterController>();
                PlayerMovementMotor motor = player.AddComponent<PlayerMovementMotor>();
                target.transform.position = Vector3.right * 5f;

                PlayerStateContext context = new PlayerStateContext
                {
                    CurrentState = PlayerStateId.Guard,
                    PlayerTransform = player.transform,
                    IsLockOn = true,
                    LockOnTarget = target.transform,
                    CurrentGuardReaction = reaction
                };

                motor.Tick(context, 1f);

                Assert.That(Vector3.Angle(player.transform.forward, Vector3.forward), Is.LessThan(1f));
            }
            finally
            {
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(player);
            }
        }
    }
}
