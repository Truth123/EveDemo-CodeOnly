// 文件说明：验证 SlashCombo 红光提示资产、动画时序与红黄玩法边界。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时逻辑，仅校验红光 Prefab、动画事件和既有 HitNode 契约。

using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Combat.Timeline;
using UnityEditor;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossRedFrenzyCueAssetTests
    {
        private const string PrefabPath =
            "Assets/Prefabs/Boss/FX_Boss_RedFrenzyCue.prefab";
        private const string AcceleratedPrefabPath =
            "Assets/Prefabs/Boss/FX_Boss_RedFrenzyCue_053s.prefab";
        private const string ClipPath =
            "Assets/Animator/RavenAuthored/M_Raven_SlashCombo.anim";
        private const string TimelinePath =
            "Assets/ScriptableObjects/CombatTimelines/Boss/Raven/Raven_SlashCombo.asset";

        [Test]
        public void Prefab_UsesFourTimedGroupsAndInwardForceField()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);

            Assert.That(prefab, Is.Not.Null);
            ParticleSystem[] particles = prefab.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(prefab.transform.childCount, Is.EqualTo(4));
            Assert.That(prefab.transform.Find("ShardField"), Is.Not.Null);
            Assert.That(prefab.transform.Find("TrailConvergence"), Is.Not.Null);
            Assert.That(prefab.transform.Find("CoreChargePulse"), Is.Not.Null);
            Assert.That(prefab.transform.Find("RedSignature"), Is.Not.Null);
            Assert.That(prefab.GetComponent<ParticleSystemForceField>(), Is.Not.Null);
            Assert.That(particles.Count(system => system.trails.enabled), Is.EqualTo(6));
            Assert.That(particles.All(system => !system.main.loop), Is.True);
            Assert.That(particles.All(system => !system.main.playOnAwake), Is.True);

            ParticleSystem[] shardSystems = prefab.transform
                .Find("ShardField")
                .GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(shardSystems, Has.Length.EqualTo(5));
            foreach (ParticleSystem shardSystem in shardSystems)
            {
                ParticleSystem.TextureSheetAnimationModule sheet =
                    shardSystem.textureSheetAnimation;
                Assert.That(sheet.enabled, Is.True,
                    $"{shardSystem.name} 必须启用图集切帧，不能把完整 4×4 图集显示在单颗粒子上。");
                Assert.That(sheet.numTilesX, Is.EqualTo(4));
                Assert.That(sheet.numTilesY, Is.EqualTo(4));
                Assert.That(sheet.frameOverTime.constant, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(sheet.startFrame.mode,
                    Is.EqualTo(ParticleSystemCurveMode.TwoConstants));
                Assert.That(sheet.startFrame.constantMin, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(sheet.startFrame.constantMax, Is.EqualTo(0.999f).Within(0.0001f));
            }

            Transform shardField = prefab.transform.Find("ShardField");
            Transform sharedForceFieldTransform =
                shardField.Find("ConvergenceForceField");
            Assert.That(sharedForceFieldTransform, Is.Not.Null);
            Assert.That(shardField.Find("SideWingLeft/ConvergenceForceField"), Is.Null);

            ParticleSystemForceField sharedForceField =
                sharedForceFieldTransform.GetComponent<ParticleSystemForceField>();
            Assert.That(sharedForceField, Is.Not.Null);
            Assert.That(sharedForceField.gravity.constantMax,
                Is.EqualTo(-3f).Within(0.0001f));
            Assert.That(sharedForceField.drag.constantMax,
                Is.EqualTo(10f).Within(0.0001f));
            Assert.That(sharedForceField.startRange,
                Is.EqualTo(0.08f).Within(0.0001f));
            Assert.That(sharedForceField.endRange,
                Is.EqualTo(3f).Within(0.0001f));

            string[] shardNames =
            {
                "SideWingLeft",
                "SideWingRight",
                "UpperCrown",
                "LowerScatter",
                "AccentFragments"
            };
            int[] burstMaximums = { 30, 30, 30, 30, 20 };
            Vector2[] startSpeedRanges =
            {
                new Vector2(3.128f, 4.08f),
                new Vector2(3.196f, 4.148f),
                new Vector2(2.72f, 3.672f),
                new Vector2(2.04f, 2.992f),
                new Vector2(2.108f, 3.332f)
            };
            float[] forceMultipliers = { 1f, 1.12f, 1f, 0.78f, 0.8f };

            for (int systemIndex = 0; systemIndex < shardNames.Length; systemIndex++)
            {
                ParticleSystem shardSystem = shardField
                    .Find(shardNames[systemIndex])
                    .GetComponent<ParticleSystem>();
                ParticleSystem.MainModule main = shardSystem.main;
                Assert.That(main.startDelay.constantMax,
                    Is.EqualTo(0f).Within(0.0001f));
                Assert.That(main.startLifetime.constantMin,
                    Is.EqualTo(0.94f).Within(0.0001f));
                Assert.That(main.startLifetime.constantMax,
                    Is.EqualTo(1f).Within(0.0001f));
                Assert.That(main.startSpeed.constantMin,
                    Is.EqualTo(startSpeedRanges[systemIndex].x).Within(0.0001f));
                Assert.That(main.startSpeed.constantMax,
                    Is.EqualTo(startSpeedRanges[systemIndex].y).Within(0.0001f));

                ParticleSystem.EmissionModule emission = shardSystem.emission;
                var bursts = new ParticleSystem.Burst[emission.burstCount];
                emission.GetBursts(bursts);
                Assert.That(bursts, Has.Length.EqualTo(1));
                Assert.That(bursts[0].count.constantMin, Is.EqualTo(0f));
                Assert.That(bursts[0].count.constantMax,
                    Is.EqualTo(burstMaximums[systemIndex]));

                ParticleSystem.SizeOverLifetimeModule size =
                    shardSystem.sizeOverLifetime;
                Assert.That(size.enabled, Is.True);
                Assert.That(size.size.curve.Evaluate(0f),
                    Is.EqualTo(0.70f).Within(0.001f));
                Assert.That(size.size.curve.Evaluate(0.16f),
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(size.size.curve.Evaluate(0.58f),
                    Is.EqualTo(0.92f).Within(0.001f));
                Assert.That(size.size.curve.Evaluate(0.78f),
                    Is.EqualTo(0.72f).Within(0.001f));
                Assert.That(size.size.curve.Evaluate(0.92f),
                    Is.EqualTo(0.36f).Within(0.001f));
                Assert.That(size.size.curve.Evaluate(1f),
                    Is.EqualTo(0.08f).Within(0.001f));

                ParticleSystem.ExternalForcesModule external =
                    shardSystem.externalForces;
                Assert.That(external.enabled, Is.True);
                Assert.That(external.influenceFilter,
                    Is.EqualTo(ParticleSystemGameObjectFilter.List));
                Assert.That(external.influenceCount, Is.EqualTo(1));
                Assert.That(external.GetInfluence(0), Is.SameAs(sharedForceField));
                Assert.That(external.multiplier,
                    Is.EqualTo(forceMultipliers[systemIndex]).Within(0.0001f));
                Assert.That(external.multiplierCurve.curve.Evaluate(0.30f),
                    Is.EqualTo(0f).Within(0.001f));
                Assert.That(external.multiplierCurve.curve.Evaluate(0.40f),
                    Is.EqualTo(0.25f).Within(0.001f));
                Assert.That(external.multiplierCurve.curve.Evaluate(0.55f),
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(external.multiplierCurve.curve.Evaluate(0.82f),
                    Is.EqualTo(1f).Within(0.001f));
                Assert.That(external.multiplierCurve.curve.Evaluate(1f),
                    Is.EqualTo(0.2f).Within(0.001f));

                float orbitalScale =
                    shardNames[systemIndex] == "AccentFragments" ? 0.25f : 1f;
                ParticleSystem.VelocityOverLifetimeModule velocity =
                    shardSystem.velocityOverLifetime;
                Assert.That(velocity.enabled, Is.True);
                Assert.That(velocity.space,
                    Is.EqualTo(ParticleSystemSimulationSpace.Local));
                Assert.That(velocity.radial.constant, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(velocity.orbitalX.constantMin,
                    Is.EqualTo(-1.76f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalX.constantMax,
                    Is.EqualTo(1.76f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalY.constantMin,
                    Is.EqualTo(-1.12f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalY.constantMax,
                    Is.EqualTo(1.12f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalZ.constantMin,
                    Is.EqualTo(-8f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalZ.constantMax,
                    Is.EqualTo(8f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetX.constantMin,
                    Is.EqualTo(-0.12f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetX.constantMax,
                    Is.EqualTo(0.12f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetY.constantMin,
                    Is.EqualTo(-0.09f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetY.constantMax,
                    Is.EqualTo(0.09f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetZ.constantMin,
                    Is.EqualTo(-0.07f * orbitalScale).Within(0.0001f));
                Assert.That(velocity.orbitalOffsetZ.constantMax,
                    Is.EqualTo(0.07f * orbitalScale).Within(0.0001f));

                ParticleSystem.LimitVelocityOverLifetimeModule limit =
                    shardSystem.limitVelocityOverLifetime;
                Assert.That(limit.enabled, Is.True);
                Assert.That(limit.dampen, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(limit.limit.curve.Evaluate(0f),
                    Is.EqualTo(6f).Within(0.001f));
                Assert.That(limit.limit.curve.Evaluate(0.45f),
                    Is.EqualTo(5f).Within(0.001f));
                Assert.That(limit.limit.curve.Evaluate(0.58f),
                    Is.EqualTo(3f).Within(0.001f));
                Assert.That(limit.limit.curve.Evaluate(0.68f),
                    Is.EqualTo(1.2f).Within(0.001f));
                Assert.That(limit.limit.curve.Evaluate(0.78f),
                    Is.EqualTo(0.45f).Within(0.001f));
                Assert.That(limit.limit.curve.Evaluate(1f),
                    Is.EqualTo(0.15f).Within(0.001f));
            }

            float maxVisibleDuration = particles.Max(system =>
                system.main.startDelay.constantMax + system.main.startLifetime.constantMax);
            Assert.That(maxVisibleDuration, Is.LessThanOrEqualTo(1.0001f));

            Material[] materials = particles
                .Select(system => system.GetComponent<ParticleSystemRenderer>().sharedMaterial)
                .Where(material => material != null)
                .Distinct()
                .ToArray();
            Assert.That(materials, Is.Not.Empty);
            Assert.That(materials.All(material =>
                    AssetDatabase.GetAssetPath(material).StartsWith(
                        "Assets/Materials/Raven/RedFrenzy/",
                        StringComparison.Ordinal)),
                Is.True);
        }

        [Test]
        public void Prefab_FixedSeedSamplesMatchExpansionConvergenceAndPeakOverlap()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.SetActive(true);
                Transform shardField = instance.transform.Find("ShardField");
                Transform trailConvergence = instance.transform.Find("TrailConvergence");
                Transform coreChargePulse = instance.transform.Find("CoreChargePulse");

                ParticleStageSample early = SampleStage(instance, shardField, trailConvergence, coreChargePulse, 0.15f);
                ParticleStageSample expanded = SampleStage(instance, shardField, trailConvergence, coreChargePulse, 0.35f);
                ParticleStageSample turning = SampleStage(instance, shardField, trailConvergence, coreChargePulse, 0.59f);
                ParticleStageSample peak = SampleStage(instance, shardField, trailConvergence, coreChargePulse, 0.76f);
                ParticleStageSample finished = SampleStage(instance, shardField, trailConvergence, coreChargePulse, 1.01f);

                Assert.That(early.LeftCount, Is.GreaterThan(0));
                Assert.That(early.RightCount, Is.GreaterThan(0));
                Assert.That(early.UpperCount, Is.GreaterThan(0));
                Assert.That(early.LowerCount, Is.GreaterThan(0));
                Assert.That(early.CoreParticleCount, Is.GreaterThan(0));
                Assert.That(expanded.AverageShardRadius, Is.GreaterThan(early.AverageShardRadius));
                Assert.That(turning.AverageShardRadius, Is.LessThan(expanded.AverageShardRadius * 1.2f));
                Assert.That(peak.AverageShardRadius, Is.LessThan(turning.AverageShardRadius));
                Assert.That(peak.TrailHeadCount, Is.EqualTo(6));
                Assert.That(peak.CoreParticleCount, Is.GreaterThan(early.CoreParticleCount));
                Assert.That(finished.TotalParticleCount, Is.EqualTo(0));

                float[] beforeTurn = SampleShardRadii(instance, shardField, 0.65f);
                float[] afterTurn = SampleShardRadii(instance, shardField, 0.70f);
                Assert.That(afterTurn, Has.Length.EqualTo(beforeTurn.Length));
                int movingInward = 0;
                for (int i = 0; i < beforeTurn.Length; i++)
                {
                    if (afterTurn[i] < beforeTurn[i])
                    {
                        movingInward++;
                    }
                }

                Assert.That(movingInward, Is.GreaterThan(beforeTurn.Length / 2));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void AcceleratedPrefab_PreservesStructureAndCompletesInPointFiveThreeSeconds()
        {
            GameObject original = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject accelerated = AssetDatabase.LoadAssetAtPath<GameObject>(
                AcceleratedPrefabPath);

            Assert.That(original, Is.Not.Null);
            Assert.That(accelerated, Is.Not.Null);
            ParticleSystem[] originalSystems = original.GetComponentsInChildren<ParticleSystem>(true);
            ParticleSystem[] acceleratedSystems = accelerated.GetComponentsInChildren<ParticleSystem>(true);
            Assert.That(accelerated.transform.childCount, Is.EqualTo(original.transform.childCount));
            Assert.That(acceleratedSystems, Has.Length.EqualTo(originalSystems.Length));

            var originalByPath = originalSystems.ToDictionary(
                system => AnimationUtility.CalculateTransformPath(
                    system.transform,
                    original.transform));
            const float targetDuration = 0.53f;
            float expectedSimulationSpeed = 1f / targetDuration;
            float maximumVisibleDuration = 0f;
            foreach (ParticleSystem acceleratedSystem in acceleratedSystems)
            {
                string relativePath = AnimationUtility.CalculateTransformPath(
                    acceleratedSystem.transform,
                    accelerated.transform);
                Assert.That(originalByPath.TryGetValue(
                    relativePath,
                    out ParticleSystem originalSystem),
                    Is.True,
                    $"加速版本缺少原版粒子节点：{relativePath}");

                ParticleSystem.MainModule originalMain = originalSystem.main;
                ParticleSystem.MainModule acceleratedMain = acceleratedSystem.main;
                Assert.That(acceleratedMain.startDelay.constantMin,
                    Is.EqualTo(originalMain.startDelay.constantMin).Within(0.0001f));
                Assert.That(acceleratedMain.startDelay.constantMax,
                    Is.EqualTo(originalMain.startDelay.constantMax).Within(0.0001f));
                Assert.That(acceleratedMain.startLifetime.constantMin,
                    Is.EqualTo(originalMain.startLifetime.constantMin).Within(0.0001f));
                Assert.That(acceleratedMain.startLifetime.constantMax,
                    Is.EqualTo(originalMain.startLifetime.constantMax).Within(0.0001f));
                Assert.That(acceleratedMain.duration,
                    Is.EqualTo(originalMain.duration).Within(0.0001f));
                Assert.That(acceleratedMain.simulationSpeed,
                    Is.EqualTo(expectedSimulationSpeed).Within(0.0001f));

                float visibleDuration =
                    (acceleratedMain.startDelay.constantMax +
                        acceleratedMain.startLifetime.constantMax) /
                    acceleratedMain.simulationSpeed;
                maximumVisibleDuration = Mathf.Max(maximumVisibleDuration, visibleDuration);
            }

            Assert.That(maximumVisibleDuration,
                Is.EqualTo(targetDuration).Within(0.0001f));
        }

        [Test]
        public void Prefab_AllShardSystemsReturnAlongVisibleBidirectionalArcs()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.SetActive(true);
                string[] shardNames =
                {
                    "SideWingLeft",
                    "SideWingRight",
                    "UpperCrown",
                    "LowerScatter",
                    "AccentFragments"
                };

                foreach (string shardName in shardNames)
                {
                    ParticleSystem system = instance.transform
                        .Find($"ShardField/{shardName}")
                        .GetComponent<ParticleSystem>();
                    Dictionary<uint, Vector3> atPeak =
                        SampleShardPositions(instance, system, 0.45f);
                    Dictionary<uint, Vector3> atEarlyReturn =
                        SampleShardPositions(instance, system, 0.60f);
                    Dictionary<uint, Vector3> atMidReturn =
                        SampleShardPositions(instance, system, 0.70f);
                    Dictionary<uint, Vector3> atLateReturn =
                        SampleShardPositions(instance, system, 0.80f);

                    float peakRadius = AverageRadius(atPeak);
                    float earlyRadius = AverageRadius(atEarlyReturn);
                    float middleRadius = AverageRadius(atMidReturn);
                    float lateRadius = AverageRadius(atLateReturn);
                    Assert.That(earlyRadius, Is.LessThan(peakRadius * 0.90f),
                        $"{shardName} 在 0.60s 必须已离开扩散峰值并开始回流。");
                    Assert.That(middleRadius, Is.LessThan(earlyRadius * 0.65f),
                        $"{shardName} 在 0.70s 必须继续明显靠近核心。");
                    Assert.That(lateRadius, Is.LessThan(middleRadius * 0.55f),
                        $"{shardName} 在 0.80s 必须进入核心附近的末段回收。");
                    Assert.That(atLateReturn, Is.Not.Empty,
                        $"{shardName} 在 0.80s 仍应可见，不能只靠提前缩小消失。");

                    int positiveTurns = 0;
                    int negativeTurns = 0;
                    float tangentDistance = 0f;
                    int comparedParticles = 0;
                    foreach (KeyValuePair<uint, Vector3> particle in atPeak)
                    {
                        if (!atEarlyReturn.TryGetValue(particle.Key, out Vector3 next))
                        {
                            continue;
                        }

                        Vector3 inward = -particle.Value.normalized;
                        Vector3 displacement = next - particle.Value;
                        Vector3 tangent = displacement -
                            Vector3.Dot(displacement, inward) * inward;
                        tangentDistance += tangent.magnitude;
                        float turnSign = Vector3.Cross(inward, displacement).z;
                        positiveTurns += turnSign > 0.00001f ? 1 : 0;
                        negativeTurns += turnSign < -0.00001f ? 1 : 0;
                        comparedParticles++;
                    }

                    Assert.That(comparedParticles, Is.GreaterThan(0));
                    Assert.That(tangentDistance / comparedParticles,
                        Is.GreaterThan(0.05f),
                        $"{shardName} 必须产生可读的切向位移，不能沿原路径倒放。");
                    Assert.That(positiveTurns, Is.GreaterThan(0),
                        $"{shardName} 必须包含一个方向的弧线回流。");
                    Assert.That(negativeTurns, Is.GreaterThan(0),
                        $"{shardName} 必须同时包含反方向弧线，避免整齐同步。");
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void Prefab_TrailsUseEvenUpperFanAndCommonCoreEndpoint()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Transform trailGroup = prefab.transform.Find("TrailConvergence");
            Assert.That(trailGroup, Is.Not.Null);
            Assert.That(trailGroup.childCount, Is.EqualTo(6));

            Vector3[] expectedPositions =
            {
                new Vector3(-1.70f, 1.55f, 0.24f),
                new Vector3(-1.05f, 1.80f, -0.16f),
                new Vector3(-0.38f, 2.05f, 0.08f),
                new Vector3(0.38f, 2.00f, -0.08f),
                new Vector3(1.05f, 1.78f, 0.16f),
                new Vector3(1.70f, 1.53f, -0.24f)
            };
            float previousX = float.NegativeInfinity;
            var noiseStrengths = new float[6];
            GameObject instance = UnityEngine.Object.Instantiate(prefab);
            try
            {
                instance.hideFlags = HideFlags.HideAndDontSave;
                instance.SetActive(true);
                for (int trailIndex = 0; trailIndex < expectedPositions.Length;
                     trailIndex++)
                {
                    string trailName = $"Trail0{trailIndex + 1}";
                    Transform trail = trailGroup.Find(trailName);
                    Assert.That(trail, Is.Not.Null);
                    Assert.That(Vector3.Distance(
                            trail.localPosition,
                            expectedPositions[trailIndex]),
                        Is.LessThan(0.0001f));
                    Assert.That(trail.localPosition.y, Is.GreaterThanOrEqualTo(1.5f),
                        $"{trailName} 必须从核心上方开始，不能再从腿部或下方发射。");
                    Assert.That(trail.localPosition.magnitude, Is.GreaterThan(2f),
                        $"{trailName} 的起点距离必须足以形成参考图中的长弧线。");
                    if (trailIndex > 0)
                    {
                        float horizontalGap = trail.localPosition.x - previousX;
                        Assert.That(horizontalGap, Is.InRange(0.60f, 0.80f),
                            $"{trailName} 与前一轨迹应按确定槽位均匀分散。");
                    }

                    previousX = trail.localPosition.x;
                    ParticleSystem particles = trail.GetComponent<ParticleSystem>();
                    ParticleSystem.MainModule main = particles.main;
                    Assert.That(main.startDelay.constantMax +
                        main.startLifetime.constantMax,
                        Is.EqualTo(0.98f).Within(0.001f));
                    Assert.That(main.startSize.constantMin,
                        Is.EqualTo(0.085f).Within(0.0001f));
                    Assert.That(main.startSize.constantMax,
                        Is.EqualTo(0.13f).Within(0.0001f));

                    ParticleSystem.ShapeModule shape = particles.shape;
                    Assert.That(shape.shapeType,
                        Is.EqualTo(ParticleSystemShapeType.Cone));
                    Assert.That(shape.radius, Is.EqualTo(0.01f).Within(0.0001f));
                    Assert.That(shape.angle, Is.EqualTo(0.75f).Within(0.0001f));
                    Assert.That(particles.externalForces.enabled, Is.False,
                        $"{trailName} 使用校准后的确定路线，不得再被根部随机力场推离核心。");

                    ParticleSystem.NoiseModule noise = particles.noise;
                    Assert.That(noise.enabled, Is.True);
                    Assert.That(noise.octaveCount, Is.EqualTo(1));
                    noiseStrengths[trailIndex] = noise.strength.constantMax;

                    ParticleSystem.TrailModule trails = particles.trails;
                    Assert.That(trails.enabled, Is.True);
                    Assert.That(trails.lifetime.constantMin,
                        Is.EqualTo(0.52f).Within(0.0001f));
                    Assert.That(trails.lifetime.constantMax,
                        Is.EqualTo(0.60f).Within(0.0001f));
                    Assert.That(trails.minVertexDistance,
                        Is.EqualTo(0.055f).Within(0.0001f));
                    Assert.That(trails.dieWithParticles, Is.True);
                    Assert.That(trails.widthOverTrail.curve.Evaluate(0.14f),
                        Is.EqualTo(1.20f).Within(0.001f));

                    ParticleSystem instanceParticles = instance.transform
                        .Find($"TrailConvergence/{trailName}")
                        .GetComponent<ParticleSystem>();
                    float endpointSampleTime =
                        instanceParticles.main.startDelay.constantMax +
                        instanceParticles.main.startLifetime.constantMax - 0.012f;
                    instanceParticles.Stop(true,
                        ParticleSystemStopBehavior.StopEmittingAndClear);
                    instanceParticles.Simulate(endpointSampleTime, false, true, true);
                    var samples = new ParticleSystem.Particle[
                        instanceParticles.particleCount];
                    int count = instanceParticles.GetParticles(samples);
                    Assert.That(count, Is.EqualTo(1));
                    Assert.That((samples[0].position - instance.transform.position).magnitude,
                        Is.LessThan(0.02f),
                        $"{trailName} 的末端必须汇入统一的胸口核心点。");
                }

                Assert.That(noiseStrengths.Distinct().Count(), Is.GreaterThan(3),
                    "六条轨迹应保留不同 Noise 强度，但起点不再完全随机。");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        [Test]
        public void SlashCombo_HasSingleRedCueEventAndHairOnlyRedCurve()
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipPath);

            Assert.That(clip, Is.Not.Null);
            AnimationEvent[] redEvents = AnimationUtility.GetAnimationEvents(clip)
                .Where(animationEvent =>
                    animationEvent.functionName == "OnRedAttackParticleStart")
                .ToArray();
            Assert.That(redEvents, Has.Length.EqualTo(1));
            Assert.That(redEvents[0].time, Is.EqualTo(0f).Within(0.0001f));

            AnimationCurve bodyAlphaCurve = AnimationUtility.GetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(BossBodyColorController),
                    "bodyColor.a"));
            AnimationCurve hairRedCurve = AnimationUtility.GetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(BossBodyColorController),
                    "hairColor.r"));
            AnimationCurve hairAlphaCurve = AnimationUtility.GetEditorCurve(
                clip,
                EditorCurveBinding.FloatCurve(
                    string.Empty,
                    typeof(BossBodyColorController),
                    "hairColor.a"));
            Assert.That(bodyAlphaCurve.Evaluate(0.125f), Is.EqualTo(0f).Within(0.001f));
            Assert.That(hairRedCurve.Evaluate(0.125f), Is.EqualTo(2.2f).Within(0.001f));
            Assert.That(hairAlphaCurve.Evaluate(0.125f), Is.EqualTo(0.55f).Within(0.001f));
            Assert.That(hairAlphaCurve.Evaluate(2.125f), Is.GreaterThan(0f));
            Assert.That(hairAlphaCurve.Evaluate(2.625f), Is.EqualTo(0f).Within(0.001f));
        }

        [Test]
        public void SlashCombo_FirstFourHitsStayGuardableAndYellowIaiStaysUnblockable()
        {
            BossAttackTimelineAsset timeline =
                AssetDatabase.LoadAssetAtPath<BossAttackTimelineAsset>(TimelinePath);

            Assert.That(timeline, Is.Not.Null);
            var hitNodes = timeline.ToBossAttackDefinition().HitNodes;
            Assert.That(hitNodes, Has.Length.EqualTo(5));
            for (int i = 0; i < 4; i++)
            {
                Assert.That(hitNodes[i].CanBeGuarded, Is.True);
                Assert.That(hitNodes[i].CanBePerfectGuarded, Is.True);
            }

            Assert.That(hitNodes[4].HitNodeId, Is.EqualTo("SlashCombo_YellowIai"));
            Assert.That(hitNodes[4].CanBeGuarded, Is.False);
            Assert.That(hitNodes[4].CanBePerfectGuarded, Is.False);
            Assert.That(hitNodes[4].CanBePerfectEvaded, Is.True);
        }

        /// <summary>以固定随机种子重建指定时间点，并汇总四向碎片、轨迹和核心粒子的阶段数据。</summary>
        /// <param name="root">临时实例化的红光提示根对象。</param>
        /// <param name="shardField">包含四向碎片系统的表现组。</param>
        /// <param name="trailConvergence">包含六条汇聚轨迹的表现组。</param>
        /// <param name="coreChargePulse">包含持续核心与峰值闪光的表现组。</param>
        /// <param name="time">相对红光事件的采样时间，单位为秒。</param>
        /// <returns>该时间点的方向覆盖、平均半径和存活粒子数量。</returns>
        private static ParticleStageSample SampleStage(
            GameObject root,
            Transform shardField,
            Transform trailConvergence,
            Transform coreChargePulse,
            float time)
        {
            SimulateAt(root, time);
            ParticleSystem[] shardSystems =
                shardField.GetComponentsInChildren<ParticleSystem>(true);
            int shardCount = 0;
            int leftCount = 0;
            int rightCount = 0;
            int upperCount = 0;
            int lowerCount = 0;
            float radiusSum = 0f;
            for (int systemIndex = 0; systemIndex < shardSystems.Length; systemIndex++)
            {
                ParticleSystem system = shardSystems[systemIndex];
                var particles = new ParticleSystem.Particle[system.particleCount];
                int count = system.GetParticles(particles);
                for (int particleIndex = 0; particleIndex < count; particleIndex++)
                {
                    Vector3 position = particles[particleIndex].position - root.transform.position;
                    float radius = position.magnitude;
                    shardCount++;
                    radiusSum += radius;
                    leftCount += position.x < -0.08f ? 1 : 0;
                    rightCount += position.x > 0.08f ? 1 : 0;
                    upperCount += position.y > 0.08f ? 1 : 0;
                    lowerCount += position.y < -0.08f ? 1 : 0;
                }
            }

            int trailHeadCount = trailConvergence
                .GetComponentsInChildren<ParticleSystem>(true)
                .Sum(system => system.particleCount);
            int coreParticleCount = coreChargePulse
                .GetComponentsInChildren<ParticleSystem>(true)
                .Sum(system => system.particleCount);
            int totalParticleCount = root
                .GetComponentsInChildren<ParticleSystem>(true)
                .Sum(system => system.particleCount);
            return new ParticleStageSample(
                shardCount > 0 ? radiusSum / shardCount : 0f,
                leftCount,
                rightCount,
                upperCount,
                lowerCount,
                trailHeadCount,
                coreParticleCount,
                totalParticleCount);
        }

        /// <summary>重建指定时间点并按粒子系统稳定顺序返回全部碎片到核心的距离。</summary>
        /// <param name="root">临时实例化的红光提示根对象。</param>
        /// <param name="shardField">包含固定随机种子的碎片表现组。</param>
        /// <param name="time">相对红光事件的采样时间，单位为秒。</param>
        /// <returns>按系统和粒子索引排列的径向距离数组，单位为米。</returns>
        private static float[] SampleShardRadii(
            GameObject root,
            Transform shardField,
            float time)
        {
            SimulateAt(root, time);
            return shardField
                .GetComponentsInChildren<ParticleSystem>(true)
                .SelectMany(system =>
                {
                    var particles = new ParticleSystem.Particle[system.particleCount];
                    int count = system.GetParticles(particles);
                    return particles
                        .Take(count)
                        .Select(particle =>
                            (particle.position - root.transform.position).magnitude);
                })
                .ToArray();
        }

        /// <summary>以固定随机种子重建一个碎片系统，并按粒子随机种子记录其世界空间位置。</summary>
        /// <param name="root">临时实例化的红光提示根对象。</param>
        /// <param name="system">需要采样的单个碎片粒子系统。</param>
        /// <param name="time">相对红光事件的采样时间，单位为秒。</param>
        /// <returns>以粒子随机种子为键、相对特效根节点位置为值的采样结果。</returns>
        private static Dictionary<uint, Vector3> SampleShardPositions(
            GameObject root,
            ParticleSystem system,
            float time)
        {
            SimulateAt(root, time);
            var particles = new ParticleSystem.Particle[system.particleCount];
            int count = system.GetParticles(particles);
            var result = new Dictionary<uint, Vector3>(count);
            for (int particleIndex = 0; particleIndex < count; particleIndex++)
            {
                result[particles[particleIndex].randomSeed] =
                    particles[particleIndex].position - root.transform.position;
            }

            return result;
        }

        /// <summary>计算一次碎片采样中所有粒子到核心的平均距离。</summary>
        /// <param name="positions">按随机种子记录的粒子相对位置。</param>
        /// <returns>所有粒子到特效根节点的平均距离，单位为米；无粒子时返回零。</returns>
        private static float AverageRadius(Dictionary<uint, Vector3> positions)
        {
            return positions.Count > 0
                ? positions.Values.Average(position => position.magnitude)
                : 0f;
        }

        /// <summary>清空全部粒子后从零推进到指定时间，避免前一采样污染当前结果。</summary>
        /// <param name="root">临时红光提示实例。</param>
        /// <param name="time">需要重建的相对播放时间，单位为秒。</param>
        private static void SimulateAt(GameObject root, float time)
        {
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i].Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                systems[i].Simulate(time, false, true, true);
            }
        }

        private readonly struct ParticleStageSample
        {
            public ParticleStageSample(
                float averageShardRadius,
                int leftCount,
                int rightCount,
                int upperCount,
                int lowerCount,
                int trailHeadCount,
                int coreParticleCount,
                int totalParticleCount)
            {
                AverageShardRadius = averageShardRadius;
                LeftCount = leftCount;
                RightCount = rightCount;
                UpperCount = upperCount;
                LowerCount = lowerCount;
                TrailHeadCount = trailHeadCount;
                CoreParticleCount = coreParticleCount;
                TotalParticleCount = totalParticleCount;
            }

            public float AverageShardRadius { get; }
            public int LeftCount { get; }
            public int RightCount { get; }
            public int UpperCount { get; }
            public int LowerCount { get; }
            public int TrailHeadCount { get; }
            public int CoreParticleCount { get; }
            public int TotalParticleCount { get; }
        }
    }
}
