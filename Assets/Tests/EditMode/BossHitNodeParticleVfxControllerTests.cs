// 文件说明：验证 Boss HitNode 人工一次性粒子绑定、多效果叠加和逐绑定防重复规则。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅验证 Boss 出手粒子的人工配置选择。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Combat.Timeline;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossHitNodeParticleVfxControllerTests
    {
        [Test]
        public void TryPlayForHitNode_TwoBindingsForSameHitNodeSpawnIndependentlyOnce()
        {
            GameObject root = new GameObject("BossHitNodeParticleVfxController_Test");
            GameObject firstEffect = CreateParticleSource("FirstEffect");
            GameObject secondEffect = CreateParticleSource("SecondEffect");
            GameObject firstAnchor = new GameObject("FirstAnchor");
            GameObject secondAnchor = new GameObject("SecondAnchor");
            BossHitNodeParticleVfxController controller = root.AddComponent<BossHitNodeParticleVfxController>();
            BossAttackTimelineAsset timeline = CreateTimeline("Test_Attack", "Hit_A");
            try
            {
                controller.BindEffectForHitNode(
                    timeline,
                    "Hit_A",
                    firstEffect,
                    firstAnchor.transform,
                    BossHitNodeParticleDirectionMode.BossHorizontalForward,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    2.2f);
                controller.BindEffectForHitNode(
                    timeline,
                    "Hit_A",
                    secondEffect,
                    secondAnchor.transform,
                    BossHitNodeParticleDirectionMode.AnchorForward,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    2.2f);

                bool firstPlay = controller.TryPlayForHitNode("Test_Attack", "Hit_A");
                bool repeatedPlay = controller.TryPlayForHitNode("Test_Attack", "Hit_A");

                Assert.That(firstPlay, Is.True);
                Assert.That(repeatedPlay, Is.False);
                Assert.That(GameObject.Find("FirstEffect_Runtime"), Is.Not.Null);
                Assert.That(GameObject.Find("SecondEffect_Runtime"), Is.Not.Null);
            }
            finally
            {
                DestroyRuntimeInstance("FirstEffect_Runtime");
                DestroyRuntimeInstance("SecondEffect_Runtime");
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(firstAnchor);
                Object.DestroyImmediate(secondAnchor);
                Object.DestroyImmediate(firstEffect);
                Object.DestroyImmediate(secondEffect);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryPlayForHitNode_UnconfiguredNodeDoesNotUseHardcodedDefault()
        {
            GameObject root = new GameObject("BossHitNodeParticleVfxController_Test");
            BossHitNodeParticleVfxController controller = root.AddComponent<BossHitNodeParticleVfxController>();
            try
            {
                bool played = controller.TryPlayForHitNode("Raven_ChaseCombo", "ChaseSlash_4");

                Assert.That(played, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ResetTriggerState_AllowsEveryBindingToPlayInNextAttack()
        {
            GameObject root = new GameObject("BossHitNodeParticleVfxController_Test");
            GameObject effect = CreateParticleSource("ResetEffect");
            GameObject anchor = new GameObject("ResetAnchor");
            BossHitNodeParticleVfxController controller = root.AddComponent<BossHitNodeParticleVfxController>();
            BossAttackTimelineAsset timeline = CreateTimeline("Test_Attack", "Hit_A");
            try
            {
                controller.BindEffectForHitNode(
                    timeline,
                    "Hit_A",
                    effect,
                    anchor.transform,
                    BossHitNodeParticleDirectionMode.BossHorizontalForward,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    2.2f);
                controller.TryPlayForHitNode("Test_Attack", "Hit_A");

                controller.ResetTriggerState();
                bool playedAfterReset = controller.TryPlayForHitNode("Test_Attack", "Hit_A");

                Assert.That(playedAfterReset, Is.True);
            }
            finally
            {
                DestroyRuntimeInstance("ResetEffect_Runtime");
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TryPlayForAttackTime_WaitsForLeadThresholdAndBackfillsCrossedFrame()
        {
            GameObject root = new GameObject("BossHitNodeParticleVfxController_LeadTimeTest");
            GameObject effect = CreateParticleSource("LeadTimeEffect");
            GameObject anchor = new GameObject("LeadTimeAnchor");
            BossHitNodeParticleVfxController controller = root.AddComponent<BossHitNodeParticleVfxController>();
            BossAttackTimelineAsset timeline = CreateTimeline("Test_LeadTime", "Hit_A");
            try
            {
                BossAttackDefinition attack = timeline.ToBossAttackDefinition();
                controller.BindEffectForHitNode(
                    timeline,
                    "Hit_A",
                    effect,
                    anchor.transform,
                    BossHitNodeParticleDirectionMode.BossHorizontalForward,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    2.2f,
                    0.25f);

                bool beforeThreshold = controller.TryPlayForAttackTime(attack, 0.74f);
                bool afterCrossingThreshold = controller.TryPlayForAttackTime(attack, 0.8f);
                bool repeatedAfterSpawn = controller.TryPlayForAttackTime(attack, 1.2f);

                Assert.That(beforeThreshold, Is.False);
                Assert.That(afterCrossingThreshold, Is.True);
                Assert.That(repeatedAfterSpawn, Is.False);
                Assert.That(GameObject.Find("LeadTimeEffect_Runtime"), Is.Not.Null);
            }
            finally
            {
                DestroyRuntimeInstance("LeadTimeEffect_Runtime");
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(anchor);
                Object.DestroyImmediate(effect);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>
        /// 创建包含单个 ParticleSystem 的临时特效源对象。
        /// </summary>
        /// <param name="name">源对象和运行时实例使用的基础名称。</param>
        /// <returns>可被控制器实例化与播放的粒子源对象。</returns>
        private static GameObject CreateParticleSource(string name)
        {
            GameObject effect = new GameObject(name);
            effect.AddComponent<ParticleSystem>();
            return effect;
        }

        /// <summary>
        /// 删除 EditMode 测试中不会由延时 Destroy 自动清理的运行时粒子实例。
        /// </summary>
        /// <param name="instanceName">控制器生成的完整实例名称。</param>
        private static void DestroyRuntimeInstance(string instanceName)
        {
            GameObject[] objects = Resources.FindObjectsOfTypeAll<GameObject>();
            for (int i = 0; i < objects.Length; i++)
            {
                GameObject instance = objects[i];
                if (instance != null &&
                    instance.scene.IsValid() &&
                    string.Equals(instance.name, instanceName, System.StringComparison.Ordinal))
                {
                    Object.DestroyImmediate(instance);
                }
            }
        }

        /// <summary>
        /// 创建只包含指定 HitNode 的最小 BossAttack Timeline，供人工绑定匹配测试使用。
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
                    StartTime = 1f,
                    EndTime = 1.5f
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
