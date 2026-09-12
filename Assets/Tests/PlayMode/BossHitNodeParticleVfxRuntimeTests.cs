// 文件说明：验证 Boss 一次性 HitNode 粒子锁定首帧世界姿态并脱离生成挂点。
// 所属模块：PlayMode 测试。
// 运行影响：不参与正式运行时逻辑，仅回归武器、手部和腿部挂点移动后的特效空间行为。

using NUnit.Framework;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Combat.Timeline;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossHitNodeParticleVfxRuntimeTests
    {
        private readonly List<Object> createdObjects = new List<Object>();

        [TearDown]
        public void TearDown()
        {
            for (int i = createdObjects.Count - 1; i >= 0; i--)
            {
                Object createdObject = createdObjects[i];
                if (createdObject != null)
                {
                    Object.DestroyImmediate(createdObject);
                }
            }

            createdObjects.Clear();
        }

        [Test]
        public void BossHorizontalForward_SpawnedEffectDoesNotFollowAnchorAfterTrigger()
        {
            GameObject boss = Track(new GameObject("BossParticleVfxRuntime_Test"));
            boss.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            BossHitNodeParticleVfxController controller = boss.AddComponent<BossHitNodeParticleVfxController>();
            GameObject anchor = Track(new GameObject("WeaponAnchor_Test"));
            anchor.transform.SetPositionAndRotation(new Vector3(2f, 1f, 3f), Quaternion.Euler(25f, 35f, 15f));
            GameObject effectPrefab = Track(CreateParticleSource("BossForwardEffect"));
            BossAttackTimelineAsset timeline = Track(CreateTimeline("Test_Attack", "Hit_A"));
            controller.BindEffectForHitNode(
                timeline,
                "Hit_A",
                effectPrefab,
                anchor.transform,
                BossHitNodeParticleDirectionMode.BossHorizontalForward,
                Vector3.zero,
                new Vector3(-90f, 0f, 0f),
                Vector3.one * 0.3f,
                5f);

            bool played = controller.TryPlayForHitNode("Test_Attack", "Hit_A");
            GameObject instance = Track(GameObject.Find("BossForwardEffect_Runtime"));
            Assert.That(played, Is.True);
            Assert.That(instance, Is.Not.Null);

            Vector3 spawnedPosition = instance.transform.position;
            Quaternion spawnedRotation = instance.transform.rotation;
            Quaternion expectedRotation =
                Quaternion.LookRotation(Vector3.right, Vector3.up) * Quaternion.Euler(-90f, 0f, 0f);
            Assert.That(Vector3.Distance(spawnedPosition, anchor.transform.position), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(spawnedRotation, expectedRotation), Is.LessThan(0.01f));
            Assert.That(Vector3.Distance(instance.transform.localScale, Vector3.one * 0.3f), Is.LessThan(0.0001f));

            anchor.transform.SetPositionAndRotation(new Vector3(20f, 8f, -10f), Quaternion.Euler(80f, 170f, 35f));

            Assert.That(Vector3.Distance(instance.transform.position, spawnedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(instance.transform.rotation, spawnedRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void AnchorForward_CapturesAnchorRotationOnlyOnTriggerFrame()
        {
            GameObject boss = Track(new GameObject("BossParticleVfxRuntime_Test"));
            BossHitNodeParticleVfxController controller = boss.AddComponent<BossHitNodeParticleVfxController>();
            GameObject anchor = Track(new GameObject("LegAnchor_Test"));
            anchor.transform.SetPositionAndRotation(new Vector3(-1f, 0.5f, 4f), Quaternion.Euler(10f, 70f, 20f));
            GameObject effectPrefab = Track(CreateParticleSource("AnchorForwardEffect"));
            BossAttackTimelineAsset timeline = Track(CreateTimeline("Test_Attack", "Hit_A"));
            controller.BindEffectForHitNode(
                timeline,
                "Hit_A",
                effectPrefab,
                anchor.transform,
                BossHitNodeParticleDirectionMode.AnchorForward,
                new Vector3(0f, -0.338f, 0f),
                new Vector3(-90f, 0f, 0f),
                Vector3.one * 0.3f,
                5f);

            controller.TryPlayForHitNode("Test_Attack", "Hit_A");
            GameObject instance = Track(GameObject.Find("AnchorForwardEffect_Runtime"));
            Assert.That(instance, Is.Not.Null);

            Vector3 expectedPosition = anchor.transform.TransformPoint(new Vector3(0f, -0.338f, 0f));
            Quaternion expectedRotation = anchor.transform.rotation * Quaternion.Euler(-90f, 0f, 0f);
            Assert.That(Vector3.Distance(instance.transform.position, expectedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(instance.transform.rotation, expectedRotation), Is.LessThan(0.01f));

            Vector3 spawnedPosition = instance.transform.position;
            Quaternion spawnedRotation = instance.transform.rotation;
            anchor.transform.SetPositionAndRotation(Vector3.one * 15f, Quaternion.identity);

            Assert.That(Vector3.Distance(instance.transform.position, spawnedPosition), Is.LessThan(0.0001f));
            Assert.That(Quaternion.Angle(instance.transform.rotation, spawnedRotation), Is.LessThan(0.01f));
        }

        [Test]
        public void ConfiguredLifetime_SpawnedInstanceRemainsDetachedFromAnchor()
        {
            GameObject boss = Track(new GameObject("BossParticleVfxRuntime_Test"));
            BossHitNodeParticleVfxController controller = boss.AddComponent<BossHitNodeParticleVfxController>();
            GameObject anchor = Track(new GameObject("LifetimeAnchor_Test"));
            GameObject effectPrefab = Track(CreateParticleSource("LifetimeEffect"));
            BossAttackTimelineAsset timeline = Track(CreateTimeline("Test_Attack", "Hit_A"));
            controller.BindEffectForHitNode(
                timeline,
                "Hit_A",
                effectPrefab,
                anchor.transform,
                BossHitNodeParticleDirectionMode.BossHorizontalForward,
                Vector3.zero,
                Vector3.zero,
                Vector3.one,
                0.05f);

            controller.TryPlayForHitNode("Test_Attack", "Hit_A");
            GameObject instance = GameObject.Find("LifetimeEffect_Runtime");
            Assert.That(instance, Is.Not.Null);
            Assert.That(instance.transform.parent, Is.Null);
        }

        /// <summary>
        /// 把测试创建对象加入统一清理列表并原样返回。
        /// </summary>
        /// <typeparam name="T">需要在测试结束时销毁的 UnityEngine.Object 类型。</typeparam>
        /// <param name="createdObject">当前测试刚创建的对象。</param>
        /// <returns>与输入相同的对象，便于创建与赋值写在同一表达式。</returns>
        private T Track<T>(T createdObject) where T : Object
        {
            if (createdObject != null)
            {
                createdObjects.Add(createdObject);
            }

            return createdObject;
        }

        /// <summary>
        /// 创建包含单个循环 ParticleSystem 的临时特效源对象。
        /// </summary>
        /// <param name="name">源对象和运行时实例使用的基础名称。</param>
        /// <returns>可被控制器实例化与播放的粒子源对象。</returns>
        private static GameObject CreateParticleSource(string name)
        {
            GameObject effect = new GameObject(name);
            ParticleSystem particleSystem = effect.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule main = particleSystem.main;
            main.loop = true;
            return effect;
        }

        /// <summary>
        /// 创建只包含指定 HitNode 的最小 BossAttack Timeline，供世界空间粒子测试使用。
        /// </summary>
        /// <param name="attackId">测试 Timeline 的 ActionId。</param>
        /// <param name="hitNodeIds">按作者顺序写入 Timeline 的 HitNode ID。</param>
        /// <returns>包含一个 HitNode Track 的临时 BossAttack Timeline。</returns>
        private static BossAttackTimelineAsset CreateTimeline(string attackId, params string[] hitNodeIds)
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

            CombatTimelineClip[] clips = new CombatTimelineClip[hitNodeIds.Length];
            for (int i = 0; i < hitNodeIds.Length; i++)
            {
                clips[i] = new CombatHitNodeClip
                {
                    Name = hitNodeIds[i],
                    CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                    StartTime = 0f,
                    EndTime = 0.5f
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
