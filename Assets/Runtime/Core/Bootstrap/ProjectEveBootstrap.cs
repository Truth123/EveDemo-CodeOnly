// 文件说明：维护 Demo 场景运行时引导和初始绑定。
// 所属模块：启动引导。
// 运行影响：影响场景启动时的核心对象初始化。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.Core
{
    /// <summary>
    /// 场景启动入口。后续所有运行时管理器的初始化顺序应集中在这里显式编排。
    /// </summary>
    [DefaultExecutionOrder(-10000)]
    public sealed class ProjectEveBootstrap : MonoBehaviour
    {
        /// <summary>场景中的玩家状态机引用，可手动绑定，未绑定时自动查找。</summary>
        [SerializeField] private PlayerStateMachine playerStateMachine;

        /// <summary>当前场景玩家状态机。</summary>
        public PlayerStateMachine PlayerStateMachine => playerStateMachine;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            // 只允许 Bootstrap 负责兜底查找，避免后续各系统在热路径里反复 Find。
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }
        }
    }
}
