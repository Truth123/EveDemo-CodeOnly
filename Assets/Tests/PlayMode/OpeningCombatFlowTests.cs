// 文件说明：验证 Demo 开场提交使用的玩家自动锁定、战斗 Idle、锁定镜头终点与 Boss 初始冷却记忆。
// 所属模块：PlayMode 测试。
// 运行影响：仅在 Unity Test Runner 中创建临时对象和内存 Timeline，不修改正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Brain;
using ProjectEVE.CameraSystem.LockOn;
using ProjectEVE.CameraSystem.Rigs;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class OpeningCombatFlowTests
    {
        private GameObject playerObject;
        private GameObject targetObject;
        private GameObject cameraObject;

        [TearDown]
        public void TearDown()
        {
            if (cameraObject != null)
            {
                Object.DestroyImmediate(cameraObject);
            }

            if (targetObject != null)
            {
                Object.DestroyImmediate(targetObject);
            }

            if (playerObject != null)
            {
                Object.DestroyImmediate(playerObject);
            }
        }

        [Test]
        public void BeginCombatIdleAndLockOn_UsesPreferredBossAndEntersCombatIdle()
        {
            playerObject = new GameObject("OpeningCombat_Player");
            playerObject.AddComponent<LockOnController>();
            PlayerStateMachine stateMachine = playerObject.AddComponent<PlayerStateMachine>();
            targetObject = new GameObject("OpeningCombat_BossTarget");
            targetObject.transform.position = new Vector3(0f, 1.2f, 8f);
            LockOnTarget target = targetObject.AddComponent<LockOnTarget>();

            stateMachine.PrepareTitlePresentation();
            Assert.That(stateMachine.Context.IsInCombat, Is.False);
            Assert.That(stateMachine.Context.IsLockOn, Is.False);

            bool locked = stateMachine.BeginCombatIdleAndLockOn(target);

            Assert.That(locked, Is.True);
            Assert.That(stateMachine.Context.CurrentState, Is.EqualTo(PlayerStateId.Idle));
            Assert.That(stateMachine.Context.IsInCombat, Is.True);
            Assert.That(stateMachine.Context.IsLockOn, Is.True);
            Assert.That(stateMachine.Context.LockOnTarget, Is.SameAs(target.TargetPoint));
            Assert.That(stateMachine.Context.ControlMode, Is.EqualTo(ControlMode.LockOn));
        }

        [Test]
        public void TryGetLockOnCameraPose_ReturnsStablePoseLookingAtPlayerPivot()
        {
            playerObject = new GameObject("OpeningCamera_Player");
            playerObject.transform.position = new Vector3(2f, 0.6f, 6.1f);
            playerObject.AddComponent<LockOnController>();
            playerObject.AddComponent<PlayerStateMachine>();

            targetObject = new GameObject("OpeningCamera_BossTarget");
            targetObject.transform.position = new Vector3(2.1f, 1.8f, -4.2f);
            LockOnTarget target = targetObject.AddComponent<LockOnTarget>();

            cameraObject = new GameObject("OpeningCamera_Rig");
            cameraObject.AddComponent<Camera>();
            PlayerCameraRig rig = cameraObject.AddComponent<PlayerCameraRig>();

            bool resolved = rig.TryGetLockOnCameraPose(
                target.TargetPoint,
                out Vector3 position,
                out Quaternion rotation);

            Vector3 expectedPivot = playerObject.transform.position + new Vector3(0f, 1.45f, 0f);
            Vector3 actualForward = rotation * Vector3.forward;
            Vector3 expectedForward = (expectedPivot - position).normalized;
            Assert.That(resolved, Is.True);
            Assert.That(IsFinite(position), Is.True);
            Assert.That(IsFinite(rotation), Is.True);
            Assert.That(Vector3.Distance(position, expectedPivot), Is.EqualTo(5.4f).Within(0.01f));
            Assert.That(Vector3.Dot(actualForward, expectedForward), Is.GreaterThan(0.999f));
        }

        [Test]
        public void BeginCooldown_StartsConfiguredDurationWithoutChangingSelectionHistory()
        {
            BossAttackTimelineAsset timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            try
            {
                timeline.ConfigureIdentity(
                    CombatTimelineOwner.Boss,
                    CombatTimelineActionKind.BossAttack,
                    "OpeningCooldownAttack",
                    "OpeningCooldownAttack",
                    "OpeningCooldownAttack",
                    1f,
                    60f);
                timeline.ConfigureBossAttack(90f, 4f, 0f);
                BossAttackDefinition definition = timeline.ToBossAttackDefinition();
                BossDecisionMemory memory = new BossDecisionMemory();

                memory.BeginCooldown(definition, 10f);

                Assert.That(memory.GetActionCooldownRemaining(definition.AttackId, 10f), Is.EqualTo(4f).Within(0.001f));
                Assert.That(memory.GetActionCooldownRemaining(definition.AttackId, 12.5f), Is.EqualTo(1.5f).Within(0.001f));
                Assert.That(memory.LastSelectedActionId, Is.Empty);
                Assert.That(memory.RepeatedSelectedActionCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        /// <summary>检查 Vector3 三个分量均为有限值。</summary>
        /// <param name="value">需要检查的世界坐标或方向。</param>
        /// <returns>true 表示三个分量均非 NaN 且非 Infinity。</returns>
        private static bool IsFinite(Vector3 value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) && float.IsFinite(value.z);
        }

        /// <summary>检查 Quaternion 四个分量均为有限值。</summary>
        /// <param name="value">需要检查的旋转。</param>
        /// <returns>true 表示四个分量均非 NaN 且非 Infinity。</returns>
        private static bool IsFinite(Quaternion value)
        {
            return float.IsFinite(value.x) && float.IsFinite(value.y) &&
                float.IsFinite(value.z) && float.IsFinite(value.w);
        }
    }
}
