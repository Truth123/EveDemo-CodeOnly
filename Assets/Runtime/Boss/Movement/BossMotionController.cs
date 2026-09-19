// 文件说明：维护 Boss 底层代码位移、Root Motion、MotionWarp 和碰撞裁剪。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 空间位置、攻击落点和穿模保护。
// 设计边界：BossMovementSystem 是正式移动请求入口；本组件只执行底层位移，不决定 AI 出招、HitNode 命中、伤害或受击反应语义。
// 数据来源：攻击 / 反应位移参数只来自 BossMotionWarpProfile；缺少 Profile 或 Action 配置时拒绝位移，不创建硬编码默认配置。
// 碰撞规则：玩家 target 身体不是 Boss 动作位移的硬阻挡；环境碰撞仍通过 CapsuleCast、CharacterController.Move 和重叠恢复处理。

using ProjectEVE.Boss.AI;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// Boss 底层位移执行组件。正式移动请求由 BossMovementSystem 统一提交，本组件负责 CharacterController、Root Motion、CodeDriven 位移、Motion Warp 与重叠恢复。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class BossMotionController : MonoBehaviour
    {
        /// <summary>Boss 身体碰撞体。所有正式身体位移都通过 CharacterController.Move 执行。</summary>
        [SerializeField] private CharacterController characterController;
        /// <summary>Motion Warp 数据资产。缺失时拒绝 Boss 出招，不创建运行时默认位移配置。</summary>
        [SerializeField] private BossMotionWarpProfile motionWarpProfile;
        [SerializeField] private BossPhaseMoveVfx phaseMoveVfx;
        /// <summary>位移与空间预检使用的碰撞层。</summary>
        [SerializeField] private LayerMask collisionMask = ~0;
        /// <summary>运行时自动创建 CharacterController 时使用的高度。</summary>
        [SerializeField] private float defaultControllerHeight = 1.8f;
        /// <summary>运行时自动创建 CharacterController 时使用的半径。</summary>
        [SerializeField] private float defaultControllerRadius = 0.55f;
        /// <summary>单帧最大重叠推出距离。</summary>
        [SerializeField] private float maxDepenetrationPerFrame = 0.35f;
        private readonly RaycastHit[] castHits = new RaycastHit[16];
        private readonly Collider[] overlapHits = new Collider[24];
        private readonly Dictionary<string, float> windowMoveDistances = new Dictionary<string, float>();
        private readonly Dictionary<string, float> windowCurveProgresses = new Dictionary<string, float>();
        private readonly Dictionary<string, Vector3> lockedWarpTargets = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> lockedFacingTargets = new Dictionary<string, Vector3>();
        private readonly Dictionary<string, Vector3> lockedDirections = new Dictionary<string, Vector3>();
        private readonly List<Collider> ignoredTargetColliders = new List<Collider>();
        private readonly BossWarpWindowRuntimeState rootWarpState = new BossWarpWindowRuntimeState();

        private const float AttackPointApproachTolerance = 0.03f;

        private string activeOrbitWindowName = string.Empty;
        private Vector3 activeOrbitCenter;
        private Vector3 activeOrbitStartRadial;
        private Vector3 activeOrbitEndRadial;
        private Vector3 activeOrbitEndPosition;
        private float activeOrbitRadius;
        private float activeOrbitSignedAngle;
        private bool activeOrbitVfxEnded;

        private BossAttackDefinition currentAttack;
        private BossAttackMotionConfig currentConfig;
        private Transform target;
        private Transform collisionIgnoredTarget;
        private BossAttackMotionMode currentMotionMode;
        private float currentAttackElapsed;
        private string currentCodeMoveWindowFilter = string.Empty;
        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            BindReferences();
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
        }

        private void OnValidate()
        {
            defaultControllerHeight = Mathf.Max(0.5f, defaultControllerHeight);
            defaultControllerRadius = Mathf.Max(0.1f, defaultControllerRadius);
            maxDepenetrationPerFrame = Mathf.Max(0.01f, maxDepenetrationPerFrame);
            BindReferences();
        }

        /// <summary>组件停用时恢复本组件主动设置过的 Boss / Target 碰撞忽略关系。</summary>
        private void OnDisable()
        {
            ClearTargetCollisionIgnores();
        }

        /// <summary>
        /// 开始攻击位移上下文，并按攻击 ID 从 BossMotionWarpProfile 读取 RootMotion / CodeMove 配置。
        /// </summary>
        /// <param name="attackDefinition">本次攻击定义；为空时会关闭动作位移。</param>
        /// <param name="targetTransform">本次攻击的玩家目标，用于解析攻击落点、朝向目标和碰撞忽略。</param>
        public void BeginAttack(BossAttackDefinition attackDefinition, Transform targetTransform)
        {
            currentAttack = attackDefinition;
            string actionId = attackDefinition != null ? attackDefinition.AttackId : string.Empty;
            BeginProfileMotion(actionId, string.Empty, targetTransform);
        }

        /// <summary>
        /// 开始反应位移上下文。反应只使用 Profile 中指定的 CodeMove 窗口，不接管攻击命中或 Root Motion。
        /// </summary>
        /// <param name="actionId">Reaction Timeline 引用的 MotionProfile action ID。</param>
        /// <param name="profileWindowName">Reaction Timeline 引用的 CodeMove 窗口名；为空时允许该 action 下所有 CodeMove 窗口。</param>
        /// <param name="targetTransform">反应位移参考目标，通常是玩家。</param>
        /// <returns>找到可用 MotionProfile 配置时返回 true；缺 action、缺 Profile 或缺配置时返回 false。</returns>
        public bool BeginReactionMotion(string actionId, string profileWindowName, Transform targetTransform)
        {
            currentAttack = null;
            return BeginProfileMotion(actionId, profileWindowName, targetTransform);
        }

        /// <summary>结束攻击位移上下文。</summary>
        public void EndAttack()
        {
            EndProfileMotion();
        }

        /// <summary>结束反应位移上下文。</summary>
        public void EndReactionMotion()
        {
            EndProfileMotion();
        }

        /// <summary>
        /// 开始 Profile 驱动的位移上下文，供攻击和反应共享底层 CodeMove / Warp 配置。
        /// </summary>
        /// <param name="actionId">用于查询 BossMotionWarpProfile 的动作 ID。</param>
        /// <param name="profileWindowName">可选 CodeMove 窗口过滤名；为空时不过滤。</param>
        /// <param name="targetTransform">位移目标，用于落点、朝向和碰撞忽略。</param>
        /// <returns>成功读取 MotionProfile action 配置时返回 true；否则返回 false。</returns>
        private bool BeginProfileMotion(
            string actionId,
            string profileWindowName,
            Transform targetTransform)
        {
            target = targetTransform;
            RefreshTargetCollisionIgnores();
            currentAttackElapsed = 0f;
            currentCodeMoveWindowFilter = profileWindowName ?? string.Empty;
            ClearMotionRuntime();
            currentConfig = null;
            if (string.IsNullOrEmpty(actionId))
            {
                currentMotionMode = BossAttackMotionMode.None;
                return false;
            }

            if (motionWarpProfile == null)
            {
                Debug.LogError("BossMotionController requires a BossMotionWarpProfile. Boss attacks and reaction motion are rejected until the asset is assigned.", this);
            }
            else if (!motionWarpProfile.TryGetConfig(actionId, out currentConfig))
            {
                Debug.LogError($"BossMotionWarpProfile missing config for action '{actionId}'. Boss motion is disabled for this action.", this);
            }

            currentMotionMode = currentConfig != null ? currentConfig.MotionMode : BossAttackMotionMode.None;
            return currentConfig != null;
        }

        /// <summary>结束当前 Profile 位移上下文。</summary>
        private void EndProfileMotion()
        {
            currentAttack = null;
            currentConfig = null;
            currentMotionMode = BossAttackMotionMode.None;
            currentAttackElapsed = 0f;
            currentCodeMoveWindowFilter = string.Empty;
            ClearMotionRuntime();
            phaseMoveVfx?.Stop();
        }

        /// <summary>清理 Profile 位移运行时缓存。</summary>
        private void ClearMotionRuntime()
        {
            windowMoveDistances.Clear();
            windowCurveProgresses.Clear();
            lockedWarpTargets.Clear();
            lockedFacingTargets.Clear();
            lockedDirections.Clear();
            rootWarpState.Clear();
            ClearOrbitRuntime();
        }

        /// <summary>
        /// 更新当前目标，用于 Approach / Strafe 等非攻击移动和后续碰撞忽略刷新。
        /// </summary>
        /// <param name="targetTransform">新的玩家目标；为空时清空目标引用。</param>
        public void SetTarget(Transform targetTransform)
        {
            target = targetTransform;
            RefreshTargetCollisionIgnores();
        }

        /// <summary>
        /// 只读检查当前条件是否允许出招。
        /// </summary>
        /// <param name="attackDefinition">候选攻击定义，用其中的 AttackId 查询 MotionProfile。</param>
        /// <param name="targetTransform">候选攻击目标。</param>
        /// <returns>当前空间条件允许启动攻击时返回 true；缺 Profile、缺配置或空间不满足时返回 false。</returns>
        public bool CanStartAttackReadOnly(BossAttackDefinition attackDefinition, Transform targetTransform)
        {
            return EvaluateAttackStartEligibility(attackDefinition, targetTransform);
        }

        /// <summary>
        /// 执行攻击启动空间预筛选，不产生运行时诊断状态或日志。
        /// </summary>
        /// <param name="attackDefinition">候选攻击定义；为空时视为不需要 Motion 预筛选。</param>
        /// <param name="targetTransform">候选攻击目标；为空时视为不需要 Motion 预筛选。</param>
        /// <returns>候选攻击满足 MotionProfile 选择条件和空间条件时返回 true。</returns>
        private bool EvaluateAttackStartEligibility(
            BossAttackDefinition attackDefinition,
            Transform targetTransform)
        {
            if (attackDefinition == null || targetTransform == null)
            {
                return true;
            }

            if (motionWarpProfile == null)
            {
                return false;
            }

            if (!motionWarpProfile.TryGetConfig(attackDefinition.AttackId, out BossAttackMotionConfig config))
            {
                return false;
            }

            if (config.Selection == null)
            {
                return true;
            }

            float distance = HorizontalDistance(transform.position, targetTransform.position);
            BossAttackSelectionProfile selection = config.Selection;
            if (distance < selection.MinStartDistance)
            {
                return false;
            }

            if (distance > selection.MaxStartDistance)
            {
                return false;
            }

            Vector3 toTarget = DirectionTo(targetTransform);
            if (selection.RequiredForwardClearance > 0f && !HasMoveClearance(toTarget, selection.RequiredForwardClearance, targetTransform))
            {
                return false;
            }

            if (selection.RequiredBackwardClearance > 0f && !HasMoveClearance(-toTarget, selection.RequiredBackwardClearance, targetTransform))
            {
                return false;
            }

            return CanUseOrbitWindows(config, targetTransform);
        }

        /// <summary>
        /// 执行普通代码位移，用于 Approach / Strafe 非攻击重定位。
        /// </summary>
        /// <param name="direction">期望移动方向；只使用水平分量。</param>
        /// <param name="speed">移动速度，单位为米每秒。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <returns>经过碰撞裁剪和重叠恢复后的实际水平位移。</returns>
        public Vector3 MoveCodeDriven(Vector3 direction, float speed, float deltaTime)
        {
            Vector3 flatDirection = Flatten(direction);
            if (flatDirection.sqrMagnitude <= 0.0001f || speed <= 0f || deltaTime <= 0f)
            {
                return Vector3.zero;
            }

            Vector3 delta = flatDirection.normalized * speed * deltaTime;
            return MoveWithCharacterController(delta);
        }

        /// <summary>
        /// 推进当前攻击中的代码驱动 Warp 窗口，并执行窗口内声明的 CodeMove 位移。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="attackElapsed">当前攻击经过时间，单位为秒。</param>
        public void TickAttackMotion(float deltaTime, float attackElapsed)
        {
            TickProfileMotion(deltaTime, attackElapsed);
        }

        /// <summary>
        /// 推进反应位移中的代码驱动窗口，并只消费 BeginReactionMotion 指定的窗口过滤。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="reactionElapsed">当前反应状态经过时间，单位为秒。</param>
        public void TickReactionMotion(float deltaTime, float reactionElapsed)
        {
            TickProfileMotion(deltaTime, reactionElapsed);
        }

        /// <summary>
        /// 按当前 Profile 配置推进 CodeMove 窗口，攻击和反应位移共用该入口。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="elapsed">当前攻击或反应时间轴经过时间，单位为秒。</param>
        private void TickProfileMotion(float deltaTime, float elapsed)
        {
            currentAttackElapsed = elapsed;
            if (currentConfig == null || deltaTime <= 0f)
            {
                return;
            }

            for (int i = 0; i < currentConfig.CodeMoveWindows.Length; i++)
            {
                BossCodeMoveWindow window = currentConfig.CodeMoveWindows[i];
                if (window == null ||
                    !ShouldTickCodeMoveWindow(window) ||
                    !window.Contains(elapsed))
                {
                    continue;
                }

                ApplyCodeMoveWindow(window, deltaTime);
            }
        }

        /// <summary>
        /// 判断 CodeMove 窗口是否匹配当前反应位移过滤名。
        /// </summary>
        /// <param name="window">待检查的 CodeMove 窗口。</param>
        /// <returns>未设置过滤名或窗口名与过滤名一致时返回 true。</returns>
        private bool ShouldTickCodeMoveWindow(BossCodeMoveWindow window)
        {
            return string.IsNullOrEmpty(currentCodeMoveWindowFilter) ||
                string.Equals(window.Name, currentCodeMoveWindowFilter, StringComparison.Ordinal);
        }

        /// <summary>
        /// 接收 Animator 子节点转发的 Root Motion，在 RootMotionWarped 攻击上下文中缩放、裁剪并应用到 CharacterController。
        /// </summary>
        /// <param name="deltaPosition">Animator 本帧输出的 Root Motion 位移，只使用水平分量。</param>
        /// <param name="deltaRotation">Animator 本帧输出的 Root Motion 旋转，只消费 yaw。</param>
        public void HandleRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            Vector3 rootMotionDelta = Flatten(deltaPosition);
            if (currentMotionMode != BossAttackMotionMode.RootMotionWarped)
            {
                return;
            }

            Vector3 workingRootDelta = ApplyRootMotionSuppressWindow(rootMotionDelta);
            if (workingRootDelta.sqrMagnitude > 0.000001f)
            {
                Vector3 warpedDelta = BuildRootMotionDelta(workingRootDelta, Time.deltaTime);
                if (warpedDelta.sqrMagnitude > 0.000001f)
                {
                    MoveWithCharacterController(warpedDelta);
                }
            }
            else
            {
                rootWarpState.Clear();
            }

            ApplyRootMotionRotation(deltaRotation, Time.deltaTime);
        }

        /// <summary>
        /// 按当前攻击时间匹配 Root Motion 抑制窗口，并返回抑制后的水平位移。
        /// </summary>
        /// <param name="rootDelta">Animator 本帧 Root Motion 水平位移。</param>
        /// <returns>应用 TranslationScale 后的 Root Motion 位移；无抑制窗口时返回原位移。</returns>
        private Vector3 ApplyRootMotionSuppressWindow(Vector3 rootDelta)
        {
            if (currentConfig == null || currentConfig.RootMotionSuppressWindows == null)
            {
                return rootDelta;
            }

            for (int i = 0; i < currentConfig.RootMotionSuppressWindows.Length; i++)
            {
                BossRootMotionSuppressWindow window = currentConfig.RootMotionSuppressWindows[i];
                if (window == null || !window.Contains(currentAttackElapsed))
                {
                    continue;
                }

                return rootDelta * Mathf.Max(0f, window.TranslationScale);
            }

            return rootDelta;
        }

        /// <summary>
        /// 将 Animator Root Motion 转换为当前 Warp 窗口允许的位移。
        /// </summary>
        /// <param name="rootDelta">抑制后的 Animator Root Motion 水平位移。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <returns>经过窗口级 Warp、StopDistance 保护和单帧上限裁剪后的位移。</returns>
        private Vector3 BuildRootMotionDelta(Vector3 rootDelta, float deltaTime)
        {
            if (currentConfig == null || rootDelta.sqrMagnitude <= 0.000001f)
            {
                return rootDelta;
            }

            BossMotionWarpWindow window = FindActiveWarpWindow(currentAttackElapsed);
            if (window == null)
            {
                rootWarpState.Clear();
                return rootDelta;
            }

            if (window.TargetingMode == BossWarpTargetingMode.OrbitAroundTarget)
            {
                rootWarpState.Clear();
                return rootDelta;
            }

            Vector3 targetPoint = ResolveWarpTarget(window.Name, window.TargetingMode, window.StopDistance, window.FollowTarget);
            if (!rootWarpState.Matches(window))
            {
                rootWarpState.Begin(window, transform, targetPoint);
            }
            else
            {
                rootWarpState.UpdateTarget(window, targetPoint);
            }

            Vector3 warpedDelta = rootWarpState.WarpDelta(window, rootDelta);
            warpedDelta = ClampApproachDeltaToStopDistance(warpedDelta, window);
            warpedDelta = ClampFrameMove(warpedDelta, window.MaxMovePerFrame);

            return warpedDelta;
        }

        /// <summary>
        /// 裁剪朝玩家方向的 Root Motion 分量，避免 ToAttackPoint 窗口越过 StopDistance 继续挤压玩家。
        /// </summary>
        /// <param name="delta">Root Motion Warp 后的本帧位移。</param>
        /// <param name="window">当前 Root Motion Warp 窗口。</param>
        /// <returns>保留横向和远离玩家分量、只裁剪过量接近分量后的位移。</returns>
        private Vector3 ClampApproachDeltaToStopDistance(Vector3 delta, BossMotionWarpWindow window)
        {
            if (window == null || target == null || window.TargetingMode != BossWarpTargetingMode.ToAttackPoint)
            {
                return delta;
            }

            delta = Flatten(delta);
            if (delta.sqrMagnitude <= 0.000001f)
            {
                return delta;
            }

            Vector3 toTarget = Flatten(target.position - transform.position);
            float distance = toTarget.magnitude;
            Vector3 approachDirection = distance > 0.0001f ? toTarget / distance : Flatten(transform.forward).normalized;
            if (approachDirection.sqrMagnitude <= 0.0001f)
            {
                return delta;
            }

            float approachAmount = Vector3.Dot(delta, approachDirection);
            if (approachAmount <= 0f)
            {
                return delta;
            }

            float stopDistance = Mathf.Max(0f, window.StopDistance);
            float deadZone = Mathf.Max(0f, window.DeadZone);
            float allowedApproach = Mathf.Max(0f, distance - stopDistance - deadZone);
            if (approachAmount <= allowedApproach + 0.0001f)
            {
                return delta;
            }

            Vector3 clampDelta = approachDirection * (approachAmount - allowedApproach);
            return delta - clampDelta;
        }

        /// <summary>
        /// 应用一个激活中的 CodeMove 窗口，按目标模式生成位移、转向并累积窗口距离预算。
        /// </summary>
        /// <param name="window">当前时间命中的 CodeMove 窗口。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyCodeMoveWindow(BossCodeMoveWindow window, float deltaTime)
        {
            if (window.TargetingMode == BossWarpTargetingMode.OrbitAroundTarget)
            {
                ApplyOrbitMoveWindow(window, deltaTime);
                return;
            }

            if (IsFacingOnlyWindow(window))
            {
                RotateTowardFacingTarget(window.Name, window.FollowTarget, window.MaxYawSpeed, deltaTime);
                return;
            }

            float remainingDistance = GetRemainingWindowDistance(window.Name, window.MaxMoveDistance);
            if (window.MaxMoveDistance > 0f && remainingDistance <= 0f)
            {
                return;
            }

            Vector3 delta;
            if (window.ProgressMode == BossCodeMoveProgressMode.CurveDistance)
            {
                delta = BuildCurveDistanceDelta(window, remainingDistance);
            }
            else if (window.UseDistanceBand)
            {
                delta = CalculateWarpCorrection(
                    window.Name,
                    window.TargetingMode,
                    window.StopDistance,
                    window.DeadZone,
                    window.MaxMoveDistance,
                    window.MaxMoveSpeed,
                    window.FollowTarget,
                    window.AllowBackwardCorrection,
                    deltaTime,
                    predictedBaseDelta: Vector3.zero,
                    remainingWindowTime: Mathf.Max(0.001f, window.EndTime - currentAttackElapsed));
            }
            else
            {
                Vector3 direction = ResolveWindowDirection(window.Name, window.TargetingMode, window.FollowTarget, window.StopDistance);
                if (direction.sqrMagnitude <= 0.0001f)
                {
                    RotateTowardFacingTarget(window.Name, window.FollowTarget, window.MaxYawSpeed, deltaTime);
                    return;
                }

                float distance = window.MaxMoveSpeed * deltaTime;
                if (window.MaxMoveDistance > 0f)
                {
                    distance = Mathf.Min(distance, remainingDistance);
                }

                delta = direction.normalized * distance;
            }

            if (delta.sqrMagnitude <= 0.000001f)
            {
                RotateTowardFacingTarget(window.Name, window.FollowTarget, window.MaxYawSpeed, deltaTime);
                return;
            }

            RotateTowardFacingTarget(window.Name, window.FollowTarget, window.MaxYawSpeed, deltaTime);
            Vector3 applied = MoveWithCharacterController(delta);
            AccumulateWindowDistance(window.Name, applied.magnitude);
        }

        /// <summary>
        /// 判断窗口是否明确声明为零位移、仅更新目标朝向的 ConstantSpeed 窗口。
        /// </summary>
        /// <param name="window">当前准备执行的 CodeMove 窗口。</param>
        /// <returns>窗口不属于 Orbit、无位移速度和距离预算且配置了旋转速度时返回 true。</returns>
        private static bool IsFacingOnlyWindow(BossCodeMoveWindow window)
        {
            return window != null &&
                window.TargetingMode != BossWarpTargetingMode.OrbitAroundTarget &&
                window.ProgressMode == BossCodeMoveProgressMode.ConstantSpeed &&
                window.MaxMoveSpeed <= 0f &&
                window.MaxMoveDistance <= 0f &&
                window.MaxYawSpeed > 0f;
        }

        /// <summary>
        /// 按曲线进度构建本帧 CodeMove 位移，适用于受击、击倒等非匀速冲击位移。
        /// </summary>
        /// <param name="window">当前 CurveDistance CodeMove 窗口。</param>
        /// <param name="remainingDistance">该窗口剩余可移动距离，单位为米。</param>
        /// <returns>按曲线累计进度差计算出的本帧位移；方向无效或距离耗尽时为零。</returns>
        private Vector3 BuildCurveDistanceDelta(BossCodeMoveWindow window, float remainingDistance)
        {
            Vector3 direction = ResolveWindowDirection(window.Name, window.TargetingMode, window.FollowTarget, window.StopDistance);
            if (direction.sqrMagnitude <= 0.0001f || window.MaxMoveDistance <= 0f)
            {
                return Vector3.zero;
            }

            float normalizedTime = Mathf.Clamp01(Mathf.InverseLerp(window.StartTime, window.EndTime, currentAttackElapsed));
            float currentProgress = window.EvaluateDistance01(normalizedTime);
            string windowName = window.Name ?? string.Empty;
            windowCurveProgresses.TryGetValue(windowName, out float previousProgress);
            float deltaProgress = Mathf.Max(0f, currentProgress - previousProgress);
            windowCurveProgresses[windowName] = Mathf.Max(previousProgress, currentProgress);

            float distance = deltaProgress * Mathf.Max(0f, window.MaxMoveDistance);
            if (window.MaxMoveDistance > 0f)
            {
                distance = Mathf.Min(distance, remainingDistance);
            }

            if (distance <= 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 delta = direction.normalized * distance;
            return delta;
        }

        /// <summary>
        /// 应用 OrbitAroundTarget CodeMove 窗口，沿玩家左右侧圆弧移动并驱动可选相位移动 VFX。
        /// </summary>
        /// <param name="window">当前激活的 Orbit CodeMove 窗口。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyOrbitMoveWindow(BossCodeMoveWindow window, float deltaTime)
        {
            if (target == null)
            {
                return;
            }

            if (!IsOrbitRuntimeActive(window))
            {
                if (!BeginOrbitWindow(window))
                {
                    return;
                }
            }
            else if (window.FollowTarget)
            {
                RefreshOrbitTarget(window);
            }

            float normalizedTime = Mathf.Clamp01(Mathf.InverseLerp(window.StartTime, window.EndTime, currentAttackElapsed));
            float easedTime = Mathf.SmoothStep(0f, 1f, normalizedTime);
            Vector3 desiredPosition = ResolveOrbitPosition(easedTime);
            Vector3 delta = Flatten(desiredPosition - transform.position);

            float frameLimit = Mathf.Max(0f, window.MaxMoveSpeed) * deltaTime;
            delta = ClampFrameMove(delta, frameLimit);

            RotateTowardPosition(target.position, window.MaxYawSpeed, deltaTime);

            if (delta.sqrMagnitude > 0.000001f)
            {
                MoveWithCharacterController(delta);
                phaseMoveVfx?.Tick(transform.position);
            }

            if (normalizedTime >= 0.999f && !activeOrbitVfxEnded)
            {
                phaseMoveVfx?.PlayEnd(transform.position);
                activeOrbitVfxEnded = true;
            }
        }

        /// <summary>
        /// 在出招前检查所有 Orbit 窗口的落点空间，避免选择必然被环境阻挡的环绕招式。
        /// </summary>
        /// <param name="config">候选攻击的 MotionProfile 配置。</param>
        /// <param name="targetTransform">候选攻击目标。</param>
        /// <returns>所有 Orbit 窗口至少有可用落点时返回 true；指定侧或随机两侧都不可用时返回 false。</returns>
        private bool CanUseOrbitWindows(BossAttackMotionConfig config, Transform targetTransform)
        {
            if (config == null || config.CodeMoveWindows == null)
            {
                return true;
            }

            for (int i = 0; i < config.CodeMoveWindows.Length; i++)
            {
                BossCodeMoveWindow window = config.CodeMoveWindows[i];
                if (window == null || window.TargetingMode != BossWarpTargetingMode.OrbitAroundTarget)
                {
                    continue;
                }

                bool leftAvailable = IsOrbitSideAvailable(window, targetTransform, BossOrbitSide.Left);
                bool rightAvailable = IsOrbitSideAvailable(window, targetTransform, BossOrbitSide.Right);
                if (window.OrbitSide == BossOrbitSide.Random)
                {
                    if (!leftAvailable && !rightAvailable)
                    {
                        return false;
                    }

                    continue;
                }

                if (window.OrbitSide == BossOrbitSide.Left && !leftAvailable)
                {
                    return false;
                }

                if (window.OrbitSide == BossOrbitSide.Right && !rightAvailable)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 初始化 Orbit 窗口运行时状态，锁定本次环绕侧、圆心、开始半径和角度终点。
        /// </summary>
        /// <param name="window">即将开始的 Orbit CodeMove 窗口。</param>
        /// <returns>成功选择可用环绕侧并完成初始化时返回 true；两侧都不可用时返回 false。</returns>
        private bool BeginOrbitWindow(BossCodeMoveWindow window)
        {
            if (!TryResolveOrbitSide(window, out BossOrbitSide selectedSide))
            {
                return false;
            }

            activeOrbitWindowName = window.Name;
            activeOrbitCenter = ResolveOrbitCenter(target);

            Vector3 startOffset = Flatten(transform.position - activeOrbitCenter);
            activeOrbitRadius = Mathf.Max(0.1f, startOffset.magnitude);
            activeOrbitStartRadial = startOffset.sqrMagnitude > 0.0001f
                ? startOffset.normalized
                : ResolveOrbitFallbackStartRadial(target, selectedSide);
            activeOrbitSignedAngle = ResolveOrbitSignedAngle(window, selectedSide);
            activeOrbitEndRadial = ResolveOrbitEndRadial(activeOrbitStartRadial, activeOrbitSignedAngle);
            activeOrbitEndPosition = activeOrbitCenter + activeOrbitEndRadial * activeOrbitRadius;
            activeOrbitEndPosition.y = transform.position.y;
            activeOrbitVfxEnded = false;

            phaseMoveVfx?.PlayBegin(transform.position, activeOrbitEndPosition);
            return true;
        }

        /// <summary>
        /// 判断传入 Orbit 窗口是否已经拥有本轮运行时缓存。
        /// </summary>
        /// <param name="window">待检查的 Orbit CodeMove 窗口。</param>
        /// <returns>当前缓存窗口名与传入窗口名一致时返回 true。</returns>
        private bool IsOrbitRuntimeActive(BossCodeMoveWindow window)
        {
            return !string.IsNullOrEmpty(activeOrbitWindowName) && activeOrbitWindowName == window.Name;
        }

        /// <summary>
        /// 在 FollowTarget Orbit 窗口中刷新圆心和终点；半径与角度仍使用窗口开始时锁定的值。
        /// </summary>
        /// <param name="window">当前运行中的 Orbit CodeMove 窗口。</param>
        private void RefreshOrbitTarget(BossCodeMoveWindow window)
        {
            activeOrbitCenter = ResolveOrbitCenter(target);
            activeOrbitEndPosition = activeOrbitCenter + activeOrbitEndRadial * activeOrbitRadius;
            activeOrbitEndPosition.y = transform.position.y;
        }

        /// <summary>
        /// 根据 Orbit 归一化进度计算本帧期望世界位置。
        /// </summary>
        /// <param name="normalizedTime">窗口内归一化时间，范围 0 到 1。</param>
        /// <returns>沿锁定圆弧和半径插值得到的 Boss 期望世界坐标。</returns>
        private Vector3 ResolveOrbitPosition(float normalizedTime)
        {
            Vector3 radial = Quaternion.AngleAxis(activeOrbitSignedAngle * normalizedTime, Vector3.up) * activeOrbitStartRadial;
            Vector3 position = activeOrbitCenter + radial.normalized * activeOrbitRadius;
            position.y = transform.position.y;
            return position;
        }

        /// <summary>
        /// 为 Orbit 窗口选择本次使用的左右侧，Random 只在窗口开始时随机一次。
        /// </summary>
        /// <param name="window">需要选择侧向的 Orbit CodeMove 窗口。</param>
        /// <param name="side">写回最终使用的侧向；失败时为 Random。</param>
        /// <returns>指定侧或随机出的侧有足够落点空间时返回 true。</returns>
        private bool TryResolveOrbitSide(BossCodeMoveWindow window, out BossOrbitSide side)
        {
            side = window.OrbitSide;
            if (window.OrbitSide != BossOrbitSide.Random)
            {
                return IsOrbitSideAvailable(window, target, side);
            }

            bool leftAvailable = IsOrbitSideAvailable(window, target, BossOrbitSide.Left);
            bool rightAvailable = IsOrbitSideAvailable(window, target, BossOrbitSide.Right);
            if (!leftAvailable && !rightAvailable)
            {
                side = BossOrbitSide.Random;
                return false;
            }

            if (leftAvailable && rightAvailable)
            {
                side = UnityEngine.Random.value < 0.5f ? BossOrbitSide.Left : BossOrbitSide.Right;
                return true;
            }

            side = leftAvailable ? BossOrbitSide.Left : BossOrbitSide.Right;
            return true;
        }

        /// <summary>
        /// 判断目标某一侧的 Orbit 落点是否能容纳 Boss CharacterController。
        /// </summary>
        /// <param name="window">提供 OrbitAngleDegrees 的 Orbit CodeMove 窗口。</param>
        /// <param name="targetTransform">Orbit 圆心参考目标。</param>
        /// <param name="side">要检查的目标左侧或右侧。</param>
        /// <returns>目标存在且对应落点没有环境重叠时返回 true。</returns>
        private bool IsOrbitSideAvailable(BossCodeMoveWindow window, Transform targetTransform, BossOrbitSide side)
        {
            if (targetTransform == null)
            {
                return false;
            }

            Vector3 landingPoint = ResolveOrbitLandingPoint(window, targetTransform, side);
            return HasControllerSpaceAt(landingPoint, targetTransform, ignoreTarget: true);
        }

        /// <summary>
        /// 计算 Orbit 窗口在目标指定侧的终点坐标。
        /// </summary>
        /// <param name="targetTransform">Orbit 圆心参考目标。</param>
        /// <param name="window">提供本次预筛选角度的 Orbit CodeMove 窗口。</param>
        /// <param name="side">目标左侧或右侧。</param>
        /// <returns>与 Boss 当前高度对齐后的世界落点。</returns>
        private Vector3 ResolveOrbitLandingPoint(BossCodeMoveWindow window, Transform targetTransform, BossOrbitSide side)
        {
            Vector3 center = ResolveOrbitCenter(targetTransform);
            Vector3 startOffset = Flatten(transform.position - center);
            float radius = Mathf.Max(0.1f, startOffset.magnitude);
            Vector3 startRadial = startOffset.sqrMagnitude > 0.0001f
                ? startOffset.normalized
                : ResolveOrbitFallbackStartRadial(targetTransform, side);
            Vector3 radial = ResolveOrbitEndRadial(startRadial, ResolveOrbitSignedAngle(window, side));
            Vector3 landingPoint = center + radial * radius;
            landingPoint.y = transform.position.y;
            return landingPoint;
        }

        /// <summary>
        /// 根据 Orbit 侧向和配置角度得到本窗口的水平旋转角度。
        /// </summary>
        /// <param name="window">提供 OrbitAngleDegrees 的 Orbit CodeMove 窗口。</param>
        /// <param name="side">本次 Orbit 使用的左侧或右侧。</param>
        /// <returns>右侧为正、左侧为负的水平旋转角度。</returns>
        private static float ResolveOrbitSignedAngle(BossCodeMoveWindow window, BossOrbitSide side)
        {
            float angle = window != null ? Mathf.Max(0f, window.OrbitAngleDegrees) : 0f;
            return side == BossOrbitSide.Left ? -angle : angle;
        }

        /// <summary>
        /// 按签名角度旋转起始径向，得到 Orbit 终点径向。
        /// </summary>
        /// <param name="startRadial">窗口开始时 Boss 相对目标的水平单位方向。</param>
        /// <param name="signedAngle">右侧为正、左侧为负的旋转角度。</param>
        /// <returns>旋转后的水平单位方向。</returns>
        private static Vector3 ResolveOrbitEndRadial(Vector3 startRadial, float signedAngle)
        {
            Vector3 radial = Quaternion.AngleAxis(signedAngle, Vector3.up) * startRadial;
            return radial.sqrMagnitude > 0.0001f ? radial.normalized : Vector3.forward;
        }

        /// <summary>
        /// Boss 与目标重合时提供稳定的 Orbit 起始径向，避免无法计算角度终点。
        /// </summary>
        /// <param name="targetTransform">Orbit 圆心参考目标。</param>
        /// <param name="side">本次 Orbit 使用的左侧或右侧。</param>
        /// <returns>用于替代零长度起始偏移的水平单位方向。</returns>
        private Vector3 ResolveOrbitFallbackStartRadial(Transform targetTransform, BossOrbitSide side)
        {
            Vector3 forward = targetTransform != null ? Flatten(targetTransform.forward) : Vector3.zero;
            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = Flatten(transform.forward);
            }

            if (forward.sqrMagnitude <= 0.0001f)
            {
                forward = side == BossOrbitSide.Left ? Vector3.right : Vector3.left;
            }

            return forward.normalized;
        }

        /// <summary>
        /// 解析 Orbit 圆心，并把高度对齐到 Boss 当前水平面。
        /// </summary>
        /// <param name="targetTransform">Orbit 圆心参考目标；为空时使用 Boss 当前坐标。</param>
        /// <returns>用于水平环绕计算的世界圆心。</returns>
        private Vector3 ResolveOrbitCenter(Transform targetTransform)
        {
            Vector3 center = targetTransform != null ? targetTransform.position : transform.position;
            center.y = transform.position.y;
            return center;
        }

        /// <summary>
        /// 计算 CodeDriven 距离带窗口的本帧修正位移，用于在窗口时间内逐步靠近目标落点。
        /// </summary>
        /// <param name="windowName">当前 CodeMove 窗口名，用于锁定目标和累计距离。</param>
        /// <param name="targetingMode">窗口目标解析模式。</param>
        /// <param name="stopDistance">ToAttackPoint 模式希望保留的玩家距离，单位为米。</param>
        /// <param name="deadZone">距离误差死区，单位为米。</param>
        /// <param name="maxCorrectionDistance">该窗口允许的最大修正距离，单位为米；小于等于 0 表示不限制。</param>
        /// <param name="maxCorrectionSpeed">该窗口允许的最大修正速度，单位为米每秒；小于等于 0 表示不按速度限制。</param>
        /// <param name="followTarget">是否每帧刷新目标点或方向。</param>
        /// <param name="allowBackwardCorrection">ToAttackPoint 过近时是否允许反向修正。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="predictedBaseDelta">同帧已有基础位移，用于预估修正前的位置。</param>
        /// <param name="remainingWindowTime">窗口剩余时间，单位为秒。</param>
        /// <returns>本帧允许应用的水平修正位移；目标无效、误差在死区内或预算耗尽时为零。</returns>
        private Vector3 CalculateWarpCorrection(
            string windowName,
            BossWarpTargetingMode targetingMode,
            float stopDistance,
            float deadZone,
            float maxCorrectionDistance,
            float maxCorrectionSpeed,
            bool followTarget,
            bool allowBackwardCorrection,
            float deltaTime,
            Vector3 predictedBaseDelta,
            float remainingWindowTime)
        {
            if (target == null)
            {
                return Vector3.zero;
            }

            Vector3 targetPoint = ResolveWarpTarget(windowName, targetingMode, stopDistance, followTarget);
            Vector3 predictedPosition = transform.position + Flatten(predictedBaseDelta);
            Vector3 error = Flatten(targetPoint - predictedPosition);
            float distanceError = ResolveDistanceError(stopDistance);

            if (targetingMode == BossWarpTargetingMode.ToAttackPoint)
            {
                if (Mathf.Abs(distanceError) <= deadZone)
                {
                    return Vector3.zero;
                }

                if (distanceError < -deadZone && !allowBackwardCorrection)
                {
                    return Vector3.zero;
                }
            }

            if (targetingMode == BossWarpTargetingMode.AwayFromTarget && error.sqrMagnitude <= 0.0001f)
            {
                error = ResolveWindowDirection(windowName, targetingMode, followTarget, stopDistance);
            }

            if (error.sqrMagnitude <= 0.000001f)
            {
                return Vector3.zero;
            }

            Vector3 desired = error * Mathf.Clamp01(deltaTime / Mathf.Max(0.001f, remainingWindowTime));
            float speedLimit = Mathf.Max(0f, maxCorrectionSpeed) * deltaTime;
            if (speedLimit > 0f && desired.magnitude > speedLimit)
            {
                desired = desired.normalized * speedLimit;
            }

            float remainingDistance = GetRemainingWindowDistance(windowName, maxCorrectionDistance);
            if (maxCorrectionDistance > 0f && desired.magnitude > remainingDistance)
            {
                desired = desired.normalized * remainingDistance;
            }

            if (targetingMode == BossWarpTargetingMode.ToAttackPoint && !allowBackwardCorrection)
            {
                Vector3 toTarget = DirectionToTarget();
                if (Vector3.Dot(desired, toTarget) < 0f)
                {
                    desired = Vector3.zero;
                }
            }

            return desired;
        }

        /// <summary>
        /// 按单帧位移上限裁剪水平位移。
        /// </summary>
        /// <param name="delta">待裁剪的位移。</param>
        /// <param name="maxMovePerFrame">本帧最大允许位移，单位为米；小于等于 0 表示不限制。</param>
        /// <returns>未超过上限时返回原位移；超过上限时返回同方向、长度为上限的位移。</returns>
        private static Vector3 ClampFrameMove(Vector3 delta, float maxMovePerFrame)
        {
            if (maxMovePerFrame <= 0f || delta.magnitude <= maxMovePerFrame)
            {
                return delta;
            }

            return delta.normalized * maxMovePerFrame;
        }

        /// <summary>
        /// 查找当前攻击时间命中的 Root Motion Warp 窗口。
        /// </summary>
        /// <param name="attackElapsed">当前攻击经过时间，单位为秒。</param>
        /// <returns>第一个包含该时间点的 Warp 窗口；没有匹配时返回 null。</returns>
        private BossMotionWarpWindow FindActiveWarpWindow(float attackElapsed)
        {
            for (int i = 0; i < currentConfig.WarpWindows.Length; i++)
            {
                BossMotionWarpWindow window = currentConfig.WarpWindows[i];
                if (window != null && window.Contains(attackElapsed))
                {
                    return window;
                }
            }

            return null;
        }

        /// <summary>
        /// 解析位移落点。ToAttackPoint 只允许从远处向玩家方向接近，不会在过近时生成 Boss 身后的反向落点。
        /// </summary>
        /// <param name="windowName">当前 MotionWarp 或 CodeMove 窗口名，用于锁定非跟随窗口的落点。</param>
        /// <param name="targetingMode">当前窗口的目标解析模式。</param>
        /// <param name="stopDistance">Boss 从远处接近玩家时希望保留的距离，单位为米。</param>
        /// <param name="followTarget">是否每帧跟随玩家当前位置刷新落点。</param>
        /// <returns>当前窗口本帧使用的世界位移落点。</returns>
        private Vector3 ResolveWarpTarget(string windowName, BossWarpTargetingMode targetingMode, float stopDistance, bool followTarget)
        {
            if (!followTarget && lockedWarpTargets.TryGetValue(windowName, out Vector3 lockedTarget))
            {
                return lockedTarget;
            }

            Vector3 resolved;
            switch (targetingMode)
            {
                case BossWarpTargetingMode.AwayFromTarget:
                    resolved = transform.position + ResolveWindowDirection(windowName, targetingMode, followTarget, stopDistance);
                    break;
                case BossWarpTargetingMode.FixedForward:
                case BossWarpTargetingMode.Strafe:
                    resolved = transform.position + ResolveWindowDirection(windowName, targetingMode, followTarget, stopDistance);
                    break;
                default:
                    resolved = ResolveAttackTargetPoint(stopDistance);
                    break;
            }

            if (!followTarget)
            {
                lockedWarpTargets[windowName] = resolved;
            }

            return resolved;
        }

        /// <summary>
        /// 解析代码位移方向。ToAttackPoint 在距离不足时返回零，避免无视 StopDistance 继续向前挤压玩家。
        /// </summary>
        /// <param name="windowName">当前 CodeMove 窗口名，用于锁定非跟随窗口方向。</param>
        /// <param name="targetingMode">当前窗口的方向解析模式。</param>
        /// <param name="followTarget">是否每帧跟随玩家当前位置刷新方向。</param>
        /// <param name="stopDistance">ToAttackPoint 模式下希望保留的玩家前方距离，单位为米。</param>
        /// <returns>允许移动时返回单位方向；距离不足或目标无效时按模式返回零或 fallback 方向。</returns>
        private Vector3 ResolveWindowDirection(string windowName, BossWarpTargetingMode targetingMode, bool followTarget, float stopDistance = 0f)
        {
            if (!followTarget && lockedDirections.TryGetValue(windowName, out Vector3 lockedDirection))
            {
                return lockedDirection;
            }

            Vector3 direction;
            switch (targetingMode)
            {
                case BossWarpTargetingMode.AwayFromTarget:
                    direction = -DirectionToTarget();
                    break;
                case BossWarpTargetingMode.Strafe:
                    direction = Vector3.Cross(DirectionToTarget(), Vector3.up);
                    break;
                case BossWarpTargetingMode.FixedForward:
                    direction = Flatten(transform.forward);
                    break;
                default:
                    direction = ResolveAttackApproachDirection(stopDistance);
                    break;
            }

            if (direction.sqrMagnitude <= 0.0001f)
            {
                return targetingMode == BossWarpTargetingMode.ToAttackPoint
                    ? Vector3.zero
                    : Flatten(transform.forward).normalized;
            }

            direction = direction.normalized;
            if (!followTarget)
            {
                lockedDirections[windowName] = direction;
            }

            return direction;
        }

        /// <summary>
        /// 计算 Boss 接近玩家时的攻击落点。玩家已经小于 StopDistance 时保持当前位置，避免反向穿到玩家另一侧。
        /// </summary>
        /// <param name="stopDistance">Boss 从远处接近玩家时希望保留的距离，单位为米。</param>
        /// <returns>同侧约束后的攻击落点；距离不足时为 Boss 当前水平位置。</returns>
        private Vector3 ResolveAttackTargetPoint(float stopDistance)
        {
            if (target == null)
            {
                return transform.position;
            }

            Vector3 toTarget = Flatten(target.position - transform.position);
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f)
            {
                return transform.position;
            }

            float approachDistance = distance - Mathf.Max(0f, stopDistance);
            if (approachDistance <= AttackPointApproachTolerance)
            {
                return transform.position;
            }

            Vector3 targetPoint = transform.position + toTarget.normalized * approachDistance;
            targetPoint.y = transform.position.y;
            return targetPoint;
        }

        /// <summary>
        /// 根据 StopDistance 判断 ToAttackPoint 窗口是否仍能向玩家方向接近。
        /// </summary>
        /// <param name="stopDistance">攻击希望停在玩家前方的距离，单位为米。</param>
        /// <returns>允许接近时返回 Boss 指向玩家的单位方向；距离不足时返回零向量。</returns>
        private Vector3 ResolveAttackApproachDirection(float stopDistance)
        {
            if (target == null)
            {
                Vector3 fallback = Flatten(transform.forward);
                return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
            }

            Vector3 toTarget = Flatten(target.position - transform.position);
            float distance = toTarget.magnitude;
            if (distance <= 0.0001f || distance - Mathf.Max(0f, stopDistance) <= AttackPointApproachTolerance)
            {
                return Vector3.zero;
            }

            return toTarget / distance;
        }

        /// <summary>
        /// 计算 Boss 与目标之间相对 StopDistance 的水平距离误差。
        /// </summary>
        /// <param name="stopDistance">期望保留的目标距离，单位为米。</param>
        /// <returns>当前水平距离减去 StopDistance；正数表示偏远，负数表示偏近。</returns>
        private float ResolveDistanceError(float stopDistance)
        {
            if (target == null)
            {
                return 0f;
            }

            return HorizontalDistance(transform.position, target.position) - Mathf.Max(0f, stopDistance);
        }

        /// <summary>
        /// 查询某个窗口在本次动作上下文中剩余的移动距离预算。
        /// </summary>
        /// <param name="windowName">窗口名，作为累计距离字典的键。</param>
        /// <param name="maxDistance">窗口最大移动距离，单位为米；小于等于 0 表示无限预算。</param>
        /// <returns>剩余可移动距离；无限预算时返回 float.MaxValue。</returns>
        private float GetRemainingWindowDistance(string windowName, float maxDistance)
        {
            if (maxDistance <= 0f)
            {
                return float.MaxValue;
            }

            windowMoveDistances.TryGetValue(windowName, out float movedDistance);
            return Mathf.Max(0f, maxDistance - movedDistance);
        }

        /// <summary>
        /// 累计窗口已经实际应用的移动距离，用于后续距离预算裁剪。
        /// </summary>
        /// <param name="windowName">窗口名，作为累计距离字典的键。</param>
        /// <param name="movedDistance">本帧实际应用位移长度，单位为米。</param>
        private void AccumulateWindowDistance(string windowName, float movedDistance)
        {
            if (movedDistance <= 0f)
            {
                return;
            }

            windowMoveDistances.TryGetValue(windowName, out float currentDistance);
            windowMoveDistances[windowName] = currentDistance + movedDistance;
        }

        /// <summary>
        /// 让 Boss 身体胶囊忽略当前目标的非 trigger 身体碰撞，避免玩家身体卡住 Boss 攻击、后撤或突进位移。
        /// </summary>
        private void RefreshTargetCollisionIgnores()
        {
            if (collisionIgnoredTarget == target && ignoredTargetColliders.Count > 0)
            {
                return;
            }

            ClearTargetCollisionIgnores();
            collisionIgnoredTarget = target;
            if (characterController == null || target == null || !characterController.enabled)
            {
                return;
            }

            Collider[] targetColliders = target.GetComponentsInChildren<Collider>(false);
            for (int i = 0; i < targetColliders.Length; i++)
            {
                Collider candidate = targetColliders[i];
                if (candidate == null ||
                    candidate.isTrigger ||
                    !candidate.enabled ||
                    candidate.transform == transform ||
                    candidate.transform.IsChildOf(transform))
                {
                    continue;
                }

                Physics.IgnoreCollision(characterController, candidate, true);
                ignoredTargetColliders.Add(candidate);
            }
        }

        /// <summary>
        /// 恢复 Boss 身体胶囊和旧目标身体 Collider 的碰撞关系，避免换目标或停用组件后留下全局忽略。
        /// </summary>
        private void ClearTargetCollisionIgnores()
        {
            if (characterController != null)
            {
                for (int i = 0; i < ignoredTargetColliders.Count; i++)
                {
                    Collider ignored = ignoredTargetColliders[i];
                    if (ignored != null)
                    {
                        Physics.IgnoreCollision(characterController, ignored, false);
                    }
                }
            }

            ignoredTargetColliders.Clear();
            collisionIgnoredTarget = null;
        }

        /// <summary>
        /// 通过 CharacterController 执行正式位移，并在移动后处理残留重叠。
        /// </summary>
        /// <param name="requestedDelta">请求应用的世界位移；只使用水平分量。</param>
        /// <returns>CharacterController 和重叠恢复后实际产生的水平位移。</returns>
        private Vector3 MoveWithCharacterController(Vector3 requestedDelta)
        {
            requestedDelta = Flatten(requestedDelta);
            if (requestedDelta.sqrMagnitude <= 0.000001f)
            {
                return Vector3.zero;
            }

            BindReferences();
            RefreshTargetCollisionIgnores();

            requestedDelta = ClipDeltaByCapsuleCast(requestedDelta);
            if (requestedDelta.sqrMagnitude <= 0.000001f)
            {
                ResolveOverlaps();
                return Vector3.zero;
            }

            Vector3 before = transform.position;
            characterController?.Move(requestedDelta);
            Vector3 applied = transform.position - before;

            ResolveOverlaps();
            return applied;
        }

        /// <summary>
        /// 在位移后处理环境残留重叠，并用 ComputePenetration 将 Boss 胶囊推出。
        /// </summary>
        private void ResolveOverlaps()
        {
            if (characterController == null)
            {
                return;
            }

            GetControllerCapsule(out Vector3 point1, out Vector3 point2, out float radius);
            int hitCount = Physics.OverlapCapsuleNonAlloc(point1, point2, radius, overlapHits, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider other = overlapHits[i];
                if (other == null || IsIgnoredCollider(other, target, ignoreTarget: true))
                {
                    continue;
                }

                if (!Physics.ComputePenetration(
                    characterController,
                    transform.position,
                    transform.rotation,
                    other,
                    other.transform.position,
                    other.transform.rotation,
                    out Vector3 direction,
                    out float distance))
                {
                    continue;
                }

                Vector3 push = Flatten(direction) * Mathf.Min(distance, maxDepenetrationPerFrame);
                if (push.sqrMagnitude <= 0.000001f)
                {
                    continue;
                }

                characterController.Move(push);
            }
        }

        /// <summary>
        /// 使用 Boss 胶囊体预扫指定方向，判断该距离内是否有环境阻挡。
        /// </summary>
        /// <param name="direction">待检查方向；只使用水平分量。</param>
        /// <param name="distance">预扫距离，单位为米。</param>
        /// <param name="targetTransform">当前玩家目标；其身体 Collider 可按规则忽略。</param>
        /// <returns>方向无效、距离为零或没有环境阻挡时返回 true；命中环境阻挡时返回 false。</returns>
        private bool HasMoveClearance(Vector3 direction, float distance, Transform targetTransform)
        {
            direction = Flatten(direction);
            if (direction.sqrMagnitude <= 0.0001f || distance <= 0f)
            {
                return true;
            }

            GetControllerCapsule(out Vector3 point1, out Vector3 point2, out float radius);
            int hitCount = Physics.CapsuleCastNonAlloc(
                point1,
                point2,
                radius,
                direction.normalized,
                castHits,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = castHits[i].collider;
                if (hitCollider == null || IsIgnoredCollider(hitCollider, targetTransform, ignoreTarget: true))
                {
                    continue;
                }

                return false;
            }

            return true;
        }

        /// <summary>
        /// 判断 Boss 胶囊体放置到指定 rootPosition 时是否有足够空间。
        /// </summary>
        /// <param name="rootPosition">待检查的 Boss 根节点世界坐标。</param>
        /// <param name="targetTransform">当前玩家目标；可按 ignoreTarget 决定是否忽略其身体 Collider。</param>
        /// <param name="ignoreTarget">是否忽略 targetTransform 及其子物体上的非 trigger Collider。</param>
        /// <returns>没有环境穿插时返回 true；会与环境产生有效穿插时返回 false。</returns>
        private bool HasControllerSpaceAt(Vector3 rootPosition, Transform targetTransform, bool ignoreTarget)
        {
            GetControllerCapsuleAt(rootPosition, out Vector3 point1, out Vector3 point2, out float radius);
            int hitCount = Physics.OverlapCapsuleNonAlloc(point1, point2, radius, overlapHits, collisionMask, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = overlapHits[i];
                if (hitCollider == null || IsIgnoredCollider(hitCollider, targetTransform, ignoreTarget))
                {
                    continue;
                }

                if (characterController == null)
                {
                    return false;
                }

                if (Physics.ComputePenetration(
                    characterController,
                    rootPosition,
                    transform.rotation,
                    hitCollider,
                    hitCollider.transform.position,
                    hitCollider.transform.rotation,
                    out _,
                    out float distance) && distance > 0.001f)
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 在正式 Move 前用 CapsuleCast 裁剪请求位移，避免穿过环境阻挡。
        /// </summary>
        /// <param name="requestedDelta">原始请求位移。</param>
        /// <returns>未命中环境时返回原位移；命中时返回裁剪到最近安全距离的位移。</returns>
        private Vector3 ClipDeltaByCapsuleCast(Vector3 requestedDelta)
        {
            if (characterController == null)
            {
                return requestedDelta;
            }

            float distance = requestedDelta.magnitude;
            if (distance <= 0.0001f)
            {
                return Vector3.zero;
            }

            Vector3 direction = requestedDelta / distance;
            GetControllerCapsule(out Vector3 point1, out Vector3 point2, out float radius);
            int hitCount = Physics.CapsuleCastNonAlloc(
                point1,
                point2,
                radius,
                direction,
                castHits,
                distance,
                collisionMask,
                QueryTriggerInteraction.Ignore);

            float closestDistance = distance;
            bool blocked = false;
            for (int i = 0; i < hitCount; i++)
            {
                Collider hitCollider = castHits[i].collider;
                if (hitCollider == null || IsIgnoredCollider(hitCollider, target, ignoreTarget: true))
                {
                    continue;
                }

                closestDistance = Mathf.Min(closestDistance, Mathf.Max(0f, castHits[i].distance - 0.02f));
                blocked = true;
            }

            if (!blocked)
            {
                return requestedDelta;
            }

            return direction * closestDistance;
        }

        /// <summary>
        /// 判断 Collider 是否应在 Boss 位移空间检测中忽略。
        /// </summary>
        /// <param name="candidate">待检查的 Collider。</param>
        /// <param name="targetTransform">当前玩家目标。</param>
        /// <param name="ignoreTarget">是否忽略目标及其子物体上的 Collider。</param>
        /// <returns>空引用、trigger、自身 Collider 或允许忽略的目标 Collider 返回 true；环境阻挡返回 false。</returns>
        private bool IsIgnoredCollider(Collider candidate, Transform targetTransform, bool ignoreTarget)
        {
            if (candidate == null || candidate.isTrigger)
            {
                return true;
            }

            if (candidate.transform == transform || candidate.transform.IsChildOf(transform))
            {
                return true;
            }

            if (ignoreTarget && targetTransform != null && (candidate.transform == targetTransform || candidate.transform.IsChildOf(targetTransform)))
            {
                return true;
            }

            return false;
        }

        /// <summary>
        /// 读取当前 CharacterController 的世界胶囊端点和半径。
        /// </summary>
        /// <param name="point1">写回胶囊上端世界坐标。</param>
        /// <param name="point2">写回胶囊下端世界坐标。</param>
        /// <param name="radius">写回胶囊半径，单位为米。</param>
        private void GetControllerCapsule(out Vector3 point1, out Vector3 point2, out float radius)
        {
            BindReferences();
            radius = characterController != null ? characterController.radius : defaultControllerRadius;
            float height = characterController != null ? Mathf.Max(characterController.height, radius * 2f) : defaultControllerHeight;
            Vector3 center = transform.TransformPoint(characterController != null ? characterController.center : Vector3.up * (height * 0.5f));
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            point1 = center + Vector3.up * halfSegment;
            point2 = center - Vector3.up * halfSegment;
        }

        /// <summary>
        /// 按指定根节点位置计算 CharacterController 胶囊端点和半径。
        /// </summary>
        /// <param name="rootPosition">假设的 Boss 根节点世界坐标。</param>
        /// <param name="point1">写回胶囊上端世界坐标。</param>
        /// <param name="point2">写回胶囊下端世界坐标。</param>
        /// <param name="radius">写回胶囊半径，单位为米。</param>
        private void GetControllerCapsuleAt(Vector3 rootPosition, out Vector3 point1, out Vector3 point2, out float radius)
        {
            BindReferences();
            radius = characterController != null ? characterController.radius : defaultControllerRadius;
            float height = characterController != null ? Mathf.Max(characterController.height, radius * 2f) : defaultControllerHeight;
            Vector3 localCenter = characterController != null ? characterController.center : Vector3.up * (height * 0.5f);
            Vector3 center = rootPosition + transform.rotation * localCenter;
            float halfSegment = Mathf.Max(0f, height * 0.5f - radius);
            point1 = center + Vector3.up * halfSegment;
            point2 = center - Vector3.up * halfSegment;
        }

        /// <summary>
        /// 应用 Animator Root Motion 的 yaw 旋转，并在 Warp 窗口中追加面向目标的受限转向。
        /// </summary>
        /// <param name="deltaRotation">Animator 本帧 Root Motion 旋转。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyRootMotionRotation(Quaternion deltaRotation, float deltaTime)
        {
            Vector3 euler = deltaRotation.eulerAngles;
            float rootYaw = Mathf.DeltaAngle(0f, euler.y);
            if (Mathf.Abs(rootYaw) > 0.001f)
            {
                transform.rotation = Quaternion.Euler(0f, transform.rotation.eulerAngles.y + rootYaw, 0f);
            }

            BossMotionWarpWindow window = FindActiveWarpWindow(currentAttackElapsed);
            if (window != null)
            {
                RotateTowardFacingTarget(window.Name, window.FollowTarget, window.MaxYawSpeed, deltaTime);
            }
        }

        /// <summary>
        /// 将攻击期朝向转向玩家身体或窗口开始时锁定的玩家位置，不再朝 StopDistance 落点转向。
        /// </summary>
        /// <param name="windowName">当前 MotionWarp 或 CodeMove 窗口名，用于锁定非跟随窗口的朝向。</param>
        /// <param name="followTarget">是否每帧跟随玩家当前位置刷新朝向目标。</param>
        /// <param name="maxYawSpeed">每秒最大旋转角速度，单位为度。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void RotateTowardFacingTarget(string windowName, bool followTarget, float maxYawSpeed, float deltaTime)
        {
            if (maxYawSpeed <= 0f || target == null)
            {
                return;
            }

            Vector3 facingTarget = ResolveFacingTarget(windowName, followTarget);
            Vector3 direction = Flatten(facingTarget - transform.position);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = DirectionToTarget();
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxYawSpeed * deltaTime);
        }

        /// <summary>
        /// 解析当前窗口的朝向目标。FollowTarget 关闭时锁定窗口首次看到的玩家位置。
        /// </summary>
        /// <param name="windowName">当前 MotionWarp 或 CodeMove 窗口名。</param>
        /// <param name="followTarget">是否每帧跟随玩家当前位置。</param>
        /// <returns>Boss 当前窗口应面向的世界坐标。</returns>
        private Vector3 ResolveFacingTarget(string windowName, bool followTarget)
        {
            string key = windowName ?? string.Empty;
            if (!followTarget && lockedFacingTargets.TryGetValue(key, out Vector3 lockedFacingTarget))
            {
                return lockedFacingTarget;
            }

            Vector3 facingTarget = target != null ? target.position : transform.position + transform.forward;
            facingTarget.y = transform.position.y;
            if (!followTarget)
            {
                lockedFacingTargets[key] = facingTarget;
            }

            return facingTarget;
        }

        /// <summary>
        /// 将 Boss 水平朝向限制速度地转向指定世界坐标。
        /// </summary>
        /// <param name="worldPosition">期望面向的世界坐标。</param>
        /// <param name="maxYawSpeed">每秒最大旋转角速度，单位为度。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void RotateTowardPosition(Vector3 worldPosition, float maxYawSpeed, float deltaTime)
        {
            if (maxYawSpeed <= 0f)
            {
                return;
            }

            Vector3 direction = Flatten(worldPosition - transform.position);
            if (direction.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, maxYawSpeed * deltaTime);
        }

        /// <summary>
        /// 计算 Boss 指向目标的水平单位方向，目标无效时退回当前前方向。
        /// </summary>
        /// <param name="targetTransform">方向目标。</param>
        /// <returns>水平单位方向；目标和前方向都无效时返回 Vector3.forward。</returns>
        private Vector3 DirectionTo(Transform targetTransform)
        {
            if (targetTransform == null)
            {
                Vector3 fallback = Flatten(transform.forward);
                return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
            }

            Vector3 direction = Flatten(targetTransform.position - transform.position);
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Flatten(transform.forward).normalized;
        }

        /// <summary>
        /// 计算 Boss 指向当前缓存目标的水平单位方向。
        /// </summary>
        /// <returns>目标有效时返回指向目标的方向；否则返回前方向 fallback。</returns>
        private Vector3 DirectionToTarget()
        {
            return DirectionTo(target);
        }

        /// <summary>
        /// 自动绑定 CharacterController 和相位移动 VFX 依赖，降低场景手动配置成本。
        /// </summary>
        private void BindReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }

            if (phaseMoveVfx == null)
            {
                phaseMoveVfx = GetComponent<BossPhaseMoveVfx>();
            }
        }
        /// <summary>
        /// 清理 Orbit 窗口缓存的侧向、圆心、半径、终点和 VFX 完成状态。
        /// </summary>
        private void ClearOrbitRuntime()
        {
            activeOrbitWindowName = string.Empty;
            activeOrbitCenter = Vector3.zero;
            activeOrbitStartRadial = Vector3.zero;
            activeOrbitEndRadial = Vector3.zero;
            activeOrbitEndPosition = Vector3.zero;
            activeOrbitRadius = 0f;
            activeOrbitSignedAngle = 0f;
            activeOrbitVfxEnded = false;
        }

        /// <summary>
        /// 去除向量的垂直分量，使 Boss 位移和距离计算保持在水平面。
        /// </summary>
        /// <param name="value">待转换的世界向量。</param>
        /// <returns>y 分量置零后的向量。</returns>
        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }

        /// <summary>
        /// 计算两个世界坐标之间的水平距离。
        /// </summary>
        /// <param name="a">第一个世界坐标。</param>
        /// <param name="b">第二个世界坐标。</param>
        /// <returns>忽略 y 分量后的距离，单位为米。</returns>
        private static float HorizontalDistance(Vector3 a, Vector3 b)
        {
            return Flatten(a - b).magnitude;
        }
    }
}
