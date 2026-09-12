// 文件说明：通过独立蒙皮覆盖层维护 Raven Boss 可动画的全身/头发 HDR 颜色，并控制身体、武器和常驻武器光效的可见度。
// 所属模块：Boss 动画表现。
// 运行影响：只修改视觉组件与 MaterialPropertyBlock，不创建材质实例，也不影响碰撞、换手、拖尾或战斗状态。

using UnityEngine;

namespace ProjectEVE.Boss.Animation
{
    /// <summary>
    /// 把 Animator 写入的 HDR 颜色作为贴图采样后的全身覆盖色，并允许红光提示只覆盖 Raven 的两个头发材质槽。
    /// BodyColor/HairColor Alpha 只表示颜色覆盖权重；Visibility 为一时完全显示，为零时只禁用视觉组件。
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class BossBodyColorController : MonoBehaviour
    {
        private const float VisibilityDisableThreshold = 0.0001f;
        private const int HairMaterial02Index = 5;
        private const int HairMaterial01Index = 6;

        private static readonly int OverlayColorPropertyId = Shader.PropertyToID("_OverlayColor");
        private static readonly int BaseColorPropertyId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorPropertyId = Shader.PropertyToID("_Color");

        /// <summary>Raven 身体、服装、头部和头发共用的 SkinnedMeshRenderer。</summary>
        [SerializeField] private SkinnedMeshRenderer bodyRenderer;
        /// <summary>复用身体网格和骨骼、只绘制 HDR 颜色的透明覆盖 SkinnedMeshRenderer。</summary>
        [SerializeField] private SkinnedMeshRenderer bodyColorOverlayRenderer;
        /// <summary>唯一 Raven 武器 Renderer；只接收 Visibility，不接收 BodyColor RGB。</summary>
        [SerializeField] private Renderer weaponRenderer;
        /// <summary>武器 Renderer 中使用 MI_CH_M_NA_53_Weapon 的材质槽。</summary>
        [SerializeField, Min(0)] private int weaponMaterialIndex;
        /// <summary>FX_BossWeaponIdleGlow 下的 CoreLine、LeftEdge 和 RightEdge。</summary>
        [SerializeField] private LineRenderer[] weaponIdleGlowLines = new LineRenderer[0];
        /// <summary>FX_BossWeaponIdleGlow_BaseNode 的 Renderer。</summary>
        [SerializeField] private Renderer weaponIdleGlowBaseRenderer;
        /// <summary>BaseNode Renderer 中使用 M_CombatVfx_Glow 的材质槽。</summary>
        [SerializeField, Min(0)] private int weaponIdleGlowBaseMaterialIndex;
        /// <summary>FX_BossWeaponIdleGlow 下的 TipLight 和 BaseLight。</summary>
        [SerializeField] private Light[] weaponIdleGlowLights = new Light[0];
        /// <summary>可由 AnimationClip 录制的 HDR 身体颜色；Alpha 只控制身体颜色覆盖权重。</summary>
        [ColorUsage(true, true)]
        [SerializeField] private Color bodyColor = new Color(0f, 0f, 0f, 0f);
        /// <summary>可由 AnimationClip 录制的 HDR 头发颜色；只覆盖 Raven 身体 Renderer 的材质槽 5 和 6。</summary>
        [ColorUsage(true, true)]
        [SerializeField] private Color hairColor = new Color(0f, 0f, 0f, 0f);
        /// <summary>可由 AnimationClip 录制的整体视觉可见度；一为完全显示，零为完全隐藏。</summary>
        [Range(0f, 1f)]
        [SerializeField] private float visibility = 1f;

        private Material[] cachedBodyMaterials;
        private Material[] cachedBodyOverlayMaterials;
        private Material cachedWeaponMaterial;
        private Material cachedIdleGlowBaseMaterial;
        private LineRenderer[] cachedIdleGlowLines;
        private Light[] cachedIdleGlowLights;
        private Color originalWeaponBaseColor;
        private Color originalIdleGlowBaseColor;
        private Color[] originalLineStartColors;
        private Color[] originalLineEndColors;
        private bool[] originalLineEnabledStates;
        private float[] originalLightIntensities;
        private bool[] originalLightEnabledStates;
        private bool originalBodyRendererEnabled;
        private bool originalWeaponRendererEnabled;
        private bool originalIdleGlowBaseRendererEnabled;
        private MaterialPropertyBlock propertyBlock;
        private Color lastAppliedBodyColor;
        private Color lastAppliedHairColor;
        private float lastAppliedVisibility;
        private bool isConfigured;
        private bool hasAppliedAppearance;
        private bool configurationErrorLogged;

        /// <summary>组件启用时缓存静态材质与 authored 视觉状态，并立即应用当前动画值。</summary>
        private void OnEnable()
        {
            if (!isConfigured || HasVisualLayoutChanged())
            {
                RebuildVisualCache(false);
            }

            ApplyAppearanceIfChanged(true);
        }

        /// <summary>Inspector 配置变化后重建必要缓存，使 Animation Window 之外的编辑也能立即预览。</summary>
        private void OnValidate()
        {
            if (!isConfigured || HasVisualLayoutChanged())
            {
                RebuildVisualCache();
            }

            ApplyAppearanceIfChanged(true);
        }

        /// <summary>Animator 写入 BodyColor、HairColor 或 Visibility 曲线后，把新值提交到全部绑定视觉组件。</summary>
        private void OnDidApplyAnimationProperties()
        {
            ApplyAppearanceIfChanged();
        }

        /// <summary>Play Mode 中兜底检查动画值和视觉绑定变化，且只在实际变化时写入。</summary>
        private void LateUpdate()
        {
            if (!Application.isPlaying)
            {
                return;
            }

            if (HasVisualLayoutChanged())
            {
                RebuildVisualCache();
            }

            // ApplyAppearanceIfChanged();
        }

        /// <summary>组件禁用时恢复身体、武器和常驻光效的 authored 外观与启用状态。</summary>
        private void OnDisable()
        {
            RestoreOriginalAppearance();
        }

        /// <summary>首次添加组件时按 Raven 固定对象名寻找视觉绑定，仍允许 Inspector 显式覆盖。</summary>
        private void Reset()
        {
            SkinnedMeshRenderer[] skinnedRenderers = GetComponentsInChildren<SkinnedMeshRenderer>(true);
            for (int i = 0; i < skinnedRenderers.Length; i++)
            {
                if (skinnedRenderers[i].name == "RavenBodyColorOverlay")
                {
                    bodyColorOverlayRenderer = skinnedRenderers[i];
                }
                else if (bodyRenderer == null)
                {
                    bodyRenderer = skinnedRenderers[i];
                }
            }
            Renderer[] renderers = GetComponentsInChildren<Renderer>(true);
            for (int i = 0; i < renderers.Length; i++)
            {
                Renderer renderer = renderers[i];
                if (renderer.name == "CH_M_NA_53_Weapon.001")
                {
                    weaponRenderer = renderer;
                }
                else if (renderer.name == "FX_BossWeaponIdleGlow_BaseNode")
                {
                    weaponIdleGlowBaseRenderer = renderer;
                }
            }

            weaponIdleGlowLines = FindNamedIdleGlowLines();
            weaponIdleGlowLights = FindNamedIdleGlowLights();
            RebuildVisualCache(false);
            ApplyAppearanceIfChanged(true);
        }

        /// <summary>检查 Renderer 材质、常驻光效数组或绑定引用是否已经改变。</summary>
        /// <returns>true 表示必须重新读取 authored 外观；false 表示现有缓存仍有效。</returns>
        private bool HasVisualLayoutChanged()
        {
            if (bodyRenderer == null || bodyColorOverlayRenderer == null || weaponRenderer == null ||
                weaponIdleGlowBaseRenderer == null || cachedBodyMaterials == null ||
                cachedBodyOverlayMaterials == null || cachedIdleGlowLines == null || cachedIdleGlowLights == null)
            {
                return true;
            }

            Material[] sharedMaterials = bodyRenderer.sharedMaterials;
            if (sharedMaterials.Length != cachedBodyMaterials.Length)
            {
                return true;
            }

            for (int i = 0; i < sharedMaterials.Length; i++)
            {
                if (sharedMaterials[i] != cachedBodyMaterials[i])
                {
                    return true;
                }
            }

            Material[] overlayMaterials = bodyColorOverlayRenderer.sharedMaterials;
            if (overlayMaterials.Length != cachedBodyOverlayMaterials.Length)
            {
                return true;
            }

            for (int i = 0; i < overlayMaterials.Length; i++)
            {
                if (overlayMaterials[i] != cachedBodyOverlayMaterials[i])
                {
                    return true;
                }
            }

            Material[] weaponMaterials = weaponRenderer.sharedMaterials;
            if (weaponMaterialIndex < 0 || weaponMaterialIndex >= weaponMaterials.Length ||
                weaponMaterials[weaponMaterialIndex] != cachedWeaponMaterial)
            {
                return true;
            }

            Material[] glowMaterials = weaponIdleGlowBaseRenderer.sharedMaterials;
            if (weaponIdleGlowBaseMaterialIndex < 0 ||
                weaponIdleGlowBaseMaterialIndex >= glowMaterials.Length ||
                glowMaterials[weaponIdleGlowBaseMaterialIndex] != cachedIdleGlowBaseMaterial)
            {
                return true;
            }

            return !ReferencesMatch(weaponIdleGlowLines, cachedIdleGlowLines) ||
                !ReferencesMatch(weaponIdleGlowLights, cachedIdleGlowLights);
        }

        /// <summary>校验所有显式视觉绑定，并缓存材质颜色、线条颜色、灯光强度和启用状态。</summary>
        /// <param name="reportErrors">true 表示缺失配置时输出错误；false 用于组件刚添加、Inspector 尚未完成绑定的阶段。</param>
        private void RebuildVisualCache(bool reportErrors = true)
        {
            isConfigured = false;
            hasAppliedAppearance = false;

            if (!ValidateBindings(reportErrors, out Material[] bodyMaterials,
                    out Material[] bodyOverlayMaterials,
                    out Material weaponMaterial, out Material glowBaseMaterial))
            {
                return;
            }

            cachedBodyMaterials = bodyMaterials;
            cachedBodyOverlayMaterials = bodyOverlayMaterials;
            cachedWeaponMaterial = weaponMaterial;
            cachedIdleGlowBaseMaterial = glowBaseMaterial;
            cachedIdleGlowLines = (LineRenderer[])weaponIdleGlowLines.Clone();
            cachedIdleGlowLights = (Light[])weaponIdleGlowLights.Clone();
            originalWeaponBaseColor = weaponMaterial.GetColor(BaseColorPropertyId);
            originalIdleGlowBaseColor = glowBaseMaterial.GetColor(ColorPropertyId);
            originalBodyRendererEnabled = bodyRenderer.enabled;
            originalWeaponRendererEnabled = weaponRenderer.enabled;
            originalIdleGlowBaseRendererEnabled = weaponIdleGlowBaseRenderer.enabled;

            originalLineStartColors = new Color[cachedIdleGlowLines.Length];
            originalLineEndColors = new Color[cachedIdleGlowLines.Length];
            originalLineEnabledStates = new bool[cachedIdleGlowLines.Length];
            for (int i = 0; i < cachedIdleGlowLines.Length; i++)
            {
                LineRenderer line = cachedIdleGlowLines[i];
                originalLineStartColors[i] = line.startColor;
                originalLineEndColors[i] = line.endColor;
                originalLineEnabledStates[i] = line.enabled;
            }

            originalLightIntensities = new float[cachedIdleGlowLights.Length];
            originalLightEnabledStates = new bool[cachedIdleGlowLights.Length];
            for (int i = 0; i < cachedIdleGlowLights.Length; i++)
            {
                Light glowLight = cachedIdleGlowLights[i];
                originalLightIntensities[i] = glowLight.intensity;
                originalLightEnabledStates[i] = glowLight.enabled;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            configurationErrorLogged = false;
            isConfigured = true;
        }

        /// <summary>验证身体、武器和常驻光效拥有计划要求的材质属性与组件数量。</summary>
        /// <param name="reportErrors">true 表示首次发现配置问题时记录错误。</param>
        /// <param name="bodyMaterials">成功时写回身体七个静态材质。</param>
        /// <param name="bodyOverlayMaterials">成功时写回颜色覆盖层的七个材质槽。</param>
        /// <param name="weaponMaterial">成功时写回唯一武器透明材质。</param>
        /// <param name="glowBaseMaterial">成功时写回 BaseNode 使用的共享发光材质。</param>
        /// <returns>true 表示全部绑定可以安全驱动；false 表示至少一个必要绑定失效。</returns>
        private bool ValidateBindings(
            bool reportErrors,
            out Material[] bodyMaterials,
            out Material[] bodyOverlayMaterials,
            out Material weaponMaterial,
            out Material glowBaseMaterial)
        {
            bodyMaterials = null;
            bodyOverlayMaterials = null;
            weaponMaterial = null;
            glowBaseMaterial = null;

            if (bodyRenderer == null)
            {
                return FailValidation(reportErrors, "BossBodyColorController 缺少身体 SkinnedMeshRenderer 绑定。");
            }

            bodyMaterials = bodyRenderer.sharedMaterials;
            if (bodyMaterials.Length != 7)
            {
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController 需要 Raven 身体的 7 个材质槽，当前为 {bodyMaterials.Length} 个。");
            }

            for (int i = 0; i < bodyMaterials.Length; i++)
            {
                Material material = bodyMaterials[i];
                if (material == null)
                {
                    return FailValidation(
                        reportErrors,
                        $"BossBodyColorController 身体材质槽 {i} 为空。");
                }
            }

            if (bodyColorOverlayRenderer == null)
            {
                return FailValidation(reportErrors, "BossBodyColorController 缺少身体颜色覆盖 Renderer 绑定。");
            }

            if (bodyColorOverlayRenderer.sharedMesh != bodyRenderer.sharedMesh ||
                bodyColorOverlayRenderer.rootBone != bodyRenderer.rootBone ||
                !ReferencesMatch(bodyColorOverlayRenderer.bones, bodyRenderer.bones))
            {
                return FailValidation(
                    reportErrors,
                    "BossBodyColorController 的颜色覆盖 Renderer 必须与身体使用相同网格、RootBone 和骨骼顺序。");
            }

            bodyOverlayMaterials = bodyColorOverlayRenderer.sharedMaterials;
            if (bodyOverlayMaterials.Length != bodyMaterials.Length)
            {
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController 颜色覆盖层需要 {bodyMaterials.Length} 个材质槽，当前为 {bodyOverlayMaterials.Length} 个。");
            }

            for (int i = 0; i < bodyOverlayMaterials.Length; i++)
            {
                Material material = bodyOverlayMaterials[i];
                if (material == null || !material.HasProperty(OverlayColorPropertyId))
                {
                    string materialName = material != null ? material.name : "<null>";
                    return FailValidation(
                        reportErrors,
                        $"BossBodyColorController 覆盖材质槽 {i}（{materialName}）缺少 _OverlayColor。");
                }
            }

            if (weaponRenderer == null)
            {
                return FailValidation(reportErrors, "BossBodyColorController 缺少 Raven 武器 Renderer 绑定。");
            }

            Material[] weaponMaterials = weaponRenderer.sharedMaterials;
            if (weaponMaterialIndex < 0 || weaponMaterialIndex >= weaponMaterials.Length)
            {
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController 武器材质槽 {weaponMaterialIndex} 超出范围。");
            }

            weaponMaterial = weaponMaterials[weaponMaterialIndex];
            if (weaponMaterial == null || !weaponMaterial.HasProperty(BaseColorPropertyId))
            {
                string materialName = weaponMaterial != null ? weaponMaterial.name : "<null>";
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController 武器材质（{materialName}）缺少 _BaseColor。");
            }

            if (weaponIdleGlowLines == null || weaponIdleGlowLines.Length != 3 ||
                ContainsNullReference(weaponIdleGlowLines))
            {
                return FailValidation(
                    reportErrors,
                    "BossBodyColorController 必须绑定 FX_BossWeaponIdleGlow 的 3 个 LineRenderer。");
            }

            if (weaponIdleGlowBaseRenderer == null)
            {
                return FailValidation(reportErrors, "BossBodyColorController 缺少 IdleGlow BaseNode Renderer 绑定。");
            }

            Material[] glowMaterials = weaponIdleGlowBaseRenderer.sharedMaterials;
            if (weaponIdleGlowBaseMaterialIndex < 0 || weaponIdleGlowBaseMaterialIndex >= glowMaterials.Length)
            {
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController IdleGlow BaseNode 材质槽 {weaponIdleGlowBaseMaterialIndex} 超出范围。");
            }

            glowBaseMaterial = glowMaterials[weaponIdleGlowBaseMaterialIndex];
            if (glowBaseMaterial == null || !glowBaseMaterial.HasProperty(ColorPropertyId))
            {
                string materialName = glowBaseMaterial != null ? glowBaseMaterial.name : "<null>";
                return FailValidation(
                    reportErrors,
                    $"BossBodyColorController IdleGlow BaseNode 材质（{materialName}）缺少 _Color。");
            }

            if (weaponIdleGlowLights == null || weaponIdleGlowLights.Length != 2 ||
                ContainsNullReference(weaponIdleGlowLights))
            {
                return FailValidation(
                    reportErrors,
                    "BossBodyColorController 必须绑定 FX_BossWeaponIdleGlow 的 2 个 Light。");
            }

            return true;
        }

        /// <summary>仅在强制刷新、颜色变化或可见度变化时更新全部绑定视觉组件。</summary>
        /// <param name="force">true 表示忽略动画值缓存并立即重写；false 表示相同值不重复提交。</param>
        private void ApplyAppearanceIfChanged(bool force = false)
        {
            if (!isConfigured)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            float normalizedVisibility = Mathf.Clamp01(visibility);
            if (!force && hasAppliedAppearance && bodyColor == lastAppliedBodyColor &&
                hairColor == lastAppliedHairColor &&
                Mathf.Approximately(normalizedVisibility, lastAppliedVisibility))
            {
                return;
            }

            bool shouldRender = normalizedVisibility > VisibilityDisableThreshold;
            bodyRenderer.enabled = originalBodyRendererEnabled && shouldRender;
            weaponRenderer.enabled = originalWeaponRendererEnabled && shouldRender;
            weaponIdleGlowBaseRenderer.enabled = originalIdleGlowBaseRendererEnabled && shouldRender;

            Color overlayColor = new Color(
                bodyColor.r,
                bodyColor.g,
                bodyColor.b,
                Mathf.Clamp01(bodyColor.a));
            Color hairOverlayColor = CompositeOverlay(
                overlayColor,
                new Color(hairColor.r, hairColor.g, hairColor.b, Mathf.Clamp01(hairColor.a)));
            bool hasVisibleOverlay = overlayColor.a > VisibilityDisableThreshold ||
                hairOverlayColor.a > VisibilityDisableThreshold;
            bodyColorOverlayRenderer.enabled = originalBodyRendererEnabled && shouldRender && hasVisibleOverlay;
            for (int i = 0; i < cachedBodyOverlayMaterials.Length; i++)
            {
                bodyColorOverlayRenderer.GetPropertyBlock(propertyBlock, i);
                propertyBlock.SetColor(
                    OverlayColorPropertyId,
                    IsHairMaterialIndex(i) ? hairOverlayColor : overlayColor);
                bodyColorOverlayRenderer.SetPropertyBlock(propertyBlock, i);
            }

            Color weaponBaseColor = originalWeaponBaseColor;
            weaponBaseColor.a = originalWeaponBaseColor.a * normalizedVisibility;
            weaponRenderer.GetPropertyBlock(propertyBlock, weaponMaterialIndex);
            propertyBlock.SetColor(BaseColorPropertyId, weaponBaseColor);
            weaponRenderer.SetPropertyBlock(propertyBlock, weaponMaterialIndex);

            for (int i = 0; i < cachedIdleGlowLines.Length; i++)
            {
                LineRenderer line = cachedIdleGlowLines[i];
                Color startColor = originalLineStartColors[i];
                Color endColor = originalLineEndColors[i];
                startColor.a *= normalizedVisibility;
                endColor.a *= normalizedVisibility;
                line.startColor = startColor;
                line.endColor = endColor;
                line.enabled = originalLineEnabledStates[i] && shouldRender;
            }

            Color glowBaseColor = originalIdleGlowBaseColor;
            glowBaseColor.a = originalIdleGlowBaseColor.a * normalizedVisibility;
            weaponIdleGlowBaseRenderer.GetPropertyBlock(
                propertyBlock,
                weaponIdleGlowBaseMaterialIndex);
            propertyBlock.SetColor(ColorPropertyId, glowBaseColor);
            weaponIdleGlowBaseRenderer.SetPropertyBlock(
                propertyBlock,
                weaponIdleGlowBaseMaterialIndex);

            for (int i = 0; i < cachedIdleGlowLights.Length; i++)
            {
                Light glowLight = cachedIdleGlowLights[i];
                glowLight.intensity = originalLightIntensities[i] * normalizedVisibility;
                glowLight.enabled = originalLightEnabledStates[i] && shouldRender;
            }

            lastAppliedBodyColor = bodyColor;
            lastAppliedHairColor = hairColor;
            lastAppliedVisibility = normalizedVisibility;
            hasAppliedAppearance = true;
        }

        /// <summary>清除身体颜色覆盖，并恢复全部视觉组件的 authored 颜色、亮度和启用状态。</summary>
        private void RestoreOriginalAppearance()
        {
            if (bodyColorOverlayRenderer != null)
            {
                bodyColorOverlayRenderer.enabled = false;
            }

            if (!isConfigured || bodyRenderer == null)
            {
                return;
            }

            propertyBlock ??= new MaterialPropertyBlock();
            for (int i = 0; i < cachedBodyOverlayMaterials.Length; i++)
            {
                bodyColorOverlayRenderer.GetPropertyBlock(propertyBlock, i);
                propertyBlock.SetColor(OverlayColorPropertyId, Color.clear);
                bodyColorOverlayRenderer.SetPropertyBlock(propertyBlock, i);
            }

            bodyRenderer.enabled = originalBodyRendererEnabled;

            if (weaponRenderer != null && cachedWeaponMaterial != null)
            {
                weaponRenderer.GetPropertyBlock(propertyBlock, weaponMaterialIndex);
                propertyBlock.SetColor(BaseColorPropertyId, originalWeaponBaseColor);
                weaponRenderer.SetPropertyBlock(propertyBlock, weaponMaterialIndex);
                weaponRenderer.enabled = originalWeaponRendererEnabled;
            }

            for (int i = 0; i < cachedIdleGlowLines.Length; i++)
            {
                LineRenderer line = cachedIdleGlowLines[i];
                line.startColor = originalLineStartColors[i];
                line.endColor = originalLineEndColors[i];
                line.enabled = originalLineEnabledStates[i];
            }

            if (weaponIdleGlowBaseRenderer != null && cachedIdleGlowBaseMaterial != null)
            {
                weaponIdleGlowBaseRenderer.GetPropertyBlock(
                    propertyBlock,
                    weaponIdleGlowBaseMaterialIndex);
                propertyBlock.SetColor(ColorPropertyId, originalIdleGlowBaseColor);
                weaponIdleGlowBaseRenderer.SetPropertyBlock(
                    propertyBlock,
                    weaponIdleGlowBaseMaterialIndex);
                weaponIdleGlowBaseRenderer.enabled = originalIdleGlowBaseRendererEnabled;
            }

            for (int i = 0; i < cachedIdleGlowLights.Length; i++)
            {
                Light glowLight = cachedIdleGlowLights[i];
                glowLight.intensity = originalLightIntensities[i];
                glowLight.enabled = originalLightEnabledStates[i];
            }

            hasAppliedAppearance = false;
        }

        /// <summary>判断固定的 Raven 身体材质槽是否属于头发。</summary>
        /// <param name="materialIndex">SkinnedMeshRenderer 的材质槽索引。</param>
        /// <returns>true 表示槽 5 或槽 6，应接收 HairColor；false 表示只接收 BodyColor。</returns>
        private static bool IsHairMaterialIndex(int materialIndex)
        {
            return materialIndex == HairMaterial02Index || materialIndex == HairMaterial01Index;
        }

        /// <summary>按 HairColor 覆盖 BodyColor 的顺序合成两个非预乘 HDR 颜色覆盖层。</summary>
        /// <param name="baseOverlay">全身覆盖层，Alpha 为覆盖权重。</param>
        /// <param name="topOverlay">头发专用覆盖层，Alpha 为覆盖权重。</param>
        /// <returns>可直接写入 _BodyColorOverlay 的非预乘 HDR 颜色。</returns>
        private static Color CompositeOverlay(Color baseOverlay, Color topOverlay)
        {
            float baseAlpha = Mathf.Clamp01(baseOverlay.a);
            float topAlpha = Mathf.Clamp01(topOverlay.a);
            float resultAlpha = topAlpha + baseAlpha * (1f - topAlpha);
            if (resultAlpha <= 0f)
            {
                return Color.clear;
            }

            float baseContribution = baseAlpha * (1f - topAlpha);
            Vector3 premultipliedRgb =
                new Vector3(topOverlay.r, topOverlay.g, topOverlay.b) * topAlpha +
                new Vector3(baseOverlay.r, baseOverlay.g, baseOverlay.b) * baseContribution;
            Vector3 resultRgb = premultipliedRgb / resultAlpha;
            return new Color(resultRgb.x, resultRgb.y, resultRgb.z, resultAlpha);
        }

        /// <summary>按固定名称收集 Raven 武器常驻光效的三条线，供 Reset 自动绑定。</summary>
        /// <returns>按 CoreLine、LeftEdge、RightEdge 顺序返回找到的 LineRenderer；缺失位置为 null。</returns>
        private LineRenderer[] FindNamedIdleGlowLines()
        {
            string[] names =
            {
                "FX_BossWeaponIdleGlow_CoreLine",
                "FX_BossWeaponIdleGlow_LeftEdge",
                "FX_BossWeaponIdleGlow_RightEdge"
            };
            LineRenderer[] result = new LineRenderer[names.Length];
            LineRenderer[] lines = GetComponentsInChildren<LineRenderer>(true);
            for (int lineIndex = 0; lineIndex < lines.Length; lineIndex++)
            {
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (lines[lineIndex].name == names[nameIndex])
                    {
                        result[nameIndex] = lines[lineIndex];
                    }
                }
            }

            return result;
        }

        /// <summary>按固定名称收集 Raven 武器常驻光效的两盏灯，供 Reset 自动绑定。</summary>
        /// <returns>按 TipLight、BaseLight 顺序返回找到的 Light；缺失位置为 null。</returns>
        private Light[] FindNamedIdleGlowLights()
        {
            string[] names =
            {
                "FX_BossWeaponIdleGlow_TipLight",
                "FX_BossWeaponIdleGlow_BaseLight"
            };
            Light[] result = new Light[names.Length];
            Light[] lights = GetComponentsInChildren<Light>(true);
            for (int lightIndex = 0; lightIndex < lights.Length; lightIndex++)
            {
                for (int nameIndex = 0; nameIndex < names.Length; nameIndex++)
                {
                    if (lights[lightIndex].name == names[nameIndex])
                    {
                        result[nameIndex] = lights[lightIndex];
                    }
                }
            }

            return result;
        }

        /// <summary>比较 UnityEngine.Object 数组是否以相同顺序引用相同对象。</summary>
        /// <typeparam name="T">LineRenderer 或 Light 等 UnityEngine.Object 类型。</typeparam>
        /// <param name="current">当前 Inspector 数组。</param>
        /// <param name="cached">上次缓存的数组副本。</param>
        /// <returns>true 表示长度和每项引用都相同；false 表示绑定已经改变。</returns>
        private static bool ReferencesMatch<T>(T[] current, T[] cached) where T : Object
        {
            if (current == null || cached == null || current.Length != cached.Length)
            {
                return false;
            }

            for (int i = 0; i < current.Length; i++)
            {
                if (current[i] != cached[i])
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>检查 UnityEngine.Object 数组是否包含失效引用。</summary>
        /// <typeparam name="T">LineRenderer 或 Light 等 UnityEngine.Object 类型。</typeparam>
        /// <param name="values">需要检查的 Inspector 数组。</param>
        /// <returns>true 表示至少一项为空；false 表示全部引用有效。</returns>
        private static bool ContainsNullReference<T>(T[] values) where T : Object
        {
            for (int i = 0; i < values.Length; i++)
            {
                if (values[i] == null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>结束当前配置校验，并按调用方要求只记录一次明确错误。</summary>
        /// <param name="reportErrors">true 表示应将错误写入 Unity Console。</param>
        /// <param name="message">需要显示的具体缺失配置或材质属性。</param>
        /// <returns>始终返回 false，便于校验分支直接返回。</returns>
        private bool FailValidation(bool reportErrors, string message)
        {
            if (reportErrors)
            {
                LogConfigurationError(message);
            }

            return false;
        }

        /// <summary>每次失效配置只输出一次明确错误，避免编辑器预览持续刷屏。</summary>
        /// <param name="message">需要显示的具体缺失配置或材质属性。</param>
        private void LogConfigurationError(string message)
        {
            if (configurationErrorLogged)
            {
                return;
            }

            Debug.LogError(message, this);
            configurationErrorLogged = true;
        }
    }
}
