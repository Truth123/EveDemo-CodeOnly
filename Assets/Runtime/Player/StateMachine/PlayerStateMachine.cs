// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.CameraSystem.LockOn;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Input;
using ProjectEVE.Player.Animation;
using ProjectEVE.Player.Combat;
using ProjectEVE.Player.Movement;
using ProjectEVE.Player.States;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Player
{
    /// <summary>
    /// 玩家状态机的 MonoBehaviour 桥接层。负责输入仲裁、状态注册、单帧单次切换和组件协作。
    /// </summary>
    public sealed class PlayerStateMachine : MonoBehaviour
    {
        /// <summary>场景启动时进入的初始顶层状态。</summary>
        [SerializeField] private PlayerStateId initialState = PlayerStateId.Idle;
        /// <summary>第 7 阶段机制测试用玩家初始资源。</summary>
        [SerializeField]
        private CombatResourceSet initialResources = new CombatResourceSet
        {
            CurrentHp = 300f,
            MaxHp = 300f,
            BetaEnergy = 0f,
            MaxBetaEnergy = 32f
        };
        /// <summary>移动输入进入 Locomotion 的阈值。</summary>
        [SerializeField] private float moveThreshold = 0.1f;
        /// <summary>玩家输入读取器，负责把键鼠输入转换为 PlayerInputSnapshot。</summary>
        [SerializeField] private PlayerInputReader inputReader;
        /// <summary>锁定控制器，负责选择、维持和解除 Boss 锁定。</summary>
        [SerializeField] private LockOnController lockOnController;
        /// <summary>移动 Motor，负责自由移动、锁定移动和 SprintIntent。</summary>
        [SerializeField] private PlayerMovementMotor movementMotor;
        /// <summary>动画桥接器，负责把状态上下文同步到 Animator 参数。</summary>
        [SerializeField] private PlayerAnimationBridge animationBridge;
        /// <summary>Skill1 三段出手与命中特效控制器，由状态和武器 Hitbox 共享调用。</summary>
        [SerializeField] private PlayerSkillEffectController skillEffectController;

        /// <summary>状态机共享上下文，后续具体状态会基于它读取输入和资源。</summary>
        public PlayerStateContext Context { get; } = new PlayerStateContext();

        private readonly Dictionary<PlayerStateId, PlayerStateBase> states = new Dictionary<PlayerStateId, PlayerStateBase>();
        private PlayerStateBase currentState;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            // 只在桥接层绑定 Unity 对象引用，具体状态逻辑保持为可测试的普通 C#。
            if (inputReader == null)
            {
                inputReader = GetComponent<PlayerInputReader>();
            }

            if (lockOnController == null)
            {
                lockOnController = GetComponent<LockOnController>();
            }

            if (movementMotor == null)
            {
                movementMotor = GetComponent<PlayerMovementMotor>();
            }

            if (animationBridge == null)
            {
                animationBridge = GetComponent<PlayerAnimationBridge>();
            }

            if (skillEffectController == null)
            {
                skillEffectController = GetComponentInChildren<PlayerSkillEffectController>(true);
            }

            RegisterStates();

            Context.InitializeResources(initialResources);
            Context.PlayerTransform = transform;
            Context.MoveThreshold = moveThreshold;
            TransitionTo(initialState);
        }

        /// <summary>
        /// 按帧推进运行时逻辑，并刷新依赖的状态、输入或显示数据。
        /// </summary>
        private void Update()
        {
            float deltaTime = UnityEngine.Time.deltaTime;
            if (inputReader != null)
            {
                Context.Input = inputReader.CurrentSnapshot;
            }

            lockOnController?.Tick(Context);

            if (Context.Input.LockOnPressed)
            {
                lockOnController?.ToggleLockOn(Context);
            }

            TickState(deltaTime);
            movementMotor?.Tick(Context, deltaTime);
            animationBridge?.Tick(Context);
        }

        /// <summary>
        /// 调试或强制流程使用的状态切换入口。
        /// </summary>
        public void ForceState(PlayerStateId nextState)
        {
            TransitionTo(nextState);
        }

        /// <summary>
        /// 把玩家恢复为标题展示状态，清除锁定与残留动作输入，并同步非战斗 Idle 动画参数。
        /// </summary>
        public void PrepareTitlePresentation()
        {
            Context.Input = default;
            Context.ClearActionWindowsAndBuffers();
            Context.IsInCombat = false;
            lockOnController?.ClearLockOn(Context);
            if (Context.CurrentState != PlayerStateId.Dead && Context.CurrentState != PlayerStateId.Idle)
            {
                TransitionTo(PlayerStateId.Idle);
            }

            animationBridge?.Tick(Context);
        }

        /// <summary>
        /// 在开场镜头到位后让玩家进入战斗 Idle，并优先锁定调用方指定的 Boss 目标。
        /// </summary>
        /// <param name="preferredTarget">需要自动锁定的 Boss 目标；为空或失效时回退到当前视野内最佳目标。</param>
        /// <returns>true 表示已经获得正式锁定目标；false 表示没有可用目标但仍已进入战斗 Idle。</returns>
        public bool BeginCombatIdleAndLockOn(LockOnTarget preferredTarget)
        {
            Context.Input = default;
            Context.ClearActionWindowsAndBuffers();
            Context.IsInCombat = true;
            if (Context.CurrentState != PlayerStateId.Dead)
            {
                TransitionTo(PlayerStateId.Idle);
            }

            bool locked = lockOnController != null &&
                (lockOnController.TryLockOnTarget(Context, preferredTarget) ||
                 lockOnController.TryLockOnBestTarget(Context));
            animationBridge?.Tick(Context);
            return locked;
        }

        /// <summary>
        /// 外部命中系统可调用该入口请求普通受击。
        /// </summary>
        public void RequestHitReaction()
        {
            Context.CombatReactionAnimationRequestVersion++;
            TransitionTo(PlayerStateId.HitReaction);
        }

        /// <summary>
        /// 外部命中系统可调用该入口请求击倒。
        /// </summary>
        public void RequestKnockdown()
        {
            Context.CombatReactionAnimationRequestVersion++;
            TransitionTo(PlayerStateId.Knockdown);
        }

        /// <summary>
        /// 外部战斗流程可调用该入口请求死亡。
        /// </summary>
        public void RequestDead()
        {
            TransitionTo(PlayerStateId.Dead);
        }

        /// <summary>
        /// 获取当前玩家状态匹配的 Combat Timeline 播放事实，供编辑器运行时 playhead 跟随。
        /// </summary>
        public bool TryGetCombatTimelinePlaybackSnapshot(out CombatTimelineRuntimePlaybackSnapshot snapshot)
        {
            snapshot = default;
            switch (Context.CurrentState)
            {
                case PlayerStateId.Attack:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        Context.CurrentAttackNodeId,
                        Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.Skill:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        PlayerSkillActionResolver.Skill1ActionId,
                        Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.Evade:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        Context.CurrentEvadeMode == EvadeModeId.Perfect
                            ? PlayerEvadeState.PerfectTimelineId
                            : PlayerEvadeState.NormalTimelineId,
                        Context.CurrentEvadeMode == EvadeModeId.Perfect
                            ? Context.PerfectEvadeModeElapsed
                            : Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.Guard:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        PlayerGuardState.GuardTimelineId,
                        Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.HitReaction:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        PlayerHitReactionState.TimelineId,
                        Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.Knockdown:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        PlayerKnockdownState.TimelineId,
                        Context.StateElapsedTime,
                        out snapshot);
                case PlayerStateId.Dead:
                    return TryCreatePlaybackSnapshot(
                        Context.CurrentState,
                        PlayerDeadState.TimelineId,
                        Context.StateElapsedTime,
                        out snapshot);
                default:
                    return false;
            }
        }

        /// <summary>
        /// 注册 States 到查询表、事件或运行时缓存中。
        /// </summary>
        private void RegisterStates()
        {
            states.Clear();
            Register(new PlayerIdleState());
            Register(new PlayerLocomotionState());
            Register(new PlayerEvadeState());
            Register(new PlayerAttackState());
            Register(new PlayerGuardState());
            Register(new PlayerSkillState(skillEffectController));
            Register(new PlayerHitReactionState());
            Register(new PlayerKnockdownState());
            Register(new PlayerDeadState());
        }

        /// <summary>
        /// 注册 Register 到查询表、事件或运行时缓存中。
        /// </summary>
        private void Register(PlayerStateBase state)
        {
            states[state.StateId] = state;
        }

        /// <summary>
        /// 创建玩家运行时 Timeline 播放快照；无 ActionId 的状态不参与编辑器跟随。
        /// </summary>
        private static bool TryCreatePlaybackSnapshot(
            PlayerStateId state,
            string actionId,
            float elapsedTime,
            out CombatTimelineRuntimePlaybackSnapshot snapshot)
        {
            if (string.IsNullOrEmpty(actionId))
            {
                snapshot = default;
                return false;
            }

            snapshot = new CombatTimelineRuntimePlaybackSnapshot(
                "Player",
                state.ToString(),
                actionId,
                elapsedTime);
            return true;
        }

        /// <summary>
        /// 推进 State 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        private void TickState(float deltaTime)
        {
            PlayerStateId requestedState = EvaluateForcedTransition();

            if (requestedState == PlayerStateId.None)
            {
                requestedState = EvaluateInputTransition();
            }

            if (requestedState == PlayerStateId.None && currentState != null)
            {
                Context.StateElapsedTime += deltaTime;
                requestedState = currentState.Tick(Context, deltaTime);
            }

            if (requestedState != PlayerStateId.None && requestedState != Context.CurrentState)
            {
                TransitionTo(requestedState);
            }
        }


        private PlayerStateId EvaluateForcedTransition()
        {
            if (Context.Resources.IsDead && Context.CurrentState != PlayerStateId.Dead)
            {
                return PlayerStateId.Dead;
            }

            return PlayerStateId.None;
        }

        /// <summary>
        /// 执行 Evaluate / Input / Transition 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        private PlayerStateId EvaluateInputTransition()
        {
            if (Context.CurrentState != PlayerStateId.Idle && Context.CurrentState != PlayerStateId.Locomotion)
            {
                return PlayerStateId.None;
            }

            if (Context.Input.EvadePressed)
            {
                return PlayerStateId.Evade;
            }

            if (PlayerActionInputRouter.TryConsumeSkill1(Context, true))
            {
                return PlayerStateId.Skill;
            }

            if (Context.Input.LightAttackPressed || Context.Input.HeavyAttackPressed)
            {
                Context.RequestAttack(PlayerActionInputRouter.ResolveRequestedAttackInput(Context));
                return PlayerStateId.Attack;
            }

            if (PlayerActionInputRouter.TryConsumeGuard(Context, true))
            {
                return PlayerStateId.Guard;
            }

            if (Context.CurrentState == PlayerStateId.Idle && Context.HasMoveInput)
            {
                return PlayerStateId.Locomotion;
            }

            if (Context.CurrentState == PlayerStateId.Locomotion && !Context.HasMoveInput)
            {
                return PlayerStateId.Idle;
            }

            return PlayerStateId.None;
        }

        /// <summary>
        /// 执行 Transition / To 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        private void TransitionTo(PlayerStateId nextState)
        {
            if (!states.TryGetValue(nextState, out PlayerStateBase next))
            {
                Debug.LogWarning($"PlayerStateMachine cannot transition to unregistered state: {nextState}", this);
                return;
            }

            currentState?.Exit(Context);
            Context.SetState(nextState);
            currentState = next;
            currentState.Enter(Context);
            animationBridge?.OnStateChanged(Context);
        }
    }
}
