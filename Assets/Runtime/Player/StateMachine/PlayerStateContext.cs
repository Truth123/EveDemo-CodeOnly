// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat;
using ProjectEVE.Input;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Movement;
using UnityEngine;

namespace ProjectEVE.Player
{
    /// <summary>
    /// Perfect Evade 四方向动画分类。数值会同步到 Animator 的 PerfectEvadeDirection 参数。
    /// </summary>
    public enum PerfectEvadeDirectionId
    {
        /// <summary>未触发或不需要播放 PerfectEvade 动画。</summary>
        None = 0,
        /// <summary>前向完美闪避动画。</summary>
        Forward = 1,
        /// <summary>后向完美闪避动画。</summary>
        Backward = 2,
        /// <summary>左向完美闪避动画。</summary>
        Left = 3,
        /// <summary>右向完美闪避动画。</summary>
        Right = 4
    }

    /// <summary>
    /// 敌方命中来源相对玩家朝向的方向分类，用于受击、防御和击倒动画选择。
    /// </summary>
    public enum PlayerHitDirectionId
    {
        None = 0,
        Front = 1,
        Back = 2,
        Left = 3,
        Right = 4
    }

    /// <summary>
    /// Guard 内部反应类型，不作为顶层状态。
    /// </summary>
    public enum PlayerGuardReactionId
    {
        None = 0,
        GuardHit = 1,
        PerfectGuard = 2
    }

    /// <summary>
    /// Guard Loop 中防御慢走的主方向。数值同步到 Animator 的 GuardMoveDirection 参数。
    /// </summary>
    public enum PlayerGuardMoveDirectionId
    {
        None = 0,
        Forward = 1,
        Backward = 2,
        Left = 3,
        Right = 4
    }

    /// <summary>
    /// 击倒表现类型，用于区分前倒和后倒动画。
    /// </summary>
    public enum PlayerKnockdownTypeId
    {
        Backward = 0,
        Forward = 1
    }

    /// <summary>
    /// 死亡表现类型。死亡判定仍由 HP / CombatHitOutcome 决定。
    /// </summary>
    public enum PlayerDeadTypeId
    {
        Stand = 0,
        Down = 1
    }

    /// <summary>
    /// 玩家状态机共享上下文。状态只读写这份运行时数据，避免直接耦合场景组件。
    /// </summary>
    public sealed class PlayerStateContext
    {
        /// <summary>当前帧输入快照。</summary>
        public PlayerInputSnapshot Input;
        /// <summary>玩家战斗资源，只包含 HP 与 BetaEnergy。</summary>
        public CombatResourceSet Resources;
        private int consecutiveNormalAttackHitCount;
        private int lastCountedNormalAttackInstanceId;
        /// <summary>当前顶层状态。</summary>
        public PlayerStateId CurrentState;
        /// <summary>上一个顶层状态。</summary>
        public PlayerStateId PreviousState;
        /// <summary>当前状态内部阶段。</summary>
        public PlayerStatePhase CurrentPhase;
        /// <summary>当前控制模式：自由视角或锁定视角。</summary>
        public ControlMode ControlMode;
        /// <summary>是否处于战斗模式，影响 Idle / Locomotion 动画选择。</summary>
        public bool IsInCombat;
        /// <summary>是否存在有效锁定目标。</summary>
        public bool IsLockOn;
        /// <summary>玩家是否已经死亡。</summary>
        public bool IsDead;
        /// <summary>进入当前状态后的累计时间。</summary>
        public float StateElapsedTime;
        /// <summary>移动输入进入 Locomotion 的最小阈值。</summary>
        public float MoveThreshold = 0.1f;
        /// <summary>玩家 Transform，供朝向和位移桥接使用。</summary>
        public Transform PlayerTransform;
        /// <summary>当前锁定目标，通常是 Boss。</summary>
        public Transform LockOnTarget;
        /// <summary>闪避长按形成的冲刺意图。</summary>
        public bool SprintIntent;
        /// <summary>当前帧解析后的世界空间移动方向。</summary>
        public Vector3 MoveDirection;
        /// <summary>当前帧实际使用的水平移动速度。</summary>
        public float MoveSpeed;
        /// <summary>当前帧 CharacterController 实际产生的水平速度。</summary>
        public Vector3 ActualHorizontalVelocity;
        /// <summary>由实际水平速度投影得到的 Animator 二维移动参数。</summary>
        public Vector2 AnimationMove;
        /// <summary>由实际水平速度归一化得到的 Animator 移动强度。</summary>
        public float AnimationMoveMagnitude;
        /// <summary>进入动作状态时锁存的二维动画方向，供 Evade 等一次性动作使用。</summary>
        public Vector2 ActionMove;
        /// <summary>进入动作状态时锁存的原始二维输入方向。</summary>
        public Vector2 ActionInputMove;
        /// <summary>进入动作状态后锁存的世界空间方向，避免闪避播放中途随相机漂移。</summary>
        public Vector3 ActionWorldDirection;
        /// <summary>当前动作是否已经解析并锁存了有效世界方向。</summary>
        public bool HasActionWorldDirection;
        /// <summary>状态本地输入缓存。动作状态按窗口决定是否写入和消费。</summary>
        public InputCommandBuffer InputBuffer = new InputCommandBuffer();

        /// <summary>请求进入 Attack 状态时携带的起手输入类型。</summary>
        public AttackInputType RequestedAttackInput;
        /// <summary>当前攻击节点 ID，例如 L1、H1。</summary>
        public string CurrentAttackNodeId = string.Empty;
        /// <summary>当前攻击节点的输入类型。</summary>
        public AttackInputType CurrentAttackInputType;
        /// <summary>当前攻击段数，用于调试和 Animator 同步。</summary>
        public int CurrentComboIndex;
        /// <summary>当前攻击实例 ID。每次进入新攻击节点都会变化，用于同一段攻击的重复命中过滤。</summary>
        public int CurrentAttackInstanceId;
        /// <summary>当前攻击用于命中解析的攻击类型。</summary>
        public CombatAttackType CurrentAttackCombatType;
        /// <summary>当前攻击的 HP 伤害。</summary>
        public float CurrentAttackDamage;
        /// <summary>当前攻击的削韧伤害，同时作为测试版防御值伤害。</summary>
        public float CurrentAttackPoiseDamage;
        /// <summary>本次攻击锁定的世界方向。</summary>
        public Vector3 AttackDirection;
        /// <summary>锁定攻击朝向 Boss 的期望转角，仅用于调试。</summary>
        public float LastAttackDesiredTurnAngle;
        /// <summary>锁定攻击实际应用到本次 AttackDirection 的转角，仅用于调试。</summary>
        public float LastAttackAppliedTurnAngle;
        /// <summary>本次攻击方向是否因锁定连段最大转角被裁剪。</summary>
        public bool WasAttackDirectionClamped;
        /// <summary>当前攻击判定窗口是否开启。</summary>
        public bool IsAttackHitboxActive;
        /// <summary>当前攻击命中结果。真实命中由 Hitbox / Hurtbox 阶段写入。</summary>
        public AttackContactResult AttackContactResult;
        /// <summary>最近一次玩家攻击命中目标时 CombatHitResolver 输出的结果。</summary>
        public CombatHitOutcome LastCombatHitOutcome;

        /// <summary>Evade 无敌窗口是否开启。</summary>
        public bool IsEvadeInvincible;
        /// <summary>Perfect Evade 判定窗口是否开启。</summary>
        public bool IsPerfectEvadeWindow;
        /// <summary>当前 Evade 内部模式，用于区分普通闪避和触发后的完美闪避专用时间轴。</summary>
        public EvadeModeId CurrentEvadeMode;
        /// <summary>是否已经在本次 Evade 中触发 PerfectEvade 动画模式。</summary>
        public bool IsPerfectEvadeActive;
        /// <summary>本次 PerfectEvade 使用的四方向动画分类。</summary>
        public PerfectEvadeDirectionId PerfectEvadeDirection;
        /// <summary>PerfectEvade 触发时对应的普通 Evade 已经过时间，用于动画 CrossFade 起播 offset 映射。</summary>
        public float PerfectEvadeActivatedElapsed;
        /// <summary>PerfectEvade 专用时间轴已经过时间，从触发 PerfectEvade 的当帧重新从 0 计算。</summary>
        public float PerfectEvadeModeElapsed;
        /// <summary>Guard 普通防御格挡是否已经生效。</summary>
        public bool IsGuardBlockActive;
        /// <summary>Perfect Guard 判定窗口是否开启。</summary>
        public bool IsPerfectGuardWindow;
        /// <summary>PerfectGuard 反应中通过重新按下 Guard 开启的连续完美防御窗口。</summary>
        public bool IsPerfectGuardChainWindow;
        /// <summary>当前连续完美防御窗口已开启时间，仅用于调试显示。</summary>
        public float PerfectGuardChainElapsed;
        /// <summary>是否是 GuardRelease 中重新按下防御后的起手重入，用于动画层选择更平滑的回防御路径。</summary>
        public bool IsGuardReentry;
        /// <summary>Guard Loop 中当前是否正在防御慢走。</summary>
        public bool IsGuardWalking;
        /// <summary>Guard 慢走动画方向。停止输入时保留最近方向，用于选择对应 End 动画。</summary>
        public PlayerGuardMoveDirectionId GuardMoveDirection;
        /// <summary>最近一次有效 Guard 慢走方向。</summary>
        public PlayerGuardMoveDirectionId LastGuardMoveDirection;
        /// <summary>Skill 霸体是否开启。霸体不等于无敌，只阻止非致死受击打断。</summary>
        public bool IsSkillSuperArmor;
        /// <summary>当前动作是否处于不可断或通用反应霸体窗口，用于 Attack Uninterruptible 等非 Skill 能力。</summary>
        public bool IsActionUninterruptible;
        /// <summary>Skill 武器判定窗口是否开启。</summary>
        public bool IsSkillHitboxActive;
        /// <summary>当前 Skill 释放实例 ID，用于多段命中去重和间隔控制。</summary>
        public int CurrentSkillCastId;
        /// <summary>当前 Skill 激活的 HitNode ID。为空表示当前帧没有技能命中段。</summary>
        public string CurrentSkillHitNodeId = string.Empty;
        /// <summary>当前 Skill 激活的 HitNode 序号，用于调试和命中去重。</summary>
        public int CurrentSkillHitIndex;
        /// <summary>当前 Skill 的 HP 伤害。</summary>
        public float CurrentSkillDamage;
        /// <summary>当前 Skill 的削韧 / 防御值伤害。</summary>
        public float CurrentSkillPoiseDamage;
        /// <summary>当前 Skill 命中希望防守方产生的基础反应。</summary>
        public CombatReactionIntent CurrentSkillReactionIntent = CombatReactionIntent.None;
        /// <summary>当前 Skill 最近一次接触结果。</summary>
        public AttackContactResult SkillContactResult;
        /// <summary>玩家作为防守方时最近一次受击解析结果。</summary>
        public CombatHitOutcome LastDefenderHitOutcome;
        /// <summary>最近一次敌方命中世界坐标。</summary>
        public Vector3 LastReceivedHitPosition;
        /// <summary>最近一次敌方命中方向。</summary>
        public Vector3 LastReceivedHitDirection;
        /// <summary>是否存在最近一次敌方命中数据。</summary>
        public bool HasLastReceivedHit;
        /// <summary>最近一次敌方命中来源方向。</summary>
        public PlayerHitDirectionId LastReceivedHitDirectionId;
        /// <summary>受击 / 击倒动画请求版本。每次有效进入 HitReaction 或 Knockdown 时递增，用于同动画重播。</summary>
        public int CombatReactionAnimationRequestVersion;
        /// <summary>当前 Guard 内部反应。</summary>
        public PlayerGuardReactionId CurrentGuardReaction;
        /// <summary>Guard 反应请求版本。每次 GuardHit / PerfectGuard 成功时递增。</summary>
        public int GuardReactionRequestVersion;
        /// <summary>当前击倒表现类型。</summary>
        public PlayerKnockdownTypeId CurrentKnockdownType;
        /// <summary>当前死亡表现类型。</summary>
        public PlayerDeadTypeId CurrentDeadType;

        /// <summary>状态本帧请求启动的代码驱动动作位移 ID。</summary>
        public PlayerActionMotionId RequestedActionMotionId;
        /// <summary>动作位移请求版本。每次请求递增，Motor 用它识别重启位移。</summary>
        public int ActionMotionRequestVersion;
        /// <summary>状态请求动作位移时指定的世界方向。</summary>
        public Vector3 RequestedActionMotionDirection;
        /// <summary>动作位移请求是否携带显式世界方向。</summary>
        public bool HasRequestedActionMotionDirection;
        /// <summary>当前 Motor 正在执行的动作位移 ID，用于调试。</summary>
        public PlayerActionMotionId ActiveActionMotionId;
        /// <summary>当前动作位移是否仍在执行。</summary>
        public bool IsActionMotionActive;
        /// <summary>当前动作位移已执行时间。</summary>
        public float ActionMotionElapsed;
        /// <summary>当前动作位移配置总时长。</summary>
        public float ActionMotionDuration;
        /// <summary>当前帧动作位移水平 delta。</summary>
        public Vector3 LastActionMotionDelta;
        /// <summary>当前帧 CharacterController.Move 返回的碰撞标记。</summary>
        public CollisionFlags LastActionMotionCollisionFlags;
        /// <summary>当前帧是否消费了攻击动画 Root Motion。</summary>
        public bool IsRootMotionActiveThisFrame;
        /// <summary>当前帧 Animator 输出的原始水平 Root Motion delta。</summary>
        public Vector3 LastRootMotionDelta;
        /// <summary>当前帧经 CharacterController.Move 实际应用的水平 Root Motion delta。</summary>
        public Vector3 LastAppliedRootMotionDelta;
        /// <summary>当前帧 Root Motion 移动返回的碰撞标记。</summary>
        public CollisionFlags LastRootMotionCollisionFlags;

        /// <summary>是否处于攻击起手可取消窗口。</summary>
        public bool IsCommitCancelWindow;
        /// <summary>是否处于连段消费窗口。</summary>
        public bool IsComboWindow;
        /// <summary>是否处于闪避取消窗口。</summary>
        public bool IsEvadeCancelWindow;
        /// <summary>是否处于技能取消窗口。</summary>
        public bool IsSkillCancelWindow;
        /// <summary>是否处于防御取消窗口。</summary>
        public bool IsGuardCancelWindow;
        /// <summary>是否处于动作重置窗口。</summary>
        public bool IsResetWindow;
        /// <summary>是否处于移动取消窗口。Attack / Skill 后摇与 Knockdown 起身阶段可用它低优先级恢复普通移动。</summary>
        public bool IsMoveCancelWindow;
        /// <summary>是否存在有效攻击缓存。</summary>
        public bool HasBufferedAttackInput;
        /// <summary>是否存在有效闪避缓存。</summary>
        public bool HasBufferedEvadeInput;
        /// <summary>是否存在有效技能缓存。</summary>
        public bool HasBufferedSkillInput;
        /// <summary>当前攻击缓存的输入类型。</summary>
        public AttackInputType BufferedAttackInputType;

        /// <summary>当前帧是否存在有效移动输入。</summary>
        public bool HasMoveInput => Input.HasMoveInput(MoveThreshold);
        /// <summary>检查 BetaEnergy 是否足够释放某个技能。</summary>
        public bool HasEnoughSkillEnergy(float cost) => Resources.BetaEnergy >= cost;
        /// <summary>当前是否具备阻止普通受击反应的 armor 能力。死亡仍由受击接收器优先处理。</summary>
        public bool HasActiveReactionArmor => IsSkillSuperArmor || IsActionUninterruptible;
        /// <summary>尚未兑换为 BE 的连续普通攻击有效命中次数，范围为 0 到 1。</summary>
        public int ConsecutiveNormalAttackHitCount => consecutiveNormalAttackHitCount;

        /// <summary>初始化玩家资源，进入 Play 后由 PlayerStateMachine 写入测试默认值。</summary>
        public void InitializeResources(CombatResourceSet initialResources)
        {
            Resources = initialResources;
            Resources.Clamp();
            IsDead = Resources.IsDead;
            consecutiveNormalAttackHitCount = 0;
            lastCountedNormalAttackInstanceId = 0;
        }

        /// <summary>尝试消耗 BetaEnergy。消耗失败时不修改资源。</summary>
        public bool TrySpendBetaEnergy(float cost)
        {
            if (Resources.BetaEnergy < cost)
            {
                return false;
            }

            Resources.BetaEnergy -= cost;
            Resources.Clamp();
            return true;
        }

        /// <summary>增加 BetaEnergy，主要由 PerfectGuard / PerfectEvade 奖励调用。</summary>
        public void AddBetaEnergy(float amount)
        {
            if (amount <= 0f)
            {
                return;
            }

            Resources.BetaEnergy += amount;
            Resources.Clamp();
        }

        /// <summary>
        /// 记录一个普通攻击实例的首次有效命中；每累计两个不同攻击实例增加 1 点 BE。
        /// </summary>
        /// <param name="attackInstanceId">当前普通攻击节点的正整数实例 ID。</param>
        /// <returns>true 表示本次实例首次计入连续命中；重复或非法实例返回 false。</returns>
        public bool RecordNormalAttackHit(int attackInstanceId)
        {
            if (attackInstanceId <= 0 || attackInstanceId == lastCountedNormalAttackInstanceId)
            {
                return false;
            }

            lastCountedNormalAttackInstanceId = attackInstanceId;
            consecutiveNormalAttackHitCount++;
            if (consecutiveNormalAttackHitCount >= 2)
            {
                AddBetaEnergy(1f);
                consecutiveNormalAttackHitCount = 0;
            }

            return true;
        }

        /// <summary>
        /// 在普通攻击判定窗口实际挥空后清除尚未兑换的连续命中次数。
        /// </summary>
        public void ResetNormalAttackHitSequenceOnWhiff()
        {
            consecutiveNormalAttackHitCount = 0;
        }

        /// <summary>进入普通 Evade 模式，重置本次 Evade 内部的 PerfectEvade 运行时数据。</summary>
        public void BeginNormalEvade()
        {
            CurrentEvadeMode = EvadeModeId.Normal;
            IsPerfectEvadeActive = false;
            PerfectEvadeDirection = PerfectEvadeDirectionId.None;
            PerfectEvadeActivatedElapsed = 0f;
            PerfectEvadeModeElapsed = 0f;
        }

        /// <summary>
        /// 激活 Evade 内部的 PerfectEvade 动画模式。顶层状态仍保持 Evade，但后续窗口改用 PerfectEvade 时间轴。
        /// </summary>
        public void ActivatePerfectEvade()
        {
            if (IsPerfectEvadeActive)
            {
                return;
            }

            CurrentEvadeMode = EvadeModeId.Perfect;
            IsPerfectEvadeActive = true;
            PerfectEvadeDirection = ResolvePerfectEvadeDirection(ActionMove);
            PerfectEvadeActivatedElapsed = StateElapsedTime;
            PerfectEvadeModeElapsed = 0f;
            RequestActionMotion(ResolvePerfectEvadeMotionId(PerfectEvadeDirection));
        }

        /// <summary>
        /// 清理 Evade 内部模式和 PerfectEvade 动画模式，通常在离开 Evade 或强制状态切换时调用。
        /// </summary>
        public void ClearEvadeRuntime()
        {
            CurrentEvadeMode = EvadeModeId.None;
            IsEvadeInvincible = false;
            IsPerfectEvadeWindow = false;
            IsPerfectEvadeActive = false;
            PerfectEvadeDirection = PerfectEvadeDirectionId.None;
            PerfectEvadeActivatedElapsed = 0f;
            PerfectEvadeModeElapsed = 0f;
        }

        /// <summary>
        /// 清理 PerfectEvade 动画模式，保留普通 Evade 模式时不会强制改写 CurrentEvadeMode。
        /// </summary>
        public void ClearPerfectEvadeRuntime()
        {
            if (CurrentEvadeMode == EvadeModeId.Perfect)
            {
                CurrentEvadeMode = EvadeModeId.None;
            }

            IsPerfectEvadeActive = false;
            PerfectEvadeDirection = PerfectEvadeDirectionId.None;
            PerfectEvadeActivatedElapsed = 0f;
            PerfectEvadeModeElapsed = 0f;
        }

        /// <summary>应用受击 HP 扣减。状态切换由 PlayerCombatReceiver 决定。</summary>
        /// <param name="hpDamage">本次应扣除的非负生命值。</param>
        public void ApplyDamage(float hpDamage)
        {
            Resources.CurrentHp -= Mathf.Max(0f, hpDamage);
            Resources.Clamp();
            IsDead = Resources.IsDead;
        }

        /// <summary>记录最近一次受击原始数据，供进入 HitReaction / Knockdown 时解析位移方向。</summary>
        public void RecordReceivedHit(in CombatHitData hit)
        {
            LastReceivedHitPosition = hit.HitPosition;
            LastReceivedHitDirection = hit.HitDirection;
            HasLastReceivedHit = true;
            LastReceivedHitDirectionId = ResolveLastHitDirectionId();
        }

        /// <summary>请求 Guard 状态播放一次内部反应。</summary>
        public void RequestGuardReaction(PlayerGuardReactionId reaction)
        {
            CurrentGuardReaction = reaction;
            GuardReactionRequestVersion++;
        }

        /// <summary>清理 Guard 内部反应。</summary>
        public void ClearGuardReaction()
        {
            CurrentGuardReaction = PlayerGuardReactionId.None;
        }

        /// <summary>清理 PerfectGuard 连续弹反窗口运行时数据。</summary>
        public void ClearPerfectGuardChainWindow()
        {
            IsPerfectGuardChainWindow = false;
            PerfectGuardChainElapsed = 0f;
        }

        /// <summary>根据最近受击方向准备击倒表现。</summary>
        public void PrepareKnockdown()
        {
            CurrentKnockdownType = LastReceivedHitDirectionId == PlayerHitDirectionId.Back
                ? PlayerKnockdownTypeId.Forward
                : PlayerKnockdownTypeId.Backward;
        }

        /// <summary>根据当前状态准备死亡表现。</summary>
        public void PrepareDead()
        {
            CurrentDeadType = CurrentState == PlayerStateId.Knockdown || PreviousState == PlayerStateId.Knockdown
                ? PlayerDeadTypeId.Down
                : PlayerDeadTypeId.Stand;
        }

        /// <summary>请求启动代码驱动动作位移，由 PlayerMovementMotor 在移动阶段消费。</summary>
        public void RequestActionMotion(PlayerActionMotionId motionId)
        {
            RequestedActionMotionId = motionId;
            RequestedActionMotionDirection = Vector3.zero;
            HasRequestedActionMotionDirection = false;
            ActionMotionRequestVersion++;
        }

        /// <summary>请求启动带显式方向的代码驱动动作位移，由 PlayerMovementMotor 在移动阶段消费。</summary>
        public void RequestActionMotion(PlayerActionMotionId motionId, Vector3 worldDirection)
        {
            RequestedActionMotionId = motionId;
            RequestedActionMotionDirection = worldDirection;
            HasRequestedActionMotionDirection = worldDirection.sqrMagnitude > 0.0001f;
            ActionMotionRequestVersion++;
        }

        /// <summary>按最近受击数据解析水平击退方向。</summary>
        public Vector3 ResolveLastHitKnockbackDirection()
        {
            Vector3 direction = LastReceivedHitDirection;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.0001f)
            {
                return direction.normalized;
            }

            if (PlayerTransform != null && HasLastReceivedHit)
            {
                direction = PlayerTransform.position - LastReceivedHitPosition;
                direction.y = 0f;
                if (direction.sqrMagnitude > 0.0001f)
                {
                    return direction.normalized;
                }
            }

            return PlayerTransform != null ? -PlayerTransform.forward : Vector3.back;
        }

        /// <summary>解析最近一次命中来源相对玩家朝向的方向。</summary>
        public PlayerHitDirectionId ResolveLastHitDirectionId()
        {
            if (PlayerTransform == null)
            {
                return PlayerHitDirectionId.None;
            }

            Vector3 sourceDirection = -ResolveLastHitKnockbackDirection();
            sourceDirection.y = 0f;
            if (sourceDirection.sqrMagnitude <= 0.0001f)
            {
                return PlayerHitDirectionId.Front;
            }

            sourceDirection.Normalize();
            Vector3 forward = PlayerTransform.forward;
            Vector3 right = PlayerTransform.right;
            forward.y = 0f;
            right.y = 0f;
            forward.Normalize();
            right.Normalize();

            float forwardDot = Vector3.Dot(forward, sourceDirection);
            float rightDot = Vector3.Dot(right, sourceDirection);
            if (Mathf.Abs(forwardDot) >= Mathf.Abs(rightDot))
            {
                return forwardDot >= 0f ? PlayerHitDirectionId.Front : PlayerHitDirectionId.Back;
            }

            return rightDot >= 0f ? PlayerHitDirectionId.Right : PlayerHitDirectionId.Left;
        }

        /// <summary>同步动作位移调试状态。</summary>
        public void SetActionMotionRuntimeState(
            bool isActive,
            PlayerActionMotionId motionId,
            float elapsed,
            float duration,
            Vector3 lastDelta,
            CollisionFlags collisionFlags)
        {
            IsActionMotionActive = isActive;
            ActiveActionMotionId = motionId;
            ActionMotionElapsed = elapsed;
            ActionMotionDuration = duration;
            LastActionMotionDelta = lastDelta;
            LastActionMotionCollisionFlags = collisionFlags;
        }

        /// <summary>同步攻击 Root Motion 调试状态。</summary>
        public void SetRootMotionRuntimeState(
            bool isActive,
            Vector3 rawDelta,
            Vector3 appliedDelta,
            CollisionFlags collisionFlags)
        {
            IsRootMotionActiveThisFrame = isActive;
            LastRootMotionDelta = rawDelta;
            LastAppliedRootMotionDelta = appliedDelta;
            LastRootMotionCollisionFlags = collisionFlags;
        }

        /// <summary>切换顶层状态，并重置阶段和状态计时。</summary>
        public void SetState(PlayerStateId nextState)
        {
            PreviousState = CurrentState;
            CurrentState = nextState;
            CurrentPhase = PlayerStatePhase.Start;
            StateElapsedTime = 0f;
        }

        /// <summary>请求下一次进入 Attack 时使用指定起手类型。</summary>
        public void RequestAttack(AttackInputType inputType)
        {
            RequestedAttackInput = inputType;
        }

        /// <summary>清理攻击节点、窗口和缓存显示状态。</summary>
        public void ClearAttackRuntime()
        {
            CurrentAttackNodeId = string.Empty;
            CurrentAttackInputType = AttackInputType.None;
            CurrentComboIndex = 0;
            CurrentAttackInstanceId = 0;
            CurrentAttackCombatType = CombatAttackType.LightAttack;
            CurrentAttackDamage = 0f;
            CurrentAttackPoiseDamage = 0f;
            AttackDirection = Vector3.zero;
            LastAttackDesiredTurnAngle = 0f;
            LastAttackAppliedTurnAngle = 0f;
            WasAttackDirectionClamped = false;
            IsAttackHitboxActive = false;
            AttackContactResult = ProjectEVE.Player.Attacks.AttackContactResult.None;
            LastCombatHitOutcome = CombatHitOutcome.None;
            IsActionUninterruptible = false;
            ClearActionWindows();
            HasBufferedAttackInput = false;
            HasBufferedEvadeInput = false;
            HasBufferedSkillInput = false;
            BufferedAttackInputType = AttackInputType.None;
        }

        /// <summary>清理 Evade / Guard / Skill 等防守和派生窗口标记。</summary>
        public void ClearActionWindows()
        {
            IsCommitCancelWindow = false;
            IsComboWindow = false;
            IsEvadeCancelWindow = false;
            IsSkillCancelWindow = false;
            IsGuardCancelWindow = false;
            IsResetWindow = false;
            IsMoveCancelWindow = false;
        }

        /// <summary>清理 Evade 与 Guard 的防守判定标记。</summary>
        public void ClearDefenseRuntime()
        {
            IsEvadeInvincible = false;
            IsPerfectEvadeWindow = false;
            IsGuardBlockActive = false;
            IsPerfectGuardWindow = false;
            ClearPerfectGuardChainWindow();
            IsGuardReentry = false;
            IsGuardWalking = false;
            GuardMoveDirection = PlayerGuardMoveDirectionId.None;
            LastGuardMoveDirection = PlayerGuardMoveDirectionId.None;
        }

        /// <summary>清理 Skill 释放数据和判定标记。</summary>
        public void ClearSkillRuntime()
        {
            IsSkillSuperArmor = false;
            IsActionUninterruptible = false;
            IsSkillHitboxActive = false;
            CurrentSkillCastId = 0;
            CurrentSkillHitNodeId = string.Empty;
            CurrentSkillHitIndex = 0;
            CurrentSkillDamage = 0f;
            CurrentSkillPoiseDamage = 0f;
            CurrentSkillReactionIntent = CombatReactionIntent.None;
            SkillContactResult = ProjectEVE.Player.Attacks.AttackContactResult.None;
        }

        /// <summary>进入强制状态或死亡时清理动作窗口、缓存和一次性判定标记。</summary>
        public void ClearActionWindowsAndBuffers()
        {
            InputBuffer.Clear();
            RequestedAttackInput = AttackInputType.None;
            ClearAttackRuntime();
            ClearDefenseRuntime();
            ClearSkillRuntime();
            ActionMove = Vector2.zero;
            ActionInputMove = Vector2.zero;
            ActionWorldDirection = Vector3.zero;
            HasActionWorldDirection = false;
            ClearEvadeRuntime();
        }

        /// <summary>
        /// 解析 Perfect / Evade / Direction 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static PerfectEvadeDirectionId ResolvePerfectEvadeDirection(Vector2 actionMove)
        {
            if (actionMove.sqrMagnitude <= 0.0001f)
            {
                return PerfectEvadeDirectionId.Backward;
            }

            return Mathf.Abs(actionMove.x) > Mathf.Abs(actionMove.y)
                ? actionMove.x < 0f ? PerfectEvadeDirectionId.Left : PerfectEvadeDirectionId.Right
                : actionMove.y < 0f ? PerfectEvadeDirectionId.Backward : PerfectEvadeDirectionId.Forward;
        }

        /// <summary>
        /// 解析 Perfect / Evade / Motion / Id 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private static PlayerActionMotionId ResolvePerfectEvadeMotionId(PerfectEvadeDirectionId direction)
        {
            switch (direction)
            {
                case PerfectEvadeDirectionId.Forward:
                    return PlayerActionMotionId.PerfectEvadeForward;
                case PerfectEvadeDirectionId.Backward:
                    return PlayerActionMotionId.PerfectEvadeBackward;
                case PerfectEvadeDirectionId.Left:
                    return PlayerActionMotionId.PerfectEvadeLeft;
                case PerfectEvadeDirectionId.Right:
                    return PlayerActionMotionId.PerfectEvadeRight;
                default:
                    return PlayerActionMotionId.PerfectEvadeBackward;
            }
        }
    }
}
