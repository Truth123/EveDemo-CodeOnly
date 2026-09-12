// 文件说明：定义 Boss ActionSet 可使用的战术标签常量。
// 所属模块：Boss AI 数据。
// 运行影响：影响 BossActionSelector 对合格攻击候选的战术评分，不绕过冷却、距离、角度或空间预筛选。

namespace ProjectEVE.Boss.AI
{
    /// <summary>
    /// Boss 动作池战术标签。标签只用于合格候选之间的 Utility 调整。
    /// </summary>
    public static class BossActionTags
    {
        /// <summary>快速近身基础压迫。</summary>
        public const string FastPressure = "FastPressure";
        /// <summary>近距离多段连击。</summary>
        public const string CloseCombo = "CloseCombo";
        /// <summary>后撤或拉开后反击。</summary>
        public const string DisengageCounter = "DisengageCounter";
        /// <summary>中距离突进多段追击。</summary>
        public const string GapCloseCombo = "GapCloseCombo";
        /// <summary>连续防御 / 完美格挡压力。</summary>
        public const string MultiHitPressure = "MultiHitPressure";
        /// <summary>黄光不可格挡攻击，只能通过闪避 / 完美闪避处理。</summary>
        public const string YellowUnblockable = "YellowUnblockable";

        /// <summary>
        /// 判断标签是否属于决定 Boss 行为节奏的核心战术标签。
        /// </summary>
        /// <param name="tag">待检查的 ActionSet 标签。</param>
        /// <returns>true 表示该标签会参与最近标签重复限制。</returns>
        public static bool IsCoreTacticalTag(string tag)
        {
            return tag == FastPressure ||
                   tag == CloseCombo ||
                   tag == DisengageCounter ||
                   tag == GapCloseCombo ||
                   tag == MultiHitPressure;
        }
    }
}
