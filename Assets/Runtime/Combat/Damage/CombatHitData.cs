// 文件说明：定义通用命中、伤害和防守方状态数据。
// 所属模块：战斗伤害数据。
// 运行影响：影响 Player/Boss 命中链路的数据传递。

using System;
using UnityEngine;

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 攻击命中数据。由 Hitbox/Hurtbox 系统生成，交给 CombatHitResolver 解析。
    /// </summary>
    [Serializable]
    public struct CombatHitData
    {
        /// <summary>本次攻击的唯一编号，用于防止同一攻击重复命中。</summary>
        public int AttackId;
        /// <summary>攻击来源阵营。</summary>
        public CombatTeam AttackerTeam;
        /// <summary>攻击来源类型，用于 Light / Heavy / Skill 等来源门控，不表达击倒或破防效果。</summary>
        public CombatAttackType AttackType;
        /// <summary>打点作者声明的基础反应意图，Resolver 会把它转换为最终命中结果。</summary>
        public CombatReactionIntent ReactionIntent;
        /// <summary>HP 伤害。</summary>
        public float Damage;
        /// <summary>削韧伤害，后续用于 Boss 或玩家韧性系统。</summary>
        public float PoiseDamage;
        /// <summary>防御伤害语义数据；当前保留给 Timeline 与命中结果，不再驱动玩家 SH 或 Boss 独立护盾。</summary>
        public float GuardDamage;
        /// <summary>命中世界坐标，用于特效、音效和击退来源。</summary>
        public Vector3 HitPosition;
        /// <summary>命中方向，用于受击动画、击退和防御方向判定。</summary>
        public Vector3 HitDirection;
        /// <summary>该攻击是否允许普通防御。</summary>
        public bool CanBeGuarded;
        /// <summary>该攻击是否允许完美防御。</summary>
        public bool CanBePerfectGuarded;
        /// <summary>该攻击是否允许完美闪避。</summary>
        public bool CanBePerfectEvaded;
    }

    /// <summary>
    /// 防守方在命中发生瞬间的关键战斗状态快照。
    /// </summary>
    [Serializable]
    public struct DefenderCombatState
    {
        /// <summary>防守方当前 HP。</summary>
        public float CurrentHp;
        /// <summary>是否处于 Guard 顶层状态。</summary>
        public bool IsGuarding;
        /// <summary>普通防御判定是否已经生效。</summary>
        public bool IsGuardBlockActive;
        /// <summary>是否处于完美防御窗口。</summary>
        public bool IsPerfectGuardWindow;
        /// <summary>是否处于 Evade 顶层状态。</summary>
        public bool IsEvading;
        /// <summary>是否处于闪避无敌帧。</summary>
        public bool IsEvadeInvincible;
        /// <summary>是否处于完美闪避窗口。</summary>
        public bool IsPerfectEvadeWindow;
        /// <summary>是否存在 NearMiss 候选，用于完美闪避外圈检测。</summary>
        public bool HasNearMissCandidate;
    }

    /// <summary>
    /// 命中解析输出，供状态机和资源系统执行后续状态切换与扣血。
    /// </summary>
    [Serializable]
    public struct CombatHitResult
    {
        /// <summary>本次命中的最终结果。</summary>
        public CombatHitOutcome Outcome;
        /// <summary>最终应用到 HP 的伤害。</summary>
        public float AppliedHpDamage;
        /// <summary>最终应用到防御值的伤害。</summary>
        public float AppliedGuardDamage;

        /// <summary>
        /// 创建一次命中解析结果。
        /// </summary>
        public CombatHitResult(CombatHitOutcome outcome, float appliedHpDamage, float appliedGuardDamage)
        {
            Outcome = outcome;
            AppliedHpDamage = appliedHpDamage;
            AppliedGuardDamage = appliedGuardDamage;
        }
    }
}
