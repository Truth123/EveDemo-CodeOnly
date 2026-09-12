// 文件说明：验证 BossActionRunner 正式攻击生命周期接管和 BossActor 委托路径。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 GameObject，不影响正式场景。

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
using ProjectEVE.Combat.Timeline;
using System.Reflection;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossActionRunnerTests
    {
        [Test]
        public void BossActionRunner_StartAttack_BeginsExecutorLifecycle()
        {
            GameObject boss = CreateBoss("BossActionRunnerStartBoss", out BossAttackExecutor executor, out BossMotionController motion, out BossAnimationBridge animation);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossActionRunner runner = CreateRunner(executor, motion, animation);
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_Start", 1f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                runner.Bind(executor, movement, animation);

                BossActionRunnerStartResult startResult = runner.StartAction(attack, BossActionKind.Attack, null);

                Assert.That(startResult.Succeeded, Is.True);
                Assert.That(startResult.ActionId, Is.EqualTo("ActionRunner_Start"));
                Assert.That(startResult.AttackInstanceId, Is.EqualTo(1));
                Assert.That(runner.IsRunning, Is.True);
                Assert.That(runner.CurrentActionId, Is.EqualTo("ActionRunner_Start"));
                Assert.That(runner.CurrentActionDefinition, Is.SameAs(attack));
                Assert.That(movement.IsActionMotionActive, Is.True);
                Assert.That(executor.IsExecuting, Is.True);
                Assert.That(executor.CurrentAttackInstanceId, Is.EqualTo(1));
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        [Test]
        public void BossActionRunner_Tick_ReturnsRunningThenCompletedAtAttackDuration()
        {
            GameObject boss = CreateBoss("BossActionRunnerTickBoss", out BossAttackExecutor executor, out BossMotionController motion, out BossAnimationBridge animation);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossActionRunner runner = CreateRunner(executor, motion, animation);
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_Tick", 0.5f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                runner.Bind(executor, movement, animation);
                runner.StartAction(attack, BossActionKind.Attack, null);

                BossActionRunnerTickResult running = runner.Tick(0.1f);
                BossActionRunnerTickResult completed = runner.Tick(0.5f);

                Assert.That(running.Status, Is.EqualTo(BossActionRunnerTickStatus.Running));
                Assert.That(completed.IsCompleted, Is.True);
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        [Test]
        public void BossActionRunner_EndAttack_ClearsExecutorAndRunner()
        {
            GameObject boss = CreateBoss("BossActionRunnerEndBoss", out BossAttackExecutor executor, out BossMotionController motion, out BossAnimationBridge animation);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossActionRunner runner = CreateRunner(executor, motion, animation);
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_End", 1f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(motion, profile);
                BossMovementSystem movement = CreateMovementSystem(motion);
                runner.Bind(executor, movement, animation);
                runner.StartAction(attack, BossActionKind.Attack, null);

                runner.EndAction();

                Assert.That(runner.IsRunning, Is.False);
                Assert.That(movement.CurrentLayer, Is.EqualTo(BossMovementLayer.None));
                Assert.That(runner.CurrentActionId, Is.EqualTo(string.Empty));
                Assert.That(runner.CurrentActionDefinition, Is.Null);
                Assert.That(executor.IsExecuting, Is.False);
                Assert.That(executor.CurrentAttackId, Is.EqualTo(string.Empty));
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        [Test]
        public void BossActor_StartAttack_DelegatesStartToActionRunner()
        {
            GameObject boss = CreateBossWithActor("BossActorActionRunnerStartBoss", out BossActor controller, out BossAttackExecutor executor);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_ControllerStart", 1f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActionSet actionSet = AssignActionSet(controller, attack.AttackId);

                InvokePrivate(controller, "StartAction", attack, BossActionKind.Attack);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Action));
                Assert.That(executor.IsExecuting, Is.True);
                Assert.That(executor.CurrentAttackId, Is.EqualTo("ActionRunner_ControllerStart"));
                Object.DestroyImmediate(actionSet);
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        [Test]
        public void BossActor_TickAction_DelegatesAttackCompletionToActionRunner()
        {
            GameObject boss = CreateBossWithActor("BossActorActionRunnerTickBoss", out BossActor controller, out BossAttackExecutor executor);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_ControllerTick", 0.1f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActionSet actionSet = AssignActionSet(controller, attack.AttackId);
                InvokePrivate(controller, "StartAction", attack, BossActionKind.Attack);

                InvokePrivate(controller, "TickAction", 0.2f);

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.Recovery));
                Assert.That(executor.IsExecuting, Is.True,
                    "进入 Recovery 后仍需保留攻击尾段，直到 Recovery 推进器完成动画与 Motion 清理。");
                Object.DestroyImmediate(actionSet);
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        [Test]
        public void BossActor_StopBossAi_EndsActionRunnerLifecycle()
        {
            GameObject boss = CreateBossWithActor("BossActorActionRunnerStopBoss", out BossActor controller, out BossAttackExecutor executor);
            BossAttackTimelineAsset timeline = null;
            BossMotionWarpProfile profile = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("ActionRunner_ControllerStop", 1f, out timeline);
                profile = CreateMotionProfileForAttack(attack.AttackId);
                AssignMotionProfile(boss.GetComponent<BossMotionController>(), profile);
                BossActionSet actionSet = AssignActionSet(controller, attack.AttackId);
                InvokePrivate(controller, "StartAction", attack, BossActionKind.Attack);

                controller.StopBossAi();

                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.None));
                Assert.That(executor.IsExecuting, Is.False);
                Assert.That(controller.CurrentActionId, Is.EqualTo(string.Empty));
                Object.DestroyImmediate(actionSet);
            }
            finally
            {
                DestroyTestObjects(boss, timeline, profile);
            }
        }

        private static BossActionRunner CreateRunner(
            BossAttackExecutor executor,
            BossMotionController motion,
            BossAnimationBridge animation)
        {
            BossActionRunner runner = new BossActionRunner();
            runner.Bind(executor, CreateMovementSystem(motion), animation);
            return runner;
        }

        private static BossMovementSystem CreateMovementSystem(BossMotionController motion)
        {
            BossMovementSystem movement = new BossMovementSystem();
            movement.Bind(motion);
            return movement;
        }

        private static GameObject CreateBoss(
            string name,
            out BossAttackExecutor executor,
            out BossMotionController motion,
            out BossAnimationBridge animation)
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
            animation = boss.AddComponent<BossAnimationBridge>();
            motion = boss.AddComponent<BossMotionController>();
            executor = boss.AddComponent<BossAttackExecutor>();
            Behaviour weaponSampler = boss.AddComponent<TestTrailSampler>();
            Behaviour leftFootSampler = boss.AddComponent<TestTrailSampler>();
            Behaviour rightFootSampler = boss.AddComponent<TestTrailSampler>();
            weaponSampler.enabled = false;
            leftFootSampler.enabled = false;
            rightFootSampler.enabled = false;
            SetPrivateField(executor, "weaponSlashTrailSampler", weaponSampler);
            SetPrivateField(executor, "leftFootSlashTrailSampler", leftFootSampler);
            SetPrivateField(executor, "rightFootSlashTrailSampler", rightFootSampler);
            SetPrivateField(executor, "logHits", false);
            return boss;
        }

        private static GameObject CreateBossWithActor(
            string name,
            out BossActor controller,
            out BossAttackExecutor executor)
        {
            GameObject boss = CreateBoss(name, out executor, out _, out _);
            controller = boss.AddComponent<BossActor>();
            SetPrivateField(controller, "autoStart", false);
            return boss;
        }

        private static BossAttackDefinition CreateAttackDefinition(
            string attackId,
            float duration,
            out BossAttackTimelineAsset timeline)
        {
            timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossAttack,
                attackId,
                attackId,
                "Anim_" + attackId,
                duration,
                60f);
            timeline.ConfigureBossAttack(
                90f,
                0.1f,
                duration,
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
                            StartTime = Mathf.Min(0.02f, duration * 0.2f),
                            EndTime = Mathf.Max(0.03f, duration * 0.5f),
                            EffectiveRange = 3f,
                            EffectiveAngle = 90f,
                            MaxHitsPerTarget = 1
                        }
                    }
                }
            });
            return timeline.ToBossAttackDefinition();
        }

        private static BossMotionWarpProfile CreateMotionProfileForAttack(string attackId)
        {
            BossMotionWarpProfile profile = ScriptableObject.CreateInstance<BossMotionWarpProfile>();
            BossAttackMotionConfig config = new BossAttackMotionConfig(
                attackId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(0f, 6f, 0f, 0f),
                null,
                null,
                null);
            typeof(BossMotionWarpProfile)
                .GetField("attacks", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(profile, new[] { config });
            return profile;
        }

        private static void AssignMotionProfile(BossMotionController motion, BossMotionWarpProfile profile)
        {
            typeof(BossMotionController)
                .GetField("motionWarpProfile", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(motion, profile);
        }

        private static BossActionSet AssignActionSet(BossActor actor, string actionId)
        {
            BossActionSet actionSet = ScriptableObject.CreateInstance<BossActionSet>();
            actionSet.Configure(
                new[]
                {
                    new BossActionSetEntry(
                        actionId,
                        BossActionKind.Attack,
                        true,
                        System.Array.Empty<string>(),
                        BossActionPhaseMask.All,
                        new BossPhaseWeights(1f, 1f, 1f),
                        BossActionPoolId.CloseDuel,
                        0f,
                        BossActionStrength.Low,
                        BossActionFollowUpPolicy.None,
                        BossPressurePreference.None,
                        BossActionPreferenceFlags.None,
                        0)
                },
                System.Array.Empty<BossPhasePressureProfile>(),
                System.Array.Empty<BossActionPoolPolicy>(),
                new BossAiTuning());
            SetPrivateField(actor, "actionSet", actionSet);
            return actionSet;
        }

        private static void InvokePrivate(object target, string methodName, params object[] args)
        {
            target.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(target, args);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static void DestroyTestObjects(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                if (objects[i] != null)
                {
                    Object.DestroyImmediate(objects[i]);
                }
            }
        }

        private sealed class TestTrailSampler : MonoBehaviour
        {
        }
    }
}
