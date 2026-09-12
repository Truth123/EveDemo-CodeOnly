// 文件说明：验证 BossCombatSystem 正式命中执行、Detached 场景视觉模板、SourcePart 固定拖尾、语义过滤、Receiver / Hurtbox 去重和 Executor 委托路径。
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
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class BossCombatSystemTests
    {
        [Test]
        public void BossCombatSystem_ProcessDetachedHit_DoesNotRequireBoneAnchor()
        {
            GameObject boss = CreateBoss("BossCombatSystemDetachedBoss", out BossAttackExecutor executor, out BossActor actor);
            GameObject target = CreatePlayerTarget("BossCombatSystemDetachedTarget", new Vector3(0f, 0f, 8f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Detached", out timeline);
                BossCombatSystem system = CreateStartedCombatSystem(1101);
                Collider collider = target.GetComponent<Collider>();
                PlayerCombatReceiver receiver = target.GetComponent<PlayerCombatReceiver>();
                BossCombatProcessResult result = system.ProcessDetachedHit(
                    new BossCombatExecutionRequest(
                        attack,
                        1101,
                        attack.HitNodes[0],
                        null,
                        boss.transform,
                        executor,
                        actor,
                        CombatTeam.Boss,
                        ~0,
                        4,
                        false,
                        () => true),
                    receiver,
                    collider,
                    target.transform.position - Vector3.forward,
                    receiver.GetInstanceID());

                Assert.That(result.HasHit, Is.True);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossDetachedHitVolume_InstantPulseHitsOverlappingTargetOnlyOnce()
        {
            GameObject boss = CreateBoss("BossInstantPulseBoss", out BossAttackExecutor executor, out BossActor actor);
            GameObject target = CreatePlayerTarget("BossInstantPulseTarget", new Vector3(0f, 0f, 2f), true, true);
            GameObject extraCollider = new GameObject("BossInstantPulseTargetExtraCollider");
            extraCollider.transform.SetParent(target.transform, false);
            extraCollider.AddComponent<BoxCollider>().size = Vector3.one * 0.25f;
            GameObject pulseObject = new GameObject("BossInstantPulseVolume");
            pulseObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.identity);
            pulseObject.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            SphereCollider sphereCollider = pulseObject.AddComponent<SphereCollider>();
            sphereCollider.radius = 1f;
            Rigidbody rigidbody = pulseObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            BossDetachedHitVolume pulse = pulseObject.AddComponent<BossDetachedHitVolume>();
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_InstantPulse",
                    out timeline,
                    effectiveRange: 20f,
                    effectiveAngle: 180f,
                    sourcePart: BossAttackSourcePart.Detached);
                BossCombatSystem system = CreateStartedCombatSystem(1102);
                pulse.Initialize(
                    system,
                    new BossCombatExecutionRequest(
                        attack,
                        1102,
                        attack.HitNodes[0],
                        null,
                        boss.transform,
                        executor,
                        actor,
                        CombatTeam.Boss,
                        ~0,
                        8,
                        false,
                        () => true),
                    Vector3.zero,
                    0.45f);
                Physics.SyncTransforms();

                int hitCount = pulse.ExecuteInstantPulse();

                Assert.That(hitCount, Is.EqualTo(1));
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
                Assert.That(sphereCollider.enabled, Is.False);
            }
            finally
            {
                DestroyTestObjects(boss, target, pulseObject, timeline);
            }
        }

        [Test]
        public void BossDetachedHitVolume_InstantPulseUsesConfiguredWorldCenterAndRadius()
        {
            GameObject boss = CreateBoss("BossInstantPulseRangeBoss", out BossAttackExecutor executor, out BossActor actor);
            GameObject insideTarget = CreatePlayerTarget("BossInstantPulseInside", new Vector3(0f, 0f, 4.2f), true, true);
            GameObject outsideTarget = CreatePlayerTarget("BossInstantPulseOutside", new Vector3(0f, 0f, 5f), true, true);
            GameObject highTarget = CreatePlayerTarget("BossInstantPulseHigh", new Vector3(0f, 3f, 2f), true, true);
            GameObject pulseObject = new GameObject("BossInstantPulseRangeVolume");
            pulseObject.transform.SetPositionAndRotation(new Vector3(0f, 0f, 2f), Quaternion.identity);
            pulseObject.transform.localScale = new Vector3(2.5f, 1f, 2.5f);
            pulseObject.AddComponent<SphereCollider>().radius = 1f;
            Rigidbody rigidbody = pulseObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            BossDetachedHitVolume pulse = pulseObject.AddComponent<BossDetachedHitVolume>();
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_InstantPulseRange",
                    out timeline,
                    effectiveRange: 20f,
                    effectiveAngle: 180f,
                    sourcePart: BossAttackSourcePart.Detached);
                BossCombatSystem system = CreateStartedCombatSystem(1104);
                pulse.Initialize(
                    system,
                    new BossCombatExecutionRequest(
                        attack,
                        1104,
                        attack.HitNodes[0],
                        null,
                        boss.transform,
                        executor,
                        actor,
                        CombatTeam.Boss,
                        ~0,
                        12,
                        false,
                        () => true),
                    Vector3.zero,
                    0.45f);
                Physics.SyncTransforms();

                int hitCount = pulse.ExecuteInstantPulse();

                Assert.That(hitCount, Is.EqualTo(1));
                Assert.That(insideTarget.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
                Assert.That(outsideTarget.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
                Assert.That(highTarget.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
            }
            finally
            {
                DestroyTestObjects(boss, insideTarget, outsideTarget, highTarget, pulseObject, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessDetachedHit_AppliesEffectiveRangeFilter()
        {
            GameObject boss = CreateBoss("BossDetachedRangeBoss", out BossAttackExecutor executor, out BossActor actor);
            GameObject target = CreatePlayerTarget("BossDetachedRangeTarget", new Vector3(0f, 0f, 3f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_DetachedRange",
                    out timeline,
                    effectiveRange: 1f,
                    sourcePart: BossAttackSourcePart.Detached);
                BossCombatSystem system = CreateStartedCombatSystem(1103);
                PlayerCombatReceiver receiver = target.GetComponent<PlayerCombatReceiver>();
                BossCombatProcessResult result = system.ProcessDetachedHit(
                    new BossCombatExecutionRequest(
                        attack,
                        1103,
                        attack.HitNodes[0],
                        null,
                        boss.transform,
                        executor,
                        actor,
                        CombatTeam.Boss,
                        ~0,
                        4,
                        false,
                        () => true),
                    receiver,
                    target.GetComponent<Collider>(),
                    target.transform.position,
                    receiver.GetInstanceID());

                Assert.That(result.HasSemanticResult, Is.True);
                Assert.That(result.SemanticAccepted, Is.False);
                Assert.That(result.HasHit, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossDetachedAttackEmitter_InstantPulseInheritsExecutorQueryConfiguration()
        {
            GameObject boss = CreateBoss("BossDetachedPulseConfigBoss", out _, out BossActor actor);
            boss.SetActive(false);
            GameObject target = CreatePlayerTarget("BossDetachedPulseConfigTarget", new Vector3(0f, 0f, 2f), true, true);
            target.layer = 8;
            GameObject detachedPrototypeObject = new GameObject("BossDetachedPulseConfigPrototype");
            detachedPrototypeObject.transform.position = Vector3.one * 1000f;
            detachedPrototypeObject.AddComponent<SphereCollider>().radius = 3f;
            Rigidbody rigidbody = detachedPrototypeObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            BossDetachedHitVolume detachedPrototype = detachedPrototypeObject.AddComponent<BossDetachedHitVolume>();
            BossDetachedAttackEmitter emitter = boss.AddComponent<BossDetachedAttackEmitter>();
            BossAttackTimelineAsset timeline = null;
            BossDetachedHitVolume spawned = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_DetachedPulseConfig",
                    out timeline,
                    effectiveRange: 20f,
                    effectiveAngle: 360f,
                    hitNodeIds: new[] { "Pulse_1" },
                    sourcePart: BossAttackSourcePart.Detached);
                BossDetachedAttackBinding binding = new BossDetachedAttackBinding
                {
                    ActionId = attack.AttackId,
                    HitNodeId = attack.HitNodes[0].HitNodeId,
                    Prefab = detachedPrototype,
                    SpawnAnchor = boss.transform,
                    LocalScale = Vector3.one,
                    DirectionMode = BossDetachedDirectionMode.BossHorizontalForward,
                    ExecutionMode = BossDetachedExecutionMode.InstantPulse,
                    Lifetime = 0.1f
                };
                SetPrivateField(emitter, "bindings", new[] { binding });
                boss.SetActive(true);
                BossCombatSystem system = CreateStartedCombatSystem(1702);
                LayerMask expectedMask = 1 << 8;
                const int expectedBufferSize = 23;
                Physics.SyncTransforms();

                emitter.BeginAttack();
                emitter.SpawnForHitNode(
                    attack,
                    1702,
                    attack.HitNodes[0],
                    boss.transform,
                    emitter,
                    actor,
                    CombatTeam.Boss,
                    system,
                    expectedMask,
                    expectedBufferSize,
                    false);

                BossDetachedHitVolume[] volumes = Object.FindObjectsByType<BossDetachedHitVolume>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                for (int i = 0; i < volumes.Length; i++)
                {
                    if (volumes[i] != detachedPrototype)
                    {
                        spawned = volumes[i];
                        break;
                    }
                }

                Assert.That(spawned, Is.Not.Null);
                BossCombatExecutionRequest request = (BossCombatExecutionRequest)typeof(BossDetachedHitVolume)
                    .GetField("request", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(spawned);
                Assert.That(request.HitMask.value, Is.EqualTo(expectedMask.value));
                Assert.That(request.OverlapBufferSize, Is.EqualTo(expectedBufferSize));
            }
            finally
            {
                DestroyTestObjects(spawned, detachedPrototypeObject, boss, target, timeline);
            }
        }

        [Test]
        public void BossDetachedAttackEmitter_ClonesSelfPropelledTemplateWithoutStackingDetachedMovement()
        {
            GameObject boss = CreateBoss("BossDetachedTemplateBoss", out _, out BossActor actor);
            boss.SetActive(false);
            GameObject visualTemplate = new GameObject("BossDetachedTemplateVisual");
            visualTemplate.transform.SetParent(boss.transform, false);
            visualTemplate.transform.localPosition = new Vector3(0.15f, 0.5f, -0.2f);
            visualTemplate.transform.localRotation = Quaternion.Euler(0f, 0f, 180f);
            visualTemplate.transform.localScale = Vector3.one * 0.5f;
            visualTemplate.AddComponent<ParticleSystem>();
            GameObject pivot = new GameObject("Pivot");
            pivot.transform.SetParent(visualTemplate.transform, false);
            ParticleSystem pivotParticles = pivot.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule pivotMain = pivotParticles.main;
            pivotMain.startSpeed = 30f;
            GameObject slash = new GameObject("Slashes");
            slash.transform.SetParent(pivot.transform, false);
            ParticleSystem slashParticles = slash.AddComponent<ParticleSystem>();
            ParticleSystem.MainModule slashMain = slashParticles.main;
            slashMain.startLifetime = 0.3f;

            GameObject detachedPrototypeObject = new GameObject("BossDetachedTemplatePrototype");
            detachedPrototypeObject.transform.position = Vector3.one * 1000f;
            detachedPrototypeObject.AddComponent<BoxCollider>().isTrigger = true;
            Rigidbody rigidbody = detachedPrototypeObject.AddComponent<Rigidbody>();
            rigidbody.useGravity = false;
            rigidbody.isKinematic = true;
            BossDetachedHitVolume detachedPrototype = detachedPrototypeObject.AddComponent<BossDetachedHitVolume>();
            BossDetachedAttackEmitter emitter = boss.AddComponent<BossDetachedAttackEmitter>();
            BossAttackTimelineAsset timeline = null;
            BossDetachedHitVolume spawned = null;
            GameObject visualInstance = null;
            try
            {
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_DetachedTemplate",
                    out timeline,
                    hitNodeIds: new[] { "SwordAura_1" },
                    sourcePart: BossAttackSourcePart.Detached);
                BossDetachedAttackBinding binding = new BossDetachedAttackBinding
                {
                    ActionId = attack.AttackId,
                    HitNodeId = attack.HitNodes[0].HitNodeId,
                    Prefab = detachedPrototype,
                    VisualTemplate = visualTemplate,
                    VisualTravelDriver = pivotParticles,
                    SpawnAnchor = boss.transform,
                    LocalEulerAngles = new Vector3(0f, 0f, 27f),
                    LocalScale = Vector3.one,
                    DirectionMode = BossDetachedDirectionMode.BossHorizontalForward,
                    ExecutionMode = BossDetachedExecutionMode.TriggerVolume,
                    Speed = 12f,
                    Lifetime = 2.2f
                };
                SetPrivateField(emitter, "bindings", new[] { binding });
                boss.SetActive(true);
                BossCombatSystem system = CreateStartedCombatSystem(1701);

                emitter.BeginAttack();
                emitter.SpawnForHitNode(
                    attack,
                    1701,
                    attack.HitNodes[0],
                    boss.transform,
                    emitter,
                    actor,
                    CombatTeam.Boss,
                    system,
                    ~0,
                    8,
                    false);

                BossDetachedHitVolume[] volumes = Object.FindObjectsByType<BossDetachedHitVolume>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                for (int i = 0; i < volumes.Length; i++)
                {
                    if (volumes[i] != detachedPrototype)
                    {
                        spawned = volumes[i];
                        break;
                    }
                }

                Assert.That(visualTemplate.activeSelf, Is.False);
                Assert.That(spawned, Is.Not.Null);
                Quaternion expectedRotation = Quaternion.LookRotation(boss.transform.forward, Vector3.up) * Quaternion.Euler(0f, 0f, 27f);
                Assert.That(Quaternion.Angle(spawned.transform.rotation, expectedRotation), Is.LessThan(0.01f));
                ParticleSystem[] allParticleSystems = Object.FindObjectsByType<ParticleSystem>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None);
                for (int i = 0; i < allParticleSystems.Length; i++)
                {
                    if (allParticleSystems[i].gameObject.name == visualTemplate.name + "(Clone)")
                    {
                        visualInstance = allParticleSystems[i].gameObject;
                        break;
                    }
                }

                Assert.That(visualInstance, Is.Not.Null);
                Assert.That(visualInstance.transform.parent, Is.Null);
                Assert.That(spawned.transform.Find(visualTemplate.name + "(Clone)"), Is.Null);
                Assert.That(visualInstance.activeSelf, Is.True);
                Vector3 expectedVisualPosition = spawned.transform.TransformPoint(visualTemplate.transform.localPosition);
                Quaternion expectedVisualRotation = spawned.transform.rotation * visualTemplate.transform.localRotation;
                Vector3 expectedVisualScale = Vector3.Scale(spawned.transform.lossyScale, visualTemplate.transform.localScale);
                Assert.That(Vector3.Distance(visualInstance.transform.position, expectedVisualPosition), Is.LessThan(0.0001f));
                Assert.That(Quaternion.Angle(visualInstance.transform.rotation, expectedVisualRotation), Is.LessThan(0.01f));
                Assert.That(Vector3.Distance(visualInstance.transform.localScale, expectedVisualScale), Is.LessThan(0.0001f));

                ParticleSystem[] particleSystems = visualInstance.GetComponentsInChildren<ParticleSystem>(true);
                Assert.That(particleSystems.Length, Is.EqualTo(3));
                for (int i = 0; i < particleSystems.Length; i++)
                {
                    Assert.That(particleSystems[i].isPlaying, Is.True);
                }

                ParticleSystem clonedPivot = visualInstance.transform.Find("Pivot").GetComponent<ParticleSystem>();
                ParticleSystem clonedSlash = visualInstance.transform.Find("Pivot/Slashes").GetComponent<ParticleSystem>();
                Assert.That(clonedPivot.main.startSpeed.constant, Is.EqualTo(24f).Within(0.001f));
                Assert.That(pivotParticles.main.startSpeed.constant, Is.EqualTo(30f).Within(0.001f));
                Assert.That(clonedSlash.main.startLifetime.constant, Is.EqualTo(0.3f).Within(0.001f));
                Vector3 velocity = (Vector3)typeof(BossDetachedHitVolume)
                    .GetField("velocity", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(spawned);
                float remainingLifetime = (float)typeof(BossDetachedHitVolume)
                    .GetField("remainingLifetime", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(spawned);
                Assert.That(Vector3.Distance(velocity, boss.transform.forward * 12f), Is.LessThan(0.0001f));
                Assert.That(remainingLifetime, Is.EqualTo(2.2f).Within(0.001f));
            }
            finally
            {
                DestroyTestObjects(visualInstance, spawned, detachedPrototypeObject, boss, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_ReceivesHitThroughPlayerCombatReceiver()
        {
            GameObject boss = CreateBoss("BossCombatSystemReceiveBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemReceiveTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Receive", out timeline);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1001);

                Assert.That(result.HasHit, Is.True);
                Assert.That(result.HitOutcome, Is.EqualTo(CombatHitOutcome.HitReaction));
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_RequiresReceiver()
        {
            GameObject boss = CreateBoss("BossCombatSystemRequireBoss", out BossAttackExecutor executor, out _);
            GameObject receiverOnly = CreatePlayerTarget("BossCombatSystemReceiverOnlyTarget", new Vector3(0f, 0f, 1f), true, false);
            GameObject hurtboxOnly = CreatePhysicsTarget("BossCombatSystemHurtboxOnlyTarget", new Vector3(0.4f, 0f, 1f));
            hurtboxOnly.AddComponent<CombatHurtbox>();
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon, new Vector3(2f, 2f, 2f));
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Require", out timeline);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1002);

                Assert.That(result.HasHit, Is.True);
                Assert.That(receiverOnly.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, receiverOnly, hurtboxOnly, timeline);
            }
        }

        [TestCase(true)]
        [TestCase(false)]
        public void BossCombatSystem_ProcessHitNode_SkipsInactiveOrDisabledAnchor(bool disableCollider)
        {
            GameObject boss = CreateBoss("BossCombatSystemInactiveAnchorBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemInactiveAnchorTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                if (disableCollider)
                {
                    anchor.BoxCollider.enabled = false;
                }
                else
                {
                    anchor.gameObject.SetActive(false);
                }

                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_InactiveAnchor", out timeline);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1011);

                Assert.That(result.HasHit, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_DedupesByAttackHitNodeAndTarget()
        {
            GameObject boss = CreateBoss("BossCombatSystemDedupeBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemDedupeTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Dedupe", out timeline);
                BossCombatSystem system = CreateStartedCombatSystem(1003);
                BossCombatProcessResult first = ProcessFirstHitNode(system, executor, attack, anchor, 1003);
                BossCombatProcessResult second = ProcessFirstHitNode(system, executor, attack, anchor, 1003);

                Assert.That(first.HasHit, Is.True);
                Assert.That(second.HasHit, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_DedupesByReceiverWhenHurtboxMissing()
        {
            GameObject boss = CreateBoss("BossCombatSystemReceiverDedupeBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemReceiverDedupeTarget", new Vector3(0f, 0f, 1f), true, false);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_ReceiverDedupe", out timeline);
                BossCombatSystem system = CreateStartedCombatSystem(1010);
                BossCombatProcessResult first = ProcessFirstHitNode(system, executor, attack, anchor, 1010);
                BossCombatProcessResult second = ProcessFirstHitNode(system, executor, attack, anchor, 1010);

                Assert.That(first.HasHit, Is.True);
                Assert.That(second.HasHit, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_AllowsDifferentHitNodesForSameHurtbox()
        {
            GameObject boss = CreateBoss("BossCombatSystemDifferentNodesBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemDifferentNodesTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_DifferentNodes",
                    out timeline,
                    hitNodeIds: new[] { "Hit_A", "Hit_B" });
                BossCombatSystem system = CreateStartedCombatSystem(1004);

                BossCombatProcessResult first = ProcessHitNode(system, executor, attack, attack.HitNodes[0], anchor, 1004);
                BossCombatProcessResult second = ProcessHitNode(system, executor, attack, attack.HitNodes[1], anchor, 1004);

                Assert.That(first.HasHit, Is.True);
                Assert.That(second.HasHit, Is.True);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(280f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_AllowsDifferentHurtboxesForSameHitNode()
        {
            GameObject boss = CreateBoss("BossCombatSystemDifferentHurtboxesBoss", out BossAttackExecutor executor, out _);
            GameObject firstTarget = CreatePlayerTarget("BossCombatSystemDifferentHurtboxesA", new Vector3(-0.35f, 0f, 1f), true, true);
            GameObject secondTarget = CreatePlayerTarget("BossCombatSystemDifferentHurtboxesB", new Vector3(0.35f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon, new Vector3(3f, 2f, 2f));
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_DifferentHurtboxes", out timeline);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1005);

                Assert.That(result.HasHit, Is.True);
                Assert.That(firstTarget.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
                Assert.That(secondTarget.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, firstTarget, secondTarget, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_FiltersOutsideEffectiveRange()
        {
            GameObject boss = CreateBoss("BossCombatSystemRangeBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemRangeTarget", new Vector3(0f, 0f, 3f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon, new Vector3(8f, 2f, 8f));
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Range", out timeline, effectiveRange: 1f);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1006);

                Assert.That(result.HasHit, Is.False);
                Assert.That(result.HasSemanticResult, Is.True);
                Assert.That(result.SemanticAccepted, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_FiltersOutsideEffectiveAngle()
        {
            GameObject boss = CreateBoss("BossCombatSystemAngleBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemAngleTarget", new Vector3(0f, 0f, -1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon, new Vector3(4f, 2f, 4f));
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Angle", out timeline, effectiveAngle: 45f);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1007);

                Assert.That(result.HasHit, Is.False);
                Assert.That(result.HasSemanticResult, Is.True);
                Assert.That(result.SemanticAccepted, Is.False);
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(300f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_TriggersPerfectGuardBossStagger()
        {
            GameObject boss = CreateBoss("BossCombatSystemPerfectGuardBoss", out BossAttackExecutor executor, out BossActor controller);
            GameObject target = CreatePlayerTarget("BossCombatSystemPerfectGuardTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset attackTimeline = null;
            BossReactionTimelineAsset reactionTimeline = null;
            try
            {
                RegisterBossHitStaggerTimeline(out reactionTimeline);
                PlayerStateMachine player = target.GetComponent<PlayerStateMachine>();
                player.Context.SetState(PlayerStateId.Guard);
                player.Context.IsGuardBlockActive = true;
                player.Context.IsPerfectGuardWindow = true;
                player.Context.Resources.BetaEnergy = 20f;

                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_PerfectGuard",
                    out attackTimeline,
                    triggersPerfectGuardBossStagger: true);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1008);

                Assert.That(result.HasHit, Is.True);
                Assert.That(result.HitOutcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
                Assert.That(player.Context.Resources.CurrentHp, Is.EqualTo(300f));
                Assert.That(player.Context.Resources.BetaEnergy, Is.EqualTo(22f));
                Assert.That(player.Context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.PerfectGuard));
                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.HitStagger));
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(19));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                DestroyTestObjects(boss, target, attackTimeline, reactionTimeline);
            }
        }

        [Test]
        public void BossCombatSystem_ProcessHitNode_PerfectGuardWithoutStaggerFlagDoesNotStaggerBoss()
        {
            GameObject boss = CreateBoss("BossCombatSystemPerfectGuardNoStaggerBoss", out BossAttackExecutor executor, out BossActor controller);
            GameObject target = CreatePlayerTarget("BossCombatSystemPerfectGuardNoStaggerTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset attackTimeline = null;
            BossReactionTimelineAsset reactionTimeline = null;
            try
            {
                RegisterBossHitStaggerTimeline(out reactionTimeline);
                PlayerStateMachine player = target.GetComponent<PlayerStateMachine>();
                player.Context.SetState(PlayerStateId.Guard);
                player.Context.IsGuardBlockActive = true;
                player.Context.IsPerfectGuardWindow = true;
                player.Context.Resources.BetaEnergy = 20f;

                BossAttackHitboxAnchor anchor = CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition(
                    "CombatSystem_PerfectGuardNoStagger",
                    out attackTimeline,
                    triggersPerfectGuardBossStagger: false);
                BossCombatProcessResult result = ProcessFirstHitNode(executor, attack, anchor, 1011);

                Assert.That(result.HasHit, Is.True);
                Assert.That(result.HitOutcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
                Assert.That(player.Context.Resources.CurrentHp, Is.EqualTo(300f));
                Assert.That(player.Context.Resources.BetaEnergy, Is.EqualTo(22f));
                Assert.That(player.Context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.PerfectGuard));
                Assert.That(controller.CurrentState, Is.EqualTo(BossStateId.None));
                Assert.That(controller.LastPlayerHitRejectReason, Is.EqualTo("PerfectGuardHitNodeDoesNotStaggerBoss"));
                Assert.That(controller.CurrentShieldDefense, Is.EqualTo(19));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                DestroyTestObjects(boss, target, attackTimeline, reactionTimeline);
            }
        }

        [Test]
        public void BossAttackExecutor_Tick_DelegatesFormalHitProcessingToBossCombatSystem()
        {
            GameObject boss = CreateBoss("BossCombatSystemExecutorBoss", out BossAttackExecutor executor, out _);
            GameObject target = CreatePlayerTarget("BossCombatSystemExecutorTarget", new Vector3(0f, 0f, 1f), true, true);
            BossAttackTimelineAsset timeline = null;
            try
            {
                CreateHitboxAnchor(boss, BossAttackSourcePart.Weapon);
                BossAttackDefinition attack = CreateAttackDefinition("CombatSystem_Executor", out timeline);

                executor.BeginAttack(attack, 1009, target.transform);
                bool finished = executor.Tick(0.1f);

                Assert.That(finished, Is.False);
                Assert.That(executor.HasLastHit, Is.True);
                Assert.That(executor.LastOutcome, Is.EqualTo(CombatHitOutcome.HitReaction));
                Assert.That(target.GetComponent<PlayerStateMachine>().Context.Resources.CurrentHp, Is.EqualTo(290f));
            }
            finally
            {
                DestroyTestObjects(boss, target, timeline);
            }
        }

        [Test]
        public void BossAttackExecutor_FixedSlashTrailsRouteBySourcePart()
        {
            GameObject boss = CreateBoss("BossFixedSlashTrailRoutingBoss", out BossAttackExecutor executor, out _);
            TestTrailSampler weaponSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler leftFootSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler rightFootSampler = boss.AddComponent<TestTrailSampler>();
            BossAttackTimelineAsset weaponTimeline = null;
            BossAttackTimelineAsset leftTimeline = null;
            BossAttackTimelineAsset rightTimeline = null;
            BossAttackTimelineAsset detachedTimeline = null;
            try
            {
                ConfigureFixedSlashTrails(executor, weaponSampler, leftFootSampler, rightFootSampler);

                BossAttackDefinition weaponAttack = CreateAttackDefinition(
                    "Trail_Weapon",
                    out weaponTimeline,
                    sourcePart: BossAttackSourcePart.Weapon);
                executor.BeginAttack(weaponAttack, 1101, null);
                executor.Tick(0.1f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, true, false, false);
                executor.EndAttack();

                BossAttackDefinition leftAttack = CreateAttackDefinition(
                    "Trail_LeftFoot",
                    out leftTimeline,
                    sourcePart: BossAttackSourcePart.LeftFoot);
                executor.BeginAttack(leftAttack, 1102, null);
                executor.Tick(0.1f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, true, false);
                executor.EndAttack();

                BossAttackDefinition rightAttack = CreateAttackDefinition(
                    "Trail_RightFoot",
                    out rightTimeline,
                    sourcePart: BossAttackSourcePart.RightFoot);
                executor.BeginAttack(rightAttack, 1103, null);
                executor.Tick(0.1f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, false, true);
                executor.EndAttack();

                BossAttackDefinition detachedAttack = CreateAttackDefinition(
                    "Trail_Detached",
                    out detachedTimeline,
                    sourcePart: BossAttackSourcePart.Detached);
                executor.BeginAttack(detachedAttack, 1104, null);
                executor.Tick(0.1f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, false, false);
            }
            finally
            {
                DestroyTestObjects(boss, weaponTimeline, leftTimeline, rightTimeline, detachedTimeline);
            }
        }

        [Test]
        public void BossAttackExecutor_FixedSlashTrailsAggregateSameFrameAndStopAfterWindow()
        {
            GameObject boss = CreateBoss("BossFixedSlashTrailAggregateBoss", out BossAttackExecutor executor, out _);
            TestTrailSampler weaponSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler leftFootSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler rightFootSampler = boss.AddComponent<TestTrailSampler>();
            BossAttackTimelineAsset timeline = null;
            try
            {
                ConfigureFixedSlashTrails(executor, weaponSampler, leftFootSampler, rightFootSampler);
                BossAttackDefinition attack = CreateAttackDefinitionWithSourceParts(
                    "Trail_Aggregate",
                    out timeline,
                    BossAttackSourcePart.Weapon,
                    BossAttackSourcePart.LeftFoot);

                executor.BeginAttack(attack, 1105, null);
                executor.Tick(0.1f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, true, true, false);

                executor.Tick(0.5f);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, false, false);

                weaponSampler.enabled = true;
                leftFootSampler.enabled = true;
                rightFootSampler.enabled = true;
                executor.EndAttack();
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, false, false);
            }
            finally
            {
                DestroyTestObjects(boss, timeline);
            }
        }

        [Test]
        public void BossAttackExecutor_ParticleBoundHitNodeSkipsItsFixedSlashTrail()
        {
            GameObject boss = CreateBoss("BossParticleTrailExclusionBoss", out BossAttackExecutor executor, out _);
            TestTrailSampler weaponSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler leftFootSampler = boss.AddComponent<TestTrailSampler>();
            TestTrailSampler rightFootSampler = boss.AddComponent<TestTrailSampler>();
            BossHitNodeParticleVfxController particleVfx = boss.AddComponent<BossHitNodeParticleVfxController>();
            GameObject effectPrefab = new GameObject("ParticleTrailExclusionEffect");
            effectPrefab.AddComponent<ParticleSystem>();
            BossAttackTimelineAsset timeline = null;
            GameObject runtimeEffect = null;
            try
            {
                ConfigureFixedSlashTrails(executor, weaponSampler, leftFootSampler, rightFootSampler);
                BossAttackDefinition attack = CreateAttackDefinitionWithSourceParts(
                    "Trail_ParticleExclusion",
                    out timeline,
                    BossAttackSourcePart.Weapon,
                    BossAttackSourcePart.LeftFoot);
                particleVfx.BindEffectForHitNode(
                    timeline,
                    "Hit_Weapon_0",
                    effectPrefab,
                    boss.transform,
                    BossHitNodeParticleDirectionMode.BossHorizontalForward,
                    Vector3.zero,
                    Vector3.zero,
                    Vector3.one,
                    2.2f);

                executor.BeginAttack(attack, 1106, null);
                executor.Tick(0.1f);
                runtimeEffect = GameObject.Find(effectPrefab.name + "_Runtime");

                Assert.That(runtimeEffect, Is.Not.Null);
                AssertTrailStates(weaponSampler, leftFootSampler, rightFootSampler, false, true, false);
            }
            finally
            {
                DestroyTestObjects(runtimeEffect, boss, effectPrefab, timeline);
            }
        }

        private static BossCombatSystem CreateStartedCombatSystem(int attackInstanceId)
        {
            BossCombatSystem system = new BossCombatSystem();
            system.BeginAttack(attackInstanceId);
            return system;
        }

        private static BossCombatProcessResult ProcessFirstHitNode(
            BossAttackExecutor executor,
            BossAttackDefinition attack,
            BossAttackHitboxAnchor anchor,
            int attackInstanceId)
        {
            return ProcessFirstHitNode(CreateStartedCombatSystem(attackInstanceId), executor, attack, anchor, attackInstanceId);
        }

        private static BossCombatProcessResult ProcessFirstHitNode(
            BossCombatSystem system,
            BossAttackExecutor executor,
            BossAttackDefinition attack,
            BossAttackHitboxAnchor anchor,
            int attackInstanceId)
        {
            return ProcessHitNode(system, executor, attack, attack.HitNodes[0], anchor, attackInstanceId);
        }

        private static BossCombatProcessResult ProcessHitNode(
            BossCombatSystem system,
            BossAttackExecutor executor,
            BossAttackDefinition attack,
            CombatHitNodeData hitNode,
            BossAttackHitboxAnchor anchor,
            int attackInstanceId)
        {
            Physics.SyncTransforms();
            return system.ProcessHitNode(new BossCombatExecutionRequest(
                attack,
                attackInstanceId,
                hitNode,
                new[] { anchor },
                executor.transform,
                executor,
                executor.GetComponent<BossActor>(),
                CombatTeam.Boss,
                ~0,
                24,
                false,
                () => true));
        }

        private static GameObject CreateBoss(string name, out BossAttackExecutor executor, out BossActor controller)
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
            boss.AddComponent<BossAnimationBridge>();
            boss.AddComponent<BossMotionController>();
            executor = boss.AddComponent<BossAttackExecutor>();
            controller = boss.AddComponent<BossActor>();
            ConfigureFixedSlashTrails(
                executor,
                boss.AddComponent<TestTrailSampler>(),
                boss.AddComponent<TestTrailSampler>(),
                boss.AddComponent<TestTrailSampler>());
            SetPrivateField(executor, "logHits", false);
            return boss;
        }

        private static GameObject CreatePlayerTarget(string name, Vector3 position, bool withReceiver, bool withHurtbox)
        {
            GameObject target = CreatePhysicsTarget(name, position);
            PlayerStateMachine player = target.AddComponent<PlayerStateMachine>();
            player.Context.IsActionUninterruptible = true;
            if (withReceiver)
            {
                PlayerCombatReceiver receiver = target.AddComponent<PlayerCombatReceiver>();
                SetPrivateField(receiver, "logHits", false);
                SetPrivateField(receiver, "backGuardAllowed", true);
            }

            if (withHurtbox)
            {
                target.AddComponent<CombatHurtbox>();
            }

            return target;
        }

        private static GameObject CreatePhysicsTarget(string name, Vector3 position)
        {
            GameObject target = new GameObject(name);
            target.transform.position = position;
            BoxCollider collider = target.AddComponent<BoxCollider>();
            collider.size = Vector3.one * 0.5f;
            return target;
        }

        private static BossAttackHitboxAnchor CreateHitboxAnchor(
            GameObject boss,
            BossAttackSourcePart sourcePart,
            Vector3? size = null)
        {
            GameObject anchorObject = new GameObject("Hitbox_" + sourcePart);
            anchorObject.transform.SetParent(boss.transform, false);
            BoxCollider boxCollider = anchorObject.AddComponent<BoxCollider>();
            boxCollider.size = size ?? new Vector3(2f, 2f, 2f);
            BossAttackHitboxAnchor anchor = anchorObject.AddComponent<BossAttackHitboxAnchor>();
            SetPrivateField(anchor, "sourcePart", sourcePart);
            SetPrivateField(anchor, "boxCollider", boxCollider);
            return anchor;
        }

        private static BossAttackDefinition CreateAttackDefinition(
            string attackId,
            out BossAttackTimelineAsset timeline,
            float effectiveRange = 0f,
            float effectiveAngle = 0f,
            bool triggersPerfectGuardBossStagger = false,
            string[] hitNodeIds = null,
            BossAttackSourcePart sourcePart = BossAttackSourcePart.Weapon)
        {
            string[] nodeIds = hitNodeIds ?? new[] { "Hit_Weapon" };
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

            CombatTimelineClip[] clips = new CombatTimelineClip[nodeIds.Length];
            for (int i = 0; i < nodeIds.Length; i++)
            {
                clips[i] = new CombatHitNodeClip
                {
                    Name = nodeIds[i],
                    CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                    StartTime = 0f,
                    EndTime = 0.5f,
                    Damage = 10f,
                    PoiseDamage = 2f,
                    GuardDamage = 5f,
                    MaxHitsPerTarget = 1,
                    EffectiveRange = effectiveRange,
                    EffectiveAngle = effectiveAngle,
                    SourcePart = sourcePart,
                    ReactionIntent = CombatReactionIntent.HitReaction,
                    CanBeGuarded = true,
                    CanBePerfectGuarded = true,
                    CanBePerfectEvaded = true,
                    TriggersPerfectGuardBossStagger = triggersPerfectGuardBossStagger
                };
            }

            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = clips
                }
            });
            return timeline.ToBossAttackDefinition();
        }

        /// <summary>
        /// 创建同一激活窗口内含多个不同 SourcePart 的最小攻击，用于验证固定拖尾帧聚合。
        /// </summary>
        /// <param name="attackId">测试攻击及 Timeline 的唯一 ID。</param>
        /// <param name="timeline">写回本测试创建、需要由调用方销毁的 Timeline。</param>
        /// <param name="sourceParts">依次写入各 HitNode 的来源部位。</param>
        /// <returns>由临时 Timeline 转换出的 Boss 攻击定义。</returns>
        private static BossAttackDefinition CreateAttackDefinitionWithSourceParts(
            string attackId,
            out BossAttackTimelineAsset timeline,
            params BossAttackSourcePart[] sourceParts)
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
            timeline.ConfigureBossAttack(90f, 0.1f, 1f, 0.1f);

            CombatTimelineClip[] clips = new CombatTimelineClip[sourceParts.Length];
            for (int i = 0; i < sourceParts.Length; i++)
            {
                clips[i] = new CombatHitNodeClip
                {
                    Name = $"Hit_{sourceParts[i]}_{i}",
                    CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                    StartTime = 0f,
                    EndTime = 0.5f,
                    Damage = 10f,
                    MaxHitsPerTarget = 1,
                    SourcePart = sourceParts[i],
                    ReactionIntent = CombatReactionIntent.HitReaction
                };
            }

            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = clips
                }
            });
            return timeline.ToBossAttackDefinition();
        }

        /// <summary>
        /// 为测试执行器写入三个固定拖尾采样器，并确保初始状态全部关闭。
        /// </summary>
        /// <param name="executor">需要配置固定拖尾引用的攻击执行器。</param>
        /// <param name="weaponSampler">Weapon HitNode 使用的测试采样器。</param>
        /// <param name="leftFootSampler">LeftFoot HitNode 使用的测试采样器。</param>
        /// <param name="rightFootSampler">RightFoot HitNode 使用的测试采样器。</param>
        private static void ConfigureFixedSlashTrails(
            BossAttackExecutor executor,
            Behaviour weaponSampler,
            Behaviour leftFootSampler,
            Behaviour rightFootSampler)
        {
            weaponSampler.enabled = false;
            leftFootSampler.enabled = false;
            rightFootSampler.enabled = false;
            SetPrivateField(executor, "weaponSlashTrailSampler", weaponSampler);
            SetPrivateField(executor, "leftFootSlashTrailSampler", leftFootSampler);
            SetPrivateField(executor, "rightFootSlashTrailSampler", rightFootSampler);
        }

        /// <summary>
        /// 断言三个固定拖尾采样器的启停状态。
        /// </summary>
        /// <param name="weaponSampler">Weapon 固定拖尾采样器。</param>
        /// <param name="leftFootSampler">LeftFoot 固定拖尾采样器。</param>
        /// <param name="rightFootSampler">RightFoot 固定拖尾采样器。</param>
        /// <param name="weaponEnabled">期望的 Weapon 采样状态。</param>
        /// <param name="leftFootEnabled">期望的 LeftFoot 采样状态。</param>
        /// <param name="rightFootEnabled">期望的 RightFoot 采样状态。</param>
        private static void AssertTrailStates(
            Behaviour weaponSampler,
            Behaviour leftFootSampler,
            Behaviour rightFootSampler,
            bool weaponEnabled,
            bool leftFootEnabled,
            bool rightFootEnabled)
        {
            Assert.That(weaponSampler.enabled, Is.EqualTo(weaponEnabled));
            Assert.That(leftFootSampler.enabled, Is.EqualTo(leftFootEnabled));
            Assert.That(rightFootSampler.enabled, Is.EqualTo(rightFootEnabled));
        }

        private static void RegisterBossHitStaggerTimeline(out BossReactionTimelineAsset timeline)
        {
            CombatTimelineProvider.ResetCache();
            timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                "Boss_HitStagger",
                "Boss_HitStagger",
                "Anim_Boss_HitStagger",
                0.5f,
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

            Dictionary<string, CombatTimelineActionAsset> timelines =
                (Dictionary<string, CombatTimelineActionAsset>)typeof(CombatTimelineProvider)
                    .GetField("timelineById", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            timelines[timeline.ActionId] = timeline;
            typeof(CombatTimelineProvider)
                .GetField("initialized", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        /// <summary>
        /// 销毁当前测试创建的临时对象；传入组件时销毁其所属 GameObject，避免同对象上的碰撞体或刚体残留到场景。
        /// </summary>
        /// <param name="objects">需要在测试收尾阶段立即销毁的 GameObject、Component 或 ScriptableObject。</param>
        private static void DestroyTestObjects(params Object[] objects)
        {
            for (int i = 0; i < objects.Length; i++)
            {
                Object testObject = objects[i];
                if (testObject == null)
                {
                    continue;
                }

                if (testObject is Component component)
                {
                    Object.DestroyImmediate(component.gameObject);
                    continue;
                }

                Object.DestroyImmediate(testObject);
            }
        }

        private sealed class TestTrailSampler : MonoBehaviour
        {
        }
    }
}
