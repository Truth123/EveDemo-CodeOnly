// 文件说明：验证 BossReactionSystem 反应优先级和 BossActor 受击入口接入。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject 和临时 Timeline 资产，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.Actions;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.Config;
using ProjectEVE.Boss.Reaction;
using ProjectEVE.Boss.State;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossReactionSystemTests
    {
        [TearDown]
        public void TearDown()
        {
            CombatTimelineProvider.ResetCache();
        }

        [Test]
        public void BossRuntimeConfigResolver_TryGetHitStaggerConfig_ReturnsRegisteredTimeline()
        {
            BossReactionTimelineAsset timeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out timeline);
                BossRuntimeConfigResolver resolver = new BossRuntimeConfigResolver();

                bool found = resolver.TryGetHitStaggerConfig(out CombatTimelineReactionConfig config);

                Assert.That(found, Is.True);
                Assert.That(config.TotalDuration, Is.EqualTo(0.5f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void BossRuntimeConfigResolver_TryGetKnockdownConfig_ReturnsRegisteredTimeline()
        {
            BossReactionTimelineAsset timeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out timeline);
                BossRuntimeConfigResolver resolver = new BossRuntimeConfigResolver();

                bool found = resolver.TryGetKnockdownConfig(out CombatTimelineReactionConfig config);

                Assert.That(found, Is.True);
                Assert.That(config.ReactionType, Is.EqualTo(CombatTimelineReactionType.Knockdown));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void BossRuntimeConfigResolver_TryGetHitStaggerConfig_LogsOnceWhenMissing()
        {
            ClearRegisteredTimelines();
            BossRuntimeConfigResolver resolver = new BossRuntimeConfigResolver();
            bool loggerEnabled = Debug.unityLogger.logEnabled;
            Debug.unityLogger.logEnabled = false;
            bool first;
            bool second;
            try
            {
                first = resolver.TryGetHitStaggerConfig(out _);
                second = resolver.TryGetHitStaggerConfig(out _);
            }
            finally
            {
                Debug.unityLogger.logEnabled = loggerEnabled;
            }

            Assert.That(first, Is.False);
            Assert.That(second, Is.False);
            Assert.That(
                typeof(BossRuntimeConfigResolver)
                    .GetField("missingBossHitStaggerTimelineLogged", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(resolver),
                Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_ReturnsDeadWithHighestPriority()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.Dead,
                false,
                BossReactionGateResult.BlockedByBossReactionGate,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => false, () => false);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.Dead));
            Assert.That(decision.Interrupts, Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_LightHeavyDoesNotInterruptAttack()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.HitReaction,
                false,
                BossReactionGateResult.BlockedByClosedWindow,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.None));
            Assert.That(decision.Interrupts, Is.False);
            Assert.That(decision.RejectReason, Is.EqualTo("BossReactionGateClosed"));
        }

        [Test]
        public void BossReactionSystem_Decide_RepositionIgnoresAttackReactionGate()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.HitReaction,
                false,
                BossReactionGateResult.BlockedByClosedWindow,
                false);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.HitStagger));
            Assert.That(decision.Interrupts, Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_LightAttackInterruptsAttackWhenWindowOpen()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.HitReaction,
                false,
                BossReactionGateResult.Allowed,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.HitStagger));
            Assert.That(decision.Interrupts, Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_DamageOnlyDoesNotEnterReaction()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.DamageOnly,
                false,
                BossReactionGateResult.Allowed,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.None));
            Assert.That(decision.Interrupts, Is.False);
            Assert.That(decision.RejectReason, Is.EqualTo("IgnoredOutcome:DamageOnly"));
        }

        [Test]
        public void BossReactionSystem_Decide_LightHeavyInterruptsNonAttack()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Idle,
                CombatHitOutcome.HitReaction,
                false,
                BossReactionGateResult.BlockedByClosedWindow);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.HitStagger));
            Assert.That(decision.Interrupts, Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_SkillKnockdownWhenNotArmored()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.Knockdown,
                false,
                BossReactionGateResult.Allowed,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.Knockdown));
            Assert.That(decision.Interrupts, Is.True);
        }

        [Test]
        public void BossReactionSystem_Decide_SkillDoesNotKnockdownWhenBlockedByBossGate()
        {
            BossPlayerHitReactionInput input = new BossPlayerHitReactionInput(
                BossStateId.Action,
                CombatHitOutcome.Knockdown,
                false,
                BossReactionGateResult.BlockedByBossReactionGate,
                true);

            BossReactionDecision decision = BossReactionSystem.DecidePlayerHit(input, () => true, () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.None));
            Assert.That(decision.Interrupts, Is.False);
            Assert.That(decision.BlockedByBossGate, Is.True);
            Assert.That(decision.RejectReason, Is.EqualTo("BlockedByBossReactionGate"));
        }

        [Test]
        public void BossReactionSystem_Decide_PerfectGuardStaggerRequiresHitNodeFlag()
        {
            BossReactionDecision decision = BossReactionSystem.DecidePerfectGuardBossResponse(
                BossStateId.Action,
                false,
                false,
                () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.None));
            Assert.That(decision.Interrupts, Is.False);
            Assert.That(decision.RejectReason, Is.EqualTo("PerfectGuardHitNodeDoesNotStaggerBoss"));
        }

        [Test]
        public void BossReactionSystem_Decide_ShieldBreakImmediatelyStartsLongStunWithPerfectGuardAnimation()
        {
            BossReactionDecision decision = BossReactionSystem.DecidePerfectGuardBossResponse(
                BossStateId.Action,
                false,
                true,
                () => true);

            Assert.That(decision.Id, Is.EqualTo(BossReactionDecisionId.ShieldBreakStun));
            Assert.That(decision.Interrupts, Is.True);
            BossReactionExecutionPlan plan = BossReactionSystem.CreateExecutionPlan(decision, BossHitDirectionId.Left);
            Assert.That(plan.TargetState, Is.EqualTo(BossStateId.ShieldBreakStun));
            Assert.That(plan.Animation, Is.EqualTo(BossReactionAnimationId.PerfectGuardStagger));
            Assert.That(plan.StopRuntime, Is.True);
            Assert.That(plan.RequiresHitStaggerConfig, Is.False);
        }

        [Test]
        public void BossReactionSystem_Decide_ShieldBreakStunOnlyAllowsSkillKnockdownToInterrupt()
        {
            BossReactionDecision light = BossReactionSystem.DecidePlayerHit(
                new BossPlayerHitReactionInput(
                    BossStateId.ShieldBreakStun,
                    CombatHitOutcome.Knockdown,
                    false,
                    BossReactionGateResult.Allowed,
                    false,
                    CombatAttackType.LightAttack),
                () => true,
                () => true);
            BossReactionDecision skill = BossReactionSystem.DecidePlayerHit(
                new BossPlayerHitReactionInput(
                    BossStateId.ShieldBreakStun,
                    CombatHitOutcome.Knockdown,
                    false,
                    BossReactionGateResult.Allowed,
                    false,
                    CombatAttackType.SkillAttack),
                () => true,
                () => true);

            Assert.That(light.Id, Is.EqualTo(BossReactionDecisionId.None));
            Assert.That(light.RejectReason, Is.EqualTo("ShieldBreakStunKeepsCurrentState"));
            Assert.That(skill.Id, Is.EqualTo(BossReactionDecisionId.Knockdown));
        }

        [Test]
        public void BossReactionSystem_CreateExecutionPlan_ReturnsNoneForRejectedDecision()
        {
            BossReactionDecision decision = BossReactionDecision.Reject("NoReaction");

            BossReactionExecutionPlan plan = BossReactionSystem.CreateExecutionPlan(
                decision,
                BossHitDirectionId.Front);

            Assert.That(plan.HasExecution, Is.False);
            Assert.That(plan.TargetState, Is.EqualTo(BossStateId.None));
            Assert.That(plan.Animation, Is.EqualTo(BossReactionAnimationId.None));
            Assert.That(plan.RequiresHitStaggerConfig, Is.False);
            Assert.That(plan.RequiresKnockdownConfig, Is.False);
        }

        [Test]
        public void BossReactionSystem_CreateExecutionPlan_MapsDeadToStopAndDeadState()
        {
            BossReactionExecutionPlan plan = BossReactionSystem.CreateExecutionPlan(
                BossReactionDecision.Interrupt(BossReactionDecisionId.Dead),
                BossHitDirectionId.Back);

            Assert.That(plan.HasExecution, Is.True);
            Assert.That(plan.StopRuntime, Is.True);
            Assert.That(plan.TargetState, Is.EqualTo(BossStateId.Dead));
            Assert.That(plan.Animation, Is.EqualTo(BossReactionAnimationId.None));
        }

        [Test]
        public void BossReactionSystem_CreateExecutionPlan_MapsHitStaggerAndPerfectGuardAnimations()
        {
            BossReactionExecutionPlan hitPlan = BossReactionSystem.CreateExecutionPlan(
                BossReactionDecision.Interrupt(BossReactionDecisionId.HitStagger),
                BossHitDirectionId.Left);
            BossReactionExecutionPlan perfectGuardPlan = BossReactionSystem.CreateExecutionPlan(
                BossReactionDecision.Interrupt(BossReactionDecisionId.PerfectGuardStagger),
                BossHitDirectionId.Front);

            Assert.That(hitPlan.TargetState, Is.EqualTo(BossStateId.HitStagger));
            Assert.That(hitPlan.Animation, Is.EqualTo(BossReactionAnimationId.HitStagger));
            Assert.That(hitPlan.Direction, Is.EqualTo(BossHitDirectionId.Left));
            Assert.That(hitPlan.RequiresHitStaggerConfig, Is.True);
            Assert.That(hitPlan.RequiresKnockdownConfig, Is.False);
            Assert.That(perfectGuardPlan.TargetState, Is.EqualTo(BossStateId.HitStagger));
            Assert.That(perfectGuardPlan.Animation, Is.EqualTo(BossReactionAnimationId.PerfectGuardStagger));
        }

        [Test]
        public void BossReactionSystem_CreateExecutionPlan_MapsKnockdownToResetRuntime()
        {
            BossReactionExecutionPlan plan = BossReactionSystem.CreateExecutionPlan(
                BossReactionDecision.Interrupt(BossReactionDecisionId.Knockdown),
                BossHitDirectionId.Right);

            Assert.That(plan.HasExecution, Is.True);
            Assert.That(plan.StopRuntime, Is.True);
            Assert.That(plan.TargetState, Is.EqualTo(BossStateId.Knockdown));
            Assert.That(plan.Animation, Is.EqualTo(BossReactionAnimationId.KnockdownStart));
            Assert.That(plan.Direction, Is.EqualTo(BossHitDirectionId.Right));
            Assert.That(plan.ResetKnockdownRuntime, Is.True);
            Assert.That(plan.RequiresHitStaggerConfig, Is.False);
            Assert.That(plan.RequiresKnockdownConfig, Is.True);
        }

        [Test]
        public void BossReactionSystem_EvaluateKnockdownTick_ReturnsNoneBeforeStageThresholds()
        {
            BossKnockdownTickPlan plan = BossReactionSystem.EvaluateKnockdownTick(
                new BossKnockdownTickInput(
                    0.1f,
                    0.2f,
                    0.4f,
                    0.6f,
                    false,
                    false));

            Assert.That(plan.PlayLoopAnimation, Is.False);
            Assert.That(plan.PlayEndAnimation, Is.False);
            Assert.That(plan.StateCommit, Is.EqualTo(BossKnockdownStateCommitId.None));
            Assert.That(plan.TransitionToIdle, Is.False);
        }

        [Test]
        public void BossReactionSystem_EvaluateKnockdownTick_PlaysLoopAndEndOnce()
        {
            BossKnockdownTickPlan loopPlan = BossReactionSystem.EvaluateKnockdownTick(
                new BossKnockdownTickInput(
                    0.25f,
                    0.2f,
                    0.4f,
                    0.6f,
                    false,
                    false));
            BossKnockdownTickPlan endPlan = BossReactionSystem.EvaluateKnockdownTick(
                new BossKnockdownTickInput(
                    0.45f,
                    0.2f,
                    0.4f,
                    0.6f,
                    true,
                    false));

            Assert.That(loopPlan.PlayLoopAnimation, Is.True);
            Assert.That(loopPlan.MarkLoopPlayed, Is.True);
            Assert.That(loopPlan.PlayEndAnimation, Is.False);
            Assert.That(endPlan.PlayLoopAnimation, Is.False);
            Assert.That(endPlan.PlayEndAnimation, Is.True);
            Assert.That(endPlan.MarkEndPlayed, Is.True);
        }

        [Test]
        public void BossReactionSystem_EvaluateKnockdownTick_PreservesSameFrameLoopEndAndReturn()
        {
            BossKnockdownTickPlan plan = BossReactionSystem.EvaluateKnockdownTick(
                new BossKnockdownTickInput(
                    0.7f,
                    0.2f,
                    0.4f,
                    0.6f,
                    false,
                    false));

            Assert.That(plan.PlayLoopAnimation, Is.True);
            Assert.That(plan.PlayEndAnimation, Is.True);
            Assert.That(plan.StateCommit, Is.EqualTo(BossKnockdownStateCommitId.TransitionToIdle));
            Assert.That(plan.TransitionToIdle, Is.True);
        }

        [Test]
        public void BossActor_ReceivePlayerHit_UsesReactionDecisionAndEntersKnockdown()
        {
            GameObject boss = CreateCompleteBoss("BossReactionActorSkillBoss");
            BossReactionTimelineAsset knockdownTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out knockdownTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.Knockdown),
                    new CombatHitResult(CombatHitOutcome.Knockdown, 10f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Knockdown));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
                Assert.That(controller.LastPlayerHitBlockedByBossGate, Is.False);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo(string.Empty));
            }
            finally
            {
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_TickKnockdown_ConsumesPlanAndReturnsIdleAfterTimeline()
        {
            GameObject boss = CreateCompleteBoss("BossReactionActorKnockdownTickBoss");
            BossReactionTimelineAsset knockdownTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out knockdownTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Knockdown);
                TickActorStateMachine(controller, 0.7f);

                InvokeTickKnockdown(controller);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Strafe));
            }
            finally
            {
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_KeepsAttackForLightHeavyAttackHit()
        {
            GameObject boss = CreateCompleteBoss("BossReactionControllerLightBoss");
            try
            {
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Action);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(controller.LastPlayerHitInterrupted, Is.False);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("BossReactionGateClosed"));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_DamageOnlyDoesNotEnterReaction()
        {
            GameObject boss = CreateCompleteBoss("BossReactionDamageOnlyBoss");
            try
            {
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Action);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.DamageOnly),
                    new CombatHitResult(CombatHitOutcome.DamageOnly, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(controller.LastPlayerHitInterrupted, Is.False);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("IgnoredOutcome:DamageOnly"));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_LightAttackInterruptsAttackWhenAllowGateOpen()
        {
            GameObject target = CreatePlayerTarget("BossReactionAllowGateTarget", new Vector3(0f, 0f, 3f));
            GameObject boss = CreateCompleteBoss("BossReactionAllowGateBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossAttackTimelineAsset attackTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out hitStaggerTimeline);
                attackTimeline = CreateBossAttackTimelineWithGate(
                    "Test_AllowGateAttack",
                    CreateBossGate("BossGate_Allow", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, CombatAttackType.LightAttack));
                BossActor controller = boss.GetComponent<BossActor>();
                profile = CreateMotionProfile(CreateAttackMotionConfig(attackTimeline.ActionId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                GetActionRunner(controller).StartAction(
                    attackTimeline.ToBossAttackDefinition(),
                    BossActionKind.Attack,
                    target.transform);
                InvokeCommitStateTransition(controller, BossStateId.Action);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_SkillKnockdownBlockedByBossGate()
        {
            GameObject target = CreatePlayerTarget("BossReactionBlockGateTarget", new Vector3(0f, 0f, 3f));
            GameObject boss = CreateCompleteBoss("BossReactionBlockGateBoss");
            BossReactionTimelineAsset knockdownTimeline = null;
            BossAttackTimelineAsset attackTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out knockdownTimeline);
                attackTimeline = CreateBossAttackTimelineWithGate(
                    "Test_BlockGateAttack",
                    CreateBossGate("BossGate_Allow", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, CombatAttackType.SkillAttack),
                    CreateBossGate("BossGate_BlockSkillKnockdown", BossReactionGatePolicy.BlockInterrupt, BossReactionGateBlockedOutcome.Knockdown, CombatAttackType.SkillAttack));
                BossActor controller = boss.GetComponent<BossActor>();
                profile = CreateMotionProfile(CreateAttackMotionConfig(attackTimeline.ActionId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                GetActionRunner(controller).StartAction(
                    attackTimeline.ToBossAttackDefinition(),
                    BossActionKind.Attack,
                    target.transform);
                InvokeCommitStateTransition(controller, BossStateId.Action);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.Knockdown),
                    new CombatHitResult(CombatHitOutcome.Knockdown, 10f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(controller.LastPlayerHitInterrupted, Is.False);
                Assert.That(controller.LastPlayerHitBlockedByBossGate, Is.True);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("BlockedByBossReactionGate"));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_ExecutesHitStaggerReactionForNonAttackHit()
        {
            GameObject boss = CreateCompleteBoss("BossReactionControllerHitStaggerBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out hitStaggerTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo(string.Empty));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_RefreshesElapsedForSameHitStaggerState()
        {
            GameObject boss = CreateCompleteBoss("BossReactionRefreshHitStaggerBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    string.Empty,
                    string.Empty,
                    0.5f,
                    out hitStaggerTimeline,
                    CreateBossGate("BossGate_AllowLight", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, CombatAttackType.LightAttack));
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.HitStagger);
                TickActorStateMachine(controller, 0.25f);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.StateElapsed, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerSkillHit_RefreshesElapsedWithoutUsingNormalHitBudget()
        {
            GameObject boss = CreateCompleteBoss("BossReactionRefreshSkillHitStaggerBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    string.Empty,
                    string.Empty,
                    1.2f,
                    out hitStaggerTimeline,
                    CreateBossGate(
                        "BossGate_AllowSkill",
                        BossReactionGatePolicy.AllowInterrupt,
                        BossReactionGateBlockedOutcome.Any,
                        CombatAttackType.SkillAttack));
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.HitStagger);
                TickActorStateMachine(controller, 0.8f);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.StateElapsed, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo(string.Empty));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_StopsRefreshingHitStaggerAfterPressureBudget()
        {
            GameObject boss = CreateCompleteBoss("BossReactionPressureLimitBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    string.Empty,
                    string.Empty,
                    0.5f,
                    out hitStaggerTimeline,
                    CreateBossGate("BossGate_AllowLight", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, CombatAttackType.LightAttack));
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.HitStagger);
                TickActorStateMachine(controller, 0.1f);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);

                TickActorStateMachine(controller, 0.1f);
                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                float elapsedBeforeLimitHit = controller.StateElapsed;
                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.LastPlayerHitInterrupted, Is.False);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("ReactionPressureLimitReached"));
                Assert.That(controller.StateElapsed, Is.EqualTo(elapsedBeforeLimitHit).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_HitStaggerReaction_TicksConfiguredCodeMoveWrap()
        {
            GameObject target = CreatePlayerTarget("BossReactionHitStaggerMoveTarget", new Vector3(0f, 0f, 3f));
            GameObject boss = CreateCompleteBoss("BossReactionHitStaggerMoveBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    "Boss_HitStagger",
                    "HitStaggerWrap",
                    out hitStaggerTimeline);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_HitStagger", "HitStaggerWrap", 0.75f, 0.6f));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);
                Vector3 before = boss.transform.position;

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);
                controller.TickActor(0.25f);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(boss.transform.position.z, Is.LessThan(before.z));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_KnockdownReaction_TicksConfiguredCodeMoveWrap()
        {
            GameObject target = CreatePlayerTarget("BossReactionKnockdownMoveTarget", new Vector3(0f, 0f, 3f));
            GameObject boss = CreateCompleteBoss("BossReactionKnockdownMoveBoss");
            BossReactionTimelineAsset knockdownTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    "Boss_Knockdown",
                    "KnockdownWrap",
                    1.583f,
                    out knockdownTimeline);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Boss_Knockdown", "KnockdownWrap", 1.583f, 2.2f));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);
                Vector3 before = boss.transform.position;

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.Knockdown),
                    new CombatHitResult(CombatHitOutcome.Knockdown, 10f, 0f),
                    null);
                Assert.That(knockdownTimeline.ToReactionConfig().HasBossCodeMove, Is.True);
                Assert.That(GetMovementSystem(controller).CurrentLayer, Is.EqualTo(BossMovementLayer.Reaction));
                controller.TickActor(0.25f);
                controller.TickActor(0.5f);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Knockdown));
                Assert.That(GetMovementSystem(controller).CurrentLayer, Is.EqualTo(BossMovementLayer.Reaction));
                Assert.That(boss.transform.position.z, Is.LessThan(before.z - 0.0002f));
            }
            finally
            {
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReactionCodeMoveMissingMotionConfig_StillEntersReactionState()
        {
            GameObject target = CreatePlayerTarget("BossReactionMissingMotionTarget", new Vector3(0f, 0f, 3f));
            GameObject boss = CreateCompleteBoss("BossReactionMissingMotionBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    "Boss_HitStagger",
                    "HitStaggerWrap",
                    out hitStaggerTimeline);
                profile = CreateMotionProfile(CreateReactionMotionConfig("Other_Reaction", "OtherWrap", 0.75f, 0.6f));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);

                LogAssert.Expect(LogType.Error, "BossMotionWarpProfile missing config for action 'Boss_HitStagger'. Boss motion is disabled for this action.");
                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_TickKnockdown_UsesRuntimeConfigResolver()
        {
            GameObject boss = CreateCompleteBoss("BossReactionActorResolverKnockdownBoss");
            BossReactionTimelineAsset knockdownTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out knockdownTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Knockdown);
                TickActorStateMachine(controller, 0.7f);

                InvokeTickKnockdown(controller);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Strafe));
            }
            finally
            {
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ReceivePlayerHit_UsesRuntimeConfigResolverForReactionConfig()
        {
            GameObject boss = CreateCompleteBoss("BossReactionActorResolverHitStaggerBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out hitStaggerTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Idle);

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_PerfectGuardReaction_UsesReactionDecision()
        {
            GameObject boss = CreateCompleteBoss("BossReactionActorPerfectGuardBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out hitStaggerTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                InvokeCommitStateTransition(controller, BossStateId.Action);

                CombatHitNodeData hitNode = new CombatHitNodeData(new CombatHitNodeClip
                {
                    Name = "PerfectGuard_Stagger",
                    StartTime = 0f,
                    EndTime = 0.2f,
                    AttackType = CombatAttackType.LightAttack,
                    TriggersPerfectGuardBossStagger = true
                }, 1);
                controller.ResolvePerfectGuardAgainstBoss(hitNode);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.LastReceivedPlayerHitOutcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
                Assert.That(controller.LastPlayerHitInterrupted, Is.True);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("PerfectGuardBossStagger"));
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(19));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_TwentiethPerfectGuardImmediatelyEntersShieldBreakStunAndUsesSingleTimeline()
        {
            GameObject boss = CreateCompleteBoss("BossShieldBreakStunBoss");
            GameObject target = CreatePlayerTarget("BossShieldBreakStunTarget", new Vector3(0f, 0f, 3f));
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossAttackTimelineAsset attackTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossReactionTimelineWithCanReturn(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    1f,
                    0.8f,
                    out hitStaggerTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                attackTimeline = CreateBossAttackTimelineWithGate("ShieldBreak_NonFinalComboHit");
                profile = CreateMotionProfile(CreateAttackMotionConfig(attackTimeline.ActionId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                Assert.That(
                    GetActionRunner(controller).StartAction(
                        attackTimeline.ToBossAttackDefinition(),
                        BossActionKind.Attack,
                        target.transform).Succeeded,
                    Is.True);
                InvokeCommitStateTransition(controller, BossStateId.Action);

                BossShieldBreakStunVfxController vfx = BindShieldBreakStunVfx(controller, boss);
                CombatHitNodeData hitNode = new CombatHitNodeData(new CombatHitNodeClip
                {
                    Name = "ShieldOnlyPerfectGuard",
                    StartTime = 0f,
                    EndTime = 0.2f,
                    AttackType = CombatAttackType.LightAttack,
                    TriggersPerfectGuardBossStagger = false
                }, 1);

                for (int i = 0; i < 19; i++)
                {
                    controller.ResolvePerfectGuardAgainstBoss(hitNode);
                }

                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(1));
                Assert.That(controller.CurrentState, Is.Not.EqualTo(BossStateId.ShieldBreakStun));

                controller.ResolvePerfectGuardAgainstBoss(hitNode);
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(0));
                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.ShieldBreakStun));
                Assert.That(controller.StateElapsed, Is.EqualTo(0f).Within(0.0001f));
                Assert.That(GetActionRunner(controller).IsRunning, Is.False);
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("BossShieldDefenseBroken"));
                Assert.That(vfx.IsStunPlaying, Is.True);
                Assert.That(ReadShieldBreakWeakAnimationPlayed(controller), Is.False);

                TickActorStateMachine(controller, 0.25f);
                float stunElapsedBeforeHit = controller.StateElapsed;
                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                    new CombatHitResult(CombatHitOutcome.HitReaction, 5f, 0f),
                    null);
                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.ShieldBreakStun));
                Assert.That(controller.StateElapsed, Is.EqualTo(stunElapsedBeforeHit).Within(0.0001f));
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("ShieldBreakStunKeepsCurrentState"));

                TickActorStateMachine(controller, 0.54f);
                InvokeTickShieldBreakStun(controller);
                Assert.That(controller.StateElapsed, Is.EqualTo(0.79f).Within(0.0001f));
                Assert.That(ReadShieldBreakWeakAnimationPlayed(controller), Is.False);

                TickActorStateMachine(controller, 0.011f);
                InvokeTickShieldBreakStun(controller);
                Assert.That(ReadShieldBreakWeakAnimationPlayed(controller), Is.True);

                TickActorStateMachine(controller, 4.5f);
                InvokeTickShieldBreakStun(controller);
                Assert.That(vfx.IsRecoveryWarningPlaying, Is.True);

                TickActorStateMachine(controller, 0.7f);
                InvokeTickShieldBreakStun(controller);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Strafe));
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(20));
                Assert.That(vfx.IsStunPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(target);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_ShieldBreakStunSkillKnockdownKeepsShieldEmptyUntilKnockdownExit()
        {
            GameObject boss = CreateCompleteBoss("BossShieldBreakSkillKnockdownBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossReactionTimelineAsset knockdownTimeline = null;
            try
            {
                RegisterBossReactionTimeline(
                    "Boss_HitStagger",
                    CombatTimelineReactionType.HitReaction,
                    out hitStaggerTimeline);
                RegisterBossReactionTimeline(
                    "Boss_Knockdown",
                    CombatTimelineReactionType.Knockdown,
                    out knockdownTimeline);
                RegisterTestTimeline(hitStaggerTimeline);
                BossActor controller = boss.GetComponent<BossActor>();
                CombatHitNodeData hitNode = new CombatHitNodeData(new CombatHitNodeClip
                {
                    Name = "ShieldBreakPerfectGuard",
                    StartTime = 0f,
                    EndTime = 0.2f,
                    AttackType = CombatAttackType.LightAttack
                }, 1);
                for (int i = 0; i < controller.MaxShieldDefense; i++)
                {
                    controller.ResolvePerfectGuardAgainstBoss(hitNode);
                }

                controller.ReceivePlayerHit(
                    CreatePlayerHit(CombatAttackType.SkillAttack, CombatReactionIntent.Knockdown),
                    new CombatHitResult(CombatHitOutcome.Knockdown, 5f, 0f),
                    null);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Knockdown));
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(0));

                InvokeCommitStateTransition(controller, BossStateId.Strafe);
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(20));
            }
            finally
            {
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        /// <summary>调用长眩晕 Tick，验证 Weak 动画衔接、恢复预警与自然结束共用同一计时。</summary>
        /// <param name="controller">当前处于 ShieldBreakStun 的 BossActor。</param>
        private static void InvokeTickShieldBreakStun(BossActor controller)
        {
            MethodInfo method = typeof(BossActor).GetMethod(
                "TickShieldBreakStun",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(controller, null);
        }

        /// <summary>读取长眩晕 Weak 动画的一次性切换标记。</summary>
        /// <param name="controller">要读取运行时标记的 BossActor。</param>
        /// <returns>true 表示本次眩晕已经切换到 Weak 动画；false 表示仍处于 JustParry 引导动画。</returns>
        private static bool ReadShieldBreakWeakAnimationPlayed(BossActor controller)
        {
            FieldInfo field = typeof(BossActor).GetField(
                "shieldBreakWeakAnimationPlayed",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (bool)field.GetValue(controller);
        }

        /// <summary>为 BossActor 安装最小三阶段护盾击破视觉测试夹具。</summary>
        /// <param name="controller">要绑定视觉控制器的 BossActor。</param>
        /// <param name="boss">承载视觉根的 Boss GameObject。</param>
        /// <returns>已绑定三个阶段根且处于停止状态的视觉控制器。</returns>
        private static BossShieldBreakStunVfxController BindShieldBreakStunVfx(
            BossActor controller,
            GameObject boss)
        {
            BossShieldBreakStunVfxController vfx = boss.AddComponent<BossShieldBreakStunVfxController>();
            GameObject breakRoot = new GameObject("BreakBurst");
            GameObject loopRoot = new GameObject("StunLoop");
            GameObject warningRoot = new GameObject("RecoveryWarning");
            breakRoot.transform.SetParent(boss.transform, false);
            loopRoot.transform.SetParent(boss.transform, false);
            warningRoot.transform.SetParent(boss.transform, false);
            vfx.BindEffectRoots(breakRoot, loopRoot, warningRoot);
            typeof(BossActor)
                .GetField("shieldBreakStunVfx", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, vfx);
            return vfx;
        }

        private static GameObject CreateCompleteBoss(string name)
        {
            GameObject boss = new GameObject(name);
            boss.AddComponent<CharacterController>();
            boss.AddComponent<BossAttackExecutor>();
            boss.AddComponent<BossAnimationBridge>();
            boss.AddComponent<BossMotionController>();
            boss.AddComponent<BossActor>();
            boss.AddComponent<BossCombatReceiver>();
            return boss;
        }

        private static GameObject CreatePlayerTarget(string name, Vector3 position)
        {
            GameObject target = new GameObject(name);
            target.transform.position = position;
            target.AddComponent<PlayerCombatReceiver>();
            return target;
        }

        private static CombatHitData CreatePlayerHit(CombatAttackType attackType, CombatReactionIntent reactionIntent)
        {
            return new CombatHitData
            {
                AttackerTeam = CombatTeam.Player,
                AttackType = attackType,
                ReactionIntent = reactionIntent,
                Damage = 10f,
                HitDirection = Vector3.forward
            };
        }

        private static void InvokeCommitStateTransition(BossActor controller, BossStateId state)
        {
            typeof(BossActor)
                .GetMethod("CommitStateTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { state, false });
        }

        private static void InvokeTickKnockdown(BossActor controller)
        {
            typeof(BossActor)
                .GetMethod("TickKnockdown", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, null);
        }

        private static BossMovementSystem GetMovementSystem(BossActor controller)
        {
            return (BossMovementSystem)typeof(BossActor)
                .GetField("movementSystem", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
        }

        private static BossActionRunner GetActionRunner(BossActor controller)
        {
            return (BossActionRunner)typeof(BossActor)
                .GetField("actionRunner", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
        }

        private static void TickActorStateMachine(BossActor controller, float deltaTime)
        {
            BossStateMachine stateMachine = (BossStateMachine)typeof(BossActor)
                .GetField("stateMachine", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
            stateMachine.Tick(deltaTime);
        }

        private static void RegisterBossAttackTimeline(string attackId, out BossAttackTimelineAsset timeline)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossAttack,
                attackId,
                attackId,
                "Anim_" + attackId,
                1f,
                60f);
            timeline.ConfigureBossAttack(
                90f,
                0.1f,
                1f,
                0.1f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[] { CreateAttackHitNode(attackId) }
                }
            });
            RegisterTestTimeline(timeline);
        }

        private static BossAttackTimelineAsset CreateBossAttackTimelineWithGate(
            string attackId,
            params BossReactionGateWindowClip[] gates)
        {
            BossAttackTimelineAsset timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossAttack,
                attackId,
                attackId,
                "Anim_" + attackId,
                1f,
                60f);
            timeline.ConfigureBossAttack(
                90f,
                0.1f,
                1f,
                0.1f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[] { CreateAttackHitNode(attackId) }
                },
                new CombatTimelineTrack("Boss Gate", CombatTimelineTrackKind.Interrupt)
                {
                    Clips = gates
                }
            });

            return timeline;
        }

        private static CombatHitNodeClip CreateAttackHitNode(string attackId)
        {
            return new CombatHitNodeClip
            {
                Name = attackId + "_Hit",
                CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                StartTime = 0.1f,
                EndTime = 0.2f,
                EffectiveRange = 3f,
                EffectiveAngle = 90f,
                MaxHitsPerTarget = 1
            };
        }

        private static BossReactionGateWindowClip CreateBossGate(
            string name,
            BossReactionGatePolicy policy,
            BossReactionGateBlockedOutcome blockedOutcome,
            params CombatAttackType[] attackTypes)
        {
            return new BossReactionGateWindowClip
            {
                Name = name,
                StartTime = 0f,
                EndTime = 1f,
                Policy = policy,
                BlockedOutcome = blockedOutcome,
                AttackTypes = attackTypes
            };
        }

        private static BossMotionWarpProfile CreateMotionProfile(params BossAttackMotionConfig[] attacks)
        {
            BossMotionWarpProfile profile = ScriptableObject.CreateInstance<BossMotionWarpProfile>();
            typeof(BossMotionWarpProfile)
                .GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(profile, attacks);
            return profile;
        }

        private static void AssignMotionProfile(BossMotionController motionController, BossMotionWarpProfile profile)
        {
            typeof(BossMotionController)
                .GetField("motionWarpProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(motionController, profile);
        }

        private static void RegisterBossReactionTimeline(
            string reactionId,
            CombatTimelineReactionType reactionType,
            out BossReactionTimelineAsset timeline)
        {
            RegisterBossReactionTimeline(
                reactionId,
                reactionType,
                string.Empty,
                string.Empty,
                0.5f,
                out timeline);
        }

        private static void RegisterBossReactionTimeline(
            string reactionId,
            CombatTimelineReactionType reactionType,
            string bossMotionActionId,
            string profileWindowName,
            out BossReactionTimelineAsset timeline)
        {
            RegisterBossReactionTimeline(
                reactionId,
                reactionType,
                bossMotionActionId,
                profileWindowName,
                0.5f,
                out timeline);
        }

        private static void RegisterBossReactionTimeline(
            string reactionId,
            CombatTimelineReactionType reactionType,
            string bossMotionActionId,
            string profileWindowName,
            float totalDuration,
            out BossReactionTimelineAsset timeline,
            params BossReactionGateWindowClip[] gates)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                reactionId,
                reactionId,
                "Anim_" + reactionId,
                totalDuration,
                60f);
            timeline.ConfigureReaction(reactionType);
            List<CombatTimelineTrack> tracks = new List<CombatTimelineTrack>
            {
                new CombatTimelineTrack("Reaction", CombatTimelineTrackKind.Reaction)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatReactionWindowClip
                        {
                            Name = "Stun",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_Stun,
                            StartTime = 0f,
                            EndTime = 0.2f
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "Recovery",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery,
                            StartTime = 0.3f,
                            EndTime = 0.5f
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "CanReturn",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn,
                            StartTime = 0.5f,
                            EndTime = 0.5f
                        }
                    }
                }
            };

            if (gates != null && gates.Length > 0)
            {
                tracks.Add(new CombatTimelineTrack("Boss Gate", CombatTimelineTrackKind.Interrupt)
                {
                    Clips = gates
                });
            }

            if (!string.IsNullOrEmpty(bossMotionActionId))
            {
                tracks.Add(new CombatTimelineTrack("Motion", CombatTimelineTrackKind.BossCodeMove)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatMotionReferenceClip
                        {
                            Name = profileWindowName,
                            MotionKind = CombatTimelineClipKind.BossCodeMove,
                            StartTime = 0f,
                            EndTime = 0.5f,
                            BossAttackId = bossMotionActionId,
                            ProfileWindowName = profileWindowName
                        }
                    }
                });
            }

            timeline.SetTracks(tracks.ToArray());

            RegisterTestTimeline(timeline);
        }

        /// <summary>注册带指定 CanReturn 起点的临时 Boss Reaction Timeline。</summary>
        /// <param name="reactionId">运行时查询使用的 Reaction ID。</param>
        /// <param name="reactionType">临时 Timeline 的反应类型。</param>
        /// <param name="totalDuration">Timeline 总时长，单位秒。</param>
        /// <param name="canReturnTime">Reaction_CanReturn 窗口起点，单位秒。</param>
        /// <param name="timeline">写回已注册的临时 Timeline 资产。</param>
        private static void RegisterBossReactionTimelineWithCanReturn(
            string reactionId,
            CombatTimelineReactionType reactionType,
            float totalDuration,
            float canReturnTime,
            out BossReactionTimelineAsset timeline)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                reactionId,
                reactionId,
                "Anim_" + reactionId,
                totalDuration,
                60f);
            timeline.ConfigureReaction(reactionType);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Reaction", CombatTimelineTrackKind.Reaction)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatReactionWindowClip
                        {
                            Name = "Stun",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_Stun,
                            StartTime = 0f,
                            EndTime = Mathf.Min(0.2f, totalDuration)
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "Recovery",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery,
                            StartTime = Mathf.Min(0.3f, canReturnTime),
                            EndTime = totalDuration
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "CanReturn",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn,
                            StartTime = canReturnTime,
                            EndTime = totalDuration
                        }
                    }
                }
            });
            RegisterTestTimeline(timeline);
        }

        private static BossAttackMotionConfig CreateReactionMotionConfig(
            string actionId,
            string windowName,
            float duration,
            float distance)
        {
            return new BossAttackMotionConfig(
                actionId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                new[]
                {
                    BossCodeMoveWindow.CurveBack(
                        windowName,
                        0f,
                        duration,
                        distance)
                });
        }

        private static BossAttackMotionConfig CreateAttackMotionConfig(string actionId)
        {
            return new BossAttackMotionConfig(
                actionId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                null);
        }

        private static void RegisterTestTimeline(CombatTimelineActionAsset timeline)
        {
            Dictionary<string, CombatTimelineActionAsset> timelines =
                (Dictionary<string, CombatTimelineActionAsset>)typeof(CombatTimelineProvider)
                    .GetField("timelineById", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            timelines[timeline.ActionId] = timeline;
            typeof(CombatTimelineProvider)
                .GetField("initialized", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
        }

        private static void ClearRegisteredTimelines()
        {
            CombatTimelineProvider.ResetCache();
            typeof(CombatTimelineProvider)
                .GetField("initialized", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
        }
    }
}
