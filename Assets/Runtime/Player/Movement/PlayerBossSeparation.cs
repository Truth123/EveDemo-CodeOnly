// 文件说明：维护玩家普通移动、动作位移、Root Motion 和独立于锁定状态的 Boss 软互斥站位；不负责 Boss 攻击落点求解。
// 所属模块：玩家移动。
// 运行影响：影响玩家 CharacterController 位移、动作位移裁剪和动画位移反馈。

using UnityEngine;

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// Maintains soft player-vs-boss positioning when their CharacterControllers do not collide.
    /// Damage still comes only from Hitbox/Hurtbox; this component only clips body movement and does not solve Boss attack landing.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    public sealed class PlayerBossSeparation : MonoBehaviour
    {
        [Header("Target")]
        [Tooltip("Boss 身体在水平面上的互斥中心。该引用独立于锁定目标，取消锁定后仍会阻止玩家穿过 Boss。")]
        [SerializeField] private Transform bossBodyCenter;

        [Header("Safety Distance")]
        [SerializeField] private bool separationEnabled = true;
        [SerializeField] private float movementSafeDistance = 0.6f;
        [SerializeField] private float evadeSafeDistance = 0.6f;
        [SerializeField] private float attackSafeDistance = 1.15f;
        [SerializeField] private float skillSafeDistance = 1.15f;
        [SerializeField] private float fallbackSafeDistance = 1.0f;

        [Header("Sliding")]
        [Range(0f, 1f)]
        [SerializeField] private float movementTangentSlideScale = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float evadeTangentSlideScale = 1f;
        [Range(0f, 1f)]
        [SerializeField] private float attackTangentSlideScale = 0f;
        [Range(0f, 1f)]
        [SerializeField] private float skillTangentSlideScale = 0f;

        [Header("Post Correction")]
        [SerializeField] private float allowedContactError = 0.1f;
        [SerializeField] private float maxPostCorrectionPerFrame = 0.12f;

        private CharacterController characterController;
        private bool suppressOrdinarySeparation;

        public bool SeparationEnabled
        {
            get => separationEnabled;
            set => separationEnabled = value;
        }

        public bool SuppressOrdinarySeparation
        {
            get => suppressOrdinarySeparation;
            set => suppressOrdinarySeparation = value;
        }

        /// <summary>Boss 身体在水平面上的互斥中心；与相机锁定状态无关。</summary>
        public Transform BossBodyCenter
        {
            get => bossBodyCenter;
            set => bossBodyCenter = value;
        }

        public Vector3 LastClippedDelta { get; private set; }
        public Vector3 LastPostCorrectionDelta { get; private set; }
        public float LastSafeDistance { get; private set; }

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

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            movementSafeDistance = Mathf.Max(0f, movementSafeDistance);
            evadeSafeDistance = Mathf.Max(0f, evadeSafeDistance);
            attackSafeDistance = Mathf.Max(0f, attackSafeDistance);
            skillSafeDistance = Mathf.Max(0f, skillSafeDistance);
            fallbackSafeDistance = Mathf.Max(0f, fallbackSafeDistance);
            allowedContactError = Mathf.Max(0f, allowedContactError);
            maxPostCorrectionPerFrame = Mathf.Max(0f, maxPostCorrectionPerFrame);
            BindReferences();
        }

        /// <summary>
        /// 执行 Clip / Horizontal / Delta 相关逻辑，并维护 玩家移动 模块的运行时一致性。
        /// </summary>
        public Vector3 ClipHorizontalDelta(PlayerStateContext context, Vector3 requestedDelta)
        {
            LastClippedDelta = Vector3.zero;
            requestedDelta.y = 0f;

            if (!CanApply(context) || requestedDelta.sqrMagnitude <= 0.000001f)
            {
                return requestedDelta;
            }

            Vector3 fromBoss = ResolveFromBossDirection(transform.position, out float distance);
            if (fromBoss.sqrMagnitude <= 0.0001f)
            {
                return requestedDelta;
            }

            float safeDistance = ResolveSafeDistance(context.CurrentState);
            float tangentScale = ResolveTangentSlideScale(context.CurrentState);
            LastSafeDistance = safeDistance;

            Vector3 inwardDirection = -fromBoss;
            float inwardAmount = Vector3.Dot(requestedDelta, inwardDirection);
            Vector3 inwardDelta = inwardAmount > 0f ? inwardDirection * inwardAmount : Vector3.zero;
            Vector3 nonInwardDelta = requestedDelta - inwardDelta;

            if (tangentScale < 0.999f)
            {
                Vector3 tangent = Vector3.ProjectOnPlane(nonInwardDelta, fromBoss);
                Vector3 radialAway = nonInwardDelta - tangent;
                nonInwardDelta = radialAway + tangent * tangentScale;
            }

            if (inwardAmount <= 0f)
            {
                return nonInwardDelta;
            }

            float allowedInward = Mathf.Max(0f, distance - safeDistance);
            Vector3 clippedInwardDelta = inwardDirection * Mathf.Min(inwardAmount, allowedInward);
            Vector3 clippedDelta = nonInwardDelta + clippedInwardDelta;
            LastClippedDelta = requestedDelta - clippedDelta;
            return clippedDelta;
        }

        /// <summary>
        /// 执行 Post / Correct 相关逻辑，并维护 玩家移动 模块的运行时一致性。
        /// </summary>
        public CollisionFlags PostCorrect(PlayerStateContext context)
        {
            LastPostCorrectionDelta = Vector3.zero;

            if (!CanApply(context))
            {
                return CollisionFlags.None;
            }

            Vector3 fromBoss = ResolveFromBossDirection(transform.position, out float distance);
            if (fromBoss.sqrMagnitude <= 0.0001f)
            {
                return CollisionFlags.None;
            }

            // float safeDistance = ResolveSafeDistance(context.CurrentState);
            float safeDistance = movementSafeDistance;  // 使用最小的安全距离来避免在 PostCorrect 时出现过度纠正
            LastSafeDistance = safeDistance;
            float penetration = safeDistance - distance;
            if (penetration <= allowedContactError)
            {
                return CollisionFlags.None;
            }

            Vector3 correction = fromBoss * Mathf.Min(penetration, maxPostCorrectionPerFrame);
            Vector3 before = transform.position;
            CollisionFlags flags = characterController != null
                ? characterController.Move(correction)
                : CollisionFlags.None;
            Vector3 appliedCorrection = transform.position - before;
            appliedCorrection.y = 0f;
            LastPostCorrectionDelta = appliedCorrection;
            return flags;
        }

        /// <summary>
        /// 检查当前条件是否允许应用身体互斥；锁定状态不参与该判断。
        /// </summary>
        /// <param name="context">当前玩家状态上下文，用于选择状态对应的安全距离。</param>
        /// <returns>启用互斥、未被动作临时抑制且身体中心与状态上下文均有效时返回 true；否则返回 false。</returns>
        private bool CanApply(PlayerStateContext context)
        {
            return separationEnabled &&
                !suppressOrdinarySeparation &&
                context != null &&
                bossBodyCenter != null;
        }

        /// <summary>
        /// 计算玩家相对 Boss 身体中心的水平单位方向和距离；完全重合时使用玩家反向作为推出方向。
        /// </summary>
        /// <param name="playerPosition">玩家当前世界坐标。</param>
        /// <param name="distance">写回玩家与 Boss 身体中心的水平距离，单位为米。</param>
        /// <returns>从 Boss 指向玩家的水平单位方向。</returns>
        private Vector3 ResolveFromBossDirection(Vector3 playerPosition, out float distance)
        {
            Vector3 fromBoss = playerPosition - bossBodyCenter.position;
            fromBoss.y = 0f;
            distance = fromBoss.magnitude;
            if (distance > 0.0001f)
            {
                return fromBoss / distance;
            }

            Vector3 fallback = -transform.forward;
            fallback.y = 0f;
            if (fallback.sqrMagnitude <= 0.0001f)
            {
                fallback = Vector3.back;
            }

            distance = 0f;
            return fallback.normalized;
        }

        /// <summary>
        /// 解析 Safe / Distance 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private float ResolveSafeDistance(PlayerStateId stateId)
        {
            switch (stateId)
            {
                case PlayerStateId.Attack:
                    return attackSafeDistance;
                case PlayerStateId.Evade:
                    return evadeSafeDistance;
                case PlayerStateId.Skill:
                    return skillSafeDistance;
                case PlayerStateId.Idle:
                case PlayerStateId.Locomotion:
                case PlayerStateId.Guard:
                    return movementSafeDistance;
                default:
                    return fallbackSafeDistance;
            }
        }

        /// <summary>
        /// 解析 Tangent / Slide / Scale 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private float ResolveTangentSlideScale(PlayerStateId stateId)
        {
            switch (stateId)
            {
                case PlayerStateId.Attack:
                    return attackTangentSlideScale;
                case PlayerStateId.Evade:
                    return evadeTangentSlideScale;
                case PlayerStateId.Skill:
                    return skillTangentSlideScale;
                case PlayerStateId.Idle:
                case PlayerStateId.Locomotion:
                case PlayerStateId.Guard:
                    return movementTangentSlideScale;
                default:
                    return movementTangentSlideScale;
            }
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (characterController == null)
            {
                characterController = GetComponent<CharacterController>();
            }
        }
    }
}
