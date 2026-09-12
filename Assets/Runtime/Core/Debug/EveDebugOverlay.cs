// 文件说明：维护玩家运行时 Debug Overlay。
// 所属模块：调试显示。
// 运行影响：影响开发期玩家状态面板和测试可视化。

using ProjectEVE.Player;
using UnityEngine;

namespace ProjectEVE.Core.Debugging
{
    /// <summary>
    /// 临时调试面板，用于 Play Mode 中观察玩家状态。
    /// </summary>
    public sealed class EveDebugOverlay : MonoBehaviour
    {
        /// <summary>要观察的玩家状态机。</summary>
        [SerializeField] private PlayerStateMachine playerStateMachine;
        /// <summary>是否显示调试面板。</summary>
        [SerializeField] private bool visible = true;

        private Vector2 playerScrollPosition;
        private const float KeyboardScrollStep = 180f;
        private const float KeyboardScrollLargeStep = 520f;

        /// <summary>调试 UI 当前是否可见。</summary>
        public bool IsVisible => visible;

        /// <summary>由 Demo UI 控制开发期调试面板显隐。</summary>
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
            // OnGUI 仅用于开发期可视化，正式 UI 不依赖该调试面板。
            if (!visible)
            {
                return;
            }

            DrawPlayerDebugWindow();
        }

        /// <summary>
        /// 绘制 Player / Debug / Window 调试或编辑器界面，不改变核心战斗运行时语义。
        /// </summary>
        private void DrawPlayerDebugWindow()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = FindFirstObjectByType<PlayerStateMachine>();
            }

            if (playerStateMachine == null)
            {
                return;
            }

            PlayerStateContext context = playerStateMachine.Context;
            float panelHeight = Mathf.Min(720f, Screen.height - 24f);
            Rect panelRect = new Rect(12f, 12f, 560f, panelHeight);
            HandleScrollInput(Event.current, panelRect, ref playerScrollPosition);

            GUILayout.BeginArea(panelRect, GUI.skin.box);
            playerScrollPosition = GUILayout.BeginScrollView(playerScrollPosition);

            GUILayout.Label("State");
            GUILayout.Label("DebugScroll: PageUp / PageDown / Home / End");
            GUILayout.Label($"State: {context.CurrentState}");
            GUILayout.Label($"Phase: {context.CurrentPhase}");
            GUILayout.Label($"Elapsed: {context.StateElapsedTime:0.00}s");
            GUILayout.Label($"Mode: {context.ControlMode}");
            GUILayout.Label($"Move: {context.Input.Move} / {context.Input.MoveMagnitude:0.00}");
            GUILayout.Label($"AnimMove: {FormatVector(context.AnimationMove)} / {context.AnimationMoveMagnitude:0.00}");
            GUILayout.Label($"ActualVelocity: {FormatVector(context.ActualHorizontalVelocity)}");
            GUILayout.Label($"RootMotion: Active={context.IsRootMotionActiveThisFrame} Raw={FormatVector(context.LastRootMotionDelta)} Applied={FormatVector(context.LastAppliedRootMotionDelta)} Flags={context.LastRootMotionCollisionFlags}");
            GUILayout.Label($"SprintIntent: {context.SprintIntent}");
            GUILayout.Label($"Speed: {context.MoveSpeed:0.00}");
            GUILayout.Label($"LockTarget: {(context.LockOnTarget == null ? "None" : context.LockOnTarget.name)}");

            GUILayout.Space(6f);
            GUILayout.Label("Resources");
            GUILayout.Label($"HP={context.Resources.CurrentHp:0}/{context.Resources.MaxHp:0}");
            GUILayout.Label($"Beta={context.Resources.BetaEnergy:0}/{context.Resources.MaxBetaEnergy:0}");
            GUILayout.Label($"NormalHitStreak={context.ConsecutiveNormalAttackHitCount}/2");

            GUILayout.Space(6f);
            GUILayout.Label("Combat Result");
            GUILayout.Label($"Attack: {context.CurrentAttackNodeId} / {context.CurrentAttackInputType} / Combo {context.CurrentComboIndex}");
            GUILayout.Label($"AttackData: Id={context.CurrentAttackInstanceId} Type={context.CurrentAttackCombatType} Damage={context.CurrentAttackDamage:0.##} Poise={context.CurrentAttackPoiseDamage:0.##}");
            GUILayout.Label($"AttackTurn: Desired={context.LastAttackDesiredTurnAngle:0.0} Applied={context.LastAttackAppliedTurnAngle:0.0} Clamped={context.WasAttackDirectionClamped}");
            GUILayout.Label($"HitboxActive: {context.IsAttackHitboxActive} / Result: {context.AttackContactResult}");
            GUILayout.Label($"LastCombatOutcome: {context.LastCombatHitOutcome}");
            GUILayout.Label($"DefenderOutcome: {context.LastDefenderHitOutcome}");
            GUILayout.Label($"Reaction: Direction={context.LastReceivedHitDirectionId} Guard={context.CurrentGuardReaction} Knockdown={context.CurrentKnockdownType} Dead={context.CurrentDeadType}");
            GUILayout.Label($"Skill: Cast={context.CurrentSkillCastId} Node={FormatText(context.CurrentSkillHitNodeId)} HitIndex={context.CurrentSkillHitIndex} Intent={context.CurrentSkillReactionIntent}");
            GUILayout.Label($"SkillData: Damage={context.CurrentSkillDamage:0.##} Poise={context.CurrentSkillPoiseDamage:0.##} Result={context.SkillContactResult}");


            GUILayout.Space(6f);
            GUILayout.Label("Windows");
            GUILayout.Label($"Evade: Mode={context.CurrentEvadeMode} Invincible={context.IsEvadeInvincible} PerfectWindow={context.IsPerfectEvadeWindow}");
            GUILayout.Label($"PerfectEvade: Active={context.IsPerfectEvadeActive} Direction={context.PerfectEvadeDirection}");
            GUILayout.Label($"PerfectTiming: Activated={context.PerfectEvadeActivatedElapsed:0.00}s ModeElapsed={context.PerfectEvadeModeElapsed:0.00}s");
            GUILayout.Label($"Guard: Block={context.IsGuardBlockActive} Perfect={context.IsPerfectGuardWindow} Reentry={context.IsGuardReentry}");
            GUILayout.Label($"PerfectGuard: PGWindow={context.IsPerfectGuardWindow} PGChain={context.IsPerfectGuardChainWindow} ChainElapsed={context.PerfectGuardChainElapsed:0.00}s ReactionVersion={context.GuardReactionRequestVersion}");
            GUILayout.Label($"GuardWalk: Walking={context.IsGuardWalking} Dir={context.GuardMoveDirection} Last={context.LastGuardMoveDirection}");
            GUILayout.Label($"Skill: SuperArmor={context.IsSkillSuperArmor} Hitbox={context.IsSkillHitboxActive}");
            GUILayout.Label($"AttackWindows: Commit={context.IsCommitCancelWindow} Combo={context.IsComboWindow} Reset={context.IsResetWindow} MoveCancel={context.IsMoveCancelWindow}");
            GUILayout.Label($"Cancel: Evade={context.IsEvadeCancelWindow} Skill={context.IsSkillCancelWindow} Guard={context.IsGuardCancelWindow}");

            GUILayout.Space(6f);
            GUILayout.Label("Buffers");
            GUILayout.Label($"Attack={context.HasBufferedAttackInput}:{context.BufferedAttackInputType}");
            GUILayout.Label($"Evade={context.HasBufferedEvadeInput}");
            GUILayout.Label($"Skill={context.HasBufferedSkillInput}");

            GUILayout.EndScrollView();
            GUILayout.EndArea();
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
        }

        /// <summary>
        /// 执行 Format / Text 相关逻辑，并维护 调试显示 模块的运行时一致性。
        /// </summary>
        private static string FormatText(string value)
        {
            return string.IsNullOrEmpty(value) ? "-" : value;
        }

        /// <summary>
        /// 执行 Format / Vector 相关逻辑，并维护 调试显示 模块的运行时一致性。
        /// </summary>
        private static string FormatVector(Vector3 value)
        {
            return $"({value.x:0.00},{value.y:0.00},{value.z:0.00})";
        }

        /// <summary>
        /// 执行 Format / Vector 相关逻辑，并维护 调试显示 模块的运行时一致性。
        /// </summary>
        private static string FormatVector(Vector2 value)
        {
            return $"({value.x:0.00},{value.y:0.00})";
        }

        /// <summary>
        /// 处理 Scroll / Input 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
        private void HandleScrollInput(Event currentEvent, Rect panelRect, ref Vector2 scrollPosition)
        {
            if (currentEvent == null)
            {
                return;
            }

            if (currentEvent.type == EventType.ScrollWheel && panelRect.Contains(currentEvent.mousePosition))
            {
                scrollPosition.y += currentEvent.delta.y * 32f;
                currentEvent.Use();
                return;
            }

            if (currentEvent.type != EventType.KeyDown)
            {
                return;
            }

            switch (currentEvent.keyCode)
            {
                case KeyCode.PageDown:
                    scrollPosition.y += KeyboardScrollLargeStep;
                    currentEvent.Use();
                    break;
                case KeyCode.PageUp:
                    scrollPosition.y = Mathf.Max(0f, scrollPosition.y - KeyboardScrollLargeStep);
                    currentEvent.Use();
                    break;
                case KeyCode.End:
                    scrollPosition.y += KeyboardScrollStep;
                    currentEvent.Use();
                    break;
                case KeyCode.Home:
                    scrollPosition.y = 0f;
                    currentEvent.Use();
                    break;
            }
        }
    }
}
