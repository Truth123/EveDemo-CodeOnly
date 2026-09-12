// 文件说明：验证 BossActor 正式入口、BossBlackboard、Brain 和 Selector 的收敛后基线。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.Actions;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Brain;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Config;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Boss.State;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.TestTools;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossActorRuntimeTests
    {
        private static readonly BossActionSelectionRandom01 SelectHighestCandidate = () => 0f;

        [Test]
        public void BossBlackboard_Empty_UsesStableDefaultFacts()
        {
            BossBlackboard blackboard = BossBlackboard.Empty;

            Assert.That(blackboard.CurrentState, Is.EqualTo(BossStateId.None));
            Assert.That(blackboard.IsRunning, Is.False);
            Assert.That(blackboard.HasTarget, Is.False);
            Assert.That(blackboard.DistanceToTarget, Is.EqualTo(float.MaxValue));
            Assert.That(blackboard.AngleToTarget, Is.EqualTo(180f));
            Assert.That(blackboard.LastSelectedActionId, Is.EqualTo(string.Empty));
            Assert.That(blackboard.RepeatedSelectedActionCount, Is.EqualTo(0));
            Assert.That(blackboard.IsActionExecuting, Is.False);
            Assert.That(blackboard.CurrentActionId, Is.EqualTo(string.Empty));
            Assert.That(blackboard.CurrentAttackInstanceId, Is.EqualTo(0));
        }

        [Test]
        public void BossActor_Blackboard_ExportsTargetAndStateFacts()
        {
            GameObject boss = CreateCompleteBoss("BossActorBlackboardFactsBoss");
            GameObject target = CreatePlayerTarget("BossActorBlackboardFactsTarget", new Vector3(3f, 0f, 4f));
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Idle);

                BossBlackboard blackboard = actor.Blackboard;

                Assert.That(blackboard.CurrentState, Is.EqualTo(BossStateId.Idle));
                Assert.That(blackboard.IsRunning, Is.True);
                Assert.That(blackboard.HasTarget, Is.True);
                Assert.That(blackboard.DistanceToTarget, Is.EqualTo(5f).Within(0.001f));
                Assert.That(blackboard.AngleToTarget, Is.EqualTo(Mathf.Atan2(3f, 4f) * Mathf.Rad2Deg).Within(0.01f));
            }
            finally
            {
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_Blackboard_ExportsActionExecutionFacts()
        {
            GameObject boss = CreateCompleteBoss("BossActorBlackboardActionBoss");
            GameObject target = CreatePlayerTarget("BossActorBlackboardActionTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BossAttackDefinition attack = CreateTestAttackDefinition("Blackboard_Action", out timeline);
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActionRunner runner = GetActionRunner(actor);
                runner.StartAction(attack, BossActionKind.Attack, target.transform);

                BossBlackboard blackboard = actor.Blackboard;

                Assert.That(blackboard.IsActionExecuting, Is.True);
                Assert.That(blackboard.CurrentActionId, Is.EqualTo("Blackboard_Action"));
                Assert.That(blackboard.CurrentActionKind, Is.EqualTo(BossActionKind.Attack));
                Assert.That(blackboard.CurrentAttackInstanceId, Is.EqualTo(1));
                Assert.That(blackboard.CurrentActionElapsed, Is.EqualTo(0f));
                Assert.That(blackboard.CurrentHitNodeId, Is.EqualTo(string.Empty));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [TestCase(BossStateId.Idle, true)]
        [TestCase(BossStateId.Approach, true)]
        [TestCase(BossStateId.Strafe, true)]
        [TestCase(BossStateId.Action, false)]
        [TestCase(BossStateId.Recovery, false)]
        [TestCase(BossStateId.HitStagger, false)]
        [TestCase(BossStateId.Knockdown, false)]
        public void BossActor_TickActor_RotatesOnlyInFacingAllowedStates(BossStateId state, bool shouldRotate)
        {
            GameObject boss = CreateCompleteBoss("BossActorFacingGateBoss");
            GameObject target = CreatePlayerTarget("BossActorFacingGateTarget", new Vector3(4f, 0f, 0f));
            BossReactionTimelineAsset knockdownTimeline = null;
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, state);
                boss.transform.rotation = Quaternion.identity;
                if (state == BossStateId.Knockdown)
                {
                    RegisterBossKnockdownTimeline(out knockdownTimeline);
                }

                actor.TickActor(0.1f);

                float rotationDelta = Quaternion.Angle(Quaternion.identity, boss.transform.rotation);
                if (shouldRotate)
                {
                    Assert.That(rotationDelta, Is.GreaterThan(0.1f));
                }
                else
                {
                    Assert.That(rotationDelta, Is.LessThan(0.1f));
                }
            }
            finally
            {
                if (knockdownTimeline != null)
                {
                    CombatTimelineProvider.ResetCache();
                }

                Object.DestroyImmediate(knockdownTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Evaluate_ReturnsNoneForEmptyBlackboard()
        {
            Assert.That(BossBrain.Evaluate(BossBlackboard.Empty), Is.EqualTo(BossBrainIntentId.None));
        }

        [Test]
        public void BossBrain_Evaluate_ReturnsAttackForThinkingWithTarget()
        {
            BossBlackboard blackboard = CreateBrainBlackboard(BossStateId.Idle, true, false);

            Assert.That(BossBrain.Evaluate(blackboard), Is.EqualTo(BossBrainIntentId.Attack));
        }

        [Test]
        public void BossBrain_Evaluate_ReturnsContinueActionWhenActionExecuting()
        {
            BossBlackboard blackboard = CreateBrainBlackboard(BossStateId.Idle, true, true);

            Assert.That(BossBrain.Evaluate(blackboard), Is.EqualTo(BossBrainIntentId.ContinueAction));
        }

        [Test]
        public void BossBrain_Evaluate_ReturnsRepositionForMovementState()
        {
            BossBlackboard blackboard = CreateBrainBlackboard(BossStateId.Approach, true, false);

            Assert.That(BossBrain.Evaluate(blackboard), Is.EqualTo(BossBrainIntentId.Reposition));
        }

        [Test]
        public void BossBrain_Decide_HoldsNeutralForMinimumDurationThenStartsEligibleAttack()
        {
            GameObject boss = CreateCompleteBoss("BossBrainStartAttackMemoryBoss");
            GameObject target = CreatePlayerTarget("BossBrainStartAttackMemoryTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            BossActionSet actionSet = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Brain_Memory_Attack", out timeline);
                RegisterTestTimeline(timeline);
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId));
                actionSet = CreateActionSet(attack.AttackId);

                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBlackboard blackboard = CreateBrainBlackboard(
                    BossStateId.Strafe,
                    true,
                    false,
                    stateElapsed: 0.2f,
                    distanceToTarget: 2f);
                BossBrainDecisionInput input = CreateBrainDecisionInput();

                BossBrainDecision holdingDecision = BossBrain.Decide(blackboard, input, actor, actionSet, 0f);
                Assert.That(holdingDecision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));

                blackboard = CreateBrainBlackboard(
                    BossStateId.Strafe,
                    true,
                    false,
                    stateElapsed: 0.3f,
                    distanceToTarget: 2f);
                BossBrainDecision forcedNeutralDecision = BossBrain.Decide(
                    blackboard,
                    CreateBrainDecisionInput(tempoResetRequired: true),
                    actor,
                    actionSet,
                    0f);
                Assert.That(forcedNeutralDecision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));

                BossBrainDecision decision = BossBrain.Decide(blackboard, input, actor, actionSet, 0f);

                Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.StartAction));
                Assert.That(decision.ActionDefinition.AttackId, Is.EqualTo(attack.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [TestCase(0.49f, BossBrainDecisionId.MoveApproach)]
        [TestCase(0.5f, BossBrainDecisionId.MoveStrafe)]
        public void BossBrain_Decide_MidDistanceNeutralUsesFixedOneToOneChoice(
            float roll,
            BossBrainDecisionId expectedDecision)
        {
            BossBrainDecision decision = BossBrain.Decide(
                CreateBrainBlackboard(
                    BossStateId.Idle,
                    true,
                    false,
                    distanceToTarget: 5f),
                CreateBrainDecisionInput(),
                null,
                null,
                0f,
                () => roll);

            Assert.That(decision.Id, Is.EqualTo(expectedDecision));
        }

        [Test]
        public void BossBrain_Decide_ExactCloseBoundaryUsesStrafe()
        {
            BossBrainDecision decision = BossBrain.Decide(
                CreateBrainBlackboard(
                    BossStateId.Idle,
                    true,
                    false,
                    distanceToTarget: 3.5f),
                CreateBrainDecisionInput(),
                null,
                null,
                0f,
                () => 0f);

            Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
        }

        [Test]
        public void BossBrain_Decide_ReactionCounterSelectsHitEscapeAttackAndRepositionWhenScorePassed()
        {
            GameObject boss = CreateCompleteBoss("BossBrainPressureCounterBoss");
            GameObject target = CreatePlayerTarget("BossBrainPressureCounterTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset rushTimeline = null;
            BossAttackTimelineAsset swordAuraTimeline = null;
            BossAttackTimelineAsset repositionTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition rush = CreateTestAttackDefinition("Brain_Pressure_Rush", out rushTimeline);
                BossAttackDefinition swordAura = CreateTestAttackDefinition("Brain_Pressure_SwordAura", out swordAuraTimeline);
                BossAttackDefinition reposition = CreateTestRepositionDefinition("Brain_Pressure_RapidMoveBack", out repositionTimeline);
                RegisterTestTimeline(rushTimeline);
                RegisterTestTimeline(swordAuraTimeline);
                RegisterTestTimeline(repositionTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateHitEscapeActionSetEntry(rush.AttackId, BossActionKind.Attack),
                    CreateHitEscapeActionSetEntry(swordAura.AttackId, BossActionKind.Attack),
                    CreateHitEscapeActionSetEntry(reposition.AttackId, BossActionKind.Reposition));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(rush.AttackId, 0f, 6f),
                    CreateMotionConfig(swordAura.AttackId, 0f, 6f),
                    CreateMotionConfig(reposition.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBlackboard blackboard = CreateBrainBlackboard(
                    BossStateId.HitStagger,
                    true,
                    false,
                    stateElapsed: 1f,
                    distanceToTarget: 1f,
                    canReturnFromReaction: true);
                BossBrainDecisionInput input = CreateBrainDecisionInput(
                    canUseReactionCounter: true);

                BossBrainDecision firstDecision = BossBrain.Decide(
                    blackboard,
                    input,
                    actor,
                    actionSet,
                    0f,
                    () => 0f);
                BossBrainDecision lastDecision = BossBrain.Decide(
                    blackboard,
                    input,
                    actor,
                    actionSet,
                    0f,
                    () => 0.999f);

                Assert.That(firstDecision.Id, Is.EqualTo(BossBrainDecisionId.StartAction));
                Assert.That(firstDecision.ActionDefinition.AttackId, Is.EqualTo(rush.AttackId));
                Assert.That(firstDecision.ActionKind, Is.EqualTo(BossActionKind.Attack));
                Assert.That(lastDecision.Id, Is.EqualTo(BossBrainDecisionId.StartAction));
                Assert.That(lastDecision.ActionDefinition.AttackId, Is.EqualTo(reposition.AttackId));
                Assert.That(lastDecision.ActionKind, Is.EqualTo(BossActionKind.Reposition));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(rushTimeline);
                Object.DestroyImmediate(swordAuraTimeline);
                Object.DestroyImmediate(repositionTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_TrySelectAction_BlocksHitEscapeOutsideReactionEscapeContext()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorHitEscapeBlockedBoss");
            GameObject target = CreatePlayerTarget("BossSelectorHitEscapeBlockedTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Selector_HitEscape_Blocked", out timeline);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSetFromEntries(CreateHitEscapeActionSetEntry(attack.AttackId, BossActionKind.Attack));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                bool selected = BossActionSelector.TrySelectAction(
                    BossBrainIntentId.Attack,
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 1f),
                    actor,
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(canUseReactionCounter: true),
                    out BossAttackDefinition selectedDefinition,
                    out BossActionKind selectedKind);

                Assert.That(selected, Is.False);
                Assert.That(selectedDefinition, Is.Null);
                Assert.That(selectedKind, Is.EqualTo(BossActionKind.Attack));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_ReactionCounterScoreControlsUnrestrictedAttack()
        {
            GameObject boss = CreateCompleteBoss("BossBrainHitStaggerNormalCounterBoss");
            GameObject target = CreatePlayerTarget("BossBrainHitStaggerNormalCounterTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset attackTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Brain_HitStagger_NormalCounter", out attackTimeline);
                RegisterTestTimeline(attackTimeline);
                actionSet = CreateActionSetFromEntries(CreateActionSetEntry(attack.AttackId));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBlackboard blackboard = CreateBrainBlackboard(
                    BossStateId.HitStagger,
                    true,
                    false,
                    stateElapsed: 1f,
                    distanceToTarget: 1f,
                    canReturnFromReaction: true);
                BossBrainDecision blockedDecision = BossBrain.Decide(
                    blackboard,
                    CreateBrainDecisionInput(
                        canUseReactionCounter: false),
                    actor,
                    actionSet,
                    0f);
                BossBrainDecision counterDecision = BossBrain.Decide(
                    blackboard,
                    CreateBrainDecisionInput(
                        canUseReactionCounter: true),
                    actor,
                    actionSet,
                    0f);

                Assert.That(blockedDecision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
                Assert.That(blockedDecision.ActionDefinition, Is.Null);
                Assert.That(counterDecision.Id, Is.EqualTo(BossBrainDecisionId.StartAction));
                Assert.That(counterDecision.ActionDefinition.AttackId, Is.EqualTo(attack.AttackId));
                Assert.That(counterDecision.ActionKind, Is.EqualTo(BossActionKind.Attack));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_Phase1SkipsPhase2OnlyMoveChain()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorPhaseGateBoss");
            GameObject target = CreatePlayerTarget("BossSelectorPhaseGateTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset moveChainTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Phase_Slash", out slashTimeline, weight: 1f);
                BossAttackDefinition moveChain = CreateTestAttackDefinition("Selector_Phase_MoveChain", out moveChainTimeline, weight: 10f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(moveChainTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntryWithTempo(
                        moveChain.AttackId,
                        BossActionPhaseMask.Phase2 | BossActionPhaseMask.Desperation,
                        45f,
                        55f,
                        20f,
                        true,
                        1,
                        BossActionTags.MultiHitPressure),
                    CreateActionSetEntryWithTempo(
                        slash.AttackId,
                        BossActionPhaseMask.All,
                        12f,
                        100f,
                        0f,
                        false,
                        0,
                        BossActionTags.FastPressure));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(moveChain.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 2f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(currentPhase: BossCombatPhaseId.Phase1),
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(slash.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(moveChainTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        [Ignore("旧版 Timeline 单权重语义已删除；阶段权重由 ActionSet 专项测试覆盖。")]
        public void BossActionSelector_Select_Phase2AllowsLowFrequencyMoveChain()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorMoveChainPhase2Boss");
            GameObject target = CreatePlayerTarget("BossSelectorMoveChainPhase2Target", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset moveChainTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Phase2_Slash", out slashTimeline, weight: 1f);
                BossAttackDefinition moveChain = CreateTestAttackDefinition("Selector_Phase2_MoveChain", out moveChainTimeline, weight: 8f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(moveChainTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntryWithTempo(
                        slash.AttackId,
                        BossActionPhaseMask.All,
                        12f,
                        100f,
                        0f,
                        false,
                        0,
                        BossActionTags.FastPressure),
                    CreateActionSetEntryWithTempo(
                        moveChain.AttackId,
                        BossActionPhaseMask.Phase2 | BossActionPhaseMask.Desperation,
                        45f,
                        55f,
                        20f,
                        true,
                        1,
                        BossActionTags.MultiHitPressure));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(moveChain.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 2f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(currentPhase: BossCombatPhaseId.Phase2),
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(moveChain.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(moveChainTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_HighPressureBlocksLongAction()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorPressureGateBoss");
            GameObject target = CreatePlayerTarget("BossSelectorPressureGateTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset longTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Pressure_Slash", out slashTimeline, weight: 1f);
                BossAttackDefinition longAction = CreateTestAttackDefinition("Selector_Pressure_Long", out longTimeline, weight: 10f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(longTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntryWithTempo(
                        longAction.AttackId,
                        BossActionPhaseMask.All,
                        45f,
                        55f,
                        20f,
                        true,
                        1,
                        BossActionTags.MultiHitPressure),
                    CreateActionSetEntryWithTempo(
                        slash.AttackId,
                        BossActionPhaseMask.All,
                        12f,
                        100f,
                        0f,
                        false,
                        0,
                        BossActionTags.FastPressure));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(longAction.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 2f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(
                        currentPhase: BossCombatPhaseId.Phase2,
                        tempoPressure: 80f),
                    SelectHighestCandidate);

                Assert.That(selected, Is.Null);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(longTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_DecayPressureRejectsLowRiskAttack()
        {
            GameObject boss = CreateCompleteBoss("BossBrainDecayLowRiskBoss");
            GameObject target = CreatePlayerTarget("BossBrainDecayLowRiskTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Brain_Decay_LowRisk", out timeline);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSetFromEntries(CreateActionSetEntryWithTempo(
                    attack.AttackId,
                    BossActionPhaseMask.All,
                    5f,
                    0f,
                    0f,
                    false,
                    0));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBrainDecision decision = BossBrain.Decide(
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 2f),
                    CreateBrainDecisionInput(
                        tempoPressure: 62f,
                        pressureDecayMode: true),
                    actor,
                    actionSet,
                    0f);

                Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_DecaySelectsHitEscapeRepositionOutsideHitStaggerAndRejectsAttack()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorDecayRepositionBoss");
            GameObject target = CreatePlayerTarget("BossSelectorDecayRepositionTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset attackTimeline = null;
            BossAttackTimelineAsset repositionTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Selector_Decay_HitEscapeAttack", out attackTimeline);
                BossAttackDefinition reposition = CreateTestRepositionDefinition(
                    "Selector_Decay_RapidMoveBack",
                    out repositionTimeline);
                RegisterTestTimeline(attackTimeline);
                RegisterTestTimeline(repositionTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateHitEscapeActionSetEntry(attack.AttackId, BossActionKind.Attack),
                    CreateHitEscapeActionSetEntry(reposition.AttackId, BossActionKind.Reposition));
                typeof(BossActionSet)
                    .GetField("poolPolicies", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(actionSet, new[]
                    {
                        new BossActionPoolPolicy
                        {
                            Pool = BossActionPoolId.HitEscape,
                            MaxDistance = 3.5f,
                            RequireHitStaggerContext = true
                        }
                    });
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(attack.AttackId, 0f, 6f),
                    CreateMotionConfig(reposition.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                bool selectedOutsideDecay = BossActionSelector.TrySelectAction(
                    BossBrainIntentId.Attack,
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 1f),
                    actor,
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(tempoPressure: 40f),
                    out BossAttackDefinition definitionOutsideDecay,
                    out _,
                    () => 0f);

                bool selected = BossActionSelector.TrySelectAction(
                    BossBrainIntentId.Attack,
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 1f),
                    actor,
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(
                        tempoPressure: 62f,
                        pressureDecayMode: true),
                    out BossAttackDefinition selectedDefinition,
                    out BossActionKind selectedKind,
                    () => 0f);

                Assert.That(selectedOutsideDecay, Is.False);
                Assert.That(definitionOutsideDecay, Is.Null);
                Assert.That(selected, Is.True);
                Assert.That(selectedDefinition.AttackId, Is.EqualTo(reposition.AttackId));
                Assert.That(selectedKind, Is.EqualTo(BossActionKind.Reposition));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(repositionTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_UsesHysteresisPressureDecayModeFromInput()
        {
            GameObject boss = CreateCompleteBoss("BossBrainDecayHysteresisBoss");
            GameObject target = CreatePlayerTarget("BossBrainDecayHysteresisTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition special = CreateTestAttackDefinition("Brain_Decay_HysteresisSpecial", out timeline);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSetFromEntries(new BossActionSetEntry(
                    special.AttackId,
                    BossActionKind.Attack,
                    true,
                    System.Array.Empty<string>(),
                    BossActionPhaseMask.All,
                    new BossPhaseWeights(1f, 1f, 1f),
                    BossActionPoolId.Special,
                    5f,
                    BossActionStrength.Critical,
                    BossActionFollowUpPolicy.None,
                    BossPressurePreference.None,
                    BossActionPreferenceFlags.None,
                    0));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(special.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBrainDecision decision = BossBrain.Decide(
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 2f),
                    CreateBrainDecisionInput(
                        tempoPressure: 50f,
                        pressureDecayMode: true),
                    actor,
                    actionSet,
                    0f);

                Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_HardPressureLimitForcesNeutralBeforeActionSelection()
        {
            GameObject boss = CreateCompleteBoss("BossBrainHardLimitBoss");
            GameObject target = CreatePlayerTarget("BossBrainHardLimitTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Brain_HardLimit_Attack", out timeline);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSetFromEntries(CreateActionSetEntry(attack.AttackId));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBrainDecision decision = BossBrain.Decide(
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 2f),
                    CreateBrainDecisionInput(tempoPressure: 70f),
                    actor,
                    actionSet,
                    0f);

                Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_TempoResetRequiredMovesBeforeAttacking()
        {
            BossBrainDecision decision = BossBrain.Decide(
                CreateBrainBlackboard(
                    BossStateId.Idle,
                    true,
                    false,
                    distanceToTarget: 2f),
                CreateBrainDecisionInput(
                    tempoResetRequired: true,
                    tempoPressure: 40f),
                null,
                null,
                0f);

            Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.MoveStrafe));
        }

        [Test]
        public void BossActionSelector_Select_RecentUseLimitBlocksRepeatedLongAction()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorRecentLimitBoss");
            GameObject target = CreatePlayerTarget("BossSelectorRecentLimitTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset longTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Recent_Slash", out slashTimeline, weight: 1f);
                BossAttackDefinition longAction = CreateTestAttackDefinition("Selector_Recent_Long", out longTimeline, weight: 10f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(longTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntryWithTempo(
                        longAction.AttackId,
                        BossActionPhaseMask.All,
                        45f,
                        55f,
                        20f,
                        true,
                        1,
                        BossActionTags.MultiHitPressure),
                    CreateActionSetEntryWithTempo(
                        slash.AttackId,
                        BossActionPhaseMask.All,
                        12f,
                        100f,
                        0f,
                        false,
                        0,
                        BossActionTags.FastPressure));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(longAction.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 2f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    CreateBrainDecisionInput(recentActionIds: new[] { longAction.AttackId }),
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(slash.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(longTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_AllEligibleCandidatesMaySelectFourthCandidate()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorTopThreeBoss");
            GameObject target = CreatePlayerTarget("BossSelectorTopThreeTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset firstTimeline = null;
            BossAttackTimelineAsset secondTimeline = null;
            BossAttackTimelineAsset thirdTimeline = null;
            BossAttackTimelineAsset fourthTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition first = CreateTestAttackDefinition("Selector_Top3_First", out firstTimeline, weight: 4f);
                BossAttackDefinition second = CreateTestAttackDefinition("Selector_Top3_Second", out secondTimeline, weight: 3f);
                BossAttackDefinition third = CreateTestAttackDefinition("Selector_Top3_Third", out thirdTimeline, weight: 2f);
                BossAttackDefinition fourth = CreateTestAttackDefinition("Selector_Top3_Fourth", out fourthTimeline, weight: 1f);
                RegisterTestTimeline(firstTimeline);
                RegisterTestTimeline(secondTimeline);
                RegisterTestTimeline(thirdTimeline);
                RegisterTestTimeline(fourthTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(first.AttackId),
                    CreateActionSetEntry(second.AttackId),
                    CreateActionSetEntry(third.AttackId),
                    CreateActionSetEntry(fourth.AttackId));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(first.AttackId, 0f, 6f),
                    CreateMotionConfig(second.AttackId, 0f, 6f),
                    CreateMotionConfig(third.AttackId, 0f, 6f),
                    CreateMotionConfig(fourth.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 2f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    default,
                    null,
                    false,
                    () => 0.999f);

                Assert.That(selected.AttackId, Is.EqualTo(fourth.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(firstTimeline);
                Object.DestroyImmediate(secondTimeline);
                Object.DestroyImmediate(thirdTimeline);
                Object.DestroyImmediate(fourthTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossBrain_Decide_EligibleActionStartsAfterBrainEntry()
        {
            GameObject boss = CreateCompleteBoss("BossBrainPerfectRewardNoForcedNeutralBoss");
            GameObject target = CreatePlayerTarget("BossBrainPerfectRewardNoForcedNeutralTarget", new Vector3(0f, 0f, 2f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Brain_PerfectReward_NoForcedNeutral", out timeline);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSetFromEntries(CreateActionSetEntry(attack.AttackId));
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(attack.AttackId, 0f, 6f));
                BossActor actor = boss.GetComponent<BossActor>();
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);

                BossBrainDecision decision = BossBrain.Decide(
                    CreateBrainBlackboard(
                        BossStateId.Idle,
                        true,
                        false,
                        distanceToTarget: 2f),
                    CreateBrainDecisionInput(),
                    actor,
                    actionSet,
                    0f);

                Assert.That(decision.Id, Is.EqualTo(BossBrainDecisionId.StartAction));
                Assert.That(decision.ActionDefinition.AttackId, Is.EqualTo(attack.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_ReturnsNullForNonAttackIntent()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorNonAttackBoss");
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Selector_NonAttack", out timeline);
                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Idle,
                    CreateSelectorBlackboard(),
                    boss.GetComponent<BossActor>(),
                    new[] { attack },
                    0f);

                Assert.That(selected, Is.Null);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        [Ignore("BossAttackTimelineAsset 不再保存权重；阶段权重由 ActionSet 负责。")]
        public void BossActionSelector_Select_PrefersHigherWeightEligibleAttack()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorWeightBoss");
            GameObject target = CreatePlayerTarget("BossSelectorWeightTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset lowTimeline = null;
            BossAttackTimelineAsset highTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition low = CreateTestAttackDefinition("Selector_Low", out lowTimeline, weight: 1f);
                BossAttackDefinition high = CreateTestAttackDefinition("Selector_High", out highTimeline, weight: 4f);
                BindActorReferences(boss.GetComponent<BossActor>());
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(low.AttackId),
                    CreateMotionConfig(high.AttackId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(),
                    boss.GetComponent<BossActor>(),
                    new[] { low, high },
                    0f,
                    () => 0.999f);

                Assert.That(selected, Is.SameAs(high));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(lowTimeline);
                Object.DestroyImmediate(highTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_AppliesRepeatPenaltyWithoutMutatingActor()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorRepeatBoss");
            GameObject target = CreatePlayerTarget("BossSelectorRepeatTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset repeatedTimeline = null;
            BossAttackTimelineAsset alternateTimeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition repeated = CreateTestAttackDefinition("Selector_Repeated", out repeatedTimeline, weight: 4f);
                BossAttackDefinition alternate = CreateTestAttackDefinition("Selector_Alternate", out alternateTimeline, weight: 2.5f);
                BindActorReferences(boss.GetComponent<BossActor>());
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(repeated.AttackId),
                    CreateMotionConfig(alternate.AttackId));
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(lastSelectedActionId: repeated.AttackId, repeatedSelectedActionCount: 1),
                    boss.GetComponent<BossActor>(),
                    new[] { repeated, alternate },
                    0f,
                    () => 0.999f);

                Assert.That(selected, Is.SameAs(alternate));
                Assert.That(boss.GetComponent<BossActor>().LastSelectedActionId, Is.EqualTo(string.Empty));
                Assert.That(boss.GetComponent<BossActor>().RepeatedSelectedActionCount, Is.EqualTo(0));
            }
            finally
            {
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(repeatedTimeline);
                Object.DestroyImmediate(alternateTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_UsesFastPressureAtCloseDistance()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorCloseTagBoss");
            GameObject target = CreatePlayerTarget("BossSelectorCloseTagTarget", new Vector3(0f, 0f, 1.5f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset chaseTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Close_Slash", out slashTimeline, weight: 4f);
                BossAttackDefinition chase = CreateTestAttackDefinition("Selector_Close_Chase", out chaseTimeline, weight: 5f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(chaseTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(slash.AttackId, BossActionTags.FastPressure),
                    CreateActionSetEntry(chase.AttackId, BossActionTags.GapCloseCombo));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(chase.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 1.5f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(slash.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(chaseTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        [Ignore("旧版距离到标签的硬编码优先级已删除；现在先选择动作池，再在池内加权。")]
        public void BossActionSelector_Select_UsesDisengageCounterWhenVeryClose()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorDisengageTagBoss");
            GameObject target = CreatePlayerTarget("BossSelectorDisengageTagTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset counterTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Disengage_Slash", out slashTimeline, weight: 4f);
                BossAttackDefinition counter = CreateTestAttackDefinition("Selector_Disengage_Counter", out counterTimeline, weight: 2f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(counterTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(slash.AttackId, BossActionTags.FastPressure),
                    CreateActionSetEntry(counter.AttackId, BossActionTags.DisengageCounter));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(counter.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 1f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(counter.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(counterTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_RequiresPreferredTagWhenRequested()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorPreferredTagBoss");
            GameObject target = CreatePlayerTarget("BossSelectorPreferredTagTarget", new Vector3(0f, 0f, 1f));
            BossAttackTimelineAsset slashTimeline = null;
            BossAttackTimelineAsset counterTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition slash = CreateTestAttackDefinition("Selector_Preferred_Slash", out slashTimeline, weight: 10f);
                BossAttackDefinition counter = CreateTestAttackDefinition("Selector_Preferred_Counter", out counterTimeline, weight: 1f);
                RegisterTestTimeline(slashTimeline);
                RegisterTestTimeline(counterTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(slash.AttackId, BossActionTags.FastPressure),
                    CreateActionSetEntry(counter.AttackId, BossActionTags.DisengageCounter));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(slash.AttackId, 0f, 6f),
                    CreateMotionConfig(counter.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 1f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    new[] { BossActionTags.DisengageCounter },
                    true,
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(counter.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(slashTimeline);
                Object.DestroyImmediate(counterTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        [Ignore("旧版距离到 GapClose 标签的硬编码优先级已删除；现在由动作池策略决定。")]
        public void BossActionSelector_Select_UsesGapCloseComboAtMidDistance()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorGapCloseTagBoss");
            GameObject target = CreatePlayerTarget("BossSelectorGapCloseTagTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset pressureTimeline = null;
            BossAttackTimelineAsset gapTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition pressure = CreateTestAttackDefinition("Selector_Gap_Pressure", out pressureTimeline, weight: 4f);
                BossAttackDefinition gapClose = CreateTestAttackDefinition("Selector_Gap_Chase", out gapTimeline, weight: 2.8f);
                RegisterTestTimeline(pressureTimeline);
                RegisterTestTimeline(gapTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(pressure.AttackId, BossActionTags.FastPressure),
                    CreateActionSetEntry(gapClose.AttackId, BossActionTags.GapCloseCombo));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(pressure.AttackId, 0f, 6f),
                    CreateMotionConfig(gapClose.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(distanceToTarget: 4f),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(gapClose.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(pressureTimeline);
                Object.DestroyImmediate(gapTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActionSelector_Select_PenalizesRepeatedActionTag()
        {
            GameObject boss = CreateCompleteBoss("BossSelectorRepeatedTagBoss");
            GameObject target = CreatePlayerTarget("BossSelectorRepeatedTagTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset pressureTimeline = null;
            BossAttackTimelineAsset gapTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                CombatTimelineProvider.ResetCache();
                BossAttackDefinition pressure = CreateTestAttackDefinition("Selector_RepeatTag_Pressure", out pressureTimeline, weight: 4f);
                BossAttackDefinition gapClose = CreateTestAttackDefinition("Selector_RepeatTag_Chase", out gapTimeline, weight: 2.8f);
                RegisterTestTimeline(pressureTimeline);
                RegisterTestTimeline(gapTimeline);
                actionSet = CreateActionSetFromEntries(
                    CreateActionSetEntry(pressure.AttackId, BossActionTags.FastPressure),
                    CreateActionSetEntry(gapClose.AttackId, BossActionTags.GapCloseCombo));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(pressure.AttackId, 0f, 6f),
                    CreateMotionConfig(gapClose.AttackId, 0f, 6f));
                BindActorReferences(boss.GetComponent<BossActor>());
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);

                BossAttackDefinition selected = BossActionSelector.Select(
                    BossBrainIntentId.Attack,
                    CreateSelectorBlackboard(
                        distanceToTarget: 4f,
                        lastSelectedActionId: gapClose.AttackId,
                        repeatedSelectedActionCount: 0),
                    boss.GetComponent<BossActor>(),
                    actionSet,
                    0f,
                    SelectHighestCandidate);

                Assert.That(selected.AttackId, Is.EqualTo(pressure.AttackId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(pressureTimeline);
                Object.DestroyImmediate(gapTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_TickActor_StartsAttackFromIdleWhenEligible()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsIdleAttackBoss");
            GameObject target = CreatePlayerTarget("BossTacticsIdleAttackTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossAttackTimeline("Tactics_IdleAttack", out timeline);
                actionSet = CreateActionSet("Tactics_IdleAttack");
                profile = CreateMotionProfileForAttacks(CreateMotionConfig("Tactics_IdleAttack"));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Idle);

                actor.TickActor(0.1f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(actor.CurrentActionId, Is.EqualTo("Tactics_IdleAttack"));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_TickActor_StartsAttackFromApproachWhenEligible()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsApproachAttackBoss");
            GameObject target = CreatePlayerTarget("BossTacticsApproachAttackTarget", new Vector3(0f, 0f, 4f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                RegisterBossAttackTimeline("Tactics_ApproachAttack", out timeline);
                actionSet = CreateActionSet("Tactics_ApproachAttack");
                profile = CreateMotionProfileForAttacks(CreateMotionConfig("Tactics_ApproachAttack"));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Approach);
                TickActorState(actor, 0.31f);

                actor.TickActor(0.1f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(actor.CurrentActionId, Is.EqualTo("Tactics_ApproachAttack"));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_StartAttackFromHitStagger_PreservesActionMotionLayer()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsHitStaggerAttackBoss");
            GameObject target = CreatePlayerTarget("BossTacticsHitStaggerAttackTarget", new Vector3(0f, 0f, 3f));
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            BossActionSet actionSet = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition("Tactics_HitStaggerCounter", out timeline);
                profile = CreateMotionProfileForAttacks(CreateMotionConfigWithCodeMove(attack.AttackId));
                actionSet = CreateActionSet(attack.AttackId);

                BossActor actor = boss.GetComponent<BossActor>();
                BossMotionController motionController = boss.GetComponent<BossMotionController>();
                AssignMotionProfile(motionController, profile);
                SetPrivateField(actor, "actionSet", actionSet);
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.HitStagger);
                Vector3 before = boss.transform.position;

                bool started = InvokeStartAction(actor, attack, BossActionKind.Attack);
                actor.TickActor(0.1f);

                BossMovementSystem movement = GetMovementSystem(actor);
                Assert.That(started, Is.True);
                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.Action));
                Assert.That(movement.CurrentActionId, Is.EqualTo(attack.AttackId));
                Assert.That(boss.transform.position.z, Is.GreaterThan(before.z));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_StartAttack_CooldownBeginsAtLastHitNodeEnd()
        {
            const float cooldown = 2f;
            GameObject boss = CreateCompleteBoss("BossAttackCooldownAnchorBoss");
            GameObject target = CreatePlayerTarget("BossAttackCooldownAnchorTarget", new Vector3(0f, 0f, 3f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            try
            {
                BossAttackDefinition attack = CreateTestAttackDefinition(
                    "CooldownAnchor_Attack",
                    out timeline,
                    cooldown: cooldown);
                actionSet = CreateActionSet(attack.AttackId);

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                BindActorReferences(actor);

                LogAssert.Expect(LogType.Error, "BossMotionController requires a BossMotionWarpProfile. Boss attacks and reaction motion are rejected until the asset is assigned.");
                bool started = InvokeStartAction(actor, attack, BossActionKind.Attack);
                float remaining = actor.GetActionCooldownRemaining(attack.AttackId, Time.time);

                Assert.That(started, Is.True);
                Assert.That(
                    remaining,
                    Is.EqualTo(attack.LastHitNodeEndTime + cooldown).Within(0.001f));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_StartReposition_CooldownStillBeginsAtActionStart()
        {
            const float cooldown = 2f;
            GameObject boss = CreateCompleteBoss("BossRepositionCooldownAnchorBoss");
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            try
            {
                BossAttackDefinition reposition = CreateTestRepositionDefinition(
                    "CooldownAnchor_Reposition",
                    out timeline,
                    cooldown);
                actionSet = CreateActionSetFromEntries(
                    CreateHitEscapeActionSetEntry(reposition.AttackId, BossActionKind.Reposition));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                BindActorReferences(actor);

                LogAssert.Expect(LogType.Error, "BossMotionController requires a BossMotionWarpProfile. Boss attacks and reaction motion are rejected until the asset is assigned.");
                bool started = InvokeStartAction(actor, reposition, BossActionKind.Reposition);
                float remaining = actor.GetActionCooldownRemaining(reposition.AttackId, Time.time);

                Assert.That(started, Is.True);
                Assert.That(remaining, Is.EqualTo(cooldown).Within(0.001f));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
            }
        }

        /// <summary>验证 P2/P3 每次转阶段各固定释放一次 BurstAreaSlash，且阶段请求不被已有冷却阻止。</summary>
        [Test]
        public void BossActor_PhaseTransition_ForcesBurstAreaSlashOncePerEnteredPhaseAndIgnoresCooldown()
        {
            GameObject boss = CreateCompleteBoss("BossPhaseTransitionBurstBoss");
            GameObject target = CreatePlayerTarget("BossPhaseTransitionBurstTarget", new Vector3(0f, 0f, 3f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition burst = CreateTestAttackDefinition(
                    RavenBossAttackIds.BurstAreaSlashId,
                    out timeline,
                    cooldown: 6f);
                RegisterTestTimeline(timeline);
                actionSet = CreateActionSet(RavenBossAttackIds.BurstAreaSlashId);
                profile = CreateMotionProfileForAttacks(CreateMotionConfig(RavenBossAttackIds.BurstAreaSlashId));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Strafe);

                Assert.That(InvokeStartAction(actor, burst, BossActionKind.Attack), Is.True);
                GetActionRunner(actor).EndAction();
                SetActorState(actor, BossStateId.Strafe);
                Assert.That(
                    actor.GetActionCooldownRemaining(RavenBossAttackIds.BurstAreaSlashId, Time.time),
                    Is.GreaterThan(0f));

                InvokeObserveCombatPhaseTransition(actor, BossCombatPhaseId.Phase2);

                Assert.That(InvokeTryStartPendingPhaseTransitionBurst(actor), Is.True);
                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(actor.CurrentActionId, Is.EqualTo(RavenBossAttackIds.BurstAreaSlashId));

                GetActionRunner(actor).EndAction();
                SetActorState(actor, BossStateId.Strafe);
                InvokeObserveCombatPhaseTransition(actor, BossCombatPhaseId.Desperation);
                Assert.That(InvokeTryStartPendingPhaseTransitionBurst(actor), Is.True);
                Assert.That(actor.CurrentActionId, Is.EqualTo(RavenBossAttackIds.BurstAreaSlashId));

                GetActionRunner(actor).EndAction();
                SetActorState(actor, BossStateId.Strafe);
                Assert.That(InvokeTryStartPendingPhaseTransitionBurst(actor), Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        /// <summary>验证阶段转换不会覆盖正在执行的攻击，并在该动作结束后的安全帧启动 BurstAreaSlash。</summary>
        [Test]
        public void BossActor_PhaseTransition_DoesNotInterruptRunningActionAndStartsAfterItEnds()
        {
            GameObject boss = CreateCompleteBoss("BossPhaseTransitionDeferredBurstBoss");
            GameObject target = CreatePlayerTarget("BossPhaseTransitionDeferredBurstTarget", new Vector3(0f, 0f, 3f));
            BossAttackTimelineAsset currentTimeline = null;
            BossAttackTimelineAsset burstTimeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            CombatResourceComponent resource = null;
            try
            {
                BossAttackDefinition current = CreateTestAttackDefinition(
                    "PhaseTransition_CurrentAttack",
                    out currentTimeline);
                _ = CreateTestAttackDefinition(
                    RavenBossAttackIds.BurstAreaSlashId,
                    out burstTimeline);
                RegisterTestTimeline(currentTimeline);
                RegisterTestTimeline(burstTimeline);
                actionSet = CreateActionSet(current.AttackId, RavenBossAttackIds.BurstAreaSlashId);
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(current.AttackId),
                    CreateMotionConfig(RavenBossAttackIds.BurstAreaSlashId));

                BossActor actor = boss.GetComponent<BossActor>();
                resource = boss.AddComponent<CombatResourceComponent>();
                AssignActionSet(actor, actionSet);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Strafe);
                Assert.That(InvokeStartAction(actor, current, BossActionKind.Attack), Is.True);

                resource.ApplyHitResult(new CombatHitResult(CombatHitOutcome.DamageOnly, 201f, 0f));
                actor.TickActor(0.01f);

                Assert.That(InvokeTryStartPendingPhaseTransitionBurst(actor), Is.False);
                Assert.That(actor.CurrentActionId, Is.EqualTo(current.AttackId));

                GetActionRunner(actor).EndAction();
                SetActorState(actor, BossStateId.Strafe);
                actor.TickActor(0.01f);
                Assert.That(actor.CurrentActionId, Is.EqualTo(RavenBossAttackIds.BurstAreaSlashId));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(currentTimeline);
                Object.DestroyImmediate(burstTimeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_CanReturnFromReaction_UsesCanReturnStartAsThreshold()
        {
            GameObject boss = CreateCompleteBoss("BossCanReturnThresholdBoss");
            BossReactionTimelineAsset timeline = null;
            try
            {
                RegisterBossHitStaggerTimeline(out timeline, canReturnStart: 0.2f, canReturnEnd: 0.25f);
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.HitStagger);

                TickActorState(actor, 0.19f);
                Assert.That(InvokeCanReturnFromReaction(actor), Is.False);

                TickActorState(actor, 0.2f);
                Assert.That(InvokeCanReturnFromReaction(actor), Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_CanReturnFromReaction_SkillHitReactionWaitsForPointNineFiveSeconds()
        {
            GameObject boss = CreateCompleteBoss("BossSkillHitReactionReturnThresholdBoss");
            BossReactionTimelineAsset timeline = null;
            try
            {
                RegisterBossHitStaggerTimeline(out timeline, canReturnStart: 0.2f, canReturnEnd: 1.2f);
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.HitStagger);
                SetPrivateField(actor, "lastReceivedAttackType", CombatAttackType.SkillAttack);
                SetPrivateField(actor, "lastReceivedHitOutcome", CombatHitOutcome.HitReaction);

                TickActorState(actor, 0.8f);
                Assert.That(InvokeCanReturnFromReaction(actor), Is.False);

                TickActorState(actor, 0.15f);
                Assert.That(InvokeCanReturnFromReaction(actor), Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_CanReturnFromReaction_PerfectGuardWaitsForRecoveryLock()
        {
            GameObject boss = CreateCompleteBoss("BossPerfectGuardCanReturnLockBoss");
            BossReactionTimelineAsset hitStaggerTimeline = null;
            BossAttackTimelineAsset attackTimeline = null;
            try
            {
                RegisterBossHitStaggerTimeline(out hitStaggerTimeline, canReturnStart: 0.2f, canReturnEnd: 0.25f);
                BossAttackDefinition attack = CreateTestAttackDefinition("PerfectGuard_Lock_Source", out attackTimeline);
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.HitStagger);
                TickActorState(actor, 0.4f);
                SetPrivateField(actor, "lastReceivedHitOutcome", CombatHitOutcome.PerfectGuard);

                BossAttackRecoveryTracker activeLock = new BossAttackRecoveryTracker();
                activeLock.Begin(attack, 1, Time.time);
                SetPrivateField(actor, "attackRecoveryTracker", activeLock);

                Assert.That(InvokeCanReturnFromReaction(actor), Is.False);

                BossAttackRecoveryTracker expiredLock = new BossAttackRecoveryTracker();
                expiredLock.Begin(attack, 1, Time.time - 10f);
                SetPrivateField(actor, "attackRecoveryTracker", expiredLock);

                Assert.That(InvokeCanReturnFromReaction(actor), Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(attackTimeline);
                Object.DestroyImmediate(hitStaggerTimeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        [Ignore("当前测试程序集不可用 UnityEngine.TestTools.LogAssert；该反例会故意触发 Error log。")]
        public void BossActor_StartRepositionWithoutMustStrafe_IsRejected()
        {
            GameObject boss = CreateCompleteBoss("BossInvalidRepositionBoss");
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            try
            {
                BossAttackDefinition reposition = CreateTestRepositionDefinition(
                    "Invalid_Reposition",
                    out timeline);
                actionSet = CreateActionSetFromEntries(new BossActionSetEntry(
                    reposition.AttackId,
                    BossActionKind.Reposition,
                    true,
                    System.Array.Empty<string>(),
                    BossActionPhaseMask.All,
                    new BossPhaseWeights(1f, 1f, 1f),
                    BossActionPoolId.HitEscape,
                    0f,
                    BossActionStrength.Defensive,
                    BossActionFollowUpPolicy.None,
                    BossPressurePreference.None,
                    BossActionPreferenceFlags.None,
                    0));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                BindActorReferences(actor);

                bool started = InvokeStartAction(actor, reposition, BossActionKind.Reposition);

                Assert.That(started, Is.False);
                Assert.That(actor.CurrentState, Is.Not.EqualTo(BossStateId.Action));
                Assert.That(GetActionRunner(actor).IsRunning, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_RepositionUsesActionStateThenEntersMinimumStrafe()
        {
            GameObject boss = CreateCompleteBoss("BossUnifiedRepositionBoss");
            GameObject target = CreatePlayerTarget("BossUnifiedRepositionTarget", new Vector3(0f, 0f, 3f));
            BossAttackTimelineAsset timeline = null;
            BossActionSet actionSet = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition reposition = CreateTestRepositionDefinition(
                    "Unified_RapidMoveBack",
                    out timeline);
                actionSet = CreateActionSetFromEntries(
                    CreateHitEscapeActionSetEntry(reposition.AttackId, BossActionKind.Reposition));
                profile = CreateMotionProfileForAttacks(
                    CreateMotionConfig(reposition.AttackId, 0f, 6f));

                BossActor actor = boss.GetComponent<BossActor>();
                AssignActionSet(actor, actionSet);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BindActorReferences(actor);
                GetDecisionMemory(actor).AddPressure(50f);

                bool started = InvokeStartAction(actor, reposition, BossActionKind.Reposition);

                Assert.That(started, Is.True);
                Assert.That(GetDecisionMemory(actor).TempoPressure, Is.EqualTo(30f).Within(0.001f));
                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(actor.CurrentActionKind, Is.EqualTo(BossActionKind.Reposition));
                Assert.That(GetActionRunner(actor).CurrentActionDefinition, Is.SameAs(reposition));
                Assert.That(GetActionRunner(actor).CurrentElapsed, Is.EqualTo(0f));

                actor.TickActor(0.51f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Strafe));
                Assert.That(GetActionRunner(actor).IsRunning, Is.False);
                Assert.That(GetDecisionMemory(actor).ConsecutiveAttackActionCount, Is.EqualTo(0));
                Assert.That(GetDecisionMemory(actor).RecentActionIds[0], Is.EqualTo(reposition.AttackId));

                actor.TickActor(0.1f);
                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Strafe));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(profile);
                Object.DestroyImmediate(actionSet);
                Object.DestroyImmediate(timeline);
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_TickActor_MovesApproachWhenTooFarAndNoAttackAvailable()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsApproachBoss");
            GameObject target = CreatePlayerTarget("BossTacticsApproachTarget", new Vector3(0f, 0f, 10f));
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Idle);

                actor.TickActor(0.1f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Approach));
            }
            finally
            {
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_TickActor_MovesStrafeWhenCloseAndNoAttackAvailable()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsCloseStrafeBoss");
            GameObject target = CreatePlayerTarget("BossTacticsCloseStrafeTarget", new Vector3(0f, 0f, 0.3f));
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Idle);

                actor.TickActor(0.1f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Strafe));
            }
            finally
            {
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        [Test]
        public void BossActor_TickActor_ContinuesStrafeBeforeDuration()
        {
            GameObject boss = CreateCompleteBoss("BossTacticsStrafeBoss");
            GameObject target = CreatePlayerTarget("BossTacticsStrafeTarget", new Vector3(0f, 0f, 4f));
            try
            {
                BossActor actor = boss.GetComponent<BossActor>();
                BindActorReferences(actor);
                SetActorState(actor, BossStateId.Strafe);

                actor.TickActor(0.1f);

                Assert.That(actor.CurrentState, Is.EqualTo(BossStateId.Strafe));
            }
            finally
            {
                Object.DestroyImmediate(boss);
                Object.DestroyImmediate(target);
            }
        }

        private static GameObject CreateCompleteBoss(string name)
        {
            GameObject boss = new GameObject(name);
            Transform rightSocket = new GameObject("RightSocket").transform;
            rightSocket.SetParent(boss.transform, false);
            Transform leftSocket = new GameObject("LeftSocket").transform;
            leftSocket.SetParent(boss.transform, false);
            Transform weapon = new GameObject("Weapon").transform;
            weapon.SetParent(rightSocket, false);
            BossWeaponHandController weaponHand = boss.AddComponent<BossWeaponHandController>();
            weaponHand.ConfigureWeapon(weapon, rightSocket, leftSocket);
            weaponHand.ConfigureLeftHandHitNodes(
                System.Array.Empty<BossAttackTimelineAsset>(),
                System.Array.Empty<string>());
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

        private static BossAttackDefinition CreateTestAttackDefinition(
            string attackId,
            out BossAttackTimelineAsset timeline,
            float weight = 1f,
            float cooldown = 0f)
        {
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
                cooldown,
                weight,
                0.1f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatHitNodeClip
                        {
                            Name = attackId + "_Hit",
                            CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                            StartTime = 0.1f,
                            EndTime = 0.2f,
                            EffectiveRange = 3f,
                            EffectiveAngle = 90f,
                            MaxHitsPerTarget = 1
                        }
                    }
                }
            });
            return timeline.ToBossAttackDefinition();
        }

        private static BossAttackDefinition CreateTestRepositionDefinition(
            string actionId,
            out BossAttackTimelineAsset timeline,
            float cooldown = 0f)
        {
            timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReposition,
                actionId,
                actionId,
                "Anim_" + actionId,
                0.5f,
                60f);
            timeline.ConfigureBossAttack(
                90f,
                cooldown,
                1f,
                0f);
            timeline.SetTracks(System.Array.Empty<CombatTimelineTrack>());
            return timeline.ToBossAttackDefinition();
        }

        private static BossBlackboard CreateSelectorBlackboard(
            float angleToTarget = 0f,
            string lastSelectedActionId = "",
            int repeatedSelectedActionCount = 0,
            float distanceToTarget = 4f)
        {
            return new BossBlackboard(
                BossStateId.Idle,
                true,
                string.Empty,
                0f,
                string.Empty,
                0f,
                true,
                distanceToTarget,
                angleToTarget,
                lastSelectedActionId,
                repeatedSelectedActionCount,
                false,
                BossActionKind.Attack,
                0,
                false);
        }

        private static BossBlackboard CreateBrainBlackboard(
            BossStateId state,
            bool hasTarget,
            bool isExecuting,
            float stateElapsed = 0f,
            float distanceToTarget = 5f,
            bool canReturnFromReaction = false)
        {
            return new BossBlackboard(
                state,
                state != BossStateId.None && state != BossStateId.Dead,
                isExecuting ? "Brain_Test_Action" : string.Empty,
                stateElapsed,
                string.Empty,
                0f,
                hasTarget,
                hasTarget ? distanceToTarget : float.MaxValue,
                hasTarget ? 0f : 180f,
                string.Empty,
                0,
                isExecuting,
                BossActionKind.Attack,
                isExecuting ? 11 : 0,
                canReturnFromReaction);
        }

        private static BossBrainDecisionInput CreateBrainDecisionInput(
            bool canUseReactionCounter = false,
            BossCombatPhaseId currentPhase = BossCombatPhaseId.Phase1,
            float tempoPressure = 0f,
            bool pressureDecayMode = false,
            int consecutiveAttackActionCount = 0,
            bool tempoResetRequired = false,
            IReadOnlyList<string> recentActionIds = null,
            BossPlayerTacticalSnapshot playerTacticalSnapshot = default)
        {
            BossPlayerTacticalSnapshot playerSnapshot = playerTacticalSnapshot.CurrentState == PlayerStateId.None
                ? BossPlayerTacticalSnapshot.Empty
                : playerTacticalSnapshot;

            return new BossBrainDecisionInput(
                canUseReactionCounter,
                currentPhase,
                tempoPressure,
                pressureDecayMode,
                consecutiveAttackActionCount,
                tempoResetRequired,
                recentActionIds,
                playerSnapshot);
        }

        private static BossPlayerTacticalSnapshot CreatePlayerSnapshot(
            bool isFrequentGuarder = false,
            bool isSustainedRetreat = false)
        {
            return new BossPlayerTacticalSnapshot(
                PlayerStateId.Idle,
                0f,
                isFrequentGuarder,
                isSustainedRetreat);
        }

        private static BossMotionWarpProfile CreateMotionProfileForAttacks(params BossAttackMotionConfig[] attacks)
        {
            BossMotionWarpProfile profile = ScriptableObject.CreateInstance<BossMotionWarpProfile>();
            typeof(BossMotionWarpProfile)
                .GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(profile, attacks);
            return profile;
        }

        private static BossAttackMotionConfig CreateMotionConfig(
            string attackId,
            float minSelectionDistance = 0f,
            float maxSelectionDistance = 6f)
        {
            return new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(minSelectionDistance, maxSelectionDistance, 0f, 0f),
                null,
                null,
                null);
        }

        private static BossAttackMotionConfig CreateMotionConfigWithCodeMove(string attackId)
        {
            return new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                new[]
                {
                    BossCodeMoveWindow.ToAttackPoint(
                        "Tactics_HitStaggerCounterMove",
                        0f,
                        0.5f,
                        1f,
                        0.1f,
                        3f,
                        10f,
                        360f,
                        false)
                });
        }

        private static void AssignMotionProfile(BossMotionController motionController, BossMotionWarpProfile profile)
        {
            typeof(BossMotionController)
                .GetField("motionWarpProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(motionController, profile);
        }

        private static void AssignActionSet(BossActor actor, BossActionSet actionSet)
        {
            SetPrivateField(actor, "actionSet", actionSet);
        }

        private static void SetPrivateField(object targetObject, string fieldName, object value)
        {
            targetObject.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(targetObject, value);
        }

        private static int GetPrivateInt(object targetObject, string fieldName)
        {
            return (int)targetObject.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(targetObject);
        }

        private static BossDecisionMemory GetDecisionMemory(BossActor actor)
        {
            return (BossDecisionMemory)typeof(BossActor)
                .GetField("decisionMemory", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
        }

        private static BossActionRunner GetActionRunner(BossActor actor)
        {
            return (BossActionRunner)typeof(BossActor)
                .GetField("actionRunner", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
        }

        private static BossMovementSystem GetMovementSystem(BossActor actor)
        {
            return (BossMovementSystem)typeof(BossActor)
                .GetField("movementSystem", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
        }

        private static bool InvokeStartAction(
            BossActor actor,
            BossAttackDefinition actionDefinition,
            BossActionKind actionKind)
        {
            return (bool)typeof(BossActor)
                .GetMethod("StartAction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, new object[] { actionDefinition, actionKind });
        }

        /// <summary>调用 Actor 的阶段观察入口，供测试精确构造阶段边界。</summary>
        /// <param name="actor">接受阶段变化的测试 BossActor。</param>
        /// <param name="currentPhase">本次要提交的当前 HP 阶段。</param>
        private static void InvokeObserveCombatPhaseTransition(
            BossActor actor,
            BossCombatPhaseId currentPhase)
        {
            typeof(BossActor)
                .GetMethod("ObserveCombatPhaseTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, new object[] { currentPhase });
        }

        /// <summary>调用 Actor 的待执行阶段招式入口。</summary>
        /// <param name="actor">持有阶段请求的测试 BossActor。</param>
        /// <returns>true 表示本次调用成功启动 BurstAreaSlash；false 表示无请求、当前动作仍在运行或配置无效。</returns>
        private static bool InvokeTryStartPendingPhaseTransitionBurst(BossActor actor)
        {
            return (bool)typeof(BossActor)
                .GetMethod("TryStartPendingPhaseTransitionBurst", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, null);
        }

        private static BossActionSet CreateActionSet(params string[] actionIds)
        {
            BossActionSet actionSet = ScriptableObject.CreateInstance<BossActionSet>();
            BossActionSetEntry[] entries = new BossActionSetEntry[actionIds.Length];
            for (int i = 0; i < actionIds.Length; i++)
            {
                entries[i] = CreateActionSetEntry(actionIds[i]);
            }

            typeof(BossActionSet)
                .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(actionSet, entries);
            return actionSet;
        }

        private static BossActionSet CreateActionSetFromEntries(params BossActionSetEntry[] entries)
        {
            BossActionSet actionSet = ScriptableObject.CreateInstance<BossActionSet>();
            typeof(BossActionSet)
                .GetField("actions", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(actionSet, entries);
            return actionSet;
        }

        private static BossActionSetEntry CreateActionSetEntry(string actionId, params string[] tags)
        {
            return CreateActionSetEntryWithTempo(
                actionId,
                BossActionPhaseMask.All,
                0f,
                0f,
                0f,
                false,
                0,
                tags);
        }

        private static BossActionSetEntry CreateHitEscapeActionSetEntry(
            string actionId,
            BossActionKind actionKind)
        {
            return new BossActionSetEntry(
                actionId,
                actionKind,
                true,
                System.Array.Empty<string>(),
                BossActionPhaseMask.All,
                new BossPhaseWeights(1f, 1f, 1f),
                BossActionPoolId.HitEscape,
                0f,
                BossActionStrength.Defensive,
                actionKind == BossActionKind.Reposition
                    ? BossActionFollowUpPolicy.MustStrafe
                    : BossActionFollowUpPolicy.None,
                BossPressurePreference.None,
                BossActionPreferenceFlags.None,
                0);
        }

        private static BossActionSetEntry CreateActionSetEntryWithTempo(
            string actionId,
            BossActionPhaseMask allowedPhases,
            float pressureCost,
            float maxPressureToSelect,
            float minPressureToPrefer,
            bool tempoResetAfterAction,
            int recentUseLimit,
            params string[] tags)
        {
            BossPressurePreference preference = minPressureToPrefer > 0f
                ? BossPressurePreference.High
                : BossPressurePreference.None;
            return new BossActionSetEntry(
                actionId,
                BossActionKind.Attack,
                true,
                tags ?? System.Array.Empty<string>(),
                allowedPhases,
                new BossPhaseWeights(1f, 1f, 1f),
                BossActionPoolId.CloseDuel,
                pressureCost,
                maxPressureToSelect > 0f && maxPressureToSelect < 100f
                    ? BossActionStrength.High
                    : BossActionStrength.Low,
                tempoResetAfterAction
                    ? BossActionFollowUpPolicy.MustEnterNeutral
                    : BossActionFollowUpPolicy.None,
                preference,
                BossActionPreferenceFlags.None,
                recentUseLimit);
        }

        private static void RegisterBossAttackTimeline(string attackId, out BossAttackTimelineAsset timeline)
        {
            CombatTimelineProvider.ResetCache();
            timeline = null;
            _ = CreateTestAttackDefinition(attackId, out timeline);
            RegisterTestTimeline(timeline);
        }

        private static void RegisterBossKnockdownTimeline(out BossReactionTimelineAsset timeline)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                "Boss_Knockdown",
                "Boss_Knockdown",
                "Anim_Boss_Knockdown",
                0.5f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.Knockdown);
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
            });
            RegisterTestTimeline(timeline);
        }

        private static void RegisterBossHitStaggerTimeline(
            out BossReactionTimelineAsset timeline,
            float canReturnStart,
            float canReturnEnd)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                "Boss_HitStagger",
                "Boss_HitStagger",
                "Anim_Boss_HitStagger",
                0.6f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.HitReaction);
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
                            EndTime = 0.1f
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "Recovery",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery,
                            StartTime = 0.1f,
                            EndTime = 0.6f
                        },
                        new CombatReactionWindowClip
                        {
                            Name = "CanReturn",
                            CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn,
                            StartTime = canReturnStart,
                            EndTime = canReturnEnd
                        }
                    }
                }
            });
            RegisterTestTimeline(timeline);
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

        private static void SetActorState(BossActor actor, BossStateId state)
        {
            BossStateMachine stateMachine = (BossStateMachine)typeof(BossActor)
                .GetField("stateMachine", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
            stateMachine.TransitionTo(state, out _);
        }

        private static void TickActorState(BossActor actor, float deltaTime)
        {
            BossStateMachine stateMachine = (BossStateMachine)typeof(BossActor)
                .GetField("stateMachine", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(actor);
            stateMachine.Tick(deltaTime);
        }

        private static bool InvokeCanReturnFromReaction(BossActor actor)
        {
            return (bool)typeof(BossActor)
                .GetMethod("CanReturnFromReaction", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, null);
        }

        private static void BindActorReferences(BossActor actor)
        {
            typeof(BossActor)
                .GetMethod("BindReferences", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(actor, null);
        }
    }
}
