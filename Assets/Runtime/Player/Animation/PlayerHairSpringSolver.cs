// 文件说明：求解 Eve 马尾的动画目标跟随、轻量弹簧、骨长/弯曲约束与身体软碰撞。
// 所属模块：玩家动画表现。
// 运行影响：只计算发骨表现位置，不参与角色移动、战斗物理或伤害判定。

using UnityEngine;

namespace ProjectEVE.Player.Animation
{
    /// <summary>
    /// 以线段胶囊表达头发专用的数学碰撞代理，不注册到 Unity Physics 世界。
    /// </summary>
    internal struct PlayerHairCollisionCapsule
    {
        /// <summary>胶囊中心线起点，世界坐标。</summary>
        public Vector3 PointA;
        /// <summary>胶囊中心线终点，世界坐标；与起点相同时表达球体。</summary>
        public Vector3 PointB;
        /// <summary>胶囊半径，单位为米。</summary>
        public float Radius;
        /// <summary>该代理开始影响的发链段索引，用于允许发根从头部绑定点自然穿出。</summary>
        public int MinHairSegmentIndex;
        /// <summary>是否禁止接触法线向上托举头发，使腰髋和腿部接触优先沿身体外侧滑落。</summary>
        public bool PreventUpwardCorrection;

        /// <summary>
        /// 创建世界空间头发碰撞代理。
        /// </summary>
        /// <param name="pointA">胶囊中心线起点，世界坐标。</param>
        /// <param name="pointB">胶囊中心线终点，世界坐标。</param>
        /// <param name="radius">胶囊半径，单位为米。</param>
        /// <param name="minHairSegmentIndex">该代理开始影响的发链段索引，0 表示从根段开始。</param>
        /// <param name="preventUpwardCorrection">为 true 时去除接触修正中的向上分量。</param>
        public PlayerHairCollisionCapsule(
            Vector3 pointA,
            Vector3 pointB,
            float radius,
            int minHairSegmentIndex = 0,
            bool preventUpwardCorrection = false)
        {
            PointA = pointA;
            PointB = pointB;
            Radius = Mathf.Max(0f, radius);
            MinHairSegmentIndex = Mathf.Max(0, minHairSegmentIndex);
            PreventUpwardCorrection = preventUpwardCorrection;
        }
    }

    /// <summary>
    /// 保存单条发链的稳定求解参数；调用方在初始化时提供，运行期间不产生配置资产。
    /// </summary>
    internal struct PlayerHairSolverSettings
    {
        /// <summary>发链追随碰撞安全动画目标的固有频率，单位 Hz。</summary>
        public float FrequencyHz;
        /// <summary>弹簧阻尼比，1 附近表示接近临界阻尼。</summary>
        public float DampingRatio;
        /// <summary>保留世界空间位置的比例，0 完全跟随角色，1 完全保留。</summary>
        public float Inertia;
        /// <summary>Physics.gravity 对发链的缩放比例。</summary>
        public float GravityScale;
        /// <summary>碰撞体之外额外保留的安全距离，单位为米。</summary>
        public float CollisionMargin;
        /// <summary>每个积分子步允许的最大时长，单位为秒。</summary>
        public float MaxSubstep;
        /// <summary>单帧最多执行的积分子步数量。</summary>
        public int MaxSubsteps;
        /// <summary>接触后保留的切向速度比例。</summary>
        public float ContactTangentRetention;
        /// <summary>超过该深度的去穿透会直接清零速度，单位为米。</summary>
        public float DeepPenetrationThreshold;
    }

    /// <summary>
    /// 纯 C# 发链求解器。动画负责基础轮廓，求解器只增加低惯性下垂并将最终链保持在身体代理之外。
    /// </summary>
    internal sealed class PlayerHairSpringSolver
    {
        private const float Epsilon = 0.000001f;
        private const int SafeTargetIterations = 5;
        private const int SimulationConstraintIterations = 2;
        private const int OutputConstraintIterations = 3;

        private readonly int pointCount;
        private readonly float[] segmentLengths;
        private readonly float[] maxBendRadians;
        private readonly float[] hairRadii;
        private readonly float[] pointDynamicWeights;
        private readonly Vector3[] safeTargets;
        private readonly Vector3[] positions;
        private readonly Vector3[] velocities;
        private readonly Vector3[] outputScratch;
        private readonly Vector3[] contactNormals;
        private readonly float[] contactDepths;
        private readonly bool[] contacts;

        private PlayerHairSolverSettings settings;
        private bool initialized;

        /// <summary>
        /// 创建固定点数的发链求解器并复制不会在运行时变化的结构参数。
        /// </summary>
        /// <param name="segmentLengths">相邻发骨之间的原始长度，数量必须比点数少一。</param>
        /// <param name="maxBendDegrees">各发骨点相对动画方向允许的最大偏角，单位为度。</param>
        /// <param name="hairRadii">各发骨点参与碰撞的有效半径，单位为米。</param>
        /// <param name="pointDynamicWeights">各发骨点混入动态结果的权重。</param>
        /// <param name="solverSettings">弹簧、时间步和接触响应参数。</param>
        public PlayerHairSpringSolver(
            float[] segmentLengths,
            float[] maxBendDegrees,
            float[] hairRadii,
            float[] pointDynamicWeights,
            PlayerHairSolverSettings solverSettings)
        {
            pointCount = hairRadii != null ? hairRadii.Length : 0;
            if (pointCount < 2 ||
                segmentLengths == null || segmentLengths.Length != pointCount - 1 ||
                maxBendDegrees == null || maxBendDegrees.Length != pointCount ||
                pointDynamicWeights == null || pointDynamicWeights.Length != pointCount)
            {
                throw new System.ArgumentException("Hair solver arrays must describe one chain with at least two points.");
            }

            this.segmentLengths = (float[])segmentLengths.Clone();
            maxBendRadians = new float[pointCount];
            this.hairRadii = (float[])hairRadii.Clone();
            this.pointDynamicWeights = (float[])pointDynamicWeights.Clone();
            for (int i = 0; i < pointCount; i++)
            {
                maxBendRadians[i] = Mathf.Max(0f, maxBendDegrees[i]) * Mathf.Deg2Rad;
                this.hairRadii[i] = Mathf.Max(0f, this.hairRadii[i]);
                this.pointDynamicWeights[i] = Mathf.Clamp01(this.pointDynamicWeights[i]);
            }

            settings = solverSettings;
            settings.FrequencyHz = Mathf.Max(0.01f, settings.FrequencyHz);
            settings.DampingRatio = Mathf.Max(0f, settings.DampingRatio);
            settings.Inertia = Mathf.Clamp01(settings.Inertia);
            settings.GravityScale = Mathf.Max(0f, settings.GravityScale);
            settings.CollisionMargin = Mathf.Max(0f, settings.CollisionMargin);
            settings.MaxSubstep = Mathf.Max(0.0001f, settings.MaxSubstep);
            settings.MaxSubsteps = Mathf.Max(1, settings.MaxSubsteps);
            settings.ContactTangentRetention = Mathf.Clamp01(settings.ContactTangentRetention);
            settings.DeepPenetrationThreshold = Mathf.Max(0f, settings.DeepPenetrationThreshold);

            safeTargets = new Vector3[pointCount];
            positions = new Vector3[pointCount];
            velocities = new Vector3[pointCount];
            outputScratch = new Vector3[pointCount];
            contactNormals = new Vector3[pointCount];
            contactDepths = new float[pointCount];
            contacts = new bool[pointCount];
        }

        /// <summary>求解器是否已经用一帧有效动画姿态完成无速度初始化。</summary>
        internal bool IsInitialized => initialized;

        /// <summary>
        /// 从当前动画姿态构造无穿透发链，并把所有速度清零；用于启用、传送和异常恢复。
        /// </summary>
        /// <param name="rawAnimationPoints">本帧 Animator 输出的发骨世界位置。</param>
        /// <param name="currentColliders">当前帧玩家身体数学代理。</param>
        /// <param name="currentColliderCount">碰撞代理数组内的有效元素数量。</param>
        public void Reset(
            Vector3[] rawAnimationPoints,
            PlayerHairCollisionCapsule[] currentColliders,
            int currentColliderCount)
        {
            ValidatePointArray(rawAnimationPoints, nameof(rawAnimationPoints));
            BuildSafeTargets(rawAnimationPoints, currentColliders, currentColliderCount);
            for (int i = 0; i < pointCount; i++)
            {
                positions[i] = safeTargets[i];
                velocities[i] = Vector3.zero;
                outputScratch[i] = safeTargets[i];
            }

            initialized = true;
        }

        /// <summary>
        /// 推进一帧低惯性弹簧，并返回始终以碰撞安全动画姿态为基准的最终世界空间发骨点。
        /// </summary>
        /// <param name="rawAnimationPoints">本帧 Animator 输出的发骨世界位置。</param>
        /// <param name="previousRootPosition">上一帧角色根节点世界位置。</param>
        /// <param name="previousRootRotation">上一帧角色根节点世界旋转。</param>
        /// <param name="currentRootPosition">当前帧角色根节点世界位置。</param>
        /// <param name="currentRootRotation">当前帧角色根节点世界旋转。</param>
        /// <param name="deltaTime">缩放后的帧时间，单位为秒。</param>
        /// <param name="stateDynamicWeight">当前玩家状态允许的动态权重。</param>
        /// <param name="gravity">项目世界重力，单位为米每二次方秒。</param>
        /// <param name="currentColliders">当前帧身体代理，用于生成安全目标和最终复核。</param>
        /// <param name="currentColliderCount">当前帧代理数组内的有效元素数量。</param>
        /// <param name="sweptColliders">包含当前、上一帧及运动扫掠体的代理数组。</param>
        /// <param name="sweptColliderCount">扫掠代理数组内的有效元素数量。</param>
        /// <param name="outputPoints">写回最终发骨世界位置的预分配数组。</param>
        /// <returns>成功得到有限且满足约束的结果时返回 true；检测到异常值并重置时返回 false。</returns>
        public bool Evaluate(
            Vector3[] rawAnimationPoints,
            Vector3 previousRootPosition,
            Quaternion previousRootRotation,
            Vector3 currentRootPosition,
            Quaternion currentRootRotation,
            float deltaTime,
            float stateDynamicWeight,
            Vector3 gravity,
            PlayerHairCollisionCapsule[] currentColliders,
            int currentColliderCount,
            PlayerHairCollisionCapsule[] sweptColliders,
            int sweptColliderCount,
            Vector3[] outputPoints)
        {
            ValidatePointArray(rawAnimationPoints, nameof(rawAnimationPoints));
            ValidatePointArray(outputPoints, nameof(outputPoints));
            BuildSafeTargets(rawAnimationPoints, currentColliders, currentColliderCount);

            if (!initialized)
            {
                Reset(rawAnimationPoints, currentColliders, currentColliderCount);
            }

            float clampedStateWeight = Mathf.Clamp01(stateDynamicWeight);
            if (deltaTime <= 0f || clampedStateWeight <= Epsilon)
            {
                for (int i = 0; i < pointCount; i++)
                {
                    positions[i] = safeTargets[i];
                    velocities[i] = Vector3.zero;
                    outputPoints[i] = safeTargets[i];
                }

                return true;
            }

            ApplyRootFollow(previousRootPosition, previousRootRotation, currentRootPosition, currentRootRotation);

            float simulatedTime = Mathf.Min(deltaTime, settings.MaxSubstep * settings.MaxSubsteps);
            int substepCount = Mathf.Clamp(Mathf.CeilToInt(simulatedTime / settings.MaxSubstep), 1, settings.MaxSubsteps);
            float substep = simulatedTime / substepCount;
            for (int step = 0; step < substepCount; step++)
            {
                Integrate(substep, gravity);
                ClearContacts();
                for (int iteration = 0; iteration < SimulationConstraintIterations; iteration++)
                {
                    ConstrainChain(positions, rawAnimationPoints);
                    ResolveCollisions(positions, sweptColliders, sweptColliderCount, true);
                }

                ConstrainChain(positions, rawAnimationPoints);
                ResolveCollisions(positions, currentColliders, currentColliderCount, true);
                ApplyContactVelocities();
            }

            for (int i = 0; i < pointCount; i++)
            {
                float dynamicBlend = pointDynamicWeights[i] * clampedStateWeight;
                outputScratch[i] = Vector3.LerpUnclamped(safeTargets[i], positions[i], dynamicBlend);
            }

            for (int iteration = 0; iteration < OutputConstraintIterations; iteration++)
            {
                ConstrainChain(outputScratch, rawAnimationPoints);
                ResolveCollisions(outputScratch, currentColliders, currentColliderCount, false);
            }

            ConstrainChain(outputScratch, rawAnimationPoints);
            ResolveCollisions(outputScratch, currentColliders, currentColliderCount, false);

            if (!AllFinite(outputScratch) || !AllFinite(positions) || !AllFinite(velocities))
            {
                Reset(rawAnimationPoints, currentColliders, currentColliderCount);
                for (int i = 0; i < pointCount; i++)
                {
                    outputPoints[i] = safeTargets[i];
                }

                return false;
            }

            for (int i = 0; i < pointCount; i++)
            {
                outputPoints[i] = outputScratch[i];
            }

            return true;
        }

        /// <summary>
        /// 返回指定发骨点当前模拟速度，供定向测试确认去穿透没有注入弹射能量。
        /// </summary>
        /// <param name="pointIndex">发骨点索引，0 对应链根。</param>
        /// <returns>该点的世界空间速度，单位为米每秒。</returns>
        internal Vector3 GetVelocity(int pointIndex)
        {
            return velocities[pointIndex];
        }

        /// <summary>
        /// 将模拟点的大部分位姿变化随角色根节点移动，只留下配置比例的世界空间惯性。
        /// </summary>
        /// <param name="previousPosition">上一帧角色根节点世界位置。</param>
        /// <param name="previousRotation">上一帧角色根节点世界旋转。</param>
        /// <param name="currentPosition">当前帧角色根节点世界位置。</param>
        /// <param name="currentRotation">当前帧角色根节点世界旋转。</param>
        private void ApplyRootFollow(
            Vector3 previousPosition,
            Quaternion previousRotation,
            Vector3 currentPosition,
            Quaternion currentRotation)
        {
            Quaternion fullDeltaRotation = currentRotation * Quaternion.Inverse(previousRotation);
            float follow = 1f - settings.Inertia;
            Quaternion velocityRotation = Quaternion.SlerpUnclamped(Quaternion.identity, fullDeltaRotation, follow);
            for (int i = 1; i < pointCount; i++)
            {
                Vector3 fullyFollowed = currentPosition + fullDeltaRotation * (positions[i] - previousPosition);
                positions[i] = Vector3.LerpUnclamped(positions[i], fullyFollowed, follow);
                velocities[i] = velocityRotation * velocities[i];
            }

            positions[0] = safeTargets[0];
            velocities[0] = Vector3.zero;
        }

        /// <summary>
        /// 用半隐式欧拉积分让发链追随安全动画目标，同时施加重力。
        /// </summary>
        /// <param name="deltaTime">当前积分子步时长，单位为秒。</param>
        /// <param name="gravity">世界空间重力，单位为米每二次方秒。</param>
        private void Integrate(float deltaTime, Vector3 gravity)
        {
            float omega = 2f * Mathf.PI * settings.FrequencyHz;
            float stiffness = omega * omega;
            float damping = 2f * settings.DampingRatio * omega;
            for (int i = 1; i < pointCount; i++)
            {
                float gravityAlongChain = Mathf.Lerp(0.35f, 1f, i / (float)(pointCount - 1));
                Vector3 acceleration =
                    stiffness * (safeTargets[i] - positions[i]) -
                    damping * velocities[i] +
                    gravity * (settings.GravityScale * gravityAlongChain);
                velocities[i] += acceleration * deltaTime;
                positions[i] += velocities[i] * deltaTime;
            }

            positions[0] = safeTargets[0];
            velocities[0] = Vector3.zero;
        }

        /// <summary>
        /// 从原始动画姿态生成当前帧的碰撞安全目标；修正量不会进入速度状态。
        /// </summary>
        /// <param name="rawAnimationPoints">Animator 输出的原始发骨世界位置。</param>
        /// <param name="currentColliders">当前身体代理。</param>
        /// <param name="currentColliderCount">身体代理数组内的有效数量。</param>
        private void BuildSafeTargets(
            Vector3[] rawAnimationPoints,
            PlayerHairCollisionCapsule[] currentColliders,
            int currentColliderCount)
        {
            for (int i = 0; i < pointCount; i++)
            {
                safeTargets[i] = rawAnimationPoints[i];
            }

            for (int iteration = 0; iteration < SafeTargetIterations; iteration++)
            {
                ConstrainChain(safeTargets, rawAnimationPoints);
                ResolveCollisions(safeTargets, currentColliders, currentColliderCount, false);
            }

            ConstrainChain(safeTargets, rawAnimationPoints);
            ResolveCollisions(safeTargets, currentColliders, currentColliderCount, false);
        }

        /// <summary>
        /// 恢复各段原始长度，并限制当前方向相对动画方向的最大偏角。
        /// </summary>
        /// <param name="points">需要原地修正的世界空间发骨点。</param>
        /// <param name="rawAnimationPoints">本帧动画点，用作根位置和弯曲方向基准。</param>
        private void ConstrainChain(Vector3[] points, Vector3[] rawAnimationPoints)
        {
            points[0] = rawAnimationPoints[0];
            for (int i = 0; i < pointCount - 1; i++)
            {
                Vector3 rawDirection = rawAnimationPoints[i + 1] - rawAnimationPoints[i];
                Vector3 currentDirection = points[i + 1] - points[i];
                if (rawDirection.sqrMagnitude <= Epsilon)
                {
                    rawDirection = Vector3.down;
                }

                if (currentDirection.sqrMagnitude <= Epsilon)
                {
                    currentDirection = rawDirection;
                }

                rawDirection.Normalize();
                currentDirection.Normalize();
                float angle = Vector3.Angle(rawDirection, currentDirection) * Mathf.Deg2Rad;
                float maxAngle = maxBendRadians[Mathf.Min(i + 1, maxBendRadians.Length - 1)];
                if (angle > maxAngle && angle > Epsilon)
                {
                    Quaternion delta = Quaternion.FromToRotation(rawDirection, currentDirection);
                    currentDirection = (Quaternion.SlerpUnclamped(Quaternion.identity, delta, maxAngle / angle) * rawDirection).normalized;
                }

                float animatedLength = Vector3.Distance(rawAnimationPoints[i], rawAnimationPoints[i + 1]);
                float constrainedLength = animatedLength > Epsilon ? animatedLength : segmentLengths[i];
                points[i + 1] = points[i] + currentDirection * constrainedLength;
            }
        }

        /// <summary>
        /// 将发链线段胶囊推出身体代理，并可记录接触法线以修正速度。
        /// </summary>
        /// <param name="points">需要原地修正的发骨点。</param>
        /// <param name="colliders">身体或扫掠碰撞代理数组。</param>
        /// <param name="colliderCount">碰撞代理数组内的有效数量。</param>
        /// <param name="recordVelocityContact">为 true 时记录接触法线和深度，供积分后移除弹射速度。</param>
        private void ResolveCollisions(
            Vector3[] points,
            PlayerHairCollisionCapsule[] colliders,
            int colliderCount,
            bool recordVelocityContact)
        {
            if (colliders == null || colliderCount <= 0)
            {
                return;
            }

            int validColliderCount = Mathf.Min(colliderCount, colliders.Length);
            for (int segmentIndex = 0; segmentIndex < pointCount - 1; segmentIndex++)
            {
                for (int colliderIndex = 0; colliderIndex < validColliderCount; colliderIndex++)
                {
                    PlayerHairCollisionCapsule body = colliders[colliderIndex];
                    if (segmentIndex < body.MinHairSegmentIndex)
                    {
                        continue;
                    }

                    ClosestPointsOnSegments(
                        points[segmentIndex],
                        points[segmentIndex + 1],
                        body.PointA,
                        body.PointB,
                        out float hairT,
                        out _,
                        out Vector3 hairPoint,
                        out Vector3 bodyPoint);

                    float combinedRadius =
                        Mathf.Lerp(hairRadii[segmentIndex], hairRadii[segmentIndex + 1], hairT) +
                        body.Radius +
                        settings.CollisionMargin;
                    Vector3 separation = hairPoint - bodyPoint;
                    float distance = separation.magnitude;
                    float penetration = combinedRadius - distance;
                    if (penetration <= 0f)
                    {
                        continue;
                    }

                    Vector3 normal = distance > Epsilon
                        ? separation / distance
                        : ResolveFallbackNormal(points[segmentIndex], points[segmentIndex + 1], body);
                    normal = ResolveContactNormal(normal, body.PreventUpwardCorrection);
                    Vector3 correction = normal * penetration;
                    float parentShare = segmentIndex == 0 ? 0f : Mathf.Lerp(0.25f, 0.45f, hairT);
                    float childShare = 1f - parentShare;
                    points[segmentIndex] += correction * parentShare;
                    points[segmentIndex + 1] += correction * childShare;

                    if (!recordVelocityContact)
                    {
                        continue;
                    }

                    RecordContact(segmentIndex, normal, penetration, parentShare);
                    RecordContact(segmentIndex + 1, normal, penetration, childShare);
                }
            }

            points[0] = safeTargets[0];
        }

        /// <summary>
        /// 对躯干下部代理移除向上的接触分量，使重力能让头发沿身体表面向下滑动，而不是被球体端帽托起。
        /// </summary>
        /// <param name="normal">几何计算得到的身体外侧法线。</param>
        /// <param name="preventUpwardCorrection">是否限制向上修正。</param>
        /// <returns>用于位置投影和速度修正的单位法线。</returns>
        internal static Vector3 ResolveContactNormal(Vector3 normal, bool preventUpwardCorrection)
        {
            if (!preventUpwardCorrection || normal.y <= 0f)
            {
                return normal;
            }

            Vector3 horizontal = Vector3.ProjectOnPlane(normal, Vector3.up);
            return horizontal.sqrMagnitude > Epsilon ? horizontal.normalized : Vector3.back;
        }

        /// <summary>
        /// 记录一个发骨点的累计接触法线与最大穿透深度。
        /// </summary>
        /// <param name="pointIndex">接触发骨点索引。</param>
        /// <param name="normal">从身体表面指向头发外部的世界法线。</param>
        /// <param name="penetration">本次穿透深度，单位为米。</param>
        /// <param name="share">本点承担的修正比例。</param>
        private void RecordContact(int pointIndex, Vector3 normal, float penetration, float share)
        {
            if (pointIndex <= 0 || pointIndex >= pointCount || share <= 0f)
            {
                return;
            }

            contacts[pointIndex] = true;
            contactNormals[pointIndex] += normal * share;
            contactDepths[pointIndex] = Mathf.Max(contactDepths[pointIndex], penetration * share);
        }

        /// <summary>
        /// 移除接触点的法向速度并衰减切向速度，确保投影修正不会转化为反弹。
        /// </summary>
        private void ApplyContactVelocities()
        {
            for (int i = 1; i < pointCount; i++)
            {
                if (!contacts[i])
                {
                    continue;
                }

                if (contactDepths[i] >= settings.DeepPenetrationThreshold)
                {
                    velocities[i] = Vector3.zero;
                    continue;
                }

                Vector3 normal = contactNormals[i].sqrMagnitude > Epsilon
                    ? contactNormals[i].normalized
                    : Vector3.up;
                Vector3 tangent = velocities[i] - Vector3.Dot(velocities[i], normal) * normal;
                velocities[i] = tangent * settings.ContactTangentRetention;
            }
        }

        /// <summary>清理当前子步的接触缓存，不产生临时分配。</summary>
        private void ClearContacts()
        {
            for (int i = 0; i < pointCount; i++)
            {
                contacts[i] = false;
                contactNormals[i] = Vector3.zero;
                contactDepths[i] = 0f;
            }
        }

        /// <summary>
        /// 在身体中心与发链中心重合时生成稳定的去穿透方向。
        /// </summary>
        /// <param name="hairA">发链段起点。</param>
        /// <param name="hairB">发链段终点。</param>
        /// <param name="body">发生重合的身体代理。</param>
        /// <returns>指向身体外侧的单位世界方向。</returns>
        private static Vector3 ResolveFallbackNormal(
            Vector3 hairA,
            Vector3 hairB,
            PlayerHairCollisionCapsule body)
        {
            Vector3 fromBody = (hairA + hairB) * 0.5f - (body.PointA + body.PointB) * 0.5f;
            if (fromBody.sqrMagnitude > Epsilon)
            {
                return fromBody.normalized;
            }

            Vector3 cross = Vector3.Cross(hairB - hairA, body.PointB - body.PointA);
            return cross.sqrMagnitude > Epsilon ? cross.normalized : Vector3.back;
        }

        /// <summary>
        /// 计算两条有限线段上的最近点和对应归一化参数。
        /// </summary>
        /// <param name="p1">第一条线段起点。</param>
        /// <param name="q1">第一条线段终点。</param>
        /// <param name="p2">第二条线段起点。</param>
        /// <param name="q2">第二条线段终点。</param>
        /// <param name="s">写回第一条线段最近点的 0～1 参数。</param>
        /// <param name="t">写回第二条线段最近点的 0～1 参数。</param>
        /// <param name="point1">写回第一条线段最近点。</param>
        /// <param name="point2">写回第二条线段最近点。</param>
        private static void ClosestPointsOnSegments(
            Vector3 p1,
            Vector3 q1,
            Vector3 p2,
            Vector3 q2,
            out float s,
            out float t,
            out Vector3 point1,
            out Vector3 point2)
        {
            Vector3 d1 = q1 - p1;
            Vector3 d2 = q2 - p2;
            Vector3 r = p1 - p2;
            float a = Vector3.Dot(d1, d1);
            float e = Vector3.Dot(d2, d2);
            float f = Vector3.Dot(d2, r);

            if (a <= Epsilon && e <= Epsilon)
            {
                s = 0f;
                t = 0f;
            }
            else if (a <= Epsilon)
            {
                s = 0f;
                t = Mathf.Clamp01(f / e);
            }
            else
            {
                float c = Vector3.Dot(d1, r);
                if (e <= Epsilon)
                {
                    t = 0f;
                    s = Mathf.Clamp01(-c / a);
                }
                else
                {
                    float b = Vector3.Dot(d1, d2);
                    float denominator = a * e - b * b;
                    s = denominator > Epsilon ? Mathf.Clamp01((b * f - c * e) / denominator) : 0f;
                    t = (b * s + f) / e;
                    if (t < 0f)
                    {
                        t = 0f;
                        s = Mathf.Clamp01(-c / a);
                    }
                    else if (t > 1f)
                    {
                        t = 1f;
                        s = Mathf.Clamp01((b - c) / a);
                    }
                }
            }

            point1 = p1 + d1 * s;
            point2 = p2 + d2 * t;
        }

        /// <summary>
        /// 检查数组中的每个向量是否为有限值。
        /// </summary>
        /// <param name="values">需要检查的世界空间向量数组。</param>
        /// <returns>所有分量都不是 NaN 或 Infinity 时返回 true。</returns>
        private static bool AllFinite(Vector3[] values)
        {
            for (int i = 0; i < values.Length; i++)
            {
                Vector3 value = values[i];
                if (!float.IsFinite(value.x) || !float.IsFinite(value.y) || !float.IsFinite(value.z))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 在公共求解入口处拒绝数量不匹配的调用数组，避免静默破坏链状态。
        /// </summary>
        /// <param name="points">待检查的发骨点数组。</param>
        /// <param name="parameterName">异常中使用的参数名。</param>
        private void ValidatePointArray(Vector3[] points, string parameterName)
        {
            if (points == null || points.Length != pointCount)
            {
                throw new System.ArgumentException($"{parameterName} must contain exactly {pointCount} points.", parameterName);
            }
        }
    }
}
