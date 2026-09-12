// 文件说明：维护 Boss Animator Root Motion 转发入口。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 空间位置、攻击落点和穿模保护。

using UnityEngine;
using ProjectEVE.Boss.Actor;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// 挂在 Raven Animator 子节点上，把 Animator Root Motion 转发给 Boss 正式移动系统入口。
    /// </summary>
    [RequireComponent(typeof(Animator))]
    public sealed class BossRootMotionRelay : MonoBehaviour
    {
        /// <summary>当前子节点 Animator。</summary>
        [SerializeField] private Animator animator;
        /// <summary>Boss 根对象上的正式 Actor，用于进入 MovementSystem。</summary>
        [SerializeField] private BossActor bossActor;

        [Header("Animated Root Bone")]
        [SerializeField] private Transform animatedRootBone; // FBX 里的 Root 骨骼

        [Header("Options")]
        [SerializeField] private bool resetAnimatedRootPosition = true;
        [SerializeField] private bool resetAnimatedRootRotation = false;


        private Vector3 animatedRootInitialLocalPosition;
        private Quaternion animatedRootInitialLocalRotation;


        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            BindReferences();

            if (animatedRootBone != null)
            {
                animatedRootInitialLocalPosition = animatedRootBone.localPosition;
                animatedRootInitialLocalRotation = animatedRootBone.localRotation;
            }
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();

            if (animatedRootBone != null)
            {
                animatedRootInitialLocalPosition = animatedRootBone.localPosition;
                animatedRootInitialLocalRotation = animatedRootBone.localRotation;
            }
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            BindReferences();

            if (animatedRootBone != null)
            {
                animatedRootInitialLocalPosition = animatedRootBone.localPosition;
                animatedRootInitialLocalRotation = animatedRootBone.localRotation;
            }

        }

        /// <summary>
        /// 接收 Animator Root Motion，并转交给项目位移控制逻辑统一处理。
        /// </summary>
        private void OnAnimatorMove()
        {
            if (animator == null || bossActor == null)
            {
                BindReferences();
            }

            if (animator == null || bossActor == null)
            {
                return;
            }

            Vector3 deltaPosition = animator.deltaPosition;

            Vector3 deltaPositionReal = new Vector3(deltaPosition.z, deltaPosition.y, -deltaPosition.x);
            deltaPositionReal.y = 0f;

            bossActor.HandleAnimatorRootMotion(deltaPositionReal, Quaternion.identity);
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (bossActor == null)
            {
                bossActor = GetComponentInParent<BossActor>();
            }
        }

        /// <summary>
        /// 在常规 Update 后同步最终位姿，避免读取未完成的帧状态。
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
