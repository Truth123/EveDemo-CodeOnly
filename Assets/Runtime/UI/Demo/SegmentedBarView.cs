// 文件说明：维护 Demo HUD、训练提示、暂停菜单和 UI 资源显示。
// 所属模块：战斗 UI。
// 运行影响：影响演示界面、资源条显示和调试交互。

using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ProjectEVE.UI.Demo
{
    /// <summary>
    /// Stellar Blade 风格分段资源条。只负责显示 current / max，不保存战斗数据。
    /// </summary>
    public sealed class SegmentedBarView : MonoBehaviour
    {
        [SerializeField] private int segmentCount = 32;
        [SerializeField] private int groupSize = 8;
        [SerializeField] private Vector2 segmentSize = new Vector2(8f, 8f);
        [SerializeField] private float spacing = 2f;
        [SerializeField] private float groupSpacing = 8f;
        [SerializeField] private Color filledColor = Color.white;
        [SerializeField] private Color emptyColor = new Color(0.2f, 0.24f, 0.26f, 0.75f);
        [SerializeField] private RectTransform segmentRoot;
        [SerializeField] private Text valueText;

        private readonly List<Image> segments = new List<Image>();

        /// <summary>
        /// 执行 Configure 相关逻辑，并维护 战斗 UI 模块的运行时一致性。
        /// </summary>
        public void Configure(
            int count,
            int group,
            Vector2 size,
            Color filled,
            Color empty,
            Text valueLabel)
        {
            segmentCount = Mathf.Max(1, count);
            groupSize = Mathf.Max(1, group);
            segmentSize = size;
            filledColor = filled;
            emptyColor = empty;
            valueText = valueLabel;
            Rebuild();
        }

        /// <summary>
        /// 设置 Value 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        public void SetValue(float current, float max, bool hideWhenInvalid = true)
        {
            bool valid = max > 0f;
            gameObject.SetActive(valid || !hideWhenInvalid);

            float ratio = valid ? Mathf.Clamp01(current / max) : 0f;
            int filledSegments = Mathf.RoundToInt(ratio * segmentCount);
            for (int i = 0; i < segments.Count; i++)
            {
                segments[i].color = i < filledSegments ? filledColor : emptyColor;
            }

            if (valueText != null)
            {
                valueText.text = valid ? $"{current:0} / {max:0}" : "- / -";
            }
        }

        /// <summary>
        /// 执行 Rebuild 相关逻辑，并维护 战斗 UI 模块的运行时一致性。
        /// </summary>
        private void Rebuild()
        {
            if (segmentRoot == null)
            {
                segmentRoot = (RectTransform)transform;
            }

            ClearSegments();
            float x = 0f;
            for (int i = 0; i < segmentCount; i++)
            {
                GameObject segmentObject = new GameObject($"Segment_{i:00}", typeof(RectTransform), typeof(Image));
                segmentObject.transform.SetParent(segmentRoot, false);

                RectTransform rectTransform = (RectTransform)segmentObject.transform;
                rectTransform.anchorMin = new Vector2(0f, 0.5f);
                rectTransform.anchorMax = new Vector2(0f, 0.5f);
                rectTransform.pivot = new Vector2(0f, 0.5f);
                rectTransform.sizeDelta = segmentSize;
                rectTransform.anchoredPosition = new Vector2(x, 0f);

                Image image = segmentObject.GetComponent<Image>();
                image.color = emptyColor;
                segments.Add(image);

                bool groupBreak = groupSize > 0 && (i + 1) % groupSize == 0;
                x += segmentSize.x + (groupBreak ? groupSpacing : spacing);
            }

            if (segmentCount > 0)
            {
                x -= groupSize > 0 && segmentCount % groupSize == 0 ? groupSpacing : spacing;
            }

            segmentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, x));
            segmentRoot.SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, segmentSize.y);
        }

        /// <summary>
        /// 清理 Segments 相关运行时状态，防止旧动作、旧窗口或旧命中结果泄漏到后续流程。
        /// </summary>
        private void ClearSegments()
        {
            segments.Clear();
            for (int i = segmentRoot.childCount - 1; i >= 0; i--)
            {
                Transform child = segmentRoot.GetChild(i);
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }
        }
    }
}
