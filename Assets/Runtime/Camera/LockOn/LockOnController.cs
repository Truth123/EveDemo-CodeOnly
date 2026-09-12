// 文件说明：维护自由视角、锁定视角、目标选择和相机跟随。
// 所属模块：战斗相机。
// 运行影响：影响相机关注点、锁定构图、快速位移跟随和 CameraShake 叠加。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.CameraSystem.LockOn
{
    /// <summary>
    /// 玩家锁定控制器。负责从场景中的 LockOnTarget 中选择最合适的 Boss 目标。
    /// </summary>
    public sealed class LockOnController : MonoBehaviour
    {
        /// <summary>搜索锁定目标的最大距离。</summary>
        [SerializeField] private float lockOnRadius = 18f;
        /// <summary>锁定目标相对视角中心的最大角度。</summary>
        [SerializeField] private float maxViewAngle = 70f;
        /// <summary>视角参考，通常绑定主相机；未绑定时使用 Camera.main。</summary>
        [SerializeField] private Transform viewReference;
        /// <summary>锁定距离超过该倍数后自动解除锁定。</summary>
        [SerializeField] private float unlockDistanceMultiplier = 1.25f;

        private LockOnTarget currentTarget;

        /// <summary>当前锁定目标。</summary>
        public LockOnTarget CurrentTarget => currentTarget;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            if (viewReference == null && Camera.main != null)
            {
                viewReference = Camera.main.transform;
            }
        }

        /// <summary>
        /// 每帧维护锁定有效性，并把锁定状态同步到玩家状态上下文。
        /// </summary>
        public void Tick(PlayerStateContext context)
        {
            if (currentTarget != null && !IsTargetStillValid(context, currentTarget))
            {
                ClearLockOn(context);
            }

            context.LockOnTarget = currentTarget != null ? currentTarget.TargetPoint : null;
            context.IsLockOn = context.LockOnTarget != null;
            context.ControlMode = context.IsLockOn ? ControlMode.LockOn : ControlMode.Free;
        }

        /// <summary>
        /// 切换锁定状态：已锁定则解除，未锁定则尝试选择最佳目标。
        /// </summary>
        public void ToggleLockOn(PlayerStateContext context)
        {
            if (currentTarget != null)
            {
                ClearLockOn(context);
                return;
            }

            currentTarget = FindBestTarget(context);
            Tick(context);
        }

        /// <summary>
        /// 锁定调用方明确指定的目标，用于开场等不应受当前镜头朝向限制的流程。
        /// </summary>
        /// <param name="context">需要写入锁定状态的玩家共享上下文。</param>
        /// <param name="target">需要锁定的正式 LockOnTarget；必须可锁定且处于获取半径内。</param>
        /// <returns>true 表示目标通过校验并已写入上下文；false 表示目标无效或距离过远。</returns>
        public bool TryLockOnTarget(PlayerStateContext context, LockOnTarget target)
        {
            if (context == null || target == null || !target.IsLockable)
            {
                return false;
            }

            Transform owner = context.PlayerTransform != null ? context.PlayerTransform : transform;
            if (Vector3.Distance(owner.position, target.TargetPoint.position) > lockOnRadius)
            {
                return false;
            }

            currentTarget = target;
            Tick(context);
            return context.IsLockOn;
        }

        /// <summary>
        /// 按当前视角规则锁定评分最高的目标，供没有显式目标的外部战斗流程使用。
        /// </summary>
        /// <param name="context">需要写入锁定状态的玩家共享上下文。</param>
        /// <returns>true 表示找到并锁定目标；false 表示当前视野内没有合法目标。</returns>
        public bool TryLockOnBestTarget(PlayerStateContext context)
        {
            if (context == null)
            {
                return false;
            }

            currentTarget = FindBestTarget(context);
            Tick(context);
            return context.IsLockOn;
        }

        /// <summary>
        /// 清除当前锁定目标并恢复自由控制模式。
        /// </summary>
        public void ClearLockOn(PlayerStateContext context)
        {
            currentTarget = null;
            context.LockOnTarget = null;
            context.IsLockOn = false;
            context.ControlMode = ControlMode.Free;
        }

        /// <summary>
        /// 查找 Best / Target 对象或数据，作为后续绑定、校验或显示的输入。
        /// </summary>
        private LockOnTarget FindBestTarget(PlayerStateContext context)
        {
            Transform owner = context.PlayerTransform != null ? context.PlayerTransform : transform;
            Transform view = viewReference != null ? viewReference : owner;
            LockOnTarget bestTarget = null;
            float bestScore = float.MaxValue;

            foreach (LockOnTarget target in LockOnTarget.ActiveTargets)
            {
                if (target == null || !target.IsLockable)
                {
                    continue;
                }

                Vector3 toTarget = target.TargetPoint.position - owner.position;
                float distance = toTarget.magnitude;

                if (distance > lockOnRadius)
                {
                    continue;
                }

                float angle = Vector3.Angle(view.forward, toTarget);

                if (angle > maxViewAngle)
                {
                    continue;
                }

                float score = distance + angle * 0.08f;
                if (score < bestScore)
                {
                    bestScore = score;
                    bestTarget = target;
                }
            }

            return bestTarget;
        }

        /// <summary>
        /// 判断当前对象是否处于 Target / Still / Valid 状态，避免调用方直接读取内部实现细节。
        /// </summary>
        private bool IsTargetStillValid(PlayerStateContext context, LockOnTarget target)
        {
            if (target == null || !target.IsLockable)
            {
                return false;
            }

            Transform owner = context.PlayerTransform != null ? context.PlayerTransform : transform;
            float maxDistance = lockOnRadius * unlockDistanceMultiplier;
            return Vector3.Distance(owner.position, target.TargetPoint.position) <= maxDistance;
        }
    }
}
