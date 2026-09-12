// 文件说明：按攻击池、硬资格和战术偏好选择 Boss 动作。
// 所属模块：Boss Brain。
// 运行影响：只做无副作用选择，不提交冷却、状态、动画或位移。

using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.AI;
using ProjectEVE.Combat.Timeline;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Brain
{
    public delegate float BossActionSelectionRandom01();

    public static class BossActionSelector
    {
        private readonly struct ScoredCandidate
        {
            public ScoredCandidate(BossAttackDefinition definition, BossActionSetEntry entry, float score)
            {
                Definition = definition;
                Entry = entry;
                Score = score;
            }

            public BossAttackDefinition Definition { get; }
            public BossActionSetEntry Entry { get; }
            public float Score { get; }
        }

        /// <summary>检查是否存在除短冷却外已满足全部硬条件的 GapClose 动作。</summary>
        /// <param name="blackboard">当前距离、角度和状态事实。</param>
        /// <param name="input">当前阶段、压力与最近动作历史。</param>
        /// <param name="actor">提供冷却、角度和 Motion 可行性只读检查的 BossActor。</param>
        /// <param name="actionSet">动作池与冷却即将恢复阈值的数据源。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <returns>true 表示存在仅被剩余不超过阈值的冷却阻止的 GapClose。</returns>
        public static bool HasGapCloseBlockedOnlyByShortCooldown(
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActor actor,
            BossActionSet actionSet,
            float now)
        {
            if (actionSet == null)
            {
                return false;
            }

            BossPhasePressureProfile pressureProfile = actionSet.GetPressureProfile(input.CurrentPhase);
            bool pressureDecayMode = input.PressureDecayMode;
            return HasGapCloseBlockedOnlyByShortCooldown(
                blackboard,
                input,
                actor,
                actionSet,
                now,
                pressureProfile,
                pressureDecayMode);
        }

        /// <summary>使用已解析的阶段压力配置检查 GapClose 是否只被短冷却阻止，避免同一决策帧重复读取配置。</summary>
        /// <param name="blackboard">当前距离、角度和状态事实。</param>
        /// <param name="input">当前阶段、压力与最近动作历史。</param>
        /// <param name="actor">提供冷却、角度和 Motion 可行性只读检查的 BossActor。</param>
        /// <param name="actionSet">动作池与冷却即将恢复阈值的数据源。</param>
        /// <param name="now">当前运行时秒数。</param>
        /// <param name="pressureProfile">当前阶段压力配置。</param>
        /// <param name="pressureDecayMode">当前压力是否已进入降压模式。</param>
        /// <returns>true 表示存在仅被剩余不超过阈值的冷却阻止的 GapClose。</returns>
        public static bool HasGapCloseBlockedOnlyByShortCooldown(
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossPhasePressureProfile pressureProfile,
            bool pressureDecayMode)
        {
            if (actor == null || actionSet == null)
            {
                return false;
            }

            float threshold = Mathf.Max(0f, actionSet.Tuning.CooldownSoonThreshold);
            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry entry = actionSet.Actions[i];
                if (!entry.Enabled ||
                    entry.Kind != BossActionKind.Attack ||
                    entry.Pool != BossActionPoolId.GapClose ||
                    !CombatTimelineProvider.TryGetBossAttack(entry.ActionId, out BossAttackDefinition definition) ||
                    !CanSelectEntry(entry, blackboard, input, actionSet, pressureProfile, pressureDecayMode, actor) ||
                    !IsBlockedOnlyByShortCooldown(entry, definition, actor, now, threshold))
                {
                    continue;
                }

                return true;
            }

            return false;
        }

        public static BossAttackDefinition Select(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossActionSelectionRandom01 random01 = null)
        {
            return Select(intent, blackboard, actor, actionSet, now, default, null, false, random01);
        }

        /// <summary>从指定动作池统一选择 Attack 或 Reposition，并返回所选条目的显式动作类别。</summary>
        /// <param name="blackboard">当前目标、距离、角度和状态事实。</param>
        /// <param name="actor">提供冷却与 Motion 可行性只读检查的 BossActor。</param>
        /// <param name="actionSet">保存动作条目、阶段压力和池策略的动作集。</param>
        /// <param name="now">本次选择的运行时秒数。</param>
        /// <param name="input">阶段、压力和最近动作历史。</param>
        /// <param name="pool">只允许从该动作池选择。</param>
        /// <param name="random01">可选的 0 到 1 随机源；测试可注入固定值。</param>
        /// <param name="definition">成功时写回所选 Timeline 运行时定义。</param>
        /// <param name="actionKind">成功时写回 ActionSet 条目的显式动作类别。</param>
        /// <returns>true 表示找到合格候选；false 表示该池当前没有可执行动作。</returns>
        public static bool TrySelectFromPool(
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossBrainDecisionInput input,
            BossActionPoolId pool,
            out BossAttackDefinition definition,
            out BossActionKind actionKind,
            BossActionSelectionRandom01 random01 = null)
        {
            definition = null;
            actionKind = BossActionKind.Attack;
            if (actor == null || actionSet == null)
            {
                return false;
            }

            BossPhasePressureProfile pressureProfile = actionSet.GetPressureProfile(input.CurrentPhase);
            bool pressureDecayMode = input.PressureDecayMode;
            List<ScoredCandidate> candidates = new List<ScoredCandidate>();
            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry entry = actionSet.Actions[i];
                bool supportedKind = entry.Kind == BossActionKind.Attack || entry.Kind == BossActionKind.Reposition;
                if (!entry.Enabled || !supportedKind || entry.Pool != pool ||
                    !CombatTimelineProvider.TryGetBossAttack(entry.ActionId, out BossAttackDefinition candidateDefinition) ||
                    !actor.CanSelectActionReadOnly(candidateDefinition, now) ||
                    !CanSelectEntry(entry, blackboard, input, actionSet, pressureProfile, pressureDecayMode, actor))
                {
                    continue;
                }

                float score = ComputeActionScore(candidateDefinition, entry, blackboard, input, actionSet, pressureProfile, null, actor);
                if (score > 0f)
                {
                    candidates.Add(new ScoredCandidate(candidateDefinition, entry, score));
                }
            }

            ScoredCandidate selected = SelectWeightedCandidate(candidates, random01);
            if (selected.Definition == null)
            {
                return false;
            }

            definition = selected.Definition;
            actionKind = selected.Entry.Kind;
            return true;
        }

        /// <summary>从正式动作池中选择一个可启动的 Attack 或 Reposition Action。</summary>
        /// <param name="intent">当前 Brain 高层意图；只有 Attack 意图会尝试选择动作。</param>
        /// <param name="blackboard">当前目标、距离、角度和状态事实。</param>
        /// <param name="actor">提供冷却、角度和 Motion 可行性只读检查的 BossActor。</param>
        /// <param name="actionSet">保存动作条目、阶段压力、池策略和偏好的动作集。</param>
        /// <param name="now">本次选择使用的运行时秒数。</param>
        /// <param name="input">阶段、压力、受击反制许可和最近动作历史。</param>
        /// <param name="definition">成功时写回所选 Timeline 运行时定义。</param>
        /// <param name="actionKind">成功时写回 ActionSet 条目的显式执行类别。</param>
        /// <param name="random01">可选的 0 到 1 随机源；测试可注入固定值。</param>
        /// <returns>true 表示找到一个合格动作；false 表示没有可执行动作。</returns>
        public static bool TrySelectAction(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossBrainDecisionInput input,
            out BossAttackDefinition definition,
            out BossActionKind actionKind,
            BossActionSelectionRandom01 random01 = null)
        {
            BossPhasePressureProfile pressureProfile = actionSet != null
                ? actionSet.GetPressureProfile(input.CurrentPhase)
                : BossPhasePressureProfile.CreateDefault(input.CurrentPhase);
            bool pressureDecayMode = input.PressureDecayMode;
            return TrySelectAction(
                intent,
                blackboard,
                actor,
                actionSet,
                now,
                input,
                pressureProfile,
                pressureDecayMode,
                out definition,
                out actionKind,
                out _,
                random01);
        }

        /// <summary>从正式动作池中选择 Action，并在同一次扫描中记录 GapClose 是否只被短冷却阻止。</summary>
        /// <param name="intent">当前 Brain 高层意图；只有 Attack 意图会尝试选择动作。</param>
        /// <param name="blackboard">当前目标、距离、角度和状态事实。</param>
        /// <param name="actor">提供冷却、角度和 Motion 可行性只读检查的 BossActor。</param>
        /// <param name="actionSet">保存动作条目、阶段压力、池策略和偏好的动作集。</param>
        /// <param name="now">本次选择使用的运行时秒数。</param>
        /// <param name="input">阶段、压力、受击反制许可和最近动作历史。</param>
        /// <param name="pressureProfile">当前阶段压力配置，调用方可缓存后传入。</param>
        /// <param name="pressureDecayMode">当前压力是否已进入降压模式。</param>
        /// <param name="definition">成功时写回所选 Timeline 运行时定义。</param>
        /// <param name="actionKind">成功时写回 ActionSet 条目的显式执行类别。</param>
        /// <param name="gapCloseBlockedOnlyByShortCooldown">无论是否选中动作，写回本次扫描中是否存在只被短冷却阻止的 GapClose。</param>
        /// <param name="random01">可选的 0 到 1 随机源；测试可注入固定值。</param>
        /// <returns>true 表示找到一个合格动作；false 表示没有可执行动作。</returns>
        public static bool TrySelectAction(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossBrainDecisionInput input,
            BossPhasePressureProfile pressureProfile,
            bool pressureDecayMode,
            out BossAttackDefinition definition,
            out BossActionKind actionKind,
            out bool gapCloseBlockedOnlyByShortCooldown,
            BossActionSelectionRandom01 random01 = null)
        {
            definition = null;
            actionKind = BossActionKind.Attack;
            gapCloseBlockedOnlyByShortCooldown = false;
            if (intent != BossBrainIntentId.Attack || actor == null || actionSet == null)
            {
                return false;
            }

            Dictionary<BossActionPoolId, List<ScoredCandidate>> candidatesByPool =
                new Dictionary<BossActionPoolId, List<ScoredCandidate>>();
            float cooldownSoonThreshold = Mathf.Max(0f, actionSet.Tuning.CooldownSoonThreshold);

            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry entry = actionSet.Actions[i];
                bool supportedKind = entry.Kind == BossActionKind.Attack || entry.Kind == BossActionKind.Reposition;
                if (!entry.Enabled || !supportedKind)
                {
                    continue;
                }

                if (!CombatTimelineProvider.TryGetBossAttack(entry.ActionId, out BossAttackDefinition candidateDefinition) ||
                    !CanSelectEntry(entry, blackboard, input, actionSet, pressureProfile, pressureDecayMode, actor))
                {
                    continue;
                }

                if (!actor.CanSelectActionReadOnly(candidateDefinition, now))
                {
                    gapCloseBlockedOnlyByShortCooldown |= IsGapCloseBlockedOnlyByShortCooldown(
                        entry,
                        candidateDefinition,
                        actor,
                        now,
                        cooldownSoonThreshold);
                    continue;
                }

                float score = ComputeActionScore(candidateDefinition, entry, blackboard, input, actionSet, pressureProfile, null, actor);
                if (score <= 0f)
                {
                    continue;
                }

                if (!candidatesByPool.TryGetValue(entry.Pool, out List<ScoredCandidate> candidates))
                {
                    candidates = new List<ScoredCandidate>();
                    candidatesByPool.Add(entry.Pool, candidates);
                }

                candidates.Add(new ScoredCandidate(candidateDefinition, entry, score));
            }

            if (candidatesByPool.Count == 0)
            {
                return false;
            }

            BossActionPoolId selectedPool = SelectPool(candidatesByPool, blackboard, input, actionSet.Tuning, random01);
            if (!candidatesByPool.TryGetValue(selectedPool, out List<ScoredCandidate> selectedCandidates))
            {
                return false;
            }

            ScoredCandidate selected = SelectWeightedCandidate(selectedCandidates, random01);
            if (selected.Definition == null)
            {
                return false;
            }

            definition = selected.Definition;
            actionKind = selected.Entry.Kind;
            return true;
        }

        private static bool IsGapCloseBlockedOnlyByShortCooldown(
            BossActionSetEntry entry,
            BossAttackDefinition definition,
            BossActor actor,
            float now,
            float cooldownSoonThreshold)
        {
            return entry.Kind == BossActionKind.Attack &&
                entry.Pool == BossActionPoolId.GapClose &&
                IsBlockedOnlyByShortCooldown(entry, definition, actor, now, cooldownSoonThreshold);
        }

        private static bool IsBlockedOnlyByShortCooldown(
            BossActionSetEntry entry,
            BossAttackDefinition definition,
            BossActor actor,
            float now,
            float cooldownSoonThreshold)
        {
            if (definition == null || actor == null || !actor.CanSelectActionIgnoringCooldownReadOnly(definition))
            {
                return false;
            }

            float remaining = actor.GetActionCooldownRemaining(entry.ActionId, now);
            return remaining > 0f && remaining <= cooldownSoonThreshold;
        }

        public static BossAttackDefinition Select(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossBrainDecisionInput input,
            BossActionSelectionRandom01 random01 = null)
        {
            return Select(intent, blackboard, actor, actionSet, now, input, null, false, random01);
        }

        public static BossAttackDefinition Select(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            IReadOnlyList<string> preferredTags,
            bool requirePreferredTag,
            BossActionSelectionRandom01 random01 = null)
        {
            return Select(intent, blackboard, actor, actionSet, now, default, preferredTags, requirePreferredTag, random01);
        }

        /// <summary>先选择攻击池，再在池内按阶段权重和偏好选择具体攻击。</summary>
        public static BossAttackDefinition Select(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            BossActionSet actionSet,
            float now,
            BossBrainDecisionInput input,
            IReadOnlyList<string> preferredTags,
            bool requirePreferredTag,
            BossActionSelectionRandom01 random01 = null)
        {
            if (intent != BossBrainIntentId.Attack || actor == null || actionSet == null)
            {
                return null;
            }

            Dictionary<BossActionPoolId, List<ScoredCandidate>> candidatesByPool =
                new Dictionary<BossActionPoolId, List<ScoredCandidate>>();
            BossPhasePressureProfile pressureProfile = actionSet.GetPressureProfile(input.CurrentPhase);
            bool pressureDecayMode = input.PressureDecayMode;

            for (int i = 0; i < actionSet.Actions.Count; i++)
            {
                BossActionSetEntry entry = actionSet.Actions[i];
                if (!entry.Enabled || entry.Kind != BossActionKind.Attack)
                {
                    continue;
                }

                if (!CombatTimelineProvider.TryGetBossAttack(entry.ActionId, out BossAttackDefinition definition) ||
                    !actor.CanSelectActionReadOnly(definition, now) ||
                    !CanSelectEntry(entry, blackboard, input, actionSet, pressureProfile, pressureDecayMode, actor))
                {
                    continue;
                }

                if (requirePreferredTag && !HasAnyTag(entry.Tags, preferredTags))
                {
                    continue;
                }

                float score = ComputeActionScore(definition, entry, blackboard, input, actionSet, pressureProfile, preferredTags, actor);
                if (score <= 0f)
                {
                    continue;
                }

                if (!candidatesByPool.TryGetValue(entry.Pool, out List<ScoredCandidate> candidates))
                {
                    candidates = new List<ScoredCandidate>();
                    candidatesByPool.Add(entry.Pool, candidates);
                }

                candidates.Add(new ScoredCandidate(definition, entry, score));
            }

            if (candidatesByPool.Count == 0)
            {
                return null;
            }

            BossActionPoolId selectedPool = SelectPool(candidatesByPool, blackboard, input, actionSet.Tuning, random01);
            return candidatesByPool.TryGetValue(selectedPool, out List<ScoredCandidate> selectedCandidates)
                ? SelectWeighted(selectedCandidates, random01)
                : null;
        }

        /// <summary>测试和无 ActionSet 调用使用等基础权重选择，不参与正式池策略。</summary>
        public static BossAttackDefinition Select(
            BossBrainIntentId intent,
            BossBlackboard blackboard,
            BossActor actor,
            IReadOnlyList<BossAttackDefinition> attacks,
            float now,
            BossActionSelectionRandom01 random01 = null)
        {
            if (intent != BossBrainIntentId.Attack || actor == null || attacks == null)
            {
                return null;
            }

            List<ScoredCandidate> candidates = new List<ScoredCandidate>();
            for (int i = 0; i < attacks.Count; i++)
            {
                BossAttackDefinition definition = attacks[i];
                if (definition == null || !actor.CanSelectActionReadOnly(definition, now))
                {
                    continue;
                }

                float score = ComputeAngleAndRepeatScore(definition, blackboard, 1f);
                candidates.Add(new ScoredCandidate(definition, default, score));
            }

            return SelectWeighted(candidates, random01);
        }

        private static bool CanSelectEntry(
            BossActionSetEntry entry,
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActionSet actionSet,
            BossPhasePressureProfile pressureProfile,
            bool pressureDecayMode,
            BossActor actor)
        {
            bool isHitStagger = blackboard.CurrentState == BossStateId.HitStagger;
            bool isDecayReposition = pressureDecayMode && entry.Kind == BossActionKind.Reposition;
            if (isHitStagger &&
                (!blackboard.CanReturnFromReaction || !input.CanUseReactionCounter))
            {
                return false;
            }

            if (pressureDecayMode && entry.Kind == BossActionKind.Attack)
            {
                return false;
            }

            if (entry.Pool == BossActionPoolId.HitEscape && !isHitStagger && !isDecayReposition)
            {
                return false;
            }

            if (!entry.AllowsPhase(input.CurrentPhase) ||
                input.TempoPressure + entry.PressureCost + actor.ArenaEdgePressureBonus > pressureProfile.HardLimit)
            {
                return false;
            }

            if (entry.RecentUseLimit > 0 && CountRecent(input.RecentActionIds, entry.ActionId) >= entry.RecentUseLimit)
            {
                return false;
            }

            if (pressureDecayMode &&
                (entry.Pool == BossActionPoolId.Special || entry.Pool == BossActionPoolId.Ranged))
            {
                return false;
            }

            if (actionSet.TryGetPoolPolicy(entry.Pool, out BossActionPoolPolicy policy))
            {
                if (policy.RecentThreeLimit > 0 &&
                    CountRecentPool(input.RecentActionIds, actionSet, entry.Pool) >= policy.RecentThreeLimit)
                {
                    return false;
                }

                float distance = blackboard.DistanceToTarget;
                if (distance < policy.MinDistance || (policy.MaxDistance > 0f && distance > policy.MaxDistance))
                {
                    return false;
                }

                if ((policy.RequireHitStaggerContext && !isHitStagger && !isDecayReposition) ||
                    (policy.DisallowHitStaggerContext && isHitStagger) ||
                    (policy.DisallowDuringPressureDecay && pressureDecayMode))
                {
                    return false;
                }
            }

            return true;
        }

        private static float ComputeActionScore(
            BossAttackDefinition definition,
            BossActionSetEntry entry,
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossActionSet actionSet,
            BossPhasePressureProfile pressureProfile,
            IReadOnlyList<string> preferredTags,
            BossActor actor)
        {
            float score = entry.GetWeight(input.CurrentPhase);
            if (score <= 0f)
            {
                return 0f;
            }

            score = ComputeAngleAndRepeatScore(definition, blackboard, score);
            BossAiTuning tuning = actionSet.Tuning;
            score *= ResolvePressurePreferenceMultiplier(entry.PressurePreference, input.TempoPressure, pressureProfile, tuning);

            float preferenceMultiplier = 1f;
            BossActionPreferenceFlags flags = entry.PreferenceFlags;
            if ((flags & BossActionPreferenceFlags.PreviousActionSlash) != 0 &&
                blackboard.LastSelectedActionId == RavenBossAttackIds.SlashId)
            {
                preferenceMultiplier *= tuning.PreferenceMultiplier;
            }
            if ((flags & BossActionPreferenceFlags.BossLowHealth) != 0 && actor.HpRatio <= tuning.LowHealthRatio)
            {
                preferenceMultiplier *= tuning.PreferenceMultiplier;
            }
            if ((flags & BossActionPreferenceFlags.BossLowPoise) != 0 && actor.PoiseRatio <= tuning.LowPoiseRatio)
            {
                preferenceMultiplier *= tuning.PreferenceMultiplier;
            }
            if ((flags & BossActionPreferenceFlags.BossNearPhaseTransition) != 0 &&
                actor.DistanceToNextPhaseRatio <= tuning.NearPhaseTransitionRatio)
            {
                preferenceMultiplier *= tuning.PreferenceMultiplier;
            }
            if (HasAnyTag(entry.Tags, preferredTags))
            {
                preferenceMultiplier *= tuning.PreferenceMultiplier;
            }

            if (input.TempoPressure >= pressureProfile.SoftLimit &&
                (entry.Strength == BossActionStrength.High || entry.Strength == BossActionStrength.Critical))
            {
                preferenceMultiplier *= 0.35f;
            }

            score *= Mathf.Min(tuning.PreferenceMultiplierCap, preferenceMultiplier);
            return score;
        }

        private static float ComputeAngleAndRepeatScore(BossAttackDefinition definition, BossBlackboard blackboard, float score)
        {
            if (blackboard.HasTarget && definition.AngleLimit > 0f)
            {
                float normalizedAngle = Mathf.Clamp01(blackboard.AngleToTarget / definition.AngleLimit);
                score *= Mathf.Lerp(1.25f, 0.75f, normalizedAngle);
            }

            if (blackboard.LastSelectedActionId == definition.AttackId)
            {
                score /= Mathf.Max(1, blackboard.RepeatedSelectedActionCount + 1);
            }

            return score;
        }

        private static float ResolvePressurePreferenceMultiplier(
            BossPressurePreference preference,
            float pressure,
            BossPhasePressureProfile profile,
            BossAiTuning tuning)
        {
            if (preference == BossPressurePreference.None)
            {
                return 1f;
            }

            bool matches = preference switch
            {
                BossPressurePreference.Low => pressure < profile.TargetMin,
                BossPressurePreference.MidLow => pressure <= profile.TargetMax,
                BossPressurePreference.Mid => pressure >= profile.TargetMin && pressure <= profile.TargetMax,
                BossPressurePreference.High => pressure > profile.TargetMax,
                _ => true
            };
            return matches ? tuning.PressureMatchMultiplier : tuning.PressureMismatchMultiplier;
        }

        private static BossActionPoolId SelectPool(
            Dictionary<BossActionPoolId, List<ScoredCandidate>> pools,
            BossBlackboard blackboard,
            BossBrainDecisionInput input,
            BossAiTuning tuning,
            BossActionSelectionRandom01 random01)
        {
            List<KeyValuePair<BossActionPoolId, float>> scores = new List<KeyValuePair<BossActionPoolId, float>>();
            float total = 0f;
            foreach (KeyValuePair<BossActionPoolId, List<ScoredCandidate>> pair in pools)
            {
                float poolScore = 0f;
                for (int i = 0; i < pair.Value.Count; i++)
                {
                    poolScore += pair.Value[i].Score;
                }

                if (pair.Key == BossActionPoolId.GapClose && input.PlayerTacticalSnapshot.IsSustainedRetreat)
                {
                    poolScore *= tuning.PreferenceMultiplier;
                }
                if (pair.Key == BossActionPoolId.Special && input.PlayerTacticalSnapshot.IsLongGuarding)
                {
                    poolScore *= tuning.PreferenceMultiplier;
                }
                if (pair.Key == BossActionPoolId.HitEscape && blackboard.CurrentState == BossStateId.HitStagger)
                {
                    poolScore *= tuning.PreferenceMultiplier;
                }

                scores.Add(new KeyValuePair<BossActionPoolId, float>(pair.Key, poolScore));
                total += poolScore;
            }

            float roll = Mathf.Clamp01(random01 != null ? random01() : Random.value) * total;
            float accumulated = 0f;
            for (int i = 0; i < scores.Count; i++)
            {
                accumulated += scores[i].Value;
                if (roll <= accumulated)
                {
                    return scores[i].Key;
                }
            }

            return scores[scores.Count - 1].Key;
        }

        private static BossAttackDefinition SelectWeighted(List<ScoredCandidate> candidates, BossActionSelectionRandom01 random01)
        {
            return SelectWeightedCandidate(candidates, random01).Definition;
        }

        /// <summary>按候选分数随机返回完整候选，使调用方同时获得定义与显式 ActionKind。</summary>
        /// <param name="candidates">已经完成硬过滤和评分的候选集合。</param>
        /// <param name="random01">可选的 0–1 随机源；未提供时使用 UnityEngine.Random。</param>
        /// <returns>命中的完整候选；集合为空或总分无效时返回默认值。</returns>
        private static ScoredCandidate SelectWeightedCandidate(
            List<ScoredCandidate> candidates,
            BossActionSelectionRandom01 random01)
        {
            if (candidates == null || candidates.Count == 0)
            {
                return default;
            }

            float total = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                total += Mathf.Max(0f, candidates[i].Score);
            }

            if (total <= 0f)
            {
                return default;
            }

            float roll = Mathf.Clamp01(random01 != null ? random01() : Random.value) * total;
            float accumulated = 0f;
            for (int i = 0; i < candidates.Count; i++)
            {
                accumulated += Mathf.Max(0f, candidates[i].Score);
                if (roll <= accumulated)
                {
                    return candidates[i];
                }
            }

            return candidates[candidates.Count - 1];
        }

        private static bool HasAnyTag(IReadOnlyList<string> tags, IReadOnlyList<string> requested)
        {
            if (tags == null || requested == null)
            {
                return false;
            }

            for (int i = 0; i < requested.Count; i++)
            {
                for (int j = 0; j < tags.Count; j++)
                {
                    if (tags[j] == requested[i])
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        private static int CountRecent(IReadOnlyList<string> values, string value)
        {
            if (values == null || string.IsNullOrEmpty(value))
            {
                return 0;
            }

            int count = 0;
            for (int i = 0; i < values.Count; i++)
            {
                if (values[i] == value)
                {
                    count++;
                }
            }

            return count;
        }

        /// <summary>统计最近三个正式攻击中来自指定动作池的数量。</summary>
        /// <param name="recentActionIds">最新项在前的最近攻击 ID。</param>
        /// <param name="actionSet">用于把 ActionId 映射回动作池的唯一配置源。</param>
        /// <param name="pool">需要统计的动作池。</param>
        /// <returns>最近三个条目中属于该池的数量。</returns>
        private static int CountRecentPool(
            IReadOnlyList<string> recentActionIds,
            BossActionSet actionSet,
            BossActionPoolId pool)
        {
            if (recentActionIds == null || actionSet == null)
            {
                return 0;
            }

            int count = 0;
            int recentCount = Mathf.Min(3, recentActionIds.Count);
            for (int recentIndex = 0; recentIndex < recentCount; recentIndex++)
            {
                string actionId = recentActionIds[recentIndex];
                for (int actionIndex = 0; actionIndex < actionSet.Actions.Count; actionIndex++)
                {
                    BossActionSetEntry recentEntry = actionSet.Actions[actionIndex];
                    if (recentEntry.ActionId == actionId && recentEntry.Pool == pool)
                    {
                        count++;
                        break;
                    }
                }
            }

            return count;
        }
    }
}
