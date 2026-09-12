// 文件说明：维护 Raven Boss 动作资产、位移配置，以及动作、Root Motion、BodyColor 合并后的可编辑动画。
// 所属模块：Boss Editor。
// 运行影响：仅在手动执行菜单时修改 ScriptableObject、Animator、AnimationClip、材质和 Demo 场景绑定，不参与运行时。

#if UNITY_EDITOR
using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Animation;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ProjectEVE.Editor.Boss
{
    public static class RavenBossContentMigration
    {
        private const string TimelineFolder = "Assets/ScriptableObjects/CombatTimelines/Boss/Raven";
        private const string ActionSetPath = "Assets/ScriptableObjects/Boss/RavenBossActionSet.asset";
        private const string MotionProfilePath = "Assets/ScriptableObjects/Boss/RavenBossMotionWarpProfile.asset";
        private const string CatalogPath = TimelineFolder + "/RavenCombatTimelineCatalog.asset";
        private const string RavenAnimationPath = "Assets/ThirtPartyModel/EveAnimation/Anim/RavenMonster_Animation.fbx";
        private const string RavenControllerPath = "Assets/Animator/Raven.controller";
        private const string LegacyBodyColorAnimationFolder = "Assets/Animator/RavenBodyColor";
        private const string RavenAuthoredAnimationFolder = "Assets/Animator/RavenAuthored";
        private const string BodyColorLayerName = "BodyColor";
        private const string BodyColorAvatarMaskPath =
            LegacyBodyColorAnimationFolder + "/Raven_BodyColor_NoRoot.mask";
        private const string NeutralBodyColorClipPath =
            LegacyBodyColorAnimationFolder + "/Raven_BodyColor_Neutral.anim";
        private const string DemoScenePath = "Assets/Scenes/Demo.unity";
        private const string RavenAnimatorRootPath = "Boss/Trans/CH_M_NA_53_Preview";
        private const string RavenBodyRendererRelativePath = "CH_M_NA_53_Preview.001";
        private const string RavenWeaponRendererName = "CH_M_NA_53_Weapon.001";
        private const string RavenWeaponIdleGlowRootName = "FX_BossWeaponIdleGlow";
        private const string RavenBodyOverlayShaderName = "Project EVE/Raven Body Overlay";
        private const string RavenWeaponMaterialPath = "Assets/Materials/Raven/MI_CH_M_NA_53_Weapon.mat";

        private static readonly string[] BodyColorActionStateNames =
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

        private static readonly HashSet<string> AuthoredBodyColorStateNames =
            new HashSet<string>(StringComparer.Ordinal)
            {
                "M_Raven_MoveChainCombo",
                "M_Raven_MoveCombo",
                "M_Raven_RapidMoveBack"
            };

        private static readonly string[] BodyColorPropertyNames =
        {
            "bodyColor.r",
            "bodyColor.g",
            "bodyColor.b",
            "bodyColor.a",
            "visibility"
        };

        private static readonly RavenBodyMaterialConfig[] RavenBodyMaterialConfigs =
        {
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_NPC_Raven_01_Suit01.mat", false, -1),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_NPC_Raven_01_BaseBody.mat", false, -1),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_NPC_Raven_01_Suit02.mat", false, -1),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_RavensuitVinyl.mat", false, -1),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_M_NA_53_Head.mat", false, -1),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_M_NA_53_Hair02.mat", false, 2000),
            new RavenBodyMaterialConfig("Assets/Materials/Raven/MI_CH_M_NA_53_Hair01.mat", true, 2450)
        };

        private readonly struct RavenBodyMaterialConfig
        {
            public RavenBodyMaterialConfig(string path, bool alphaClip, int renderQueue)
            {
                Path = path;
                AlphaClip = alphaClip;
                RenderQueue = renderQueue;
            }

            public string Path { get; }
            public bool AlphaClip { get; }
            public int RenderQueue { get; }
        }

        private readonly struct ExistingAttackConfig
        {
            public ExistingAttackConfig(string id, float angle, float cooldown, float naturalRecovery, string finalHitNode)
            {
                Id = id;
                Angle = angle;
                Cooldown = cooldown;
                NaturalRecovery = naturalRecovery;
                FinalHitNode = finalHitNode;
            }

            public string Id { get; }
            public float Angle { get; }
            public float Cooldown { get; }
            public float NaturalRecovery { get; }
            public string FinalHitNode { get; }
        }

        [MenuItem("Project EVE/Boss/Migrate Raven 13 Actions")]
        public static void Migrate()
        {
            ExistingAttackConfig[] existing =
            {
                new ExistingAttackConfig(RavenBossAttackIds.SlashId, 70f, 2f, 0.6f, "Slash_1"),
                new ExistingAttackConfig(RavenBossAttackIds.SlashChainId, 20f, 2f, 0.73f, "SlashChain_2"),
                new ExistingAttackConfig(RavenBossAttackIds.MoveComboId, 90f, 4f, 0.8f, "MoveSlash_3"),
                new ExistingAttackConfig(RavenBossAttackIds.MoveChainComboId, 180f, 4f, 1f, "MoveChainSlash_6"),
                new ExistingAttackConfig(RavenBossAttackIds.EvadeBackRushId, 20f, 4f, 0.8f, "RushSlash_2"),
                new ExistingAttackConfig(RavenBossAttackIds.ChaseGrabId, 45f, 6f, 0.8f, "ChaseGrab_Impact"),
                new ExistingAttackConfig(RavenBossAttackIds.ChaseComboId, 40f, 6f, 1f, "ChaseSlash_4"),
                new ExistingAttackConfig(RavenBossAttackIds.SlashComboId, 40f, 6f, 1.2f, "SlashCombo_YellowIai")
            };

            for (int i = 0; i < existing.Length; i++)
            {
                MigrateExistingTimeline(existing[i]);
            }

            BossAttackTimelineAsset beta = CreateOrUpdateNewAttack(
                RavenBossAttackIds.BetaChargeComboId, "BetaChargeCombo", "M_Raven_BetaChargeCombo", 7.183f, 90f, 6f, 1.2f,
                new[] { 2.2f, 3.8f, 5.783f }, 3, false);
            BossAttackTimelineAsset burst = CreateOrUpdateNewAttack(
                RavenBossAttackIds.BurstAreaSlashId, "BurstAreaSlash", "M_Raven_BurstAreaSlash", 5.1f, 180f, 10f, 1.3f,
                new[] { 2.45f, 3.1f, 3.73f }, 3, true, "M_Raven_BurstAreaSlashEnd", 2.15f);
            BossAttackTimelineAsset swordAura = CreateOrUpdateNewAttack(
                RavenBossAttackIds.SwordAuraComboId, "SwordAuraCombo", "M_Raven_SwordAuraCombo", 4.5f, 90f, 10f, 1f,
                new[] { 1.3f, 1.85f, 2.45f }, 3, true);
            BossAttackTimelineAsset evadeAura = CreateOrUpdateNewAttack(
                RavenBossAttackIds.EvadeBackSwordAuraId, "EvadeBackSwordAura", "M_Raven_EvadeBackSwordAura", 3.75f, 45f, 6f, 0.9f,
                new[] { 2.2f }, 1, true);
            BossAttackTimelineAsset rapid = CreateOrUpdateNewAttack(
                RavenBossAttackIds.RapidMoveBackId, "RapidMoveBack", "M_Raven_RapidMoveBack", 1.4f, 30f, 4f, 0f,
                Array.Empty<float>(), 0, false);

            ConfigureActionSet();
            ConfigureMotionProfile();
            UpdateCatalog(beta, burst, swordAura, evadeAura, rapid);
            AssetDatabase.SaveAssets();
            ConfigureBodyColorAnimationAssets();
            Debug.Log("Raven 13 actions migration completed. New actions remain disabled until runtime validation passes.");
        }

        /// <summary>
        /// 独立创建或修复 Raven 身体颜色控制器、静态材质关键字和单 Clip 合并动画。
        /// 已通过完整契约验证的合并 Clip 不会被重写。
        /// </summary>
        [MenuItem("Project EVE/Boss/Configure Raven Body Color Animation")]
        public static void ConfigureBodyColorAnimation()
        {
            ConfigureBodyColorAnimationAssets();
            Debug.Log("Raven body color animation configuration completed.");
        }

        /// <summary>
        /// 配置身体颜色所需的静态材质、Animator 资产和当前 Demo 场景组件绑定。
        /// </summary>
        private static void ConfigureBodyColorAnimationAssets()
        {
            ConfigureRavenBodyMaterials();
            ConfigureRavenWeaponMaterialTransparency();
            ConfigureMergedRavenAnimations();
            ConfigureDemoBodyColorController();
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 将 Raven 七个身体材质切到覆盖 Shader，恢复各槽原有 Opaque / Cutout 深度语义，并维持 authored Emission。
        /// </summary>
        private static void ConfigureRavenBodyMaterials()
        {
            Shader overlayShader = Shader.Find(RavenBodyOverlayShaderName);
            if (overlayShader == null)
            {
                throw new InvalidOperationException(
                    $"Missing Raven body overlay Shader: {RavenBodyOverlayShaderName}");
            }

            for (int i = 0; i < RavenBodyMaterialConfigs.Length; i++)
            {
                RavenBodyMaterialConfig config = RavenBodyMaterialConfigs[i];
                Material material = AssetDatabase.LoadAssetAtPath<Material>(config.Path);
                if (material == null)
                {
                    throw new InvalidOperationException(
                        $"Missing Raven body material: {config.Path}");
                }

                if (material.shader != overlayShader)
                {
                    material.shader = overlayShader;
                }

                if (!material.HasProperty("_BodyColorOverlay") ||
                    !material.HasProperty("_BodyVisibility") ||
                    !material.HasProperty("_BaseColor") ||
                    !material.HasProperty("_EmissionColor") ||
                    !material.HasProperty("_Surface") ||
                    !material.HasProperty("_Blend"))
                {
                    throw new InvalidOperationException(
                        $"Raven body material does not support body color properties: {material.name}");
                }

                Color emissionColor = material.GetColor("_EmissionColor");
                if (emissionColor.maxColorComponent <= 0.001f)
                {
                    const float MinimumStaticEmission = 0.001f;
                    material.SetColor(
                        "_EmissionColor",
                        new Color(
                            MinimumStaticEmission,
                            MinimumStaticEmission,
                            MinimumStaticEmission,
                            emissionColor.a));
                }

                material.SetFloat("_Surface", 0f);
                material.SetFloat("_Blend", 0f);
                material.SetFloat("_AlphaClip", config.AlphaClip ? 1f : 0f);
                if (material.HasProperty("_BlendModePreserveSpecular"))
                {
                    material.SetFloat("_BlendModePreserveSpecular", 1f);
                }
                BaseShaderGUI.SetupMaterialBlendMode(material);
                BaseShaderGUI.SetMaterialKeywords(material);
                material.renderQueue = config.RenderQueue;
                material.EnableKeyword("_EMISSION");
                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssetIfDirty(material);
                AssetDatabase.ImportAsset(
                    config.Path,
                    ImportAssetOptions.ForceUpdate);

                Material reloadedMaterial =
                    AssetDatabase.LoadAssetAtPath<Material>(config.Path);
                int expectedEffectiveRenderQueue = config.RenderQueue < 0
                    ? (int)UnityEngine.Rendering.RenderQueue.Geometry
                    : config.RenderQueue;
                if (reloadedMaterial == null ||
                    reloadedMaterial.shader != overlayShader ||
                    !reloadedMaterial.IsKeywordEnabled("_EMISSION") ||
                    !Mathf.Approximately(reloadedMaterial.GetFloat("_Surface"), 0f) ||
                    !Mathf.Approximately(reloadedMaterial.GetFloat("_ZWrite"), 1f) ||
                    !Mathf.Approximately(
                        reloadedMaterial.GetFloat("_AlphaClip"),
                        config.AlphaClip ? 1f : 0f) ||
                    reloadedMaterial.renderQueue != expectedEffectiveRenderQueue)
                {
                    throw new InvalidOperationException(
                        $"Failed to persist Raven body overlay material: {config.Path}");
                }
            }
        }

        /// <summary>
        /// 将唯一 Raven 武器材质静态配置为 URP Alpha 透明，使 PropertyBlock Alpha 能产生真实淡出。
        /// </summary>
        private static void ConfigureRavenWeaponMaterialTransparency()
        {
            Material weaponMaterial = AssetDatabase.LoadAssetAtPath<Material>(RavenWeaponMaterialPath);
            if (weaponMaterial == null)
            {
                throw new InvalidOperationException(
                    $"Missing Raven weapon material: {RavenWeaponMaterialPath}");
            }

            if (!weaponMaterial.HasProperty("_Surface") ||
                !weaponMaterial.HasProperty("_Blend") ||
                !weaponMaterial.HasProperty("_BaseColor"))
            {
                throw new InvalidOperationException(
                    $"Raven weapon material does not support URP transparency: {weaponMaterial.name}");
            }

            weaponMaterial.SetFloat("_Surface", 1f);
            weaponMaterial.SetFloat("_Blend", 0f);
            if (weaponMaterial.HasProperty("_BlendModePreserveSpecular"))
            {
                weaponMaterial.SetFloat("_BlendModePreserveSpecular", 0f);
            }
            BaseShaderGUI.SetupMaterialBlendMode(weaponMaterial);
            BaseShaderGUI.SetMaterialKeywords(weaponMaterial);
            EditorUtility.SetDirty(weaponMaterial);
            AssetDatabase.SaveAssetIfDirty(weaponMaterial);
        }

        /// <summary>
        /// 从只读 FBX 动作提取 14 个可编辑 Clip，并将有效 BodyColor 曲线与事件合入同一资产。
        /// 源动作决定最终长度和 Root Motion；较短的表现曲线保持绝对秒数，不缩放也不补齐。
        /// </summary>
        private static void ConfigureMergedRavenAnimations()
        {
            AnimatorController controller =
                AssetDatabase.LoadAssetAtPath<AnimatorController>(RavenControllerPath);
            if (controller == null || controller.layers.Length == 0)
            {
                throw new InvalidOperationException($"Missing Raven AnimatorController: {RavenControllerPath}");
            }

            Dictionary<string, AnimatorState> baseStates = CollectStates(controller.layers[0].stateMachine);
            Dictionary<string, AnimationClip> sourceClips =
                new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            Dictionary<string, AnimationClip> legacyBodyColorClips =
                new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            Dictionary<string, AnimationClip> mergedClips =
                new Dictionary<string, AnimationClip>(StringComparer.Ordinal);
            List<string> createdAssetPaths = new List<string>();

            for (int i = 0; i < BodyColorActionStateNames.Length; i++)
            {
                string stateName = BodyColorActionStateNames[i];
                if (!baseStates.TryGetValue(stateName, out AnimatorState state))
                {
                    throw new InvalidOperationException(
                        $"Raven BodyColor action state is missing from Base Layer: {stateName}");
                }

                AnimationClip sourceClip = FindAnimationClip(stateName);
                if (sourceClip == null || !sourceClip.hasRootCurves)
                {
                    throw new InvalidOperationException(
                        $"Raven source action is missing Root Motion: {stateName}");
                }

                sourceClips.Add(stateName, sourceClip);
                string mergedPath = $"{RavenAuthoredAnimationFolder}/{stateName}.anim";
                AnimationClip existingMergedClip =
                    AssetDatabase.LoadAssetAtPath<AnimationClip>(mergedPath);
                if (existingMergedClip != null)
                {
                    ValidateMergedClip(existingMergedClip, sourceClip, stateName);
                    mergedClips.Add(stateName, existingMergedClip);
                }
                else if (AuthoredBodyColorStateNames.Contains(stateName))
                {
                    string legacyPath =
                        $"{LegacyBodyColorAnimationFolder}/{stateName}_BodyColor.anim";
                    AnimationClip legacyClip = AssetDatabase.LoadAssetAtPath<AnimationClip>(legacyPath);
                    if (legacyClip == null)
                    {
                        throw new InvalidOperationException(
                            $"Missing authored Raven BodyColor clip: {legacyPath}");
                    }

                    ValidateBodyColorCurves(legacyClip, stateName);
                    legacyBodyColorClips.Add(stateName, legacyClip);
                }
            }

            ValidateNonMergedStatesUseWriteDefaults(baseStates);
            EnsureAssetFolder(RavenAuthoredAnimationFolder);

            try
            {
                for (int i = 0; i < BodyColorActionStateNames.Length; i++)
                {
                    string stateName = BodyColorActionStateNames[i];
                    string mergedPath = $"{RavenAuthoredAnimationFolder}/{stateName}.anim";
                    if (!mergedClips.TryGetValue(stateName, out AnimationClip mergedClip))
                    {
                        legacyBodyColorClips.TryGetValue(stateName, out AnimationClip legacyClip);
                        mergedClip = CreateMergedClip(sourceClips[stateName], legacyClip, stateName);
                        AssetDatabase.CreateAsset(mergedClip, mergedPath);
                        createdAssetPaths.Add(mergedPath);
                        ValidateMergedClip(mergedClip, sourceClips[stateName], stateName);
                        mergedClips.Add(stateName, mergedClip);
                    }
                }
            }
            catch
            {
                for (int i = 0; i < createdAssetPaths.Count; i++)
                {
                    AssetDatabase.DeleteAsset(createdAssetPaths[i]);
                }

                throw;
            }

            Dictionary<AnimatorState, Motion> originalMotions =
                new Dictionary<AnimatorState, Motion>();
            AnimatorControllerLayer[] originalLayers = controller.layers;
            try
            {
                for (int i = 0; i < BodyColorActionStateNames.Length; i++)
                {
                    string stateName = BodyColorActionStateNames[i];
                    AnimatorState state = baseStates[stateName];
                    originalMotions.Add(state, state.motion);
                    state.motion = mergedClips[stateName];
                    EditorUtility.SetDirty(state);
                }

                RemoveBodyColorLayers(controller);
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
            }
            catch
            {
                foreach (KeyValuePair<AnimatorState, Motion> pair in originalMotions)
                {
                    pair.Key.motion = pair.Value;
                    EditorUtility.SetDirty(pair.Key);
                }

                controller.layers = originalLayers;
                EditorUtility.SetDirty(controller);
                AssetDatabase.SaveAssetIfDirty(controller);
                for (int i = 0; i < createdAssetPaths.Count; i++)
                {
                    AssetDatabase.DeleteAsset(createdAssetPaths[i]);
                }

                throw;
            }

            DeleteOrphanBodyColorStateMachines(controller);
            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssetIfDirty(controller);
            DeleteLegacyBodyColorAssets();
        }

        /// <summary>
        /// 复制源动作并写入 BodyColor 曲线与事件。源 Clip 的长度、帧率、Root Transform 设置和 Root Motion 不变。
        /// </summary>
        /// <param name="sourceClip">提供骨骼、Root Motion、ClipSettings 和源事件的 FBX 子 Clip。</param>
        /// <param name="legacyBodyColorClip">三个有效表现 Clip 之一；其他动作传入 null 并写入中性曲线。</param>
        /// <param name="stateName">用于命名和错误信息的 Animator 状态名。</param>
        /// <returns>尚未写入 AssetDatabase 的完整可编辑合并 Clip。</returns>
        private static AnimationClip CreateMergedClip(
            AnimationClip sourceClip,
            AnimationClip legacyBodyColorClip,
            string stateName)
        {
            AnimationClip mergedClip = UnityEngine.Object.Instantiate(sourceClip);
            mergedClip.name = stateName;
            mergedClip.hideFlags = HideFlags.None;
            mergedClip.wrapMode = sourceClip.wrapMode;
            AnimationUtility.SetAnimationClipSettings(
                mergedClip,
                AnimationUtility.GetAnimationClipSettings(sourceClip));

            if (legacyBodyColorClip != null)
            {
                CopyBodyColorCurves(legacyBodyColorClip, mergedClip);
                AnimationUtility.SetAnimationEvents(
                    mergedClip,
                    MergeAnimationEvents(
                        AnimationUtility.GetAnimationEvents(sourceClip),
                        AnimationUtility.GetAnimationEvents(legacyBodyColorClip)));
            }
            else
            {
                SetNeutralBodyColorCurves(mergedClip, sourceClip.length);
                AnimationUtility.SetAnimationEvents(
                    mergedClip,
                    AnimationUtility.GetAnimationEvents(sourceClip));
            }

            return mergedClip;
        }

        /// <summary>
        /// 将旧表现 Clip 的五条曲线按原始绝对秒数复制到合并 Clip，不补帧、不缩放。
        /// </summary>
        /// <param name="source">提供人工 BodyColor 与 Visibility 曲线的旧 Clip。</param>
        /// <param name="destination">接收曲线的完整动作 Clip。</param>
        private static void CopyBodyColorCurves(AnimationClip source, AnimationClip destination)
        {
            for (int i = 0; i < BodyColorPropertyNames.Length; i++)
            {
                EditorCurveBinding binding = CreateBodyColorBinding(BodyColorPropertyNames[i]);
                AnimationCurve sourceCurve = AnimationUtility.GetEditorCurve(source, binding);
                if (sourceCurve == null)
                {
                    throw new InvalidOperationException(
                        $"Missing {binding.propertyName} curve in authored clip {source.name}.");
                }

                AnimationCurve copiedCurve = new AnimationCurve(sourceCurve.keys)
                {
                    preWrapMode = sourceCurve.preWrapMode,
                    postWrapMode = sourceCurve.postWrapMode
                };
                AnimationUtility.SetEditorCurve(destination, binding, copiedCurve);
            }
        }

        /// <summary>
        /// 为没有有效表现值的动作写入覆盖源动作全长的中性属性，确保 Write Defaults 之外也有明确默认值。
        /// </summary>
        /// <param name="clip">接收中性曲线的完整动作 Clip。</param>
        /// <param name="duration">源动作长度，单位秒。</param>
        private static void SetNeutralBodyColorCurves(AnimationClip clip, float duration)
        {
            float safeDuration = Mathf.Max(duration, 1f / 60f);
            for (int i = 0; i < BodyColorPropertyNames.Length; i++)
            {
                float value = string.Equals(
                    BodyColorPropertyNames[i],
                    "visibility",
                    StringComparison.Ordinal) ? 1f : 0f;
                AnimationUtility.SetEditorCurve(
                    clip,
                    CreateBodyColorBinding(BodyColorPropertyNames[i]),
                    AnimationCurve.Constant(0f, safeDuration, value));
            }
        }

        /// <summary>
        /// 验证人工表现 Clip 确实包含五条约定曲线；Clip 长度不参与判断。
        /// </summary>
        /// <param name="clip">需要校验的旧 BodyColor Clip。</param>
        /// <param name="stateName">用于错误信息的动作状态名。</param>
        private static void ValidateBodyColorCurves(AnimationClip clip, string stateName)
        {
            for (int i = 0; i < BodyColorPropertyNames.Length; i++)
            {
                EditorCurveBinding binding = CreateBodyColorBinding(BodyColorPropertyNames[i]);
                if (AnimationUtility.GetEditorCurve(clip, binding) == null)
                {
                    throw new InvalidOperationException(
                        $"Raven authored BodyColor clip {stateName} is missing {binding.propertyName}.");
                }
            }
        }

        /// <summary>
        /// 构造 Animator 根节点 BossBodyColorController 的浮点曲线绑定。
        /// </summary>
        /// <param name="propertyName">序列化属性名。</param>
        /// <returns>路径为空、类型为 BossBodyColorController 的曲线绑定。</returns>
        private static EditorCurveBinding CreateBodyColorBinding(string propertyName)
        {
            return EditorCurveBinding.FloatCurve(
                string.Empty,
                typeof(BossBodyColorController),
                propertyName);
        }

        /// <summary>
        /// 合并源动作和表现 Clip 的 Animation Event，并按完整参数去重后按时间排序。
        /// </summary>
        /// <param name="sourceEvents">FBX 动作已有事件。</param>
        /// <param name="bodyColorEvents">表现 Clip 已有事件。</param>
        /// <returns>去重并按时间升序排列的事件数组。</returns>
        private static AnimationEvent[] MergeAnimationEvents(
            AnimationEvent[] sourceEvents,
            AnimationEvent[] bodyColorEvents)
        {
            List<AnimationEvent> mergedEvents = new List<AnimationEvent>();
            AddUniqueAnimationEvents(mergedEvents, sourceEvents);
            AddUniqueAnimationEvents(mergedEvents, bodyColorEvents);
            mergedEvents.Sort((left, right) => left.time.CompareTo(right.time));
            return mergedEvents.ToArray();
        }

        /// <summary>
        /// 将尚未存在的事件加入目标集合。
        /// </summary>
        /// <param name="destination">接收唯一事件的列表。</param>
        /// <param name="source">候选事件数组。</param>
        private static void AddUniqueAnimationEvents(
            List<AnimationEvent> destination,
            AnimationEvent[] source)
        {
            for (int i = 0; i < source.Length; i++)
            {
                AnimationEvent candidate = source[i];
                bool duplicate = false;
                for (int eventIndex = 0; eventIndex < destination.Count; eventIndex++)
                {
                    if (AreSameAnimationEvent(destination[eventIndex], candidate))
                    {
                        duplicate = true;
                        break;
                    }
                }

                if (!duplicate)
                {
                    destination.Add(candidate);
                }
            }
        }

        /// <summary>
        /// 比较 Animation Event 的时间、入口和全部参数。
        /// </summary>
        /// <param name="left">第一个事件。</param>
        /// <param name="right">第二个事件。</param>
        /// <returns>全部可序列化字段相同时返回 true，否则返回 false。</returns>
        private static bool AreSameAnimationEvent(AnimationEvent left, AnimationEvent right)
        {
            return Mathf.Approximately(left.time, right.time) &&
                string.Equals(left.functionName, right.functionName, StringComparison.Ordinal) &&
                string.Equals(left.stringParameter, right.stringParameter, StringComparison.Ordinal) &&
                Mathf.Approximately(left.floatParameter, right.floatParameter) &&
                left.intParameter == right.intParameter &&
                left.objectReferenceParameter == right.objectReferenceParameter &&
                left.messageOptions == right.messageOptions;
        }

        /// <summary>
        /// 验证合并 Clip 保留源动作契约并包含五条表现曲线；已有资产不满足时拒绝覆盖。
        /// </summary>
        /// <param name="mergedClip">需要验证的可编辑合并资产。</param>
        /// <param name="sourceClip">提供长度、帧率与源曲线绑定的 FBX Clip。</param>
        /// <param name="stateName">用于错误信息的状态名。</param>
        private static void ValidateMergedClip(
            AnimationClip mergedClip,
            AnimationClip sourceClip,
            string stateName)
        {
            if (!mergedClip.hasRootCurves ||
                !Mathf.Approximately(mergedClip.length, sourceClip.length) ||
                !Mathf.Approximately(mergedClip.frameRate, sourceClip.frameRate))
            {
                throw new InvalidOperationException(
                    $"Existing Raven merged clip has an incompatible Root Motion contract: {stateName}");
            }

            HashSet<EditorCurveBinding> mergedBindings = new HashSet<EditorCurveBinding>(
                AnimationUtility.GetCurveBindings(mergedClip));
            EditorCurveBinding[] sourceBindings = AnimationUtility.GetCurveBindings(sourceClip);
            for (int i = 0; i < sourceBindings.Length; i++)
            {
                if (!mergedBindings.Contains(sourceBindings[i]))
                {
                    throw new InvalidOperationException(
                        $"Existing Raven merged clip is missing source curve {sourceBindings[i].propertyName}: {stateName}");
                }
            }

            ValidateBodyColorCurves(mergedClip, stateName);
        }

        /// <summary>
        /// 确认所有未合并状态使用 Write Defaults，以便离开合并动作后恢复默认 BodyColor 与 Visibility。
        /// </summary>
        /// <param name="baseStates">Base Layer 的完整状态表。</param>
        private static void ValidateNonMergedStatesUseWriteDefaults(
            Dictionary<string, AnimatorState> baseStates)
        {
            HashSet<string> mergedStateNames = new HashSet<string>(
                BodyColorActionStateNames,
                StringComparer.Ordinal);
            foreach (KeyValuePair<string, AnimatorState> pair in baseStates)
            {
                if (!mergedStateNames.Contains(pair.Key) && !pair.Value.writeDefaultValues)
                {
                    throw new InvalidOperationException(
                        $"Raven non-merged state must use Write Defaults: {pair.Key}");
                }
            }
        }

        /// <summary>
        /// 从 Controller 删除全部旧 BodyColor 同步层，保留 Base Layer 与任何其他未知层。
        /// </summary>
        /// <param name="controller">需要清理的 Raven AnimatorController。</param>
        private static void RemoveBodyColorLayers(AnimatorController controller)
        {
            AnimatorControllerLayer[] layers = controller.layers;
            List<AnimatorControllerLayer> retainedLayers = new List<AnimatorControllerLayer>(layers.Length);
            for (int i = 0; i < layers.Length; i++)
            {
                if (!string.Equals(layers[i].name, BodyColorLayerName, StringComparison.Ordinal))
                {
                    retainedLayers.Add(layers[i]);
                }
            }

            controller.layers = retainedLayers.ToArray();
        }

        /// <summary>
        /// 删除旧同步层遗留在 Controller 资产中的无引用 BodyColor 状态机子资产。
        /// </summary>
        /// <param name="controller">已经移除 BodyColor 层的 Raven AnimatorController。</param>
        private static void DeleteOrphanBodyColorStateMachines(AnimatorController controller)
        {
            HashSet<AnimatorStateMachine> retainedStateMachines = new HashSet<AnimatorStateMachine>();
            AnimatorControllerLayer[] layers = controller.layers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i].stateMachine != null)
                {
                    retainedStateMachines.Add(layers[i].stateMachine);
                }
            }

            UnityEngine.Object[] controllerAssets =
                AssetDatabase.LoadAllAssetsAtPath(RavenControllerPath);
            for (int i = 0; i < controllerAssets.Length; i++)
            {
                if (controllerAssets[i] is AnimatorStateMachine stateMachine &&
                    string.Equals(stateMachine.name, BodyColorLayerName, StringComparison.Ordinal) &&
                    !retainedStateMachines.Contains(stateMachine))
                {
                    UnityEngine.Object.DestroyImmediate(stateMachine, true);
                }
            }
        }

        /// <summary>
        /// 删除完成 Controller 切换后已无引用的独立颜色 Clip、Neutral Clip 和 NoRoot Mask。
        /// </summary>
        private static void DeleteLegacyBodyColorAssets()
        {
            for (int i = 0; i < BodyColorActionStateNames.Length; i++)
            {
                AssetDatabase.DeleteAsset(
                    $"{LegacyBodyColorAnimationFolder}/{BodyColorActionStateNames[i]}_BodyColor.anim");
            }

            AssetDatabase.DeleteAsset(NeutralBodyColorClipPath);
            AssetDatabase.DeleteAsset(BodyColorAvatarMaskPath);

            if (!AssetDatabase.IsValidFolder(LegacyBodyColorAnimationFolder))
            {
                return;
            }

            string[] remainingAssets = AssetDatabase.FindAssets(
                string.Empty,
                new[] { LegacyBodyColorAnimationFolder });
            if (remainingAssets.Length == 0)
            {
                AssetDatabase.DeleteAsset(LegacyBodyColorAnimationFolder);
            }
        }

        /// <summary>
        /// 收集 Base Layer 的全部状态，并拒绝重复状态名，保证单 Clip 状态映射明确。
        /// </summary>
        /// <param name="stateMachine">需要递归扫描的状态机。</param>
        /// <returns>以状态名索引的 AnimatorState。</returns>
        private static Dictionary<string, AnimatorState> CollectStates(AnimatorStateMachine stateMachine)
        {
            Dictionary<string, AnimatorState> states =
                new Dictionary<string, AnimatorState>(StringComparer.Ordinal);
            CollectStatesRecursive(stateMachine, states);
            return states;
        }

        /// <summary>
        /// 递归读取当前状态机及其子状态机。
        /// </summary>
        /// <param name="stateMachine">当前扫描的状态机。</param>
        /// <param name="states">写入状态名与 AnimatorState 的目标字典。</param>
        private static void CollectStatesRecursive(
            AnimatorStateMachine stateMachine,
            Dictionary<string, AnimatorState> states)
        {
            ChildAnimatorState[] childStates = stateMachine.states;
            for (int i = 0; i < childStates.Length; i++)
            {
                AnimatorState state = childStates[i].state;
                if (!states.TryAdd(state.name, state))
                {
                    throw new InvalidOperationException(
                        $"Duplicate Raven Animator state name prevents merged clip mapping: {state.name}");
                }
            }

            ChildAnimatorStateMachine[] childStateMachines = stateMachine.stateMachines;
            for (int i = 0; i < childStateMachines.Length; i++)
            {
                CollectStatesRecursive(childStateMachines[i].stateMachine, states);
            }
        }

        /// <summary>
        /// 在当前打开的 Demo 场景中把身体颜色控制器绑定到 Raven Animator 根和唯一身体 Renderer。
        /// </summary>
        private static void ConfigureDemoBodyColorController()
        {
            if (!string.Equals(
                    EditorSceneManager.GetActiveScene().path,
                    DemoScenePath,
                    StringComparison.Ordinal))
            {
                Debug.LogWarning(
                    $"Raven BodyColor scene binding skipped because {DemoScenePath} is not the active scene.");
                return;
            }

            GameObject animatorRoot = GameObject.Find(RavenAnimatorRootPath);
            if (animatorRoot == null)
            {
                throw new InvalidOperationException(
                    $"Missing Raven Animator root in Demo scene: {RavenAnimatorRootPath}");
            }

            Transform rendererTransform = animatorRoot.transform.Find(RavenBodyRendererRelativePath);
            SkinnedMeshRenderer renderer =
                rendererTransform != null ? rendererTransform.GetComponent<SkinnedMeshRenderer>() : null;
            if (renderer == null)
            {
                throw new InvalidOperationException(
                    $"Missing Raven body Renderer: {RavenAnimatorRootPath}/{RavenBodyRendererRelativePath}");
            }

            Renderer weaponRenderer = null;
            Renderer[] ravenRenderers = animatorRoot.GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < ravenRenderers.Length; i++)
            {
                if (string.Equals(
                        ravenRenderers[i].name,
                        RavenWeaponRendererName,
                        StringComparison.Ordinal))
                {
                    weaponRenderer = ravenRenderers[i];
                    break;
                }
            }

            if (weaponRenderer == null)
            {
                throw new InvalidOperationException(
                    $"Missing Raven weapon Renderer under {RavenAnimatorRootPath}: {RavenWeaponRendererName}");
            }

            Transform idleGlowRoot = weaponRenderer.transform.Find(RavenWeaponIdleGlowRootName);
            if (idleGlowRoot == null)
            {
                throw new InvalidOperationException(
                    $"Missing {RavenWeaponIdleGlowRootName} under Raven weapon Renderer.");
            }

            LineRenderer[] idleGlowLines =
            {
                RequireChildComponent<LineRenderer>(idleGlowRoot, "FX_BossWeaponIdleGlow_CoreLine"),
                RequireChildComponent<LineRenderer>(idleGlowRoot, "FX_BossWeaponIdleGlow_LeftEdge"),
                RequireChildComponent<LineRenderer>(idleGlowRoot, "FX_BossWeaponIdleGlow_RightEdge")
            };
            Renderer idleGlowBaseRenderer = RequireChildComponent<Renderer>(
                idleGlowRoot,
                "FX_BossWeaponIdleGlow_BaseNode");
            Light[] idleGlowLights =
            {
                RequireChildComponent<Light>(idleGlowRoot, "FX_BossWeaponIdleGlow_TipLight"),
                RequireChildComponent<Light>(idleGlowRoot, "FX_BossWeaponIdleGlow_BaseLight")
            };

            BossBodyColorController bodyColorController =
                animatorRoot.GetComponent<BossBodyColorController>();
            bool addedController = bodyColorController == null;
            if (bodyColorController == null)
            {
                bodyColorController = Undo.AddComponent<BossBodyColorController>(animatorRoot);
            }

            SerializedObject serializedController = new SerializedObject(bodyColorController);
            serializedController.FindProperty("bodyRenderer").objectReferenceValue = renderer;
            serializedController.FindProperty("weaponRenderer").objectReferenceValue = weaponRenderer;
            serializedController.FindProperty("weaponMaterialIndex").intValue = 0;
            SerializedProperty lineProperty = serializedController.FindProperty("weaponIdleGlowLines");
            lineProperty.arraySize = idleGlowLines.Length;
            for (int i = 0; i < idleGlowLines.Length; i++)
            {
                lineProperty.GetArrayElementAtIndex(i).objectReferenceValue = idleGlowLines[i];
            }
            serializedController.FindProperty("weaponIdleGlowBaseRenderer").objectReferenceValue =
                idleGlowBaseRenderer;
            serializedController.FindProperty("weaponIdleGlowBaseMaterialIndex").intValue = 0;
            SerializedProperty lightProperty = serializedController.FindProperty("weaponIdleGlowLights");
            lightProperty.arraySize = idleGlowLights.Length;
            for (int i = 0; i < idleGlowLights.Length; i++)
            {
                lightProperty.GetArrayElementAtIndex(i).objectReferenceValue = idleGlowLights[i];
            }
            SerializedProperty colorProperty = serializedController.FindProperty("bodyColor");
            if (addedController && !colorProperty.hasMultipleDifferentValues)
            {
                colorProperty.colorValue = new Color(0f, 0f, 0f, 0f);
            }
            if (addedController)
            {
                serializedController.FindProperty("visibility").floatValue = 1f;
            }
            serializedController.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(bodyColorController);
            EditorSceneManager.MarkSceneDirty(animatorRoot.scene);
        }

        /// <summary>按精确对象名读取常驻武器光效子树中的组件，缺失或重名时中止迁移。</summary>
        /// <typeparam name="T">需要绑定的 Renderer、LineRenderer 或 Light 类型。</typeparam>
        /// <param name="parent">FX_BossWeaponIdleGlow 根节点。</param>
        /// <param name="childName">需要查找的子对象精确名称。</param>
        /// <returns>唯一同名子对象上的指定组件。</returns>
        private static T RequireChildComponent<T>(Transform parent, string childName) where T : Component
        {
            T match = null;
            T[] components = parent.GetComponentsInChildren<T>(true);
            for (int i = 0; i < components.Length; i++)
            {
                if (!string.Equals(components[i].name, childName, StringComparison.Ordinal))
                {
                    continue;
                }

                if (match != null)
                {
                    throw new InvalidOperationException(
                        $"Duplicate {typeof(T).Name} named {childName} under {parent.name}.");
                }

                match = components[i];
            }

            if (match == null)
            {
                throw new InvalidOperationException(
                    $"Missing {typeof(T).Name} named {childName} under {parent.name}.");
            }

            return match;
        }

        /// <summary>
        /// 逐级创建缺失的 Assets 文件夹。
        /// </summary>
        /// <param name="folderPath">需要存在的 Assets 相对文件夹路径。</param>
        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath))
            {
                return;
            }

            string parent = System.IO.Path.GetDirectoryName(folderPath)?.Replace('\\', '/');
            string folderName = System.IO.Path.GetFileName(folderPath);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(folderName))
            {
                throw new InvalidOperationException($"Invalid asset folder path: {folderPath}");
            }

            EnsureAssetFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }

        private static void MigrateExistingTimeline(ExistingAttackConfig config)
        {
            string path = $"{TimelineFolder}/{config.Id}.asset";
            BossAttackTimelineAsset timeline = AssetDatabase.LoadAssetAtPath<BossAttackTimelineAsset>(path);
            if (timeline == null)
            {
                throw new InvalidOperationException($"Missing Raven timeline: {path}");
            }

            timeline.ConfigureBossAttack(config.Angle, config.Cooldown, config.NaturalRecovery);
            foreach (CombatHitNodeClip hitNode in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                if (config.Id == RavenBossAttackIds.MoveComboId &&
                    hitNode.Name == "MoveSlash_2")
                {
                    hitNode.SourcePart = BossAttackSourcePart.Weapon;
                }

                hitNode.TriggersPerfectGuardBossStagger = hitNode.Name == config.FinalHitNode && hitNode.CanBePerfectGuarded;
                if (config.Id == RavenBossAttackIds.ChaseComboId)
                {
                    hitNode.CanBeGuarded = false;
                    hitNode.CanBePerfectGuarded = true;
                    hitNode.CanBePerfectEvaded = true;
                    hitNode.ReactionIntent = CombatReactionIntent.HitReaction;
                }
            }

            timeline.SetDefenseRewardRules(BuildExistingRewardRules(config.Id));
            EditorUtility.SetDirty(timeline);
        }

        private static BossDefenseRewardRule[] BuildExistingRewardRules(string id)
        {
            if (id == RavenBossAttackIds.SlashId) return new[] { Rule("FinalPGPE", 0.8f, Condition("Slash_1", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.SlashChainId) return new[] { Rule("FinalPGPE", 1f, Condition("SlashChain_2", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.MoveComboId) return new[] { Rule("FinalPGPE", 1.1f, Condition("MoveSlash_3", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.MoveChainComboId) return new[] { Rule("FinalPGPE", 1.3f, Condition("MoveChainSlash_6", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.EvadeBackRushId) return new[] { Rule("FinalPGPE", 1f, Condition("RushSlash_2", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.ChaseGrabId) return new[] { Rule("FinalPE", 1f, Condition("ChaseGrab_Impact", BossDefenseOutcomeMask.PerfectEvade)) };
            if (id == RavenBossAttackIds.ChaseComboId)
            {
                return new[]
                {
                    Rule("FinalPGPE", 1.3f, Condition("ChaseSlash_4", BossDefenseOutcomeMask.PerfectGuard | BossDefenseOutcomeMask.PerfectEvade)),
                    Rule("AllPG", 1.8f,
                        Condition("ChaseSlash_1", BossDefenseOutcomeMask.PerfectGuard), Condition("ChaseSlash_2", BossDefenseOutcomeMask.PerfectGuard),
                        Condition("ChaseSlash_3", BossDefenseOutcomeMask.PerfectGuard), Condition("ChaseSlash_4", BossDefenseOutcomeMask.PerfectGuard))
                };
            }
            if (id == RavenBossAttackIds.SlashComboId)
            {
                return new[]
                {
                    Rule("FinalPE", 1.5f, Condition("SlashCombo_YellowIai", BossDefenseOutcomeMask.PerfectEvade)),
                    Rule("FullBreak", 2f,
                        Condition("SlashCombo_1", BossDefenseOutcomeMask.PerfectGuard), Condition("SlashCombo_2", BossDefenseOutcomeMask.PerfectGuard),
                        Condition("SlashCombo_3", BossDefenseOutcomeMask.PerfectGuard), Condition("SlashCombo_4", BossDefenseOutcomeMask.PerfectGuard),
                        Condition("SlashCombo_YellowIai", BossDefenseOutcomeMask.PerfectEvade))
                };
            }
            return Array.Empty<BossDefenseRewardRule>();
        }

        private static BossAttackTimelineAsset CreateOrUpdateNewAttack(
            string id, string displayName, string stateName, float duration, float angle, float cooldown, float recovery,
            float[] hitTimes, int hitCount, bool detached, string secondPreviewState = null, float secondPreviewStart = 0f)
        {
            string path = $"{TimelineFolder}/{id}.asset";
            BossAttackTimelineAsset timeline = AssetDatabase.LoadAssetAtPath<BossAttackTimelineAsset>(path);
            if (timeline == null)
            {
                timeline = ScriptableObject.CreateInstance<BossAttackTimelineAsset>();
                AssetDatabase.CreateAsset(timeline, path);
            }

            timeline.ConfigureIdentity(
                CombatTimelineOwner.Boss,
                id == RavenBossAttackIds.RapidMoveBackId
                    ? CombatTimelineActionKind.BossReposition
                    : CombatTimelineActionKind.BossAttack,
                id,
                displayName,
                stateName,
                duration,
                60f);
            timeline.ConfigureBossAttack(angle, cooldown, recovery);
            List<CombatTimelineTrack> tracks = new List<CombatTimelineTrack>();
            if (hitCount > 0)
            {
                CombatTimelineTrack hitTrack = new CombatTimelineTrack("HitNode", CombatTimelineTrackKind.HitNode);
                CombatTimelineClip[] clips = new CombatTimelineClip[hitCount];
                for (int i = 0; i < hitCount; i++)
                {
                    bool final = i == hitCount - 1;
                    float center = hitTimes[i];
                    clips[i] = new CombatHitNodeClip
                    {
                        Name = $"{displayName}_{i + 1}", StartTime = Mathf.Max(0f, center - 0.06f), EndTime = center + 0.06f,
                        CapabilityId = CombatTimelineCapabilityId.Hit_Attack,
                        HitIndex = i + 1, SourcePart = detached ? BossAttackSourcePart.Detached : BossAttackSourcePart.Weapon,
                        AttackType = CombatAttackType.HeavyAttack, ReactionIntent = final ? CombatReactionIntent.Knockdown : CombatReactionIntent.HitReaction,
                        Damage = final ? 34f : 24f, PoiseDamage = final ? 28f : 18f, GuardDamage = 0f,
                        CanBeGuarded = false, CanBePerfectGuarded = false, CanBePerfectEvaded = true,
                        MaxHitsPerTarget = 1, EffectiveRange = detached ? 20f : 4f, EffectiveAngle = 180f
                    };
                }
                hitTrack.Clips = clips;
                tracks.Add(hitTrack);
            }

            CombatTimelineTrack previewTrack = new CombatTimelineTrack("Animation Preview", CombatTimelineTrackKind.AnimationPreview);
            List<CombatTimelineClip> previews = new List<CombatTimelineClip>
            {
                CreatePreview(stateName, 0f, string.IsNullOrEmpty(secondPreviewState) ? duration : secondPreviewStart)
            };
            if (!string.IsNullOrEmpty(secondPreviewState)) previews.Add(CreatePreview(secondPreviewState, secondPreviewStart, duration));
            previewTrack.Clips = previews.ToArray();
            tracks.Add(previewTrack);
            timeline.SetTracks(tracks.ToArray());

            BossDefenseRewardCondition[] allConditions = new BossDefenseRewardCondition[hitCount];
            for (int i = 0; i < hitCount; i++) allConditions[i] = Condition($"{displayName}_{i + 1}", BossDefenseOutcomeMask.PerfectEvade);
            List<BossDefenseRewardRule> rewards = new List<BossDefenseRewardRule>();
            if (hitCount > 0) rewards.Add(Rule("FinalPE", id == RavenBossAttackIds.SwordAuraComboId ? 1.4f : id == RavenBossAttackIds.EvadeBackSwordAuraId ? 1.1f : 1.5f, allConditions[hitCount - 1]));
            if (hitCount > 1 && (id == RavenBossAttackIds.BetaChargeComboId || id == RavenBossAttackIds.BurstAreaSlashId)) rewards.Add(Rule("AllPE", 2f, allConditions));
            timeline.SetDefenseRewardRules(rewards.ToArray());
            EditorUtility.SetDirty(timeline);
            return timeline;
        }

        private static CombatAnimationClipWindow CreatePreview(string stateName, float start, float end)
        {
            return new CombatAnimationClipWindow
            {
                Name = stateName, AnimatorStateName = stateName, PreviewAnimationClip = FindAnimationClip(stateName),
                StartTime = start, EndTime = end, ClipSpeed = 2.5f
            };
        }

        private static AnimationClip FindAnimationClip(string name)
        {
            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(RavenAnimationPath);
            string importedSuffix = $"|{name}";
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is not AnimationClip clip || clip.name.StartsWith("__preview__", StringComparison.Ordinal))
                {
                    continue;
                }

                if (clip.name == name || clip.name.EndsWith(importedSuffix, StringComparison.Ordinal))
                {
                    return clip;
                }
            }

            Debug.LogWarning($"Raven animation clip not found: {name}");
            return null;
        }

        private static void ConfigureActionSet()
        {
            BossActionSet set = AssetDatabase.LoadAssetAtPath<BossActionSet>(ActionSetPath);
            if (set == null) throw new InvalidOperationException($"Missing action set: {ActionSetPath}");
            BossActionSetEntry[] entries =
            {
                Entry(RavenBossAttackIds.SlashId, BossActionKind.Attack, true, BossActionPoolId.CloseDuel, 30,18,18,7,BossActionStrength.Low),
                Entry(RavenBossAttackIds.SlashChainId, BossActionKind.Attack, true, BossActionPoolId.CloseDuel,30,18,18,11,BossActionStrength.Low, preferences: BossActionPreferenceFlags.PreviousActionSlash),
                Entry(RavenBossAttackIds.MoveComboId, BossActionKind.Attack,true,BossActionPoolId.CloseDuel,28,28,30,15,BossActionStrength.Medium),
                Entry(RavenBossAttackIds.MoveChainComboId, BossActionKind.Attack,true,BossActionPoolId.CloseDuel,28,28,31,24,BossActionStrength.High, pressurePreference: BossPressurePreference.MidLow),
                Entry(RavenBossAttackIds.BetaChargeComboId, BossActionKind.Attack,true,BossActionPoolId.Special,0,14,18,24,BossActionStrength.Critical,BossActionFollowUpPolicy.MustEnterNeutral,BossPressurePreference.MidLow,BossActionPreferenceFlags.BossLowHealth|BossActionPreferenceFlags.BossLowPoise|BossActionPreferenceFlags.BossNearPhaseTransition),
                Entry(RavenBossAttackIds.EvadeBackRushId, BossActionKind.Attack,true,BossActionPoolId.HitEscape,20,20,22,14,BossActionStrength.Medium, pressurePreference: BossPressurePreference.MidLow),
                Entry(RavenBossAttackIds.ChaseGrabId, BossActionKind.Attack,true,BossActionPoolId.GapClose,15,20,23,17,BossActionStrength.Medium),
                Entry(RavenBossAttackIds.ChaseComboId, BossActionKind.Attack,true,BossActionPoolId.GapClose,15,20,24,25,BossActionStrength.High),
                Entry(RavenBossAttackIds.SlashComboId, BossActionKind.Attack,true,BossActionPoolId.Special,10,15,18,30,BossActionStrength.Critical,BossActionFollowUpPolicy.MustEnterNeutral,BossPressurePreference.MidLow, tags:new[]{BossActionTags.MultiHitPressure}),
                Entry(RavenBossAttackIds.BurstAreaSlashId, BossActionKind.Attack,true,BossActionPoolId.Special,0,8,12,30,BossActionStrength.Critical,BossActionFollowUpPolicy.MustEnterNeutral,BossPressurePreference.Low),
                Entry(RavenBossAttackIds.SwordAuraComboId, BossActionKind.Attack,true,BossActionPoolId.Ranged,0,12,15,24,BossActionStrength.High, pressurePreference: BossPressurePreference.Mid),
                Entry(RavenBossAttackIds.EvadeBackSwordAuraId, BossActionKind.Attack,true,BossActionPoolId.HitEscape,0,16,18,17,BossActionStrength.Medium, pressurePreference: BossPressurePreference.MidLow),
                Entry(RavenBossAttackIds.RapidMoveBackId, BossActionKind.Reposition,true,BossActionPoolId.HitEscape,15,15,15,0,BossActionStrength.Defensive,BossActionFollowUpPolicy.MustStrafe,BossPressurePreference.High)
            };
            BossPhasePressureProfile[] pressures =
            {
                BossPhasePressureProfile.CreateDefault(BossCombatPhaseId.Phase1), BossPhasePressureProfile.CreateDefault(BossCombatPhaseId.Phase2),
                BossPhasePressureProfile.CreateDefault(BossCombatPhaseId.Desperation)
            };
            BossActionPoolPolicy[] policies =
            {
                new BossActionPoolPolicy{Pool=BossActionPoolId.HitEscape,MinDistance=0,MaxDistance=3.5f,RequireHitStaggerContext=true,RecentThreeLimit=2},
                new BossActionPoolPolicy{Pool=BossActionPoolId.CloseDuel,MinDistance=0,MaxDistance=3.5f},
                new BossActionPoolPolicy{Pool=BossActionPoolId.GapClose,MinDistance=3.5f,MaxDistance=9,DisallowHitStaggerContext=true},
                new BossActionPoolPolicy{Pool=BossActionPoolId.Special,MinDistance=0,MaxDistance=3.5f,DisallowDuringPressureDecay=true,RecentThreeLimit=2},
                new BossActionPoolPolicy{Pool=BossActionPoolId.Ranged,MinDistance=6,MaxDistance=11,DisallowHitStaggerContext=true,DisallowDuringPressureDecay=true,RecentThreeLimit=1}
            };
            set.Configure(entries, pressures, policies, new BossAiTuning());
            EditorUtility.SetDirty(set);
        }

        private static BossActionSetEntry Entry(string id, BossActionKind kind, bool enabled, BossActionPoolId pool, float p1,float p2,float p3,float pressure,BossActionStrength strength,
            BossActionFollowUpPolicy followUp=BossActionFollowUpPolicy.None,BossPressurePreference pressurePreference=BossPressurePreference.None,BossActionPreferenceFlags preferences=BossActionPreferenceFlags.None,string[] tags=null)
        {
            BossActionPhaseMask phases = p1 <= 0 ? BossActionPhaseMask.Phase2 | BossActionPhaseMask.Desperation : BossActionPhaseMask.All;
            return new BossActionSetEntry(id,kind,enabled,tags,phases,new BossPhaseWeights(p1,p2,p3),pool,pressure,strength,followUp,pressurePreference,preferences,1);
        }

        private static void ConfigureMotionProfile()
        {
            BossMotionWarpProfile profile = AssetDatabase.LoadAssetAtPath<BossMotionWarpProfile>(MotionProfilePath);
            if (profile == null) throw new InvalidOperationException($"Missing motion profile: {MotionProfilePath}");
            List<BossAttackMotionConfig> configs = new List<BossAttackMotionConfig>(profile.Attacks ?? Array.Empty<BossAttackMotionConfig>());
            SetSelection(configs,RavenBossAttackIds.SlashId,0,2.6f,.5f,0); SetSelection(configs,RavenBossAttackIds.SlashChainId,0,2.6f,.5f,0);
            SetSelection(configs,RavenBossAttackIds.MoveComboId,0,3.5f,.5f,0); SetSelection(configs,RavenBossAttackIds.MoveChainComboId,0,3.5f,.5f,0);
            SetSelection(configs,RavenBossAttackIds.EvadeBackRushId,0,2.6f,0,2.2f); SetSelection(configs,RavenBossAttackIds.ChaseGrabId,3.5f,9,.5f,0);
            SetSelection(configs,RavenBossAttackIds.ChaseComboId,3.5f,9,.5f,0); SetSelection(configs,RavenBossAttackIds.SlashComboId,0,3.5f,.5f,0);
            Upsert(configs, SimpleMotion(RavenBossAttackIds.BetaChargeComboId,0,3.5f,.5f,0));
            Upsert(configs, SimpleMotion(RavenBossAttackIds.BurstAreaSlashId,0,3.5f,.5f,0));
            Upsert(configs, SwordAuraComboMotion());
            Upsert(configs, EvadeBackSwordAuraMotion());
            Upsert(configs, BackMotion(RavenBossAttackIds.RapidMoveBackId,0f,1.1f));
            profile.Configure(configs.ToArray());
            EditorUtility.SetDirty(profile);
        }

        private static BossAttackMotionConfig SimpleMotion(string id,float min,float max,float forward,float back) =>
            new BossAttackMotionConfig(id,BossAttackMotionMode.None,new BossAttackSelectionProfile(min,max,forward,back),Array.Empty<BossMotionWarpWindow>(),Array.Empty<BossRootMotionSuppressWindow>(),Array.Empty<BossCodeMoveWindow>());
        private static BossAttackMotionConfig BackMotion(string id,float start,float end) =>
            new BossAttackMotionConfig(id,BossAttackMotionMode.CodeDrivenWarped,new BossAttackSelectionProfile(0,2.6f,0,2.2f),Array.Empty<BossMotionWarpWindow>(),Array.Empty<BossRootMotionSuppressWindow>(),new[]{BossCodeMoveWindow.FixedBack("BackStep",start,end,4f,2.2f)});

        /// <summary>创建剑气三连每次发射前重新跟踪目标、发射后保持直线方向的动作配置。</summary>
        private static BossAttackMotionConfig SwordAuraComboMotion() =>
            new BossAttackMotionConfig(
                RavenBossAttackIds.SwordAuraComboId,
                BossAttackMotionMode.CodeDrivenWarped,
                new BossAttackSelectionProfile(6,11,0,0),
                Array.Empty<BossMotionWarpWindow>(),
                Array.Empty<BossRootMotionSuppressWindow>(),
                new[]
                {
                    BossCodeMoveWindow.FaceTarget("AimVolley12",.21666667f,.8333333f,720f),
                    BossCodeMoveWindow.FaceTarget("AimShot3",1.7166667f,2.0666666f,720f)
                });

        /// <summary>保留后跳剑气的动画 Root Motion，并在唯一剑气发射前重新跟踪目标。</summary>
        private static BossAttackMotionConfig EvadeBackSwordAuraMotion() =>
            new BossAttackMotionConfig(
                RavenBossAttackIds.EvadeBackSwordAuraId,
                BossAttackMotionMode.RootMotionWarped,
                new BossAttackSelectionProfile(0,3.5f,0,2.2f),
                Array.Empty<BossMotionWarpWindow>(),
                Array.Empty<BossRootMotionSuppressWindow>(),
                new[]
                {
                    BossCodeMoveWindow.FaceTarget("AimShot",1.4333333f,1.7833333f,720f)
                });

        private static void SetSelection(List<BossAttackMotionConfig> configs,string id,float min,float max,float forward,float back)
        { for(int i=0;i<configs.Count;i++) if(configs[i].AttackId==id){configs[i].Selection=new BossAttackSelectionProfile(min,max,forward,back);return;} }
        private static void Upsert(List<BossAttackMotionConfig> configs,BossAttackMotionConfig config)
        { for(int i=0;i<configs.Count;i++) if(configs[i].AttackId==config.AttackId){configs[i]=config;return;} configs.Add(config); }

        private static void UpdateCatalog(params BossAttackTimelineAsset[] additions)
        {
            CombatTimelineCatalog catalog = AssetDatabase.LoadAssetAtPath<CombatTimelineCatalog>(CatalogPath);
            if (catalog == null) throw new InvalidOperationException($"Missing catalog: {CatalogPath}");
            List<CombatTimelineActionAsset> timelines = new List<CombatTimelineActionAsset>(catalog.Timelines ?? Array.Empty<CombatTimelineActionAsset>());
            for(int i=0;i<additions.Length;i++) if(additions[i]!=null && !timelines.Contains(additions[i])) timelines.Add(additions[i]);
            catalog.Configure(catalog.CatalogId,CombatTimelineOwner.Boss,timelines.ToArray());
            EditorUtility.SetDirty(catalog);
        }

        private static BossDefenseRewardCondition Condition(string id,BossDefenseOutcomeMask outcomes) => new BossDefenseRewardCondition{HitNodeId=id,AllowedOutcomes=outcomes};
        private static BossDefenseRewardRule Rule(string name,float duration,params BossDefenseRewardCondition[] conditions) => new BossDefenseRewardRule{Name=name,RewardDuration=duration,Conditions=conditions};
    }
}
#endif
