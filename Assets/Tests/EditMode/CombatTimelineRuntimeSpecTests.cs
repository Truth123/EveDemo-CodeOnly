// 文件说明：验证 Combat Timeline RuntimeSpec、PlayerMotion 目标吸附和 Validation 的基础行为。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中构造临时 Timeline 资产，不影响正式场景。

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Input;
using ProjectEVE.Player;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using ProjectEVE.Player.States;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class CombatTimelineRuntimeSpecTests
    {
        [Test]
        public void PlayerStateContext_NormalAttackHitsGrantOneBetaEveryTwoUniqueInstances()
        {
            PlayerStateContext context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 0f,
                MaxBetaEnergy = 32f
            });

            Assert.That(context.RecordNormalAttackHit(101), Is.True);
            Assert.That(context.RecordNormalAttackHit(101), Is.False);
            Assert.That(context.Resources.BetaEnergy, Is.EqualTo(0f));
            Assert.That(context.ConsecutiveNormalAttackHitCount, Is.EqualTo(1));

            Assert.That(context.RecordNormalAttackHit(102), Is.True);
            Assert.That(context.Resources.BetaEnergy, Is.EqualTo(1f));
            Assert.That(context.ConsecutiveNormalAttackHitCount, Is.EqualTo(0));
        }

        [Test]
        public void PlayerStateContext_WhiffClearsPendingNormalHitAndBetaClampsAt32()
        {
            PlayerStateContext context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 31f,
                MaxBetaEnergy = 32f
            });

            context.RecordNormalAttackHit(201);
            context.ResetNormalAttackHitSequenceOnWhiff();
            context.RecordNormalAttackHit(202);
            context.RecordNormalAttackHit(203);
            context.AddBetaEnergy(20f);

            Assert.That(context.Resources.BetaEnergy, Is.EqualTo(32f));
            Assert.That(context.ConsecutiveNormalAttackHitCount, Is.EqualTo(0));
            Assert.That(context.TrySpendBetaEnergy(8f), Is.True);
            Assert.That(context.Resources.BetaEnergy, Is.EqualTo(24f));
            context.Resources.BetaEnergy = 7f;
            Assert.That(context.TrySpendBetaEnergy(8f), Is.False);
            Assert.That(context.Resources.BetaEnergy, Is.EqualTo(7f));
        }

        [Test]
        public void PlayerCombatReceiver_PerfectDefenseRewardsTwoBetaAndGuardHitDoesNotBreakShieldResource()
        {
            GameObject player = new GameObject("PlayerBetaDefenseRule");
            try
            {
                PlayerStateMachine stateMachine = player.AddComponent<PlayerStateMachine>();
                PlayerCombatReceiver receiver = player.AddComponent<PlayerCombatReceiver>();
                CombatHitData hit = CreateHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction);
                hit.AttackerTeam = CombatTeam.Boss;

                stateMachine.Context.SetState(PlayerStateId.Guard);
                stateMachine.Context.IsGuardBlockActive = true;
                stateMachine.Context.IsPerfectGuardWindow = true;
                CombatHitResult perfectGuard = receiver.ReceiveHit(hit);
                Assert.That(perfectGuard.Outcome, Is.EqualTo(CombatHitOutcome.PerfectGuard));
                Assert.That(stateMachine.Context.Resources.BetaEnergy, Is.EqualTo(2f));

                stateMachine.Context.SetState(PlayerStateId.Evade);
                stateMachine.Context.IsPerfectGuardWindow = false;
                stateMachine.Context.IsEvadeInvincible = true;
                stateMachine.Context.IsPerfectEvadeWindow = true;
                CombatHitResult perfectEvade = receiver.ReceiveHit(hit);
                Assert.That(perfectEvade.Outcome, Is.EqualTo(CombatHitOutcome.PerfectEvade));
                Assert.That(stateMachine.Context.Resources.BetaEnergy, Is.EqualTo(4f));

                stateMachine.Context.SetState(PlayerStateId.Guard);
                stateMachine.Context.IsEvadeInvincible = false;
                stateMachine.Context.IsPerfectEvadeWindow = false;
                stateMachine.Context.IsGuardBlockActive = true;
                CombatHitResult guardHit = receiver.ReceiveHit(hit);
                Assert.That(guardHit.Outcome, Is.EqualTo(CombatHitOutcome.GuardHit));
                Assert.That(stateMachine.Context.CurrentState, Is.EqualTo(PlayerStateId.Guard));
                Assert.That(stateMachine.Context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.GuardHit));
                Assert.That(guardHit.AppliedHpDamage, Is.Zero);
                Assert.That(stateMachine.Context.Resources.CurrentHp, Is.EqualTo(300f).Within(0.001f));
            }
            finally
            {
                Object.DestroyImmediate(player);
            }
        }

        [Test]
        public void CombatHitResolver_MapsHitReactionIntentToHitReaction()
        {
            CombatHitResult result = CombatHitResolver.Resolve(
                CreateHit(CombatAttackType.LightAttack, CombatReactionIntent.HitReaction),
                CreateDefender());

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.HitReaction));
            Assert.That(result.AppliedHpDamage, Is.EqualTo(10f));
        }

        [Test]
        public void CombatHitResolver_MapsKnockdownIntentToKnockdown()
        {
            CombatHitResult result = CombatHitResolver.Resolve(
                CreateHit(CombatAttackType.LightAttack, CombatReactionIntent.Knockdown),
                CreateDefender());

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.Knockdown));
            Assert.That(result.AppliedHpDamage, Is.EqualTo(10f));
        }

        [Test]
        public void CombatHitResolver_DamageOnlyAppliesDamageWithoutReaction()
        {
            CombatHitResult result = CombatHitResolver.Resolve(
                CreateHit(CombatAttackType.SkillAttack, CombatReactionIntent.DamageOnly),
                CreateDefender());

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.DamageOnly));
            Assert.That(result.AppliedHpDamage, Is.EqualTo(10f));
        }

        [Test]
        public void CombatHitResolver_DoesNotInferKnockdownFromHeavyAttack()
        {
            CombatHitResult result = CombatHitResolver.Resolve(
                CreateHit(CombatAttackType.HeavyAttack, CombatReactionIntent.HitReaction),
                CreateDefender());

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.HitReaction));
        }

        [Test]
        public void CombatHitResolver_NoneIntentProducesNoHit()
        {
            CombatHitData hit = CreateHit(CombatAttackType.LightAttack, CombatReactionIntent.None);
            hit.Damage = 200f;

            CombatHitResult result = CombatHitResolver.Resolve(
                hit,
                CreateDefender());

            Assert.That(result.Outcome, Is.EqualTo(CombatHitOutcome.None));
            Assert.That(result.AppliedHpDamage, Is.EqualTo(0f));
        }

        [Test]
        public void RuntimeSpec_EvaluatesCapabilityAndActiveHitNodeIds()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                CombatActionCapabilitySnapshot active = spec.Evaluate(0.30f);
                CombatActionCapabilitySnapshot recovery = spec.Evaluate(0.70f);

                Assert.That(active.Phase, Is.EqualTo(PlayerStatePhase.Active));
                Assert.That(active.AttackHitboxActive, Is.True);
                Assert.That(active.ActiveHitNodeIds, Is.EqualTo(new[] { "Hit_A" }));
                Assert.That(recovery.AttackHitboxActive, Is.False);
                Assert.That(recovery.ActiveHitNodeIds, Is.Empty);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_TryGetEarliestWindow_ReturnsLowestStartTime()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                new CombatTimelineTrack("Additional Cancel", CombatTimelineTrackKind.Cancel)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        Cancel("Early Evade", CombatTimelineCapabilityId.Cancel_ToEvade, 0.1f, 0.2f)
                    }
                }
            }).ToArray());

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(
                    spec.TryGetEarliestWindow(
                        CombatTimelineCapabilityId.Cancel_ToEvade,
                        out ProjectEVE.Player.Windows.ActionWindow window),
                    Is.True);
                Assert.That(window.StartTime, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(window.EndTime, Is.EqualTo(0.2f).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_EvaluatesThreePlayerMotionWindowsIndependently()
        {
            PlayerSkillTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerSkillTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerSkill,
                "Test_SkillMotion",
                "Test Skill Motion",
                "Anim_Test_SkillMotion",
                2.5f,
                60f);
            timeline.ConfigureSkill(8f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Motion", CombatTimelineTrackKind.PlayerMotion)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        CreatePlayerMotion("Hit1 Magnet", 0.50f, 0.80f),
                        CreatePlayerMotion("Hit2 Magnet", 0.90f, 1.18f),
                        CreatePlayerMotion("Hit3 Magnet", 1.65f, 1.98f)
                    }
                }
            });

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(spec.Evaluate(0.49f).ActivePlayerMotionIds, Is.Empty);
                Assert.That(spec.Evaluate(0.65f).ActivePlayerMotionIds,
                    Is.EqualTo(new[] { PlayerActionMotionId.Skill1Forward }));
                Assert.That(spec.Evaluate(0.85f).ActivePlayerMotionIds, Is.Empty);
                Assert.That(spec.Evaluate(1.00f).ActivePlayerMotionIds,
                    Is.EqualTo(new[] { PlayerActionMotionId.Skill1Forward }));
                Assert.That(spec.Evaluate(1.40f).ActivePlayerMotionIds, Is.Empty);
                Assert.That(spec.Evaluate(1.80f).ActivePlayerMotionIds,
                    Is.EqualTo(new[] { PlayerActionMotionId.Skill1Forward }));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void PlayerActionMotionRuntime_TargetMagnetStopsAtConfiguredDistance()
        {
            PlayerActionMotionConfig config = CreateTargetMagnetConfig();
            PlayerActionMotionRuntime runtime = new PlayerActionMotionRuntime();
            Vector3 playerPosition = Vector3.zero;
            Vector3 targetPosition = new Vector3(0f, 0f, 2.5f);

            Assert.That(runtime.BeginTargetMagnet(config, playerPosition, targetPosition, Vector3.forward), Is.True);

            Vector3 delta = runtime.Tick(0.28f, playerPosition, targetPosition, Vector3.forward, true);

            Assert.That(delta.z, Is.EqualTo(1.35f).Within(0.001f));
            Assert.That(Vector3.Distance(playerPosition + delta, targetPosition), Is.EqualTo(1.15f).Within(0.001f));
        }

        [Test]
        public void PlayerActionMotionRuntime_TargetMagnetTracksMovingTargetAndStopsWhenTargetLeavesLimits()
        {
            PlayerActionMotionConfig config = CreateTargetMagnetConfig();
            PlayerActionMotionRuntime runtime = new PlayerActionMotionRuntime();

            Assert.That(runtime.BeginTargetMagnet(config, Vector3.zero, new Vector3(0f, 0f, 2.5f), Vector3.forward), Is.True);
            Vector3 firstDelta = runtime.Tick(0.08f, Vector3.zero, new Vector3(0f, 0f, 2.5f), Vector3.forward, true);
            Vector3 secondDelta = runtime.Tick(
                0.08f,
                firstDelta,
                new Vector3(0.8f, 0f, 2.5f),
                Vector3.forward,
                true);

            Assert.That(secondDelta.x, Is.GreaterThan(0f));

            Vector3 rejectedDelta = runtime.Tick(
                0.02f,
                firstDelta + secondDelta,
                new Vector3(0f, 0f, -2f),
                Vector3.forward,
                true);

            Assert.That(rejectedDelta, Is.EqualTo(Vector3.zero));
            Assert.That(runtime.IsActive, Is.False);
        }

        [Test]
        public void PlayerActionMotionRuntime_TargetMagnetRejectsTargetsOutsideDistanceOrAngle()
        {
            PlayerActionMotionConfig config = CreateTargetMagnetConfig();
            PlayerActionMotionRuntime runtime = new PlayerActionMotionRuntime();
            Vector3 outsideAngle = Quaternion.AngleAxis(61f, Vector3.up) * Vector3.forward * 2f;

            Assert.That(
                runtime.BeginTargetMagnet(config, Vector3.zero, new Vector3(0f, 0f, 3.01f), Vector3.forward),
                Is.False);
            Assert.That(
                runtime.BeginTargetMagnet(config, Vector3.zero, outsideAngle, Vector3.forward),
                Is.False);
        }

        [Test]
        public void TimelineFrameModelChanges_KeepClipTimesAndRecalculateFrames()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            CombatTimelineClip clip = timeline.EnumerateAllClips().First(item => item.Name == "Hit_A");
            float originalStartTime = clip.StartTime;
            float originalEndTime = clip.EndTime;
            int originalStartFrame = Mathf.RoundToInt(originalStartTime * timeline.FrameRate);
            int originalEndFrame = Mathf.RoundToInt(originalEndTime * timeline.FrameRate);

            try
            {
                timeline.SetFrameRatePreserveDuration(120f);

                Assert.That(clip.StartTime, Is.EqualTo(originalStartTime).Within(0.0001f));
                Assert.That(clip.EndTime, Is.EqualTo(originalEndTime).Within(0.0001f));
                Assert.That(Mathf.RoundToInt(clip.StartTime * timeline.FrameRate), Is.EqualTo(originalStartFrame * 2));
                Assert.That(Mathf.RoundToInt(clip.EndTime * timeline.FrameRate), Is.EqualTo(originalEndFrame * 2));

                timeline.SetTotalFrames(30);

                Assert.That(clip.StartTime, Is.EqualTo(originalStartTime).Within(0.0001f));
                Assert.That(clip.EndTime, Is.EqualTo(originalEndTime).Within(0.0001f));

                timeline.SetDurationFromSeconds(0.25f);

                Assert.That(clip.StartTime, Is.EqualTo(originalStartTime).Within(0.0001f));
                Assert.That(clip.EndTime, Is.EqualTo(originalEndTime).Within(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_EvaluatesBossReactionGateAllowWindow()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                CreateBossGateTrack(new BossReactionGateWindowClip
                {
                    Name = "BossGate_Allow",
                    StartTime = 0.5f,
                    EndTime = 0.8f,
                    Policy = BossReactionGatePolicy.AllowInterrupt,
                    BlockedOutcome = BossReactionGateBlockedOutcome.Any,
                    AttackTypes = new[] { CombatAttackType.LightAttack, CombatAttackType.HeavyAttack, CombatAttackType.SkillAttack }
                })
            }).ToArray());

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.LightAttack, CombatHitOutcome.HitReaction, 0.6f).Id,
                    Is.EqualTo(BossReactionGateResultId.Allowed));
                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.SkillAttack, CombatHitOutcome.Knockdown, 0.6f).Id,
                    Is.EqualTo(BossReactionGateResultId.Allowed));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_EvaluatesBossReactionGateBlockSkillKnockdown()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                CreateBossGateTrack(
                    CreateBossGate("BossGate_Allow", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, 0.5f, 0.8f, CombatAttackType.SkillAttack),
                    CreateBossGate("BossGate_BlockSkillKnockdown", BossReactionGatePolicy.BlockInterrupt, BossReactionGateBlockedOutcome.Knockdown, 0.5f, 0.8f, CombatAttackType.SkillAttack))
            }).ToArray());

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.SkillAttack, CombatHitOutcome.Knockdown, 0.6f).Id,
                    Is.EqualTo(BossReactionGateResultId.BlockedByBossReactionGate));
                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.SkillAttack, CombatHitOutcome.HitReaction, 0.6f).Id,
                    Is.EqualTo(BossReactionGateResultId.Allowed));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_ReturnsClosedWhenNoAllowGateMatches()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                CreateBossGateTrack(CreateBossGate("BossGate_Allow", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, 0.5f, 0.8f, CombatAttackType.HeavyAttack))
            }).ToArray());

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.LightAttack, CombatHitOutcome.HitReaction, 0.6f).Id,
                    Is.EqualTo(BossReactionGateResultId.BlockedByClosedWindow));
                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.HeavyAttack, CombatHitOutcome.HitReaction, 0.9f).Id,
                    Is.EqualTo(BossReactionGateResultId.BlockedByClosedWindow));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void RuntimeSpec_BlockGateOverridesAllowGateWhenBothMatch()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                CreateBossGateTrack(
                    CreateBossGate("BossGate_Allow", BossReactionGatePolicy.AllowInterrupt, BossReactionGateBlockedOutcome.Any, 0.5f, 0.8f, CombatAttackType.SkillAttack),
                    CreateBossGate("BossGate_Block", BossReactionGatePolicy.BlockInterrupt, BossReactionGateBlockedOutcome.Any, 0.6f, 0.7f, CombatAttackType.SkillAttack))
            }).ToArray());

            try
            {
                CombatActionRuntimeSpec spec = timeline.BuildRuntimeSpec();

                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.SkillAttack, CombatHitOutcome.Knockdown, 0.65f).Id,
                    Is.EqualTo(BossReactionGateResultId.BlockedByBossReactionGate));
                Assert.That(
                    spec.EvaluateBossReactionGate(CombatAttackType.SkillAttack, CombatHitOutcome.Knockdown, 0.75f).Id,
                    Is.EqualTo(BossReactionGateResultId.Allowed));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsMissingRequiredPlayerAttackCapabilities()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline(includeRequiredCancelWindows: false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("Missing capability: Input_AttackBuffer")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void CombatTimelineValidator_FlagsHitNodeWithNoneReactionIntent()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            try
            {
                foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
                {
                    clip.ReactionIntent = CombatReactionIntent.None;
                    break;
                }

                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("HitNode ReactionIntent is None")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Skill1_FirstTwoHitNodesAreHitReactionAndThirdKnockdown()
        {
            Assert.That(PlayerSkillActionResolver.TryGetSkill1(out PlayerSkillActionData skill), Is.True);

            Assert.That(skill.TryGetHitNode("Skill1_Hit1", out CombatHitNodeData hit1), Is.True);
            Assert.That(skill.TryGetHitNode("Skill1_Hit2", out CombatHitNodeData hit2), Is.True);
            Assert.That(skill.TryGetHitNode("Skill1_Hit3", out CombatHitNodeData hit3), Is.True);
            Assert.That(hit1.ReactionIntent, Is.EqualTo(CombatReactionIntent.HitReaction));
            Assert.That(hit2.ReactionIntent, Is.EqualTo(CombatReactionIntent.HitReaction));
            Assert.That(hit3.ReactionIntent, Is.EqualTo(CombatReactionIntent.Knockdown));
        }

        [Test]
        public void PlayerSkillState_RequestsEachTimelineMotionWindowOnce()
        {
            CombatTimelineProvider.ResetCache();
            PlayerStateContext context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 32f,
                MaxBetaEnergy = 32f
            });
            context.SetState(PlayerStateId.Skill);
            PlayerSkillState state = new PlayerSkillState();
            state.Enter(context);
            int initialRequestVersion = context.ActionMotionRequestVersion;

            context.StateElapsedTime = 0.60f;
            state.Tick(context, 0f);
            Assert.That(context.ActionMotionRequestVersion, Is.EqualTo(initialRequestVersion + 1));

            context.StateElapsedTime = 0.70f;
            state.Tick(context, 0f);
            Assert.That(context.ActionMotionRequestVersion, Is.EqualTo(initialRequestVersion + 1));

            context.StateElapsedTime = 0.85f;
            state.Tick(context, 0f);
            context.StateElapsedTime = 1.00f;
            state.Tick(context, 0f);
            Assert.That(context.ActionMotionRequestVersion, Is.EqualTo(initialRequestVersion + 2));

            context.StateElapsedTime = 1.40f;
            state.Tick(context, 0f);
            context.StateElapsedTime = 1.80f;
            state.Tick(context, 0f);
            Assert.That(context.ActionMotionRequestVersion, Is.EqualTo(initialRequestVersion + 3));

            state.Exit(context);
            CombatTimelineProvider.ResetCache();
        }

        [Test]
        public void PlayerSkillState_MovementCancelRequiresOpenWindowAndMoveInput()
        {
            CombatTimelineProvider.ResetCache();
            PlayerStateContext context = CreateSkillContext();
            PlayerSkillState state = new PlayerSkillState();
            try
            {
                state.Enter(context);

                context.StateElapsedTime = 3.49f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 3.49f
                };
                PlayerStateId beforeWindow = state.Tick(context, 0f);

                context.StateElapsedTime = 3.5f;
                context.Input = new PlayerInputSnapshot { Time = 3.5f };
                PlayerStateId withoutMoveInput = state.Tick(context, 0f);

                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 3.5f
                };
                PlayerStateId withMoveInput = state.Tick(context, 0f);
                bool windowOpenAtStart = context.IsMoveCancelWindow;

                context.StateElapsedTime = 6f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 6f
                };
                PlayerStateId atWindowEnd = state.Tick(context, 0f);

                context.StateElapsedTime = 6.01f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 6.01f
                };
                PlayerStateId afterSkillEnd = state.Tick(context, 0f);

                Assert.That(beforeWindow, Is.EqualTo(PlayerStateId.None));
                Assert.That(withoutMoveInput, Is.EqualTo(PlayerStateId.None));
                Assert.That(withMoveInput, Is.EqualTo(PlayerStateId.Locomotion));
                Assert.That(windowOpenAtStart, Is.True);
                Assert.That(atWindowEnd, Is.EqualTo(PlayerStateId.Locomotion));
                Assert.That(afterSkillEnd, Is.EqualTo(PlayerStateId.Locomotion));
                Assert.That(context.IsMoveCancelWindow, Is.False);
            }
            finally
            {
                state.Exit(context);
                CombatTimelineProvider.ResetCache();
            }
        }

        [Test]
        public void PlayerSkillState_ActionCancelHasPriorityOverMovementCancel()
        {
            CombatTimelineProvider.ResetCache();
            PlayerStateContext context = CreateSkillContext();
            PlayerSkillState state = new PlayerSkillState();
            try
            {
                state.Enter(context);
                context.StateElapsedTime = 3.5f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    EvadePressed = true,
                    Time = 3.5f
                };

                PlayerStateId transition = state.Tick(context, 0f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.Evade));
                Assert.That(context.IsEvadeCancelWindow, Is.True);
                Assert.That(context.IsMoveCancelWindow, Is.True);
            }
            finally
            {
                state.Exit(context);
                CombatTimelineProvider.ResetCache();
            }
        }

        [Test]
        public void Provider_CachesBossReactionRuntimeSpec()
        {
            CombatTimelineProvider.ResetCache();
            BossReactionTimelineAsset timeline = CreateBossReactionTimelineWithMotion("Boss_CacheReaction", string.Empty, string.Empty);
            try
            {
                RegisterTestTimeline(timeline);

                Assert.That(CombatTimelineProvider.TryGetBossReaction("Boss_CacheReaction", out CombatTimelineReactionConfig first), Is.True);
                Assert.That(CombatTimelineProvider.TryGetBossReaction("Boss_CacheReaction", out CombatTimelineReactionConfig second), Is.True);

                Assert.That(second.RuntimeSpec, Is.SameAs(first.RuntimeSpec));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Provider_CachesBossAttackDefinition()
        {
            CombatTimelineProvider.ResetCache();
            BossAttackTimelineAsset timeline = CreateBossAttackTimeline("Boss_CacheAttack");
            try
            {
                RegisterTestTimeline(timeline);

                Assert.That(CombatTimelineProvider.TryGetBossAttack("Boss_CacheAttack", out BossAttackDefinition first), Is.True);
                Assert.That(CombatTimelineProvider.TryGetBossAttack("Boss_CacheAttack", out BossAttackDefinition second), Is.True);
                BossAttackDefinition enumerated = CombatTimelineProvider.EnumerateBossAttacks()
                    .FirstOrDefault(attack => attack.AttackId == "Boss_CacheAttack");

                Assert.That(second, Is.SameAs(first));
                Assert.That(second.RuntimeSpec, Is.SameAs(first.RuntimeSpec));
                Assert.That(enumerated, Is.SameAs(first));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Provider_ResetRuntimeDataCacheRebuildsRuntimeData()
        {
            CombatTimelineProvider.ResetCache();
            BossReactionTimelineAsset timeline = CreateBossReactionTimelineWithMotion("Boss_ResetRuntimeCache", string.Empty, string.Empty);
            try
            {
                RegisterTestTimeline(timeline);
                Assert.That(CombatTimelineProvider.TryGetBossReaction("Boss_ResetRuntimeCache", out CombatTimelineReactionConfig first), Is.True);

                CombatTimelineProvider.ResetRuntimeDataCache();
                Assert.That(CombatTimelineProvider.TryGetBossReaction("Boss_ResetRuntimeCache", out CombatTimelineReactionConfig second), Is.True);

                Assert.That(second.RuntimeSpec, Is.Not.SameAs(first.RuntimeSpec));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Provider_RebuildRuntimeDataCachePrewarmsRuntimeData()
        {
            CombatTimelineProvider.ResetCache();
            BossAttackTimelineAsset bossAttack = CreateBossAttackTimeline("Boss_PrewarmAttack");
            BossReactionTimelineAsset bossReaction = CreateBossReactionTimelineWithMotion("Boss_PrewarmReaction", string.Empty, string.Empty);
            PlayerReactionTimelineAsset playerReaction = CreateHitReactionTimeline();
            try
            {
                RegisterTestTimeline(bossAttack);
                RegisterTestTimeline(bossReaction);
                RegisterTestTimeline(playerReaction);

                CombatTimelineProvider.RebuildRuntimeDataCache();

                Assert.That(GetRuntimeCacheCount("bossAttackById"), Is.EqualTo(1));
                Assert.That(GetRuntimeCacheCount("bossReactionById"), Is.EqualTo(1));
                Assert.That(GetRuntimeCacheCount("playerReactionById"), Is.EqualTo(1));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(bossAttack);
                Object.DestroyImmediate(bossReaction);
                Object.DestroyImmediate(playerReaction);
            }
        }

        [Test]
        public void Provider_ResetCacheClearsAssetAndRuntimeCaches()
        {
            CombatTimelineProvider.ResetCache();
            BossAttackTimelineAsset timeline = CreateBossAttackTimeline("Boss_ResetAllCache");
            try
            {
                RegisterTestTimeline(timeline);
                Assert.That(CombatTimelineProvider.TryGetBossAttack("Boss_ResetAllCache", out _), Is.True);

                CombatTimelineProvider.ResetCache();

                Assert.That(GetTimelineCacheCount(), Is.EqualTo(0));
                Assert.That(GetRuntimeCacheCount("bossAttackById"), Is.EqualTo(0));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_AllowsAnimationPreviewToExtendPastTimelineDuration()
        {
            PlayerAttackTimelineAsset timeline = CreateAttackTimeline();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                new CombatTimelineTrack("Preview", CombatTimelineTrackKind.AnimationPreview)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatAnimationClipWindow
                        {
                            Name = "PreviewOverflow",
                            CapabilityId = CombatTimelineCapabilityId.None,
                            StartTime = 0.2f,
                            EndTime = 1.4f,
                            ClipStartOffset = 0f,
                            ClipEndOffset = 1.2f,
                            ClipSpeed = 1f
                        }
                    }
                }
            }).ToArray());

            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("PreviewOverflow") &&
                    message.Message.Contains("clip ends")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }


        [Test]
        public void PlayerGuardTimeline_ParsesInternalReactionDurations()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline();
            try
            {
                PlayerGuardActionData data = timeline.ToGuardActionData();
                CombatActionCapabilitySnapshot chainInput = data.RuntimeSpec.Evaluate(0.2f);

                Assert.That(data.GuardHitReactionDuration, Is.EqualTo(0.83f).Within(0.001f));
                Assert.That(data.PerfectGuardReactionDuration, Is.EqualTo(0.5f).Within(0.001f));
                Assert.That(data.PerfectGuardChainActiveDuration, Is.EqualTo(10f / 60f).Within(0.001f));
                Assert.That(data.GuardHitCancelData.CanBufferEvade(0.2f), Is.False);
                Assert.That(data.GuardHitCancelData.CanBufferSkill(0.2f), Is.False);
                Assert.That(data.GuardHitCancelData.CanCancelToEvade(0.3f), Is.False);
                Assert.That(data.GuardHitCancelData.CanCancelToSkill(0.4f), Is.False);
                Assert.That(data.GuardHitCancelData.CanResetAttack(0.45f), Is.False);
                Assert.That(data.GuardHitCancelData.CanCancelToGuardRelease(0.1f), Is.False);
                Assert.That(data.GuardHitCancelData.CanCancelToGuardRelease(0.2f), Is.True);
                Assert.That(data.PerfectGuardCancelData.CanCancelToGuardRelease(0.2f), Is.False);
                Assert.That(data.PerfectGuardCancelData.CanCancelToGuardRelease(0.3f), Is.True);
                Assert.That(data.GuardReleaseCancelData.CanBufferEvade(0.1f), Is.True);
                Assert.That(data.GuardReleaseCancelData.CanCancelToEvade(0.3f), Is.True);
                Assert.That(data.GuardReleaseCancelData.CanResetAttack(0.3f), Is.True);
                Assert.That(chainInput.PerfectGuardChainInput, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsMissingGuardInternalReactionMarker()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline(includeGuardHitMarker: false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("GuardHitReaction marker")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsMissingGuardHitGuardReleaseWindow()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline(includeGuardHitReleaseCancel: false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("GuardHit Cancel_ToGuardRelease")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsMissingPerfectGuardGuardReleaseWindow()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline(includePerfectGuardReleaseCancel: false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("PerfectGuard Cancel_ToGuardRelease")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_RejectsGuardHitActionBufferAndCancelWindows()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline(includeUnsupportedGuardHitActionWindows: true);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);
                string[] forbiddenCapabilities =
                {
                    "Input_EvadeBuffer",
                    "Input_SkillBuffer",
                    "Cancel_ToEvade",
                    "Cancel_ToSkill",
                    "Cancel_AttackReset"
                };

                foreach (string capability in forbiddenCapabilities)
                {
                    Assert.That(messages.Any(message =>
                        message.Severity == CombatTimelineValidationSeverity.Error &&
                        message.Message.Contains("GuardHit track does not allow") &&
                        message.Message.Contains(capability)), Is.True, capability);
                }
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_RejectsPerfectGuardActionBufferAndCancelWindows()
        {
            PlayerGuardTimelineAsset timeline = CreateGuardTimeline(includeUnsupportedPerfectGuardActionWindows: true);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);
                string[] forbiddenCapabilities =
                {
                    "Input_EvadeBuffer",
                    "Input_SkillBuffer",
                    "Cancel_ToEvade",
                    "Cancel_ToSkill",
                    "Cancel_AttackReset"
                };

                foreach (string capability in forbiddenCapabilities)
                {
                    Assert.That(messages.Any(message =>
                        message.Severity == CombatTimelineValidationSeverity.Error &&
                        message.Message.Contains("PerfectGuard track does not allow") &&
                        message.Message.Contains(capability)), Is.True, capability);
                }
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_AllowsDeadReactionWithoutFunctionalWindows()
        {
            PlayerReactionTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerReaction,
                "Player_Dead",
                "Player Dead",
                "SM_Reaction/Anim_Dead",
                1f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.Dead);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Phase", CombatTimelineTrackKind.Phase)
                {
                    Clips = new CombatTimelineClip[] { }
                },
                new CombatTimelineTrack("Animation Preview", CombatTimelineTrackKind.AnimationPreview)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatAnimationClipWindow
                        {
                            Name = "Animation Preview",
                            CapabilityId = CombatTimelineCapabilityId.None,
                            StartTime = 0f,
                            EndTime = 1f,
                            AnimatorStateName = "SM_Reaction/Anim_Dead",
                            ClipSpeed = 1f,
                            LoopPreview = true
                        }
                    }
                }
            });

            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("Missing capability")), Is.False);
                Assert.That(messages.Any(message =>
                    message.Message.Contains("Dead reaction should not configure")), Is.False);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ReactionConfig_UsesAnimationPreviewTrackForPreviewData()
        {
            PlayerReactionTimelineAsset timeline = CreateHitReactionTimeline();
            AnimationClip previewClip = new AnimationClip();
            timeline.SetTracks(timeline.Tracks.Concat(new[]
            {
                new CombatTimelineTrack("Animation Preview", CombatTimelineTrackKind.AnimationPreview)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatAnimationClipWindow
                        {
                            Name = "HitReaction Preview",
                            CapabilityId = CombatTimelineCapabilityId.None,
                            StartTime = 0f,
                            EndTime = 0.5f,
                            PreviewAnimationClip = previewClip,
                            AnimatorStateName = "SM_Reaction/Anim_HitReaction_Light",
                            ClipStartOffset = 0.1f,
                            ClipEndOffset = 0.5f,
                            ClipSpeed = 1.25f,
                            LoopPreview = true
                        }
                    }
                }
            }).ToArray());

            try
            {
                CombatTimelineReactionConfig config = timeline.ToReactionConfig();

                Assert.That(config.PreviewAnimationClip, Is.SameAs(previewClip));
                Assert.That(config.AnimationStateName, Is.EqualTo("SM_Reaction/Anim_HitReaction_Light"));
                Assert.That(config.ClipStartOffset, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(config.ClipSpeed, Is.EqualTo(1.25f).Within(0.0001f));
                Assert.That(config.LoopPreview, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(previewClip);
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_WarnsWhenReactionHasNoAnimationPreviewClip()
        {
            PlayerReactionTimelineAsset timeline = CreateHitReactionTimeline();
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Warning &&
                    message.Message.Contains("Animation Preview clip is missing")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ReactionConfig_ExposesRuntimeSpecCancelWindows()
        {
            PlayerReactionTimelineAsset timeline = CreateKnockdownTimeline(CombatTimelineCapabilityId.Cancel_ToEvade);
            try
            {
                CombatTimelineReactionConfig config = timeline.ToReactionConfig();

                Assert.That(config.RuntimeSpec, Is.Not.Null);
                Assert.That(config.RuntimeSpec.Evaluate(1.2f).CanCancelToEvade, Is.False);
                Assert.That(config.RuntimeSpec.Evaluate(1.35f).CanCancelToEvade, Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ReactionConfig_UsesReactionWindowsForTimingAndMotion()
        {
            PlayerReactionTimelineAsset timeline = CreateHitReactionTimeline();
            try
            {
                CombatTimelineReactionConfig config = timeline.ToReactionConfig();

                Assert.That(config.StunDuration, Is.EqualTo(0.33333334f).Within(0.0001f));
                Assert.That(config.RecoveryStartTime, Is.EqualTo(0.33333334f).Within(0.0001f));
                Assert.That(config.CanReturnTime, Is.EqualTo(0.53333336f).Within(0.0001f));
                Assert.That(config.MotionId, Is.EqualTo(PlayerActionMotionId.HitReactionLight));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void ReactionConfig_ExposesBossCodeMoveReference()
        {
            BossReactionTimelineAsset timeline = CreateBossReactionTimelineWithMotion(
                "Boss_HitStagger",
                "Boss_HitStagger",
                "HitStaggerWrap");
            try
            {
                CombatTimelineReactionConfig config = timeline.ToReactionConfig();

                Assert.That(config.HasBossCodeMove, Is.True);
                Assert.That(config.BossMotionActionId, Is.EqualTo("Boss_HitStagger"));
                Assert.That(config.BossMotionWindowName, Is.EqualTo("HitStaggerWrap"));
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsBossReactionCodeMoveMissingReference()
        {
            BossReactionTimelineAsset timeline = CreateBossReactionTimelineWithMotion(
                "Boss_HitStagger",
                string.Empty,
                string.Empty);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("BossCodeMove requires Boss Motion Action Id")), Is.True);
                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("BossCodeMove requires Profile Window")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsPlayerKnockdownMissingPhaseClip()
        {
            PlayerReactionTimelineAsset timeline = CreateKnockdownTimeline(
                new[] { CombatTimelineCapabilityId.Cancel_ToEvade },
                false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("PlayerKnockdown Start")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void PlayerGuardState_GuardHitReleaseEntersReleaseEarly()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                PlayerGuardState state = EnterGuardHit(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot { GuardHeld = false, Time = 0.1f };
                PlayerStateId transition = state.Tick(context, 0.1f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Active));
                Assert.That(context.IsGuardBlockActive, Is.True);
                Assert.That(context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.GuardHit));

                context.Input = new PlayerInputSnapshot { GuardHeld = false, Time = 0.2f };
                transition = state.Tick(context, 0.1f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsGuardBlockActive, Is.False);
                Assert.That(context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.None));

                context.Input = new PlayerInputSnapshot { GuardPressed = true, GuardHeld = true, Time = 0.22f };
                transition = state.Tick(context, 0.02f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Start));
                Assert.That(context.IsGuardReentry, Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerGuardState_GuardHitHeldIgnoresActionInputs()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                PlayerGuardState state = EnterGuardHit(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Skill1Pressed = true,
                    LightAttackPressed = true,
                    GuardHeld = true,
                    Time = 0.3f
                };
                PlayerStateId transition = state.Tick(context, 0.3f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Active));
                Assert.That(context.IsGuardBlockActive, Is.True);
                Assert.That(context.IsEvadeCancelWindow, Is.False);
                Assert.That(context.IsSkillCancelWindow, Is.False);
                Assert.That(context.IsResetWindow, Is.False);
                Assert.That(context.HasBufferedEvadeInput, Is.False);
                Assert.That(context.HasBufferedSkillInput, Is.False);
                Assert.That(context.HasBufferedAttackInput, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerGuardState_GuardHitReleaseTransfersEvadeAndSkillToReleaseBuffers()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            PlayerSkillTimelineAsset skillTimeline = CreateSkill1Timeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                RegisterTestTimeline(skillTimeline);
                PlayerGuardState state = EnterGuardHit(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Skill1Pressed = true,
                    GuardHeld = false,
                    Time = 0.3f
                };
                PlayerStateId transition = state.Tick(context, 0.3f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsGuardBlockActive, Is.False);
                Assert.That(context.HasBufferedEvadeInput, Is.True);
                Assert.That(context.HasBufferedSkillInput, Is.True);

                context.Input = new PlayerInputSnapshot { GuardHeld = false, Time = 0.55f };
                transition = state.Tick(context, 0.25f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.Evade));
                Assert.That(context.IsEvadeCancelWindow, Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(skillTimeline);
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerGuardState_GuardHitReleaseDoesNotBufferAttack()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                PlayerGuardState state = EnterGuardHit(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    GuardHeld = false,
                    Time = 0.3f
                };
                PlayerStateId transition = state.Tick(context, 0.3f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.HasBufferedAttackInput, Is.False);

                context.Input = new PlayerInputSnapshot { GuardHeld = false, Time = 0.55f };
                transition = state.Tick(context, 0.25f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsResetWindow, Is.True);

                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    GuardHeld = false,
                    Time = 0.56f
                };
                transition = state.Tick(context, 0f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.Attack));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerGuardState_GuardHitHeldReturnsToLoopAndCanReplayReaction()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                PlayerGuardState state = EnterGuardHit(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot { GuardHeld = true, Time = 0.83f };
                PlayerStateId transition = state.Tick(context, 0.83f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Loop));
                Assert.That(context.IsGuardBlockActive, Is.True);

                context.RequestGuardReaction(PlayerGuardReactionId.GuardHit);
                context.Input = new PlayerInputSnapshot { GuardHeld = true, Time = 0.84f };
                transition = state.Tick(context, 0.01f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Active));
                Assert.That(context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.GuardHit));
                Assert.That(context.IsGuardBlockActive, Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerGuardState_PerfectGuardReleaseRequiresWindowAndAttackRepress()
        {
            PlayerGuardTimelineAsset guardTimeline = CreateGuardTimeline();
            try
            {
                RegisterTestTimeline(guardTimeline);
                PlayerGuardState state = EnterPerfectGuard(out PlayerStateContext context);

                context.Input = new PlayerInputSnapshot
                {
                    GuardPressed = true,
                    GuardHeld = true,
                    Time = 0.17f
                };
                PlayerStateId transition = state.Tick(context, 0.17f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.IsPerfectGuardChainWindow, Is.True);

                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    GuardHeld = false,
                    Time = 0.2f
                };
                transition = state.Tick(context, 0.03f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Active));
                Assert.That(context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.PerfectGuard));
                Assert.That(context.IsGuardBlockActive, Is.True);

                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    GuardHeld = false,
                    Time = 0.22f
                };
                transition = state.Tick(context, 0.02f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.CurrentGuardReaction, Is.EqualTo(PlayerGuardReactionId.None));
                Assert.That(context.IsGuardBlockActive, Is.False);
                Assert.That(context.IsPerfectGuardChainWindow, Is.False);
                Assert.That(context.HasBufferedAttackInput, Is.False);

                context.Input = new PlayerInputSnapshot { GuardHeld = false, Time = 0.32f };
                transition = state.Tick(context, 0.1f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsResetWindow, Is.True);

                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    GuardHeld = false,
                    Time = 0.33f
                };
                transition = state.Tick(context, 0.01f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.Attack));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(guardTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_DoesNotTransitionBeforeCancelWindow()
        {
            PlayerReactionTimelineAsset knockdownTimeline = CreateKnockdownTimeline(
                CombatTimelineCapabilityId.Cancel_ToEvade,
                CombatTimelineCapabilityId.Cancel_ToSkill,
                CombatTimelineCapabilityId.Cancel_ToGuard,
                CombatTimelineCapabilityId.Cancel_AttackReset);
            try
            {
                RegisterTestTimeline(knockdownTimeline);
                PlayerKnockdownState state = EnterKnockdown(out PlayerStateContext context);
                context.StateElapsedTime = 1.2f;
                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Skill1Pressed = true,
                    LightAttackPressed = true,
                    GuardPressed = true,
                    Time = 1.2f
                };

                PlayerStateId transition = state.Tick(context, 0.016f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsEvadeCancelWindow, Is.False);
                Assert.That(context.IsSkillCancelWindow, Is.False);
                Assert.That(context.IsResetWindow, Is.False);
                Assert.That(context.IsGuardCancelWindow, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(knockdownTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_OnlyTransitionsForOpenCancelWindow()
        {
            PlayerReactionTimelineAsset knockdownTimeline = CreateKnockdownTimeline(CombatTimelineCapabilityId.Cancel_ToEvade);
            try
            {
                RegisterTestTimeline(knockdownTimeline);
                PlayerKnockdownState state = EnterKnockdown(out PlayerStateContext context);
                context.StateElapsedTime = 1.35f;
                context.Input = new PlayerInputSnapshot
                {
                    GuardPressed = true,
                    Time = 1.35f
                };

                PlayerStateId blockedTransition = state.Tick(context, 0.016f);

                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Time = 1.35f
                };
                PlayerStateId allowedTransition = state.Tick(context, 0.016f);

                Assert.That(blockedTransition, Is.EqualTo(PlayerStateId.None));
                Assert.That(allowedTransition, Is.EqualTo(PlayerStateId.Evade));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Reset));
                Assert.That(context.IsEvadeCancelWindow, Is.True);
                Assert.That(context.IsGuardCancelWindow, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(knockdownTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_CancelWindowsOpenSkillAttackAndGuard()
        {
            PlayerReactionTimelineAsset skillKnockdown = CreateKnockdownTimeline(CombatTimelineCapabilityId.Cancel_ToSkill);
            PlayerReactionTimelineAsset attackKnockdown = CreateKnockdownTimeline(CombatTimelineCapabilityId.Cancel_AttackReset);
            PlayerReactionTimelineAsset guardKnockdown = CreateKnockdownTimeline(CombatTimelineCapabilityId.Cancel_ToGuard);
            PlayerSkillTimelineAsset skillTimeline = CreateSkill1Timeline();
            try
            {
                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(skillKnockdown);
                PlayerKnockdownState skillState = EnterKnockdown(out PlayerStateContext skillContext);
                skillContext.StateElapsedTime = 1.35f;
                skillContext.Input = new PlayerInputSnapshot { Skill1Pressed = true, Time = 1.35f };

                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(attackKnockdown);
                PlayerKnockdownState attackState = EnterKnockdown(out PlayerStateContext attackContext);
                attackContext.StateElapsedTime = 1.35f;
                attackContext.Input = new PlayerInputSnapshot { LightAttackPressed = true, Time = 1.35f };

                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(guardKnockdown);
                PlayerKnockdownState guardState = EnterKnockdown(out PlayerStateContext guardContext);
                guardContext.StateElapsedTime = 1.35f;
                guardContext.Input = new PlayerInputSnapshot { GuardPressed = true, Time = 1.35f };

                Assert.That(skillState.Tick(skillContext, 0.016f), Is.EqualTo(PlayerStateId.Skill));
                Assert.That(attackState.Tick(attackContext, 0.016f), Is.EqualTo(PlayerStateId.Attack));
                Assert.That(attackContext.RequestedAttackInput, Is.EqualTo(AttackInputType.Light));
                Assert.That(guardState.Tick(guardContext, 0.016f), Is.EqualTo(PlayerStateId.Guard));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(skillKnockdown);
                Object.DestroyImmediate(attackKnockdown);
                Object.DestroyImmediate(guardKnockdown);
                Object.DestroyImmediate(skillTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_MovementReturnRequiresOpenWindowAndMoveInput()
        {
            PlayerReactionTimelineAsset knockdownTimeline = CreateKnockdownTimeline(
                CombatTimelineCapabilityId.Cancel_MovementReturn);
            try
            {
                RegisterTestTimeline(knockdownTimeline);
                PlayerKnockdownState state = EnterKnockdown(out PlayerStateContext context);
                context.StateElapsedTime = 1.2f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 1.2f
                };

                PlayerStateId beforeWindow = state.Tick(context, 0.016f);

                context.StateElapsedTime = 1.35f;
                context.Input = new PlayerInputSnapshot { Time = 1.35f };
                PlayerStateId withoutMoveInput = state.Tick(context, 0.016f);

                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    Time = 1.35f
                };
                PlayerStateId withMoveInput = state.Tick(context, 0.016f);

                Assert.That(beforeWindow, Is.EqualTo(PlayerStateId.None));
                Assert.That(withoutMoveInput, Is.EqualTo(PlayerStateId.None));
                Assert.That(withMoveInput, Is.EqualTo(PlayerStateId.Locomotion));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Reset));
                Assert.That(context.IsMoveCancelWindow, Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(knockdownTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_ActionCancelHasPriorityOverMovementReturn()
        {
            PlayerReactionTimelineAsset knockdownTimeline = CreateKnockdownTimeline(
                CombatTimelineCapabilityId.Cancel_ToEvade,
                CombatTimelineCapabilityId.Cancel_MovementReturn);
            try
            {
                RegisterTestTimeline(knockdownTimeline);
                PlayerKnockdownState state = EnterKnockdown(out PlayerStateContext context);
                context.StateElapsedTime = 1.35f;
                context.Input = new PlayerInputSnapshot
                {
                    Move = Vector2.up,
                    EvadePressed = true,
                    Time = 1.35f
                };

                PlayerStateId transition = state.Tick(context, 0.016f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.Evade));
                Assert.That(context.IsMoveCancelWindow, Is.True);
                Assert.That(context.IsEvadeCancelWindow, Is.True);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(knockdownTimeline);
            }
        }

        [Test]
        public void PlayerKnockdownState_MissingCancelWindowsStillReturnsAfterDuration()
        {
            PlayerReactionTimelineAsset knockdownTimeline = CreateKnockdownTimeline();
            try
            {
                RegisterTestTimeline(knockdownTimeline);
                PlayerKnockdownState state = EnterKnockdown(out PlayerStateContext context);
                context.StateElapsedTime = 1.35f;
                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    EvadePressed = true,
                    GuardPressed = true,
                    Time = 1.35f
                };

                PlayerStateId blockedTransition = state.Tick(context, 0.016f);

                context.StateElapsedTime = 1.61f;
                PlayerStateId returnTransition = state.Tick(context, 0.016f);

                Assert.That(blockedTransition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Reset));
                Assert.That(context.IsResetWindow, Is.False);
                Assert.That(returnTransition, Is.EqualTo(PlayerStateId.Idle));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(knockdownTimeline);
            }
        }

        [Test]
        public void Validation_ReportsPlayerHitReactionMissingPhaseClip()
        {
            PlayerReactionTimelineAsset timeline = CreateHitReactionTimeline(
                new[] { CombatTimelineCapabilityId.Cancel_ToEvade },
                false);
            try
            {
                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("PlayerHitReaction Start")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void PlayerHitReactionState_DoesNotTransitionBeforeCancelWindow()
        {
            PlayerReactionTimelineAsset hitReactionTimeline = CreateHitReactionTimeline(
                CombatTimelineCapabilityId.Cancel_ToEvade,
                CombatTimelineCapabilityId.Cancel_ToSkill,
                CombatTimelineCapabilityId.Cancel_ToGuard,
                CombatTimelineCapabilityId.Cancel_AttackReset);
            try
            {
                RegisterTestTimeline(hitReactionTimeline);
                PlayerHitReactionState state = EnterHitReaction(out PlayerStateContext context);
                context.StateElapsedTime = 0.42f;
                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Skill1Pressed = true,
                    LightAttackPressed = true,
                    GuardPressed = true,
                    Time = 0.42f
                };

                PlayerStateId transition = state.Tick(context, 0.016f);

                Assert.That(transition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Recovery));
                Assert.That(context.IsEvadeCancelWindow, Is.False);
                Assert.That(context.IsSkillCancelWindow, Is.False);
                Assert.That(context.IsResetWindow, Is.False);
                Assert.That(context.IsGuardCancelWindow, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(hitReactionTimeline);
            }
        }

        [Test]
        public void PlayerHitReactionState_OnlyTransitionsForOpenCancelWindow()
        {
            PlayerReactionTimelineAsset hitReactionTimeline = CreateHitReactionTimeline(CombatTimelineCapabilityId.Cancel_ToEvade);
            try
            {
                RegisterTestTimeline(hitReactionTimeline);
                PlayerHitReactionState state = EnterHitReaction(out PlayerStateContext context);
                context.StateElapsedTime = 0.56f;
                context.Input = new PlayerInputSnapshot
                {
                    GuardPressed = true,
                    Time = 0.56f
                };

                PlayerStateId blockedTransition = state.Tick(context, 0.016f);

                context.Input = new PlayerInputSnapshot
                {
                    EvadePressed = true,
                    Time = 0.56f
                };
                PlayerStateId allowedTransition = state.Tick(context, 0.016f);

                Assert.That(blockedTransition, Is.EqualTo(PlayerStateId.None));
                Assert.That(allowedTransition, Is.EqualTo(PlayerStateId.Evade));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Reset));
                Assert.That(context.IsEvadeCancelWindow, Is.True);
                Assert.That(context.IsGuardCancelWindow, Is.False);
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(hitReactionTimeline);
            }
        }

        [Test]
        public void PlayerHitReactionState_CancelWindowsOpenSkillAttackAndGuard()
        {
            PlayerReactionTimelineAsset skillHitReaction = CreateHitReactionTimeline(CombatTimelineCapabilityId.Cancel_ToSkill);
            PlayerReactionTimelineAsset attackHitReaction = CreateHitReactionTimeline(CombatTimelineCapabilityId.Cancel_AttackReset);
            PlayerReactionTimelineAsset guardHitReaction = CreateHitReactionTimeline(CombatTimelineCapabilityId.Cancel_ToGuard);
            PlayerSkillTimelineAsset skillTimeline = CreateSkill1Timeline();
            try
            {
                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(skillHitReaction);
                PlayerHitReactionState skillState = EnterHitReaction(out PlayerStateContext skillContext);
                skillContext.StateElapsedTime = 0.56f;
                skillContext.Input = new PlayerInputSnapshot { Skill1Pressed = true, Time = 0.56f };

                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(attackHitReaction);
                PlayerHitReactionState attackState = EnterHitReaction(out PlayerStateContext attackContext);
                attackContext.StateElapsedTime = 0.56f;
                attackContext.Input = new PlayerInputSnapshot { LightAttackPressed = true, Time = 0.56f };

                RegisterTestTimeline(skillTimeline);
                RegisterTestTimeline(guardHitReaction);
                PlayerHitReactionState guardState = EnterHitReaction(out PlayerStateContext guardContext);
                guardContext.StateElapsedTime = 0.56f;
                guardContext.Input = new PlayerInputSnapshot { GuardPressed = true, Time = 0.56f };

                Assert.That(skillState.Tick(skillContext, 0.016f), Is.EqualTo(PlayerStateId.Skill));
                Assert.That(attackState.Tick(attackContext, 0.016f), Is.EqualTo(PlayerStateId.Attack));
                Assert.That(attackContext.RequestedAttackInput, Is.EqualTo(AttackInputType.Light));
                Assert.That(guardState.Tick(guardContext, 0.016f), Is.EqualTo(PlayerStateId.Guard));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(skillHitReaction);
                Object.DestroyImmediate(attackHitReaction);
                Object.DestroyImmediate(guardHitReaction);
                Object.DestroyImmediate(skillTimeline);
            }
        }

        [Test]
        public void PlayerHitReactionState_MissingCancelWindowsStillReturnsAfterDuration()
        {
            PlayerReactionTimelineAsset hitReactionTimeline = CreateHitReactionTimeline();
            try
            {
                RegisterTestTimeline(hitReactionTimeline);
                PlayerHitReactionState state = EnterHitReaction(out PlayerStateContext context);
                context.StateElapsedTime = 0.56f;
                context.Input = new PlayerInputSnapshot
                {
                    LightAttackPressed = true,
                    EvadePressed = true,
                    GuardPressed = true,
                    Time = 0.56f
                };

                PlayerStateId blockedTransition = state.Tick(context, 0.016f);

                context.StateElapsedTime = 0.64f;
                PlayerStateId returnTransition = state.Tick(context, 0.016f);

                Assert.That(blockedTransition, Is.EqualTo(PlayerStateId.None));
                Assert.That(context.CurrentPhase, Is.EqualTo(PlayerStatePhase.Reset));
                Assert.That(context.IsResetWindow, Is.False);
                Assert.That(returnTransition, Is.EqualTo(PlayerStateId.Idle));
            }
            finally
            {
                CombatTimelineProvider.ResetCache();
                Object.DestroyImmediate(hitReactionTimeline);
            }
        }

        [Test]
        public void Validation_ReportsBossAttackWithoutHitNode()
        {
            BossAttackTimelineAsset timeline = CreateBossAttackTimeline("Boss_AttackWithoutHitNode");
            try
            {
                timeline.SetTracks(timeline.Tracks
                    .Where(track => track.TrackKind != CombatTimelineTrackKind.HitNode)
                    .ToArray());

                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("BossAttack requires at least one HitNode")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        [Test]
        public void Validation_ReportsBossRepositionWithHitNode()
        {
            BossAttackTimelineAsset timeline = CreateBossAttackTimeline("Boss_RepositionWithHitNode");
            try
            {
                timeline.ConfigureIdentity(
                    CombatTimelineOwner.Boss,
                    CombatTimelineActionKind.BossReposition,
                    "Boss_RepositionWithHitNode",
                    "Boss Reposition With HitNode",
                    "Anim_Boss_RepositionWithHitNode",
                    1f,
                    60f);

                var messages = CombatTimelineValidator.Validate(timeline);

                Assert.That(messages.Any(message =>
                    message.Severity == CombatTimelineValidationSeverity.Error &&
                    message.Message.Contains("BossReposition must not contain HitNode")), Is.True);
            }
            finally
            {
                Object.DestroyImmediate(timeline);
            }
        }

        private static PlayerAttackTimelineAsset CreateAttackTimeline(bool includeRequiredCancelWindows = true)
        {
            PlayerAttackTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerAttackTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerAttack,
                "Test_L1",
                "Test L1",
                "Anim_Test_L1",
                1f,
                60f);
            timeline.ConfigureAttack(AttackInputType.Light, 1, string.Empty, string.Empty, CombatAttackType.LightAttack, 12f, 4f);

            CombatTimelineTrack phaseTrack = new CombatTimelineTrack("Phase", CombatTimelineTrackKind.Phase)
            {
                Clips = new CombatTimelineClip[]
                {
                    new CombatPhaseClip { Name = "Start", Phase = PlayerStatePhase.Start, CapabilityId = CombatTimelineCapabilityId.None, StartTime = 0f, EndTime = 0.2f },
                    new CombatPhaseClip { Name = "Active", Phase = PlayerStatePhase.Active, CapabilityId = CombatTimelineCapabilityId.None, StartTime = 0.2f, EndTime = 0.5f },
                    new CombatPhaseClip { Name = "Recovery", Phase = PlayerStatePhase.Recovery, CapabilityId = CombatTimelineCapabilityId.None, StartTime = 0.5f, EndTime = 0.8f },
                    new CombatPhaseClip { Name = "Reset", Phase = PlayerStatePhase.Reset, CapabilityId = CombatTimelineCapabilityId.None, StartTime = 0.8f, EndTime = 1f }
                }
            };

            CombatTimelineTrack hitTrack = new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
            {
                Clips = new CombatTimelineClip[]
                {
                    new CombatHitNodeClip
                    {
                        Name = "Hit_A",
                        CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                        StartTime = 0.25f,
                        EndTime = 0.4f,
                        Damage = 12f,
                        PoiseDamage = 4f,
                        MaxHitsPerTarget = 1
                    }
                }
            };

            if (!includeRequiredCancelWindows)
            {
                timeline.SetTracks(new[] { phaseTrack, hitTrack });
                return timeline;
            }

            CombatTimelineTrack inputTrack = new CombatTimelineTrack("Input", CombatTimelineTrackKind.InputBuffer)
            {
                Clips = new CombatTimelineClip[]
                {
                    Input("AttackBuffer", CombatTimelineCapabilityId.Input_AttackBuffer),
                    Input("EvadeBuffer", CombatTimelineCapabilityId.Input_EvadeBuffer),
                    Input("SkillBuffer", CombatTimelineCapabilityId.Input_SkillBuffer)
                }
            };

            CombatTimelineTrack cancelTrack = new CombatTimelineTrack("Cancel", CombatTimelineTrackKind.Cancel)
            {
                Clips = new CombatTimelineClip[]
                {
                    Cancel("Commit", CombatTimelineCapabilityId.Cancel_Commit, 0.05f, 0.18f),
                    Cancel("Combo", CombatTimelineCapabilityId.Cancel_AttackCombo, 0.35f, 0.55f),
                    Cancel("Evade", CombatTimelineCapabilityId.Cancel_ToEvade, 0.35f, 0.8f),
                    Cancel("Skill", CombatTimelineCapabilityId.Cancel_ToSkill, 0.35f, 0.8f),
                    Cancel("Guard", CombatTimelineCapabilityId.Cancel_ToGuard, 0.35f, 0.8f),
                    Cancel("Reset", CombatTimelineCapabilityId.Cancel_AttackReset, 0.8f, 1f),
                    Cancel("Return", CombatTimelineCapabilityId.Cancel_MovementReturn, 0.85f, 1f)
                }
            };

            timeline.SetTracks(new[] { phaseTrack, hitTrack, inputTrack, cancelTrack });
            return timeline;
        }

        private static CombatHitData CreateHit(CombatAttackType attackType, CombatReactionIntent reactionIntent)
        {
            return new CombatHitData
            {
                AttackId = 1,
                AttackerTeam = CombatTeam.Player,
                AttackType = attackType,
                ReactionIntent = reactionIntent,
                Damage = 10f,
                CanBeGuarded = true,
                CanBePerfectGuarded = true,
                CanBePerfectEvaded = true
            };
        }

        private static DefenderCombatState CreateDefender()
        {
            return new DefenderCombatState
            {
                CurrentHp = 100f
            };
        }

        private static CombatTimelineTrack CreateBossGateTrack(params BossReactionGateWindowClip[] clips)
        {
            return new CombatTimelineTrack("Boss Gate", CombatTimelineTrackKind.Interrupt)
            {
                Clips = clips
            };
        }

        private static CombatMotionReferenceClip CreatePlayerMotion(string name, float start, float end)
        {
            return new CombatMotionReferenceClip
            {
                Name = name,
                StartTime = start,
                EndTime = end,
                MotionKind = CombatTimelineClipKind.PlayerMotion,
                PlayerMotionId = PlayerActionMotionId.Skill1Forward
            };
        }

        private static PlayerActionMotionConfig CreateTargetMagnetConfig()
        {
            PlayerActionMotionConfig config = new PlayerActionMotionConfig();
            SetPrivateField(config, "motionId", PlayerActionMotionId.Skill1Forward);
            SetPrivateField(config, "duration", 0.28f);
            SetPrivateField(config, "distance", 1.85f);
            SetPrivateField(config, "normalizedDistanceCurve", AnimationCurve.Linear(0f, 0f, 1f, 1f));
            SetPrivateField(config, "stopOnSideCollision", true);
            SetPrivateField(config, "targetMode", PlayerActionMotionTargetMode.LockOnTarget);
            SetPrivateField(config, "maxTargetDistance", 3f);
            SetPrivateField(config, "maxTargetAngle", 60f);
            SetPrivateField(config, "stopDistance", 1.15f);
            return config;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            target.GetType()
                .GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);
        }

        private static BossReactionGateWindowClip CreateBossGate(
            string name,
            BossReactionGatePolicy policy,
            BossReactionGateBlockedOutcome blockedOutcome,
            float start,
            float end,
            params CombatAttackType[] attackTypes)
        {
            return new BossReactionGateWindowClip
            {
                Name = name,
                StartTime = start,
                EndTime = end,
                Policy = policy,
                BlockedOutcome = blockedOutcome,
                AttackTypes = attackTypes
            };
        }


        private static PlayerGuardTimelineAsset CreateGuardTimeline(
            bool includeGuardHitMarker = true,
            bool includeGuardHitReleaseCancel = true,
            bool includeUnsupportedGuardHitActionWindows = false,
            bool includePerfectGuardReleaseCancel = true,
            bool includeUnsupportedPerfectGuardActionWindows = false)
        {
            PlayerGuardTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerGuardTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerGuard,
                "Guard",
                "Guard",
                "SM_Guard",
                1f,
                60f);

            CombatTimelineTrack startTrack = new CombatTimelineTrack("Start", CombatTimelineTrackKind.Defense)
            {
                Clips = new CombatTimelineClip[]
                {
                    Defense("PerfectGuard", CombatTimelineCapabilityId.Defense_PerfectGuard, 0f, 0.05f),
                    Defense("BlockActive", CombatTimelineCapabilityId.Defense_GuardBlock, 0.05f, 0.16666667f)
                }
            };

            List<CombatTimelineClip> perfectGuardClips = new List<CombatTimelineClip>
            {
                Marker("PerfectGuardReaction", PlayerGuardTimelineAsset.PerfectGuardReactionMarkerId, 0f, 0.5f),
                Input("PerfectGuardChainInput", CombatTimelineCapabilityId.Input_PerfectGuardChain, 0.16666667f, 0.46666667f)
            };
            if (includePerfectGuardReleaseCancel)
            {
                perfectGuardClips.Add(Cancel("PerfectGuardGuardRelease", CombatTimelineCapabilityId.Cancel_ToGuardRelease, 0.21666667f, 0.5f));
            }

            if (includeUnsupportedPerfectGuardActionWindows)
            {
                perfectGuardClips.Add(Input("PerfectGuardEvadeBuffer", CombatTimelineCapabilityId.Input_EvadeBuffer, 0f, 0.2f));
                perfectGuardClips.Add(Input("PerfectGuardSkillBuffer", CombatTimelineCapabilityId.Input_SkillBuffer, 0f, 0.2f));
                perfectGuardClips.Add(Cancel("PerfectGuardEvadeCancel", CombatTimelineCapabilityId.Cancel_ToEvade, 0.25f, 0.5f));
                perfectGuardClips.Add(Cancel("PerfectGuardSkillCancel", CombatTimelineCapabilityId.Cancel_ToSkill, 0.25f, 0.5f));
                perfectGuardClips.Add(Cancel("PerfectGuardAttackReset", CombatTimelineCapabilityId.Cancel_AttackReset, 0.25f, 0.5f));
            }

            CombatTimelineTrack perfectGuardTrack = new CombatTimelineTrack("Perfect Guard", CombatTimelineTrackKind.InputBuffer)
            {
                Clips = perfectGuardClips.ToArray()
            };

            List<CombatTimelineClip> guardHitClips = new List<CombatTimelineClip>();
            if (includeGuardHitMarker)
            {
                guardHitClips.Add(Marker("GuardHitReaction", PlayerGuardTimelineAsset.GuardHitReactionMarkerId, 0f, 0.83f));
                if (includeGuardHitReleaseCancel)
                {
                    guardHitClips.Add(Cancel("GuardHitGuardRelease", CombatTimelineCapabilityId.Cancel_ToGuardRelease, 0.18f, 0.83f));
                }

                if (includeUnsupportedGuardHitActionWindows)
                {
                    guardHitClips.Add(Input("GuardHitEvadeBuffer", CombatTimelineCapabilityId.Input_EvadeBuffer, 0f, 0.2f));
                    guardHitClips.Add(Input("GuardHitSkillBuffer", CombatTimelineCapabilityId.Input_SkillBuffer, 0f, 0.2f));
                    guardHitClips.Add(Cancel("GuardHitEvadeCancel", CombatTimelineCapabilityId.Cancel_ToEvade, 0.25f, 0.65f));
                    guardHitClips.Add(Cancel("GuardHitSkillCancel", CombatTimelineCapabilityId.Cancel_ToSkill, 0.32f, 0.65f));
                    guardHitClips.Add(Cancel("GuardHitAttackReset", CombatTimelineCapabilityId.Cancel_AttackReset, 0.38f, 0.7f));
                }
            }

            CombatTimelineTrack guardHitTrack = new CombatTimelineTrack("GuardHit", CombatTimelineTrackKind.Marker)
            {
                Clips = guardHitClips.ToArray()
            };

            CombatTimelineTrack releaseTrack = new CombatTimelineTrack("Release", CombatTimelineTrackKind.Cancel)
            {
                Clips = new CombatTimelineClip[]
                {
                    Input("EvadeBuffer", CombatTimelineCapabilityId.Input_EvadeBuffer, 0f, 0.1f),
                    Input("SkillBuffer", CombatTimelineCapabilityId.Input_SkillBuffer, 0f, 0.1f),
                    Cancel("EvadeCancel", CombatTimelineCapabilityId.Cancel_ToEvade, 0.1f, 0.73333335f),
                    Cancel("SkillCancel", CombatTimelineCapabilityId.Cancel_ToSkill, 0.1f, 0.73333335f),
                    Cancel("AttackReset", CombatTimelineCapabilityId.Cancel_AttackReset, 0.1f, 0.73333335f)
                }
            };

            timeline.SetTracks(new[] { startTrack, perfectGuardTrack, guardHitTrack, releaseTrack });
            return timeline;
        }

        private static PlayerReactionTimelineAsset CreateKnockdownTimeline(
            params CombatTimelineCapabilityId[] cancelCapabilities)
        {
            return CreateKnockdownTimeline(cancelCapabilities, true);
        }

        private static PlayerReactionTimelineAsset CreateKnockdownTimeline(
            CombatTimelineCapabilityId[] cancelCapabilities,
            bool includePhaseClips)
        {
            PlayerReactionTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerReaction,
                PlayerKnockdownState.TimelineId,
                "Player Knockdown",
                "SM_Reaction/Anim_Knockdown_Start_B",
                1.6f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.Knockdown);

            CombatTimelineTrack phaseTrack = new CombatTimelineTrack("Phase", CombatTimelineTrackKind.Phase)
            {
                Clips = includePhaseClips
                    ? new CombatTimelineClip[]
                    {
                        new CombatPhaseClip { Name = "Start", Phase = PlayerStatePhase.Start, StartTime = 0f, EndTime = 0.36f },
                        new CombatPhaseClip { Name = "Loop", Phase = PlayerStatePhase.Loop, StartTime = 0.36f, EndTime = 0.96f },
                        new CombatPhaseClip { Name = "Recovery", Phase = PlayerStatePhase.Recovery, StartTime = 0.96f, EndTime = 1.3f },
                        new CombatPhaseClip { Name = "Reset", Phase = PlayerStatePhase.Reset, StartTime = 1.3f, EndTime = 1.6f }
                    }
                    : new CombatTimelineClip[] { }
            };

            CombatTimelineTrack reactionTrack = new CombatTimelineTrack("Reaction", CombatTimelineTrackKind.Reaction)
            {
                Clips = new CombatTimelineClip[]
                {
                    new CombatReactionWindowClip { Name = "Stun", CapabilityId = CombatTimelineCapabilityId.Reaction_Stun, StartTime = 0f, EndTime = 0.96f, ReactionMotionId = PlayerActionMotionId.KnockdownBackward },
                    new CombatReactionWindowClip { Name = "Recovery", CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery, StartTime = 0.96f, EndTime = 1.3f },
                    new CombatReactionWindowClip { Name = "CanReturn", CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn, StartTime = 1.3f, EndTime = 1.6f }
                }
            };

            CombatTimelineTrack cancelTrack = new CombatTimelineTrack("Cancel", CombatTimelineTrackKind.Cancel)
            {
                Clips = CreateCancelClips(cancelCapabilities)
            };

            timeline.SetTracks(new[] { phaseTrack, reactionTrack, cancelTrack });
            return timeline;
        }

        private static PlayerReactionTimelineAsset CreateHitReactionTimeline(
            params CombatTimelineCapabilityId[] cancelCapabilities)
        {
            return CreateHitReactionTimeline(cancelCapabilities, true);
        }

        private static PlayerReactionTimelineAsset CreateHitReactionTimeline(
            CombatTimelineCapabilityId[] cancelCapabilities,
            bool includePhaseClips)
        {
            PlayerReactionTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerReaction,
                PlayerHitReactionState.TimelineId,
                "Player HitReaction",
                "SM_Reaction/Anim_HitReaction_Light",
                38f / 60f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.HitReaction);

            CombatTimelineTrack phaseTrack = new CombatTimelineTrack("Phase", CombatTimelineTrackKind.Phase)
            {
                Clips = includePhaseClips
                    ? new CombatTimelineClip[]
                    {
                        new CombatPhaseClip { Name = "Start", Phase = PlayerStatePhase.Start, StartTime = 0f, EndTime = 0.083333336f },
                        new CombatPhaseClip { Name = "Loop", Phase = PlayerStatePhase.Loop, StartTime = 0.083333336f, EndTime = 0.33333334f },
                        new CombatPhaseClip { Name = "Recovery", Phase = PlayerStatePhase.Recovery, StartTime = 0.33333334f, EndTime = 0.53333336f },
                        new CombatPhaseClip { Name = "Reset", Phase = PlayerStatePhase.Reset, StartTime = 0.53333336f, EndTime = 0.6333333f }
                    }
                    : new CombatTimelineClip[] { }
            };

            CombatTimelineTrack reactionTrack = new CombatTimelineTrack("Reaction", CombatTimelineTrackKind.Reaction)
            {
                Clips = new CombatTimelineClip[]
                {
                    new CombatReactionWindowClip { Name = "Stun", CapabilityId = CombatTimelineCapabilityId.Reaction_Stun, StartTime = 0f, EndTime = 0.33333334f, ReactionMotionId = PlayerActionMotionId.HitReactionLight },
                    new CombatReactionWindowClip { Name = "Recovery", CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery, StartTime = 0.33333334f, EndTime = 0.53333336f },
                    new CombatReactionWindowClip { Name = "CanReturn", CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn, StartTime = 0.53333336f, EndTime = 0.6333333f }
                }
            };

            CombatTimelineTrack cancelTrack = new CombatTimelineTrack("Cancel", CombatTimelineTrackKind.Cancel)
            {
                Clips = CreateCancelClips(cancelCapabilities, 0.53333336f, 0.6333333f)
            };

            timeline.SetTracks(new[] { phaseTrack, reactionTrack, cancelTrack });
            return timeline;
        }

        private static BossReactionTimelineAsset CreateBossReactionTimelineWithMotion(
            string reactionId,
            string bossMotionActionId,
            string profileWindowName)
        {
            BossReactionTimelineAsset timeline = ScriptableObject.CreateInstance<BossReactionTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                CombatTimelineActionKind.BossReaction,
                reactionId,
                reactionId,
                "Anim_" + reactionId,
                1f,
                60f);
            timeline.ConfigureReaction(CombatTimelineReactionType.HitReaction);

            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Reaction", CombatTimelineTrackKind.Reaction)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatReactionWindowClip { Name = "Stun", CapabilityId = CombatTimelineCapabilityId.Reaction_Stun, StartTime = 0f, EndTime = 0.5f },
                        new CombatReactionWindowClip { Name = "Recovery", CapabilityId = CombatTimelineCapabilityId.Reaction_Recovery, StartTime = 0.5f, EndTime = 1f },
                        new CombatReactionWindowClip { Name = "CanReturn", CapabilityId = CombatTimelineCapabilityId.Reaction_CanReturn, StartTime = 1f, EndTime = 1f }
                    }
                },
                new CombatTimelineTrack("Motion", CombatTimelineTrackKind.BossCodeMove)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatMotionReferenceClip
                        {
                            Name = "HitStaggerWrap",
                            MotionKind = CombatTimelineClipKind.BossCodeMove,
                            StartTime = 0f,
                            EndTime = 0.5f,
                            BossAttackId = bossMotionActionId,
                            ProfileWindowName = profileWindowName
                        }
                    }
                }
            });

            return timeline;
        }

        private static BossAttackTimelineAsset CreateBossAttackTimeline(string attackId)
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
                0.2f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Phase", CombatTimelineTrackKind.Phase)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatPhaseClip { Name = "Start", Phase = PlayerStatePhase.Start, StartTime = 0f, EndTime = 0.2f },
                        new CombatPhaseClip { Name = "Active", Phase = PlayerStatePhase.Active, StartTime = 0.2f, EndTime = 0.4f },
                        new CombatPhaseClip { Name = "Recovery", Phase = PlayerStatePhase.Recovery, StartTime = 0.4f, EndTime = 1f }
                    }
                },
                new CombatTimelineTrack("Hit", CombatTimelineTrackKind.HitNode)
                {
                    Clips = new CombatTimelineClip[]
                    {
                        new CombatHitNodeClip
                        {
                            Name = "BossHit",
                            StartTime = 0.2f,
                            EndTime = 0.4f,
                            AttackType = CombatAttackType.HeavyAttack,
                            ReactionIntent = CombatReactionIntent.HitReaction,
                            Damage = 10f,
                            PoiseDamage = 4f,
                            MaxHitsPerTarget = 1
                        }
                    }
                }
            });

            return timeline;
        }

        private static CombatTimelineClip[] CreateCancelClips(CombatTimelineCapabilityId[] capabilities)
        {
            return CreateCancelClips(capabilities, 1.3f, 1.6f);
        }

        private static CombatTimelineClip[] CreateCancelClips(CombatTimelineCapabilityId[] capabilities, float startTime, float endTime)
        {
            if (capabilities == null || capabilities.Length == 0)
            {
                return new CombatTimelineClip[] { };
            }

            CombatTimelineClip[] clips = new CombatTimelineClip[capabilities.Length];
            for (int i = 0; i < capabilities.Length; i++)
            {
                clips[i] = Cancel(capabilities[i].ToString(), capabilities[i], startTime, endTime);
            }

            return clips;
        }

        private static PlayerSkillTimelineAsset CreateSkill1Timeline()
        {
            PlayerSkillTimelineAsset timeline = ScriptableObject.CreateInstance<PlayerSkillTimelineAsset>();
            timeline.ConfigureIdentity(
                CombatTimelineOwner.Player,
                CombatTimelineActionKind.PlayerSkill,
                PlayerSkillActionResolver.Skill1ActionId,
                "Skill1",
                "SM_Skill1",
                1f,
                60f);
            timeline.ConfigureSkill(0f);
            timeline.SetTracks(new[]
            {
                new CombatTimelineTrack("Empty", CombatTimelineTrackKind.Marker)
                {
                    Clips = new CombatTimelineClip[] { }
                }
            });
            return timeline;
        }

        /// <summary>
        /// 创建具有足够 BE 的 Skill 测试上下文，并把当前顶层状态设为 Skill。
        /// </summary>
        /// <returns>可直接传入 PlayerSkillState.Enter 的独立测试上下文。</returns>
        private static PlayerStateContext CreateSkillContext()
        {
            PlayerStateContext context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 32f,
                MaxBetaEnergy = 32f
            });
            context.SetState(PlayerStateId.Skill);
            return context;
        }

        private static PlayerKnockdownState EnterKnockdown(out PlayerStateContext context)
        {
            context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 100f,
                MaxBetaEnergy = 100f
            });
            context.SetState(PlayerStateId.Knockdown);
            PlayerKnockdownState state = new PlayerKnockdownState();
            state.Enter(context);
            return state;
        }

        private static PlayerGuardState EnterGuardHit(out PlayerStateContext context)
        {
            context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 100f,
                MaxBetaEnergy = 100f
            });
            context.SetState(PlayerStateId.Guard);
            PlayerGuardState state = new PlayerGuardState();
            state.Enter(context);
            context.RequestGuardReaction(PlayerGuardReactionId.GuardHit);
            context.Input = new PlayerInputSnapshot { GuardHeld = true, Time = 0f };
            state.Tick(context, 0.016f);
            return state;
        }

        private static PlayerGuardState EnterPerfectGuard(out PlayerStateContext context)
        {
            context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 100f,
                MaxBetaEnergy = 100f
            });
            context.SetState(PlayerStateId.Guard);
            PlayerGuardState state = new PlayerGuardState();
            state.Enter(context);
            context.RequestGuardReaction(PlayerGuardReactionId.PerfectGuard);
            context.Input = new PlayerInputSnapshot { GuardHeld = true, Time = 0f };
            state.Tick(context, 0.016f);
            return state;
        }

        private static PlayerHitReactionState EnterHitReaction(out PlayerStateContext context)
        {
            context = new PlayerStateContext();
            context.InitializeResources(new CombatResourceSet
            {
                CurrentHp = 300f,
                MaxHp = 300f,
                BetaEnergy = 100f,
                MaxBetaEnergy = 100f
            });
            context.SetState(PlayerStateId.HitReaction);
            PlayerHitReactionState state = new PlayerHitReactionState();
            state.Enter(context);
            return state;
        }

        private static void RegisterTestTimeline(CombatTimelineActionAsset timeline)
        {
            Dictionary<string, CombatTimelineActionAsset> timelines =
                (Dictionary<string, CombatTimelineActionAsset>)typeof(CombatTimelineProvider)
                    .GetField("timelineById", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            timelines[timeline.ActionId] = timeline;
            CombatTimelineProvider.ResetRuntimeDataCache();
            typeof(CombatTimelineProvider)
                .GetField("initialized", BindingFlags.Static | BindingFlags.NonPublic)
                .SetValue(null, true);
        }

        private static int GetTimelineCacheCount()
        {
            Dictionary<string, CombatTimelineActionAsset> timelines =
                (Dictionary<string, CombatTimelineActionAsset>)typeof(CombatTimelineProvider)
                    .GetField("timelineById", BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            return timelines.Count;
        }

        private static int GetRuntimeCacheCount(string fieldName)
        {
            System.Collections.ICollection cache =
                (System.Collections.ICollection)typeof(CombatTimelineProvider)
                    .GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic)
                    .GetValue(null);
            return cache.Count;
        }

        private static CombatDefenseWindowClip Defense(string name, CombatTimelineCapabilityId capabilityId, float start, float end)
        {
            return new CombatDefenseWindowClip
            {
                Name = name,
                CapabilityId = capabilityId,
                StartTime = start,
                EndTime = end
            };
        }

        private static CombatMarkerClip Marker(string name, string markerId, float start, float end)
        {
            return new CombatMarkerClip
            {
                Name = name,
                CapabilityId = CombatTimelineCapabilityId.None,
                StartTime = start,
                EndTime = end,
                MarkerId = markerId
            };
        }
        private static CombatAttackWindowClip Input(string name, CombatTimelineCapabilityId capabilityId)
        {
            return Input(name, capabilityId, 0.3f, 0.8f);
        }

        private static CombatAttackWindowClip Input(string name, CombatTimelineCapabilityId capabilityId, float start, float end)
        {
            return new CombatAttackWindowClip
            {
                Name = name,
                CapabilityId = capabilityId,
                StartTime = start,
                EndTime = end
            };
        }

        private static CombatCancelWindowClip Cancel(string name, CombatTimelineCapabilityId capabilityId, float start, float end)
        {
            return new CombatCancelWindowClip
            {
                Name = name,
                CapabilityId = capabilityId,
                StartTime = start,
                EndTime = end
            };
        }
    }
}
