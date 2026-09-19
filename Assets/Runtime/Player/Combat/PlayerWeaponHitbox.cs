// 文件说明：维护玩家武器 Hitbox、玩家受击入口和战斗结果接收。
// 所属模块：玩家战斗。
// 运行影响：影响玩家命中发射、受击解析、战斗反馈触发和 Skill 命中特效通知。

using ProjectEVE.Combat;
using ProjectEVE.Feedback;
using ProjectEVE.Player.Attacks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Player.Combat
{
    /// <summary>
    /// 玩家武器命中盒。根据 PlayerStateContext 的 Attack / Skill 判定窗口启停 Trigger Collider。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class PlayerWeaponHitbox : MonoBehaviour
    {
        private enum ActiveActionKind
        {
            None,
            Attack,
            Skill
        }

        private struct SkillHitKey : IEquatable<SkillHitKey>
        {
            public CombatHurtbox Hurtbox;
            public int CastId;
            public string HitNodeId;


            public bool Equals(SkillHitKey other)
            {
                return ReferenceEquals(Hurtbox, other.Hurtbox) &&
                    CastId == other.CastId &&
                    string.Equals(HitNodeId, other.HitNodeId, StringComparison.Ordinal);
            }

            /// <summary>
            /// 执行 Equals 相关逻辑，并维护 玩家战斗 模块的运行时一致性。
            /// </summary>
            public override bool Equals(object obj)
            {
                return obj is SkillHitKey other && Equals(other);
            }

            /// <summary>
            /// 获取 Hash / Code 数据，作为运行时逻辑、调试显示或编辑器界面的只读输入。
            /// </summary>
            public override int GetHashCode()
            {
                unchecked
                {
                    int hash = Hurtbox != null ? Hurtbox.GetHashCode() : 0;
                    hash = (hash * 397) ^ CastId;
                    hash = (hash * 397) ^ (HitNodeId != null ? HitNodeId.GetHashCode() : 0);
                    return hash;
                }
            }
        }

        /// <summary>玩家状态机，用于读取当前动作窗口、攻击节点和伤害数据。</summary>
        [SerializeField] private PlayerStateMachine playerStateMachine;
        /// <summary>攻击来源阵营，玩家武器固定为 Player。</summary>
        [SerializeField] private CombatTeam attackerTeam = CombatTeam.Player;
        /// <summary>是否由脚本按判定窗口自动启停 Collider。</summary>
        [SerializeField] private bool toggleColliderByActiveWindow = true;
        /// <summary>命中是否允许普通防御。</summary>
        [SerializeField] private bool canBeGuarded = true;
        /// <summary>命中是否允许 Perfect Guard。</summary>
        [SerializeField] private bool canBePerfectGuarded = true;
        /// <summary>命中是否允许 Perfect Evade。</summary>
        [SerializeField] private bool canBePerfectEvaded = true;
        /// <summary>是否在控制台输出武器命中调试信息。</summary>
        [SerializeField] private bool logHits;
        /// <summary>Skill1 三段出手与命中特效控制器，只消费正式 Skill 命中结果。</summary>
        [SerializeField] private PlayerSkillEffectController skillEffectController;

        private readonly HashSet<CombatHurtbox> attackHitHurtboxes = new HashSet<CombatHurtbox>();
        private readonly HashSet<SkillHitKey> skillHitKeys = new HashSet<SkillHitKey>();
        private Collider hitboxCollider;
        private ActiveActionKind activeKind;
        private int activeActionId;
        private bool wasActive;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            BindReferences();
            EnsureTriggerCollider();

            if (toggleColliderByActiveWindow && hitboxCollider != null)
            {
                hitboxCollider.enabled = false;
            }
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
            EnsureTriggerCollider();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            BindReferences();
            EnsureTriggerCollider();
        }

        /// <summary>
        /// 按帧推进运行时逻辑，并刷新依赖的状态、输入或显示数据。
        /// </summary>
        private void Update()
        {
            if (playerStateMachine == null)
            {
                SetColliderEnabled(false);
                wasActive = false;
                return;
            }

            PlayerStateContext context = playerStateMachine.Context;
            ActiveActionKind nextKind = ResolveActiveKind(context, out int nextActionId);
            bool isActive = nextKind != ActiveActionKind.None;

            if (isActive && (!wasActive || activeKind != nextKind || activeActionId != nextActionId))
            {
                BeginActionWindow(nextKind, nextActionId);
            }
            else if (!isActive && wasActive)
            {
                EndActionWindow();
            }

            if (toggleColliderByActiveWindow)
            {
                SetColliderEnabled(isActive);
            }

            wasActive = isActive;
        }

        /// <summary>
        /// 处理 Trigger 进入事件，并将命中候选交给战斗检测链路。
        /// </summary>
        private void OnTriggerEnter(Collider other)
        {
            TryApplyHit(other);
        }

        /// <summary>
        /// 处理 Trigger 持续接触事件，并维持命中候选或调试状态。
        /// </summary>
        private void OnTriggerStay(Collider other)
        {
            TryApplyHit(other);
        }

        /// <summary>
        /// 解析 Active / Kind 结果，并把多来源输入收敛为后续逻辑可直接消费的数据。
        /// </summary>
        private ActiveActionKind ResolveActiveKind(PlayerStateContext context, out int actionId)
        {
            if (context.CurrentState == PlayerStateId.Attack &&
                context.IsAttackHitboxActive &&
                context.CurrentAttackInstanceId > 0)
            {
                actionId = context.CurrentAttackInstanceId;
                return ActiveActionKind.Attack;
            }

            if (context.CurrentState == PlayerStateId.Skill &&
                context.IsSkillHitboxActive &&
                context.CurrentSkillCastId > 0 &&
                !string.IsNullOrEmpty(context.CurrentSkillHitNodeId))
            {
                actionId = context.CurrentSkillCastId;
                return ActiveActionKind.Skill;
            }

            actionId = 0;
            return ActiveActionKind.None;
        }

        /// <summary>
        /// 开始 Action / Window 流程，初始化本次动作或窗口需要的运行时上下文。
        /// </summary>
        private void BeginActionWindow(ActiveActionKind actionKind, int actionId)
        {
            activeKind = actionKind;
            activeActionId = actionId;

            if (actionKind == ActiveActionKind.Attack)
            {
                attackHitHurtboxes.Clear();
            }

            if (actionKind == ActiveActionKind.Skill)
            {
                skillHitKeys.Clear();
            }

            SetColliderEnabled(true);
        }

        /// <summary>
        /// 结束 Action / Window 流程，清理本次动作或窗口持有的运行时上下文。
        /// </summary>
        private void EndActionWindow()
        {
            if (activeKind == ActiveActionKind.Skill)
            {
                skillEffectController?.EndHitWindow();
            }

            activeKind = ActiveActionKind.None;
            activeActionId = 0;
            attackHitHurtboxes.Clear();
            SetColliderEnabled(false);
        }

        /// <summary>
        /// 尝试执行 Apply / Hit，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private void TryApplyHit(Collider other)
        {
            if (playerStateMachine == null || other == null || activeKind == ActiveActionKind.None)
            {
                return;
            }

            CombatHurtbox hurtbox = other.GetComponentInParent<CombatHurtbox>();
            if (hurtbox == null || !CanHitHurtbox(hurtbox, playerStateMachine.Context))
            {
                return;
            }

            PlayerStateContext context = playerStateMachine.Context;
            CombatHitData hit = BuildHitData(context, other);
            if (!hurtbox.TryReceiveHit(hit, this, out CombatHitResult result))
            {
                return;
            }

            RecordSuccessfulHit(hurtbox, context);
            context.LastCombatHitOutcome = result.Outcome;
            CombatFeedbackBus.Raise(CombatFeedbackEvent.FromHit(hit, result, this, hurtbox.gameObject));

            if (activeKind == ActiveActionKind.Skill && IsSkillHitVisualOutcome(result.Outcome))
            {
                skillEffectController?.PlayHitEffect();
            }

            if (activeKind == ActiveActionKind.Attack && IsNormalAttackEnergyContact(result.Outcome))
            {
                context.RecordNormalAttackHit(context.CurrentAttackInstanceId);
            }

            if (IsPositiveContact(result.Outcome))
            {
                if (activeKind == ActiveActionKind.Attack)
                {
                    context.AttackContactResult = AttackContactResult.OnHit;
                }
                else if (activeKind == ActiveActionKind.Skill)
                {
                    context.SkillContactResult = AttackContactResult.OnHit;
                }
            }

            if (logHits)
            {
                Debug.Log(
                    $"Player weapon hit {hurtbox.name}: action={activeKind}, id={activeActionId}, outcome={result.Outcome}, hpDamage={result.AppliedHpDamage:0.##}",
                    this);
            }
        }

        /// <summary>
        /// 检查当前条件是否允许 Hit / Hurtbox，用于在真正提交行为前做非破坏性预判。
        /// </summary>
        private bool CanHitHurtbox(CombatHurtbox hurtbox, PlayerStateContext context)
        {
            if (activeKind == ActiveActionKind.Attack)
            {
                return !attackHitHurtboxes.Contains(hurtbox);
            }

            if (activeKind != ActiveActionKind.Skill)
            {
                return false;
            }

            SkillHitKey key = CreateSkillHitKey(hurtbox, context);
            return !skillHitKeys.Contains(key);
        }

        /// <summary>
        /// 执行 Record / Successful / Hit 相关逻辑，并维护 玩家战斗 模块的运行时一致性。
        /// </summary>
        private void RecordSuccessfulHit(CombatHurtbox hurtbox, PlayerStateContext context)
        {
            if (activeKind == ActiveActionKind.Attack)
            {
                attackHitHurtboxes.Add(hurtbox);
                return;
            }

            skillHitKeys.Add(CreateSkillHitKey(hurtbox, context));
        }

        /// <summary>
        /// 构建 Hit / Data 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private CombatHitData BuildHitData(PlayerStateContext context, Collider targetCollider)
        {
            Vector3 direction = context.AttackDirection.sqrMagnitude > 0.0001f
                ? context.AttackDirection.normalized
                : transform.forward;

            bool isSkill = activeKind == ActiveActionKind.Skill;
            return new CombatHitData
            {
                AttackId = isSkill ? context.CurrentSkillCastId : context.CurrentAttackInstanceId,
                AttackerTeam = attackerTeam,
                AttackType = isSkill ? CombatAttackType.SkillAttack : context.CurrentAttackCombatType,
                ReactionIntent = isSkill ? context.CurrentSkillReactionIntent : CombatReactionIntent.HitReaction,
                Damage = isSkill ? context.CurrentSkillDamage : context.CurrentAttackDamage,
                PoiseDamage = isSkill ? context.CurrentSkillPoiseDamage : context.CurrentAttackPoiseDamage,
                GuardDamage = isSkill ? context.CurrentSkillPoiseDamage : context.CurrentAttackPoiseDamage,
                HitPosition = targetCollider.ClosestPoint(transform.position),
                HitDirection = direction,
                CanBeGuarded = canBeGuarded,
                CanBePerfectGuarded = canBePerfectGuarded,
                CanBePerfectEvaded = canBePerfectEvaded
            };
        }

        /// <summary>
        /// 判断当前对象是否处于 Positive / Contact 状态，避免调用方直接读取内部实现细节。
        /// </summary>
        private static bool IsPositiveContact(CombatHitOutcome outcome)
        {
            return outcome == CombatHitOutcome.GuardHit ||
                outcome == CombatHitOutcome.DamageOnly ||
                outcome == CombatHitOutcome.HitReaction ||
                outcome == CombatHitOutcome.Knockdown ||
                outcome == CombatHitOutcome.Dead ||
                outcome == CombatHitOutcome.PerfectGuard ||
                outcome == CombatHitOutcome.PerfectEvade;
        }

        /// <summary>
        /// 判断命中结果是否属于普通攻击 BE 连续命中的有效结果。
        /// </summary>
        /// <param name="outcome">CombatHitResolver 返回的本次接触结果。</param>
        /// <returns>DamageOnly、HitReaction、Knockdown 或 Dead 返回 true；防守和无敌结果返回 false。</returns>
        private static bool IsNormalAttackEnergyContact(CombatHitOutcome outcome)
        {
            return outcome == CombatHitOutcome.DamageOnly ||
                outcome == CombatHitOutcome.HitReaction ||
                outcome == CombatHitOutcome.Knockdown ||
                outcome == CombatHitOutcome.Dead;
        }

        /// <summary>
        /// 判断 Skill 命中结果是否应播放 hitEffect，仅接受实际造成技能接触伤害的结果。
        /// </summary>
        /// <param name="outcome">CombatHitResolver 返回的正式 Skill 命中结果。</param>
        /// <returns>DamageOnly、HitReaction、Knockdown 或 Dead 返回 true；防守、闪避、无敌和空结果返回 false。</returns>
        private static bool IsSkillHitVisualOutcome(CombatHitOutcome outcome)
        {
            return outcome == CombatHitOutcome.DamageOnly ||
                outcome == CombatHitOutcome.HitReaction ||
                outcome == CombatHitOutcome.Knockdown ||
                outcome == CombatHitOutcome.Dead;
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

            if (hitboxCollider == null)
            {
                hitboxCollider = GetComponent<Collider>();
            }

            if (skillEffectController == null && playerStateMachine != null)
            {
                skillEffectController = playerStateMachine.GetComponentInChildren<PlayerSkillEffectController>(true);
            }
        }

        /// <summary>
        /// 创建 Skill / Hit / Key 实例或数据，作为后续运行时流程的唯一标识或配置来源。
        /// </summary>
        private static SkillHitKey CreateSkillHitKey(CombatHurtbox hurtbox, PlayerStateContext context)
        {
            return new SkillHitKey
            {
                Hurtbox = hurtbox,
                CastId = context.CurrentSkillCastId,
                HitNodeId = context.CurrentSkillHitNodeId
            };
        }

        /// <summary>
        /// 确保 Trigger / Collider 可用，不满足时创建、刷新或钳制必要的运行时数据。
        /// </summary>
        private void EnsureTriggerCollider()
        {
            if (hitboxCollider != null)
            {
                hitboxCollider.isTrigger = true;
            }
        }

        /// <summary>
        /// 设置 Collider / Enabled 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private void SetColliderEnabled(bool enabled)
        {
            if (toggleColliderByActiveWindow && hitboxCollider != null && hitboxCollider.enabled != enabled)
            {
                hitboxCollider.enabled = enabled;
            }
        }

    }
}
