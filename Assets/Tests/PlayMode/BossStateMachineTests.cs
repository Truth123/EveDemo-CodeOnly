// 文件说明：验证 BossStateMachine 唯一状态存储和 BossActor facade 接入。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject，不影响正式场景。

using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.Config;
using ProjectEVE.Boss.Reaction;
using ProjectEVE.Boss.State;
using ProjectEVE.Boss.Movement;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossStateMachineTests
    {
        [Test]
        public void BossStateMachine_InitialState_IsNone()
        {
            BossStateMachine stateMachine = new BossStateMachine();

            Assert.That(stateMachine.CurrentState, Is.EqualTo(BossStateId.None));
            Assert.That(stateMachine.StateElapsed, Is.EqualTo(0f));
        }

        [Test]
        public void BossStateMachine_TransitionTo_UpdatesCurrentState()
        {
            BossStateMachine stateMachine = new BossStateMachine();

            bool changed = stateMachine.TransitionTo(BossStateId.Approach, out BossStateId previousState);

            Assert.That(changed, Is.True);
            Assert.That(previousState, Is.EqualTo(BossStateId.None));
            Assert.That(stateMachine.CurrentState, Is.EqualTo(BossStateId.Approach));
            Assert.That(stateMachine.StateElapsed, Is.EqualTo(0f));
        }

        [Test]
        public void BossStateMachine_TransitionTo_SameStateDoesNotReportChanged()
        {
            BossStateMachine stateMachine = new BossStateMachine();
            stateMachine.TransitionTo(BossStateId.Idle, out _);
            stateMachine.Tick(0.25f);

            bool changed = stateMachine.TransitionTo(BossStateId.Idle, out BossStateId previousState);

            Assert.That(changed, Is.False);
            Assert.That(previousState, Is.EqualTo(BossStateId.Idle));
            Assert.That(stateMachine.CurrentState, Is.EqualTo(BossStateId.Idle));
            Assert.That(stateMachine.StateElapsed, Is.EqualTo(0.25f).Within(0.0001f));
        }

        [Test]
        public void BossStateMachine_EvaluateTick_MapsIdleAndRepositionToEvaluateTactics()
        {
            BossStateId[] tacticalStates =
            {
                BossStateId.Idle,
                BossStateId.Approach,
                BossStateId.Strafe
            };

            foreach (BossStateId state in tacticalStates)
            {
                BossStateMachine stateMachine = new BossStateMachine();
                stateMachine.TransitionTo(state, out _);
                stateMachine.Tick(1f);

                BossStateTickDecisionId decision = stateMachine.EvaluateTick(canReturnFromReaction: false);

                Assert.That(decision, Is.EqualTo(BossStateTickDecisionId.EvaluateTactics), state.ToString());
            }
        }

        [Test]
        public void BossStateMachine_EvaluateTick_RecoveryDelegatesAndExpiredHitStaggerWithoutReturnDoesNothing()
        {
            BossStateMachine recoveryStateMachine = new BossStateMachine();
            recoveryStateMachine.TransitionTo(BossStateId.Recovery, out _);
            recoveryStateMachine.Tick(0.25f);

            BossStateMachine hitStaggerStateMachine = new BossStateMachine();
            hitStaggerStateMachine.TransitionTo(BossStateId.HitStagger, out _);
            hitStaggerStateMachine.Tick(0.25f);

            Assert.That(
                recoveryStateMachine.EvaluateTick(canReturnFromReaction: false),
                Is.EqualTo(BossStateTickDecisionId.TickRecovery));
            Assert.That(
                hitStaggerStateMachine.EvaluateTick(canReturnFromReaction: false),
                Is.EqualTo(BossStateTickDecisionId.None));
        }

        [Test]
        public void BossStateMachine_EvaluateTick_HitStaggerCanReturnRequestsTactics()
        {
            BossStateMachine stateMachine = new BossStateMachine();
            stateMachine.TransitionTo(BossStateId.HitStagger, out _);
            stateMachine.Tick(0.25f);

            BossStateTickDecisionId decision = stateMachine.EvaluateTick(canReturnFromReaction: true);

            Assert.That(decision, Is.EqualTo(BossStateTickDecisionId.EvaluateTactics));
        }

        [Test]
        public void BossStateMachine_EvaluateTick_MapsAttackAndKnockdownToExecutionTicks()
        {
            BossStateMachine actionStateMachine = new BossStateMachine();
            actionStateMachine.TransitionTo(BossStateId.Action, out _);
            BossStateMachine knockdownStateMachine = new BossStateMachine();
            knockdownStateMachine.TransitionTo(BossStateId.Knockdown, out _);

            Assert.That(
                actionStateMachine.EvaluateTick(canReturnFromReaction: false),
                Is.EqualTo(BossStateTickDecisionId.TickAction));
            Assert.That(
                knockdownStateMachine.EvaluateTick(canReturnFromReaction: false),
                Is.EqualTo(BossStateTickDecisionId.TickKnockdown));
        }

        [Test]
        public void BossActor_CommitStateTransition_UpdatesStrafeDirection()
        {
            GameObject boss = CreateCompleteBoss("BossActorStrafeRuntimeCommitTest");
            try
            {
                BossActor controller = boss.GetComponent<BossActor>();

                InvokeCommitStateTransition(controller, BossStateId.Strafe);

                Assert.That(GetPrivateInt(controller, "strafeDirection"), Is.EqualTo(-1));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_TransitionFacade_UsesBossStateMachine()
        {
            GameObject boss = CreateCompleteBoss("BossActorStateMachineFacadeTest");
            try
            {
                BossActor controller = boss.GetComponent<BossActor>();

                InvokeCommitStateTransition(controller, BossStateId.Strafe);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Strafe));
                Assert.That(controller.StateElapsed, Is.EqualTo(0f));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void BossActor_StateTransitions_UseRequestEntryBeforeLowestCommit()
        {
            string sourcePath = Path.Combine(
                Application.dataPath,
                "Runtime",
                "Boss",
                "Actor",
                "BossActor.cs");
            string[] directCommitCalls = File.ReadAllLines(sourcePath)
                .Select(line => line.Trim())
                .Where(line => line.StartsWith("CommitStateTransition("))
                .ToArray();

            Assert.That(directCommitCalls.Length, Is.EqualTo(1));
            Assert.That(directCommitCalls[0], Does.Contain("request.NextState"));
        }

        [Test]
        public void BossActor_StateMachineTransition_DoesNotChangeExistingStartStopBehavior()
        {
            GameObject boss = CreateCompleteBoss("BossActorStateMachineStartStopTest");
            try
            {
                BossActor controller = boss.GetComponent<BossActor>();

                controller.StartBossAi();

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Idle));
                Assert.That(controller.IsRunning, Is.True);

                controller.StopBossAi();

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.None));
                Assert.That(controller.IsRunning, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
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

        private static void InvokeCommitStateTransition(BossActor controller, BossStateId state)
        {
            typeof(BossActor)
                .GetMethod("CommitStateTransition", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { state, false });
        }

        private static int GetPrivateInt(BossActor controller, string fieldName)
        {
            return (int)typeof(BossActor)
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(controller);
        }

    }
}
