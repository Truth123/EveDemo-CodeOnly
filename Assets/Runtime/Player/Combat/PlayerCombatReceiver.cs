// 文件说明：维护玩家武器 Hitbox、玩家受击入口和战斗结果接收。
// 所属模块：玩家战斗。
// 运行影响：影响玩家命中发射、受击解析和战斗反馈触发。

using ProjectEVE.Combat;
using UnityEngine;

namespace ProjectEVE.Player.Combat
{
    /// <summary>
    /// 玩家受击接收器。负责把玩家状态机上下文转换为防守方快照，并处理命中后的资源和状态切换。
    /// </summary>
    public sealed class PlayerCombatReceiver : MonoBehaviour
    {
        /// <summary>玩家状态机，提供当前防御、闪避、armor 能力和资源数据。</summary>
        [SerializeField] private PlayerStateMachine playerStateMachine;
        /// <summary>玩家所属阵营。与攻击方相同或中立时不进入结算。</summary>
        [SerializeField] private CombatTeam defenderTeam = CombatTeam.Player;
        /// <summary>Perfect Guard 成功后奖励的 BetaEnergy。</summary>
        [SerializeField] private float perfectGuardBetaGain = 2f;
        /// <summary>Perfect Evade 成功后奖励的 BetaEnergy。</summary>
        [SerializeField] private float perfectEvadeBetaGain = 2f;
        /// <summary>是否允许背后命中被 Guard / PerfectGuard 处理。</summary>
        [SerializeField] private bool backGuardAllowed;
        /// <summary>是否在控制台输出受击调试信息。</summary>
        [SerializeField] private bool logHits = true;

        private void Awake()
        {
            BindReferences();
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
        }

        /// <summary>
        /// 接收一次敌方命中。返回 CombatHitResult，便于测试命中器直接显示结算结果。
        /// </summary>
        public CombatHitResult ReceiveHit(in CombatHitData hit, Component source = null)
        {
            if (hit.AttackerTeam == CombatTeam.Neutral || hit.AttackerTeam == defenderTeam)
            {
                return new CombatHitResult(CombatHitOutcome.None, 0f, 0f);
            }

            if (playerStateMachine == null)
            {
                BindReferences();
                if (playerStateMachine == null)
                {
                    return new CombatHitResult(CombatHitOutcome.None, 0f, 0f);
                }
            }

            PlayerStateContext context = playerStateMachine.Context;
            context.RecordReceivedHit(hit);

            CombatHitResult result = CombatHitResolver.Resolve(hit, BuildDefenderState(context));
            if (ShouldIgnoreGroundHit(context, hit, result))
            {
                result = new CombatHitResult(CombatHitOutcome.None, 0f, 0f);
            }

            context.LastDefenderHitOutcome = result.Outcome;

            ApplyResourceResult(context, result);
            ApplyStateResult(context, result);

            if (logHits)
            {
                Debug.Log(
                    $"Player receive hit: intent={hit.ReactionIntent}, outcome={result.Outcome}, direction={context.LastReceivedHitDirectionId}, hp={context.Resources.CurrentHp:0}/{context.Resources.MaxHp:0}, be={context.Resources.BetaEnergy:0}/{context.Resources.MaxBetaEnergy:0}",
                    this);
            }

            return result;
        }

        /// <summary>
        /// 构建 Defender / State 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private DefenderCombatState BuildDefenderState(PlayerStateContext context)
        {
            bool canGuardIncomingHit = backGuardAllowed || context.LastReceivedHitDirectionId != PlayerHitDirectionId.Back;
            return new DefenderCombatState
            {
                CurrentHp = context.Resources.CurrentHp,
                IsGuarding = context.CurrentState == PlayerStateId.Guard && canGuardIncomingHit,
                IsGuardBlockActive = context.IsGuardBlockActive && canGuardIncomingHit,
                IsPerfectGuardWindow = (context.IsPerfectGuardWindow ||
                    context.IsPerfectGuardChainWindow) && canGuardIncomingHit,
                IsEvading = context.CurrentState == PlayerStateId.Evade,
                IsEvadeInvincible = context.IsEvadeInvincible,
                IsPerfectEvadeWindow = context.IsPerfectEvadeWindow,
                HasNearMissCandidate = false
            };
        }

        /// <summary>
        /// 执行 Should / Ignore / Ground / Hit 相关逻辑，并维护 玩家战斗 模块的运行时一致性。
        /// </summary>
        private static bool ShouldIgnoreGroundHit(PlayerStateContext context, in CombatHitData hit, CombatHitResult result)
        {
            return context.CurrentState == PlayerStateId.Knockdown &&
                context.CurrentPhase == PlayerStatePhase.Loop &&
                hit.ReactionIntent == CombatReactionIntent.HitReaction &&
                result.Outcome == CombatHitOutcome.HitReaction;
        }

        /// <summary>
        /// 应用 Resource / Result 结果到当前对象，可能改变资源、位移、动画或调试状态。
        /// </summary>
        private void ApplyResourceResult(PlayerStateContext context, CombatHitResult result)
        {
            switch (result.Outcome)
            {
                case CombatHitOutcome.PerfectGuard:
                    context.AddBetaEnergy(perfectGuardBetaGain);
                    break;
                case CombatHitOutcome.PerfectEvade:
                    context.AddBetaEnergy(perfectEvadeBetaGain);
                    break;
                case CombatHitOutcome.IgnoredByInvincible:
                case CombatHitOutcome.None:
                    break;
                default:
                    context.ApplyDamage(result.AppliedHpDamage);
                    break;
            }
        }

        /// <summary>
        /// 应用 State / Result 结果到当前对象，可能改变资源、位移、动画或调试状态。
        /// </summary>
        private void ApplyStateResult(PlayerStateContext context, CombatHitResult result)
        {
            if (context.Resources.IsDead || result.Outcome == CombatHitOutcome.Dead)
            {
                context.PrepareDead();
                playerStateMachine.RequestDead();
                return;
            }

            if (result.Outcome == CombatHitOutcome.PerfectGuard && context.CurrentState == PlayerStateId.Guard)
            {
                context.RequestGuardReaction(PlayerGuardReactionId.PerfectGuard);
                return;
            }

            if (result.Outcome == CombatHitOutcome.PerfectEvade && context.CurrentState == PlayerStateId.Evade)
            {
                context.ActivatePerfectEvade();
                return;
            }

            if (context.HasActiveReactionArmor)
            {
                return;
            }

            if (result.Outcome == CombatHitOutcome.GuardHit && context.CurrentState == PlayerStateId.Guard)
            {
                context.RequestGuardReaction(PlayerGuardReactionId.GuardHit);
                return;
            }

            if (result.Outcome == CombatHitOutcome.HitReaction)
            {
                playerStateMachine.RequestHitReaction();
                return;
            }

            if (result.Outcome == CombatHitOutcome.Knockdown)
            {
                context.PrepareKnockdown();
                playerStateMachine.RequestKnockdown();
            }
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (playerStateMachine == null)
            {
                playerStateMachine = GetComponent<PlayerStateMachine>();
            }

            if (playerStateMachine == null)
            {
                playerStateMachine = GetComponentInParent<PlayerStateMachine>();
            }
        }
    }
}
