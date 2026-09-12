// 文件说明：执行玩家固定方向、绕目标与锁定目标吸附三类代码动作位移。
// 所属模块：玩家移动。
// 运行影响：为 PlayerMovementMotor 提供逐帧水平位移，并负责目标越界、停靠与碰撞停止状态。

using UnityEngine;

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// 纯运行时动作位移执行器，只根据配置、方向和时间输出本帧水平 delta。
    /// </summary>
    public sealed class PlayerActionMotionRuntime
    {
        private PlayerActionMotionConfig config;
        private Vector3 direction;
        private bool orbitActive;
        private bool targetMagnetActive;
        private Vector3 orbitCenter;
        private Vector3 orbitStartOffset;
        private float orbitRadius;
        private int orbitSideSign;
        private float elapsed;
        private float previousDistance01;

        public bool IsActive { get; private set; }
        public PlayerActionMotionId MotionId { get; private set; }
        public float Elapsed => elapsed;
        public float Duration => config != null ? config.Duration : 0f;
        public Vector3 LastDelta { get; private set; }
        public bool CollisionStopped { get; private set; }
        public bool StopOnSideCollision => config != null && config.StopOnSideCollision;

        /// <summary>
        /// 启动一次固定世界方向的动作位移，并重置上一动作的运行时进度。
        /// </summary>
        /// <param name="nextConfig">本次动作位移的时长、距离和累计距离曲线配置。</param>
        /// <param name="worldDirection">动作起手时锁定的世界空间移动方向。</param>
        public void Begin(PlayerActionMotionConfig nextConfig, Vector3 worldDirection)
        {
            Vector3 horizontalDirection = worldDirection;
            horizontalDirection.y = 0f;

            if (nextConfig == null || nextConfig.Distance <= 0f || horizontalDirection.sqrMagnitude <= 0.0001f)
            {
                Stop();
                return;
            }

            config = nextConfig;
            direction = horizontalDirection.normalized;
            orbitActive = false;
            targetMagnetActive = false;
            orbitCenter = Vector3.zero;
            orbitStartOffset = Vector3.zero;
            orbitRadius = 0f;
            orbitSideSign = 0;
            elapsed = 0f;
            previousDistance01 = 0f;
            LastDelta = Vector3.zero;
            CollisionStopped = false;
            MotionId = nextConfig.MotionId;
            IsActive = true;
        }

        /// <summary>
        /// 启动一次绕目标圆周移动，并以开始时玩家到目标的水平距离作为轨道半径。
        /// </summary>
        /// <param name="nextConfig">本次动作位移的时长、弧长和累计距离曲线配置。</param>
        /// <param name="center">轨道目标的世界坐标。</param>
        /// <param name="startPosition">玩家开始绕行时的世界坐标。</param>
        /// <param name="sideSign">大于零表示向左绕行，小于零表示向右绕行。</param>
        public void BeginOrbitAroundTarget(
            PlayerActionMotionConfig nextConfig,
            Vector3 center,
            Vector3 startPosition,
            int sideSign)
        {
            center.y = 0f;
            startPosition.y = 0f;
            Vector3 startOffset = startPosition - center;

            if (nextConfig == null ||
                nextConfig.Distance <= 0f ||
                startOffset.sqrMagnitude <= 0.0001f ||
                sideSign == 0)
            {
                Stop();
                return;
            }

            config = nextConfig;
            direction = Vector3.zero;
            orbitActive = true;
            targetMagnetActive = false;
            orbitCenter = center;
            orbitStartOffset = startOffset;
            orbitRadius = startOffset.magnitude;
            orbitSideSign = sideSign > 0 ? 1 : -1;
            elapsed = 0f;
            previousDistance01 = 0f;
            LastDelta = Vector3.zero;
            CollisionStopped = false;
            MotionId = nextConfig.MotionId;
            IsActive = true;
        }

        /// <summary>
        /// 启动一次锁定目标吸附；只有目标在配置的水平距离和角度内时才进入运行状态。
        /// </summary>
        /// <param name="nextConfig">本次动作位移配置，必须使用 LockOnTarget 模式。</param>
        /// <param name="currentPosition">玩家当前世界坐标。</param>
        /// <param name="targetPosition">当前锁定目标世界坐标。</param>
        /// <param name="currentForward">玩家当前世界前向，用于角度阈值检查。</param>
        /// <returns>目标有效并成功启动时返回 true；目标越界或配置无效时返回 false。</returns>
        public bool BeginTargetMagnet(
            PlayerActionMotionConfig nextConfig,
            Vector3 currentPosition,
            Vector3 targetPosition,
            Vector3 currentForward)
        {
            if (nextConfig == null ||
                nextConfig.TargetMode != PlayerActionMotionTargetMode.LockOnTarget ||
                nextConfig.Distance <= 0f ||
                !IsTargetWithinLimits(nextConfig, currentPosition, targetPosition, currentForward))
            {
                Stop();
                return false;
            }

            config = nextConfig;
            direction = Vector3.zero;
            orbitActive = false;
            targetMagnetActive = true;
            orbitCenter = Vector3.zero;
            orbitStartOffset = Vector3.zero;
            orbitRadius = 0f;
            orbitSideSign = 0;
            elapsed = 0f;
            previousDistance01 = 0f;
            LastDelta = Vector3.zero;
            CollisionStopped = false;
            MotionId = nextConfig.MotionId;
            IsActive = true;
            return true;
        }

        /// <summary>
        /// 推进不需要世界坐标的固定方向动作位移。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <returns>本帧请求的世界空间水平位移。</returns>
        public Vector3 Tick(float deltaTime)
        {
            return Tick(deltaTime, Vector3.zero);
        }

        /// <summary>
        /// 推进固定方向或绕目标动作位移；绕目标模式使用当前玩家坐标修正轨道误差。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="currentPosition">玩家当前世界坐标。</param>
        /// <returns>本帧请求的世界空间水平位移。</returns>
        public Vector3 Tick(float deltaTime, Vector3 currentPosition)
        {
            return Tick(deltaTime, currentPosition, Vector3.zero, Vector3.forward, false);
        }

        /// <summary>
        /// 推进动作位移，并在锁定目标模式下按目标当前位置重新校验距离、角度和停靠距离。
        /// </summary>
        /// <param name="deltaTime">本帧逻辑时间，单位为秒。</param>
        /// <param name="currentPosition">玩家当前世界坐标。</param>
        /// <param name="targetPosition">当前锁定目标世界坐标。</param>
        /// <param name="currentForward">玩家当前世界前向，用于目标角度检查。</param>
        /// <param name="hasTarget">当前是否仍有可用锁定目标。</param>
        /// <returns>本帧请求的世界空间水平位移。</returns>
        public Vector3 Tick(
            float deltaTime,
            Vector3 currentPosition,
            Vector3 targetPosition,
            Vector3 currentForward,
            bool hasTarget)
        {
            if (!IsActive || config == null)
            {
                LastDelta = Vector3.zero;
                return Vector3.zero;
            }

            elapsed = Mathf.Min(elapsed + Mathf.Max(0f, deltaTime), config.Duration);
            float normalizedTime = config.Duration > 0f ? elapsed / config.Duration : 1f;
            float currentDistance01 = config.EvaluateDistance01(normalizedTime);
            float deltaDistance = Mathf.Max(0f, currentDistance01 - previousDistance01) * config.Distance;
            previousDistance01 = currentDistance01;

            if (targetMagnetActive)
            {
                if (!hasTarget || !IsTargetWithinLimits(config, currentPosition, targetPosition, currentForward))
                {
                    Stop();
                    return Vector3.zero;
                }

                Vector3 toTarget = targetPosition - currentPosition;
                toTarget.y = 0f;
                float remainingDistance = Mathf.Max(0f, toTarget.magnitude - config.StopDistance);
                LastDelta = toTarget.sqrMagnitude > 0.0001f
                    ? toTarget.normalized * Mathf.Min(deltaDistance, remainingDistance)
                    : Vector3.zero;
            }
            else if (orbitActive)
            {
                currentPosition.y = 0f;
                float traveledDistance = currentDistance01 * config.Distance;
                float angleDegrees = orbitSideSign * traveledDistance / orbitRadius * Mathf.Rad2Deg;
                Vector3 desiredPosition = orbitCenter + Quaternion.AngleAxis(angleDegrees, Vector3.up) * orbitStartOffset;
                Vector3 orbitDelta = desiredPosition - currentPosition;
                orbitDelta.y = 0f;
                LastDelta = orbitDelta;
            }
            else
            {
                LastDelta = direction * deltaDistance;
            }

            if (elapsed >= config.Duration)
            {
                IsActive = false;
            }

            return LastDelta;
        }

        /// <summary>
        /// 检查锁定目标是否仍处于动作配置允许的水平距离和朝向夹角内。
        /// </summary>
        /// <param name="motionConfig">提供最大目标距离和角度的动作配置。</param>
        /// <param name="currentPosition">玩家当前世界坐标。</param>
        /// <param name="targetPosition">当前锁定目标世界坐标。</param>
        /// <param name="currentForward">玩家当前世界前向。</param>
        /// <returns>目标满足距离与角度限制时返回 true；否则返回 false。</returns>
        private static bool IsTargetWithinLimits(
            PlayerActionMotionConfig motionConfig,
            Vector3 currentPosition,
            Vector3 targetPosition,
            Vector3 currentForward)
        {
            Vector3 toTarget = targetPosition - currentPosition;
            toTarget.y = 0f;
            float targetDistance = toTarget.magnitude;
            if (motionConfig.MaxTargetDistance <= 0f || targetDistance > motionConfig.MaxTargetDistance)
            {
                return false;
            }

            if (targetDistance <= 0.0001f)
            {
                return true;
            }

            currentForward.y = 0f;
            if (currentForward.sqrMagnitude <= 0.0001f)
            {
                return false;
            }

            return Vector3.Angle(currentForward, toTarget) <= motionConfig.MaxTargetAngle;
        }

        /// <summary>
        /// 停止 By / Collision 流程，并清理当前动作、位移或显示状态。
        /// </summary>
        public void StopByCollision()
        {
            CollisionStopped = true;
            Stop();
        }

        /// <summary>
        /// 停止 Stop 流程，并清理当前动作、位移或显示状态。
        /// </summary>
        public void Stop()
        {
            config = null;
            direction = Vector3.zero;
            orbitActive = false;
            targetMagnetActive = false;
            orbitCenter = Vector3.zero;
            orbitStartOffset = Vector3.zero;
            orbitRadius = 0f;
            orbitSideSign = 0;
            elapsed = 0f;
            previousDistance01 = 0f;
            LastDelta = Vector3.zero;
            IsActive = false;
            MotionId = PlayerActionMotionId.None;
        }
    }
}
