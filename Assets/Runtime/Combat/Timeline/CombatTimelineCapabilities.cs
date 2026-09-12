// 文件说明：定义 Combat Timeline 运行时能力归一化模型、解析规格和当前帧快照。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、PlayerMotion、Reaction 和当前帧能力快照解析。

using ProjectEVE.Combat;
using ProjectEVE.Player;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Movement;
using ProjectEVE.Player.Windows;
using System;
using System.Collections.Generic;

namespace ProjectEVE.Combat.Timeline
{
    public enum CombatTimelineCapabilityKind
    {
        None = 0,
        Phase = 1,
        InputBuffer = 2,
        Cancel = 3,
        Hit = 4,
        Defense = 5,
        Armor = 6,
        Motion = 7,
        Reaction = 8,
        Marker = 9,
        Interrupt = 10
    }

    public enum BossReactionGateResultId
    {
        Allowed = 0,
        BlockedByClosedWindow = 1,
        BlockedByBossReactionGate = 2
    }

    public readonly struct BossReactionGateResult
    {
        public BossReactionGateResult(BossReactionGateResultId id)
        {
            Id = id;
        }

        public static readonly BossReactionGateResult Allowed =
            new BossReactionGateResult(BossReactionGateResultId.Allowed);

        public static readonly BossReactionGateResult BlockedByClosedWindow =
            new BossReactionGateResult(BossReactionGateResultId.BlockedByClosedWindow);

        public static readonly BossReactionGateResult BlockedByBossReactionGate =
            new BossReactionGateResult(BossReactionGateResultId.BlockedByBossReactionGate);

        public BossReactionGateResultId Id { get; }
        public bool IsAllowed => Id == BossReactionGateResultId.Allowed;
    }

    public readonly struct BossReactionGateWindow
    {
        public readonly string Name;
        public readonly float StartTime;
        public readonly float EndTime;
        public readonly CombatAttackType[] AttackTypes;
        public readonly BossReactionGatePolicy Policy;
        public readonly BossReactionGateBlockedOutcome BlockedOutcome;

        public BossReactionGateWindow(
            string name,
            float startTime,
            float endTime,
            CombatAttackType[] attackTypes,
            BossReactionGatePolicy policy,
            BossReactionGateBlockedOutcome blockedOutcome)
        {
            Name = string.IsNullOrEmpty(name) ? "Boss Reaction Gate" : name;
            StartTime = startTime;
            EndTime = endTime;
            AttackTypes = attackTypes ?? Array.Empty<CombatAttackType>();
            Policy = policy;
            BlockedOutcome = blockedOutcome;
        }

        public bool Contains(float elapsedTime)
        {
            return elapsedTime >= StartTime && elapsedTime <= EndTime;
        }

        public bool MatchesAttackType(CombatAttackType attackType)
        {
            for (int i = 0; i < AttackTypes.Length; i++)
            {
                if (AttackTypes[i] == attackType)
                {
                    return true;
                }
            }

            return false;
        }

        public bool BlocksOutcome(CombatHitOutcome outcome)
        {
            return BlockedOutcome == BossReactionGateBlockedOutcome.Any ||
                (BlockedOutcome == BossReactionGateBlockedOutcome.Knockdown &&
                    outcome == CombatHitOutcome.Knockdown);
        }
    }

    /// <summary>
    /// 运行时能力窗口。运行时主语义由单一 CapabilityId 表达，窗口只保留状态消费所需的时间和来源能力。
    /// </summary>
    public readonly struct CombatTimelineCapabilityWindow
    {
        public readonly string Name;
        public readonly CombatTimelineCapabilityId SourceCapabilityId;
        public readonly float StartTime;
        public readonly float EndTime;

        public CombatTimelineCapabilityWindow(
            string name,
            CombatTimelineCapabilityId sourceCapabilityId,
            float startTime,
            float endTime)
        {
            Name = string.IsNullOrEmpty(name) ? sourceCapabilityId.ToString() : name;
            SourceCapabilityId = sourceCapabilityId;
            StartTime = startTime;
            EndTime = endTime;
        }

        public bool Contains(float elapsedTime)
        {
            return elapsedTime >= StartTime && elapsedTime <= EndTime;
        }

        public ActionWindow ToActionWindow()
        {
            return new ActionWindow(Name, StartTime, EndTime);
        }
    }

    /// <summary>
    /// 当前动作在某一帧开放的能力集合。HitNode 与 PlayerMotion 只暴露激活 ID，完整配置由 Provider Data / MotionProfile 持有。
    /// </summary>
    public readonly struct CombatActionCapabilitySnapshot
    {
        private static readonly string[] EmptyHitNodeIds = Array.Empty<string>();
        private static readonly PlayerActionMotionId[] EmptyPlayerMotionIds = Array.Empty<PlayerActionMotionId>();

        public readonly PlayerStatePhase Phase;
        public readonly bool CanCommitCancel;
        public readonly bool AttackHitboxActive;
        public readonly bool CanBufferAttack;
        public readonly bool CanBufferEvade;
        public readonly bool CanBufferSkill;
        public readonly bool CanComboAttack;
        public readonly bool CanCancelToEvade;
        public readonly bool CanCancelToSkill;
        public readonly bool CanCancelToGuard;
        public readonly bool CanResetAttack;
        public readonly bool CanMoveCancel;
        public readonly bool Invincible;
        public readonly bool PerfectEvade;
        public readonly bool PerfectGuard;
        public readonly bool GuardBlock;
        public readonly bool PerfectGuardChainInput;
        public readonly bool SuperArmor;
        public readonly bool Uninterruptible;
        public readonly bool CanReturn;
        public readonly string[] ActiveHitNodeIds;
        public readonly PlayerActionMotionId[] ActivePlayerMotionIds;

        public CombatActionCapabilitySnapshot(
            PlayerStatePhase phase,
            bool canCommitCancel,
            bool attackHitboxActive,
            bool canBufferAttack,
            bool canBufferEvade,
            bool canBufferSkill,
            bool canComboAttack,
            bool canCancelToEvade,
            bool canCancelToSkill,
            bool canCancelToGuard,
            bool canResetAttack,
            bool canMoveCancel,
            bool invincible,
            bool perfectEvade,
            bool perfectGuard,
            bool guardBlock,
            bool perfectGuardChainInput,
            bool superArmor,
            bool uninterruptible,
            bool canReturn,
            string[] activeHitNodeIds,
            PlayerActionMotionId[] activePlayerMotionIds)
        {
            Phase = phase;
            CanCommitCancel = canCommitCancel;
            AttackHitboxActive = attackHitboxActive;
            CanBufferAttack = canBufferAttack;
            CanBufferEvade = canBufferEvade;
            CanBufferSkill = canBufferSkill;
            CanComboAttack = canComboAttack;
            CanCancelToEvade = canCancelToEvade;
            CanCancelToSkill = canCancelToSkill;
            CanCancelToGuard = canCancelToGuard;
            CanResetAttack = canResetAttack;
            CanMoveCancel = canMoveCancel;
            Invincible = invincible;
            PerfectEvade = perfectEvade;
            PerfectGuard = perfectGuard;
            GuardBlock = guardBlock;
            PerfectGuardChainInput = perfectGuardChainInput;
            SuperArmor = superArmor;
            Uninterruptible = uninterruptible;
            CanReturn = canReturn;
            ActiveHitNodeIds = activeHitNodeIds ?? EmptyHitNodeIds;
            ActivePlayerMotionIds = activePlayerMotionIds ?? EmptyPlayerMotionIds;
        }
    }

    /// <summary>
    /// Combat Timeline 资产的只读运行时规格。状态类应读取它生成的 CapabilitySnapshot，而不是遍历原始 clips。
    /// </summary>
    public sealed class CombatActionRuntimeSpec
    {
        private readonly struct PlayerMotionWindow
        {
            /// <summary>
            /// 保存一个 Player Motion 引用的动作内时间范围，供 RuntimeSpec 求值当前激活 ID。
            /// </summary>
            /// <param name="motionId">PlayerActionMotionProfile 中的位移配置 ID。</param>
            /// <param name="startTime">窗口开始时间，单位为秒。</param>
            /// <param name="endTime">窗口结束时间，单位为秒。</param>
            public PlayerMotionWindow(PlayerActionMotionId motionId, float startTime, float endTime)
            {
                MotionId = motionId;
                StartTime = startTime;
                EndTime = endTime;
            }

            public PlayerActionMotionId MotionId { get; }
            public float StartTime { get; }
            public float EndTime { get; }

            /// <summary>
            /// 判断动作时间是否落在该 Player Motion 窗口内。
            /// </summary>
            /// <param name="elapsedTime">当前动作已经过时间，单位为秒。</param>
            /// <returns>时间位于闭区间 StartTime 到 EndTime 内时返回 true；否则返回 false。</returns>
            public bool Contains(float elapsedTime)
            {
                return elapsedTime >= StartTime && elapsedTime <= EndTime;
            }
        }

        private static readonly CombatTimelineCapabilityWindow[] EmptyCapabilities = Array.Empty<CombatTimelineCapabilityWindow>();
        private static readonly AttackPhaseWindow[] EmptyPhaseWindows = Array.Empty<AttackPhaseWindow>();
        private static readonly CombatHitNodeData[] EmptyHitNodes = Array.Empty<CombatHitNodeData>();
        private static readonly BossReactionGateWindow[] EmptyBossReactionGateWindows = Array.Empty<BossReactionGateWindow>();
        private static readonly PlayerMotionWindow[] EmptyPlayerMotionWindows = Array.Empty<PlayerMotionWindow>();

        // 收集单个 Timeline 的归一化窗口数据；业务通过 RuntimeSpec 查询能力快照，
        // 不直接遍历原始 CombatTimelineClip。
        private readonly CombatTimelineCapabilityWindow[] capabilityWindows;
        private readonly AttackPhaseWindow[] phaseWindows;
        private readonly CombatHitNodeData[] hitNodes;
        private readonly BossReactionGateWindow[] bossReactionGateWindows;
        private readonly PlayerMotionWindow[] playerMotionWindows;

        private CombatActionRuntimeSpec(
            CombatTimelineCapabilityWindow[] capabilityWindows,
            AttackPhaseWindow[] phaseWindows,
            CombatHitNodeData[] hitNodes,
            BossReactionGateWindow[] bossReactionGateWindows,
            PlayerMotionWindow[] playerMotionWindows)
        {
            this.capabilityWindows = capabilityWindows ?? EmptyCapabilities;
            this.phaseWindows = phaseWindows ?? EmptyPhaseWindows;
            this.hitNodes = hitNodes ?? EmptyHitNodes;
            this.bossReactionGateWindows = bossReactionGateWindows ?? EmptyBossReactionGateWindows;
            this.playerMotionWindows = playerMotionWindows ?? EmptyPlayerMotionWindows;
        }

        public IReadOnlyList<BossReactionGateWindow> BossReactionGateWindows => bossReactionGateWindows;

        /// <summary>
        /// 从 Combat Timeline 资产解析 RuntimeSpec。解析期读取 CapabilityId，并输出统一能力结构。
        /// </summary>
        public static CombatActionRuntimeSpec FromTimeline(CombatTimelineActionAsset timeline)
        {
            if (timeline == null)
            {
                throw new ArgumentNullException(nameof(timeline));
            }

            List<CombatTimelineCapabilityWindow> capabilities = new List<CombatTimelineCapabilityWindow>();
            List<AttackPhaseWindow> phases = new List<AttackPhaseWindow>();
            List<CombatHitNodeData> nodes = new List<CombatHitNodeData>();
            List<BossReactionGateWindow> bossReactionGates = new List<BossReactionGateWindow>();
            List<PlayerMotionWindow> playerMotions = new List<PlayerMotionWindow>();

            foreach (CombatTimelineClip clip in timeline.EnumerateAllClips())
            {
                if (clip is CombatPhaseClip phaseClip)
                {
                    phases.Add(new AttackPhaseWindow(phaseClip.Phase, clip.ToActionWindow()));
                    continue;
                }

                if (clip is CombatHitNodeClip hitNodeClip)
                {
                    nodes.Add(new CombatHitNodeData(hitNodeClip, nodes.Count + 1));
                }

                if (clip is CombatMotionReferenceClip motionClip &&
                    motionClip.MotionKind == CombatTimelineClipKind.PlayerMotion &&
                    motionClip.PlayerMotionId != PlayerActionMotionId.None)
                {
                    playerMotions.Add(new PlayerMotionWindow(
                        motionClip.PlayerMotionId,
                        motionClip.StartTime,
                        motionClip.EndTime));
                }

                if (clip is BossReactionGateWindowClip bossGateClip)
                {
                    bossReactionGates.Add(new BossReactionGateWindow(
                        bossGateClip.Name,
                        bossGateClip.StartTime,
                        bossGateClip.EndTime,
                        bossGateClip.AttackTypes,
                        bossGateClip.Policy,
                        bossGateClip.BlockedOutcome));
                    continue;
                }

                if (CombatTimelineCapabilityUtility.TryDescribeClip(clip, out CombatTimelineCapabilityDescriptor descriptor))
                {
                    capabilities.Add(new CombatTimelineCapabilityWindow(
                        clip.Name,
                        descriptor.Id,
                        clip.StartTime,
                        clip.EndTime));
                }
            }

            capabilities.Sort(CompareCapabilityWindows);
            phases.Sort((left, right) => left.Window.StartTime.CompareTo(right.Window.StartTime));
            nodes.Sort((left, right) => left.Window.StartTime.CompareTo(right.Window.StartTime));
            bossReactionGates.Sort((left, right) => left.StartTime.CompareTo(right.StartTime));
            playerMotions.Sort((left, right) => left.StartTime.CompareTo(right.StartTime));
            return new CombatActionRuntimeSpec(
                capabilities.ToArray(),
                phases.ToArray(),
                nodes.ToArray(),
                bossReactionGates.ToArray(),
                playerMotions.ToArray());
        }

        public CombatActionCapabilitySnapshot Evaluate(float elapsedTime)
        {
            return new CombatActionCapabilitySnapshot(
                ResolvePhase(elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_Commit, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Hit_Attack, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Input_AttackBuffer, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Input_EvadeBuffer, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Input_SkillBuffer, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_AttackCombo, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_ToEvade, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_ToSkill, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_ToGuard, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_AttackReset, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Cancel_MovementReturn, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Defense_Invincible, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Defense_PerfectEvade, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Defense_PerfectGuard, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Defense_GuardBlock, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Input_PerfectGuardChain, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Armor_SuperArmor, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Armor_Uninterruptible, elapsedTime),
                IsActive(CombatTimelineCapabilityId.Reaction_CanReturn, elapsedTime),
                GetActiveHitNodeIds(elapsedTime),
                GetActivePlayerMotionIds(elapsedTime));
        }

        public CombatHitNodeData[] ToHitNodeData()
        {
            return (CombatHitNodeData[])hitNodes.Clone();
        }

        public bool HasCapability(CombatTimelineCapabilityId capabilityId)
        {
            for (int i = 0; i < capabilityWindows.Length; i++)
            {
                if (capabilityWindows[i].SourceCapabilityId == capabilityId)
                {
                    return true;
                }
            }

            return false;
        }

        public ActionWindow[] GetWindows(CombatTimelineCapabilityId capabilityId)
        {
            List<ActionWindow> windows = new List<ActionWindow>();
            for (int i = 0; i < capabilityWindows.Length; i++)
            {
                CombatTimelineCapabilityWindow capability = capabilityWindows[i];
                if (Matches(capability, capabilityId))
                {
                    windows.Add(capability.ToActionWindow());
                }
            }

            return windows.ToArray();
        }

        /// <summary>
        /// 返回指定能力中开始时间最早的窗口，供 Reaction 等需要单一时间锚点的转换逻辑使用。
        /// </summary>
        /// <param name="capabilityId">要查询的显式能力 ID。</param>
        /// <param name="window">找到时写回开始时间最早的动作窗口；未找到时写回默认值。</param>
        /// <returns>存在对应能力窗口时返回 true，否则返回 false。</returns>
        public bool TryGetEarliestWindow(CombatTimelineCapabilityId capabilityId, out ActionWindow window)
        {
            bool found = false;
            CombatTimelineCapabilityWindow earliest = default;
            for (int i = 0; i < capabilityWindows.Length; i++)
            {
                CombatTimelineCapabilityWindow capability = capabilityWindows[i];
                if (!Matches(capability, capabilityId))
                {
                    continue;
                }

                if (!found || capability.StartTime < earliest.StartTime)
                {
                    found = true;
                    earliest = capability;
                }
            }

            if (found)
            {
                window = earliest.ToActionWindow();
                return true;
            }

            window = default;
            return false;
        }

        /// <summary>
        /// 收集当前动作时间点覆盖的全部 HitNode ID，供能力快照驱动命中业务。
        /// </summary>
        /// <param name="elapsedTime">当前动作已经过时间，单位为秒。</param>
        /// <returns>当前激活的 HitNode ID；没有窗口激活时返回空数组。</returns>
        private string[] GetActiveHitNodeIds(float elapsedTime)
        {
            List<string> activeNodeIds = null;
            for (int i = 0; i < hitNodes.Length; i++)
            {
                if (!hitNodes[i].Contains(elapsedTime))
                {
                    continue;
                }

                activeNodeIds ??= new List<string>();
                activeNodeIds.Add(hitNodes[i].HitNodeId);
            }

            return activeNodeIds != null ? activeNodeIds.ToArray() : Array.Empty<string>();
        }

        /// <summary>
        /// 返回当前时间点激活的 Player Motion ID；完整位移参数继续由 PlayerActionMotionProfile 提供。
        /// </summary>
        /// <param name="elapsedTime">当前动作已经过时间，单位为秒。</param>
        /// <returns>当前激活的 Player Motion ID；没有窗口激活时返回空数组。</returns>
        private PlayerActionMotionId[] GetActivePlayerMotionIds(float elapsedTime)
        {
            List<PlayerActionMotionId> activeMotionIds = null;
            for (int i = 0; i < playerMotionWindows.Length; i++)
            {
                PlayerMotionWindow window = playerMotionWindows[i];
                if (!window.Contains(elapsedTime))
                {
                    continue;
                }

                activeMotionIds ??= new List<PlayerActionMotionId>();
                activeMotionIds.Add(window.MotionId);
            }

            return activeMotionIds != null
                ? activeMotionIds.ToArray()
                : Array.Empty<PlayerActionMotionId>();
        }

        public BossReactionGateResult EvaluateBossReactionGate(
            CombatAttackType attackType,
            CombatHitOutcome outcome,
            float elapsedTime)
        {
            for (int i = 0; i < bossReactionGateWindows.Length; i++)
            {
                BossReactionGateWindow gate = bossReactionGateWindows[i];
                if (gate.Policy == BossReactionGatePolicy.BlockInterrupt &&
                    gate.Contains(elapsedTime) &&
                    gate.MatchesAttackType(attackType) &&
                    gate.BlocksOutcome(outcome))
                {
                    return BossReactionGateResult.BlockedByBossReactionGate;
                }
            }

            for (int i = 0; i < bossReactionGateWindows.Length; i++)
            {
                BossReactionGateWindow gate = bossReactionGateWindows[i];
                if (gate.Policy == BossReactionGatePolicy.AllowInterrupt &&
                    gate.Contains(elapsedTime) &&
                    gate.MatchesAttackType(attackType))
                {
                    return BossReactionGateResult.Allowed;
                }
            }

            return BossReactionGateResult.BlockedByClosedWindow;
        }

        private PlayerStatePhase ResolvePhase(float elapsedTime)
        {
            for (int i = 0; i < phaseWindows.Length; i++)
            {
                if (phaseWindows[i].Contains(elapsedTime))
                {
                    return phaseWindows[i].Phase;
                }
            }

            return PlayerStatePhase.None;
        }

        private bool IsActive(CombatTimelineCapabilityId capabilityId, float elapsedTime)
        {
            for (int i = 0; i < capabilityWindows.Length; i++)
            {
                CombatTimelineCapabilityWindow capability = capabilityWindows[i];
                if (Matches(capability, capabilityId) && capability.Contains(elapsedTime))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool Matches(CombatTimelineCapabilityWindow capability, CombatTimelineCapabilityId capabilityId)
        {
            return capability.SourceCapabilityId == capabilityId;
        }

        private static int CompareCapabilityWindows(CombatTimelineCapabilityWindow left, CombatTimelineCapabilityWindow right)
        {
            int startCompare = left.StartTime.CompareTo(right.StartTime);
            if (startCompare != 0)
            {
                return startCompare;
            }

            return string.Compare(left.Name, right.Name, StringComparison.Ordinal);
        }
    }
}
