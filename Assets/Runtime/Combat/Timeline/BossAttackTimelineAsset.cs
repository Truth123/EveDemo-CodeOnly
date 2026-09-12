// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Boss.AI;
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEVE.Combat.Timeline
{
    [CreateAssetMenu(fileName = "BossAttackTimeline", menuName = "Project EVE/Combat/Boss Attack Timeline")]
    public sealed class BossAttackTimelineAsset : CombatTimelineActionAsset
    {
        [SerializeField] private float angleLimit = 95f;
        [SerializeField] private float cooldown = 4f;
        [FormerlySerializedAs("recoveryTime")]
        [SerializeField] private float naturalRecoveryDuration = 0.2f;
        [SerializeField] private BossDefenseRewardRule[] defenseRewardRules = Array.Empty<BossDefenseRewardRule>();

        public float AngleLimit => angleLimit;
        public float Cooldown => cooldown;
        public float NaturalRecoveryDuration => naturalRecoveryDuration;
        public IReadOnlyList<BossDefenseRewardRule> DefenseRewardRules => defenseRewardRules;

        /// <summary>
        /// 配置 Boss 攻击的冷却、角度门控和最后 HitNode 后自然恢复时间。
        /// </summary>
        /// <param name="nextAngleLimit">出招前允许的水平面向夹角，单位为度。</param>
        /// <param name="nextCooldown">该招式被选择后的冷却时间，单位为秒。</param>
        /// <param name="nextNaturalRecoveryDuration">最后 HitNode 结束后的自然后摇，单位为秒。</param>
        public void ConfigureBossAttack(
            float nextAngleLimit,
            float nextCooldown,
            float nextNaturalRecoveryDuration)
        {
            angleLimit = Mathf.Max(0f, nextAngleLimit);
            cooldown = Mathf.Max(0f, nextCooldown);
            naturalRecoveryDuration = Mathf.Max(0f, nextNaturalRecoveryDuration);
        }

        /// <summary>兼容旧测试和迁移工具；权重参数不再写入 Timeline。</summary>
        public void ConfigureBossAttack(float nextAngleLimit, float nextCooldown, float ignoredWeight, float nextNaturalRecoveryDuration)
        {
            ConfigureBossAttack(nextAngleLimit, nextCooldown, nextNaturalRecoveryDuration);
        }

        /// <summary>写入本招式按 HitNode 结果组合计算的正确破解奖励规则。</summary>
        public void SetDefenseRewardRules(BossDefenseRewardRule[] nextRules)
        {
            defenseRewardRules = nextRules ?? Array.Empty<BossDefenseRewardRule>();
        }

        /// <summary>
        /// 将 Boss Attack Timeline 转换为运行时招式定义；位移模式与出招距离由 BossMotionWarpProfile 读取。
        /// </summary>
        /// <returns>供 Boss Brain、Runner 和 CombatSystem 读取的不可变运行时招式数据。</returns>
        public BossAttackDefinition ToBossAttackDefinition()
        {
            CombatActionRuntimeSpec runtimeSpec = BuildRuntimeSpec();
            return new BossAttackDefinition(
                ActionId,
                DisplayName,
                AnimationStateName,
                angleLimit,
                cooldown,
                TotalDuration,
                naturalRecoveryDuration,
                runtimeSpec.ToHitNodeData(),
                runtimeSpec,
                defenseRewardRules);
        }
    }
}
