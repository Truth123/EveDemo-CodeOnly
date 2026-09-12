// 文件说明：验证 Demo 场景初始战斗资源与正式 Player/Boss Timeline 伤害基线。
// 所属模块：测试代码。
// 运行影响：仅在 Unity Test Runner 中只读加载场景和资产，不影响运行时战斗。

using System.Collections.Generic;
using NUnit.Framework;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class DemoCombatBalanceConfigurationTests
    {
        private const string DemoScenePath = "Assets/Scenes/Demo.unity";
        private const string PlayerTimelineFolder = "Assets/ScriptableObjects/CombatTimelines/Player";
        private const string BossTimelineFolder = "Assets/ScriptableObjects/CombatTimelines/Boss/Raven";

        [Test]
        public void DemoScene_CombatResourcesMatchStandardBalanceBaseline()
        {
            Scene demoScene = SceneManager.GetSceneByPath(DemoScenePath);
            bool openedForTest = !demoScene.IsValid() || !demoScene.isLoaded;
            if (openedForTest)
            {
                demoScene = EditorSceneManager.OpenScene(DemoScenePath, OpenSceneMode.Additive);
            }

            try
            {
                PlayerStateMachine player = FindComponentInScene<PlayerStateMachine>(demoScene);
                BossActor boss = FindComponentInScene<BossActor>(demoScene);

                Assert.That(player, Is.Not.Null);
                Assert.That(boss, Is.Not.Null);

                SerializedProperty playerResources = new SerializedObject(player).FindProperty("initialResources");
                Assert.That(playerResources, Is.Not.Null);
                Assert.That(playerResources.FindPropertyRelative("CurrentHp").floatValue, Is.EqualTo(300f));
                Assert.That(playerResources.FindPropertyRelative("MaxHp").floatValue, Is.EqualTo(300f));
                Assert.That(playerResources.FindPropertyRelative("BetaEnergy").floatValue, Is.EqualTo(0f));
                Assert.That(playerResources.FindPropertyRelative("MaxBetaEnergy").floatValue, Is.EqualTo(32f));

                CombatResourceComponent bossResources = boss.GetComponent<CombatResourceComponent>();
                Assert.That(bossResources, Is.Not.Null);
                Assert.That(bossResources.CurrentHp, Is.EqualTo(1500f));
                Assert.That(bossResources.MaxHp, Is.EqualTo(1500f));
                Assert.That(boss.MaxShieldDefense, Is.EqualTo(15));

                SerializedProperty stunDuration = new SerializedObject(boss).FindProperty("shieldBreakStunDuration");
                Assert.That(stunDuration, Is.Not.Null);
                Assert.That(stunDuration.floatValue, Is.EqualTo(6f));
            }
            finally
            {
                if (openedForTest && demoScene.IsValid() && demoScene.isLoaded)
                {
                    EditorSceneManager.CloseScene(demoScene, true);
                }
            }
        }

        [Test]
        public void PlayerTimelines_DamageMatchesConfirmedBaseline()
        {
            AssertPlayerAttackDamage("Attack_L1.asset", 10f, CombatAttackType.LightAttack);
            AssertPlayerAttackDamage("Attack_L2.asset", 12f, CombatAttackType.LightAttack);
            AssertPlayerAttackDamage("Attack_L3.asset", 14f, CombatAttackType.LightAttack);
            AssertPlayerAttackDamage("Attack_L4.asset", 18f, CombatAttackType.HeavyAttack);
            AssertPlayerAttackDamage("Attack_H1.asset", 22f, CombatAttackType.HeavyAttack);
            AssertPlayerAttackDamage("Attack_H2.asset", 28f, CombatAttackType.HeavyAttack);

            PlayerSkillTimelineAsset skill = LoadAsset<PlayerSkillTimelineAsset>(
                $"{PlayerTimelineFolder}/Skill_Skill1.asset");
            Assert.That(skill.SkillCost, Is.EqualTo(8f));

            Dictionary<string, float> expectedDamage = new Dictionary<string, float>
            {
                { "Skill1_Hit1", 60f },
                { "Skill1_Hit2", 60f },
                { "Skill1_Hit3", 80f }
            };
            AssertHitNodeDamage(skill, expectedDamage);
            Assert.That(SumFullDamage(skill), Is.EqualTo(200f));
        }

        [Test]
        public void BossTimelines_FullDamageMatchesConfirmedBaseline()
        {
            Dictionary<string, float> expectedDamage = new Dictionary<string, float>
            {
                { "Raven_RapidMoveBack.asset", 0f },
                { "Raven_Slash.asset", 25f },
                { "Raven_SlashChain.asset", 46f },
                { "Raven_EvadeBackRush.asset", 52f },
                { "Raven_MoveCombo.asset", 66f },
                { "Raven_MoveChainCombo.asset", 132f },
                { "Raven_ChaseCombo.asset", 94f },
                { "Raven_ChaseGrab.asset", 36f },
                { "Raven_SlashCombo.asset", 112f },
                { "Raven_BetaChargeCombo.asset", 82f },
                { "Raven_BurstAreaSlash.asset", 58f },
                { "Raven_EvadeBackSwordAura.asset", 34f },
                { "Raven_SwordAuraCombo.asset", 82f }
            };

            foreach (KeyValuePair<string, float> expected in expectedDamage)
            {
                BossAttackTimelineAsset timeline = LoadAsset<BossAttackTimelineAsset>(
                    $"{BossTimelineFolder}/{expected.Key}");
                Assert.That(SumFullDamage(timeline), Is.EqualTo(expected.Value), timeline.ActionId);
            }
        }

        /// <summary>
        /// 在指定场景的根节点及其子层级中查找第一个目标组件，不跨场景读取同类型对象。
        /// </summary>
        /// <typeparam name="T">需要查找的 Unity Component 类型。</typeparam>
        /// <param name="scene">限定组件搜索范围的已加载场景。</param>
        /// <returns>找到时返回目标组件；场景中不存在时返回 null。</returns>
        private static T FindComponentInScene<T>(Scene scene) where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                T component = root.GetComponentInChildren<T>(true);
                if (component != null)
                {
                    return component;
                }
            }

            return null;
        }

        /// <summary>
        /// 加载正式平衡资产，并在路径或类型配置错误时立即让测试失败。
        /// </summary>
        /// <typeparam name="T">期望加载的 UnityEngine.Object 资产类型。</typeparam>
        /// <param name="assetPath">Assets 下的完整项目相对路径。</param>
        /// <returns>成功加载的目标资产。</returns>
        private static T LoadAsset<T>(string assetPath) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            Assert.That(asset, Is.Not.Null, assetPath);
            return asset;
        }

        /// <summary>
        /// 验证一个玩家普通攻击资产的单次伤害和攻击反馈类型。
        /// </summary>
        /// <param name="fileName">Player Timeline 目录中的资产文件名。</param>
        /// <param name="expectedDamage">确认后的单次 HP 伤害。</param>
        /// <param name="expectedAttackType">确认后的 Light/Heavy 攻击来源语义。</param>
        private static void AssertPlayerAttackDamage(
            string fileName,
            float expectedDamage,
            CombatAttackType expectedAttackType)
        {
            PlayerAttackTimelineAsset timeline = LoadAsset<PlayerAttackTimelineAsset>(
                $"{PlayerTimelineFolder}/{fileName}");
            Assert.That(timeline.Damage, Is.EqualTo(expectedDamage), timeline.ActionId);
            Assert.That(timeline.AttackType, Is.EqualTo(expectedAttackType), timeline.ActionId);
        }

        /// <summary>
        /// 按 HitNode 名称验证多段 Timeline 的逐段伤害，并拒绝多余或缺失节点。
        /// </summary>
        /// <param name="timeline">需要验证的 Skill Timeline。</param>
        /// <param name="expectedDamage">HitNode 名称到确认伤害的完整映射。</param>
        private static void AssertHitNodeDamage(
            PlayerSkillTimelineAsset timeline,
            IReadOnlyDictionary<string, float> expectedDamage)
        {
            int actualCount = 0;
            foreach (CombatHitNodeClip hitNode in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                actualCount++;
                Assert.That(expectedDamage.ContainsKey(hitNode.Name), Is.True, hitNode.Name);
                Assert.That(hitNode.Damage, Is.EqualTo(expectedDamage[hitNode.Name]), hitNode.Name);
                Assert.That(hitNode.MaxHitsPerTarget, Is.EqualTo(1), hitNode.Name);
            }

            Assert.That(actualCount, Is.EqualTo(expectedDamage.Count));
        }

        /// <summary>
        /// 汇总 Timeline 全部 HitNode 对同一目标理论完整命中时的最大 HP 伤害。
        /// </summary>
        /// <param name="timeline">提供正式 HitNode 数据的 Combat Timeline。</param>
        /// <returns>所有 HitNode 的 Damage × MaxHitsPerTarget 总和。</returns>
        private static float SumFullDamage(CombatTimelineActionAsset timeline)
        {
            float totalDamage = 0f;
            foreach (CombatHitNodeClip hitNode in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                totalDamage += hitNode.Damage * Mathf.Max(1, hitNode.MaxHitsPerTarget);
            }

            return totalDamage;
        }
    }
}
