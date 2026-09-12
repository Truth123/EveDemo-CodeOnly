// 文件说明：定义通用命中、伤害和防守方状态数据。
// 所属模块：战斗伤害数据。
// 运行影响：影响 Player/Boss 命中链路的数据传递。

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 战斗阵营，用于过滤友方命中和区分玩家/Boss 攻击来源。
    /// </summary>
    public enum CombatTeam
    {
        /// <summary>中立对象。</summary>
        Neutral = 0,
        /// <summary>玩家阵营。</summary>
        Player = 1,
        /// <summary>Boss 阵营。</summary>
        Boss = 2
    }

    /// <summary>
    /// 攻击来源类型。只表达 Light / Heavy / Skill 等攻击通道，不表达击倒、破防等反应效果。
    /// </summary>
    public enum CombatAttackType
    {
        /// <summary>玩家轻攻击来源。</summary>
        LightAttack = 0,
        /// <summary>玩家重攻击来源。</summary>
        HeavyAttack = 1,
        /// <summary>玩家技能攻击。</summary>
        SkillAttack = 4
    }

    /// <summary>
    /// 命中打点声明的基础反应意图。Resolver 会在防御、闪避和死亡规则后把它转换为最终 CombatHitOutcome。
    /// </summary>
    public enum CombatReactionIntent
    {
        /// <summary>未配置有效反应意图；运行时视为无效打点，不造成伤害。</summary>
        None = 0,
        /// <summary>普通受击意图；未被防御 / 完美防御处理时进入 HitReaction。</summary>
        HitReaction = 1,
        /// <summary>击倒意图；未被防御 / 完美防御处理时进入 Knockdown。</summary>
        Knockdown = 2,
        /// <summary>只造成 HP 伤害，不要求防守方进入受击反应。</summary>
        DamageOnly = 3
    }

    /// <summary>
    /// 单次命中解析结果。结果顺序对应需求文档中的受击解析优先级。
    /// </summary>
    public enum CombatHitOutcome
    {
        /// <summary>未产生有效结果。</summary>
        None = 0,
        /// <summary>被闪避无敌帧忽略。</summary>
        IgnoredByInvincible = 10,
        /// <summary>触发完美闪避。</summary>
        PerfectEvade = 20,
        /// <summary>触发完美防御。</summary>
        PerfectGuard = 30,
        /// <summary>普通防御成功受击。</summary>
        GuardHit = 40,
        /// <summary>只造成 HP 伤害，不进入受击反应。</summary>
        DamageOnly = 45,
        /// <summary>普通受击硬直。</summary>
        HitReaction = 50,
        /// <summary>击倒或大硬直。</summary>
        Knockdown = 60,
        /// <summary>死亡。</summary>
        Dead = 70
    }
}
