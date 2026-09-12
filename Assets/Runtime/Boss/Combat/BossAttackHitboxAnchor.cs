// 文件说明：维护 Boss 攻击 Hitbox Anchor。
// 所属模块：Boss 战斗。
// 运行影响：影响 Boss HitNode 检测、玩家受击结算和 Boss 受击反馈。

using ProjectEVE.Boss.AI;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 攻击检测盒锚点。正式 Boss 命中只读取手动配置的 BoxCollider，不生成临时代码检测体。
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class BossAttackHitboxAnchor : MonoBehaviour
    {
        /// <summary>该检测盒对应的 Boss 攻击来源部位，用于 HitNode 按 SourcePart 查找。</summary>
        [SerializeField] private BossAttackSourcePart sourcePart = BossAttackSourcePart.Weapon;
        /// <summary>手动调整的攻击检测盒。代码只读取 center/size/enabled，不覆盖配置。</summary>
        [SerializeField] private BoxCollider boxCollider;

        public BossAttackSourcePart SourcePart => sourcePart;
        public BoxCollider BoxCollider => boxCollider;
        public bool IsUsable => sourcePart != BossAttackSourcePart.None && boxCollider != null;
        /// <summary>锚点所在对象激活且 BoxCollider 启用时，才允许参与手工 OverlapBox 查询。</summary>
        public bool IsActiveForCombat =>
            IsUsable &&
            gameObject.activeInHierarchy &&
            boxCollider.enabled;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
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
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            BindReferences();
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (boxCollider == null)
            {
                boxCollider = GetComponent<BoxCollider>();
            }
        }

    }
}
