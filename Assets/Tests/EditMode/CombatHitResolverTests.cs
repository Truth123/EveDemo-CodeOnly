// 文件说明：验证通用命中解析器的防御、闪避、伤害和反应输出规则。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中构造 plain C# 数据，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Combat;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class CombatHitResolverTests
    {
        [Test]
        public void CombatHitResolver_PerfectGuardProducesNoDamageOrGuardDamage()
        {
            CombatHitData hit = new CombatHitData
            {
                AttackId = 100,
                AttackerTeam = CombatTeam.Boss,
                AttackType = CombatAttackType.HeavyAttack,
                ReactionIntent = CombatReactionIntent.HitReaction,
                Damage = 45f,
                GuardDamage = 30f,
                HitPosition = Vector3.zero,
                HitDirection = Vector3.forward,
                CanBeGuarded = true,
                CanBePerfectGuarded = true,
                CanBePerfectEvaded = true
            };
            DefenderCombatState defender = new DefenderCombatState
            {
                CurrentHp = 100f,
                IsGuarding = true,
                IsGuardBlockActive = true,
                IsPerfectGuardWindow = true
            };

            CombatHitResult result = CombatHitResolver.Resolve(hit, defender);

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
            Assert.That(result.AppliedHpDamage, Is.Zero);
            Assert.That(result.AppliedGuardDamage, Is.Zero);
        }

        /// <summary>
        /// 验证普通防御即使面对足以致死的可防攻击也保持零 HP 伤害，同时继续输出 GuardHit 与 GuardDamage 数据。
        /// </summary>
        [Test]
        public void CombatHitResolver_GuardHitProducesNoHpDamageAndPreservesGuardDamage()
        {
            CombatHitData hit = CreateHit(
                CombatReactionIntent.HitReaction,
                canBeGuarded: true,
                canBePerfectGuarded: true,
                canBePerfectEvaded: true);
            DefenderCombatState defender = new DefenderCombatState
            {
                CurrentHp = 10f,
                IsGuarding = true,
                IsGuardBlockActive = true
            };

            CombatHitResult result = CombatHitResolver.Resolve(hit, defender);

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.GuardHit));
            Assert.That(result.AppliedHpDamage, Is.Zero);
            Assert.That(result.AppliedGuardDamage, Is.EqualTo(hit.GuardDamage));
        }

        [Test]
        public void CombatHitResolver_UnblockableHitIgnoresGuardAndPerfectGuard()
        {
            CombatHitData hit = CreateHit(
                CombatReactionIntent.Knockdown,
                canBeGuarded: false,
                canBePerfectGuarded: false,
                canBePerfectEvaded: true);
            DefenderCombatState defender = new DefenderCombatState
            {
                CurrentHp = 100f,
                IsGuarding = true,
                IsGuardBlockActive = true,
                IsPerfectGuardWindow = true
            };

            CombatHitResult result = CombatHitResolver.Resolve(hit, defender);

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.Knockdown));
            Assert.That(result.AppliedHpDamage, Is.EqualTo(hit.Damage));
            Assert.That(result.AppliedGuardDamage, Is.Zero);
        }

        [Test]
        public void CombatHitResolver_UnblockableHitCanStillPerfectEvade()
        {
            CombatHitData hit = CreateHit(
                CombatReactionIntent.Knockdown,
                canBeGuarded: false,
                canBePerfectGuarded: false,
                canBePerfectEvaded: true);
            DefenderCombatState defender = new DefenderCombatState
            {
                CurrentHp = 100f,
                IsEvading = true,
                IsEvadeInvincible = true,
                IsPerfectEvadeWindow = true
            };

            CombatHitResult result = CombatHitResolver.Resolve(hit, defender);

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.PerfectEvade));
            Assert.That(result.AppliedHpDamage, Is.Zero);
            Assert.That(result.AppliedGuardDamage, Is.Zero);
        }

        private static CombatHitData CreateHit(
            CombatReactionIntent reactionIntent,
            bool canBeGuarded,
            bool canBePerfectGuarded,
            bool canBePerfectEvaded)
        {
            return new CombatHitData
            {
                AttackId = 100,
                AttackerTeam = CombatTeam.Boss,
                AttackType = CombatAttackType.HeavyAttack,
                ReactionIntent = reactionIntent,
                Damage = 45f,
                GuardDamage = 30f,
                HitPosition = Vector3.zero,
                HitDirection = Vector3.forward,
                CanBeGuarded = canBeGuarded,
                CanBePerfectGuarded = canBePerfectGuarded,
                CanBePerfectEvaded = canBePerfectEvaded
            };
        }
    }
}
