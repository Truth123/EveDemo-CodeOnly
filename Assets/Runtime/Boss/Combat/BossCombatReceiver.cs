// 文件说明：维护 Boss 受击入口。
// 所属模块：Boss 战斗。
// 运行影响：影响 Boss HitNode 检测、玩家受击结算和 Boss 受击反馈。

using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 侧玩家命中接收器。订阅通用 Hurtbox 事件，把玩家攻击解释为 Boss AI 受击、击倒或不打断。
    /// </summary>
    public sealed class BossCombatReceiver : MonoBehaviour
    {
        /// <summary>Boss 顶层 Actor。</summary>
        [SerializeField] private BossActor bossActor;
        /// <summary>需要监听的 Boss 受击盒。未绑定时自动查找自身和子节点。</summary>
        [SerializeField] private CombatHurtbox[] hurtboxes;
        /// <summary>是否输出 Boss 接收玩家命中的调试日志。</summary>
        [SerializeField] private bool logHits;

        private void Awake()
        {
            BindReferences();
        }

        private void OnEnable()
        {
            BindReferences();
            SubscribeHurtboxes();
        }

        private void OnDisable()
        {
            UnsubscribeHurtboxes();
        }

        private void Reset()
        {
            BindReferences();
        }

        private void OnValidate()
        {
            BindReferences();
        }

        private void HandleHitResolved(CombatHurtbox hurtbox, CombatHitData hit, CombatHitResult result, Component source)
        {
            if (hit.AttackerTeam != CombatTeam.Player || bossActor == null)
            {
                return;
            }

            bossActor.ReceivePlayerHit(hit, result, source);

            if (logHits)
            {
                Debug.Log($"Boss received player hit: attackType={hit.AttackType}, outcome={result.Outcome}, hurtbox={hurtbox.name}", this);
            }
        }


        private void SubscribeHurtboxes()
        {
            if (hurtboxes == null)
            {
                return;
            }

            for (int i = 0; i < hurtboxes.Length; i++)
            {
                if (hurtboxes[i] == null)
                {
                    continue;
                }

                hurtboxes[i].HitResolved -= HandleHitResolved;
                hurtboxes[i].HitResolved += HandleHitResolved;
            }
        }

        private void UnsubscribeHurtboxes()
        {
            if (hurtboxes == null)
            {
                return;
            }

            for (int i = 0; i < hurtboxes.Length; i++)
            {
                if (hurtboxes[i] != null)
                {
                    hurtboxes[i].HitResolved -= HandleHitResolved;
                }
            }
        }

        private void BindReferences()
        {
            if (bossActor == null)
            {
                bossActor = GetComponentInParent<BossActor>();
            }

            if (hurtboxes == null || hurtboxes.Length == 0)
            {
                hurtboxes = GetComponentsInChildren<CombatHurtbox>(true);
            }
        }
    }
}
