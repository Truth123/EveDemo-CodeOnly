// 文件说明：维护玩家 Animator Controller 的编辑器配置工具。
// 所属模块：Animator 编辑器工具。
// 运行影响：仅在 Unity Editor 中影响 Animator 配置生成或修正。

using ProjectEVE.Player;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace ProjectEVE.EditorTools
{
    /// <summary>
    /// Eve Animator 的 PerfectEvade 接入工具。用于把 Evade 内部完美闪避动画模式同步到控制器结构。
    /// </summary>
    public static class EvePerfectEvadeControllerSetup
    {
        private const string ControllerPath = "Assets/Animator/Eve.controller";
        private const string ReturnStateName = "Anim_ReturnToLocomotion";
        private const string LockOnEvadeStateName = "BT_Evade_LockOn_8Dir";
        private const string FreeForwardEvadeStateName = "Anim_Evade_Free_Forward";
        private const string FreeBackwardEvadeStateName = "Anim_Evade_Free_Backward";
        private const string IsPerfectEvadeActiveParameter = "IsPerfectEvadeActive";
        private const string PerfectEvadeDirectionParameter = "PerfectEvadeDirection";
        private const string PlayerStateParameter = "PlayerState";

        /// <summary>
        /// Editor 编译完成后自动补齐缺失结构，保证控制器资产不会依赖手动菜单执行。
        /// </summary>
        [InitializeOnLoadMethod]
        private static void ApplyOnLoadIfNeeded()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null || !NeedsSetup(controller))
            {
                return;
            }

            ApplyPerfectEvadeSetup();
        }

        /// <summary>
        /// 菜单入口，方便手动重新整理 PerfectEvade 动画入口。
        /// </summary>
        [MenuItem("Project EVE/Animator/Apply Perfect Evade Setup")]
        public static void ApplyPerfectEvadeSetup()
        {
            AnimatorController controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
            if (controller == null)
            {
                Debug.LogError($"Eve controller not found: {ControllerPath}");
                return;
            }

            AddParameterIfMissing(controller, IsPerfectEvadeActiveParameter, AnimatorControllerParameterType.Bool);
            AddParameterIfMissing(controller, PerfectEvadeDirectionParameter, AnimatorControllerParameterType.Int);

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            AnimatorState returnState = FindState(stateMachine, ReturnStateName);
            ConfigureOrdinaryEvadeEntries(stateMachine);
            ConfigurePerfectEvadeState(stateMachine, returnState, "Anim_PerfectEvade_F");
            ConfigurePerfectEvadeState(stateMachine, returnState, "Anim_PerfectEvade_B");
            ConfigurePerfectEvadeState(stateMachine, returnState, "Anim_PerfectEvade_L");
            ConfigurePerfectEvadeState(stateMachine, returnState, "Anim_PerfectEvade_R");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();
            Debug.Log("Eve PerfectEvade Animator setup applied.");
        }

        /// <summary>
        /// 执行 Needs / Setup 相关逻辑，并维护 Animator 编辑器工具 模块的运行时一致性。
        /// </summary>
        private static bool NeedsSetup(AnimatorController controller)
        {
            if (!HasParameter(controller, IsPerfectEvadeActiveParameter) ||
                !HasParameter(controller, PerfectEvadeDirectionParameter))
            {
                return true;
            }

            AnimatorStateMachine stateMachine = controller.layers[0].stateMachine;
            if (OrdinaryEvadeEntriesNeedSetup(stateMachine))
            {
                return true;
            }

            return PerfectEvadeStateNeedsSetup(stateMachine, "Anim_PerfectEvade_F") ||
                PerfectEvadeStateNeedsSetup(stateMachine, "Anim_PerfectEvade_B") ||
                PerfectEvadeStateNeedsSetup(stateMachine, "Anim_PerfectEvade_L") ||
                PerfectEvadeStateNeedsSetup(stateMachine, "Anim_PerfectEvade_R");
        }

        /// <summary>
        /// 添加 Parameter / If / Missing 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddParameterIfMissing(
            AnimatorController controller,
            string parameterName,
            AnimatorControllerParameterType parameterType)
        {
            if (!HasParameter(controller, parameterName))
            {
                controller.AddParameter(parameterName, parameterType);
            }
        }

        /// <summary>
        /// 判断当前对象是否满足 Parameter 条件，供状态机、调试或校验逻辑读取。
        /// </summary>
        private static bool HasParameter(AnimatorController controller, string parameterName)
        {
            foreach (AnimatorControllerParameter parameter in controller.parameters)
            {
                if (parameter.name == parameterName)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行 Configure / Ordinary / Evade / Entries 相关逻辑，并维护 Animator 编辑器工具 模块的运行时一致性。
        /// </summary>
        private static void ConfigureOrdinaryEvadeEntries(AnimatorStateMachine stateMachine)
        {
            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                if (transition.destinationState == null)
                {
                    continue;
                }

                string stateName = transition.destinationState.name;
                if (stateName != LockOnEvadeStateName &&
                    stateName != FreeForwardEvadeStateName &&
                    stateName != FreeBackwardEvadeStateName)
                {
                    continue;
                }

                AddConditionIfMissing(
                    transition,
                    AnimatorConditionMode.IfNot,
                    0f,
                    IsPerfectEvadeActiveParameter);
            }
        }

        /// <summary>
        /// 执行 Ordinary / Evade / Entries / Need / Setup 相关逻辑，并维护 Animator 编辑器工具 模块的运行时一致性。
        /// </summary>
        private static bool OrdinaryEvadeEntriesNeedSetup(AnimatorStateMachine stateMachine)
        {
            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                if (transition.destinationState == null)
                {
                    continue;
                }

                string stateName = transition.destinationState.name;
                if ((stateName == LockOnEvadeStateName ||
                    stateName == FreeForwardEvadeStateName ||
                    stateName == FreeBackwardEvadeStateName) &&
                    !HasCondition(transition, IsPerfectEvadeActiveParameter))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 执行 Configure / Perfect / Evade / State 相关逻辑，并维护 Animator 编辑器工具 模块的运行时一致性。
        /// </summary>
        private static void ConfigurePerfectEvadeState(
            AnimatorStateMachine stateMachine,
            AnimatorState returnState,
            string stateName)
        {
            AnimatorState targetState = FindState(stateMachine, stateName);
            if (targetState == null)
            {
                Debug.LogWarning($"PerfectEvade state not found: {stateName}");
                return;
            }

            RemovePerfectEvadeAnyStateTransitions(stateMachine, targetState);

            if (returnState != null && !HasTransitionTo(targetState, returnState))
            {
                AnimatorStateTransition exit = targetState.AddTransition(returnState);
                exit.hasExitTime = false;
                exit.duration = 0.05f;
                exit.AddCondition(AnimatorConditionMode.Less, (int)PlayerStateId.Evade, PlayerStateParameter);
            }
        }

        /// <summary>
        /// 执行 Perfect / Evade / State / Needs / Setup 相关逻辑，并维护 Animator 编辑器工具 模块的运行时一致性。
        /// </summary>
        private static bool PerfectEvadeStateNeedsSetup(AnimatorStateMachine stateMachine, string stateName)
        {
            AnimatorState targetState = FindState(stateMachine, stateName);
            if (targetState == null)
            {
                return false;
            }

            return HasPerfectEvadeAnyStateTransition(stateMachine, targetState);
        }

        /// <summary>
        /// 查找 State 对象或数据，作为后续绑定、校验或显示的输入。
        /// </summary>
        private static AnimatorState FindState(AnimatorStateMachine stateMachine, string stateName)
        {
            foreach (ChildAnimatorState childState in stateMachine.states)
            {
                if (childState.state.name == stateName)
                {
                    return childState.state;
                }
            }

            foreach (ChildAnimatorStateMachine childStateMachine in stateMachine.stateMachines)
            {
                AnimatorState state = FindState(childStateMachine.stateMachine, stateName);
                if (state != null)
                {
                    return state;
                }
            }

            return null;
        }

        /// <summary>
        /// 判断当前对象是否满足 Perfect / Evade / Any / State / Transition 条件，供状态机、调试或校验逻辑读取。
        /// </summary>
        private static bool HasPerfectEvadeAnyStateTransition(
            AnimatorStateMachine stateMachine,
            AnimatorState targetState)
        {
            foreach (AnimatorStateTransition transition in stateMachine.anyStateTransitions)
            {
                if (transition.destinationState == targetState &&
                    (HasCondition(transition, IsPerfectEvadeActiveParameter) ||
                    HasCondition(transition, PerfectEvadeDirectionParameter)))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 移除 Perfect / Evade / Any / State / Transitions 数据，并清理相关缓存或状态引用。
        /// </summary>
        private static void RemovePerfectEvadeAnyStateTransitions(
            AnimatorStateMachine stateMachine,
            AnimatorState targetState)
        {
            AnimatorStateTransition[] transitions = stateMachine.anyStateTransitions;
            foreach (AnimatorStateTransition transition in transitions)
            {
                if (transition.destinationState == targetState &&
                    (HasCondition(transition, IsPerfectEvadeActiveParameter) ||
                    HasCondition(transition, PerfectEvadeDirectionParameter)))
                {
                    stateMachine.RemoveAnyStateTransition(transition);
                }
            }
        }

        /// <summary>
        /// 判断当前对象是否满足 Transition / To 条件，供状态机、调试或校验逻辑读取。
        /// </summary>
        private static bool HasTransitionTo(AnimatorState fromState, AnimatorState toState)
        {
            foreach (AnimatorStateTransition transition in fromState.transitions)
            {
                if (transition.destinationState == toState)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 添加 Condition / If / Missing 数据，并维护集合、缓存或运行时状态的一致性。
        /// </summary>
        private static void AddConditionIfMissing(
            AnimatorStateTransition transition,
            AnimatorConditionMode mode,
            float threshold,
            string parameterName)
        {
            if (HasCondition(transition, parameterName))
            {
                return;
            }

            transition.AddCondition(mode, threshold, parameterName);
        }

        /// <summary>
        /// 判断当前对象是否满足 Condition 条件，供状态机、调试或校验逻辑读取。
        /// </summary>
        private static bool HasCondition(AnimatorStateTransition transition, string parameterName)
        {
            foreach (AnimatorCondition condition in transition.conditions)
            {
                if (condition.parameter == parameterName)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
