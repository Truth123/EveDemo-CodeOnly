// 文件说明：验证 Boss 唯一武器按 HitNode 换手、保持本地姿态并在窗口结束或中断时回右手。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅验证 Raven 单武器左右手切换合同。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossWeaponHandControllerTests
    {
        [Test]
        public void TryApplyForAttackFrame_LeftBindingPreservesAuthoredLocalPoseAndReturnsRight()
        {
            GameObject root = new GameObject("BossWeaponHandController_Test");
            BossAttackTimelineAsset timeline = null;
            try
            {
                Transform right = CreateSocket(root.transform, "Right", Vector3.zero);
                Transform left = CreateSocket(root.transform, "Left", new Vector3(8f, 0f, 0f));
                Transform weapon = CreateWeapon(right);
                Vector3 localPosition = weapon.localPosition;
                Quaternion localRotation = weapon.localRotation;
                Vector3 localScale = weapon.localScale;
                Vector3 rightWorldPosition = weapon.position;

                BossWeaponHandController controller = root.AddComponent<BossWeaponHandController>();
                timeline = CreateTimeline("Test_LeftAttack", "LeftHit", BossAttackSourcePart.Weapon);
                controller.ConfigureWeapon(weapon, right, left);
                controller.ConfigureLeftHandHitNodes(new[] { timeline }, new[] { "LeftHit" });
                BossAttackDefinition attack = timeline.ToBossAttackDefinition();

                bool ready = controller.TryApplyForAttackFrame(attack, 0.2f, out bool changedToLeft);

                Assert.That(ready, Is.True);
                Assert.That(changedToLeft, Is.True);
                Assert.That(controller.CurrentHand, Is.EqualTo(BossWeaponHand.Left));
                Assert.That(weapon.parent, Is.SameAs(left));
                Assert.That(weapon.localPosition, Is.EqualTo(localPosition));
                Assert.That(weapon.localRotation, Is.EqualTo(localRotation));
                Assert.That(weapon.localScale, Is.EqualTo(localScale));
                Assert.That(weapon.position, Is.Not.EqualTo(rightWorldPosition));

                controller.TryApplyForAttackFrame(attack, 0.8f, out bool changedToRight);

                Assert.That(changedToRight, Is.True);
                Assert.That(controller.CurrentHand, Is.EqualTo(BossWeaponHand.Right));
                Assert.That(weapon.parent, Is.SameAs(right));
                Assert.That(weapon.localPosition, Is.EqualTo(localPosition));
                Assert.That(weapon.localRotation, Is.EqualTo(localRotation));
                Assert.That(weapon.localScale, Is.EqualTo(localScale));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryApplyForAttackFrame_UnboundWeaponAndDetachedNodesStayOnRight()
        {
            GameObject root = new GameObject("BossWeaponHandController_UnboundTest");
            BossAttackTimelineAsset timeline = null;
            try
            {
                Transform right = CreateSocket(root.transform, "Right", Vector3.zero);
                Transform left = CreateSocket(root.transform, "Left", Vector3.right * 8f);
                Transform weapon = CreateWeapon(right);
                BossWeaponHandController controller = root.AddComponent<BossWeaponHandController>();
                timeline = CreateTimeline(
                    "Test_UnboundAttack",
                    new[] { "WeaponHit", "DetachedHit" },
                    new[] { BossAttackSourcePart.Weapon, BossAttackSourcePart.Detached });
                controller.ConfigureWeapon(weapon, right, left);
                controller.ConfigureLeftHandHitNodes(System.Array.Empty<BossAttackTimelineAsset>(), System.Array.Empty<string>());

                bool ready = controller.TryApplyForAttackFrame(timeline.ToBossAttackDefinition(), 0.2f, out bool changed);

                Assert.That(ready, Is.True);
                Assert.That(changed, Is.False);
                Assert.That(controller.CurrentHand, Is.EqualTo(BossWeaponHand.Right));
                Assert.That(weapon.parent, Is.SameAs(right));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ResetToDefaultHand_InterruptsLeftHandNodeImmediately()
        {
            GameObject root = new GameObject("BossWeaponHandController_ResetTest");
            BossAttackTimelineAsset timeline = null;
            try
            {
                Transform right = CreateSocket(root.transform, "Right", Vector3.zero);
                Transform left = CreateSocket(root.transform, "Left", Vector3.right * 8f);
                Transform weapon = CreateWeapon(right);
                BossWeaponHandController controller = root.AddComponent<BossWeaponHandController>();
                timeline = CreateTimeline("Test_ResetAttack", "LeftHit", BossAttackSourcePart.Weapon);
                controller.ConfigureWeapon(weapon, right, left);
                controller.ConfigureLeftHandHitNodes(new[] { timeline }, new[] { "LeftHit" });
                controller.TryApplyForAttackFrame(timeline.ToBossAttackDefinition(), 0.2f, out _);

                controller.ResetToDefaultHand();

                Assert.That(controller.CurrentHand, Is.EqualTo(BossWeaponHand.Right));
                Assert.That(weapon.parent, Is.SameAs(right));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void IsConfigured_LeftBindingRejectsNonWeaponSourcePart()
        {
            GameObject root = new GameObject("BossWeaponHandController_InvalidBindingTest");
            BossAttackTimelineAsset timeline = null;
            try
            {
                Transform right = CreateSocket(root.transform, "Right", Vector3.zero);
                Transform left = CreateSocket(root.transform, "Left", Vector3.right * 8f);
                Transform weapon = CreateWeapon(right);
                BossWeaponHandController controller = root.AddComponent<BossWeaponHandController>();
                timeline = CreateTimeline("Test_InvalidAttack", "LeftHit", BossAttackSourcePart.LeftHand);
                controller.ConfigureWeapon(weapon, right, left);
                controller.ConfigureLeftHandHitNodes(new[] { timeline }, new[] { "LeftHit" });

                Assert.That(controller.IsConfigured, Is.False);
                Assert.That(
                    controller.TryApplyForAttackFrame(timeline.ToBossAttackDefinition(), 0.2f, out _),
                    Is.False);
                Assert.That(weapon.parent, Is.SameAs(right));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(root);
            }
        }

        private static Transform CreateSocket(Transform root, string name, Vector3 position)
        {
            GameObject socket = new GameObject(name);
            socket.transform.SetParent(root, false);
            socket.transform.position = position;
            return socket.transform;
        }

        private static Transform CreateWeapon(Transform rightSocket)
        {
            GameObject weapon = new GameObject("Weapon");
            weapon.transform.SetParent(rightSocket, false);
            weapon.transform.localPosition = new Vector3(0.25f, -0.1f, 0.5f);
            weapon.transform.localRotation = Quaternion.Euler(90f, 90f, 0f);
            weapon.transform.localScale = new Vector3(1f, 1.2f, 0.8f);
            return weapon.transform;
        }

        private static BossAttackTimelineAsset CreateTimeline(
            string attackId,
            string hitNodeId,
            BossAttackSourcePart sourcePart)
        {
            return CreateTimeline(attackId, new[] { hitNodeId }, new[] { sourcePart });
        }

        private static BossAttackTimelineAsset CreateTimeline(
            string attackId,
            string[] hitNodeIds,
            BossAttackSourcePart[] sourceParts)
        {
            BossAttackTimelineAsset timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossAttack,
                attackId,
                attackId,
                "Anim_" + attackId,
                1f,
                60f);
            timeline.ConfigureBossAttack(180f, 0f, 0f, 0.1f);
            CombatTimelineClip[] clips = new CombatTimelineClip[hitNodeIds.Length];
            for (int i = 0; i < hitNodeIds.Length; i++)
            {
                clips[i] = new CombatHitNodeClip
                {
                    Name = hitNodeIds[i],
                    CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                    StartTime = 0.1f,
                    EndTime = 0.4f,
                    SourcePart = sourceParts[i],
                    MaxHitsPerTarget = 1
                };
            }

            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = clips
                }
            });
            return timeline;
        }
    }
}
