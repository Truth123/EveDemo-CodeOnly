// 文件说明：维护 Unity 新输入系统读取和玩家输入快照。
// 所属模块：输入系统。
// 运行影响：影响状态机输入来源、移动视角输入和动作按键采样。

using System;
using UnityEngine;

namespace ProjectEVE.Input
{
    /// <summary>
    /// 单帧输入快照。输入系统每帧写入，玩家状态机只读取该快照做状态决策。
    /// </summary>
    [Serializable]
    public struct PlayerInputSnapshot
    {
        /// <summary>移动输入，来自 WASD 或后续手柄左摇杆。</summary>
        public Vector2 Move;
        /// <summary>视角输入，来自鼠标移动或后续手柄右摇杆。</summary>
        public Vector2 Look;
        /// <summary>本帧是否按下锁定键。</summary>
        public bool LockOnPressed;
        /// <summary>本帧是否按下轻攻击。</summary>
        public bool LightAttackPressed;
        /// <summary>轻攻击键当前是否保持按住，用于后续 BetaAttack 预留。</summary>
        public bool LightAttackHeld;
        /// <summary>本帧是否按下重攻击。</summary>
        public bool HeavyAttackPressed;
        /// <summary>重攻击键当前是否保持按住，用于后续蓄力或 BetaAttack 预留。</summary>
        public bool HeavyAttackHeld;
        /// <summary>本帧是否按下闪避键。</summary>
        public bool EvadePressed;
        /// <summary>闪避键物理上是否仍然按住，不等同于 SprintIntent。</summary>
        public bool EvadeHeld;
        /// <summary>本帧是否松开闪避键，用于清除 Evade 缓存或 SprintIntent。</summary>
        public bool EvadeReleased;
        /// <summary>本帧是否按下防御键。</summary>
        public bool GuardPressed;
        /// <summary>防御键是否保持按住。Guard 状态主要读取该字段。</summary>
        public bool GuardHeld;
        /// <summary>本帧是否松开防御键。</summary>
        public bool GuardReleased;
        /// <summary>本帧是否按下第一技能。</summary>
        public bool Skill1Pressed;
        /// <summary>本帧是否按下第二技能，当前版本只预留。</summary>
        public bool Skill2Pressed;
        /// <summary>采集该输入快照时的游戏时间。</summary>
        public float Time;

        /// <summary>移动输入强度，限制在 0 到 1 之间，方便状态阈值判断。</summary>
        public float MoveMagnitude => Mathf.Clamp01(Move.magnitude);

        /// <summary>
        /// 判断当前移动输入是否超过状态切换阈值。
        /// </summary>
        public bool HasMoveInput(float threshold)
        {
            return MoveMagnitude > threshold;
        }
    }
}
