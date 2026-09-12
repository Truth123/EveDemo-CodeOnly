// 文件说明：定义 Combat Timeline Provider 返回的状态动作数据。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player 状态进入时获取 Timeline 静态数据与 RuntimeSpec 的方式。

using ProjectEVE.Player.Windows;
using System;

namespace ProjectEVE.Combat.Timeline
{
    /// <summary>
    /// Player Evade 状态进入时获取的动作数据。
    /// </summary>
    public sealed class PlayerEvadeActionData
    {
        public PlayerEvadeActionData(string actionId, float totalDuration, CombatActionRuntimeSpec runtimeSpec)
        {
            ActionId = actionId ?? string.Empty;
            TotalDuration = Math.Max(0f, totalDuration);
            RuntimeSpec = runtimeSpec ?? throw new ArgumentNullException(nameof(runtimeSpec));
        }

        public string ActionId { get; }
        public float TotalDuration { get; }
        public CombatActionRuntimeSpec RuntimeSpec { get; }
    }

    /// <summary>
    /// Player Guard 状态进入时获取的动作数据。Guard mode 生命周期仍由 PlayerGuardState 维护。
    /// </summary>
    public sealed class PlayerGuardActionData
    {
        public PlayerGuardActionData(
            string actionId,
            float totalDuration,
            CombatActionRuntimeSpec runtimeSpec,
            float guardHitReactionDuration,
            float perfectGuardReactionDuration,
            float perfectGuardChainActiveDuration,
            PlayerGuardReactionCancelData guardHitCancelData,
            PlayerGuardReactionCancelData perfectGuardCancelData,
            PlayerGuardReactionCancelData guardReleaseCancelData)
        {
            ActionId = actionId ?? string.Empty;
            TotalDuration = Math.Max(0f, totalDuration);
            RuntimeSpec = runtimeSpec ?? throw new ArgumentNullException(nameof(runtimeSpec));
            GuardHitReactionDuration = Math.Max(0f, guardHitReactionDuration);
            PerfectGuardReactionDuration = Math.Max(0f, perfectGuardReactionDuration);
            PerfectGuardChainActiveDuration = Math.Max(0f, perfectGuardChainActiveDuration);
            GuardHitCancelData = guardHitCancelData;
            PerfectGuardCancelData = perfectGuardCancelData;
            GuardReleaseCancelData = guardReleaseCancelData;
        }

        public string ActionId { get; }
        public float TotalDuration { get; }
        public CombatActionRuntimeSpec RuntimeSpec { get; }
        public float GuardHitReactionDuration { get; }
        public float PerfectGuardReactionDuration { get; }
        public float PerfectGuardChainActiveDuration { get; }
        public PlayerGuardReactionCancelData GuardHitCancelData { get; }
        public PlayerGuardReactionCancelData PerfectGuardCancelData { get; }
        public PlayerGuardReactionCancelData GuardReleaseCancelData { get; }
    }

    /// <summary>
    /// Guard 内部轨道解析出的窗口集合。GuardHit、PerfectGuard 与 GuardRelease 各自持有独立实例，禁止跨轨道混读。
    /// GuardHit / PerfectGuard 当前只允许 GuardReleaseCancelWindows；其余 Buffer / Cancel 数组仅供 GuardRelease 使用。
    /// </summary>
    public readonly struct PlayerGuardReactionCancelData
    {
        private static readonly ActionWindow[] EmptyWindows = Array.Empty<ActionWindow>();

        public PlayerGuardReactionCancelData(
            ActionWindow[] evadeBufferWindows,
            ActionWindow[] skillBufferWindows,
            ActionWindow[] evadeCancelWindows,
            ActionWindow[] skillCancelWindows,
            ActionWindow[] attackResetWindows,
            ActionWindow[] guardReleaseCancelWindows)
        {
            EvadeBufferWindows = evadeBufferWindows ?? EmptyWindows;
            SkillBufferWindows = skillBufferWindows ?? EmptyWindows;
            EvadeCancelWindows = evadeCancelWindows ?? EmptyWindows;
            SkillCancelWindows = skillCancelWindows ?? EmptyWindows;
            AttackResetWindows = attackResetWindows ?? EmptyWindows;
            GuardReleaseCancelWindows = guardReleaseCancelWindows ?? EmptyWindows;
        }

        public ActionWindow[] EvadeBufferWindows { get; }
        public ActionWindow[] SkillBufferWindows { get; }
        public ActionWindow[] EvadeCancelWindows { get; }
        public ActionWindow[] SkillCancelWindows { get; }
        public ActionWindow[] AttackResetWindows { get; }
        public ActionWindow[] GuardReleaseCancelWindows { get; }

        public bool CanBufferEvade(float elapsedTime)
        {
            return IsActive(EvadeBufferWindows, elapsedTime);
        }

        public bool CanBufferSkill(float elapsedTime)
        {
            return IsActive(SkillBufferWindows, elapsedTime);
        }

        public bool CanCancelToEvade(float elapsedTime)
        {
            return IsActive(EvadeCancelWindows, elapsedTime);
        }

        public bool CanCancelToSkill(float elapsedTime)
        {
            return IsActive(SkillCancelWindows, elapsedTime);
        }

        public bool CanResetAttack(float elapsedTime)
        {
            return IsActive(AttackResetWindows, elapsedTime);
        }

        public bool CanCancelToGuardRelease(float elapsedTime)
        {
            return IsActive(GuardReleaseCancelWindows, elapsedTime);
        }

        public float GetMaxEndTime()
        {
            float endTime = 0f;
            AccumulateMaxEndTime(EvadeBufferWindows, ref endTime);
            AccumulateMaxEndTime(SkillBufferWindows, ref endTime);
            AccumulateMaxEndTime(EvadeCancelWindows, ref endTime);
            AccumulateMaxEndTime(SkillCancelWindows, ref endTime);
            AccumulateMaxEndTime(AttackResetWindows, ref endTime);
            AccumulateMaxEndTime(GuardReleaseCancelWindows, ref endTime);
            return endTime;
        }

        private static bool IsActive(ActionWindow[] windows, float elapsedTime)
        {
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i].Contains(elapsedTime))
                {
                    return true;
                }
            }

            return false;
        }

        private static void AccumulateMaxEndTime(ActionWindow[] windows, ref float endTime)
        {
            for (int i = 0; i < windows.Length; i++)
            {
                endTime = Math.Max(endTime, windows[i].EndTime);
            }
        }
    }
}
