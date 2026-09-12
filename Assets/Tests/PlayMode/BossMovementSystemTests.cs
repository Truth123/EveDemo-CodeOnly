// 文件说明：验证 BossMovementSystem 的正式移动入口和移动层级仲裁。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat.Timeline;
using System.Reflection;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossMovementSystemTests
    {
        [Test]
        public void BossMovementSystem_MoveReposition_MovesThroughMotionController()
        {
            GameObject boss = CreateBoss("BossMovementSystemRepositionBoss", out BossMotionController motion);
            try
            {
                BossMovementSystem movement = CreateMovementSystem(motion);
                Vector3 before = boss.transform.position;

                Vector3 applied = movement.MoveReposition(Vector3.forward, 2f, 0.25f);

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Reposition));
                Assert.That(applied.z, Is.GreaterThan(0f));
                Assert.That(boss.transform.position.z, Is.GreaterThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss);
            }
        }

        [Test]
        public void BossMovementSystem_MoveReposition_DoesNotOverrideActiveActionMotion()
        {
            GameObject boss = CreateBoss("BossMovementSystemActionBlocksRepositionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemActionBlocksRepositionTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_ActionBlocks", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfig(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);

                movement.BeginActionMotion(attack, target.transform);
                Vector3 applied = movement.MoveReposition(Vector3.forward, 2f, 0.25f);

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Action));
                Assert.That(applied, Is.EqualTo(Vector3.zero));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_ForwardsActionMoveThroughMotionController()
        {
            GameObject boss = CreateBoss("BossMovementSystemActionMotionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemActionMotionTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_ActionMotion", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);
                Vector3 before = boss.transform.position;

                movement.TickActionMotion(0.1f, 0.1f);

                Assert.That(movement.IsActionMotionActive, Is.True);
                Assert.That(boss.transform.position.z, Is.GreaterThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_OrbitRightNinetyUsesStartDistanceAsRadius()
        {
            GameObject boss = CreateBoss("BossMovementSystemOrbitRightBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemOrbitRightTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                Vector3 center = new Vector3(50f, 0f, 50f);
                TeleportCharacter(boss, center + Vector3.forward * 3f);
                target.transform.position = center;
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_OrbitRight", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithOrbit(
                    attack.AttackId,
                    stopDistance: 0.1f,
                    maxMoveDistance: 0f));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(1f, 1f);

                Assert.That(boss.transform.position.x, Is.EqualTo(center.x + 3f).Within(0.05f));
                Assert.That(boss.transform.position.z, Is.EqualTo(center.z).Within(0.05f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_OrbitIgnoresStopDistance()
        {
            Vector3 nearStopResult = TickOrbitWithStopDistance(0.1f);
            Vector3 farStopResult = TickOrbitWithStopDistance(10f);

            Assert.That(nearStopResult.x, Is.EqualTo(73f).Within(0.05f));
            Assert.That(farStopResult.x, Is.EqualTo(nearStopResult.x).Within(0.05f));
            Assert.That(farStopResult.z, Is.EqualTo(nearStopResult.z).Within(0.05f));
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_OrbitIgnoresMaxMoveDistance()
        {
            GameObject boss = CreateBoss("BossMovementSystemOrbitMaxDistanceBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemOrbitMaxDistanceTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                Vector3 center = new Vector3(60f, 0f, 60f);
                TeleportCharacter(boss, center + Vector3.forward * 3f);
                target.transform.position = center;
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_OrbitMaxDistance", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithOrbit(
                    attack.AttackId,
                    stopDistance: 0f,
                    maxMoveDistance: 0.05f));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(1f, 1f);

                Assert.That(boss.transform.position.x, Is.EqualTo(center.x + 3f).Within(0.05f));
                Assert.That(boss.transform.position.z, Is.EqualTo(center.z).Within(0.05f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_NonOrbitCodeMoveStillUsesMaxMoveDistance()
        {
            GameObject boss = CreateBoss("BossMovementSystemNonOrbitMaxDistanceBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemNonOrbitMaxDistanceTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_NonOrbitMaxDistance", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(
                    attack.AttackId,
                    BossCodeMoveWindow.ToAttackPoint(
                        "MovementSystem_NonOrbitLimited",
                        0f,
                        0.5f,
                        1f,
                        0.1f,
                        0.2f,
                        10f,
                        360f,
                        false)));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(0.1f, 0.1f);

                Assert.That(boss.transform.position.z, Is.GreaterThan(0f));
                Assert.That(boss.transform.position.z, Is.EqualTo(0.2f).Within(0.03f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_CloseTargetDoesNotCreateBackwardStopPoint()
        {
            GameObject boss = CreateBoss("BossMovementSystemCloseStopPointBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemCloseStopPointTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 0.5f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_CloseStopPoint", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(0.1f, 0.1f);

                Assert.That(boss.transform.position.z, Is.GreaterThanOrEqualTo(-0.001f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_CloseTargetFacesPlayerInsteadOfStopPoint()
        {
            GameObject boss = CreateBoss("BossMovementSystemFacingTargetBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemFacingTargetTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(1f, 0f, 0.5f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_FacingTarget", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(0.1f, 0.1f);

                Assert.That(boss.transform.rotation.eulerAngles.y, Is.GreaterThan(1f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossCodeMoveWindow_FaceTarget_CreatesZeroMovementTrackingWindow()
        {
            BossCodeMoveWindow window = BossCodeMoveWindow.FaceTarget(
                "MovementSystem_FacingOnly",
                0.2f,
                0.8f,
                720f);

            Assert.That(window.TargetingMode, Is.EqualTo(BossWarpTargetingMode.ToAttackPoint));
            Assert.That(window.ProgressMode, Is.EqualTo(BossCodeMoveProgressMode.ConstantSpeed));
            Assert.That(window.MaxMoveSpeed, Is.Zero);
            Assert.That(window.MaxMoveDistance, Is.Zero);
            Assert.That(window.MaxYawSpeed, Is.EqualTo(720f));
            Assert.That(window.FollowTarget, Is.True);
            Assert.That(window.UseDistanceBand, Is.False);
        }

        [Test]
        public void BossMovementSystem_TickActionMotion_FacingOnlyTracksTargetWithoutMovement()
        {
            GameObject boss = CreateBoss("BossMovementSystemFacingOnlyBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemFacingOnlyTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(3f, 0f, 0f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_FacingOnly", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(
                    attack.AttackId,
                    BossCodeMoveWindow.FaceTarget(
                        "MovementSystem_FacingOnly",
                        0.2f,
                        0.8f,
                        720f)));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);
                Vector3 startPosition = boss.transform.position;

                movement.TickActionMotion(0.1f, 0.1f);
                Assert.That(Quaternion.Angle(boss.transform.rotation, Quaternion.identity), Is.LessThan(0.01f));

                movement.TickActionMotion(0.1f, 0.2f);
                float firstTrackedYaw = boss.transform.eulerAngles.y;
                Assert.That(firstTrackedYaw, Is.EqualTo(72f).Within(0.5f));

                target.transform.position = new Vector3(-3f, 0f, 0f);
                movement.TickActionMotion(0.1f, 0.3f);
                float secondTrackedYaw = boss.transform.eulerAngles.y;
                Assert.That(Mathf.Abs(Mathf.DeltaAngle(secondTrackedYaw, 270f)),
                    Is.LessThan(Mathf.Abs(Mathf.DeltaAngle(firstTrackedYaw, 270f))));

                movement.TickActionMotion(0.1f, 0.9f);
                Assert.That(boss.transform.eulerAngles.y, Is.EqualTo(secondTrackedYaw).Within(0.01f));
                Assert.That(boss.transform.position, Is.EqualTo(startPosition));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_MoveReposition_IgnoresTargetBodyCollider()
        {
            GameObject boss = CreateBoss("BossMovementSystemIgnoreTargetBodyBoss", out BossMotionController motion);
            GameObject target = CreateTargetBody("BossMovementSystemIgnoreTargetBodyTarget");
            try
            {
                target.transform.position = new Vector3(0f, 0f, 0.8f);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.SetTarget(target.transform);

                Vector3 applied = movement.MoveReposition(Vector3.forward, 4f, 0.5f);

                Assert.That(applied.z, Is.GreaterThan(1.2f));
                Assert.That(boss.transform.position.z, Is.GreaterThan(1.2f));
            }
            finally
            {
                DestroyTestObjects(boss, target);
            }
        }

        [Test]
        public void BossMovementSystem_TickReactionMotion_ForwardsCurveCodeMoveThroughMotionController()
        {
            GameObject boss = CreateBoss("BossMovementSystemReactionMotionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemReactionMotionTarget");
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap"));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                Vector3 before = boss.transform.position;

                bool started = movement.BeginReactionMotion("Boss_HitStagger", "HitStaggerWrap", target.transform);
                movement.TickReactionMotion(0.25f, 0.5f);

                Assert.That(started, Is.True);
                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Reaction));
                Assert.That(boss.transform.position.z, Is.LessThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss, target, profile);
            }
        }

        [Test]
        public void BossMovementSystem_MoveReposition_DoesNotOverrideActiveReactionMotion()
        {
            GameObject boss = CreateBoss("BossMovementSystemReactionBlocksRepositionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemReactionBlocksRepositionTarget");
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap"));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginReactionMotion("Boss_HitStagger", "HitStaggerWrap", target.transform);

                Vector3 applied = movement.MoveReposition(Vector3.forward, 2f, 0.25f);

                Assert.That(applied, Is.EqualTo(Vector3.zero));
                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Reaction));
            }
            finally
            {
                DestroyTestObjects(boss, target, profile);
            }
        }

        [Test]
        public void BossMovementSystem_StopAllMotion_ClearsReactionLayer()
        {
            GameObject boss = CreateBoss("BossMovementSystemStopReactionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemStopReactionTarget");
            BossMotionWarpProfile profile = null;
            try
            {
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap"));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginReactionMotion("Boss_HitStagger", "HitStaggerWrap", target.transform);

                movement.StopAllMotion();

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.None));
                Assert.That(movement.CurrentActionId, Is.EqualTo(string.Empty));
            }
            finally
            {
                DestroyTestObjects(boss, target, profile);
            }
        }

        [Test]
        public void BossMovementSystem_EndReactionMotion_ClearsOnlyReactionLayer()
        {
            GameObject boss = CreateBoss("BossMovementSystemEndReactionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemEndReactionTarget");
            BossMotionWarpProfile profile = null;
            try
            {
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap"));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginReactionMotion("Boss_HitStagger", "HitStaggerWrap", target.transform);

                movement.EndReactionMotion();

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.None));
                Assert.That(movement.CurrentActionId, Is.EqualTo(string.Empty));
            }
            finally
            {
                DestroyTestObjects(boss, target, profile);
            }
        }

        [Test]
        public void BossMovementSystem_EndReactionMotion_DoesNotClearActiveActionLayer()
        {
            GameObject boss = CreateBoss("BossMovementSystemEndReactionKeepsActionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemEndReactionKeepsActionTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_ReactionExitAction", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);
                Vector3 before = boss.transform.position;

                movement.EndReactionMotion();
                movement.TickActionMotion(0.1f, 0.1f);

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Action));
                Assert.That(movement.CurrentActionId, Is.EqualTo(attack.AttackId));
                Assert.That(boss.transform.position.z, Is.GreaterThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_EndActionMotion_DoesNotClearActiveReactionLayer()
        {
            GameObject boss = CreateBoss("BossMovementSystemEndActionKeepsReactionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemEndActionKeepsReactionTarget");
            BossMotionWarpProfile profile = null;
            try
            {
                target.transform.position = new Vector3(0f, 0f, 3f);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap"));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginReactionMotion("Boss_HitStagger", "HitStaggerWrap", target.transform);
                Vector3 before = boss.transform.position;

                movement.EndActionMotion();
                movement.TickReactionMotion(0.25f, 0.5f);

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Reaction));
                Assert.That(movement.CurrentActionId, Is.EqualTo("Boss_HitStagger"));
                Assert.That(boss.transform.position.z, Is.LessThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss, target, profile);
            }
        }

        [Test]
        public void BossMovementSystem_StopAllMotion_ClearsActionLayerAndMotionContext()
        {
            GameObject boss = CreateBoss("BossMovementSystemStopBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemStopTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_Stop", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfig(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.StopAllMotion();

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.None));
                Assert.That(movement.CurrentActionId, Is.EqualTo(string.Empty));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_StopAllMotion_ClearsRepositionLayer()
        {
            GameObject boss = CreateBoss("BossMovementSystemStopRepositionBoss", out BossMotionController motion);
            try
            {
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.MoveReposition(Vector3.forward, 2f, 0.25f);

                movement.StopAllMotion();

                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.None));
            }
            finally
            {
                DestroyTestObjects(boss);
            }
        }

        [Test]
        public void BossMovementSystem_HandleRootMotion_IgnoresWhenActionLayerInactive()
        {
            GameObject boss = CreateBoss("BossMovementSystemRootMotionInactiveBoss", out BossMotionController motion);
            try
            {
                BossMovementSystem movement = CreateMovementSystem(motion);
                Vector3 before = boss.transform.position;

                movement.HandleRootMotion(new Vector3(0f, 0f, 1f), Quaternion.identity);

                Assert.That(boss.transform.position, Is.EqualTo(before));
            }
            finally
            {
                DestroyTestObjects(boss);
            }
        }

        [Test]
        public void BossMovementSystem_HandleRootMotion_ForwardsWhenActionLayerActive()
        {
            GameObject boss = CreateBoss("BossMovementSystemRootMotionActiveBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemRootMotionActiveTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_RootMotion", 1f, out timeline);
                profile = CreateMotionProfile(CreateRootMotionConfig(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);
                Vector3 before = boss.transform.position;

                movement.HandleRootMotion(new Vector3(0f, 0f, 1f), Quaternion.identity);

                Assert.That(boss.transform.position.z, Is.GreaterThan(before.z));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_RootMotionLifecycle_DoesNotChangeAnimatorApplyRootMotion()
        {
            GameObject boss = new GameObject("BossMovementSystemRootMotionLifecycleBoss");
            GameObject animatorObject = new GameObject("Animator");
            GameObject target = new GameObject("BossMovementSystemRootMotionLifecycleTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                boss.AddComponent<CharacterController>();
                animatorObject.transform.SetParent(boss.transform, false);
                Animator animator = animatorObject.AddComponent<Animator>();
                animator.applyRootMotion = true;

                BossMotionController motion = boss.AddComponent<BossMotionController>();
                Assert.That(animator.applyRootMotion, Is.True, "MotionController 初始化不应改写 Animator.applyRootMotion。");

                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_RootMotionLifecycle", 1f, out timeline);
                profile = CreateMotionProfile(CreateRootMotionConfig(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);

                for (int i = 0; i < 3; i++)
                {
                    movement.BeginActionMotion(attack, target.transform);
                    Assert.That(animator.applyRootMotion, Is.True, "RootMotionWarped 开始时不应重新初始化 Animator。");
                    movement.EndActionMotion();
                    Assert.That(animator.applyRootMotion, Is.True, "动作结束时不应重新初始化 Animator。");
                }

                movement.BeginActionMotion(attack, target.transform);
                movement.StopAllMotion();
                Assert.That(animator.applyRootMotion, Is.True, "StopAllMotion 不应改写 Animator.applyRootMotion。");
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        [Test]
        public void BossMovementSystem_HandleRootMotion_CodeDrivenActionDoesNotMoveBoss()
        {
            GameObject boss = CreateBoss("BossMovementSystemCodeDrivenRootMotionBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemCodeDrivenRootMotionTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_CodeDrivenRootMotion", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithCodeMove(attack.AttackId));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);
                Vector3 before = boss.transform.position;

                movement.HandleRootMotion(new Vector3(0f, 0f, 1f), Quaternion.identity);

                Assert.That(boss.transform.position, Is.EqualTo(before));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        private static GameObject CreateBoss(string name, out BossMotionController motion)
        {
            GameObject boss = new GameObject(name);
            boss.AddComponent<CharacterController>();
            motion = boss.AddComponent<BossMotionController>();
            return boss;
        }

        private static GameObject CreateTargetBody(string name)
        {
            GameObject target = new GameObject(name);
            target.AddComponent<CharacterController>();
            return target;
        }

        /// <summary>
        /// 在测试中移动带 CharacterController 的对象，并同步控制器内部位置。
        /// </summary>
        /// <param name="character">需要传送的测试角色对象。</param>
        /// <param name="position">传送后的世界坐标。</param>
        private static void TeleportCharacter(GameObject character, Vector3 position)
        {
            CharacterController controller = character.GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.enabled = false;
            }

            character.transform.position = position;
            Physics.SyncTransforms();

            if (controller != null)
            {
                controller.enabled = true;
                Physics.SyncTransforms();
            }
        }

        private static BossMovementSystem CreateMovementSystem(BossMotionController motion)
        {
            BossMovementSystem movement = new BossMovementSystem();
            movement.Bind(motion);
            return movement;
        }

        private static BossAttackDefinition CreateAttackDefinition(
            string attackId,
            float duration,
            out BossAttackTimelineAsset timeline)
        {
            timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossAttack,
                attackId,
                attackId,
                "Anim_" + attackId,
                duration,
                60f);
            timeline.ConfigureBossAttack(
                90f,
                0.1f,
                duration,
                0.1f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[0]
                }
            });
            return timeline.ToBossAttackDefinition();
        }

        private static BossMotionWarpProfile CreateMotionProfile(BossAttackMotionConfig config)
        {
            BossMotionWarpProfile profile = ScriptableObject.CreateInstance<BossMotionWarpProfile>();
            typeof(BossMotionWarpProfile)
                .GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(profile, new[] { config });
            return profile;
        }

        private static BossAttackMotionConfig CreateMotionConfig(string attackId)
        {
            return new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                null);
        }

        private static BossAttackMotionConfig CreateMotionConfigWithCodeMove(string attackId)
        {
            return CreateMotionConfigWithCodeMove(
                attackId,
                BossCodeMoveWindow.ToAttackPoint(
                    "MovementSystem_CodeMove",
                    0f,
                    0.5f,
                    1f,
                    0.1f,
                    3f,
                    10f,
                    360f,
                    false));
        }

        private static BossAttackMotionConfig CreateMotionConfigWithCodeMove(string attackId, BossCodeMoveWindow window)
        {
            return new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                new[] { window });
        }

        private static BossAttackMotionConfig CreateMotionConfigWithOrbit(
            string attackId,
            float stopDistance,
            float maxMoveDistance)
        {
            return CreateMotionConfigWithCodeMove(
                attackId,
                new BossCodeMoveWindow
                {
                    Name = "MovementSystem_Orbit",
                    StartTime = 0f,
                    EndTime = 1f,
                    TargetingMode = BossWarpTargetingMode.OrbitAroundTarget,
                    ProgressMode = BossCodeMoveProgressMode.ConstantSpeed,
                    StopDistance = stopDistance,
                    DeadZone = 0f,
                    MaxMoveDistance = maxMoveDistance,
                    MaxMoveSpeed = 100f,
                    MaxYawSpeed = 720f,
                    FollowTarget = false,
                    AllowBackwardCorrection = true,
                    UseDistanceBand = false,
                    OrbitSide = BossOrbitSide.Right,
                    OrbitAngleDegrees = 90f
                });
        }

        private static Vector3 TickOrbitWithStopDistance(float stopDistance)
        {
            GameObject boss = CreateBoss("BossMovementSystemOrbitStopDistanceBoss", out BossMotionController motion);
            GameObject target = new GameObject("BossMovementSystemOrbitStopDistanceTarget");
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                Vector3 center = new Vector3(70f, 0f, 70f);
                TeleportCharacter(boss, center + Vector3.forward * 3f);
                target.transform.position = center;
                BossAttackDefinition attack = CreateAttackDefinition("MovementSystem_OrbitStopDistance", 1f, out timeline);
                profile = CreateMotionProfile(CreateMotionConfigWithOrbit(
                    attack.AttackId,
                    stopDistance,
                    maxMoveDistance: 0f));
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                movement.BeginActionMotion(attack, target.transform);

                movement.TickActionMotion(1f, 1f);
                return boss.transform.position;
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline, profile);
            }
        }

        private static BossAttackMotionConfig CreateReactionMotionConfig(string actionId, string windowName)
        {
            return new BossAttackMotionConfig(
                actionId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                new[]
                {
                    BossCodeMoveWindow.CurveBack(
                        windowName,
                        0f,
                        0.75f,
                        0.6f)
                });
        }

        private static BossAttackMotionConfig CreateRootMotionConfig(string attackId)
        {
            return new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.RootMotionWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                null);
        }

        private static void AssignMotionProfile(BossMotionController motion, BossMotionWarpProfile profile)
        {
            typeof(BossMotionController)
                .GetField("motionWarpProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(motion, profile);
        }

        private static void DestroyTestObjects(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
        }
    }
}
