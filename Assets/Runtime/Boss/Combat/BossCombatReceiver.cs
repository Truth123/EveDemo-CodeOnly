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

        /// <summary>
        /// 在组件启用时注册事件、恢复运行时状态或刷新显示。
        /// </summary>
        private void OnEnable()
        {
            BindReferences();
            SubscribeHurtboxes();
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            UnsubscribeHurtboxes();
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            BindReferences();
        }

        /// <summary>
        /// 处理 Hit / Resolved 事件或输入，并把结果分发到对应运行时系统。
        /// </summary>
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

        /// <summary>
        /// 执行 Subscribe / Hurtboxes 相关逻辑，并维护 Boss 战斗 模块的运行时一致性。
        /// </summary>
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

        /// <summary>
        /// 执行 Unsubscribe / Hurtboxes 相关逻辑，并维护 Boss 战斗 模块的运行时一致性。
        /// </summary>
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

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
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
