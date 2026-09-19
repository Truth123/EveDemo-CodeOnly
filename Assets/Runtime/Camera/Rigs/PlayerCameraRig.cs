// 文件说明：维护自由视角、锁定视角、目标选择和相机跟随。
// 所属模块：战斗相机。
// 运行影响：影响相机关注点、锁定构图、快速位移跟随和 CameraShake 叠加。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.CameraSystem.Rigs
{
    /// <summary>
    /// 第三人称玩家相机。支持自由视角跟随和锁定视角基础构图，用于 Play 模式测试战斗手感。
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PlayerCameraRig : MonoBehaviour
    {
        /// <summary>玩家状态机，未绑定时自动查找场景中的 PlayerStateMachine。</summary>
        [SerializeField] private PlayerStateMachine playerStateMachine;
        /// <summary>相机跟随目标，未绑定时使用玩家状态机的 Transform。</summary>
        [SerializeField] private Transform followTarget;
        /// <summary>玩家上半身附近的相机关注点偏移。</summary>
        [SerializeField] private Vector3 pivotOffset = new Vector3(0f, 1.45f, 0f);
        /// <summary>锁定目标的关注点偏移，避免只看脚底。</summary>
        [SerializeField] private Vector3 lockOnTargetOffset = new Vector3(0f, 1.2f, 0f);
        /// <summary>自由视角默认相机距离。</summary>
        [SerializeField] private float freeDistance = 4.5f;
        /// <summary>锁定视角基础相机距离。</summary>
        [SerializeField] private float lockOnDistance = 5.4f;
        /// <summary>相机碰撞后允许的最近距离。</summary>
        [SerializeField] private float minCollisionDistance = 1.0f;
        /// <summary>相机碰撞检测半径。</summary>
        [SerializeField] private float collisionRadius = 0.24f;
        /// <summary>相机碰撞检测层。</summary>
        [SerializeField] private LayerMask collisionMask = ~0;
        /// <summary>水平鼠标灵敏度。</summary>
        [SerializeField] private float yawSensitivity = 180f;
        /// <summary>垂直鼠标灵敏度。</summary>
        [SerializeField] private float pitchSensitivity = 140f;
        /// <summary>自由视角最低俯仰角。</summary>
        [SerializeField] private float minPitch = -25f;
        /// <summary>自由视角最高俯仰角。</summary>
        [SerializeField] private float maxPitch = 60f;
        /// <summary>锁定视角默认俯仰角。</summary>
        [SerializeField] private float lockOnPitch = 18f;
        /// <summary>锁定视角允许鼠标产生的水平构图偏移。</summary>
        [SerializeField] private float maxLockOnYawOffset = 25f;
        /// <summary>锁定视角允许鼠标产生的垂直构图偏移。</summary>
        [SerializeField] private float maxLockOnPitchOffset = 12f;
        /// <summary>相机位置平滑时间。</summary>
        [SerializeField] private float positionSmoothTime = 0.08f;
        /// <summary>普通移动时相机关注点平滑时间。</summary>
        [SerializeField] private float focusSmoothTime = 0.06f;
        /// <summary>超过该关注点速度时启用快速位移镜头缓冲。</summary>
        [SerializeField] private float fastFollowSpeedThreshold = 7.5f;
        /// <summary>低于该关注点速度后才允许快速位移缓冲逐步退出。</summary>
        [SerializeField] private float fastFollowExitSpeedThreshold = 5.5f;
        /// <summary>快速位移触发后额外保持缓冲的时间，避免阈值附近反复切换。</summary>
        [SerializeField] private float fastFollowHoldTime = 0.12f;
        /// <summary>关注点落后超过该距离时继续保持快速位移缓冲，避免结束瞬间猛追。</summary>
        [SerializeField] private float fastFollowLagExitDistance = 0.35f;
        /// <summary>快速位移时相机关注点追赶平滑时间。</summary>
        [SerializeField] private float fastFocusSmoothTime = 0.16f;
        /// <summary>普通移动时相机关注点最大追赶速度。</summary>
        [SerializeField] private float normalFocusMaxSpeed = 14f;
        /// <summary>快速位移时相机关注点最大追赶速度。</summary>
        [SerializeField] private float fastFocusMaxSpeed = 16f;
        /// <summary>关注点平滑时间在普通 / 快速模式之间切换的插值速度。</summary>
        [SerializeField] private float focusSmoothTimeBlendSpeed = 10f;
        /// <summary>关注点跳变超过该距离时视为场景重置或大传送，直接对齐。</summary>
        [SerializeField] private float focusTeleportSnapDistance = 10f;
        /// <summary>相机自由旋转平滑速度。</summary>
        [SerializeField] private float freeRotationSmoothSpeed = 1000f;
        /// <summary>相机锁定旋转平滑速度。</summary>
        [SerializeField] private float lockOnRotationSmoothSpeed = 30;
        /// <summary>是否在 Play 时锁定鼠标指针。</summary>
        [SerializeField] private bool lockCursorOnPlay = true;
        /// <summary>是否允许战斗反馈层叠加相机震动。</summary>
        [SerializeField] private bool enableFeedbackShake = true;
        /// <summary>是否输出关注点平滑调试日志。默认关闭，避免 Play Mode 日志刷屏。</summary>
        [SerializeField] private bool logFocusDebug;

        private Vector3 positionVelocity;
        private Vector3 pivotVelocity;
        private Vector3 smoothedPivot;
        private Vector3 lastRawPivot;
        private Vector3 lastShakeOffset;
        private float yaw;
        private float pitch = 18f;
        private float lockOnYawOffset;
        private float lockOnPitchOffset;
        private float shakeEndTime;
        private float shakeDuration = 0.01f;
        private float shakeAmplitude;
        private float shakeFrequency = 32f;
        private float currentFocusSmoothTime;
        private float fastFollowRemainingTime;
        private int shakeSeed;
        private bool wasLockOn;
        private bool initialized;
        private bool pivotInitialized;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }

            if (followTarget == null && playerStateMachine != null)
            {
                followTarget = playerStateMachine.transform;
            }

            Vector3 euler = transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = NormalizePitch(euler.x);
        }

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            if (Application.isPlaying && lockCursorOnPlay)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            if (Application.isPlaying && lockCursorOnPlay)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
        }

        /// <summary>
        /// 在常规 Update 后同步最终位姿、相机或调试显示，避免读取未完成的帧状态。
        /// </summary>
        private void LateUpdate()
        {
            if (playerStateMachine == null || followTarget == null)
            {
                return;
            }

            PlayerStateContext context = playerStateMachine.Context;
            float deltaTime = Time.deltaTime;

            if (context.IsLockOn && context.LockOnTarget != null)
            {
                UpdateLockOnCamera(context, deltaTime);
            }
            else
            {
                UpdateFreeCamera(context, deltaTime);
            }
        }

        /// <summary>
        /// 叠加短时相机震动。只影响最终相机位置，不改变锁定目标、碰撞修正和朝向逻辑。
        /// </summary>
        public void AddShake(float amplitude, float duration, float frequency = 32f)
        {
            if (!enableFeedbackShake || amplitude <= 0f || duration <= 0f)
            {
                return;
            }

            shakeAmplitude = Mathf.Max(shakeAmplitude, amplitude);
            shakeDuration = Mathf.Max(0.01f, duration);
            shakeEndTime = Mathf.Max(shakeEndTime, Time.unscaledTime + duration);
            shakeFrequency = Mathf.Max(1f, frequency);
            shakeSeed++;
        }

        /// <summary>
        /// 计算指定锁定目标对应的稳定战斗机位，供开场镜头在启用相机控制前确定终点。
        /// </summary>
        /// <param name="targetPoint">LockOnTarget 暴露的正式目标点。</param>
        /// <param name="position">成功时写回经过相机碰撞修正的世界坐标。</param>
        /// <param name="rotation">成功时写回朝向玩家关注点的世界旋转。</param>
        /// <returns>true 表示跟随目标与锁定目标均有效并成功计算机位；false 表示依赖缺失。</returns>
        public bool TryGetLockOnCameraPose(
            Transform targetPoint,
            out Vector3 position,
            out Quaternion rotation)
        {
            EnsureRuntimeReferences();
            if (followTarget == null || targetPoint == null)
            {
                position = transform.position;
                rotation = transform.rotation;
                return false;
            }

            Vector3 playerPivot = followTarget.position + pivotOffset;
            Vector3 targetPivot = targetPoint.position + lockOnTargetOffset;
            Vector3 toTarget = targetPivot - playerPivot;
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                toTarget = followTarget.forward;
            }

            float targetYaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
            Quaternion orbitRotation = Quaternion.Euler(lockOnPitch, targetYaw, 0f);
            Vector3 desiredPosition = playerPivot - orbitRotation * Vector3.forward * lockOnDistance;
            position = ResolveCollision(playerPivot, desiredPosition);
            Vector3 lookDirection = playerPivot - position;
            rotation = lookDirection.sqrMagnitude > 0.0001f
                ? Quaternion.LookRotation(lookDirection.normalized, Vector3.up)
                : transform.rotation;
            return true;
        }

        /// <summary>
        /// 从外部开场镜头的当前位姿重建跟随缓存，使启用本组件后的首帧不会瞬移或回弹。
        /// </summary>
        /// <param name="enteringLockOn">true 表示当前玩家已锁定目标，应从标准锁定构图继续跟随。</param>
        public void SynchronizeFromCurrentPose(bool enteringLockOn)
        {
            EnsureRuntimeReferences();
            positionVelocity = Vector3.zero;
            pivotVelocity = Vector3.zero;
            lastShakeOffset = Vector3.zero;
            initialized = true;
            lockOnYawOffset = 0f;
            lockOnPitchOffset = 0f;
            wasLockOn = enteringLockOn;

            Vector3 euler = transform.rotation.eulerAngles;
            yaw = euler.y;
            pitch = NormalizePitch(euler.x);

            if (followTarget != null)
            {
                Vector3 rawPivot = followTarget.position + pivotOffset;
                smoothedPivot = rawPivot;
                lastRawPivot = rawPivot;
                pivotInitialized = true;
            }

            currentFocusSmoothTime = Mathf.Max(0.001f, focusSmoothTime);
            fastFollowRemainingTime = 0f;

            PlayerStateContext context = playerStateMachine != null ? playerStateMachine.Context : null;
            if (!enteringLockOn || context?.LockOnTarget == null || followTarget == null)
            {
                return;
            }

            Vector3 toTarget = context.LockOnTarget.position + lockOnTargetOffset -
                (followTarget.position + pivotOffset);
            toTarget.y = 0f;
            if (toTarget.sqrMagnitude > 0.0001f)
            {
                yaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
            }

            pitch = lockOnPitch;
        }

        /// <summary>补齐玩家状态机与跟随目标引用，供 Awake 之外的开场流程安全调用。</summary>
        private void EnsureRuntimeReferences()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }

            if (followTarget == null && playerStateMachine != null)
            {
                followTarget = playerStateMachine.transform;
            }
        }

        /// <summary>
        /// 自由视角：根据鼠标输入绕玩家旋转，相机始终看向玩家上半身附近的关注点。
        /// </summary>
        private void UpdateFreeCamera(PlayerStateContext context, float deltaTime)
        {
            Vector2 look = context.Input.Look;
            yaw += look.x * yawSensitivity * deltaTime;
            pitch -= look.y * pitchSensitivity * deltaTime;
            pitch = Mathf.Clamp(pitch, minPitch, maxPitch);

            wasLockOn = false;
            lockOnYawOffset = 0f;
            lockOnPitchOffset = 0f;

            Vector3 pivot = UpdateSmoothedPivot(followTarget.position + pivotOffset, deltaTime);
            Quaternion orbitRotation = Quaternion.Euler(pitch, yaw, 0f);
            Vector3 desiredPosition = pivot - orbitRotation * Vector3.forward * freeDistance;
            ApplyCameraPose(pivot, desiredPosition, deltaTime);
        }

        /// <summary>
        /// 锁定视角：以玩家和目标之间的中间构图点为关注点，允许少量鼠标偏移以便观察战斗空间。
        /// </summary>
        private void UpdateLockOnCamera(PlayerStateContext context, float deltaTime)
        {
            if (!wasLockOn)
            {
                lockOnYawOffset = 0f;
                lockOnPitchOffset = 0f;
                wasLockOn = true;
            }

            Vector2 look = context.Input.Look;
            lockOnYawOffset = Mathf.Clamp(
                lockOnYawOffset + look.x * yawSensitivity * 0.45f * deltaTime,
                -maxLockOnYawOffset,
                maxLockOnYawOffset);
            lockOnPitchOffset = Mathf.Clamp(
                lockOnPitchOffset - look.y * pitchSensitivity * 0.35f * deltaTime,
                -maxLockOnPitchOffset,
                maxLockOnPitchOffset);

            Vector3 rawPlayerPivot = followTarget.position + pivotOffset;
            Vector3 playerPivot = UpdateSmoothedPivot(rawPlayerPivot, deltaTime);
            Vector3 targetPivot = context.LockOnTarget.position + lockOnTargetOffset;
            Vector3 toTarget = targetPivot - rawPlayerPivot;
            toTarget.y = 0f;

            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                toTarget = followTarget.forward;
            }

            float baseYaw = Quaternion.LookRotation(toTarget.normalized, Vector3.up).eulerAngles.y;
            yaw = Mathf.LerpAngle(yaw, baseYaw, 1f - Mathf.Exp(-lockOnRotationSmoothSpeed * deltaTime));
            pitch = Mathf.LerpAngle(pitch, lockOnPitch, 1f - Mathf.Exp(-lockOnRotationSmoothSpeed * deltaTime));

            Vector3 lookAtPoint = playerPivot;
            Quaternion orbitRotation = Quaternion.Euler(pitch + lockOnPitchOffset, yaw + lockOnYawOffset, 0f);
            Vector3 desiredPosition = lookAtPoint - orbitRotation * Vector3.forward * lockOnDistance;
            ApplyCameraPose(lookAtPoint, desiredPosition, deltaTime);
        }

        /// <summary>
        /// 平滑玩家关注点。角色仍可快速位移，但镜头目标会短时间追上，避免被瞬间拖拽。
        /// </summary>
        private Vector3 UpdateSmoothedPivot(Vector3 rawPivot, float deltaTime)
        {
            if (!pivotInitialized || deltaTime <= 0f)
            {
                smoothedPivot = rawPivot;
                lastRawPivot = rawPivot;
                pivotVelocity = Vector3.zero;
                currentFocusSmoothTime = Mathf.Max(0.001f, focusSmoothTime);
                fastFollowRemainingTime = 0f;
                pivotInitialized = true;
                return smoothedPivot;
            }

            Vector3 rawDelta = rawPivot - lastRawPivot;
            float rawDistance = rawDelta.magnitude;
            lastRawPivot = rawPivot;

            if (rawDistance >= focusTeleportSnapDistance)
            {
                smoothedPivot = rawPivot;
                pivotVelocity = Vector3.zero;
                currentFocusSmoothTime = Mathf.Max(0.001f, focusSmoothTime);
                fastFollowRemainingTime = 0f;
                return smoothedPivot;
            }

            float rawSpeed = rawDistance / deltaTime;
            float pivotLag = Vector3.Distance(rawPivot, smoothedPivot);
            bool fastFollowEntered = rawSpeed > fastFollowSpeedThreshold;
            if (fastFollowEntered)
            {
                fastFollowRemainingTime = Mathf.Max(0f, fastFollowHoldTime);
            }
            else if (rawSpeed < fastFollowExitSpeedThreshold)
            {
                fastFollowRemainingTime = Mathf.Max(0f, fastFollowRemainingTime - deltaTime);
            }

            bool fastFollow = fastFollowEntered ||
                fastFollowRemainingTime > 0f ||
                pivotLag > fastFollowLagExitDistance;
            float targetSmoothTime = fastFollow ? fastFocusSmoothTime : focusSmoothTime;
            if (currentFocusSmoothTime <= 0f)
            {
                currentFocusSmoothTime = targetSmoothTime;
            }

            float smoothBlend = 1f - Mathf.Exp(-Mathf.Max(0f, focusSmoothTimeBlendSpeed) * deltaTime);
            currentFocusSmoothTime = Mathf.Lerp(
                currentFocusSmoothTime,
                Mathf.Max(0.001f, targetSmoothTime),
                smoothBlend);
            float maxSpeed = fastFollow ? fastFocusMaxSpeed : normalFocusMaxSpeed;

            smoothedPivot = Vector3.SmoothDamp(
                smoothedPivot,
                rawPivot,
                ref pivotVelocity,
                currentFocusSmoothTime,
                Mathf.Max(0.001f, maxSpeed),
                deltaTime);

            if (logFocusDebug)
            {
                LogFocusDebug(rawSpeed, pivotLag, fastFollow);
            }

            return smoothedPivot;
        }


        [System.Diagnostics.Conditional("UNITY_EDITOR")]
        private void LogFocusDebug(float rawSpeed, float pivotLag, bool fastFollow)
        {
            Debug.LogFormat(
                "CameraFocus RawSpeed:{0:F2} PivotLag:{1:F2} FastFollow:{2} SmoothTime:{3:F3} PivotVelocity:{4}",
                rawSpeed,
                pivotLag,
                fastFollow,
                currentFocusSmoothTime,
                pivotVelocity);
        }

        /// <summary>
        /// 应用最终相机位置和旋转，并在首次启用时立即对齐，避免 Play 第一帧出现明显补间。
        /// </summary>
        private void ApplyCameraPose(Vector3 lookAtPoint, Vector3 desiredPosition, float deltaTime)
        {
            Vector3 correctedPosition = ResolveCollision(lookAtPoint, desiredPosition);
            Vector3 basePosition;

            if (!initialized)
            {
                basePosition = correctedPosition;
                initialized = true;
            }
            else
            {
                Vector3 unshakenPosition = transform.position - lastShakeOffset;
                basePosition = Vector3.SmoothDamp(
                    unshakenPosition,
                    correctedPosition,
                    ref positionVelocity,
                    positionSmoothTime,
                    Mathf.Infinity,
                    deltaTime);
            }

            lastShakeOffset = EvaluateShakeOffset();
            transform.position = basePosition + lastShakeOffset;

            Vector3 lookDirection = lookAtPoint - transform.position;
            if (lookDirection.sqrMagnitude <= 0.0001f)
            {
                return;
            }

            Quaternion targetRotation = Quaternion.LookRotation(lookDirection.normalized, Vector3.up);
            float rotationT;
            if (wasLockOn)
            {
                rotationT = 1f - Mathf.Exp(-lockOnRotationSmoothSpeed * deltaTime);
            }
            else
            {
                rotationT = 1f - Mathf.Exp(-freeRotationSmoothSpeed * deltaTime);
            }
            transform.rotation = Quaternion.Slerp(transform.rotation, targetRotation, rotationT);
        }

        /// <summary>
        /// 执行 Evaluate / Shake / Offset 相关逻辑，并维护 战斗相机 模块的运行时一致性。
        /// </summary>
        private Vector3 EvaluateShakeOffset()
        {
            if (Time.unscaledTime >= shakeEndTime)
            {
                shakeAmplitude = 0f;
                return Vector3.zero;
            }

            float remaining = shakeEndTime - Time.unscaledTime;
            float normalizedRemaining = Mathf.Clamp01(remaining / Mathf.Max(0.01f, shakeDuration));
            float damper = normalizedRemaining * normalizedRemaining;
            float time = Time.unscaledTime * shakeFrequency;
            float x = Mathf.PerlinNoise(shakeSeed * 17.37f, time) * 2f - 1f;
            float y = Mathf.PerlinNoise(shakeSeed * 29.11f, time + 13.7f) * 2f - 1f;
            return (transform.right * x + transform.up * y) * shakeAmplitude * damper;
        }

        /// <summary>
        /// 使用球形检测把相机推到遮挡物前方，减少穿墙和被场景几何体遮住的问题。
        /// </summary>
        private Vector3 ResolveCollision(Vector3 pivot, Vector3 desiredPosition)
        {
            Vector3 toCamera = desiredPosition - pivot;
            float desiredDistance = toCamera.magnitude;

            if (desiredDistance <= minCollisionDistance)
            {
                return desiredPosition;
            }

            Vector3 direction = toCamera / desiredDistance;
            if (Physics.SphereCast(
                    pivot,
                    collisionRadius,
                    direction,
                    out RaycastHit hit,
                    desiredDistance,
                    collisionMask,
                    QueryTriggerInteraction.Ignore))
            {
                float safeDistance = Mathf.Max(hit.distance - collisionRadius, minCollisionDistance);
                return pivot + direction * safeDistance;
            }

            return desiredPosition;
        }

        /// <summary>
        /// 执行 Normalize / Pitch 相关逻辑，并维护 战斗相机 模块的运行时一致性。
        /// </summary>
        private static float NormalizePitch(float eulerX)
        {
            return eulerX > 180f ? eulerX - 360f : eulerX;
        }
    }
}
