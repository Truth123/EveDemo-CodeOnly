// 文件说明：维护 Unity 新输入系统读取和玩家输入快照。
// 所属模块：输入系统。
// 运行影响：影响状态机输入来源、移动视角输入和动作按键采样。

using UnityEngine;
using UnityEngine.InputSystem;

namespace ProjectEVE.Input
{
    /// <summary>
    /// Unity 新输入系统桥接器。负责把 Gameplay Action Map 转换成玩家状态机使用的单帧输入快照。
    /// </summary>
    public sealed class PlayerInputReader : MonoBehaviour
    {
        /// <summary>项目输入动作资源，默认绑定 Assets/InputSystem_Actions.inputactions。</summary>
        [SerializeField] private InputActionAsset inputActions;
        /// <summary>战斗原型使用的动作表名称。</summary>
        [SerializeField] private string actionMapName = "Gameplay";

        private InputActionMap gameplayMap;
        private InputAction moveAction;
        private InputAction lookAction;
        private InputAction lockOnAction;
        private InputAction lightAttackAction;
        private InputAction heavyAttackAction;
        private InputAction evadeAction;
        private InputAction guardAction;
        private InputAction skill1Action;
        private InputAction skill2Action;
        private bool hasRequiredActions;

        /// <summary>当前帧输入快照，供 PlayerStateMachine 读取。</summary>
        public PlayerInputSnapshot CurrentSnapshot { get; private set; }

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            ResolveActions();

            if (hasRequiredActions)
            {
                gameplayMap.Enable();
            }
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            if (gameplayMap != null)
            {
                gameplayMap.Disable();
            }

            CurrentSnapshot = default;
        }

        /// <summary>
        /// 按帧推进运行时逻辑，并刷新依赖的状态、输入或显示数据。
        /// </summary>
        private void Update()
        {
            CurrentSnapshot = hasRequiredActions ? ReadInputSystem() : CreateEmptySnapshot();
        }

        /// <summary>
        /// 查找并缓存 Gameplay Action Map 中的全部必需动作，避免每帧按字符串查找。
        /// </summary>
        private void ResolveActions()
        {
            hasRequiredActions = false;

            if (inputActions == null)
            {
                Debug.LogWarning("PlayerInputReader missing InputActionAsset.", this);
                return;
            }

            gameplayMap = inputActions.FindActionMap(actionMapName, false);
            if (gameplayMap == null)
            {
                Debug.LogWarning($"PlayerInputReader cannot find action map: {actionMapName}", this);
                return;
            }

            moveAction = FindAction("Move");
            lookAction = FindAction("Look");
            lockOnAction = FindAction("LockOn");
            lightAttackAction = FindAction("LightAttack");
            heavyAttackAction = FindAction("HeavyAttack");
            evadeAction = FindAction("Evade");
            guardAction = FindAction("Guard");
            skill1Action = FindAction("Skill1");
            skill2Action = FindAction("Skill2");

            hasRequiredActions =
                moveAction != null &&
                lookAction != null &&
                lockOnAction != null &&
                lightAttackAction != null &&
                heavyAttackAction != null &&
                evadeAction != null &&
                guardAction != null &&
                skill1Action != null &&
                skill2Action != null;
        }

        /// <summary>
        /// 查找 Action 对象或数据，作为后续绑定、校验或显示的输入。
        /// </summary>
        private InputAction FindAction(string actionName)
        {
            InputAction action = gameplayMap.FindAction(actionName, false);
            if (action == null)
            {
                Debug.LogWarning($"PlayerInputReader cannot find action: {actionMapName}/{actionName}", this);
            }

            return action;
        }

        /// <summary>
        /// 读取新输入系统当前帧状态，并保持和旧 PlayerInputSnapshot 字段语义一致。
        /// </summary>
        private PlayerInputSnapshot ReadInputSystem()
        {
            Vector2 move = Vector2.ClampMagnitude(moveAction.ReadValue<Vector2>(), 1f);

            return new PlayerInputSnapshot
            {
                Move = move,
                Look = lookAction.ReadValue<Vector2>(),
                LockOnPressed = lockOnAction.WasPressedThisFrame(),
                LightAttackPressed = lightAttackAction.WasPressedThisFrame(),
                LightAttackHeld = lightAttackAction.IsPressed(),
                HeavyAttackPressed = heavyAttackAction.WasPressedThisFrame(),
                HeavyAttackHeld = heavyAttackAction.IsPressed(),
                EvadePressed = evadeAction.WasPressedThisFrame(),
                EvadeHeld = evadeAction.IsPressed(),
                EvadeReleased = evadeAction.WasReleasedThisFrame(),
                GuardPressed = guardAction.WasPressedThisFrame(),
                GuardHeld = guardAction.IsPressed(),
                GuardReleased = guardAction.WasReleasedThisFrame(),
                Skill1Pressed = skill1Action.WasPressedThisFrame(),
                Skill2Pressed = skill2Action.WasPressedThisFrame(),
                Time = UnityEngine.Time.time
            };
        }

        /// <summary>
        /// 创建 Empty / Snapshot 实例或数据，作为后续运行时流程的唯一标识或配置来源。
        /// </summary>
        private static PlayerInputSnapshot CreateEmptySnapshot()
        {
            return new PlayerInputSnapshot
            {
                Time = UnityEngine.Time.time
            };
        }
    }
}
