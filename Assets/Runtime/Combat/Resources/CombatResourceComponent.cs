// 文件说明：维护 HP、BetaEnergy 等战斗资源数据和组件。
// 所属模块：战斗资源。
// 运行影响：影响资源初始化、伤害应用和死亡判断。

using UnityEngine;

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 战斗资源组件。把 CombatResourceSet 挂到场景对象上，供 Hurtbox、Boss 或玩家状态机读写。
    /// </summary>
    public sealed class CombatResourceComponent : MonoBehaviour
    {
        /// <summary>运行时资源数据，只维护 HP 与 BetaEnergy。</summary>
        [SerializeField]
        private CombatResourceSet resources = new CombatResourceSet
        {
            CurrentHp = 500f,
            MaxHp = 500f,
            BetaEnergy = 0f,
            MaxBetaEnergy = 0f
        };

        /// <summary>当前资源快照。返回值是结构体副本，外部修改不会直接写回组件。</summary>
        public CombatResourceSet Resources => resources;

        /// <summary>当前 HP。</summary>
        public float CurrentHp => resources.CurrentHp;

        /// <summary>最大 HP。</summary>
        public float MaxHp => resources.MaxHp;

        /// <summary>资源是否已经死亡。</summary>
        public bool IsDead => resources.IsDead;

        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            NormalizeInitialValues();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            NormalizeInitialValues();
        }

        /// <summary>
        /// 应用命中解析结果造成的 HP 变化；底层 GuardDamage 仅保留为命中结果数据，不再驱动资源。
        /// </summary>
        public void ApplyHitResult(in CombatHitResult result)
        {
            resources.CurrentHp -= Mathf.Max(0f, result.AppliedHpDamage);
            resources.Clamp();
        }

        /// <summary>
        /// 调试用重置入口，把 HP 恢复到上限并保留当前 BetaEnergy。
        /// </summary>
        public void ResetToFull()
        {
            NormalizeInitialValues();
            resources.CurrentHp = resources.MaxHp;
            resources.Clamp();
        }

        /// <summary>
        /// 钳制资源上限，并在初始 HP 非法时恢复为最大值。
        /// </summary>
        private void NormalizeInitialValues()
        {
            resources.MaxHp = Mathf.Max(0f, resources.MaxHp);
            resources.MaxBetaEnergy = Mathf.Max(0f, resources.MaxBetaEnergy);

            if (resources.MaxHp > 0f && resources.CurrentHp <= 0f)
            {
                resources.CurrentHp = resources.MaxHp;
            }

            resources.Clamp();
        }
    }
}
