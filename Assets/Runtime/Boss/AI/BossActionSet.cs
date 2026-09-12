// 文件说明：定义 Boss Brain 使用的动作池、阶段压力和战术调参数据。
// 所属模块：Boss AI。
// 运行影响：作为 Boss 选招资格、阶段权重、压力模式和后续策略的唯一配置来源。

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEVE.Boss.AI
{
    /// <summary>Boss 软阶段。阶段影响选招权重、压力区间和连续攻击上限。</summary>
    public enum BossCombatPhaseId
    {
        Phase1 = 0,
        Phase2 = 1,
        Desperation = 2
    }

    [Flags]
    public enum BossActionPhaseMask
    {
        None = 0,
        Phase1 = 1 << 0,
        Phase2 = 1 << 1,
        Desperation = 1 << 2,
        All = Phase1 | Phase2 | Desperation
    }

    /// <summary>文档定义的 Raven 攻击池。</summary>
    public enum BossActionPoolId
    {
        None = 0,
        HitEscape = 1,
        CloseDuel = 2,
        GapClose = 3,
        Special = 4,
        Ranged = 5
    }

    /// <summary>动作完成后必须执行的非攻击策略。</summary>
    public enum BossActionFollowUpPolicy
    {
        None = 0,
        MustEnterNeutral = 1,
        MustStrafe = 2
    }

    public enum BossActionStrength
    {
        Defensive = 0,
        Low = 1,
        Medium = 2,
        High = 3,
        Critical = 4
    }

    public enum BossPressurePreference
    {
        None = 0,
        Low = 1,
        MidLow = 2,
        Mid = 3,
        High = 4
    }

    [Flags]
    public enum BossActionPreferenceFlags
    {
        None = 0,
        PreviousActionSlash = 1 << 0,
        BossLowHealth = 1 << 1,
        BossLowPoise = 1 << 2,
        BossNearPhaseTransition = 1 << 3
    }

    /// <summary>一个动作在三个阶段的基础选择权重。</summary>
    [Serializable]
    public struct BossPhaseWeights
    {
        [Min(0f)] public float Phase1;
        [Min(0f)] public float Phase2;
        [Min(0f)] public float Phase3;

        public BossPhaseWeights(float phase1, float phase2, float phase3)
        {
            Phase1 = Mathf.Max(0f, phase1);
            Phase2 = Mathf.Max(0f, phase2);
            Phase3 = Mathf.Max(0f, phase3);
        }

        /// <summary>读取指定阶段权重；旧资产三项均为零时返回 1，保证迁移期间仍可选招。</summary>
        public float Get(BossCombatPhaseId phase)
        {
            if (Phase1 <= 0f && Phase2 <= 0f && Phase3 <= 0f)
            {
                return 1f;
            }

            return phase switch
            {
                BossCombatPhaseId.Phase2 => Mathf.Max(0f, Phase2),
                BossCombatPhaseId.Desperation => Mathf.Max(0f, Phase3),
                _ => Mathf.Max(0f, Phase1)
            };
        }
    }

    /// <summary>单阶段压力目标、硬限制和降压模式滞回阈值。</summary>
    [Serializable]
    public struct BossPhasePressureProfile
    {
        public BossCombatPhaseId Phase;
        [Min(0f)] public float TargetMin;
        [Min(0f)] public float TargetMax;
        [Min(0f)] public float SoftLimit;
        [Min(0f)] public float HardLimit;
        [Min(0f)] public float EnterDecayPressure;
        [Min(0f)] public float ExitDecayPressure;
        [Min(1)] public int MaxConsecutiveAttacks;

        public static BossPhasePressureProfile CreateDefault(BossCombatPhaseId phase)
        {
            return phase switch
            {
                BossCombatPhaseId.Phase2 => new BossPhasePressureProfile
                {
                    Phase = phase, TargetMin = 38f, TargetMax = 60f, SoftLimit = 70f,
                    HardLimit = 82f, EnterDecayPressure = 70f, ExitDecayPressure = 52f,
                    MaxConsecutiveAttacks = 3
                },
                BossCombatPhaseId.Desperation => new BossPhasePressureProfile
                {
                    Phase = phase, TargetMin = 46f, TargetMax = 66f, SoftLimit = 76f,
                    HardLimit = 86f, EnterDecayPressure = 76f, ExitDecayPressure = 58f,
                    MaxConsecutiveAttacks = 4
                },
                _ => new BossPhasePressureProfile
                {
                    Phase = BossCombatPhaseId.Phase1, TargetMin = 18f, TargetMax = 50f, SoftLimit = 60f,
                    HardLimit = 70f, EnterDecayPressure = 60f, ExitDecayPressure = 42f,
                    MaxConsecutiveAttacks = 2
                }
            };
        }
    }

    /// <summary>攻击池级硬过滤和最近使用限制。</summary>
    [Serializable]
    public struct BossActionPoolPolicy
    {
        public BossActionPoolId Pool;
        [Min(0f)] public float MinDistance;
        [Min(0f)] public float MaxDistance;
        public bool RequireHitStaggerContext;
        public bool DisallowHitStaggerContext;
        public bool DisallowDuringPressureDecay;
        [Min(0)] public int RecentThreeLimit;
    }

    /// <summary>文档未给出精确数值的 AI 条件初始调参。</summary>
    [Serializable]
    public sealed class BossAiTuning
    {
        [Min(0.01f)] public float DecisionIntervalMin = 0.15f;    // Brain 两次战术评估之间的最小间隔。Boss 在中立状态不会每帧重新选招。
        [Min(0.01f)] public float DecisionIntervalMax = 0.25f;  // Brain 两次战术评估之间的最大间隔。实际间隔用 Random.Range(min, max)。
        [Min(0f)] public float NeutralMinDuration = 0.3f;  // Approach Strafe 至少要维持多久才算完成一次中立行为。满这个时间后才清连续攻击次数和 TempoResetRequired。
        [Min(0.1f)] public float PlayerBehaviorWindow = 8f; // 玩家行为记忆统计窗口，单位秒。现在主要用于统计频繁防御、持续后退。
        [Min(0f)] public float FrequentGuardDuration = 2f; // 在统计窗口内，玩家 Guard 累计达到多少秒算“频繁防御”。会提高 SpecialPool 偏好。
        [Min(0f)] public float RetreatSustainDuration = 0.6f;  // 玩家连续远离 Boss 达到多少秒算“持续后退”。会影响 GapClosePool 偏好和中立时 Approach/Strafe 选择。
        [Min(0f)] public float CooldownSoonThreshold = 0.75f; // “GapClose 冷却快好了”的阈值。若某个 GapClose 只差不超过这个秒数的冷却，Boss 中距离会倾向 Strafe 等一下。
        [Min(1f)] public float PreferenceMultiplier = 1.5f;  // 通用偏好倍率。用于玩家持续后退偏好 GapClose、频繁防御偏好 Special、HitStagger 中偏好 HitEscape，以及单招偏好命中时加权。
        [Min(1f)] public float PressureMatchMultiplier = 1.4f;  // 动作压力偏好匹配时的倍率。比如某招偏好低压力，当前压力确实低，就乘这个值。
        [Range(0f, 1f)] public float PressureMismatchMultiplier = 0.65f; // 动作压力偏好不匹配时的倍率。
        [Min(1f)] public float PreferenceMultiplierCap = 2.5f; // 多个偏好叠加后的倍率上限，防止某个动作因为多个条件同时命中而权重爆炸。
        [Range(0f, 1f)] public float LowHealthRatio = 0.35f;  // Boss 血量低于等于这个比例时，带 BossLowHealth 偏好标记的动作加权。
        [Range(0f, 1f)] public float LowPoiseRatio = 0.3f;  // Boss Poise 低于等于这个比例时，带 BossLowPoise 偏好标记的动作加权。
        [Range(0f, 1f)] public float NearPhaseTransitionRatio = 0.05f;  // Boss 距离下一阶段血量阈值小于等于这个比例时，带 BossNearPhaseTransition 偏好标记的动作加权。
        [Min(0f)] public float ReactionSequenceResetDelay = 1.25f;  // 连续受击序列重置时间。超过这个时间没再受击，就清空本轮受击计数。
        [FormerlySerializedAs("FirstHitEscapeChance")]
        [Range(0f, 1f)] public float FirstHitCounterChance = 0.25f;  // 本轮第 1 次普通受击硬直可返回后，允许受击反制动作候选的概率。
        [FormerlySerializedAs("SecondHitEscapeChance")]
        [Range(0f, 1f)] public float SecondHitCounterChance = 0.6f; // 本轮第 2 次普通受击硬直可返回后的受击反制概率。
        [FormerlySerializedAs("ThirdHitEscapeChance")]
        [Range(0f, 1f)] public float ThirdHitCounterChance = 1f; // 本轮第 3 次普通受击硬直可返回后的受击反制概率。
    }

    [CreateAssetMenu(menuName = "Project EVE/Boss/Action Set", fileName = "BossActionSet")]
    public sealed class BossActionSet : ScriptableObject
    {
        [SerializeField] private BossActionSetEntry[] actions = Array.Empty<BossActionSetEntry>();
        [SerializeField] private BossPhasePressureProfile[] phasePressureProfiles = Array.Empty<BossPhasePressureProfile>();
        [SerializeField] private BossActionPoolPolicy[] poolPolicies = Array.Empty<BossActionPoolPolicy>();
        [SerializeField] private BossAiTuning tuning = new BossAiTuning();

        public IReadOnlyList<BossActionSetEntry> Actions => actions;
        public BossAiTuning Tuning => tuning ??= new BossAiTuning();

        public BossPhasePressureProfile GetPressureProfile(BossCombatPhaseId phase)
        {
            if (phasePressureProfiles != null)
            {
                for (int i = 0; i < phasePressureProfiles.Length; i++)
                {
                    if (phasePressureProfiles[i].Phase == phase && phasePressureProfiles[i].HardLimit > 0f)
                    {
                        return phasePressureProfiles[i];
                    }
                }
            }

            return BossPhasePressureProfile.CreateDefault(phase);
        }

        public bool TryGetPoolPolicy(BossActionPoolId pool, out BossActionPoolPolicy policy)
        {
            if (poolPolicies != null)
            {
                for (int i = 0; i < poolPolicies.Length; i++)
                {
                    if (poolPolicies[i].Pool == pool)
                    {
                        policy = poolPolicies[i];
                        return true;
                    }
                }
            }

            policy = default;
            return false;
        }

        /// <summary>供 Editor 迁移工具一次性写入完整配置。</summary>
        public void Configure(
            BossActionSetEntry[] nextActions,
            BossPhasePressureProfile[] nextPressureProfiles,
            BossActionPoolPolicy[] nextPoolPolicies,
            BossAiTuning nextTuning)
        {
            actions = nextActions ?? Array.Empty<BossActionSetEntry>();
            phasePressureProfiles = nextPressureProfiles ?? Array.Empty<BossPhasePressureProfile>();
            poolPolicies = nextPoolPolicies ?? Array.Empty<BossActionPoolPolicy>();
            tuning = nextTuning ?? new BossAiTuning();
        }
    }

    [Serializable]
    public struct BossActionSetEntry
    {
        [SerializeField] private string actionId;
        [SerializeField] private BossActionKind kind;
        [SerializeField] private bool enabled;
        [SerializeField] private string[] tags;
        [SerializeField] private BossActionPhaseMask allowedPhases;
        [SerializeField] private BossPhaseWeights phaseWeights;
        [SerializeField] private BossActionPoolId pool;
        [SerializeField] private float pressureCost;
        [SerializeField] private BossActionStrength strength;
        [SerializeField] private BossActionFollowUpPolicy followUpPolicy;
        [SerializeField] private BossPressurePreference pressurePreference;
        [SerializeField] private BossActionPreferenceFlags preferenceFlags;
        [SerializeField] private int recentUseLimit;

        public string ActionId => actionId ?? string.Empty;
        public BossActionKind Kind => kind;
        public bool Enabled => enabled;
        public IReadOnlyList<string> Tags => tags ?? Array.Empty<string>();
        public BossActionPhaseMask AllowedPhases => allowedPhases == BossActionPhaseMask.None ? BossActionPhaseMask.All : allowedPhases;
        public BossActionPoolId Pool => pool;
        public float PressureCost => Mathf.Max(0f, pressureCost);
        public BossActionStrength Strength => strength;
        public BossActionFollowUpPolicy FollowUpPolicy => followUpPolicy;
        public BossPressurePreference PressurePreference => pressurePreference;
        public BossActionPreferenceFlags PreferenceFlags => preferenceFlags;
        public int RecentUseLimit => Mathf.Max(0, recentUseLimit);

        public float GetWeight(BossCombatPhaseId phase) => phaseWeights.Get(phase);

        public bool AllowsPhase(BossCombatPhaseId phase)
        {
            BossActionPhaseMask phaseMask = phase switch
            {
                BossCombatPhaseId.Phase2 => BossActionPhaseMask.Phase2,
                BossCombatPhaseId.Desperation => BossActionPhaseMask.Desperation,
                _ => BossActionPhaseMask.Phase1
            };
            return (AllowedPhases & phaseMask) != 0;
        }

        public BossActionSetEntry(
            string actionId,
            BossActionKind kind,
            bool enabled,
            string[] tags,
            BossActionPhaseMask allowedPhases,
            BossPhaseWeights phaseWeights,
            BossActionPoolId pool,
            float pressureCost,
            BossActionStrength strength,
            BossActionFollowUpPolicy followUpPolicy,
            BossPressurePreference pressurePreference,
            BossActionPreferenceFlags preferenceFlags,
            int recentUseLimit)
        {
            this.actionId = actionId ?? string.Empty;
            this.kind = kind;
            this.enabled = enabled;
            this.tags = tags ?? Array.Empty<string>();
            this.allowedPhases = allowedPhases;
            this.phaseWeights = phaseWeights;
            this.pool = pool;
            this.pressureCost = Mathf.Max(0f, pressureCost);
            this.strength = strength;
            this.followUpPolicy = followUpPolicy;
            this.pressurePreference = pressurePreference;
            this.preferenceFlags = preferenceFlags;
            this.recentUseLimit = Mathf.Max(0, recentUseLimit);
        }
    }
}
