// 文件说明：
// PlayerMovementMotor 是玩家移动系统的运行时入口，负责把 PlayerStateContext 中的状态、输入和动作位移请求转换为
// CharacterController 位移，并把实际速度、动画移动参数、Root Motion 状态和动作位移状态写回上下文。
//
// 系统职责：
// - 普通移动：Idle / Locomotion 下根据自由视角、战斗状态、锁定目标、冲刺意图和加减速计算水平速度。
// - 防御移动：Guard Walk 使用防御慢走速度；原地 Guard Start / Loop / Release 在锁定时持续面向 Boss，
//   GuardHit / PerfectGuard 保留反应动画方向，不被通用锁定朝向覆盖。
// - 动作位移：Evade / Skill / HitReaction / Knockdown 使用 PlayerActionMotionProfile 的代码位移；
//   Attack 使用 Animator Root Motion 的水平位移。
// - 位移提交：普通移动、动作位移、Root Motion 和重力最终都通过 CharacterController.Move 提交。
// - Boss 站位互斥：PlayerBossSeparation 只裁剪和修正玩家与 Boss 的身体距离，不参与命中、伤害或防御结算。
//
// 每帧主流程：
// 1. Tick() 先更新 SprintIntent，并根据 ActionMotionRequestVersion 启动新的动作位移。
// 2. 动作位移优先，其次 Guard Walk，再处理 Attack / Guard / 受击等非普通移动状态，最后执行 Idle / Locomotion 普通移动。
// 3. 每次位移后把 MoveDirection、MoveSpeed、ActualHorizontalVelocity、动画移动参数和运行时位移状态写回 PlayerStateContext。
//
// 重点事项：
// - 本文件不负责状态切换、输入读取、Timeline 窗口判断、命中检测或伤害结算。
// - PlayerActionMotionProfile 是动作位移唯一配置源；缺配置时拒绝动作位移，不创建硬编码默认位移。
// - 修改移动流程时需要同步检查 PlayerStateContext 写回字段、Animator 移动参数、动作位移配置和 Boss 软互斥效果。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// 玩家地面移动 Motor。第 1 阶段实现自由移动、锁定移动、朝向和 SprintIntent。
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerMovementMotor : MonoBehaviour
    {
        private const float AnimationSpeedEpsilon = 0.001f;
        private const float LockOnSideEvadeMinOrbitRadius = 0.6f;

        /// <summary>相机参考，用于自由视角下把 WASD 转成世界方向。</summary>
        [SerializeField] private Transform cameraTransform;
        /// <summary>非战斗自由移动速度。</summary>
        [SerializeField] private float runSpeed = 4.5f;
        /// <summary>非战斗冲刺速度。</summary>
        [SerializeField] private float sprintSpeed = 6.5f;
        /// <summary>战斗自由移动速度。</summary>
        [SerializeField] private float battleRunSpeed = 4.3f;
        /// <summary>战斗自由冲刺速度。</summary>
        [SerializeField] private float battleSprintSpeed = 6.2f;
        /// <summary>锁定普通移动速度。</summary>
        [SerializeField] private float lockOnRunSpeed = 3.8f;
        /// <summary>锁定冲刺速度。</summary>
        [SerializeField] private float lockOnSprintSpeed = 5.8f;
        /// <summary>防御慢走基础速度。</summary>
        [SerializeField] private float guardWalkSpeed = 1.8f;
        /// <summary>后退速度倍率。</summary>
        [SerializeField] private float backwardSpeedRate = 0.8f;
        /// <summary>横移速度倍率。</summary>
        [SerializeField] private float strafeSpeedRate = 0.9f;
        /// <summary>防御后退速度倍率。</summary>
        [SerializeField] private float guardBackwardSpeedRate = 0.75f;
        /// <summary>防御横移速度倍率。</summary>
        [SerializeField] private float guardStrafeSpeedRate = 0.85f;
        /// <summary>移动加速度。</summary>
        [SerializeField] private float acceleration = 20f;
        /// <summary>移动减速度。</summary>
        [SerializeField] private float deceleration = 25f;
        /// <summary>自由移动转向速度。</summary>
        [SerializeField] private float freeMoveRotateSpeed = 720f;
        /// <summary>锁定时朝向目标的转向速度。</summary>
        [SerializeField] private float lockOnRotateSpeed = 900f;
        /// <summary>防御慢走转向速度。</summary>
        [SerializeField] private float guardRotateSpeed = 540f;
        /// <summary>技能代码位移期间的朝向修正速度。</summary>
        [SerializeField] private float skillRotateSpeed = 1440f;
        /// <summary>长按闪避键达到该时间后形成冲刺意图。</summary>
        [SerializeField] private float sprintHoldThreshold = 0.25f;
        /// <summary>重力加速度。</summary>
        [SerializeField] private float gravity = -25f;
        /// <summary>玩家 in-place 动作的代码驱动位移配置。缺失时拒绝动作位移，不创建运行时默认配置。</summary>
        [SerializeField] private PlayerActionMotionProfile actionMotionProfile;
        /// <summary>玩家与 Boss 的软互斥模块。为空时运行时从同对象查找。</summary>
        [SerializeField] private PlayerBossSeparation bossSeparation;

        private CharacterController characterController;
        private Vector3 horizontalVelocity;
        private float verticalVelocity;
        private float evadeHoldStartTime = -1f;
        private readonly PlayerActionMotionRuntime actionMotionRuntime = new PlayerActionMotionRuntime();
        private int observedActionMotionRequestVersion = -1;
        private PlayerStateContext latestContext;
        private bool missingActionMotionProfileLogged;

        /// <summary>
        /// 缓存同对象上的 CharacterController、PlayerBossSeparation 和默认相机引用。
        /// </summary>
        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            if (bossSeparation == null)
            {
                bossSeparation = GetComponent<PlayerBossSeparation>();
            }

            if (bossSeparation == null)
            {
                bossSeparation = gameObject.AddComponent<PlayerBossSeparation>();
            }

            if (cameraTransform == null && Camera.main != null)
            {
                cameraTransform = Camera.main.transform;
            }
        }

        /// <summary>
        /// 根据玩家状态和输入推进一帧移动，并把实际位移、速度和动画移动参数写回状态机上下文。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供状态、输入、锁定目标和动作位移请求，并接收移动结果。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <remarks>
        /// 动作位移优先于 Guard Walk 和普通移动；非移动状态只保留重力与必要的朝向处理。
        /// </remarks>
        public void Tick(PlayerStateContext context, float deltaTime)
        {
            latestContext = context;
            UpdateSprintIntent(context);
            StartRequestedActionMotionIfNeeded(context);

            if (actionMotionRuntime.IsActive && !CanRunActionMotion(context.CurrentState))
            {
                actionMotionRuntime.Stop();
            }

            if (actionMotionRuntime.IsActive)
            {
                if (context.CurrentState == PlayerStateId.Evade)
                {
                    ApplyEvadeRotation(context);
                }
                else if (context.CurrentState == PlayerStateId.Skill)
                {
                    ApplySkillRotation(context, deltaTime);
                }

                horizontalVelocity = Vector3.zero;
                bool hasLockOnTarget = context.IsLockOn && context.LockOnTarget != null;
                Vector3 targetPosition = hasLockOnTarget
                    ? context.LockOnTarget.position
                    : Vector3.zero;
                Vector3 actionDelta = actionMotionRuntime.Tick(
                    deltaTime,
                    transform.position,
                    targetPosition,
                    transform.forward,
                    hasLockOnTarget);
                ApplyGravity(deltaTime);
                CollisionFlags collisionFlags = MoveCharacter(actionDelta, deltaTime, out Vector3 appliedActionDelta);

                if (actionMotionRuntime.StopOnSideCollision &&
                    (collisionFlags & CollisionFlags.Sides) != 0)
                {
                    actionMotionRuntime.StopByCollision();
                }

                Vector3 actionVelocity = DeltaToVelocity(appliedActionDelta, deltaTime);
                context.MoveDirection = appliedActionDelta.sqrMagnitude > 0.0001f ? appliedActionDelta.normalized : Vector3.zero;
                context.MoveSpeed = actionVelocity.magnitude;
                SyncMovementAnimationState(context, actionVelocity, 0f, false);
                SyncActionMotionState(context, collisionFlags, actionDelta);
                return;
            }

            if (CanRunGuardMovement(context))
            {
                TickGuardMovement(context, deltaTime);
                return;
            }

            if (context.CurrentState != PlayerStateId.Idle && context.CurrentState != PlayerStateId.Locomotion)
            {
                if (context.CurrentState == PlayerStateId.Attack)
                {
                    horizontalVelocity = Vector3.zero;
                    context.MoveDirection = Vector3.zero;
                    context.MoveSpeed = 0f;
                }
                else if (context.CurrentState == PlayerStateId.Guard)
                {
                    StopHorizontalMovementImmediately(context);
                    ApplyStationaryGuardFacing(context, deltaTime);
                }
                else
                {
                    StopHorizontalMovement(context, deltaTime);
                }

                ApplyGravity(deltaTime);
                CollisionFlags collisionFlags = MoveCharacter(Vector3.zero, deltaTime, out Vector3 appliedStopDelta);
                if (context.CurrentState != PlayerStateId.Attack &&
                    context.CurrentState != PlayerStateId.Guard)
                {
                    context.MoveSpeed = DeltaToVelocity(appliedStopDelta, deltaTime).magnitude;
                }

                SyncMovementAnimationState(context, Vector3.zero, 0f, false);
                SyncActionMotionState(context, collisionFlags, Vector3.zero);
                return;
            }

            Vector3 moveDirection = ResolveMoveDirection(context);
            float moveBaseSpeed = ResolveMoveBaseSpeed(context);
            float targetSpeed = moveBaseSpeed * context.Input.MoveMagnitude;
            Vector3 targetVelocity = moveDirection * targetSpeed;
            float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude ? acceleration : deceleration;

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
            ApplyRotation(context, moveDirection, deltaTime);
            ApplyGravity(deltaTime);

            CollisionFlags locomotionCollisionFlags = MoveCharacter(horizontalVelocity * deltaTime, deltaTime, out Vector3 appliedLocomotionDelta);
            Vector3 actualLocomotionVelocity = DeltaToVelocity(appliedLocomotionDelta, deltaTime);

            context.MoveDirection = moveDirection;
            context.MoveSpeed = actualLocomotionVelocity.magnitude;
            SyncMovementAnimationState(context, actualLocomotionVelocity, moveBaseSpeed, true);
            SyncActionMotionState(context, locomotionCollisionFlags, Vector3.zero);
        }

        /// <summary>
        /// Animator Root Motion 的唯一入口。只有 Attack 状态会使用水平位移，其他状态收到时会丢弃。
        /// </summary>
        /// <param name="deltaPosition">Animator 本帧输出的 Root Motion 位移，只读取水平分量。</param>
        /// <param name="deltaRotation">Animator 本帧输出的 Root Motion 旋转；当前玩家攻击不使用该旋转。</param>
        public void HandleRootMotion(Vector3 deltaPosition, Quaternion deltaRotation)
        {
            _ = deltaRotation;
            Vector3 rootMotionDelta = deltaPosition;
            rootMotionDelta.y = 0f;

            if (latestContext == null)
            {
                return;
            }

            if (latestContext.CurrentState != PlayerStateId.Attack)
            {
                latestContext.SetRootMotionRuntimeState(false, rootMotionDelta, Vector3.zero, CollisionFlags.None);
                return;
            }

            horizontalVelocity = Vector3.zero;
            rootMotionDelta = ClipBossSeparationDelta(rootMotionDelta);
            CollisionFlags collisionFlags = MoveRootMotion(rootMotionDelta, out Vector3 appliedRootMotionDelta);
            float deltaTime = Time.deltaTime;
            Vector3 rootMotionVelocity = DeltaToVelocity(appliedRootMotionDelta, deltaTime);

            latestContext.MoveDirection = appliedRootMotionDelta.sqrMagnitude > 0.0001f
                ? appliedRootMotionDelta.normalized
                : Vector3.zero;
            latestContext.MoveSpeed = rootMotionVelocity.magnitude;
            latestContext.ActualHorizontalVelocity = rootMotionVelocity;
            latestContext.SetRootMotionRuntimeState(
                rootMotionDelta.sqrMagnitude > 0.000001f,
                rootMotionDelta,
                appliedRootMotionDelta,
                collisionFlags);
        }

        /// <summary>
        /// 根据闪避键按住时间更新冲刺意图；Guard 状态下强制关闭冲刺。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供输入时间并接收 SprintIntent 结果。</param>
        private void UpdateSprintIntent(PlayerStateContext context)
        {
            if (context.CurrentState == PlayerStateId.Guard)
            {
                evadeHoldStartTime = -1f;
                context.SprintIntent = false;
                return;
            }

            if (context.Input.EvadePressed)
            {
                evadeHoldStartTime = context.Input.Time;
                context.SprintIntent = false;
            }

            if (context.Input.EvadeReleased)
            {
                evadeHoldStartTime = -1f;
                context.SprintIntent = false;
                return;
            }

            if (context.Input.EvadeHeld && evadeHoldStartTime >= 0f)
            {
                float heldTime = context.Input.Time - evadeHoldStartTime;
                if (!context.Input.EvadePressed && heldTime >= sprintHoldThreshold)
                {
                    context.SprintIntent = true;
                }
            }
        }

        /// <summary>
        /// 使用当前输入快照计算本帧普通移动方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供移动输入、锁定状态和锁定目标。</param>
        /// <returns>归一化后的世界空间水平移动方向；无有效输入时返回零向量。</returns>
        private Vector3 ResolveMoveDirection(PlayerStateContext context)
        {
            return ResolveMoveDirection(context, context.Input.Move);
        }

        /// <summary>
        /// 将二维输入转换为世界空间移动方向；锁定时使用目标方向，自由移动时使用相机方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定状态和锁定目标。</param>
        /// <param name="move">二维移动输入，x 表示左右，y 表示前后。</param>
        /// <returns>归一化后的世界空间水平移动方向；输入幅度过小时返回零向量。</returns>
        private Vector3 ResolveMoveDirection(PlayerStateContext context, Vector2 move)
        {
            if (move.sqrMagnitude <= 0.0001f)
            {
                return Vector3.zero;
            }

            if (context.IsLockOn && context.LockOnTarget != null)
            {
                Vector3 toTarget = context.LockOnTarget.position - transform.position;
                toTarget.y = 0f;
                Vector3 forward = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                return Vector3.ClampMagnitude(forward * move.y + right * move.x, 1f);
            }

            Transform reference = cameraTransform != null ? cameraTransform : transform;
            Vector3 cameraForward = reference.forward;
            Vector3 cameraRight = reference.right;
            cameraForward.y = 0f;
            cameraRight.y = 0f;
            cameraForward.Normalize();
            cameraRight.Normalize();

            return Vector3.ClampMagnitude(cameraForward * move.y + cameraRight * move.x, 1f);
        }

        /// <summary>
        /// 监听状态机发出的动作位移请求，并在请求版本变化时启动对应的动作位移配置。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供动作位移 ID、请求版本和可选的显式方向。</param>
        private void StartRequestedActionMotionIfNeeded(PlayerStateContext context)
        {
            if (observedActionMotionRequestVersion == context.ActionMotionRequestVersion)
            {
                return;
            }

            observedActionMotionRequestVersion = context.ActionMotionRequestVersion;
            PlayerActionMotionId motionId = context.RequestedActionMotionId;
            if (motionId == PlayerActionMotionId.None)
            {
                return;
            }

            PlayerActionMotionProfile profile = GetActionMotionProfile();
            if (profile == null || !profile.TryGetMotion(motionId, out PlayerActionMotionConfig config))
            {
                actionMotionRuntime.Stop();
                return;
            }

            if (TryBeginLockOnSideEvadeOrbit(context, motionId, config))
            {
                return;
            }

            if (config.TargetMode == PlayerActionMotionTargetMode.LockOnTarget)
            {
                if (!context.IsLockOn || context.LockOnTarget == null)
                {
                    actionMotionRuntime.Stop();
                    return;
                }

                actionMotionRuntime.BeginTargetMagnet(
                    config,
                    transform.position,
                    context.LockOnTarget.position,
                    transform.forward);
                return;
            }

            Vector3 direction = ResolveActionMotionDirection(context, motionId);
            actionMotionRuntime.Begin(config, direction);
        }

        /// <summary>
        /// 锁定状态下将左右闪避转成绕目标的弧线位移；不满足条件时返回 false。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定目标和动作位移方向请求。</param>
        /// <param name="motionId">本次请求的动作位移 ID。</param>
        /// <param name="config">动作位移配置，包含位移时长、距离和曲线。</param>
        /// <returns>如果已启动绕目标弧线位移则返回 true；否则返回 false，由普通动作位移继续处理。</returns>
        private bool TryBeginLockOnSideEvadeOrbit(
            PlayerStateContext context,
            PlayerActionMotionId motionId,
            PlayerActionMotionConfig config)
        {
            if (context.HasRequestedActionMotionDirection ||
                !IsSideEvadeMotion(motionId) ||
                !context.IsLockOn ||
                context.LockOnTarget == null)
            {
                return false;
            }

            Vector3 center = context.LockOnTarget.position;
            Vector3 startPosition = transform.position;
            center.y = 0f;
            startPosition.y = 0f;

            if ((startPosition - center).sqrMagnitude < LockOnSideEvadeMinOrbitRadius * LockOnSideEvadeMinOrbitRadius)
            {
                return false;
            }

            int sideSign = motionId == PlayerActionMotionId.EvadeLeft ||
                           motionId == PlayerActionMotionId.PerfectEvadeLeft
                ? 1
                : -1;
            actionMotionRuntime.BeginOrbitAroundTarget(config, center, startPosition, sideSign);
            return true;
        }

        /// <summary>
        /// 判断指定动作位移是否属于锁定状态下可绕目标移动的侧向闪避。
        /// </summary>
        /// <param name="motionId">待判断的动作位移 ID。</param>
        /// <returns>左 / 右普通闪避或完美闪避返回 true；其他动作位移返回 false。</returns>
        private static bool IsSideEvadeMotion(PlayerActionMotionId motionId)
        {
            return motionId == PlayerActionMotionId.EvadeLeft ||
                   motionId == PlayerActionMotionId.EvadeRight ||
                   motionId == PlayerActionMotionId.PerfectEvadeLeft ||
                   motionId == PlayerActionMotionId.PerfectEvadeRight;
        }

        /// <summary>
        /// 返回显式绑定的动作位移配置；缺失时只记录一次错误并拒绝动作位移。
        /// </summary>
        /// <returns>已绑定的 PlayerActionMotionProfile；未绑定时返回 null。</returns>
        private PlayerActionMotionProfile GetActionMotionProfile()
        {
            if (actionMotionProfile != null)
            {
                return actionMotionProfile;
            }

            if (!missingActionMotionProfileLogged)
            {
                Debug.LogError("PlayerMovementMotor requires a PlayerActionMotionProfile. Action motion requests are ignored until the asset is assigned.", this);
                missingActionMotionProfileLogged = true;
            }

            return null;
        }
        /// <summary>
        /// 计算动作位移方向；显式请求方向优先，其次使用锁定目标方向，最后使用角色朝向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定目标、动作输入和显式方向请求。</param>
        /// <param name="motionId">需要计算方向的动作位移 ID。</param>
        /// <returns>归一化后的世界空间水平位移方向。</returns>
        private Vector3 ResolveActionMotionDirection(PlayerStateContext context, PlayerActionMotionId motionId)
        {
            if (context.HasRequestedActionMotionDirection)
            {
                return FlattenOrFallback(context.RequestedActionMotionDirection, -transform.forward);
            }

            if (TryResolveLockOnBasis(context, out Vector3 lockForward, out Vector3 lockRight))
            {
                return ResolveDirectionalMotion(motionId, lockForward, lockRight, context);
            }

            return ResolveDirectionalMotion(motionId, transform.forward, transform.right, context);
        }

        /// <summary>
        /// 按动作位移 ID 从前、后、左、右基准方向中选择实际位移方向。
        /// </summary>
        /// <param name="motionId">需要映射方向的动作位移 ID。</param>
        /// <param name="forward">动作参考前向。</param>
        /// <param name="right">动作参考右向。</param>
        /// <param name="context">当前玩家状态机上下文，用于向前类动作读取锁存输入方向。</param>
        /// <returns>动作位移使用的世界空间水平方向。</returns>
        private Vector3 ResolveDirectionalMotion(
            PlayerActionMotionId motionId,
            Vector3 forward,
            Vector3 right,
            PlayerStateContext context)
        {
            switch (motionId)
            {
                case PlayerActionMotionId.EvadeForward:
                case PlayerActionMotionId.PerfectEvadeForward:
                case PlayerActionMotionId.Skill1Forward:
                    return ResolveForwardActionMotionDirection(context, forward);
                case PlayerActionMotionId.EvadeBackward:
                case PlayerActionMotionId.PerfectEvadeBackward:
                    return -forward;
                case PlayerActionMotionId.EvadeLeft:
                case PlayerActionMotionId.PerfectEvadeLeft:
                    return -right;
                case PlayerActionMotionId.EvadeRight:
                case PlayerActionMotionId.PerfectEvadeRight:
                    return right;
                default:
                    return -forward;
            }
        }

        /// <summary>
        /// 计算向前类动作位移方向；非锁定状态优先使用进入动作时锁存的输入方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定状态和进入动作时的输入方向。</param>
        /// <param name="fallbackForward">没有可用输入方向时使用的前向。</param>
        /// <returns>向前类动作位移的世界空间水平方向。</returns>
        private Vector3 ResolveForwardActionMotionDirection(PlayerStateContext context, Vector3 fallbackForward)
        {
            if (context.IsLockOn && context.LockOnTarget != null)
            {
                return fallbackForward;
            }

            if (!context.HasActionWorldDirection)
            {
                context.ActionWorldDirection = ResolveMoveDirection(context, context.ActionInputMove);
                context.HasActionWorldDirection = context.ActionWorldDirection.sqrMagnitude > 0.0001f;
            }

            return context.HasActionWorldDirection
                ? context.ActionWorldDirection
                : fallbackForward;
        }

        /// <summary>
        /// 根据锁定目标生成水平前向和右向基准；没有有效锁定目标时返回 false。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定状态和锁定目标。</param>
        /// <param name="forward">输出从玩家指向锁定目标的水平前向；失败时为零向量。</param>
        /// <param name="right">输出相对锁定前向的水平右向；失败时为零向量。</param>
        /// <returns>存在有效锁定目标并成功生成基准方向时返回 true；否则返回 false。</returns>
        private bool TryResolveLockOnBasis(PlayerStateContext context, out Vector3 forward, out Vector3 right)
        {
            if (!context.IsLockOn || context.LockOnTarget == null)
            {
                forward = Vector3.zero;
                right = Vector3.zero;
                return false;
            }

            Vector3 toTarget = context.LockOnTarget.position - transform.position;
            toTarget.y = 0f;
            forward = toTarget.sqrMagnitude > 0.0001f ? toTarget.normalized : transform.forward;
            right = Vector3.Cross(Vector3.up, forward).normalized;
            return true;
        }

        /// <summary>
        /// 将方向压到水平面并归一化；输入无效时使用备用方向。
        /// </summary>
        /// <param name="direction">优先使用的世界空间方向。</param>
        /// <param name="fallback">direction 无效时使用的备用方向。</param>
        /// <returns>归一化后的水平向量；两个输入都无效时返回 Vector3.back。</returns>
        private Vector3 FlattenOrFallback(Vector3 direction, Vector3 fallback)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.back;
        }

        /// <summary>
        /// 判断当前状态是否允许动作位移继续独占本帧移动。
        /// </summary>
        /// <param name="stateId">当前玩家状态。</param>
        /// <returns>Evade、Skill、HitReaction、Knockdown 返回 true；其他状态返回 false。</returns>
        private static bool CanRunActionMotion(PlayerStateId stateId)
        {
            return stateId == PlayerStateId.Evade ||
                   stateId == PlayerStateId.Skill ||
                   stateId == PlayerStateId.HitReaction ||
                   stateId == PlayerStateId.Knockdown;
        }

        /// <summary>
        /// 根据战斗、锁定、冲刺和输入方向选择普通移动基础速度。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供战斗状态、锁定状态、输入和 SprintIntent。</param>
        /// <returns>本帧普通移动用于插值的基础速度，单位为米 / 秒。</returns>
        private float ResolveMoveBaseSpeed(PlayerStateContext context)
        {
            bool sprinting = context.SprintIntent && context.Input.EvadeHeld;
            float baseSpeed;

            if (context.IsLockOn)
            {
                baseSpeed = sprinting ? lockOnSprintSpeed : lockOnRunSpeed;
                if (context.Input.Move.y < -0.1f)
                {
                    baseSpeed *= backwardSpeedRate;
                }
                else if (Mathf.Abs(context.Input.Move.x) > 0.1f && Mathf.Abs(context.Input.Move.y) <= 0.1f)
                {
                    baseSpeed *= strafeSpeedRate;
                }
            }
            else if (context.IsInCombat)
            {
                baseSpeed = sprinting ? battleSprintSpeed : battleRunSpeed;
            }
            else
            {
                baseSpeed = sprinting ? sprintSpeed : runSpeed;
            }

            return baseSpeed;
        }

        /// <summary>
        /// 推进防御慢走位移，并同步实际速度和防御移动动画参数。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供输入、锁定目标并接收移动结果。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void TickGuardMovement(PlayerStateContext context, float deltaTime)
        {
            Vector3 moveDirection = ResolveMoveDirection(context);
            float guardMoveBaseSpeed = ResolveGuardMoveBaseSpeed(context);
            float targetSpeed = guardMoveBaseSpeed * context.Input.MoveMagnitude;
            Vector3 targetVelocity = moveDirection * targetSpeed;
            float rate = targetVelocity.sqrMagnitude > horizontalVelocity.sqrMagnitude ? acceleration : deceleration;

            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, targetVelocity, rate * deltaTime);
            ApplyGuardRotation(context, moveDirection, deltaTime);
            ApplyGravity(deltaTime);

            CollisionFlags collisionFlags = MoveCharacter(horizontalVelocity * deltaTime, deltaTime, out Vector3 appliedGuardDelta);
            Vector3 actualGuardVelocity = DeltaToVelocity(appliedGuardDelta, deltaTime);
            context.MoveDirection = moveDirection;
            context.MoveSpeed = actualGuardVelocity.magnitude;
            SyncMovementAnimationState(context, actualGuardVelocity, guardMoveBaseSpeed, true);
            SyncActionMotionState(context, collisionFlags, Vector3.zero);
        }

        /// <summary>
        /// 判断当前帧是否应走 Guard Walk，而不是原地防御或普通移动。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供当前状态和 Guard Walk 标记。</param>
        /// <returns>当前状态为 Guard 且 IsGuardWalking 为 true 时返回 true；否则返回 false。</returns>
        private static bool CanRunGuardMovement(PlayerStateContext context)
        {
            return context.CurrentState == PlayerStateId.Guard &&
                   context.IsGuardWalking;
        }

        /// <summary>
        /// 原地防御保持、起手和释放可在锁定模式下面向 Boss；GuardHit / PerfectGuard 保留反应动画方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供 Guard 反应状态和锁定目标。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyStationaryGuardFacing(PlayerStateContext context, float deltaTime)
        {
            if (context.CurrentGuardReaction != PlayerGuardReactionId.None)
            {
                return;
            }

            ApplyGuardRotation(context, Vector3.zero, deltaTime);
        }

        /// <summary>
        /// 根据防御输入方向返回防御慢走速度，后退和横移会套用各自倍率。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供移动输入和输入幅度。</param>
        /// <returns>防御慢走基础速度，单位为米 / 秒；无移动输入时返回 0。</returns>
        private float ResolveGuardMoveBaseSpeed(PlayerStateContext context)
        {
            if (!context.HasMoveInput)
            {
                return 0f;
            }

            float baseSpeed = guardWalkSpeed;
            Vector2 move = context.Input.Move;
            if (move.y < -0.1f && Mathf.Abs(move.y) >= Mathf.Abs(move.x))
            {
                baseSpeed *= guardBackwardSpeedRate;
            }
            else if (Mathf.Abs(move.x) > 0.1f && Mathf.Abs(move.x) > Mathf.Abs(move.y))
            {
                baseSpeed *= guardStrafeSpeedRate;
            }

            return baseSpeed;
        }

        /// <summary>
        /// 旋转玩家朝向防御参考方向；锁定时优先面向目标，否则面向移动方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定状态和锁定目标。</param>
        /// <param name="moveDirection">没有锁定目标时使用的移动方向。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyGuardRotation(PlayerStateContext context, Vector3 moveDirection, float deltaTime)
        {
            Vector3 facingDirection = Vector3.zero;

            if (context.IsLockOn && context.LockOnTarget != null)
            {
                facingDirection = context.LockOnTarget.position - transform.position;
            }
            else if (moveDirection.sqrMagnitude > 0.0001f)
            {
                facingDirection = moveDirection;
            }

            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, guardRotateSpeed * deltaTime);
        }

        /// <summary>
        /// 旋转玩家普通移动朝向；锁定时面向目标，自由移动时面向移动方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定状态和锁定目标。</param>
        /// <param name="moveDirection">自由移动时使用的世界空间移动方向。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyRotation(PlayerStateContext context, Vector3 moveDirection, float deltaTime)
        {
            Vector3 facingDirection = Vector3.zero;
            float rotateSpeed = freeMoveRotateSpeed;

            if (context.IsLockOn && context.LockOnTarget != null)
            {
                facingDirection = context.LockOnTarget.position - transform.position;
                rotateSpeed = lockOnRotateSpeed;
            }
            else if (moveDirection.sqrMagnitude > 0.0001f)
            {
                facingDirection = moveDirection;
            }

            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, rotateSpeed * deltaTime);
        }

        /// <summary>
        /// 设置闪避开始时的角色朝向；锁定时面向目标，非锁定前闪时面向锁存输入方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定目标、动作输入和锁存方向。</param>
        private void ApplyEvadeRotation(PlayerStateContext context)
        {
            Vector3 facingDirection = Vector3.zero;

            if (context.IsLockOn && context.LockOnTarget != null)
            {
                facingDirection = context.LockOnTarget.position - transform.position;
            }
            else if (context.ActionMove.y > 0.5f)
            {
                if (!context.HasActionWorldDirection)
                {
                    context.ActionWorldDirection = ResolveMoveDirection(context, context.ActionInputMove);
                    context.HasActionWorldDirection = context.ActionWorldDirection.sqrMagnitude > 0.0001f;
                }

                if (context.HasActionWorldDirection)
                {
                    facingDirection = context.ActionWorldDirection;
                }
            }

            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            transform.rotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
        }

        /// <summary>
        /// 技能动作位移期间平滑修正玩家朝向，保持 Skill1 前进方向和视觉朝向一致。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定目标和动作位移方向来源。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplySkillRotation(PlayerStateContext context, float deltaTime)
        {
            Vector3 facingDirection = ResolveActionMotionDirection(context, PlayerActionMotionId.Skill1Forward);
            facingDirection.y = 0f;
            if (facingDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(facingDirection.normalized, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, skillRotateSpeed * deltaTime);
        }

        /// <summary>
        /// 按减速度把水平速度逐步降到零，并清空上下文中的移动方向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，接收移动方向和移动速度结果。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void StopHorizontalMovement(PlayerStateContext context, float deltaTime)
        {
            horizontalVelocity = Vector3.MoveTowards(horizontalVelocity, Vector3.zero, deceleration * deltaTime);
            context.MoveDirection = Vector3.zero;
            context.MoveSpeed = horizontalVelocity.magnitude;
        }

        /// <summary>
        /// 立即清零水平速度和上下文移动速度，供原地防御等状态使用。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，接收清零后的移动方向和移动速度。</param>
        private void StopHorizontalMovementImmediately(PlayerStateContext context)
        {
            horizontalVelocity = Vector3.zero;
            context.MoveDirection = Vector3.zero;
            context.MoveSpeed = 0f;
        }

        /// <summary>
        /// 更新竖直速度；落地时保留轻微向下速度以贴住地面。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        private void ApplyGravity(float deltaTime)
        {
            if (characterController.isGrounded && verticalVelocity < 0f)
            {
                verticalVelocity = -2f;
            }

            verticalVelocity += gravity * deltaTime;
        }

        /// <summary>
        /// 提交普通或动作水平位移和重力位移，并返回 CharacterController 的碰撞结果。
        /// </summary>
        /// <param name="horizontalDelta">本帧请求的水平位移，会先经过 Boss 软互斥裁剪。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒，用于计算重力位移。</param>
        /// <param name="appliedHorizontalDelta">输出 CharacterController 实际应用的水平位移，包含 Boss 距离修正。</param>
        /// <returns>CharacterController.Move 返回的碰撞标记，叠加 Boss 软互斥修正产生的碰撞标记。</returns>
        private CollisionFlags MoveCharacter(Vector3 horizontalDelta, float deltaTime, out Vector3 appliedHorizontalDelta)
        {
            Vector3 before = transform.position;
            horizontalDelta = ClipBossSeparationDelta(horizontalDelta);
            Vector3 verticalDelta = Vector3.up * (verticalVelocity * deltaTime);
            CollisionFlags collisionFlags = characterController.Move(horizontalDelta + verticalDelta);
            collisionFlags |= PostCorrectBossSeparation();
            appliedHorizontalDelta = transform.position - before;
            appliedHorizontalDelta.y = 0f;
            return collisionFlags;
        }

        /// <summary>
        /// 提交攻击 Root Motion 的水平位移，并记录实际应用的水平位移。
        /// </summary>
        /// <param name="horizontalDelta">攻击 Animator Root Motion 请求的水平位移。</param>
        /// <param name="appliedHorizontalDelta">输出 CharacterController 实际应用的水平 Root Motion 位移，包含 Boss 距离修正。</param>
        /// <returns>CharacterController.Move 返回的碰撞标记，叠加 Boss 软互斥修正产生的碰撞标记。</returns>
        private CollisionFlags MoveRootMotion(Vector3 horizontalDelta, out Vector3 appliedHorizontalDelta)
        {
            Vector3 before = transform.position;
            CollisionFlags collisionFlags = characterController.Move(horizontalDelta);
            collisionFlags |= PostCorrectBossSeparation();
            appliedHorizontalDelta = transform.position - before;
            appliedHorizontalDelta.y = 0f;
            return collisionFlags;
        }

        /// <summary>
        /// 让 PlayerBossSeparation 裁剪朝 Boss 过度靠近的水平位移。
        /// </summary>
        /// <param name="horizontalDelta">裁剪前的水平位移。</param>
        /// <returns>经过 Boss 软互斥裁剪后的水平位移；缺少上下文或分离组件时返回原位移。</returns>
        private Vector3 ClipBossSeparationDelta(Vector3 horizontalDelta)
        {
            return bossSeparation != null && latestContext != null
                ? bossSeparation.ClipHorizontalDelta(latestContext, horizontalDelta)
                : horizontalDelta;
        }

        /// <summary>
        /// 在本帧移动结束后执行玩家与 Boss 的最小距离修正。
        /// </summary>
        /// <returns>Boss 软互斥修正产生的 CharacterController 碰撞标记；无法执行修正时返回 CollisionFlags.None。</returns>
        private CollisionFlags PostCorrectBossSeparation()
        {
            return bossSeparation != null && latestContext != null
                ? bossSeparation.PostCorrect(latestContext)
                : CollisionFlags.None;
        }

        /// <summary>
        /// 将一帧水平位移换算为水平速度；deltaTime 无效时返回零速度。
        /// </summary>
        /// <param name="horizontalDelta">本帧实际应用的水平位移。</param>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <returns>水平速度，单位为米 / 秒；deltaTime 小于等于 0 时返回零向量。</returns>
        private static Vector3 DeltaToVelocity(Vector3 horizontalDelta, float deltaTime)
        {
            if (deltaTime <= 0f)
            {
                return Vector3.zero;
            }

            horizontalDelta.y = 0f;
            return horizontalDelta / deltaTime;
        }

        /// <summary>
        /// 把 CharacterController 实际水平速度同步到上下文，并计算 Animator 使用的二维移动参数。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，接收实际速度和动画移动参数。</param>
        /// <param name="actualHorizontalVelocity">CharacterController 本帧实际水平速度。</param>
        /// <param name="referenceSpeed">用于归一化动画参数的参考速度，单位为米 / 秒。</param>
        /// <param name="driveAnimation">是否写入非零动画移动参数；false 时只同步实际速度并清零动画移动。</param>
        private void SyncMovementAnimationState(
            PlayerStateContext context,
            Vector3 actualHorizontalVelocity,
            float referenceSpeed,
            bool driveAnimation)
        {
            actualHorizontalVelocity.y = 0f;
            context.ActualHorizontalVelocity = actualHorizontalVelocity;

            if (!driveAnimation ||
                referenceSpeed <= AnimationSpeedEpsilon ||
                actualHorizontalVelocity.sqrMagnitude <= AnimationSpeedEpsilon * AnimationSpeedEpsilon)
            {
                context.AnimationMove = Vector2.zero;
                context.AnimationMoveMagnitude = 0f;
                return;
            }

            ResolveAnimationBasis(context, out Vector3 forward, out Vector3 right);
            Vector2 animationMove = new Vector2(
                Vector3.Dot(actualHorizontalVelocity, right) / referenceSpeed,
                Vector3.Dot(actualHorizontalVelocity, forward) / referenceSpeed);

            animationMove = Vector2.ClampMagnitude(animationMove, 1f);
            context.AnimationMove = animationMove;
            context.AnimationMoveMagnitude = Mathf.Clamp01(animationMove.magnitude);
        }

        /// <summary>
        /// 返回动画移动参数的前向和右向基准；锁定时使用目标方向，否则使用角色朝向。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，提供锁定目标。</param>
        /// <param name="forward">输出动画参数使用的水平前向。</param>
        /// <param name="right">输出动画参数使用的水平右向。</param>
        private void ResolveAnimationBasis(PlayerStateContext context, out Vector3 forward, out Vector3 right)
        {
            if (TryResolveLockOnBasis(context, out forward, out right))
            {
                return;
            }

            forward = transform.forward;
            right = transform.right;
            forward.y = 0f;
            right.y = 0f;
            forward = forward.sqrMagnitude > 0.0001f ? forward.normalized : Vector3.forward;
            right = right.sqrMagnitude > 0.0001f ? right.normalized : Vector3.right;
        }

        /// <summary>
        /// 将当前动作位移运行状态、最近位移和碰撞标记写入状态机上下文。
        /// </summary>
        /// <param name="context">当前玩家状态机上下文，接收动作位移运行状态。</param>
        /// <param name="collisionFlags">本帧 CharacterController 移动产生的碰撞标记。</param>
        /// <param name="actionDelta">动作位移运行时本帧请求的水平位移。</param>
        private void SyncActionMotionState(
            PlayerStateContext context,
            CollisionFlags collisionFlags,
            Vector3 actionDelta)
        {
            context.SetActionMotionRuntimeState(
                actionMotionRuntime.IsActive,
                actionMotionRuntime.MotionId,
                actionMotionRuntime.Elapsed,
                actionMotionRuntime.Duration,
                actionDelta,
                collisionFlags);
        }
    }
}
