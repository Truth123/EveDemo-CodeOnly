// 文件说明：维护通用 Hurtbox、阵营过滤和命中解析入口。
// 所属模块：战斗检测。
// 运行影响：影响通用受击盒、资源结算和命中事件发布。

using UnityEngine;
using System;

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 受击盒组件。负责过滤阵营、构造防守方状态快照，并把命中交给 CombatHitResolver 解析。
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public sealed class CombatHurtbox : MonoBehaviour
    {
        /// <summary>命中完成事件。Hurtbox 只暴露通用战斗数据，不依赖玩家或 Boss 具体命名空间。</summary>
        public event Action<CombatHurtbox, CombatHitData, CombatHitResult, Component> HitResolved;

        /// <summary>该受击盒所属阵营。玩家武器命中 Boss 时会过滤同阵营目标。</summary>
        [SerializeField] private CombatTeam team = CombatTeam.Boss;
        /// <summary>受击盒所属资源组件，未手动绑定时从自身或父节点查找。</summary>
        [SerializeField] private CombatResourceComponent resourceComponent;
        /// <summary>是否处于防御状态。第 6 阶段 Boss 测试目标默认关闭，后续 Boss/玩家状态机会接管。</summary>
        [SerializeField] private bool isGuarding;
        /// <summary>普通防御判定是否有效。</summary>
        [SerializeField] private bool isGuardBlockActive;
        /// <summary>完美防御窗口是否有效。</summary>
        [SerializeField] private bool isPerfectGuardWindow;
        /// <summary>是否处于闪避状态。</summary>
        [SerializeField] private bool isEvading;
        /// <summary>闪避无敌帧是否有效。</summary>
        [SerializeField] private bool isEvadeInvincible;
        /// <summary>完美闪避窗口是否有效。</summary>
        [SerializeField] private bool isPerfectEvadeWindow;
        /// <summary>是否存在 NearMiss 候选。</summary>
        [SerializeField] private bool hasNearMissCandidate;
        /// <summary>是否在控制台输出命中调试信息。</summary>
        [SerializeField] private bool logHits = true;

        private Collider hurtboxCollider;
        private CombatHitData lastHitData;
        private CombatHitResult lastHitResult;
        private Component lastHitSource;

        /// <summary>该 Hurtbox 所属阵营。</summary>
        public CombatTeam Team => team;

        /// <summary>最近一次命中的解析结果。</summary>
        public CombatHitOutcome LastOutcome => lastHitResult.Outcome;

        /// <summary>最近一次命中后的资源组件。</summary>
        public CombatResourceComponent ResourceComponent => resourceComponent;

        /// <summary>最近一次命中来源。</summary>
        public Component LastHitSource => lastHitSource;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            BindReferences();
            EnsureTriggerCollider();
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
        /// 接收一次命中。返回 false 表示被阵营或空数据过滤，没有进入结算。
        /// </summary>
        public bool TryReceiveHit(in CombatHitData hit, Component source, out CombatHitResult result)
        {
            result = default;

            if (hit.AttackerTeam == CombatTeam.Neutral || hit.AttackerTeam == team)
            {
                return false;
            }

            DefenderCombatState defender = BuildDefenderState();
            result = CombatHitResolver.Resolve(hit, defender);
            resourceComponent?.ApplyHitResult(result);

            lastHitData = hit;
            lastHitResult = result;
            lastHitSource = source;

            if (logHits)
            {
                string hpText = resourceComponent == null
                    ? "NoResource"
                    : $"HP {resourceComponent.CurrentHp:0}/{resourceComponent.MaxHp:0}";
                Debug.Log($"Combat hit: {hit.AttackId} {hit.AttackType} -> {name}, outcome={result.Outcome}, damage={result.AppliedHpDamage:0.##}, {hpText}", this);
            }

            HitResolved?.Invoke(this, hit, result, source);
            return true;
        }

        /// <summary>
        /// 构建 Defender / State 数据结构，供运行时、编辑器或调试显示使用。
        /// </summary>
        private DefenderCombatState BuildDefenderState()
        {
            return new DefenderCombatState
            {
                CurrentHp = resourceComponent != null ? resourceComponent.CurrentHp : 1f,
                IsGuarding = isGuarding,
                IsGuardBlockActive = isGuardBlockActive,
                IsPerfectGuardWindow = isPerfectGuardWindow,
                IsEvading = isEvading,
                IsEvadeInvincible = isEvadeInvincible,
                IsPerfectEvadeWindow = isPerfectEvadeWindow,
                HasNearMissCandidate = hasNearMissCandidate
            };
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (resourceComponent == null)
            {
                resourceComponent = GetComponentInParent<CombatResourceComponent>();
            }

            if (hurtboxCollider == null || hurtboxCollider is CharacterController)
            {
                hurtboxCollider = null;
                Collider[] colliders = GetComponents<Collider>();
                for (int i = 0; i < colliders.Length; i++)
                {
                    if (colliders[i] is CharacterController)
                    {
                        continue;
                    }

                    hurtboxCollider = colliders[i];
                    break;
                }
            }
        }

        /// <summary>
        /// 确保 Trigger / Collider 可用，不满足时创建、刷新或钳制必要的运行时数据。
        /// </summary>
        private void EnsureTriggerCollider()
        {
            if (hurtboxCollider != null)
            {
                hurtboxCollider.isTrigger = true;
            }
        }

    }
}
