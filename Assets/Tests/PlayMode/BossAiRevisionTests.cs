// 文件说明：验证 Raven 新动作数据、压力、滚动行为、恢复规则和 Reposition 生命周期。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时对象和内存 Timeline。

using NUnit.Framework;
using ProjectEVE.Boss.Actions;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Brain;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossAiRevisionTests
    {
        [Test]
        public void DecisionMemory_AttackPressureCommitsSeventyThenThirtyPercent()
        {
            BossDecisionMemory memory = new BossDecisionMemory();

            memory.RecordAction("Attack", BossActionKind.Attack, 20f, false);
            Assert.That(memory.TempoPressure, Is.EqualTo(14f).Within(0.001f));

            memory.RecordFirstActiveHit();
            memory.RecordFirstActiveHit();
            Assert.That(memory.TempoPressure, Is.EqualTo(20f).Within(0.001f));
        }

        [Test]
        public void DecisionMemory_TracksGuardAndContinuousRetreat()
        {
            BossDecisionMemory memory = new BossDecisionMemory();
            for (int i = 0; i < 20; i++)
            {
                memory.TickPlayerBehavior(i * 0.1f, 0.1f, PlayerStateId.Guard, i >= 14, 8f, 2f, 0.6f);
            }

            Assert.That(memory.IsFrequentGuarder, Is.True);
            Assert.That(memory.IsSustainedRetreat, Is.True);

            memory.TickPlayerBehavior(11f, 0.1f, PlayerStateId.Idle, false, 8f, 2f, 0.6f);
            Assert.That(memory.IsFrequentGuarder, Is.False);
            Assert.That(memory.IsSustainedRetreat, Is.False);
        }

        [Test]
        public void RecoveryTracker_RequiresAllConditionsAndUsesLongestDeadline()
        {
            BossAttackTimelineAsset timeline = CreateTimeline("RecoveryRules", 3f, 1f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("HitNode", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        Hit("A", 1f, 1.2f),
                        Hit("B", 2f, 2.2f)
                    }
                }
            });
            timeline.SetDefenseRewardRules(new[]
            {
                new BossDefenseRewardRule
                {
                    Name = "AllPG",
                    RewardDuration = 3f,
                    Conditions = new[]
                    {
                        new BossDefenseRewardCondition { HitNodeId = "A", AllowedOutcomes = BossDefenseOutcomeMask.PerfectGuard },
                        new BossDefenseRewardCondition { HitNodeId = "B", AllowedOutcomes = BossDefenseOutcomeMask.PerfectGuard }
                    }
                }
            });

            BossAttackRecoveryTracker tracker = new BossAttackRecoveryTracker();
            tracker.Begin(timeline.ToBossAttackDefinition(), 1, 10f);
            Assert.That(
                tracker.TryRecordOutcome(1, "A", CombatHitOutcome.PerfectGuard, 10.5f, out float firstPressureIncrease),
                Is.True);
            Assert.That(firstPressureIncrease, Is.EqualTo(0f));
            Assert.That(tracker.RewardEndTime, Is.EqualTo(0f));

            Assert.That(
                tracker.TryRecordOutcome(1, "B", CombatHitOutcome.PerfectGuard, 11f, out float secondPressureIncrease),
                Is.True);
            Assert.That(secondPressureIncrease, Is.EqualTo(0f));
            Assert.That(tracker.NaturalRecoveryEndTime, Is.EqualTo(13.2f).Within(0.001f));
            Assert.That(tracker.RewardEndTime, Is.EqualTo(14.26f).Within(0.001f));
            Assert.That(tracker.FinalRecoveryEndTime, Is.EqualTo(14.26f).Within(0.001f));
            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void RecoveryTracker_AggregatesPositiveHitPressureByAttackInstance()
        {
            BossAttackTimelineAsset timeline = CreateTimeline("HitPressureAggregation", 1f, 1f);
            BossAttackRecoveryTracker tracker = new BossAttackRecoveryTracker();
            tracker.Begin(timeline.ToBossAttackDefinition(), 41, 0f);

            float totalPressureIncrease = 0f;
            for (int i = 0; i < 6; i++)
            {
                Assert.That(
                    tracker.TryRecordOutcome(
                        41,
                        $"Hit_{i}",
                        CombatHitOutcome.HitReaction,
                        i * 0.1f,
                        out float pressureIncrease),
                    Is.True);
                totalPressureIncrease += pressureIncrease;
            }

            Assert.That(totalPressureIncrease, Is.EqualTo(4f).Within(0.001f));
            Assert.That(
                tracker.TryRecordOutcome(41, "Finisher", CombatHitOutcome.Knockdown, 0.7f, out float knockdownIncrease),
                Is.True);
            Assert.That(knockdownIncrease, Is.EqualTo(6f).Within(0.001f));
            Assert.That(
                tracker.TryRecordOutcome(41, "LateHit", CombatHitOutcome.Dead, 0.8f, out float deadIncrease),
                Is.True);
            Assert.That(deadIncrease, Is.EqualTo(0f));
            Assert.That(totalPressureIncrease + knockdownIncrease + deadIncrease, Is.EqualTo(10f).Within(0.001f));

            Assert.That(
                tracker.TryRecordOutcome(40, "StaleDetached", CombatHitOutcome.Knockdown, 0.9f, out float staleIncrease),
                Is.False);
            Assert.That(staleIncrease, Is.EqualTo(0f));

            tracker.Begin(timeline.ToBossAttackDefinition(), 42, 1f);
            Assert.That(
                tracker.TryRecordOutcome(41, "PreviousAttack", CombatHitOutcome.Knockdown, 1.1f, out float previousIncrease),
                Is.False);
            Assert.That(previousIncrease, Is.EqualTo(0f));
            Assert.That(
                tracker.TryRecordOutcome(42, "CurrentAttack", CombatHitOutcome.HitReaction, 1.2f, out float currentIncrease),
                Is.True);
            Assert.That(currentIncrease, Is.EqualTo(4f).Within(0.001f));

            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void RecoveryTracker_KnockdownBeforeHitReactionRemainsCappedAtTen()
        {
            BossAttackTimelineAsset timeline = CreateTimeline("HitPressureReverseOrder", 1f, 1f);
            BossAttackRecoveryTracker tracker = new BossAttackRecoveryTracker();
            tracker.Begin(timeline.ToBossAttackDefinition(), 51, 0f);

            Assert.That(
                tracker.TryRecordOutcome(51, "Knockdown", CombatHitOutcome.Knockdown, 0.1f, out float knockdownIncrease),
                Is.True);
            Assert.That(knockdownIncrease, Is.EqualTo(10f).Within(0.001f));
            Assert.That(
                tracker.TryRecordOutcome(51, "FollowUp", CombatHitOutcome.HitReaction, 0.2f, out float followUpIncrease),
                Is.True);
            Assert.That(followUpIncrease, Is.EqualTo(0f));

            Object.DestroyImmediate(timeline);
        }

        [Test]
        public void ActionRunner_RepositionDoesNotStartAttackExecutor()
        {
            GameObject boss = new GameObject("RepositionRunnerBoss");
            boss.AddComponent<CharacterController>();
            BossAttackExecutor executor = boss.AddComponent<BossAttackExecutor>();
            BossMotionController motion = boss.AddComponent<BossMotionController>();
            BossMotionWarpProfile profile = ScriptableObject.CreateInstance<BossMotionWarpProfile>();
            BossAttackMotionConfig motionConfig = new BossAttackMotionConfig(
                "Reposition",
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 10f, 0f, 0f),
                null,
                null,
                null);
            typeof(BossMotionWarpProfile)
                .GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(profile, new[] { motionConfig });
            typeof(BossMotionController)
                .GetField("motionWarpProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(motion, profile);
            BossAnimationBridge animation = boss.AddComponent<BossAnimationBridge>();
            BossMovementSystem movement = new BossMovementSystem();
            movement.Bind(motion);
            BossActionRunner runner = new BossActionRunner();
            runner.Bind(executor, movement, animation);
            BossAttackTimelineAsset timeline = CreateTimeline("Reposition", 0.2f, 0f, CombatTimelineActionKind.BossReposition);

            BossActionRunnerStartResult start = runner.StartAction(
                timeline.ToBossAttackDefinition(),
                BossActionKind.Reposition,
                null);
            Assert.That(start.Succeeded, Is.True);
            Assert.That(start.AttackInstanceId, Is.EqualTo(0));
            Assert.That(executor.IsExecuting, Is.False);
            Assert.That(runner.Tick(0.21f).IsCompleted, Is.True);

            runner.EndAction();
            Object.DestroyImmediate(timeline);
            Object.DestroyImmediate(profile);
            Object.DestroyImmediate(boss);
        }

        [Test]
        [Ignore("当前测试程序集不可用 UnityEngine.TestTools.LogAssert；该反例会故意触发 Error log。")]
        public void ActionRunner_RejectsActionKindAndHitNodeMismatch()
        {
            GameObject boss = new GameObject("InvalidActionRunnerBoss");
            BossAttackExecutor executor = boss.AddComponent<BossAttackExecutor>();
            BossActionRunner runner = new BossActionRunner();
            runner.Bind(executor, null, null);
            BossAttackTimelineAsset attackWithoutHitNode = CreateTimeline("InvalidAttack", 0.2f, 0f);
            BossAttackTimelineAsset repositionWithHitNode = CreateTimeline(
                "InvalidReposition",
                0.2f,
                0f,
                CombatTimelineActionKind.BossReposition);
            repositionWithHitNode.SetTracks(new[]
            {
                new CombatTimelineTrack("HitNode", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[] { Hit("InvalidHit", 0.05f, 0.1f) }
                }
            });

            BossActionRunnerStartResult attackResult = runner.StartAction(
                attackWithoutHitNode.ToBossAttackDefinition(),
                BossActionKind.Attack,
                null);
            BossActionRunnerStartResult repositionResult = runner.StartAction(
                repositionWithHitNode.ToBossAttackDefinition(),
                BossActionKind.Reposition,
                null);

            Assert.That(attackResult.Succeeded, Is.False);
            Assert.That(repositionResult.Succeeded, Is.False);
            Assert.That(runner.IsRunning, Is.False);

            Object.DestroyImmediate(attackWithoutHitNode);
            Object.DestroyImmediate(repositionWithHitNode);
            Object.DestroyImmediate(boss);
        }

        [Test]
        public void DecisionMemory_RepositionReducesPressureOnceThenContinuesNaturalDecay()
        {
            BossDecisionMemory memory = new BossDecisionMemory();
            memory.AddPressure(50f);

            memory.RecordAction("RapidMoveBack", BossActionKind.Reposition, 0f, false);
            Assert.That(memory.TempoPressure, Is.EqualTo(30f).Within(0.001f));

            memory.TickPressure(BossStateId.Action, BossActionKind.Reposition, false, 1f);

            Assert.That(memory.TempoPressure, Is.EqualTo(20f).Within(0.001f));
            Assert.That(memory.ConsecutiveAttackActionCount, Is.EqualTo(0));
            Assert.That(memory.RecentActionIds[0], Is.EqualTo("RapidMoveBack"));
        }

        [TestCase(BossCombatPhaseId.Phase1)]
        [TestCase(BossCombatPhaseId.Phase2)]
        [TestCase(BossCombatPhaseId.Desperation)]
        public void DecisionMemory_RepositionReductionAllowsDecayModeToExitOnNextUpdate(
            BossCombatPhaseId phase)
        {
            BossPhasePressureProfile profile = BossPhasePressureProfile.CreateDefault(phase);
            BossDecisionMemory memory = new BossDecisionMemory();
            memory.AddPressure(profile.EnterDecayPressure);
            memory.UpdatePressureMode(profile);
            Assert.That(memory.PressureDecayMode, Is.True);

            memory.RecordAction("RapidMoveBack", BossActionKind.Reposition, 0f, false);

            Assert.That(memory.TempoPressure, Is.LessThanOrEqualTo(profile.ExitDecayPressure));
            Assert.That(memory.PressureDecayMode, Is.True);

            memory.UpdatePressureMode(profile);

            Assert.That(memory.PressureDecayMode, Is.False);
        }

        [TestCase(BossStateId.Idle, 10f)]
        [TestCase(BossStateId.Strafe, 10f)]
        [TestCase(BossStateId.Approach, 6f)]
        [TestCase(BossStateId.HitStagger, 6f)]
        [TestCase(BossStateId.Knockdown, 10f)]
        public void DecisionMemory_UsesCurrentStatePressureDecayRates(
            BossStateId state,
            float expectedDecay)
        {
            BossDecisionMemory memory = new BossDecisionMemory();
            memory.AddPressure(20f);

            memory.TickPressure(state, null, false, 1f);

            Assert.That(memory.TempoPressure, Is.EqualTo(20f - expectedDecay).Within(0.001f));
        }

        [Test]
        public void DecisionMemory_RecoveryDecaysPressureUnlessPlayerIsInHitStun()
        {
            BossDecisionMemory memory = new BossDecisionMemory();
            memory.AddPressure(20f);

            memory.TickPressure(BossStateId.Recovery, null, false, 1f);
            Assert.That(memory.TempoPressure, Is.EqualTo(12f).Within(0.001f));

            memory.TickPressure(BossStateId.Recovery, null, true, 1f);
            Assert.That(memory.TempoPressure, Is.EqualTo(12f).Within(0.001f));

            memory.TickPressure(BossStateId.Action, BossActionKind.Attack, false, 1f);
            Assert.That(memory.TempoPressure, Is.EqualTo(12f).Within(0.001f));
        }

        private static BossAttackTimelineAsset CreateTimeline(
            string id,
            float duration,
            float recovery,
            CombatTimelineActionKind kind = CombatTimelineActionKind.BossAttack)
        {
            BossAttackTimelineAsset timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(CombatTimelineOwner.Boss, kind, id, id, id, duration, 60f);
            timeline.ConfigureBossAttack(90f, 0f, recovery);
            return timeline;
        }

        private static CombatHitNodeClip Hit(string id, float start, float end)
        {
            return new CombatHitNodeClip
            {
                Name = id,
                CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                StartTime = start,
                EndTime = end,
                EffectiveRange = 3f,
                EffectiveAngle = 90f,
                MaxHitsPerTarget = 1
            };
        }
    }
}
