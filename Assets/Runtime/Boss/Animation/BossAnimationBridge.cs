// 文件说明：维护 Boss Animator 播放桥接。
// 所属模块：Boss 动画。
// 运行影响：影响 Boss 基础状态、死亡、受击和攻击动作的动画播放。

using ProjectEVE.Boss.AI;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEVE.Boss.Animation
{
    /// <summary>
    /// Boss 动画桥接器。第一版直接 CrossFade 到 Raven.controller 中的状态，避免复杂 Animator 转场。
    /// </summary>
    public sealed class BossAnimationBridge : MonoBehaviour
    {
        /// <summary>Raven 使用的 Animator。未绑定时从自身或子节点查找。</summary>
        [SerializeField] private Animator animator;
        /// <summary>非 Idle 状态切换的默认固定混合秒数。</summary>
        [FormerlySerializedAs("defaultCrossFadeDuration")]
        [SerializeField] private float defaultCrossFadeDurationSeconds = 0.08f;
        /// <summary>Idle 状态名。</summary>
        [SerializeField] private string idleStateName = "M_Raven_BattleIdle01";
        /// <summary>死亡状态名；进入 Dead 后从起始帧播放并保持死亡姿势。</summary>
        [SerializeField] private string deadStateName = "CH_P_EVE_51|Eve_Stand_Dead2";
        /// <summary>前进状态名。</summary>
        [SerializeField] private string approachStateName = "M_Raven_Caution_Fw";
        /// <summary>左试探状态名。</summary>
        [SerializeField] private string strafeLeftStateName = "M_Raven_Caution_Lw";
        /// <summary>右试探状态名。</summary>
        [SerializeField] private string strafeRightStateName = "M_Raven_Caution_Rw";
        /// <summary>正面站立受击状态名。</summary>
        [SerializeField] private string hitFrontStateName = "Result_Hit_Stand_Light_Fw_Dw";
        /// <summary>背面站立受击状态名。</summary>
        [SerializeField] private string hitBackStateName = "Result_Hit_Stand_Light_Bw";
        /// <summary>左侧站立受击状态名。</summary>
        [SerializeField] private string hitLeftStateName = "Result_Hit_Stand_Light_Lw";
        /// <summary>右侧站立受击状态名。</summary>
        [SerializeField] private string hitRightStateName = "Result_Hit_Stand_Light_Rw";
        /// <summary>PerfectGuard 反制 Boss 时播放的受击状态名。</summary>
        [SerializeField] private string justParryStateName = "Result_Hit_JustParry";
        /// <summary>防御护盾击破眩晕状态名；该 Animator 状态保持动画末帧且无自动退出。</summary>
        [SerializeField] private string shieldBreakStunStateName = "Result_ShieldBreak_Stun";
        /// <summary>击倒开始，Boss 向后倒。</summary>
        [SerializeField] private string knockdownStartBackwardStateName = "Result_State_KnockDown_S_Bw";
        /// <summary>击倒开始，Boss 向前倒。</summary>
        [SerializeField] private string knockdownStartForwardStateName = "Result_State_KnockDown_S_Fw";
        /// <summary>击倒循环。</summary>
        [SerializeField] private string knockdownLoopStateName = "Result_State_KnockDown_L";
        /// <summary>击倒结束起身。</summary>
        [SerializeField] private string knockdownEndStateName = "Result_State_KnockDown_E";

        private int currentStateHash;

        private void Awake()
        {
            BindReferences();
        }

        private void Reset()
        {
            BindReferences();
        }

        /// <summary>播放 Boss 状态对应的基础动画。</summary>
        public void PlayState(BossStateId stateId)
        {
            switch (stateId)
            {
                case BossStateId.Approach:
                    CrossFadeState(approachStateName);
                    break;
                case BossStateId.Strafe:
                    PlayStrafe(1);
                    break;
                case BossStateId.Idle:
                case BossStateId.Recovery:
                    CrossFadeState(idleStateName);
                    break;
                case BossStateId.Dead:
                    CrossFadeState(deadStateName, true);
                    break;
                case BossStateId.HitStagger:
                    PlayHitReaction(BossHitDirectionId.Front);
                    break;
                case BossStateId.Knockdown:
                    PlayKnockdownStart(BossHitDirectionId.Front);
                    break;
                case BossStateId.ShieldBreakStun:
                    PlayShieldBreakStun();
                    break;
            }
        }

        /// <summary>按 Boss 自身横移方向播放试探移动动画。+1 为自身左移，-1 为自身右移。</summary>
        public void PlayStrafe(int strafeDirection)
        {
            CrossFadeState(strafeDirection >= 0 ? strafeLeftStateName : strafeRightStateName);
        }

        /// <summary>播放当前 Boss 招式动画。</summary>
        public void PlayAttack(BossAttackDefinition attackDefinition)
        {
            if (attackDefinition == null || string.IsNullOrEmpty(attackDefinition.AnimationStateName))
            {
                return;
            }

            CrossFadeState(attackDefinition.AnimationStateName);
        }

        /// <summary>播放 Boss 站立受击动画。</summary>
        public void PlayHitReaction(BossHitDirectionId direction)
        {
            CrossFadeState(ResolveHitStateName(direction), true);
        }

        /// <summary>播放 Boss 被 PerfectGuard 反制的动画。</summary>
        public void PlayPerfectGuardStagger()
        {
            CrossFadeState(justParryStateName, true);
        }

        /// <summary>播放防御护盾击破眩晕动画，并强制从起始帧重播。</summary>
        public void PlayShieldBreakStun()
        {
            CrossFadeState(shieldBreakStunStateName, true);
        }

        /// <summary>播放 Boss 击倒开始动画。</summary>
        public void PlayKnockdownStart(BossHitDirectionId direction)
        {
            string stateName = direction == BossHitDirectionId.Back
                ? knockdownStartForwardStateName
                : knockdownStartBackwardStateName;
            CrossFadeState(stateName, true);
        }

        /// <summary>播放 Boss 击倒循环动画。</summary>
        public void PlayKnockdownLoop()
        {
            CrossFadeState(knockdownLoopStateName, true);
        }

        /// <summary>播放 Boss 击倒结束动画。</summary>
        public void PlayKnockdownEnd()
        {
            CrossFadeState(knockdownEndStateName, true);
        }

        private string ResolveHitStateName(BossHitDirectionId direction)
        {
            switch (direction)
            {
                case BossHitDirectionId.Back:
                    return hitBackStateName;
                case BossHitDirectionId.Left:
                    return hitLeftStateName;
                case BossHitDirectionId.Right:
                    return hitRightStateName;
                case BossHitDirectionId.Front:
                default:
                    return hitFrontStateName;
            }
        }


        private void CrossFadeState(string stateName, bool forceRestart = false)
        {
            if (animator == null || string.IsNullOrEmpty(stateName))
            {
                return;
            }

            int stateHash = Animator.StringToHash($"Base Layer.{stateName}");
            if (!forceRestart && currentStateHash == stateHash)
            {
                return;
            }

            if (!animator.HasState(0, stateHash))
            {
                return;
            }

            if (stateName == idleStateName)
            {
                animator.CrossFadeInFixedTime(stateHash, 0.2f, 0, 0f);
            }
            else
            {
                animator.CrossFadeInFixedTime(stateHash, defaultCrossFadeDurationSeconds, 0, 0f);
            }
            currentStateHash = stateHash;
        }

        private void BindReferences()
        {
            if (animator == null)
            {
                animator = GetComponent<Animator>();
            }

            if (animator == null)
            {
                animator = GetComponentInChildren<Animator>();
            }
        }
    }
}
