// 文件说明：维护玩家普通移动、动作位移、Root Motion 和 Boss 软互斥站位。
// 所属模块：玩家移动。
// 运行影响：影响玩家 CharacterController 位移、动作位移裁剪和动画位移反馈。

using UnityEngine;

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// 捕获玩家 Animator Root Motion，并转交给 PlayerMovementMotor 通过 CharacterController 执行。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class PlayerRootMotionRelay : MonoBehaviour
    {
        [SerializeField] private Animator animator;
        [SerializeField] private PlayerMovementMotor movementMotor;

        [Header("Animated Root Bone")]
        [SerializeField] private Transform animatedRootBone; // FBX 里的 Root 骨骼

        [Header("Options")]
        [SerializeField] private bool resetAnimatedRootPosition = true;
        [SerializeField] private bool resetAnimatedRootRotation = false;
        [SerializeField] private bool ignoreVerticalRootMotion = true;

        private Vector3 animatedRootInitialLocalPosition;
        private Quaternion animatedRootInitialLocalRotation;

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
            BindReferences();
        }

        /// <summary>
        /// 接收 Animator Root Motion，并转交给项目位移控制逻辑统一处理。
        /// </summary>
        private void OnAnimatorMove()
        {

            BindReferences();
            if (animator == null || movementMotor == null)
            {
                return;
            }

            Vector3 deltaPosition = animator.deltaPosition;
            Vector3 deltaPositionReal = new Vector3(deltaPosition.z, deltaPosition.y, -deltaPosition.x);

            if (ignoreVerticalRootMotion)
            {
                deltaPositionReal.y = 0f;
            }

            movementMotor.HandleRootMotion(deltaPositionReal, Quaternion.identity);
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (animator == null)
            {
                Animator foundAnimator = GetComponent<Animator>();
                if (foundAnimator != null)
                {
                    animator = foundAnimator;
                }
            }

            if (movementMotor == null)
            {
                PlayerMovementMotor foundMotor = GetComponentInParent<PlayerMovementMotor>();
                if (foundMotor != null)
                {
                    movementMotor = foundMotor;
                }
            }

            if (animatedRootBone != null)
            {
                animatedRootInitialLocalPosition = animatedRootBone.localPosition;
                animatedRootInitialLocalRotation = animatedRootBone.localRotation;
            }
        }

        /// <summary>
        /// 在常规 Update 后同步最终位姿、相机或调试显示，避免读取未完成的帧状态。
        /// </summary>
        private void LateUpdate()
        {
            if (animatedRootBone != null)
            {
                if (resetAnimatedRootPosition)
                {
                    animatedRootBone.localPosition = animatedRootInitialLocalPosition;
                }

                if (resetAnimatedRootRotation)
                {
                    animatedRootBone.localRotation = animatedRootInitialLocalRotation;
                }
            }
        }
    }
}
