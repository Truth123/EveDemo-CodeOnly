using NUnit.Framework;
using ProjectEVE.Boss.Animation;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectEVE.Tests.EditMode
{
    public sealed class BossBodyColorControllerTests
    {
        private static readonly string[] BodyMaterialPaths =
        {
            "Assets/Materials/Raven/MI_CH_NPC_Raven_01_Suit01.mat",
            "Assets/Materials/Raven/MI_CH_NPC_Raven_01_BaseBody.mat",
            "Assets/Materials/Raven/MI_CH_NPC_Raven_01_Suit02.mat",
            "Assets/Materials/Raven/MI_RavensuitVinyl.mat",
            "Assets/Materials/Raven/MI_CH_M_NA_53_Head.mat",
            "Assets/Materials/Raven/MI_CH_M_NA_53_Hair02.mat",
            "Assets/Materials/Raven/MI_CH_M_NA_53_Hair01.mat"
        };

        private const string WeaponMaterialPath = "Assets/Materials/Raven/MI_CH_M_NA_53_Weapon.mat";
        private const string GlowMaterialPath = "Assets/Materials/Feedback/M_CombatVfx_Glow.mat";
        private const string UrpLitShaderName = "Universal Render Pipeline/Lit";
        private const string BodyOverlayMaterialPath =
            "Assets/Materials/Raven/M_Raven_BodyColorOverlay.mat";
        private const string RavenControllerPath = "Assets/Animator/Raven.controller";
        private const string RavenAnimationPath =
            "Assets/ThirtPartyModel/EveAnimation/Anim/RavenMonster_Animation.fbx";
        private const string RavenAuthoredAnimationFolder = "Assets/Animator/RavenAuthored";
        private const string LegacyBodyColorAnimationFolder = "Assets/Animator/RavenBodyColor";

        private static readonly string[] MergedActionStateNames =
        {
            "M_Raven_Slash",
            "M_Raven_SlashChain",
            "M_Raven_EvadeBackRush",
            "M_Raven_ChaseCombo",
            "M_Raven_ChaseGrab",
            "M_Raven_MoveChainCombo",
            "M_Raven_MoveCombo",
            "M_Raven_SlashCombo",
            "M_Raven_BetaChargeCombo",
            "M_Raven_BurstAreaSlash",
            "M_Raven_BurstAreaSlashEnd",
            "M_Raven_EvadeBackSwordAura",
            "M_Raven_RapidMoveBack",
            "M_Raven_SwordAuraCombo"
        };

        private static readonly string[] BodyColorPropertyNames =
        {
            "bodyColor.r",
            "bodyColor.g",
            "bodyColor.b",
            "bodyColor.a",
            "visibility"
        };

        private static readonly string[] HairColorPropertyNames =
        {
            "hairColor.r",
            "hairColor.g",
            "hairColor.b",
            "hairColor.a"
        };

        private GameObject root;
        private SkinnedMeshRenderer bodyRenderer;
        private SkinnedMeshRenderer bodyOverlayRenderer;
        private MeshRenderer weaponRenderer;
        private LineRenderer[] glowLines;
        private MeshRenderer glowBaseRenderer;
        private Light[] glowLights;
        private BossBodyColorController controller;
        private Material[] bodyMaterials;
        private Material weaponMaterial;
        private Material glowMaterial;
        private Material bodyOverlayMaterial;
        private Color[] originalLineStartColors;
        private Color[] originalLineEndColors;
        private float[] originalLightIntensities;

        [SetUp]
        public void SetUp()
        {
            bodyMaterials = new Material[BodyMaterialPaths.Length];
            for (int i = 0; i < BodyMaterialPaths.Length; i++)
            {
                bodyMaterials[i] = AssetDatabase.LoadAssetAtPath<Material>(BodyMaterialPaths[i]);
                Assert.That(bodyMaterials[i], Is.Not.Null, BodyMaterialPaths[i]);
            }

            weaponMaterial = AssetDatabase.LoadAssetAtPath<Material>(WeaponMaterialPath);
            glowMaterial = AssetDatabase.LoadAssetAtPath<Material>(GlowMaterialPath);
            bodyOverlayMaterial = AssetDatabase.LoadAssetAtPath<Material>(BodyOverlayMaterialPath);
            Assert.That(weaponMaterial, Is.Not.Null);
            Assert.That(glowMaterial, Is.Not.Null);
            Assert.That(bodyOverlayMaterial, Is.Not.Null);

            root = new GameObject("BossBodyColorControllerTests");
            bodyRenderer = root.AddComponent<SkinnedMeshRenderer>();
            bodyRenderer.sharedMaterials = bodyMaterials;
            bodyOverlayRenderer = new GameObject("RavenBodyColorOverlay").AddComponent<SkinnedMeshRenderer>();
            bodyOverlayRenderer.transform.SetParent(root.transform, false);
            bodyOverlayRenderer.sharedMesh = bodyRenderer.sharedMesh;
            bodyOverlayRenderer.rootBone = bodyRenderer.rootBone;
            bodyOverlayRenderer.bones = bodyRenderer.bones;
            Material[] overlayMaterials = new Material[bodyMaterials.Length];
            for (int i = 0; i < overlayMaterials.Length; i++)
            {
                overlayMaterials[i] = bodyOverlayMaterial;
            }
            bodyOverlayRenderer.sharedMaterials = overlayMaterials;
            bodyOverlayRenderer.enabled = false;

            GameObject weapon = new GameObject("CH_M_NA_53_Weapon.001");
            weapon.transform.SetParent(root.transform, false);
            weaponRenderer = weapon.AddComponent<MeshRenderer>();
            weaponRenderer.sharedMaterial = weaponMaterial;

            GameObject glowRoot = new GameObject("FX_BossWeaponIdleGlow");
            glowRoot.transform.SetParent(weapon.transform, false);
            glowLines = new[]
            {
                CreateGlowLine(glowRoot.transform, "FX_BossWeaponIdleGlow_CoreLine", 0.9f, 0.3f),
                CreateGlowLine(glowRoot.transform, "FX_BossWeaponIdleGlow_LeftEdge", 0.5f, 0.15f),
                CreateGlowLine(glowRoot.transform, "FX_BossWeaponIdleGlow_RightEdge", 0.5f, 0.15f)
            };
            glowBaseRenderer = CreateGlowBase(glowRoot.transform);
            glowLights = new[]
            {
                CreateGlowLight(glowRoot.transform, "FX_BossWeaponIdleGlow_TipLight", 0.22f),
                CreateGlowLight(glowRoot.transform, "FX_BossWeaponIdleGlow_BaseLight", 0.17f)
            };

            originalLineStartColors = new Color[glowLines.Length];
            originalLineEndColors = new Color[glowLines.Length];
            for (int i = 0; i < glowLines.Length; i++)
            {
                originalLineStartColors[i] = glowLines[i].startColor;
                originalLineEndColors[i] = glowLines[i].endColor;
            }

            originalLightIntensities = new float[glowLights.Length];
            for (int i = 0; i < glowLights.Length; i++)
            {
                originalLightIntensities[i] = glowLights[i].intensity;
            }

            controller = root.AddComponent<BossBodyColorController>();
            SetSerializedObjectReference(controller, "bodyRenderer", bodyRenderer);
            SetSerializedObjectReference(controller, "bodyColorOverlayRenderer", bodyOverlayRenderer);
            SetSerializedObjectReference(controller, "weaponRenderer", weaponRenderer);
            SetSerializedObjectArray(controller, "weaponIdleGlowLines", glowLines);
            SetSerializedObjectReference(controller, "weaponIdleGlowBaseRenderer", glowBaseRenderer);
            SetSerializedObjectArray(controller, "weaponIdleGlowLights", glowLights);
            controller.SendMessage("OnValidate");
        }

        [TearDown]
        public void TearDown()
        {
            if (root != null)
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void TransparentBodyColor_ClearsBodyOverlayWithoutChangingVisibility()
        {
            SetBodyColor(new Color(0f, 0f, 0f, 0f));
            SetVisibility(1f);

            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                AssertOverlayProperty(i, Color.clear);
            }
            Assert.That(bodyOverlayRenderer.enabled, Is.False);
            AssertWeaponColor(weaponMaterial.GetColor("_BaseColor"));
        }

        [Test]
        public void HdrBodyColor_AppliesTextureIndependentOverlayToEveryBodySlot()
        {
            Color bodyColor = new Color(0f, 2f, 4f, 0.5f);
            SetBodyColor(bodyColor);

            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                AssertOverlayProperty(i, bodyColor);
            }
            Assert.That(bodyOverlayRenderer.enabled, Is.True);
        }

        [Test]
        public void HdrHairColor_AppliesOnlyToRavenHairMaterialSlots()
        {
            Color hairColor = new Color(2.2f, 0.03f, 0.05f, 0.55f);
            SetHairColor(hairColor);

            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                AssertOverlayProperty(
                    i,
                    i == 5 || i == 6 ? hairColor : Color.clear);
            }
        }

        [Test]
        public void HairColor_CompositesOverBodyColorWithoutAffectingNonHairSlots()
        {
            Color body = new Color(2.8f, 1.65f, 0.12f, 0.5f);
            Color hair = new Color(2.2f, 0.03f, 0.05f, 0.5f);
            SetBodyColor(body);
            SetHairColor(hair);

            Color expectedHair = new Color(
                (2.2f * 0.5f + 2.8f * 0.25f) / 0.75f,
                (0.03f * 0.5f + 1.65f * 0.25f) / 0.75f,
                (0.05f * 0.5f + 0.12f * 0.25f) / 0.75f,
                0.75f);
            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                AssertOverlayProperty(
                    i,
                    i == 5 || i == 6 ? expectedHair : body);
            }
        }

        [Test]
        public void BodyColorAlpha_DoesNotFadeWeaponOrIdleGlow()
        {
            SetVisibility(1f);
            SetBodyColor(new Color(0f, 3f, 5f, 0.25f));

            AssertWeaponColor(weaponMaterial.GetColor("_BaseColor"));
            AssertIdleGlow(1f, true);
        }

        [Test]
        public void VisibilityHalf_LeavesBodyVisible_AndFadesWeaponAndIdleGlow()
        {
            SetVisibility(0.5f);

            Assert.That(bodyRenderer.enabled, Is.True);
            Assert.That(bodyOverlayRenderer.enabled, Is.False);
            Color expectedWeaponColor = weaponMaterial.GetColor("_BaseColor");
            expectedWeaponColor.a *= 0.5f;
            AssertWeaponColor(expectedWeaponColor);
            AssertIdleGlow(0.5f, true);
        }

        [Test]
        public void VisibilityZero_DisablesOnlyVisualComponents_AndCanRestoreThem()
        {
            SetVisibility(0f);

            Assert.That(bodyRenderer.enabled, Is.False);
            Assert.That(bodyOverlayRenderer.enabled, Is.False);
            Assert.That(weaponRenderer.enabled, Is.False);
            Assert.That(glowBaseRenderer.enabled, Is.False);
            AssertIdleGlow(0f, false);
            Assert.That(root.activeSelf, Is.True);
            Assert.That(weaponRenderer.gameObject.activeSelf, Is.True);

            SetVisibility(1f);

            Assert.That(bodyRenderer.enabled, Is.True);
            Assert.That(weaponRenderer.enabled, Is.True);
            Assert.That(glowBaseRenderer.enabled, Is.True);
            AssertIdleGlow(1f, true);
        }

        [Test]
        public void ApplyingAppearance_DoesNotReplaceSharedMaterialAssets()
        {
            Material[] bodyBefore = bodyRenderer.sharedMaterials;
            Material[] overlayBefore = bodyOverlayRenderer.sharedMaterials;
            Material weaponBefore = weaponRenderer.sharedMaterial;
            Material glowBefore = glowBaseRenderer.sharedMaterial;

            SetBodyColor(new Color(0f, 3f, 5f, 1f));
            SetVisibility(0.35f);

            Material[] bodyAfter = bodyRenderer.sharedMaterials;
            Material[] overlayAfter = bodyOverlayRenderer.sharedMaterials;
            for (int i = 0; i < bodyBefore.Length; i++)
            {
                Assert.That(bodyAfter[i], Is.SameAs(bodyBefore[i]));
                Assert.That(overlayAfter[i], Is.SameAs(overlayBefore[i]));
            }
            Assert.That(weaponRenderer.sharedMaterial, Is.SameAs(weaponBefore));
            Assert.That(glowBaseRenderer.sharedMaterial, Is.SameAs(glowBefore));
        }

        [Test]
        public void DisablingController_RestoresOriginalAppearanceAndEnabledStates()
        {
            SetBodyColor(new Color(0f, 3f, 5f, 1f));
            SetVisibility(0f);

            controller.enabled = false;

            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                AssertOverlayProperty(i, Color.clear);
            }
            Assert.That(bodyOverlayRenderer.enabled, Is.False);
            AssertWeaponColor(weaponMaterial.GetColor("_BaseColor"));
            Assert.That(bodyRenderer.enabled, Is.True);
            Assert.That(weaponRenderer.enabled, Is.True);
            Assert.That(glowBaseRenderer.enabled, Is.True);
            AssertIdleGlow(1f, true);
        }

        [Test]
        public void RavenBodyMaterials_UseOriginalDepthModes_WhileWeaponRemainsTransparent()
        {
            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                Material material = bodyMaterials[i];
                Assert.That(material.shader.name, Is.EqualTo(UrpLitShaderName));
                Assert.That(material.GetFloat("_Surface"), Is.EqualTo(0f));
                Assert.That(material.GetFloat("_ZWrite"), Is.EqualTo(1f));
                Assert.That(material.renderQueue, Is.LessThan(3000));
            }

            Material hair01 = bodyMaterials[6];
            Assert.That(hair01.GetFloat("_AlphaClip"), Is.EqualTo(1f));
            Assert.That(hair01.renderQueue, Is.EqualTo(2450));

            for (int i = 0; i < bodyMaterials.Length - 1; i++)
            {
                Assert.That(bodyMaterials[i].GetFloat("_AlphaClip"), Is.EqualTo(0f));
            }

            Assert.That(weaponMaterial.GetFloat("_Surface"), Is.EqualTo(1f));
            Assert.That(weaponMaterial.GetFloat("_BlendModePreserveSpecular"), Is.EqualTo(0f));
            Assert.That(weaponMaterial.renderQueue, Is.GreaterThanOrEqualTo(3000));
        }

        [Test]
        public void RavenMergedAnimations_KeepRootMotionAndBodyColorOnBaseLayer()
        {
            AnimatorController animatorController =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(RavenControllerPath);
            Assert.That(animatorController, Is.Not.Null);
            Assert.That(animatorController.layers.Length, Is.EqualTo(1));
            Assert.That(animatorController.layers[0].name, Is.EqualTo("Base Layer"));
            Assert.That(AssetDatabase.IsValidFolder(LegacyBodyColorAnimationFolder), Is.False);

            for (int stateIndex = 0; stateIndex < MergedActionStateNames.Length; stateIndex++)
            {
                string stateName = MergedActionStateNames[stateIndex];
                AnimatorState state = FindAnimatorState(
                    animatorController.layers[0].stateMachine,
                    stateName);
                AnimationClip mergedClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                    $"{RavenAuthoredAnimationFolder}/{stateName}.anim");
                AnimationClip sourceClip = FindImportedAnimationClip(stateName);

                Assert.That(state, Is.Not.Null, stateName);
                Assert.That(mergedClip, Is.Not.Null, stateName);
                Assert.That(sourceClip, Is.Not.Null, stateName);
                Assert.That(state.motion, Is.SameAs(mergedClip), stateName);
                Assert.That(mergedClip.hasRootCurves, Is.True, stateName);
                Assert.That(mergedClip.length, Is.EqualTo(sourceClip.length).Within(0.0001f), stateName);
                Assert.That(mergedClip.frameRate, Is.EqualTo(sourceClip.frameRate).Within(0.0001f), stateName);
                Assert.That(
                    EditorJsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(mergedClip)),
                    Is.EqualTo(EditorJsonUtility.ToJson(AnimationUtility.GetAnimationClipSettings(sourceClip))),
                    stateName);

                EditorCurveBinding[] mergedBindings = AnimationUtility.GetCurveBindings(mergedClip);
                EditorCurveBinding[] sourceBindings = AnimationUtility.GetCurveBindings(sourceClip);
                for (int sourceBindingIndex = 0;
                     sourceBindingIndex < sourceBindings.Length;
                     sourceBindingIndex++)
                {
                    Assert.That(
                        System.Array.Exists(
                            mergedBindings,
                            binding => binding.Equals(sourceBindings[sourceBindingIndex])),
                        Is.True,
                        $"{stateName}: {sourceBindings[sourceBindingIndex].propertyName}");
                }

                for (int propertyIndex = 0;
                     propertyIndex < BodyColorPropertyNames.Length;
                     propertyIndex++)
                {
                    EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                        string.Empty,
                        typeof(BossBodyColorController),
                        BodyColorPropertyNames[propertyIndex]);
                    Assert.That(
                        AnimationUtility.GetEditorCurve(mergedClip, binding),
                        Is.Not.Null,
                        $"{stateName}: {binding.propertyName}");
                }

                if (stateName == "M_Raven_SlashCombo")
                {
                    for (int propertyIndex = 0;
                         propertyIndex < HairColorPropertyNames.Length;
                         propertyIndex++)
                    {
                        EditorCurveBinding binding = EditorCurveBinding.FloatCurve(
                            string.Empty,
                            typeof(BossBodyColorController),
                            HairColorPropertyNames[propertyIndex]);
                        Assert.That(
                            AnimationUtility.GetEditorCurve(mergedClip, binding),
                            Is.Not.Null,
                            $"{stateName}: {binding.propertyName}");
                    }
                }
            }

            AssertEventCount("M_Raven_MoveChainCombo", "OnFastMoveParticleStart", 2);
            AssertEventCount("M_Raven_MoveCombo", "OnFastMoveParticleStart", 1);
            AssertEventCount("M_Raven_RapidMoveBack", "OnFastMoveParticleStart", 1);
        }

        private LineRenderer CreateGlowLine(
            Transform parent,
            string objectName,
            float startAlpha,
            float endAlpha)
        {
            GameObject gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            LineRenderer line = gameObject.AddComponent<LineRenderer>();
            line.sharedMaterial = glowMaterial;
            line.startColor = new Color(0f, 1f, 1f, startAlpha);
            line.endColor = new Color(0f, 1f, 1f, endAlpha);
            return line;
        }

        private MeshRenderer CreateGlowBase(Transform parent)
        {
            GameObject gameObject = new GameObject("FX_BossWeaponIdleGlow_BaseNode");
            gameObject.transform.SetParent(parent, false);
            MeshRenderer renderer = gameObject.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = glowMaterial;
            return renderer;
        }

        private static Light CreateGlowLight(Transform parent, string objectName, float intensity)
        {
            GameObject gameObject = new GameObject(objectName);
            gameObject.transform.SetParent(parent, false);
            Light glowLight = gameObject.AddComponent<Light>();
            glowLight.intensity = intensity;
            return glowLight;
        }

        private void SetBodyColor(Color color)
        {
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("bodyColor").colorValue = color;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            controller.SendMessage("OnDidApplyAnimationProperties");
        }

        /// <summary>通过序列化字段写入可动画 HairColor，并模拟 Animator 属性应用回调。</summary>
        /// <param name="color">只应覆盖 Raven 头发材质槽的 HDR 颜色与权重。</param>
        private void SetHairColor(Color color)
        {
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("hairColor").colorValue = color;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            controller.SendMessage("OnDidApplyAnimationProperties");
        }

        private void SetVisibility(float value)
        {
            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("visibility").floatValue = value;
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            controller.SendMessage("OnDidApplyAnimationProperties");
        }

        private void AssertOverlayProperty(int materialIndex, Color expected)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            bodyOverlayRenderer.GetPropertyBlock(block, materialIndex);
            Assert.That(
                block.GetColor("_OverlayColor"),
                Is.EqualTo(expected).Using(ColorComparer.Instance));
        }

        private void AssertWeaponColor(Color expectedColor)
        {
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            weaponRenderer.GetPropertyBlock(block, 0);
            Assert.That(
                block.GetColor("_BaseColor"),
                Is.EqualTo(expectedColor).Using(ColorComparer.Instance));
        }

        private void AssertIdleGlow(float visibilityScale, bool expectedEnabled)
        {
            for (int i = 0; i < glowLines.Length; i++)
            {
                Color expectedStart = originalLineStartColors[i];
                Color expectedEnd = originalLineEndColors[i];
                expectedStart.a *= visibilityScale;
                expectedEnd.a *= visibilityScale;
                Assert.That(
                    glowLines[i].startColor,
                    Is.EqualTo(expectedStart).Using(ColorComparer.Instance));
                Assert.That(
                    glowLines[i].endColor,
                    Is.EqualTo(expectedEnd).Using(ColorComparer.Instance));
                Assert.That(glowLines[i].enabled, Is.EqualTo(expectedEnabled));
            }

            Color expectedBaseColor = glowMaterial.GetColor("_Color");
            expectedBaseColor.a *= visibilityScale;
            MaterialPropertyBlock block = new MaterialPropertyBlock();
            glowBaseRenderer.GetPropertyBlock(block, 0);
            Assert.That(
                block.GetColor("_Color"),
                Is.EqualTo(expectedBaseColor).Using(ColorComparer.Instance));

            for (int i = 0; i < glowLights.Length; i++)
            {
                Assert.That(
                    glowLights[i].intensity,
                    Is.EqualTo(originalLightIntensities[i] * visibilityScale).Within(0.0001f));
                Assert.That(glowLights[i].enabled, Is.EqualTo(expectedEnabled));
            }
        }

        private static void SetSerializedObjectReference(
            Object target,
            string propertyName,
            Object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            serializedObject.FindProperty(propertyName).objectReferenceValue = value;
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 从 Raven FBX 子资产中查找指定动作 Clip，忽略 Unity 生成的预览 Clip。
        /// </summary>
        /// <param name="stateName">Animator 状态与导入动作的共同名称。</param>
        /// <returns>找到时返回只读 FBX 子 Clip，否则返回 null。</returns>
        private static AnimationClip FindImportedAnimationClip(string stateName)
        {
            Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RavenAnimationPath);
            string importedSuffix = $"|{stateName}";
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not AnimationClip clip ||
                    clip.name.StartsWith("__preview__", System.StringComparison.Ordinal))
                {
                    continue;
                }

                if (clip.name == stateName ||
                    clip.name.EndsWith(importedSuffix, System.StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            return null;
        }

        /// <summary>
        /// 验证指定合并动作只保留期望数量的 Animation Event。
        /// </summary>
        /// <param name="stateName">合并 Clip 名称。</param>
        /// <param name="functionName">需要统计的 Animation Event 公共入口。</param>
        /// <param name="expectedCount">期望事件数量。</param>
        private static void AssertEventCount(
            string stateName,
            string functionName,
            int expectedCount)
        {
            AnimationClip clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(
                $"{RavenAuthoredAnimationFolder}/{stateName}.anim");
            Assert.That(clip, Is.Not.Null, stateName);
            int count = 0;
            AnimationEvent[] events = AnimationUtility.GetAnimationEvents(clip);
            for (int i = 0; i < events.Length; i++)
            {
                if (events[i].functionName == functionName)
                {
                    count++;
                }
            }

            Assert.That(count, Is.EqualTo(expectedCount), stateName);
        }

        /// <summary>
        /// 在 Animator 状态机及其子状态机中查找指定名称的状态。
        /// </summary>
        /// <param name="stateMachine">需要递归查询的状态机。</param>
        /// <param name="stateName">目标状态名称。</param>
        /// <returns>找到时返回对应状态；不存在时返回 null。</returns>
        private static AnimatorState FindAnimatorState(
            AnimatorStateMachine stateMachine,
            string stateName)
        {
            ChildAnimatorState[] states = stateMachine.states;
            for (int i = 0; i < states.Length; i++)
            {
                if (states[i].state.name == stateName)
                {
                    return states[i].state;
                }
            }

            ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
            for (int i = 0; i < childStateMachines.Length; i++)
            {
                AnimatorState state = FindAnimatorState(childStateMachines[i].stateMachine, stateName);
                if (state != null)
                {
                    return state;
                }
            }

            return null;
        }

        private static void SetSerializedObjectArray<T>(
            Object target,
            string propertyName,
            T[] values) where T : Object
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class ColorComparer : System.Collections.Generic.IEqualityComparer<Color>
        {
            public static readonly ColorComparer Instance = new ColorComparer();

            public bool Equals(Color x, Color y)
            {
                const float ColorChannelTolerance = 0.005f;
                return Mathf.Abs(x.r - y.r) < ColorChannelTolerance &&
                    Mathf.Abs(x.g - y.g) < ColorChannelTolerance &&
                    Mathf.Abs(x.b - y.b) < ColorChannelTolerance &&
                    Mathf.Abs(x.a - y.a) < ColorChannelTolerance;
            }

            public int GetHashCode(Color obj)
            {
                return obj.GetHashCode();
            }
        }
    }
}
