// 文件说明：维护 Boss 代码位移、Root Motion、MotionWarp、碰撞裁剪和调试快照。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 空间位置、攻击落点、穿模保护和位移调试。

using UnityEngine;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// 单个 Root Motion Warp Window 的运行时预算。
    /// 它模仿 Unreal Motion Warping 的窗口级思路：用整段窗口原始 Root Motion 映射到运行时目标总位移。
    /// </summary>
    internal sealed class BossWarpWindowRuntimeState
    {
        private const float Epsilon = 0.0001f;

        private string windowName = string.Empty;
        private Vector3 startPosition;
        private Quaternion startRotation = Quaternion.identity;
        private Vector3 lockedTargetPoint;
        private Vector3 authoredTotalWorld;
        private Vector3 targetTotalWorld;

        public bool IsActive { get; private set; }
        public string WindowName => windowName;
        public Vector3 AuthoredTotalWorld => authoredTotalWorld;
        public Vector3 TargetTotalWorld => targetTotalWorld;
        public float ScaleFactor { get; private set; } = 1f;
        public bool HasValidAuthoredTotal => authoredTotalWorld.sqrMagnitude > Epsilon;

        /// <summary>
        /// 执行 Matches 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public bool Matches(BossMotionWarpWindow window)
        {
            return IsActive && window != null && windowName == window.Name;
        }

        /// <summary>
        /// 清理 Clear 相关运行时状态，防止旧动作、旧窗口或旧命中结果泄漏到后续流程。
        /// </summary>
        public void Clear()
        {
            IsActive = false;
            windowName = string.Empty;
            startPosition = Vector3.zero;
            startRotation = Quaternion.identity;
            lockedTargetPoint = Vector3.zero;
            authoredTotalWorld = Vector3.zero;
            targetTotalWorld = Vector3.zero;
            ScaleFactor = 1f;
        }

        /// <summary>
        /// 开始 Begin 流程，初始化本次动作或窗口需要的运行时上下文。
        /// </summary>
        public void Begin(BossMotionWarpWindow window, Transform owner, Vector3 targetPoint)
        {
            if (window == null || owner == null)
            {
                Clear();
                return;
            }

            IsActive = true;
            windowName = window.Name;
            startPosition = Flatten(owner.position);
            startRotation = owner.rotation;
            lockedTargetPoint = Flatten(targetPoint);
            authoredTotalWorld = ResolveAuthoredTotalWorld(window);
            targetTotalWorld = Flatten(lockedTargetPoint - startPosition);
            ScaleFactor = ResolveScale(window);
        }

        /// <summary>
        /// 更新 Target 状态，并将结果写回上下文、组件或调试数据。
        /// </summary>
        public void UpdateTarget(BossMotionWarpWindow window, Vector3 targetPoint)
        {
            if (!IsActive || window == null)
            {
                return;
            }

            if (window.FollowTarget)
            {
                lockedTargetPoint = Flatten(targetPoint);
                targetTotalWorld = Flatten(lockedTargetPoint - startPosition);
                ScaleFactor = ResolveScale(window);
            }
        }

        /// <summary>
        /// 执行 Warp / Delta 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public Vector3 WarpDelta(BossMotionWarpWindow window, Vector3 rootDelta)
        {
            rootDelta = Flatten(rootDelta);
            if (!IsActive || window == null || !window.WarpTranslation || rootDelta.sqrMagnitude <= Epsilon)
            {
                return rootDelta;
            }

            if (!HasValidAuthoredTotal || targetTotalWorld.sqrMagnitude <= Epsilon)
            {
                return rootDelta;
            }

            if (window.WarpMode == BossWarpMode.ScaleTranslation)
            {
                return rootDelta * ScaleFactor;
            }

            return SkewDelta(rootDelta);
        }

        /// <summary>
        /// 解析 Authored / Total / World 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private Vector3 ResolveAuthoredTotalWorld(BossMotionWarpWindow window)
        {
            Vector3 authoredLocal = window.UseAuthoredTotalTranslation
                ? window.AuthoredTotalTranslation
                : Vector3.forward;

            Vector3 authoredWorld = Flatten(startRotation * authoredLocal);
            if (authoredWorld.sqrMagnitude <= Epsilon)
            {
                authoredWorld = Flatten(startRotation * Vector3.forward);
            }

            return authoredWorld;
        }

        /// <summary>
        /// 解析 Scale 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private float ResolveScale(BossMotionWarpWindow window)
        {
            float authoredDistance = authoredTotalWorld.magnitude;
            if (authoredDistance <= Epsilon)
            {
                return 1f;
            }

            float targetDistance = targetTotalWorld.magnitude;
            float scale = targetDistance / authoredDistance;
            if (scale > 1f && !window.AllowScaleUp)
            {
                scale = 1f;
            }

            if (scale < 1f && !window.AllowScaleDown)
            {
                scale = 1f;
            }

            float minScale = Mathf.Max(0.01f, window.MinScaleMultiplier);
            float maxScale = Mathf.Max(minScale, window.MaxScaleMultiplier);
            return Mathf.Clamp(scale, minScale, maxScale);
        }

        /// <summary>
        /// 执行 Skew / Delta 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        private Vector3 SkewDelta(Vector3 rootDelta)
        {
            Vector3 authoredDirection = authoredTotalWorld.sqrMagnitude > Epsilon
                ? authoredTotalWorld.normalized
                : Flatten(startRotation * Vector3.forward).normalized;
            Vector3 targetDirection = targetTotalWorld.sqrMagnitude > Epsilon
                ? targetTotalWorld.normalized
                : authoredDirection;

            Quaternion directionWarp = Quaternion.FromToRotation(authoredDirection, targetDirection);
            Vector3 rotated = directionWarp * rootDelta;
            return Flatten(rotated) * ScaleFactor;
        }

        /// <summary>
        /// 执行 Flatten 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        private static Vector3 Flatten(Vector3 value)
        {
            value.y = 0f;
            return value;
        }
    }
}
