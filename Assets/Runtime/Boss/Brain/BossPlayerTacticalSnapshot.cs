// 文件说明：定义 Boss Brain 实际消费的玩家战术状态快照。
// 所属模块：Boss Brain。
// 运行影响：只作为 Boss 出招评分输入，不读取玩家原始输入、不提交状态或动作副作用。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.Boss.Brain
{
    /// <summary>
    /// Boss 只读消费的玩家战术事实。所有字段都来自玩家已经进入的公开状态或短期历史。
    /// </summary>
    public readonly struct BossPlayerTacticalSnapshot
    {
        /// <summary>无有效玩家状态时使用的空快照。</summary>
        public static readonly BossPlayerTacticalSnapshot Empty = new BossPlayerTacticalSnapshot(
            PlayerStateId.None,
            0f,
            false,
            false);

        /// <summary>
        /// 创建玩家战术快照。
        /// </summary>
        /// <param name="currentState">玩家当前顶层状态。</param>
        /// <param name="stateElapsed">玩家进入当前状态后的秒数。</param>
        /// <param name="isFrequentGuarder">玩家是否在滚动窗口内累计了足够 Guard 时间。</param>
        /// <param name="isSustainedRetreat">玩家是否连续远离 Boss 达到配置时长。</param>
        public BossPlayerTacticalSnapshot(
            PlayerStateId currentState,
            float stateElapsed,
            bool isFrequentGuarder,
            bool isSustainedRetreat)
        {
            CurrentState = currentState;
            StateElapsed = Mathf.Max(0f, stateElapsed);
            IsFrequentGuarder = isFrequentGuarder;
            IsSustainedRetreat = isSustainedRetreat;
        }

        /// <summary>玩家当前顶层状态。</summary>
        public PlayerStateId CurrentState { get; }
        /// <summary>玩家进入当前状态后的秒数。</summary>
        public float StateElapsed { get; }
        /// <summary>玩家是否处于 Guard 状态。</summary>
        public bool IsGuarding => CurrentState == PlayerStateId.Guard;
        /// <summary>玩家是否处于 HitReaction 或 Knockdown。</summary>
        public bool IsInReaction => CurrentState == PlayerStateId.HitReaction || CurrentState == PlayerStateId.Knockdown;
        /// <summary>玩家在滚动窗口内是否频繁防御。</summary>
        public bool IsFrequentGuarder { get; }
        /// <summary>玩家是否连续后退达到配置阈值。</summary>
        public bool IsSustainedRetreat { get; }
        /// <summary>玩家是否已经持续防御到足以影响 Boss 选招倾向。</summary>
        public bool IsLongGuarding => IsFrequentGuarder || (IsGuarding && StateElapsed >= 0.6f);
    }
}
