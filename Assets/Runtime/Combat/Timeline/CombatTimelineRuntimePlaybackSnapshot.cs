// 文件说明：定义 Combat Timeline 编辑器运行时跟随所需的只读播放事实。
// 所属模块：Combat Timeline。
// 运行影响：仅向编辑器暴露当前动作 Timeline ID 与经过时间，不参与玩法决策。

namespace ProjectEVE.Combat.Timeline
{
    /// <summary>
    /// 当前运行时正在播放的 Combat Timeline 事实。只供 Editor 观察 playhead 进度。
    /// </summary>
    public readonly struct CombatTimelineRuntimePlaybackSnapshot
    {
        /// <summary>
        /// 创建 CombatTimelineRuntimePlaybackSnapshot 实例，并准备 Combat Timeline 模块需要的初始状态。
        /// </summary>
        public CombatTimelineRuntimePlaybackSnapshot(
            string source,
            string runtimeState,
            string actionId,
            float elapsedTime)
        {
            Source = source ?? string.Empty;
            RuntimeState = runtimeState ?? string.Empty;
            ActionId = actionId ?? string.Empty;
            ElapsedTime = elapsedTime < 0f ? 0f : elapsedTime;
        }

        /// <summary>运行时来源，例如 Player 或 Boss。</summary>
        public string Source { get; }
        /// <summary>来源当前状态，用于编辑器状态栏显示。</summary>
        public string RuntimeState { get; }
        /// <summary>当前运行时匹配的 Combat Timeline ActionId。</summary>
        public string ActionId { get; }
        /// <summary>当前运行时状态或动作已经过时间。</summary>
        public float ElapsedTime { get; }
        /// <summary>该快照是否携带有效 Timeline ActionId。</summary>
        public bool IsValid => !string.IsNullOrEmpty(ActionId);
    }
}
