// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Player.Windows;
using System;

namespace ProjectEVE.Player.Attacks
{
    /// <summary>
    /// 普通攻击 Phase 轨道片段。只用于驱动 CurrentPhase，不参与功能窗口判断。
    /// </summary>
    [Serializable]
    public struct AttackPhaseWindow
    {
        public PlayerStatePhase Phase;
        public ActionWindow Window;

        /// <summary>
        /// 创建 AttackPhaseWindow 实例，并准备 玩家状态机 模块需要的初始状态。
        /// </summary>
        public AttackPhaseWindow(PlayerStatePhase phase, ActionWindow window)
        {
            Phase = phase;
            Window = window;
        }


        public bool Contains(float elapsedTime)
        {
            return Window.Contains(elapsedTime);
        }
    }

    /// <summary>
    /// 普通攻击连段节点定义。它只描述数据，不直接执行状态切换或命中结算。
    /// </summary>
    [Serializable]
    public sealed class AttackDefinition
    {
        /// <summary>连段节点标识，例如 L1、L2、H1。</summary>
        public string NodeId;
        /// <summary>进入该节点所需的攻击输入类型。</summary>
        public AttackInputType InputType;
        /// <summary>同链路中的段数，用于调试和 Animator 参数。</summary>
        public int ComboIndex;
        /// <summary>该攻击节点的总持续时间。</summary>
        public float TotalDuration;
        /// <summary>Combat Timeline 归一化运行时规格，PlayerAttack 通过它读取能力快照。</summary>
        public CombatActionRuntimeSpec RuntimeSpec;
        /// <summary>Provider 解析出的攻击命中窗口边界，供 Attack 状态判断挥空。</summary>
        public ActionWindow[] HitWindows;
        /// <summary>轻攻击输入可派生到的下一个节点；为空表示不能派生。</summary>
        public string NextLightNodeId;
        /// <summary>重攻击输入可派生到的下一个节点；为空表示不能派生。</summary>
        public string NextHeavyNodeId;
        /// <summary>当前阶段预留给 Hitbox 的攻击类型。</summary>
        public CombatAttackType CombatAttackType;
        /// <summary>当前阶段预留给伤害结算的基础伤害。</summary>
        public float Damage;
        /// <summary>当前阶段预留给削韧结算的基础削韧。</summary>
        public float PoiseDamage;

        /// <summary>
        /// 执行 Attack / Definition 相关逻辑，并维护 玩家状态机 模块的运行时一致性。
        /// </summary>
        public AttackDefinition(
            string nodeId,
            AttackInputType inputType,
            int comboIndex,
            float totalDuration,
            string nextLightNodeId,
            string nextHeavyNodeId,
            CombatAttackType combatAttackType,
            float damage,
            float poiseDamage,
            ActionWindow[] hitWindows,
            CombatActionRuntimeSpec runtimeSpec)
        {
            NodeId = nodeId;
            InputType = inputType;
            ComboIndex = comboIndex;
            TotalDuration = totalDuration;
            RuntimeSpec = runtimeSpec ?? throw new ArgumentNullException(nameof(runtimeSpec));
            HitWindows = hitWindows ?? Array.Empty<ActionWindow>();
            NextLightNodeId = nextLightNodeId;
            NextHeavyNodeId = nextHeavyNodeId;
            CombatAttackType = combatAttackType;
            Damage = damage;
            PoiseDamage = poiseDamage;
        }

        /// <summary>
        /// 根据输入类型返回可派生节点 ID。
        /// </summary>
        public string GetNextNodeId(AttackInputType inputType)
        {
            return inputType == AttackInputType.Light ? NextLightNodeId :
                inputType == AttackInputType.Heavy ? NextHeavyNodeId :
                string.Empty;
        }

        /// <summary>
        /// 解析当前帧能力快照，供 Attack 状态消费。
        /// </summary>
        public CombatActionCapabilitySnapshot EvaluateCapabilities(float elapsedTime)
        {
            return RuntimeSpec.Evaluate(elapsedTime);
        }
    }
}
