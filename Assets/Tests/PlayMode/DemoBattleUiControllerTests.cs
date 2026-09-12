// 文件说明：验证 Demo 标题、静态战斗操作提示、调试入口清理、战斗背景音乐、世界空间锁定提示、胜负优先级和结算计时文本的展示层契约。
// 所属模块：PlayMode 测试。
// 运行影响：仅在 Unity Test Runner 中创建临时 UGUI 层级，不修改正式场景或战斗状态。

using System.Reflection;
using NUnit.Framework;
using ProjectEVE.UI.Demo;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectEVE.Tests.PlayMode
{
    public sealed class DemoBattleUiControllerTests
    {
        [TearDown]
        public void TearDown()
        {
            Time.timeScale = 1f;
        }

        [Test]
        public void Awake_DefaultStartup_ShowsTitleAndHidesCombatGroups()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_Root");
            root.SetActive(false);
            try
            {
                root.AddComponent<DemoBattleUiController>();
                root.SetActive(true);

                CanvasGroup title = FindCanvasGroup(root.transform, "TitleScreen");
                CanvasGroup hud = FindCanvasGroup(root.transform, "HUD");
                CanvasGroup debug = FindCanvasGroup(root.transform, "DebugOverlay");
                CanvasGroup result = FindCanvasGroup(root.transform, "ResultScreen");
                Transform leftDebugPrompt = root.transform.Find("DemoBattleUICanvas/DebugOverlay/TrainingPromptPanel");
                Transform bossAiDebugPanel = root.transform.Find("DemoBattleUICanvas/DebugOverlay/BossAiDebugPanel");
                Transform titleScanlines = root.transform.Find("DemoBattleUICanvas/TitleScreen/Scanlines");
                Transform overlayLockOnIndicator = root.transform.Find("DemoBattleUICanvas/LockOnIndicator");
                RectTransform pausePanel = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel") as RectTransform;
                Transform pauseResumeButton = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel/ResumeButton");
                Transform pauseRestartButton = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel/RestartButton");
                Transform pauseQuitButton = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel/QuitButton");
                Transform pauseBossButton = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel/BossButton");
                Transform pauseDebugButton = root.transform.Find("DemoBattleUICanvas/PauseMenu/PausePanel/DebugButton");
                RectTransform bossHud = root.transform.Find("DemoBattleUICanvas/HUD/BossHUD") as RectTransform;
                RectTransform bossHpBar = root.transform.Find("DemoBattleUICanvas/HUD/BossHUD/BossHpBar") as RectTransform;
                RectTransform bossShieldBar = root.transform.Find("DemoBattleUICanvas/HUD/BossHUD/BossShieldBar") as RectTransform;

                Assert.That(title.alpha, Is.EqualTo(1f));
                Assert.That(title.interactable, Is.True);
                Assert.That(title.blocksRaycasts, Is.True);
                Assert.That(hud.alpha, Is.EqualTo(0f));
                Assert.That(debug.alpha, Is.EqualTo(0f));
                Assert.That(result.alpha, Is.EqualTo(0f));
                Assert.That(leftDebugPrompt, Is.Null);
                Assert.That(bossAiDebugPanel, Is.Not.Null);
                Assert.That(titleScanlines, Is.Null);
                Assert.That(overlayLockOnIndicator, Is.Null);
                Assert.That(pausePanel, Is.Not.Null);
                Assert.That(pausePanel.sizeDelta.y, Is.EqualTo(310f));
                Assert.That(pauseResumeButton, Is.Not.Null);
                Assert.That(pauseRestartButton, Is.Not.Null);
                Assert.That(pauseQuitButton, Is.Not.Null);
                Assert.That(pauseBossButton, Is.Null);
                Assert.That(pauseDebugButton, Is.Null);
                Assert.That(bossHud, Is.Not.Null);
                Assert.That(bossHud.anchorMin, Is.EqualTo(new Vector2(0.5f, 1f)));
                Assert.That(bossHud.anchorMax, Is.EqualTo(new Vector2(0.5f, 1f)));
                Assert.That(bossHud.anchoredPosition.x, Is.Zero);
                Assert.That(bossHpBar, Is.Not.Null);
                Assert.That(bossHpBar.anchoredPosition.x, Is.Zero);
                Assert.That(bossShieldBar, Is.Not.Null);
                Assert.That(bossShieldBar.anchoredPosition.x, Is.Zero);
                Assert.That(Time.timeScale, Is.EqualTo(1f));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void Awake_ControlGuide_ShowsSixStaticBindingsAndDoesNotCreateSkillWheel()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_ControlGuideRoot");
            root.SetActive(false);
            try
            {
                root.AddComponent<DemoBattleUiController>();
                root.SetActive(true);

                RectTransform guide = root.transform.Find("DemoBattleUICanvas/HUD/ControlGuide") as RectTransform;
                Transform skillWheel = root.transform.Find("DemoBattleUICanvas/HUD/SkillWheel");
                CanvasGroup hud = FindCanvasGroup(root.transform, "HUD");

                Assert.That(guide, Is.Not.Null);
                Assert.That(skillWheel, Is.Null);
                Assert.That(guide.parent, Is.EqualTo(hud.transform));
                Assert.That(guide.anchorMin, Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(guide.anchorMax, Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(guide.pivot, Is.EqualTo(new Vector2(1f, 0f)));
                Assert.That(guide.anchoredPosition, Is.EqualTo(new Vector2(-72f, 72f)));
                Assert.That(guide.sizeDelta, Is.EqualTo(new Vector2(620f, 230f)));
                Assert.That(guide.Find("Title")?.GetComponent<Text>()?.text, Is.EqualTo("H 键隐藏"));
                Assert.That(guide.Find("MouseColumnLabel")?.GetComponent<Text>()?.text, Is.EqualTo("鼠标"));
                Assert.That(guide.Find("KeyboardColumnLabel")?.GetComponent<Text>()?.text, Is.EqualTo("键盘"));

                string[,] bindings =
                {
                    { "MouseLightAttack", "鼠标左键", "轻攻击" },
                    { "MouseHeavyAttack", "鼠标右键", "重攻击" },
                    { "MouseLockOn", "鼠标中键", "锁定" },
                    { "KeyboardEvade", "Shift", "闪避" },
                    { "KeyboardGuard", "E", "防御" },
                    { "KeyboardSkill1", "1", "释放技能" }
                };

                for (int index = 0; index < bindings.GetLength(0); index++)
                {
                    Transform row = guide.Find(bindings[index, 0]);
                    Text keyText = row != null
                        ? row.Find("Key/Text")?.GetComponent<Text>()
                        : null;
                    Text actionText = row != null
                        ? row.Find("Action")?.GetComponent<Text>()
                        : null;

                    Assert.That(row, Is.Not.Null, bindings[index, 0]);
                    Assert.That(keyText, Is.Not.Null, $"{bindings[index, 0]} key");
                    Assert.That(actionText, Is.Not.Null, $"{bindings[index, 0]} action");
                    Assert.That(keyText.text, Is.EqualTo(bindings[index, 1]));
                    Assert.That(actionText.text, Is.EqualTo(bindings[index, 2]));
                }

                Assert.That(guide.GetComponentsInChildren<Button>(true), Is.Empty);
                foreach (Graphic graphic in guide.GetComponentsInChildren<Graphic>(true))
                {
                    Assert.That(graphic.raycastTarget, Is.False, graphic.name);
                }
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ControlGuideVisibility_CanToggleWithoutChangingHudVisibility()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_ControlGuideVisibilityRoot");
            root.SetActive(false);
            try
            {
                DemoBattleUiController controller = root.AddComponent<DemoBattleUiController>();
                root.SetActive(true);

                GameObject guide = root.transform.Find("DemoBattleUICanvas/HUD/ControlGuide")?.gameObject;
                CanvasGroup hud = FindCanvasGroup(root.transform, "HUD");
                MethodInfo setControlGuideVisible = typeof(DemoBattleUiController).GetMethod(
                    "SetControlGuideVisible",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(guide, Is.Not.Null);
                Assert.That(setControlGuideVisible, Is.Not.Null);
                Assert.That(guide.activeSelf, Is.True);
                float initialHudAlpha = hud.alpha;

                setControlGuideVisible.Invoke(controller, new object[] { false });
                Assert.That(guide.activeSelf, Is.False);
                Assert.That(hud.alpha, Is.EqualTo(initialHudAlpha));

                setControlGuideVisible.Invoke(controller, new object[] { true });
                Assert.That(guide.activeSelf, Is.True);
                Assert.That(hud.alpha, Is.EqualTo(initialHudAlpha));
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void DeveloperShortcutsAndPauseButtons_AreRemovedWhileEscapePauseInputRemains()
        {
            BindingFlags flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.NonPublic;
            string[] removedMethods =
            {
                "HandleHotkeys",
                "IsTabHeld",
                "WasPressedDigit1",
                "WasPressedDigit2",
                "WasPressedH",
                "WasPressedC",
                "RefreshPauseMenuText",
                "ToggleBoss"
            };

            foreach (string methodName in removedMethods)
            {
                Assert.That(typeof(DemoBattleUiController).GetMethod(methodName, flags), Is.Null, methodName);
            }

            Assert.That(typeof(DemoBattleUiController).GetMethod("HandlePauseInput", flags), Is.Not.Null);
            Assert.That(typeof(DemoBattleUiController).GetMethod("WasPressedEscape", flags), Is.Not.Null);
            Assert.That(typeof(DemoBattleUiController).GetMethod("HandleControlGuideInput", flags), Is.Not.Null);
            Assert.That(typeof(DemoBattleUiController).GetMethod("WasPressedControlGuideToggle", flags), Is.Not.Null);
        }

        [Test]
        public void BossAiDebug_DefaultsHiddenAndCanStillBeEnabledByConfiguration()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_DebugRoot");
            root.SetActive(false);
            try
            {
                DemoBattleUiController controller = root.AddComponent<DemoBattleUiController>();
                FieldInfo showDebugOnStart = typeof(DemoBattleUiController).GetField(
                    "showDebugOnStart",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo setDebugVisible = typeof(DemoBattleUiController).GetMethod(
                    "SetDebugVisible",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(showDebugOnStart, Is.Not.Null);
                Assert.That(setDebugVisible, Is.Not.Null);
                Assert.That(showDebugOnStart.GetValue(controller), Is.False);

                root.SetActive(true);
                CanvasGroup debug = FindCanvasGroup(root.transform, "DebugOverlay");
                Assert.That(debug.alpha, Is.Zero);

                setDebugVisible.Invoke(controller, new object[] { true });
                Assert.That(debug.alpha, Is.EqualTo(1f));

                setDebugVisible.Invoke(controller, new object[] { false });
                Assert.That(debug.alpha, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void SetLockOnIndicatorVisible_ReplaysAndClearsBoundWorldMarker()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_WorldMarkerRoot");
            GameObject markerObject = new GameObject("FX_LockOnMarker", typeof(ParticleSystem));
            root.SetActive(false);
            markerObject.SetActive(false);
            try
            {
                DemoBattleUiController controller = root.AddComponent<DemoBattleUiController>();
                ParticleSystem marker = markerObject.GetComponent<ParticleSystem>();
                typeof(DemoBattleUiController)
                    .GetField("lockOnIndicatorWorldMarker", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, marker);
                root.SetActive(true);

                MethodInfo setVisible = typeof(DemoBattleUiController).GetMethod(
                    "SetLockOnIndicatorVisible",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                setVisible.Invoke(controller, new object[] { true });
                Assert.That(markerObject.activeSelf, Is.True);
                Assert.That(marker.isPlaying, Is.True);

                setVisible.Invoke(controller, new object[] { false });
                Assert.That(markerObject.activeSelf, Is.False);
                Assert.That(marker.particleCount, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(markerObject);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void StartCombatFromTitle_PlaysBackgroundMusicBeforeOpeningTransitionCompletes()
        {
            GameObject root = new GameObject("DemoBattleUiControllerTests_BackgroundMusicRoot");
            root.SetActive(false);
            AudioClip clip = AudioClip.Create("BackgroundMusicTestClip", 4410, 1, 44100, false);
            try
            {
                AudioSource source = root.AddComponent<AudioSource>();
                source.clip = clip;
                DemoBattleUiController controller = root.AddComponent<DemoBattleUiController>();
                typeof(DemoBattleUiController)
                    .GetField("backgroundMusicSource", BindingFlags.Instance | BindingFlags.NonPublic)
                    .SetValue(controller, source);
                root.SetActive(true);

                MethodInfo startCombat = typeof(DemoBattleUiController).GetMethod(
                    "StartCombatFromTitle",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo stop = typeof(DemoBattleUiController).GetMethod(
                    "StopBackgroundMusic",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                startCombat.Invoke(controller, null);
                Assert.That(source.playOnAwake, Is.False);
                Assert.That(source.loop, Is.True);
                Assert.That(source.spatialBlend, Is.Zero);
                Assert.That(source.volume, Is.EqualTo(0.22f).Within(0.001f));
                Assert.That(source.isPlaying, Is.True);

                stop.Invoke(controller, null);
                Assert.That(source.isPlaying, Is.False);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void ResolveDeathPresentationState_SimultaneousDeathUsesDefeat()
        {
            MethodInfo method = typeof(DemoBattleUiController).GetMethod(
                "ResolveDeathPresentationState",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            object result = method.Invoke(null, new object[] { true, true });

            Assert.That(result.ToString(), Is.EqualTo("PendingDefeat"));
        }

        [TestCase(0f, "00:00.00")]
        [TestCase(65.429f, "01:05.42")]
        [TestCase(-2f, "00:00.00")]
        public void FormatBattleTime_UsesMinuteSecondCentisecondFormat(float seconds, string expected)
        {
            MethodInfo method = typeof(DemoBattleUiController).GetMethod(
                "FormatBattleTime",
                BindingFlags.Static | BindingFlags.NonPublic);

            Assert.That(method, Is.Not.Null);
            object result = method.Invoke(null, new object[] { seconds });

            Assert.That(result, Is.EqualTo(expected));
        }

        /// <summary>
        /// 从测试控制器生成的 Canvas 中查找指定展示组，并在缺失时立即报告契约失败。
        /// </summary>
        /// <param name="root">挂载 DemoBattleUiController 的临时根节点。</param>
        /// <param name="name">Canvas 下需要查找的 CanvasGroup 名称。</param>
        /// <returns>名称匹配且已挂载 CanvasGroup 的运行时 UI 节点。</returns>
        private static CanvasGroup FindCanvasGroup(Transform root, string name)
        {
            Transform child = root.Find($"DemoBattleUICanvas/{name}");
            Assert.That(child, Is.Not.Null, $"Missing runtime UI group: {name}");
            return child.GetComponent<CanvasGroup>();
        }
    }
}
