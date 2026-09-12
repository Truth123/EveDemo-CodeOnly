// 文件说明：验证 BossAnimationBridge 的状态到 Raven Animator 状态映射。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中创建临时 Animator，不影响正式场景。

using System.Linq;
using System.Reflection;
using NUnit.Framework;
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Combat.Timeline;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossAnimationBridgeTests
    {
        private const string RavenControllerPath = "Assets/Animator/Raven.controller";
        private const string DeadTimelinePath =
            "Assets/ScriptableObjects/CombatTimelines/Boss/Raven/Reaction_Boss_Dead.asset";
        private const string DeathStateName = "CH_P_EVE_51|Eve_Stand_Dead2";
        private const string PerfectGuardStaggerStateName = "Result_Hit_JustParry";
        private const string ShieldBreakStunStateName = "Result_ShieldBreak_Stun";
        private const string ShieldBreakStunClipName = "CH_M_NA_53_Preview|Result_Weak_S";

        [Test]
        public void PlayState_Dead_UsesDedicatedRavenDeathState()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(RavenControllerPath);
            Assert.That(controller, Is.Not.Null);

            GameObject boss = new GameObject("BossAnimationBridgeDeathTest");
            try
            {
                Animator animator = boss.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                BossAnimationBridge bridge = boss.AddComponent<BossAnimationBridge>();

                bridge.PlayState(BossStateId.Dead);

                int expectedStateHash = Animator.StringToHash($"Base Layer.{DeathStateName}");
                Assert.That(animator.HasState(0, expectedStateHash), Is.True);
                Assert.That(ReadCurrentStateHash(bridge), Is.EqualTo(expectedStateHash));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        [Test]
        public void ReactionBossDead_UsesDedicatedNonLoopingDeathAnimation()
        {
            BossReactionTimelineAsset timeline =
                AssetDatabase.LoadAssetAtPath<BossReactionTimelineAsset>(DeadTimelinePath);
            Assert.That(timeline, Is.Not.Null);
            Assert.That(timeline.AnimationStateName, Is.EqualTo(DeathStateName));

            CombatAnimationClipWindow[] previewWindows = timeline.Tracks
                .SelectMany(track => track.Clips)
                .OfType<CombatAnimationClipWindow>()
                .ToArray();
            Assert.That(previewWindows, Has.Length.EqualTo(1));
            Assert.That(previewWindows[0].AnimatorStateName, Is.EqualTo(DeathStateName));
            Assert.That(previewWindows[0].PreviewAnimationClip, Is.Not.Null);
            Assert.That(previewWindows[0].LoopPreview, Is.False);
        }

        [Test]
        public void ShieldBreakStunState_UsesWeakAnimationAndHoldsWithoutAutomaticTransition()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(RavenControllerPath);
            Assert.That(controller, Is.Not.Null);

            AnimatorState state = controller.layers[0].stateMachine.states
                .Select(childState => childState.state)
                .Single(candidate => candidate.name == ShieldBreakStunStateName);

            Assert.That(state.motion, Is.TypeOf<AnimationClip>());
            Assert.That(state.motion.name, Is.EqualTo(ShieldBreakStunClipName));
            Assert.That(state.speed, Is.EqualTo(2.5f).Within(0.0001f));
            Assert.That(state.transitions, Is.Empty);
        }

        [Test]
        public void ShieldBreakAnimationSequence_MapsJustParryLeadInThenWeakStun()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(RavenControllerPath);
            Assert.That(controller, Is.Not.Null);

            GameObject boss = new GameObject("BossAnimationBridgeShieldBreakSequenceTest");
            try
            {
                Animator animator = boss.AddComponent<Animator>();
                animator.runtimeAnimatorController = controller;
                BossAnimationBridge bridge = boss.AddComponent<BossAnimationBridge>();

                bridge.PlayPerfectGuardStagger();

                int justParryHash = Animator.StringToHash($"Base Layer.{PerfectGuardStaggerStateName}");
                Assert.That(animator.HasState(0, justParryHash), Is.True);
                Assert.That(ReadCurrentStateHash(bridge), Is.EqualTo(justParryHash));

                bridge.PlayShieldBreakStun();

                int weakStunHash = Animator.StringToHash($"Base Layer.{ShieldBreakStunStateName}");
                Assert.That(animator.HasState(0, weakStunHash), Is.True);
                Assert.That(ReadCurrentStateHash(bridge), Is.EqualTo(weakStunHash));
            }
            finally
            {
                Object.DestroyImmediate(boss);
            }
        }

        /// <summary>
        /// 读取动画桥接器最后一次成功切换的完整状态哈希，用于验证状态映射已通过 Animator.HasState 校验。
        /// </summary>
        /// <param name="bridge">已经执行状态播放的动画桥接器。</param>
        /// <returns>桥接器缓存的 Base Layer 完整状态哈希。</returns>
        private static int ReadCurrentStateHash(BossAnimationBridge bridge)
        {
            FieldInfo field = typeof(BossAnimationBridge).GetField(
                "currentStateHash",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            return (int)field.GetValue(bridge);
        }
    }
}
