// 文件说明：为 Boss HitNode 一次性粒子与武器换手绑定提供 Timeline 资产和 HitNode 下拉配置界面。
// 所属模块：Boss 战斗表现编辑器。
// 运行影响：仅改善 Inspector 配置与失效引用提示，不参与 Play Mode 战斗逻辑。

using ProjectEVE.Boss.Combat;
using ProjectEVE.Boss.AI;
using ProjectEVE.Combat.Timeline;
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;

namespace ProjectEVE.Editor.Boss
{
    /// <summary>
    /// 在 Boss 根对象上编辑一次性世界空间粒子的 Timeline、HitNode、Prefab、挂点和初始姿态。
    /// </summary>
    [CustomEditor(typeof(BossHitNodeParticleVfxController))]
    public sealed class BossHitNodeParticleVfxControllerEditor : UnityEditor.Editor
    {
        private const float RowCount = 11f;
        private ReorderableList bindingList;

        /// <summary>创建可排序绑定列表并注册逐行绘制与安全默认值。</summary>
        private void OnEnable()
        {
            SerializedProperty bindings = serializedObject.FindProperty("particleBindings");
            bindingList = new ReorderableList(serializedObject, bindings, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "一次性世界空间粒子绑定"),
                elementHeightCallback = _ => BossHitNodeVfxInspectorUtility.GetElementHeight(RowCount),
                drawElementCallback = DrawBindingElement,
                onAddCallback = AddBinding
            };
        }

        /// <summary>绘制人工绑定列表，并将所有修改写回场景对象。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "粒子按 HitNode 起点减 Lead Time 捕获挂点姿态并生成无父级实例，之后不会跟随武器、手或腿。同一 HitNode 可配置多条。",
                MessageType.Info);
            bindingList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 绘制一条一次性粒子绑定，并在底部显示缺失引用或失效 HitNode。
        /// </summary>
        /// <param name="rect">当前列表元素可使用的完整绘制区域。</param>
        /// <param name="index">当前绑定在序列化数组中的下标。</param>
        /// <param name="isActive">当前元素是否为列表活动项。</param>
        /// <param name="isFocused">当前元素是否拥有键盘焦点。</param>
        private void DrawBindingElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = bindingList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty timeline = element.FindPropertyRelative("attackTimeline");
            SerializedProperty hitNodeId = element.FindPropertyRelative("hitNodeId");
            SerializedProperty effectPrefab = element.FindPropertyRelative("effectPrefab");
            SerializedProperty spawnAnchor = element.FindPropertyRelative("spawnAnchor");
            SerializedProperty lifetime = element.FindPropertyRelative("lifetimeSeconds");

            BossHitNodeVfxInspectorUtility.DrawTimelineField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 0),
                timeline,
                hitNodeId);
            BossHitNodeVfxInspectorUtility.DrawHitNodePopup(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 1),
                timeline,
                hitNodeId);
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 2),
                effectPrefab,
                new GUIContent("Effect Prefab"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 3),
                spawnAnchor,
                new GUIContent("Spawn Anchor"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 4),
                element.FindPropertyRelative("directionMode"),
                new GUIContent("Direction"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 5),
                element.FindPropertyRelative("localPositionOffset"),
                new GUIContent("Local Position Offset"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 6),
                element.FindPropertyRelative("localEulerOffset"),
                new GUIContent("Euler Offset"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 7),
                element.FindPropertyRelative("scaleMultiplier"),
                new GUIContent("Scale Multiplier"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 8),
                lifetime,
                new GUIContent("Lifetime Seconds"));
            EditorGUI.PropertyField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 9),
                element.FindPropertyRelative("leadTimeSeconds"),
                new GUIContent("Lead Time Seconds"));

            string error = BossHitNodeVfxInspectorUtility.GetCommonBindingError(timeline, hitNodeId);
            if (string.IsNullOrEmpty(error) && effectPrefab.objectReferenceValue == null)
            {
                error = "未绑定粒子 Prefab。";
            }
            else if (string.IsNullOrEmpty(error) && spawnAnchor.objectReferenceValue == null)
            {
                error = "未绑定生成挂点。";
            }
            else if (string.IsNullOrEmpty(error) && lifetime.floatValue <= 0f)
            {
                error = "生命周期必须大于 0 秒。";
            }

            BossHitNodeVfxInspectorUtility.DrawError(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 10),
                error);
        }

        /// <summary>
        /// 向粒子数组追加一条空绑定，并写入缩放与生命周期的安全默认值。
        /// </summary>
        /// <param name="list">当前 Inspector 使用的可排序绑定列表。</param>
        private static void AddBinding(ReorderableList list)
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("attackTimeline").objectReferenceValue = null;
            element.FindPropertyRelative("hitNodeId").stringValue = string.Empty;
            element.FindPropertyRelative("effectPrefab").objectReferenceValue = null;
            element.FindPropertyRelative("spawnAnchor").objectReferenceValue = null;
            element.FindPropertyRelative("directionMode").enumValueIndex = 0;
            element.FindPropertyRelative("localPositionOffset").vector3Value = Vector3.zero;
            element.FindPropertyRelative("localEulerOffset").vector3Value = Vector3.zero;
            element.FindPropertyRelative("scaleMultiplier").vector3Value = Vector3.one;
            element.FindPropertyRelative("lifetimeSeconds").floatValue = 2.2f;
            element.FindPropertyRelative("leadTimeSeconds").floatValue = 0f;
            list.index = index;
        }
    }

    /// <summary>
    /// 在 Boss 根对象上编辑唯一武器、左右手 Socket 与左手 HitNode 人工绑定。
    /// </summary>
    [CustomEditor(typeof(BossWeaponHandController))]
    public sealed class BossWeaponHandControllerEditor : UnityEditor.Editor
    {
        private const float RowCount = 3f;
        private ReorderableList bindingList;

        /// <summary>创建只允许选择 Weapon HitNode 的左手绑定列表。</summary>
        private void OnEnable()
        {
            SerializedProperty bindings = serializedObject.FindProperty("leftHandHitNodes");
            bindingList = new ReorderableList(serializedObject, bindings, true, true, true, true)
            {
                drawHeaderCallback = rect => EditorGUI.LabelField(rect, "左手 Weapon HitNode"),
                elementHeightCallback = _ => BossHitNodeVfxInspectorUtility.GetElementHeight(RowCount),
                drawElementCallback = DrawBindingElement,
                onAddCallback = AddBinding
            };
        }

        /// <summary>绘制唯一武器、左右 Socket 和左手 HitNode 下拉绑定。</summary>
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.HelpBox(
                "换手保持 Weapon Root 的本地 Position / Rotation / Scale；左手节点必须使用 SourcePart=Weapon。",
                MessageType.Info);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("weaponRoot"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("rightHandSocket"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("leftHandSocket"));
            bindingList.DoLayoutList();
            serializedObject.ApplyModifiedProperties();
        }

        /// <summary>
        /// 绘制一条 Timeline 与 HitNode 绑定，并显示非 Weapon SourcePart 错误。
        /// </summary>
        /// <param name="rect">当前列表元素的绘制区域。</param>
        /// <param name="index">绑定在数组中的下标。</param>
        /// <param name="isActive">当前元素是否为活动项。</param>
        /// <param name="isFocused">当前元素是否拥有键盘焦点。</param>
        private void DrawBindingElement(Rect rect, int index, bool isActive, bool isFocused)
        {
            SerializedProperty element = bindingList.serializedProperty.GetArrayElementAtIndex(index);
            SerializedProperty timeline = element.FindPropertyRelative("attackTimeline");
            SerializedProperty hitNodeId = element.FindPropertyRelative("hitNodeId");
            BossHitNodeVfxInspectorUtility.DrawTimelineField(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 0),
                timeline,
                hitNodeId);
            BossHitNodeVfxInspectorUtility.DrawHitNodePopup(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 1),
                timeline,
                hitNodeId);

            string error = BossHitNodeVfxInspectorUtility.GetCommonBindingError(timeline, hitNodeId);
            if (string.IsNullOrEmpty(error))
            {
                error = BossHitNodeVfxInspectorUtility.GetSourcePartError(
                    timeline.objectReferenceValue as BossAttackTimelineAsset,
                    hitNodeId.stringValue,
                    BossAttackSourcePart.Weapon);
            }

            BossHitNodeVfxInspectorUtility.DrawError(
                BossHitNodeVfxInspectorUtility.GetLineRect(rect, 2),
                error);
        }

        /// <summary>
        /// 追加一条空左手绑定，要求开发者显式选择 Timeline 与 Weapon HitNode。
        /// </summary>
        /// <param name="list">当前 Inspector 使用的可排序绑定列表。</param>
        private static void AddBinding(ReorderableList list)
        {
            int index = list.serializedProperty.arraySize;
            list.serializedProperty.arraySize++;
            SerializedProperty element = list.serializedProperty.GetArrayElementAtIndex(index);
            element.FindPropertyRelative("attackTimeline").objectReferenceValue = null;
            element.FindPropertyRelative("hitNodeId").stringValue = string.Empty;
            list.index = index;
        }
    }

    /// <summary>
    /// Boss HitNode 粒子与武器换手 Inspector 共享的 Timeline、HitNode 下拉和错误显示逻辑。
    /// </summary>
    internal static class BossHitNodeVfxInspectorUtility
    {
        private const float VerticalPadding = 2f;

        /// <summary>
        /// 根据固定行数计算 ReorderableList 元素高度。
        /// </summary>
        /// <param name="rowCount">当前元素需要绘制的单行数量。</param>
        /// <returns>包含行间距与上下留白的像素高度。</returns>
        public static float GetElementHeight(float rowCount)
        {
            return rowCount * (EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing) +
                VerticalPadding * 2f;
        }

        /// <summary>
        /// 取得列表元素内指定行的绘制矩形。
        /// </summary>
        /// <param name="elementRect">当前元素的完整绘制区域。</param>
        /// <param name="rowIndex">从 0 开始的目标行下标。</param>
        /// <returns>扣除上下留白后的单行矩形。</returns>
        public static Rect GetLineRect(Rect elementRect, int rowIndex)
        {
            float step = EditorGUIUtility.singleLineHeight + EditorGUIUtility.standardVerticalSpacing;
            return new Rect(
                elementRect.x,
                elementRect.y + VerticalPadding + rowIndex * step,
                elementRect.width,
                EditorGUIUtility.singleLineHeight);
        }

        /// <summary>
        /// 绘制 BossAttack Timeline 资产字段；资产改变时清除旧 HitNode，避免跨资产残留同名字符串。
        /// </summary>
        /// <param name="rect">Timeline 字段使用的绘制区域。</param>
        /// <param name="timelineProperty">保存 BossAttackTimelineAsset 引用的序列化字段。</param>
        /// <param name="hitNodeProperty">需要在 Timeline 改变时清空的 HitNodeId 字段。</param>
        public static void DrawTimelineField(
            Rect rect,
            SerializedProperty timelineProperty,
            SerializedProperty hitNodeProperty)
        {
            EditorGUI.BeginChangeCheck();
            EditorGUI.PropertyField(rect, timelineProperty, new GUIContent("Attack Timeline"));
            if (EditorGUI.EndChangeCheck())
            {
                hitNodeProperty.stringValue = string.Empty;
            }
        }

        /// <summary>
        /// 从选中 Timeline 的 CombatHitNodeClip 顺序生成下拉框，并写回开发者选择的 HitNode ID。
        /// </summary>
        /// <param name="rect">HitNode 下拉框使用的绘制区域。</param>
        /// <param name="timelineProperty">保存 BossAttackTimelineAsset 引用的序列化字段。</param>
        /// <param name="hitNodeProperty">保存当前选择 HitNode ID 的序列化字段。</param>
        public static void DrawHitNodePopup(
            Rect rect,
            SerializedProperty timelineProperty,
            SerializedProperty hitNodeProperty)
        {
            BossAttackTimelineAsset timeline = timelineProperty.objectReferenceValue as BossAttackTimelineAsset;
            if (timeline == null)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.Popup(rect, "HitNode", 0, new[] { "请先选择 Timeline" });
                }

                return;
            }

            string[] hitNodeIds = GetHitNodeIds(timeline);
            if (hitNodeIds.Length == 0)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUI.Popup(rect, "HitNode", 0, new[] { "Timeline 中没有 HitNode" });
                }

                return;
            }

            string current = hitNodeProperty.stringValue;
            int currentIndex = Array.IndexOf(hitNodeIds, current);
            string firstLabel = string.IsNullOrEmpty(current) ? "请选择 HitNode" : $"失效：{current}";
            string[] displayOptions = new string[hitNodeIds.Length + 1];
            displayOptions[0] = firstLabel;
            Array.Copy(hitNodeIds, 0, displayOptions, 1, hitNodeIds.Length);

            int nextIndex = EditorGUI.Popup(rect, "HitNode", currentIndex >= 0 ? currentIndex + 1 : 0, displayOptions);
            if (nextIndex > 0)
            {
                hitNodeProperty.stringValue = hitNodeIds[nextIndex - 1];
            }
        }

        /// <summary>
        /// 返回 Timeline 中按作者顺序出现且去重后的 HitNode ID。
        /// </summary>
        /// <param name="timeline">需要读取 CombatHitNodeClip 的 BossAttack Timeline。</param>
        /// <returns>可直接用于 Inspector 下拉框的 HitNode ID 数组。</returns>
        public static string[] GetHitNodeIds(BossAttackTimelineAsset timeline)
        {
            if (timeline == null)
            {
                return Array.Empty<string>();
            }

            List<string> ids = new List<string>();
            HashSet<string> uniqueIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                if (clip == null || string.IsNullOrEmpty(clip.Name) || !uniqueIds.Add(clip.Name))
                {
                    continue;
                }

                ids.Add(clip.Name);
            }

            return ids.ToArray();
        }

        /// <summary>
        /// 检查两个绑定类型共有的 Timeline 与 HitNode 引用错误。
        /// </summary>
        /// <param name="timelineProperty">保存 BossAttackTimelineAsset 引用的序列化字段。</param>
        /// <param name="hitNodeProperty">保存当前选择 HitNode ID 的序列化字段。</param>
        /// <returns>空字符串表示公共字段有效；否则返回行内错误提示。</returns>
        public static string GetCommonBindingError(
            SerializedProperty timelineProperty,
            SerializedProperty hitNodeProperty)
        {
            BossAttackTimelineAsset timeline = timelineProperty.objectReferenceValue as BossAttackTimelineAsset;
            if (timeline == null)
            {
                return "未选择 BossAttack Timeline。";
            }

            if (string.IsNullOrEmpty(timeline.ActionId))
            {
                return "Timeline 的 ActionId 为空。";
            }

            string hitNodeId = hitNodeProperty.stringValue;
            if (string.IsNullOrEmpty(hitNodeId))
            {
                return "未选择 HitNode。";
            }

            if (Array.IndexOf(GetHitNodeIds(timeline), hitNodeId) < 0)
            {
                return $"Timeline 中不存在 HitNode '{hitNodeId}'。";
            }

            return string.Empty;
        }

        /// <summary>
        /// 检查指定 HitNode 的 SourcePart 是否符合绑定控制器要求。
        /// </summary>
        /// <param name="timeline">保存目标 HitNode 的 Boss Timeline。</param>
        /// <param name="hitNodeId">需要检查的 HitNode ID。</param>
        /// <param name="requiredSourcePart">该绑定允许的唯一来源部位。</param>
        /// <returns>SourcePart 正确时为空字符串，否则返回行内错误提示。</returns>
        public static string GetSourcePartError(
            BossAttackTimelineAsset timeline,
            string hitNodeId,
            BossAttackSourcePart requiredSourcePart)
        {
            if (timeline == null || string.IsNullOrEmpty(hitNodeId))
            {
                return string.Empty;
            }

            foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                if (clip != null && string.Equals(clip.Name, hitNodeId, StringComparison.Ordinal))
                {
                    return clip.SourcePart == requiredSourcePart
                        ? string.Empty
                        : $"HitNode 必须使用 SourcePart={requiredSourcePart}。";
                }
            }

            return string.Empty;
        }

        /// <summary>
        /// 使用红色迷你标签显示单条绑定错误；有效绑定保持该行为空。
        /// </summary>
        /// <param name="rect">错误信息使用的绘制区域。</param>
        /// <param name="error">空字符串或需要显示的错误原因。</param>
        public static void DrawError(Rect rect, string error)
        {
            if (string.IsNullOrEmpty(error))
            {
                return;
            }

            Color previousColor = GUI.color;
            GUI.color = new Color(1f, 0.45f, 0.45f, 1f);
            EditorGUI.LabelField(rect, error, EditorStyles.miniLabel);
            GUI.color = previousColor;
        }
    }
}
