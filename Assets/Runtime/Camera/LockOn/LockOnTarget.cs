// 文件说明：维护自由视角、锁定视角、目标选择和相机跟随。
// 所属模块：战斗相机。
// 运行影响：影响相机关注点、锁定构图、快速位移跟随和 CameraShake 叠加。

using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.CameraSystem.LockOn
{
    /// <summary>
    /// 可被玩家锁定的目标标记。Boss 根节点或胸口目标点都可以挂载该组件。
    /// </summary>
    public sealed class LockOnTarget : MonoBehaviour
    {
        private static readonly List<LockOnTarget> activeTargets = new List<LockOnTarget>();

        /// <summary>优先锁定的位置；未绑定时使用自身 Transform。</summary>
        [SerializeField] private Transform targetPoint;
        /// <summary>该目标是否允许被锁定。</summary>
        [SerializeField] private bool lockable = true;

        /// <summary>所有当前激活的锁定目标。</summary>
        public static IReadOnlyList<LockOnTarget> ActiveTargets => activeTargets;

        /// <summary>实际用于计算距离、视角和镜头朝向的锁定点。</summary>
        public Transform TargetPoint => targetPoint != null ? targetPoint : transform;

        /// <summary>目标是否当前可锁定。</summary>
        public bool IsLockable => lockable && isActiveAndEnabled;

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            if (!activeTargets.Contains(this))
            {
                activeTargets.Add(this);
            }
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            activeTargets.Remove(this);
        }
    }
}
