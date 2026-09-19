// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Combat;
using ProjectEVE.Player.Attacks;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "PlayerAttackTimeline", menuName = "Project EVE/Combat/Player Attack Timeline")]
    public sealed class PlayerAttackTimelineAsset : CombatTimelineActionAsset
    {
        [SerializeField] private AttackInputType attackInputType = AttackInputType.Light;
        [SerializeField] private int comboIndex;
        [SerializeField] private string nextLightNodeId;
        [SerializeField] private string nextHeavyNodeId;
        [SerializeField] private CombatAttackType attackType = CombatAttackType.LightAttack;
        [SerializeField] private float damage;
        [SerializeField] private float poiseDamage;

        public AttackInputType AttackInputType => attackInputType;
        public int ComboIndex => comboIndex;
        public string NextLightNodeId => nextLightNodeId;
        public string NextHeavyNodeId => nextHeavyNodeId;
        public CombatAttackType AttackType => attackType;
        public float Damage => damage;
        public float PoiseDamage => poiseDamage;


        public void ConfigureAttack(
            AttackInputType inputType,
            int nextComboIndex,
            string nextLight,
            string nextHeavy,
            CombatAttackType combatAttackType,
            float nextDamage,
            float nextPoiseDamage)
        {
            attackInputType = inputType;
            comboIndex = Mathf.Max(0, nextComboIndex);
            nextLightNodeId = nextLight ?? string.Empty;
            nextHeavyNodeId = nextHeavy ?? string.Empty;
            attackType = combatAttackType;
            damage = Mathf.Max(0f, nextDamage);
            poiseDamage = Mathf.Max(0f, nextPoiseDamage);
        }

        /// <summary>
        /// 将当前对象转换为 Attack / Definition 数据，供其他模块读取。
        /// </summary>
        public AttackDefinition ToAttackDefinition()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            var hitWindows = runtimeSpec.GetWindows(CombatTimelineCapabilityId.Hit_Attack);
            return new AttackDefinition(
                ActionId,
                attackInputType,
                comboIndex,
                TotalDuration,
                nextLightNodeId,
                nextHeavyNodeId,
                attackType,
                damage,
                poiseDamage,
                hitWindows,
                runtimeSpec);
        }
    }
}
