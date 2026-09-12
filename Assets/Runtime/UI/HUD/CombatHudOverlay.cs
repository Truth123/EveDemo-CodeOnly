// 文件说明：维护 Demo HUD、训练提示、暂停菜单和 UI 资源显示。
// 所属模块：战斗 UI。
// 运行影响：影响演示界面、资源条显示和调试交互。

using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.UI.HUD
{
    /// <summary>
    /// 求职演示用轻量战斗 HUD。只显示资源，不承载调试信息。
    /// </summary>
    public sealed class CombatHudOverlay : MonoBehaviour
    {
        [SerializeField] private bool visible = true;
        [SerializeField] private PlayerStateMachine playerStateMachine;
        [SerializeField] private CombatResourceComponent bossResource;
        [SerializeField] private BossActor bossActor;

        private static readonly Color HpColor = new Color(0.76f, 0.12f, 0.12f, 0.92f);
        private static readonly Color BetaColor = new Color(0.94f, 0.68f, 0.20f, 0.92f);
        private static readonly Color BackColor = new Color(0f, 0f, 0f, 0.55f);

        /// <summary>旧 OnGUI HUD 当前是否显示。正式演示 HUD 启用时会关闭它。</summary>
        public bool IsVisible => visible;

        /// <summary>由 Demo UI 控制旧 OnGUI HUD 显隐，避免和正式 UGUI HUD 重复。</summary>
        public void SetVisible(bool value)
        {
            visible = value;
        }

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            BindReferences();
        }

        /// <summary>
        /// 绘制 IMGUI 调试或演示界面，并读取当前运行时快照。
        /// </summary>
        private void OnGUI()
        {
            if (!visible)
            {
                return;
            }

            BindReferences();
            DrawBossHud();
            DrawPlayerHud();
        }

        /// <summary>
        /// 绘制 Boss / Hud 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawBossHud()
        {
            if (bossResource == null)
            {
                return;
            }

            float width = Mathf.Min(560f, Screen.width - 48f);
            Rect rect = new Rect((Screen.width - width) * 0.5f, 18f, width, 20f);
            DrawBar(rect, "Raven", bossResource.CurrentHp, bossResource.MaxHp, HpColor);
        }

        /// <summary>
        /// 绘制 Player / Hud 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawPlayerHud()
        {
            if (playerStateMachine == null)
            {
                return;
            }

            CombatResourceSet resources = playerStateMachine.Context.Resources;
            float x = 24f;
            float y = Screen.height - 80f;
            float width = 280f;

            DrawBar(new Rect(x, y, width, 18f), "HP", resources.CurrentHp, resources.MaxHp, HpColor);
            DrawBar(new Rect(x, y + 26f, width, 14f), "Beta", resources.BetaEnergy, resources.MaxBetaEnergy, BetaColor);
        }

        /// <summary>
        /// 绘制 Bar 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private static void DrawBar(Rect rect, string label, float current, float max, Color fillColor)
        {
            float ratio = max > 0f ? Mathf.Clamp01(current / max) : 0f;
            Color previousColor = GUI.color;

            GUI.color = BackColor;
            GUI.DrawTexture(rect, Texture2D.whiteTexture);

            Rect fillRect = rect;
            fillRect.width *= ratio;
            GUI.color = fillColor;
            GUI.DrawTexture(fillRect, Texture2D.whiteTexture);

            GUI.color = Color.white;
            GUI.Label(rect, $"{label} {current:0}/{max:0}");
            GUI.color = previousColor;
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }

            if (bossActor == null)
            {
                bossActor = FindFirstObjectByType<BossActor>();
            }

            if (bossResource == null && bossActor != null)
            {
                bossResource = bossActor.GetComponent<CombatResourceComponent>();
            }
        }
    }
}
