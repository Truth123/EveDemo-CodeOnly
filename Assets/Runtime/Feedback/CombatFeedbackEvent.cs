// 文件说明：维护 HitStop、CameraShake、VFX/SFX 事件和测试触发。
// 所属模块：战斗反馈。
// 运行影响：影响命中反馈播放、调试触发和反馈事件传递。

using ProjectEVE.Combat;
using UnityEngine;

namespace ProjectEVE.Feedback
{
    /// <summary>
    /// 战斗反馈类型。只描述表现需求，不参与伤害、状态切换或窗口判定。
    /// </summary>
    public enum CombatFeedbackKind
    {
        None = 0,
        PlayerHit = 1,
        BossHit = 2,
        GuardHit = 3,
        PerfectGuard = 4,
        PerfectEvade = 5,
        Knockdown = 6,
        Dead = 7
    }

    /// <summary>
    /// 单次战斗反馈请求。由命中结算点创建，反馈播放层消费。
    /// </summary>
    public readonly struct CombatFeedbackEvent
    {
        public readonly CombatFeedbackKind Kind;
        public readonly CombatTeam AttackerTeam;
        public readonly CombatAttackType AttackType;
        public readonly CombatHitOutcome Outcome;
        public readonly Vector3 HitPosition;
        public readonly Vector3 HitDirection;
        public readonly float AppliedHpDamage;
        public readonly Component Source;
        public readonly GameObject Target;

        /// <summary>
        /// 创建 CombatFeedbackEvent 实例，并准备 战斗反馈 模块需要的初始状态。
        /// </summary>
        public CombatFeedbackEvent(
            CombatFeedbackKind kind,
            CombatTeam attackerTeam,
            CombatAttackType attackType,
            CombatHitOutcome outcome,
            Vector3 hitPosition,
            Vector3 hitDirection,
            float appliedHpDamage,
            Component source,
            GameObject target)
        {
            Kind = kind;
            AttackerTeam = attackerTeam;
            AttackType = attackType;
            Outcome = outcome;
            HitPosition = hitPosition;
            HitDirection = hitDirection;
            AppliedHpDamage = appliedHpDamage;
            Source = source;
            Target = target;
        }

        public static CombatFeedbackEvent FromHit(
            in CombatHitData hit,
            in CombatHitResult result,
            Component source,
            GameObject target)
        {
            CombatFeedbackKind kind = ResolveKind(hit.AttackerTeam, result.Outcome);
            return new CombatFeedbackEvent(
                kind,
                hit.AttackerTeam,
                hit.AttackType,
                result.Outcome,
                hit.HitPosition,
                hit.HitDirection,
                result.AppliedHpDamage,
                source,
                target);
        }

        /// <summary>
        /// 解析 Kind 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static CombatFeedbackKind ResolveKind(CombatTeam attackerTeam, CombatHitOutcome outcome)
        {
            switch (outcome)
            {
                case CombatHitOutcome.PerfectEvade:
                    return CombatFeedbackKind.PerfectEvade;
                case CombatHitOutcome.PerfectGuard:
                    return CombatFeedbackKind.PerfectGuard;
                case CombatHitOutcome.GuardHit:
                    return CombatFeedbackKind.GuardHit;
                case CombatHitOutcome.Knockdown:
                    return CombatFeedbackKind.Knockdown;
                case CombatHitOutcome.Dead:
                    return CombatFeedbackKind.Dead;
                case CombatHitOutcome.DamageOnly:
                case CombatHitOutcome.HitReaction:
                    return attackerTeam == CombatTeam.Player
                        ? CombatFeedbackKind.PlayerHit
                        : CombatFeedbackKind.BossHit;
                default:
                    return CombatFeedbackKind.None;
            }
        }
    }
}
