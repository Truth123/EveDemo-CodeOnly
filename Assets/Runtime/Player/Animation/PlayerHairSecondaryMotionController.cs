// 文件说明：在 Animator 求值后为 Eve 马尾叠加低惯性下垂、软碰撞与状态权重。
// 所属模块：玩家动画表现。
// 运行影响：只覆盖发骨旋转，不修改动画资产、Root Motion、玩家状态或战斗判定。

using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Player.Animation
{
    /// <summary>
    /// Eve 单条马尾的表现控制器。动画提供基础姿态，内部求解器生成碰撞安全目标并叠加克制的二级运动。
    /// </summary>
    [DefaultExecutionOrder(1000)]
    public sealed class PlayerHairSecondaryMotionController : MonoBehaviour
    {
        private const int ExpectedPointCount = 9;
        private const int CurrentColliderCapacity = 8;
        private const int SweptColliderCapacity = CurrentColliderCapacity * 4;
        private const float MinimumDirectionLengthSquared = 0.0000001f;
        private static readonly int PlayerStateHash = Animator.StringToHash("PlayerState");

        [Header("动画与发链")]
        [SerializeField] private Animator animator;
        [SerializeField] private Transform characterRoot;
        [SerializeField] private Transform chainRoot;
        [SerializeField] private Transform chainTip;

        [Header("身体代理锚点")]
        [SerializeField] private Transform head;
        [SerializeField] private Transform leftUpperArm;
        [SerializeField] private Transform rightUpperArm;
        [SerializeField] private Transform neck;
        [SerializeField] private Transform spine2;
        [SerializeField] private Transform pelvis;
        [SerializeField] private Transform leftThigh;
        [SerializeField] private Transform leftCalf;
        [SerializeField] private Transform rightThigh;
        [SerializeField] private Transform rightCalf;

        [Header("身体代理尺寸")]
        [SerializeField] private Vector3 headLocalOffset = new Vector3(0f, 0.08f, 0f);
        [SerializeField, Min(0f)] private float headRadius = 0.12f;
        [SerializeField, Min(0f)] private float shoulderRadius = 0.10f;
        [SerializeField, Min(0f)] private float upperBackRadius = 0.13f;
        [SerializeField, Min(0f)] private float torsoRadius = 0.15f;
        [SerializeField, Min(0f)] private float pelvisRadius = 0.16f;
        [SerializeField, Min(0f)] private float thighRadius = 0.11f;

        [Header("自然摆动")]
        [SerializeField, Min(0.01f)] private float frequencyHz = 4.0f;
        [SerializeField, Min(0f)] private float dampingRatio = 0.86f;
        [SerializeField, Range(0f, 1f)] private float inertia = 0.20f;
        [SerializeField, Min(0f)] private float gravityScale = 1.15f;
        [SerializeField, Min(0f)] private float stateBlendDuration = 0.18f;
        [SerializeField] private float[] pointDynamicWeights =
        {
            0f, 0.12f, 0.24f, 0.38f, 0.52f, 0.66f, 0.78f, 0.88f, 0.94f
        };
        [SerializeField] private float[] maxBendDegrees =
        {
            6f, 9.5f, 13f, 16.5f, 20f, 23.5f, 27f, 30.5f, 34f
        };

        [Header("软碰撞")]
        [SerializeField] private float[] hairRadii =
        {
            0.035f, 0.0325f, 0.030f, 0.0275f, 0.025f, 0.0225f, 0.020f, 0.0175f, 0.015f
        };
        [SerializeField, Min(0f)] private float collisionMargin = 0.005f;
        [SerializeField, Range(0f, 1f)] private float contactTangentRetention = 0.62f;
        [SerializeField, Min(0f)] private float deepPenetrationThreshold = 0.025f;

        [Header("稳定性")]
        [SerializeField, Min(0.0001f)] private float maxSubstep = 1f / 120f;
        [SerializeField, Min(1)] private int maxSubsteps = 4;
        [SerializeField, Min(0f)] private float teleportDistance = 0.75f;
        [SerializeField, Range(0f, 180f)] private float teleportAngle = 45f;

        private readonly PlayerHairCollisionCapsule[] currentColliders =
            new PlayerHairCollisionCapsule[CurrentColliderCapacity];
        private readonly PlayerHairCollisionCapsule[] previousColliders =
            new PlayerHairCollisionCapsule[CurrentColliderCapacity];
        private readonly PlayerHairCollisionCapsule[] sweptColliders =
            new PlayerHairCollisionCapsule[SweptColliderCapacity];

        private Transform[] chain;
        private Quaternion[] rawWorldRotations;
        private Quaternion[] lastFinalLocalRotations;
        private Vector3[] rawAnimationPoints;
        private Vector3[] outputPoints;
        private float[] segmentLengths;
        private PlayerHairSpringSolver solver;
        private Vector3 previousRootPosition;
        private Quaternion previousRootRotation;
        private int currentColliderCount;
        private int previousColliderCount;
        private int sweptColliderCount;
        private float currentStateDynamicWeight;
        private bool initialized;
        private bool hasPreviousFrame;
        private bool hasFinalPose;
        private bool animatorWasActive;
        private bool configurationErrorReported;

        /// <summary>
        /// 在对象唤醒时解析显式发链、预分配运行数组并创建纯求解器。
        /// </summary>
        private void Awake()
        {
            TryInitialize();
        }

        /// <summary>
        /// 组件重新启用时要求下一次 Animator 有效求值后执行无速度安全重置。
        /// </summary>
        private void OnEnable()
        {
            hasPreviousFrame = false;
            hasFinalPose = false;
            animatorWasActive = false;
            if (!initialized)
            {
                TryInitialize();
            }
        }

        /// <summary>
        /// Animator 完成本帧姿态后求解头发，并且只将最终方向转换为发骨旋转。
        /// </summary>
        private void LateUpdate()
        {
            if (!initialized && !TryInitialize())
            {
                return;
            }

            bool animatorActive = animator != null && animator.enabled && animator.gameObject.activeInHierarchy;
            if (!animatorActive)
            {
                animatorWasActive = false;
                hasPreviousFrame = false;
                return;
            }

            float deltaTime = Time.deltaTime;
            if (deltaTime <= 0f && hasFinalPose)
            {
                ApplyFrozenPose();
                animatorWasActive = true;
                return;
            }

            CaptureAnimationPose();
            BuildCurrentColliders();

            Vector3 currentRootPosition = characterRoot.position;
            Quaternion currentRootRotation = characterRoot.rotation;
            bool requiresReset =
                !animatorWasActive ||
                !hasPreviousFrame ||
                Vector3.Distance(previousRootPosition, currentRootPosition) > teleportDistance ||
                Quaternion.Angle(previousRootRotation, currentRootRotation) > teleportAngle;

            float targetStateWeight = ResolveStateDynamicWeight();
            if (stateBlendDuration <= 0f)
            {
                currentStateDynamicWeight = targetStateWeight;
            }
            else
            {
                currentStateDynamicWeight = Mathf.MoveTowards(
                    currentStateDynamicWeight,
                    targetStateWeight,
                    deltaTime / stateBlendDuration);
            }

            if (requiresReset)
            {
                solver.Reset(rawAnimationPoints, currentColliders, currentColliderCount);
                currentStateDynamicWeight = targetStateWeight;
                CopyCurrentCollidersToPrevious();
                previousRootPosition = currentRootPosition;
                previousRootRotation = currentRootRotation;
                hasPreviousFrame = true;
            }

            BuildSweptColliders();
            bool finiteResult = solver.Evaluate(
                rawAnimationPoints,
                previousRootPosition,
                previousRootRotation,
                currentRootPosition,
                currentRootRotation,
                deltaTime,
                currentStateDynamicWeight,
                Physics.gravity,
                currentColliders,
                currentColliderCount,
                sweptColliders,
                sweptColliderCount,
                outputPoints);

            WriteOutputRotations();
            CacheFinalPose();
            CopyCurrentCollidersToPrevious();
            previousRootPosition = currentRootPosition;
            previousRootRotation = currentRootRotation;
            hasPreviousFrame = true;
            animatorWasActive = true;

            if (!finiteResult)
            {
                Debug.LogWarning("Eve hair simulation encountered a non-finite value and reset to the current safe animation pose.", this);
            }
        }

        /// <summary>
        /// 解析配置并创建运行时求解数据；缺少关键引用时明确报错且不运行头发覆盖。
        /// </summary>
        /// <returns>全部引用和九骨连续链有效时返回 true，否则返回 false。</returns>
        private bool TryInitialize()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (characterRoot == null)
            {
                characterRoot = transform.root;
            }

            if (!TryBuildChain(chainRoot, chainTip, out chain, out string chainError))
            {
                ReportConfigurationError(chainError);
                return false;
            }

            if (animator == null || characterRoot == null || !HasAllBodyAnchors())
            {
                ReportConfigurationError("Animator, character root, or one of the body proxy anchors is missing.");
                return false;
            }

            EnsureNineElementArray(ref pointDynamicWeights, CreateDefaultDynamicWeights());
            EnsureNineElementArray(ref maxBendDegrees, CreateDefaultBendAngles());
            EnsureNineElementArray(ref hairRadii, CreateDefaultHairRadii());

            rawWorldRotations = new Quaternion[ExpectedPointCount];
            lastFinalLocalRotations = new Quaternion[ExpectedPointCount];
            rawAnimationPoints = new Vector3[ExpectedPointCount];
            outputPoints = new Vector3[ExpectedPointCount];
            segmentLengths = new float[ExpectedPointCount - 1];
            for (int i = 0; i < segmentLengths.Length; i++)
            {
                segmentLengths[i] = Vector3.Distance(chain[i].position, chain[i + 1].position);
                if (segmentLengths[i] <= 0.0001f)
                {
                    ReportConfigurationError($"Hair segment {chain[i].name} -> {chain[i + 1].name} has zero length.");
                    return false;
                }
            }

            PlayerHairSolverSettings settings = new PlayerHairSolverSettings
            {
                FrequencyHz = frequencyHz,
                DampingRatio = dampingRatio,
                Inertia = inertia,
                GravityScale = gravityScale,
                CollisionMargin = collisionMargin,
                MaxSubstep = maxSubstep,
                MaxSubsteps = maxSubsteps,
                ContactTangentRetention = contactTangentRetention,
                DeepPenetrationThreshold = deepPenetrationThreshold
            };
            solver = new PlayerHairSpringSolver(
                segmentLengths,
                maxBendDegrees,
                hairRadii,
                pointDynamicWeights,
                settings);

            initialized = true;
            configurationErrorReported = false;
            hasPreviousFrame = false;
            hasFinalPose = false;
            return true;
        }

        /// <summary>
        /// 从 Tip 沿父级回溯到 Root，验证恰好九根连续发骨并按根到梢返回。
        /// </summary>
        /// <param name="root">期望链根。</param>
        /// <param name="tip">期望链梢。</param>
        /// <param name="result">写回按根到梢排列的发骨数组。</param>
        /// <param name="error">失败时写回可读配置原因。</param>
        /// <returns>Root/Tip 构成恰好九根直接父子链时返回 true。</returns>
        private static bool TryBuildChain(
            Transform root,
            Transform tip,
            out Transform[] result,
            out string error)
        {
            result = null;
            if (root == null || tip == null)
            {
                error = "Hair chain root or tip is not assigned.";
                return false;
            }

            List<Transform> reverse = new List<Transform>(ExpectedPointCount);
            Transform current = tip;
            while (current != null)
            {
                reverse.Add(current);
                if (current == root)
                {
                    break;
                }

                current = current.parent;
            }

            if (reverse[reverse.Count - 1] != root)
            {
                error = $"Hair tip {tip.name} is not a descendant of root {root.name}.";
                return false;
            }

            if (reverse.Count != ExpectedPointCount)
            {
                error = $"Eve hair chain must contain exactly {ExpectedPointCount} bones but contains {reverse.Count}.";
                return false;
            }

            result = new Transform[ExpectedPointCount];
            for (int i = 0; i < reverse.Count; i++)
            {
                result[i] = reverse[reverse.Count - 1 - i];
                if (i > 0 && result[i].parent != result[i - 1])
                {
                    error = $"Hair bones {result[i - 1].name} and {result[i].name} are not direct parent and child.";
                    result = null;
                    return false;
                }
            }

            error = null;
            return true;
        }

        /// <summary>
        /// 检查构建身体碰撞代理所需的所有骨骼锚点。
        /// </summary>
        /// <returns>头、肩、躯干、骨盆和双腿锚点都存在时返回 true。</returns>
        private bool HasAllBodyAnchors()
        {
            return head != null &&
                   leftUpperArm != null && rightUpperArm != null &&
                   neck != null && spine2 != null && pelvis != null &&
                   leftThigh != null && leftCalf != null &&
                   rightThigh != null && rightCalf != null;
        }

        /// <summary>
        /// 保存 Animator 本帧的发骨世界位置和旋转，作为所有动态修正的唯一基础姿态。
        /// </summary>
        private void CaptureAnimationPose()
        {
            for (int i = 0; i < chain.Length; i++)
            {
                rawAnimationPoints[i] = chain[i].position;
                rawWorldRotations[i] = chain[i].rotation;
            }
        }

        /// <summary>
        /// 根据当前身体骨骼姿态填充七个数学碰撞代理。
        /// </summary>
        private void BuildCurrentColliders()
        {
            currentColliderCount = 0;
            Vector3 headCenter = head.TransformPoint(headLocalOffset);
            AddCurrentCollider(headCenter, headCenter, headRadius, 2);
            AddCurrentCollider(leftUpperArm.position, rightUpperArm.position, shoulderRadius);
            AddCurrentCollider(neck.position, spine2.position, upperBackRadius, 0, true);
            AddCurrentCollider(spine2.position, pelvis.position, torsoRadius, 0, true);
            AddCurrentCollider(pelvis.position, pelvis.position, pelvisRadius, 0, true);
            AddCurrentCollider(leftThigh.position, leftCalf.position, thighRadius, 0, true);
            AddCurrentCollider(rightThigh.position, rightCalf.position, thighRadius, 0, true);
        }

        /// <summary>
        /// 向当前帧预分配数组追加一个身体代理。
        /// </summary>
        /// <param name="pointA">胶囊起点或球心。</param>
        /// <param name="pointB">胶囊终点；球体与起点相同。</param>
        /// <param name="radius">代理半径，单位为米。</param>
        /// <param name="minHairSegmentIndex">代理开始影响的发链段索引。</param>
        /// <param name="preventUpwardCorrection">是否禁止代理把头发向上托起。</param>
        private void AddCurrentCollider(
            Vector3 pointA,
            Vector3 pointB,
            float radius,
            int minHairSegmentIndex = 0,
            bool preventUpwardCorrection = false)
        {
            if (currentColliderCount >= currentColliders.Length)
            {
                return;
            }

            currentColliders[currentColliderCount++] = new PlayerHairCollisionCapsule(
                pointA,
                pointB,
                radius,
                minHairSegmentIndex,
                preventUpwardCorrection);
        }

        /// <summary>
        /// 构造当前、上一帧和两端运动轨迹组成的扫掠代理，阻止身体高速穿过发链。
        /// </summary>
        private void BuildSweptColliders()
        {
            sweptColliderCount = 0;
            int count = Mathf.Min(currentColliderCount, previousColliderCount);
            for (int i = 0; i < count; i++)
            {
                PlayerHairCollisionCapsule previous = previousColliders[i];
                PlayerHairCollisionCapsule current = currentColliders[i];
                AddSweptCollider(current);
                AddSweptCollider(previous);
                AddSweptCollider(new PlayerHairCollisionCapsule(
                    previous.PointA,
                    current.PointA,
                    current.Radius,
                    current.MinHairSegmentIndex,
                    current.PreventUpwardCorrection));
                AddSweptCollider(new PlayerHairCollisionCapsule(
                    previous.PointB,
                    current.PointB,
                    current.Radius,
                    current.MinHairSegmentIndex,
                    current.PreventUpwardCorrection));
            }

            if (count == 0)
            {
                for (int i = 0; i < currentColliderCount; i++)
                {
                    AddSweptCollider(currentColliders[i]);
                }
            }
        }

        /// <summary>
        /// 向预分配扫掠数组追加代理。
        /// </summary>
        /// <param name="capsule">需要参与当前帧碰撞的世界空间代理。</param>
        private void AddSweptCollider(PlayerHairCollisionCapsule capsule)
        {
            if (sweptColliderCount < sweptColliders.Length)
            {
                sweptColliders[sweptColliderCount++] = capsule;
            }
        }

        /// <summary>
        /// 保存当前身体代理供下一帧生成连续扫掠体。
        /// </summary>
        private void CopyCurrentCollidersToPrevious()
        {
            previousColliderCount = currentColliderCount;
            for (int i = 0; i < currentColliderCount; i++)
            {
                previousColliders[i] = currentColliders[i];
            }
        }

        /// <summary>
        /// 将求解后的世界方向转换为相对本帧 Animator 旋转的增量，并从根到梢写回。
        /// </summary>
        private void WriteOutputRotations()
        {
            Quaternion lastDelta = Quaternion.identity;
            for (int i = 0; i < chain.Length - 1; i++)
            {
                Vector3 rawDirection = rawAnimationPoints[i + 1] - rawAnimationPoints[i];
                Vector3 outputDirection = outputPoints[i + 1] - outputPoints[i];
                if (rawDirection.sqrMagnitude <= MinimumDirectionLengthSquared ||
                    outputDirection.sqrMagnitude <= MinimumDirectionLengthSquared)
                {
                    chain[i].rotation = rawWorldRotations[i];
                    continue;
                }

                lastDelta = Quaternion.FromToRotation(rawDirection, outputDirection);
                chain[i].rotation = lastDelta * rawWorldRotations[i];
            }

            chain[chain.Length - 1].rotation = lastDelta * rawWorldRotations[chain.Length - 1];
        }

        /// <summary>
        /// 缓存本帧最终局部旋转，使 TimeScale 为零时 Animator 不会把头发恢复为未求解姿态。
        /// </summary>
        private void CacheFinalPose()
        {
            for (int i = 0; i < chain.Length; i++)
            {
                lastFinalLocalRotations[i] = chain[i].localRotation;
            }

            hasFinalPose = true;
        }

        /// <summary>
        /// 在暂停或结算冻结期间重新应用上一帧最终局部旋转，不推进任何模拟时间。
        /// </summary>
        private void ApplyFrozenPose()
        {
            for (int i = 0; i < chain.Length; i++)
            {
                chain[i].localRotation = lastFinalLocalRotations[i];
            }
        }

        /// <summary>
        /// 根据现有 PlayerState Animator 参数返回动态表现权重；碰撞安全修正不受该权重关闭。
        /// </summary>
        /// <returns>当前顶层状态对应的 0～1 动态权重。</returns>
        private float ResolveStateDynamicWeight()
        {
            PlayerStateId state = (PlayerStateId)animator.GetInteger(PlayerStateHash);
            switch (state)
            {
                case PlayerStateId.Attack:
                case PlayerStateId.Skill:
                case PlayerStateId.HitReaction:
                    return 0.85f;
                case PlayerStateId.Evade:
                    return 0.65f;
                case PlayerStateId.Knockdown:
                case PlayerStateId.Dead:
                    return 0f;
                case PlayerStateId.Idle:
                case PlayerStateId.Locomotion:
                case PlayerStateId.Guard:
                default:
                    return 1f;
            }
        }

        /// <summary>
        /// 首次发现缺失配置时输出一次明确错误，避免每帧刷屏。
        /// </summary>
        /// <param name="message">需要提示给场景作者的具体配置原因。</param>
        private void ReportConfigurationError(string message)
        {
            if (configurationErrorReported)
            {
                return;
            }

            configurationErrorReported = true;
            Debug.LogError($"PlayerHairSecondaryMotionController disabled: {message}", this);
        }

        /// <summary>
        /// 保证 Inspector 数组保持九项；只在初始化或编辑器校验时替换无效配置。
        /// </summary>
        /// <param name="values">待校验并可能写回默认值的数组。</param>
        /// <param name="defaults">数量正确的默认数组。</param>
        private static void EnsureNineElementArray(ref float[] values, float[] defaults)
        {
            if (values == null || values.Length != ExpectedPointCount)
            {
                values = defaults;
            }
        }

        /// <summary>创建根部固定、向发梢渐增的默认动态权重。</summary>
        private static float[] CreateDefaultDynamicWeights()
        {
            return new[] { 0f, 0.12f, 0.24f, 0.38f, 0.52f, 0.66f, 0.78f, 0.88f, 0.94f };
        }

        /// <summary>创建从根到梢逐渐放宽的默认弯曲角。</summary>
        private static float[] CreateDefaultBendAngles()
        {
            return new[] { 6f, 9.5f, 13f, 16.5f, 20f, 23.5f, 27f, 30.5f, 34f };
        }

        /// <summary>创建根部较宽、向发梢收缩的默认碰撞半径。</summary>
        private static float[] CreateDefaultHairRadii()
        {
            return new[] { 0.035f, 0.0325f, 0.030f, 0.0275f, 0.025f, 0.0225f, 0.020f, 0.0175f, 0.015f };
        }

#if UNITY_EDITOR
        /// <summary>
        /// 编辑器修改参数时限制危险范围，并修复被误改长度的九项数组。
        /// </summary>
        private void OnValidate()
        {
            frequencyHz = Mathf.Max(0.01f, frequencyHz);
            dampingRatio = Mathf.Max(0f, dampingRatio);
            inertia = Mathf.Clamp01(inertia);
            gravityScale = Mathf.Max(0f, gravityScale);
            stateBlendDuration = Mathf.Max(0f, stateBlendDuration);
            collisionMargin = Mathf.Max(0f, collisionMargin);
            contactTangentRetention = Mathf.Clamp01(contactTangentRetention);
            deepPenetrationThreshold = Mathf.Max(0f, deepPenetrationThreshold);
            maxSubstep = Mathf.Max(0.0001f, maxSubstep);
            maxSubsteps = Mathf.Max(1, maxSubsteps);
            teleportDistance = Mathf.Max(0f, teleportDistance);
            teleportAngle = Mathf.Clamp(teleportAngle, 0f, 180f);
            EnsureNineElementArray(ref pointDynamicWeights, CreateDefaultDynamicWeights());
            EnsureNineElementArray(ref maxBendDegrees, CreateDefaultBendAngles());
            EnsureNineElementArray(ref hairRadii, CreateDefaultHairRadii());
        }

        /// <summary>
        /// 仅在组件被选中时绘制身体代理和发链半径，辅助调校且不产生运行时 UI。
        /// </summary>
        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.15f, 0.9f, 1f, 0.75f);
            DrawSphereProxy(head, headLocalOffset, headRadius);
            DrawCapsuleProxy(leftUpperArm, rightUpperArm, shoulderRadius);
            DrawCapsuleProxy(neck, spine2, upperBackRadius);
            DrawCapsuleProxy(spine2, pelvis, torsoRadius);
            DrawSphereProxy(pelvis, Vector3.zero, pelvisRadius);
            DrawCapsuleProxy(leftThigh, leftCalf, thighRadius);
            DrawCapsuleProxy(rightThigh, rightCalf, thighRadius);

            if (!TryBuildChain(chainRoot, chainTip, out Transform[] gizmoChain, out _))
            {
                return;
            }

            Gizmos.color = new Color(1f, 0.45f, 0.9f, 0.85f);
            for (int i = 0; i < gizmoChain.Length; i++)
            {
                float radius = hairRadii != null && hairRadii.Length == ExpectedPointCount
                    ? hairRadii[i]
                    : Mathf.Lerp(0.035f, 0.015f, i / (float)(ExpectedPointCount - 1));
                Gizmos.DrawWireSphere(gizmoChain[i].position, radius);
                if (i < gizmoChain.Length - 1)
                {
                    Gizmos.DrawLine(gizmoChain[i].position, gizmoChain[i + 1].position);
                }
            }
        }

        /// <summary>
        /// 绘制一个球形身体代理。
        /// </summary>
        /// <param name="anchor">球体跟随的骨骼。</param>
        /// <param name="localOffset">球心相对骨骼的局部偏移。</param>
        /// <param name="radius">球体半径，单位为米。</param>
        private static void DrawSphereProxy(Transform anchor, Vector3 localOffset, float radius)
        {
            if (anchor != null)
            {
                Gizmos.DrawWireSphere(anchor.TransformPoint(localOffset), radius);
            }
        }

        /// <summary>
        /// 以端点球和中心线绘制胶囊代理的简化 Gizmo。
        /// </summary>
        /// <param name="pointA">胶囊起点骨骼。</param>
        /// <param name="pointB">胶囊终点骨骼。</param>
        /// <param name="radius">胶囊半径，单位为米。</param>
        private static void DrawCapsuleProxy(Transform pointA, Transform pointB, float radius)
        {
            if (pointA == null || pointB == null)
            {
                return;
            }

            Gizmos.DrawWireSphere(pointA.position, radius);
            Gizmos.DrawWireSphere(pointB.position, radius);
            Gizmos.DrawLine(pointA.position, pointB.position);
        }
#endif
    }
}
