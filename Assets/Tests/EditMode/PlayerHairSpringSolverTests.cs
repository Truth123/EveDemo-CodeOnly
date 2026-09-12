// 文件说明：验证 Eve 轻量发链的重力、结构约束、软去穿透和可编辑动画曲线契约。
// 所属模块：EditMode 测试。
// 运行影响：不参与运行时，仅验证玩家头发表现的数学与资产边界。

using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using ProjectEVE.Player.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class PlayerHairSpringSolverTests
    {
        private const int PointCount = 9;
        private const string ControllerPath = "Assets/Animator/Eve.controller";

        [Test]
        public void Evaluate_HorizontalChainSagsAndPreservesSegmentLengths()
        {
            PlayerHairSpringSolver solver = CreateSolver(gravityScale: 0.8f, bendDegrees: 85f);
            Vector3[] raw = CreateStraightChain(Vector3.zero, Vector3.right, 0.1f);
            Vector3[] output = new Vector3[PointCount];
            PlayerHairCollisionCapsule[] noColliders = new PlayerHairCollisionCapsule[0];
            solver.Reset(raw, noColliders, 0);

            for (int i = 0; i < 45; i++)
            {
                solver.Evaluate(
                    raw,
                    Vector3.zero,
                    Quaternion.identity,
                    Vector3.zero,
                    Quaternion.identity,
                    1f / 60f,
                    1f,
                    Physics.gravity,
                    noColliders,
                    0,
                    noColliders,
                    0,
                    output);
            }

            Assert.That(output[PointCount - 1].y, Is.LessThan(raw[PointCount - 1].y - 0.005f));
            Assert.That(Vector3.Distance(output[0], raw[0]), Is.LessThan(0.00001f));
            for (int i = 0; i < PointCount - 1; i++)
            {
                Assert.That(Vector3.Distance(output[i], output[i + 1]), Is.EqualTo(0.1f).Within(0.001f));
            }
        }

        [Test]
        public void Reset_EmbeddedAnimationBuildsSafeChainWithoutVelocity()
        {
            PlayerHairSpringSolver solver = CreateSolver(gravityScale: 0f, bendDegrees: 180f);
            Vector3[] raw = CreateStraightChain(new Vector3(-0.4f, 0f, 0f), Vector3.right, 0.1f);
            PlayerHairCollisionCapsule[] colliders =
            {
                new PlayerHairCollisionCapsule(Vector3.zero, Vector3.zero, 0.15f)
            };

            solver.Reset(raw, colliders, colliders.Length);
            Vector3[] output = new Vector3[PointCount];
            solver.Evaluate(
                raw,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Quaternion.identity,
                0f,
                0f,
                Vector3.zero,
                colliders,
                colliders.Length,
                colliders,
                colliders.Length,
                output);

            for (int i = 0; i < PointCount - 1; i++)
            {
                float distance = DistancePointToSegment(Vector3.zero, output[i], output[i + 1]);
                Assert.That(distance, Is.GreaterThanOrEqualTo(0.15f + 0.02f - 0.003f));
            }

            for (int i = 0; i < PointCount; i++)
            {
                Assert.That(solver.GetVelocity(i).sqrMagnitude, Is.LessThan(0.0000001f));
            }
        }

        [Test]
        public void Evaluate_ZeroStateWeightReturnsSafeAnimationPoseAndClearsVelocity()
        {
            PlayerHairSpringSolver solver = CreateSolver(gravityScale: 0.8f, bendDegrees: 85f);
            Vector3[] raw = CreateStraightChain(Vector3.zero, Vector3.right, 0.1f);
            Vector3[] output = new Vector3[PointCount];
            PlayerHairCollisionCapsule[] noColliders = new PlayerHairCollisionCapsule[0];
            solver.Reset(raw, noColliders, 0);
            solver.Evaluate(
                raw,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Quaternion.identity,
                1f / 30f,
                1f,
                Physics.gravity,
                noColliders,
                0,
                noColliders,
                0,
                output);

            solver.Evaluate(
                raw,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Quaternion.identity,
                1f / 60f,
                0f,
                Physics.gravity,
                noColliders,
                0,
                noColliders,
                0,
                output);

            for (int i = 0; i < PointCount; i++)
            {
                Assert.That(Vector3.Distance(output[i], raw[i]), Is.LessThan(0.00001f));
                Assert.That(solver.GetVelocity(i).sqrMagnitude, Is.LessThan(0.0000001f));
            }
        }

        [Test]
        public void Evaluate_UsesCurrentAnimatedSegmentLengthsInsteadOfStartupLengths()
        {
            PlayerHairSpringSolver solver = CreateSolver(gravityScale: 0f, bendDegrees: 180f);
            Vector3[] raw = CreateStraightChain(Vector3.zero, Vector3.down, 0.1f);
            raw[2] = raw[1] + new Vector3(0.08f, -0.28f, 0.04f);
            for (int i = 3; i < raw.Length; i++)
            {
                raw[i] = raw[i - 1] + Vector3.down * 0.1f;
            }

            Vector3[] output = new Vector3[PointCount];
            PlayerHairCollisionCapsule[] noColliders = new PlayerHairCollisionCapsule[0];
            solver.Reset(raw, noColliders, 0);
            solver.Evaluate(
                raw,
                Vector3.zero,
                Quaternion.identity,
                Vector3.zero,
                Quaternion.identity,
                0f,
                0f,
                Vector3.zero,
                noColliders,
                0,
                noColliders,
                0,
                output);

            for (int i = 0; i < PointCount - 1; i++)
            {
                float animatedLength = Vector3.Distance(raw[i], raw[i + 1]);
                float outputLength = Vector3.Distance(output[i], output[i + 1]);
                Assert.That(outputLength, Is.EqualTo(animatedLength).Within(0.0001f));
            }
        }

        [Test]
        public void ResolveContactNormal_LowerBodyContactCannotLiftHair()
        {
            Vector3 radialNormal = new Vector3(0.35f, 0.80f, -0.48f).normalized;

            Vector3 unrestricted = PlayerHairSpringSolver.ResolveContactNormal(radialNormal, false);
            Vector3 slideNormal = PlayerHairSpringSolver.ResolveContactNormal(radialNormal, true);

            Assert.That(Vector3.Distance(unrestricted, radialNormal), Is.LessThan(0.00001f));
            Assert.That(slideNormal.y, Is.LessThanOrEqualTo(0.00001f));
            Assert.That(slideNormal.magnitude, Is.EqualTo(1f).Within(0.00001f));
            Assert.That(Vector3.Dot(slideNormal, new Vector3(radialNormal.x, 0f, radialNormal.z)), Is.GreaterThan(0f));
        }

        [Test]
        public void EveControllerClips_AllContainNineHairRotationBindings()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            Assert.That(controller, Is.Not.Null);

            AnimationClip[] clips = controller.animationClips.Distinct().ToArray();
            Assert.That(clips.Length, Is.EqualTo(69));
            for (int clipIndex = 0; clipIndex < clips.Length; clipIndex++)
            {
                AnimationClip clip = clips[clipIndex];
                EditorCurveBinding[] bindings = AnimationUtility.GetCurveBindings(clip);
                for (int boneIndex = 1; boneIndex <= PointCount; boneIndex++)
                {
                    string boneName = $"Ab-TL-HairB0{boneIndex}";
                    bool hasRotation = bindings.Any(binding =>
                        binding.path.EndsWith(boneName) &&
                        (binding.propertyName.Contains("m_LocalRotation") ||
                         binding.propertyName.Contains("localEulerAngles")));
                    Assert.That(hasRotation, Is.True, $"{clip.name} is missing rotation curves for {boneName}.");
                }
            }
        }

        /// <summary>
        /// 创建使用统一九段结构参数的测试求解器。
        /// </summary>
        /// <param name="gravityScale">测试使用的重力缩放。</param>
        /// <param name="bendDegrees">除根以外每个点允许的最大弯曲角。</param>
        /// <returns>尚未初始化、可直接 Reset 的求解器。</returns>
        private static PlayerHairSpringSolver CreateSolver(float gravityScale, float bendDegrees)
        {
            float[] lengths = Enumerable.Repeat(0.1f, PointCount - 1).ToArray();
            float[] bends = Enumerable.Repeat(bendDegrees, PointCount).ToArray();
            float[] radii = Enumerable.Repeat(0.02f, PointCount).ToArray();
            float[] weights = Enumerable.Repeat(1f, PointCount).ToArray();
            weights[0] = 0f;
            return new PlayerHairSpringSolver(
                lengths,
                bends,
                radii,
                weights,
                new PlayerHairSolverSettings
                {
                    FrequencyHz = 4.0f,
                    DampingRatio = 0.86f,
                    Inertia = 0.20f,
                    GravityScale = gravityScale,
                    CollisionMargin = 0f,
                    MaxSubstep = 1f / 120f,
                    MaxSubsteps = 4,
                    ContactTangentRetention = 0.5f,
                    DeepPenetrationThreshold = 0.025f
                });
        }

        /// <summary>
        /// 创建固定长度和方向的九点发链。
        /// </summary>
        /// <param name="origin">链根世界位置。</param>
        /// <param name="direction">从根指向梢的方向。</param>
        /// <param name="segmentLength">每段长度，单位为米。</param>
        /// <returns>九个世界空间发骨点。</returns>
        private static Vector3[] CreateStraightChain(Vector3 origin, Vector3 direction, float segmentLength)
        {
            Vector3[] points = new Vector3[PointCount];
            Vector3 normalizedDirection = direction.normalized;
            for (int i = 0; i < points.Length; i++)
            {
                points[i] = origin + normalizedDirection * (segmentLength * i);
            }

            return points;
        }

        /// <summary>
        /// 计算点到有限线段的最短距离。
        /// </summary>
        /// <param name="point">查询点。</param>
        /// <param name="segmentA">线段起点。</param>
        /// <param name="segmentB">线段终点。</param>
        /// <returns>最短距离，单位为米。</returns>
        private static float DistancePointToSegment(Vector3 point, Vector3 segmentA, Vector3 segmentB)
        {
            Vector3 direction = segmentB - segmentA;
            float denominator = direction.sqrMagnitude;
            if (denominator <= 0.000001f)
            {
                return Vector3.Distance(point, segmentA);
            }

            float parameter = Mathf.Clamp01(Vector3.Dot(point - segmentA, direction) / denominator);
            return Vector3.Distance(point, segmentA + direction * parameter);
        }
    }
}
