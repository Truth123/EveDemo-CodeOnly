// 文件说明：定义 Raven Boss Combat Timeline 招式 ID 常量。
// 所属模块：Boss AI 数据。
// 运行影响：影响 Boss 出招选择、MotionWarp 配置和 Timeline 资产 ID 对齐。

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Raven Boss Timeline 招式 ID。这里只保存常量，不承担 Provider 查询职责。
    /// </summary>
    public static class RavenBossAttackIds
    {
        public const string SlashId = "Raven_Slash";
        public const string OrbitSideSlashId = "Raven_OrbitSideSlash";
        public const string SlashChainId = "Raven_SlashChain";
        public const string EvadeBackRushId = "Raven_EvadeBackRush";
        public const string ChaseComboId = "Raven_ChaseCombo";
        public const string MoveComboId = "Raven_MoveCombo";
        public const string MoveChainComboId = "Raven_MoveChainCombo";
        public const string ChaseGrabId = "Raven_ChaseGrab";
        public const string SlashComboId = "Raven_SlashCombo";
        public const string BetaChargeComboId = "Raven_BetaChargeCombo";
        public const string BurstAreaSlashId = "Raven_BurstAreaSlash";
        public const string SwordAuraComboId = "Raven_SwordAuraCombo";
        public const string EvadeBackSwordAuraId = "Raven_EvadeBackSwordAura";
        public const string RapidMoveBackId = "Raven_RapidMoveBack";
    }
}
