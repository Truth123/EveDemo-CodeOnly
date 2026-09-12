// 文件说明：统一保存 Boss 决策所需的动作选择、战术节奏、连续受击和玩家行为短期记忆。
// 所属模块：Boss Brain。
// 运行影响：影响动作冷却、重复选择、压力节奏、受击反制和玩家行为偏好，不直接启动动作或驱动表现。

using ProjectEVE.Boss.AI;
using ProjectEVE.Player;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Brain
{
    /// <summary>
    /// 由 BossActor 独占的决策记忆。Brain 和 Selector 只读取 Actor 组装的输入，不直接修改此对象。
    /// </summary>
    public sealed class BossDecisionMemory
    {
        private const int RecentActionCapacity = 3;
        private const float RepositionActionPressureReduction = 20f;

        private readonly Dictionary<string, float> nextActionTimes = new Dictionary<string, float>();
        private readonly string[] recentActionIds = new string[RecentActionCapacity];
        private readonly Queue<TimedDuration> guardSamples = new Queue<TimedDuration>();

        private string lastSelectedActionId = string.Empty;
        private int repeatedSelectedActionCount;
        private float tempoPressure;
        private float pendingAttackPressure;
        private bool firstActiveHitPressureCommitted;
        private bool pressureDecayMode;
        private int consecutiveAttackActionCount;
        private bool tempoResetRequired;
        private int recentReactionHitCount;
        private float lastReactionHitTime;
        private float guardDurationInWindow;
        private float movingAwayDuration;

        /// <summary>最近一次成功启动的 Action ID。</summary>
        public string LastSelectedActionId => lastSelectedActionId;
        /// <summary>最近一次成功启动 Action 的连续重复次数。</summary>
        public int RepeatedSelectedActionCount => repeatedSelectedActionCount;
        /// <summary>当前主动进攻压力，范围 0–100。</summary>
        public float TempoPressure => tempoPressure;
        /// <summary>当前压力是否处于带滞回的降压模式。</summary>
        public bool PressureDecayMode => pressureDecayMode;
        /// <summary>未穿插合格中立行为的连续攻击次数。</summary>
        public int ConsecutiveAttackActionCount => consecutiveAttackActionCount;
        /// <summary>最近三个成功启动的 Action ID，最新项在前。</summary>
        public IReadOnlyList<string> RecentActionIds => recentActionIds;
        /// <summary>上一动作是否要求先完成一次中立行为。</summary>
        public bool TempoResetRequired => tempoResetRequired;
        /// <summary>当前连续普通受击序列已经记录的命中次数。</summary>
        public int RecentReactionHitCount => recentReactionHitCount;
        /// <summary>玩家在滚动窗口内是否频繁防御。</summary>
        public bool IsFrequentGuarder { get; private set; }
        /// <summary>玩家是否连续远离 Boss 达到配置阈值。</summary>
        public bool IsSustainedRetreat { get; private set; }

        /// <summary>
        /// 查询指定 Action 剩余冷却。
        /// </summary>
        /// <param name="actionId">需要查询的 Action ID。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <returns>剩余冷却秒数；未知 Action、空 ID 或冷却结束时返回 0。</returns>
        public float GetActionCooldownRemaining(string actionId, float now)
        {
            if (string.IsNullOrEmpty(actionId) || !nextActionTimes.TryGetValue(actionId, out float nextTime))
            {
                return 0f;
            }

            return Mathf.Max(0f, nextTime - now);
        }

        /// <summary>
        /// 只读检查 Action 定义有效且当前冷却已经结束。
        /// </summary>
        /// <param name="actionDefinition">需要检查的 Timeline 运行时定义。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <returns>true 表示定义有效且没有剩余冷却；false 表示定义无效或仍在冷却。</returns>
        public bool CanSelectReadOnly(BossAttackDefinition actionDefinition, float now)
        {
            return actionDefinition != null &&
                GetActionCooldownRemaining(actionDefinition.AttackId, now) <= 0f;
        }

        /// <summary>
        /// 提交一次已经成功启动的 Action，更新冷却、最近选择和连续重复次数。
        /// </summary>
        /// <param name="actionDefinition">已成功启动的 Timeline 运行时定义。</param>
        /// <param name="cooldownStartTime">本动作开始计算冷却的运行时秒数。</param>
        public void CommitSelection(BossAttackDefinition actionDefinition, float cooldownStartTime)
        {
            if (actionDefinition == null)
            {
                return;
            }

            string actionId = actionDefinition.AttackId ?? string.Empty;
            nextActionTimes[actionId] = cooldownStartTime + actionDefinition.Cooldown;

            if (actionId == lastSelectedActionId)
            {
                repeatedSelectedActionCount++;
                return;
            }

            lastSelectedActionId = actionId;
            repeatedSelectedActionCount = 1;
        }

        /// <summary>
        /// 仅启动指定攻击的冷却，不写入最近选择和重复次数。
        /// </summary>
        /// <param name="actionDefinition">需要进入初始冷却的正式攻击定义。</param>
        /// <param name="cooldownStartTime">开始计算冷却的运行时秒数。</param>
        public void BeginCooldown(BossAttackDefinition actionDefinition, float cooldownStartTime)
        {
            if (actionDefinition == null || string.IsNullOrEmpty(actionDefinition.AttackId))
            {
                return;
            }

            float nextTime = cooldownStartTime + Mathf.Max(0f, actionDefinition.Cooldown);
            if (nextActionTimes.TryGetValue(actionDefinition.AttackId, out float existingNextTime))
            {
                nextTime = Mathf.Max(nextTime, existingNextTime);
            }

            nextActionTimes[actionDefinition.AttackId] = nextTime;
        }

        /// <summary>
        /// 记录一次成功启动的 Boss Action。所有动作进入最近历史；Attack 增压，Reposition 一次性降压。
        /// </summary>
        /// <param name="actionId">成功启动的 Boss Action ID。</param>
        /// <param name="actionKind">来自 ActionSet 的显式动作类别。</param>
        /// <param name="pressureCost">Attack 增加的主动进攻压力。</param>
        /// <param name="requiresTempoReset">Attack 结束后是否要求先完成一次中立行为。</param>
        public void RecordAction(
            string actionId,
            BossActionKind actionKind,
            float pressureCost,
            bool requiresTempoReset)
        {
            ShiftRecentAction(actionId ?? string.Empty);
            if (actionKind == BossActionKind.Reposition)
            {
                ReducePressure(RepositionActionPressureReduction);
                return;
            }

            if (actionKind != BossActionKind.Attack)
            {
                return;
            }

            float safeCost = Mathf.Max(0f, pressureCost);
            AddPressure(safeCost * 0.7f);
            pendingAttackPressure = safeCost * 0.3f;
            firstActiveHitPressureCommitted = false;
            consecutiveAttackActionCount++;
            tempoResetRequired |= requiresTempoReset;
        }

        /// <summary>当前攻击第一次进入任一有效 HitNode 时提交剩余 30% 压力；同一攻击只生效一次。</summary>
        public void RecordFirstActiveHit()
        {
            if (firstActiveHitPressureCommitted)
            {
                return;
            }

            firstActiveHitPressureCommitted = true;
            AddPressure(pendingAttackPressure);
            pendingAttackPressure = 0f;
        }

        /// <summary>
        /// 增加主动进攻压力并钳制到 0–100。
        /// </summary>
        /// <param name="amount">需要增加的非负压力值；负值按 0 处理。</param>
        public void AddPressure(float amount)
        {
            tempoPressure = Mathf.Clamp(tempoPressure + Mathf.Max(0f, amount), 0f, 100f);
        }

        /// <summary>
        /// 降低主动进攻压力并钳制到 0–100。
        /// </summary>
        /// <param name="amount">需要降低的非负压力值；负值按 0 处理。</param>
        public void ReducePressure(float amount)
        {
            tempoPressure = Mathf.Clamp(tempoPressure - Mathf.Max(0f, amount), 0f, 100f);
        }

        /// <summary>
        /// 按 Boss 当前状态持续衰减压力；Attack 不衰减，玩家受击硬直时暂停自然衰减。
        /// </summary>
        /// <param name="state">当前 Boss 叶子状态。</param>
        /// <param name="activeActionKind">Action 状态下的动作类别；非 Action 状态传 null。</param>
        /// <param name="playerInHitStun">玩家是否处于 HitReaction 或 Knockdown。</param>
        /// <param name="deltaTime">本帧经过时间，单位为秒。</param>
        public void TickPressure(
            BossStateId state,
            BossActionKind? activeActionKind,
            bool playerInHitStun,
            float deltaTime)
        {
            if (deltaTime <= 0f || playerInHitStun)
            {
                return;
            }

            float decayPerSecond = state == BossStateId.Action
                ? activeActionKind == BossActionKind.Reposition ? 10f : 0f
                : state switch
            {
                BossStateId.Idle => 10f,
                BossStateId.Strafe => 10f,
                BossStateId.Approach => 6f,
                BossStateId.Recovery => 8f,
                BossStateId.HitStagger => 6f,
                BossStateId.Knockdown => 10f,
                BossStateId.ShieldBreakStun => 10f,
                _ => 0f
            };
            ReducePressure(decayPerSecond * deltaTime);
        }

        /// <summary>
        /// 按当前阶段阈值刷新带滞回的降压模式。
        /// </summary>
        /// <param name="profile">当前阶段的压力进入与退出阈值。</param>
        public void UpdatePressureMode(BossPhasePressureProfile profile)
        {
            pressureDecayMode = pressureDecayMode
                ? tempoPressure > profile.ExitDecayPressure
                : tempoPressure >= profile.EnterDecayPressure;
        }

        /// <summary>记录一次合格的非攻击中立行为，清除连续攻击和强制中立要求。</summary>
        public void RecordTempoReset()
        {
            consecutiveAttackActionCount = 0;
            tempoResetRequired = false;
        }

        /// <summary>
        /// 判断当前普通受击序列是否还能记录一次 HitStagger 刷新。
        /// </summary>
        /// <param name="now">当前运行时秒数。</param>
        /// <param name="reactionHitSequenceWindow">连续受击序列的过期窗口，单位为秒。</param>
        /// <param name="maxHitCount">同一序列允许记录的最大普通受击次数。</param>
        /// <returns>true 表示可以刷新并计数；false 表示序列已经达到上限。</returns>
        public bool CanRecordReactionHitInSequence(float now, float reactionHitSequenceWindow, int maxHitCount)
        {
            ResetExpiredReactionHitSequence(now, reactionHitSequenceWindow);
            return recentReactionHitCount < Mathf.Max(0, maxHitCount);
        }

        /// <summary>
        /// 记录一次已经通过门控的普通受击硬直命中。
        /// </summary>
        /// <param name="now">本次受击发生的运行时秒数。</param>
        /// <param name="reactionHitSequenceWindow">连续受击序列的过期窗口，单位为秒。</param>
        public void RecordReactionHit(float now, float reactionHitSequenceWindow)
        {
            ResetExpiredReactionHitSequence(now, reactionHitSequenceWindow);
            recentReactionHitCount++;
            lastReactionHitTime = now;
        }

        /// <summary>
        /// 记录一次被刷新次数上限拒绝的普通受击，使当前序列继续从本次命中时间计算过期。
        /// </summary>
        /// <param name="now">本次受击发生的运行时秒数。</param>
        /// <param name="reactionHitSequenceWindow">连续受击序列的过期窗口，单位为秒。</param>
        public void MarkReactionHitSequenceLimitReached(float now, float reactionHitSequenceWindow)
        {
            ResetExpiredReactionHitSequence(now, reactionHitSequenceWindow);
            lastReactionHitTime = now;
        }

        /// <summary>
        /// 推进玩家公开行为统计，并刷新频繁防御和持续后退事实。
        /// </summary>
        /// <param name="now">当前运行时秒数。</param>
        /// <param name="deltaTime">本帧经过时间，单位为秒。</param>
        /// <param name="state">玩家已经进入的公开状态。</param>
        /// <param name="isMovingAway">玩家当前速度是否明确远离 Boss。</param>
        /// <param name="windowDuration">Guard 滚动统计窗口，单位为秒。</param>
        /// <param name="frequentGuardDuration">窗口内 Guard 累计达到该秒数即视为频繁防御。</param>
        /// <param name="sustainedRetreatDuration">连续远离达到该秒数即视为持续后退。</param>
        public void TickPlayerBehavior(
            float now,
            float deltaTime,
            PlayerStateId state,
            bool isMovingAway,
            float windowDuration,
            float frequentGuardDuration,
            float sustainedRetreatDuration)
        {
            float safeDelta = Mathf.Max(0f, deltaTime);
            float cutoff = now - Mathf.Max(0.1f, windowDuration);
            if (state == PlayerStateId.Guard && safeDelta > 0f)
            {
                guardSamples.Enqueue(new TimedDuration(now, safeDelta));
                guardDurationInWindow += safeDelta;
            }

            while (guardSamples.Count > 0 && guardSamples.Peek().Time < cutoff)
            {
                guardDurationInWindow = Mathf.Max(0f, guardDurationInWindow - guardSamples.Dequeue().Duration);
            }

            movingAwayDuration = isMovingAway ? movingAwayDuration + safeDelta : 0f;
            IsFrequentGuarder = guardDurationInWindow >= Mathf.Max(0f, frequentGuardDuration);
            IsSustainedRetreat = movingAwayDuration >= Mathf.Max(0f, sustainedRetreatDuration);
        }

        /// <summary>清除主动节奏和连续受击记忆，不影响动作冷却、选择历史或玩家行为样本。</summary>
        public void ResetTacticalState()
        {
            tempoPressure = 0f;
            pendingAttackPressure = 0f;
            firstActiveHitPressureCommitted = false;
            pressureDecayMode = false;
            consecutiveAttackActionCount = 0;
            tempoResetRequired = false;
            Array.Clear(recentActionIds, 0, recentActionIds.Length);
            ClearReactionHitSequence();
        }

        /// <summary>清除目标切换前积累的玩家行为样本，不影响 Boss 自身节奏、冷却和选择历史。</summary>
        public void ResetPlayerBehavior()
        {
            guardSamples.Clear();
            guardDurationInWindow = 0f;
            movingAwayDuration = 0f;
            IsFrequentGuarder = false;
            IsSustainedRetreat = false;
        }

        /// <summary>
        /// 把新的 Action ID 写入容量为三的最近历史首位，并依次后移旧值。
        /// </summary>
        /// <param name="actionId">已经成功启动的 Action ID；null 按空字符串记录。</param>
        private void ShiftRecentAction(string actionId)
        {
            for (int i = recentActionIds.Length - 1; i > 0; i--)
            {
                recentActionIds[i] = recentActionIds[i - 1];
            }

            recentActionIds[0] = actionId ?? string.Empty;
        }

        /// <summary>
        /// 在距离最后一次计数命中超过窗口时清空连续受击序列。
        /// </summary>
        /// <param name="now">当前运行时秒数。</param>
        /// <param name="reactionHitSequenceWindow">连续受击序列允许的最大间隔，单位为秒。</param>
        private void ResetExpiredReactionHitSequence(float now, float reactionHitSequenceWindow)
        {
            if (recentReactionHitCount > 0 &&
                now - lastReactionHitTime > Mathf.Max(0f, reactionHitSequenceWindow))
            {
                ClearReactionHitSequence();
            }
        }

        /// <summary>清空连续普通受击次数和用于判断过期的最后命中时间。</summary>
        private void ClearReactionHitSequence()
        {
            recentReactionHitCount = 0;
            lastReactionHitTime = 0f;
        }

        private readonly struct TimedDuration
        {
            /// <summary>
            /// 创建一个 Guard 时长样本，用于从滚动窗口中精确移除过期帧。
            /// </summary>
            /// <param name="time">样本记录时的运行时秒数。</param>
            /// <param name="duration">本样本贡献的 Guard 秒数。</param>
            public TimedDuration(float time, float duration)
            {
                Time = time;
                Duration = duration;
            }

            public float Time { get; }
            public float Duration { get; }
        }
    }
}
