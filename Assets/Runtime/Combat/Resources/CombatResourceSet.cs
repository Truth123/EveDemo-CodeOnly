// 文件说明：维护 HP、BetaEnergy 等战斗资源数据和组件。
// 所属模块：战斗资源。
// 运行影响：影响资源初始化、伤害应用和死亡判断。

using System;

namespace ProjectEVE.Combat
{
    /// <summary>
    /// 玩家战斗资源集合。第 0 阶段只定义数据，后续由资源系统负责读写。
    /// </summary>
    [Serializable]
    public struct CombatResourceSet
    {
        /// <summary>当前生命值。</summary>
        public float CurrentHp;
        /// <summary>最大生命值。</summary>
        public float MaxHp;
        /// <summary>当前 Beta 能量，用于释放技能。</summary>
        public float BetaEnergy;
        /// <summary>Beta 能量上限。</summary>
        public float MaxBetaEnergy;
        /// <summary>生命值是否已经归零。MaxHp 未初始化时不判定死亡，避免第 1 阶段空场景启动即 Dead。</summary>
        public bool IsDead => MaxHp > 0f && CurrentHp <= 0f;

        /// <summary>
        /// 将资源限制在合法范围内，避免调试或状态切换时产生负数资源。
        /// </summary>
        public void Clamp()
        {
            CurrentHp = Math.Clamp(CurrentHp, 0f, MaxHp);
            BetaEnergy = Math.Clamp(BetaEnergy, 0f, MaxBetaEnergy);
        }
    }
}
