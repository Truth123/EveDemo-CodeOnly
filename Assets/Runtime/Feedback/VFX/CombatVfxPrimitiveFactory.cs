// 文件说明：提供战斗反馈 VFX 的轻量运行时原语创建工具。
// 所属模块：战斗反馈。
// 运行影响：只创建和销毁表现对象，不影响命中、资源、状态机或输入窗口。

using UnityEngine;

namespace ProjectEVE.Feedback
{
    /// <summary>
    /// 运行时 VFX 原语工厂。当前只保留 PerfectEvade 方向线等通用反馈原语。
    /// </summary>
    public static class CombatVfxPrimitiveFactory
    {
        /// <summary>使用显式绑定的共享材质生成完美闪避方向线，并按持续时间销毁临时表现对象。</summary>
        /// <param name="effectName">生成对象的名称前缀。</param>
        /// <param name="material">所有方向线共享的静态材质资产；缺失时拒绝生成。</param>
        /// <param name="position">方向线围绕的世界空间中心。</param>
        /// <param name="direction">方向线在水平面上的延伸方向。</param>
        /// <param name="color">起点颜色；终点沿用 RGB 并把 Alpha 渐变到 0。</param>
        /// <param name="length">每条方向线的世界空间长度。</param>
        /// <param name="width">LineRenderer 的宽度倍率。</param>
        /// <param name="duration">方向线对象的存活秒数。</param>
        /// <param name="lineCount">围绕中心生成的平行线数量。</param>
        public static void SpawnDirectionLines(
            string effectName,
            Material material,
            Vector3 position,
            Vector3 direction,
            Color color,
            float length,
            float width,
            float duration,
            int lineCount)
        {
            if (material == null)
            {
                Debug.LogError($"{nameof(CombatVfxPrimitiveFactory)} requires a static material asset for direction lines.");
                return;
            }

            if (duration <= 0f || length <= 0f || width <= 0f || lineCount <= 0)
            {
                return;
            }

            Vector3 forward = ProjectOnPlane(direction, Vector3.forward);
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
            if (right.sqrMagnitude <= 0.0001f)
            {
                right = Vector3.right;
            }

            float spacing = 0.12f;
            for (int i = 0; i < lineCount; i++)
            {
                float offset = (i - (lineCount - 1) * 0.5f) * spacing;
                Vector3 start = position - forward * length * 0.25f + right * offset + Vector3.up * (0.85f + i * 0.03f);
                Vector3 end = position + forward * length + right * (offset * 0.45f) + Vector3.up * (1.05f + i * 0.02f);

                GameObject lineObject = new GameObject(effectName + "_" + i);
                LineRenderer line = lineObject.AddComponent<LineRenderer>();
                line.useWorldSpace = true;
                line.loop = false;
                line.positionCount = 2;
                line.widthMultiplier = width;
                line.numCapVertices = 4;
                line.alignment = LineAlignment.View;
                line.sharedMaterial = material;
                line.startColor = color;
                line.endColor = new Color(color.r, color.g, color.b, 0f);
                line.SetPosition(0, start);
                line.SetPosition(1, end);

                DestroyVfxObject(lineObject, duration);
            }
        }

        private static Vector3 ProjectOnPlane(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            return value.sqrMagnitude > 0.0001f ? value.normalized : fallback;
        }

        private static void DestroyVfxObject(GameObject target, float delay)
        {
            if (target == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Object.Destroy(target, delay);
            }
            else
            {
                Object.DestroyImmediate(target);
            }
        }
    }
}
