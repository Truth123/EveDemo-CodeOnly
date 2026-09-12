// 文件说明：定义玩家代码动作位移配置，包括固定方向、绕目标与锁定目标吸附参数。
// 所属模块：玩家移动。
// 运行影响：影响 Skill、Evade 与 Reaction 的位移距离、节奏、目标约束和侧碰停止规则。

using System;
using UnityEngine;

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// 玩家动作位移的目标解析方式。FixedDirection 保持起手方向，LockOnTarget 在窗口内持续朝当前锁定目标收敛。
    /// </summary>
    public enum PlayerActionMotionTargetMode
    {
        FixedDirection = 0,
        LockOnTarget = 1
    }

    /// <summary>
    /// 玩家代码驱动动作位移配置。Attack 可使用动画 Root Motion，Skill / Evade / Reaction 使用这里的配置。
    /// </summary>
    [CreateAssetMenu(menuName = "Project EVE/Player/Action Motion Profile", fileName = "EveActionMotionProfile")]
    public sealed class PlayerActionMotionProfile : ScriptableObject
    {
        /// <summary>
        /// 尝试执行 Get / Motion，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        [SerializeField] private PlayerActionMotionConfig[] motions = Array.Empty<PlayerActionMotionConfig>();

        public bool TryGetMotion(PlayerActionMotionId motionId, out PlayerActionMotionConfig config)
        {
            if (motionId == PlayerActionMotionId.None)
            {
                config = null;
                return false;
            }

            for (int i = 0; i < motions.Length; i++)
            {
                PlayerActionMotionConfig candidate = motions[i];
                if (candidate != null && candidate.MotionId == motionId)
                {
                    config = candidate;
                    return true;
                }
            }

            config = null;
            return false;
        }


        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            if (motions == null)
            {
                motions = Array.Empty<PlayerActionMotionConfig>();
                return;
            }

            for (int i = 0; i < motions.Length; i++)
            {
                motions[i]?.Validate();
            }
        }
    }

    [Serializable]
    public sealed class PlayerActionMotionConfig
    {
        [SerializeField] private PlayerActionMotionId motionId = PlayerActionMotionId.None;
        [SerializeField] private float duration = 0.3f;
        [SerializeField] private float distance = 1f;
        [SerializeField] private AnimationCurve normalizedDistanceCurve = DefaultDistanceCurve;
        [SerializeField] private bool stopOnSideCollision;
        [SerializeField] private PlayerActionMotionTargetMode targetMode = PlayerActionMotionTargetMode.FixedDirection;
        [Min(0f)] [SerializeField] private float maxTargetDistance;
        [Range(0f, 180f)] [SerializeField] private float maxTargetAngle;
        [Min(0f)] [SerializeField] private float stopDistance;

        public PlayerActionMotionId MotionId => motionId;
        public float Duration => duration;
        public float Distance => distance;
        public bool StopOnSideCollision => stopOnSideCollision;
        public PlayerActionMotionTargetMode TargetMode => targetMode;
        public float MaxTargetDistance => maxTargetDistance;
        public float MaxTargetAngle => maxTargetAngle;
        public float StopDistance => stopDistance;

        /// <summary>
        /// 按归一化时间求值本次动作已完成的累计距离比例，并把结果钳制到 0 到 1。
        /// </summary>
        /// <param name="normalizedTime">动作经过时长占配置 Duration 的比例。</param>
        /// <returns>当前应完成的累计距离比例，范围为 0 到 1。</returns>
        public float EvaluateDistance01(float normalizedTime)
        {
            if (normalizedDistanceCurve == null || normalizedDistanceCurve.length == 0)
            {
                return Mathf.Clamp01(normalizedTime);
            }

            return Mathf.Clamp01(normalizedDistanceCurve.Evaluate(Mathf.Clamp01(normalizedTime)));
        }


        /// <summary>
        /// 钳制动作位移时长、距离和目标阈值，并在曲线缺失时恢复默认累计距离曲线。
        /// </summary>
        public void Validate()
        {
            duration = Mathf.Max(0.01f, duration);
            distance = Mathf.Max(0f, distance);
            maxTargetDistance = Mathf.Max(0f, maxTargetDistance);
            maxTargetAngle = Mathf.Clamp(maxTargetAngle, 0f, 180f);
            stopDistance = Mathf.Max(0f, stopDistance);
            if (targetMode == PlayerActionMotionTargetMode.LockOnTarget && maxTargetDistance > 0f)
            {
                stopDistance = Mathf.Min(stopDistance, maxTargetDistance);
            }

            if (normalizedDistanceCurve == null || normalizedDistanceCurve.length == 0)
            {
                normalizedDistanceCurve = DefaultDistanceCurve;
            }
        }

        private static AnimationCurve DefaultDistanceCurve
        {
            get
            {
                return new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.2f, 0.55f),
                    new Keyframe(0.65f, 0.95f),
                    new Keyframe(1f, 1f));
            }
        }
    }
}
