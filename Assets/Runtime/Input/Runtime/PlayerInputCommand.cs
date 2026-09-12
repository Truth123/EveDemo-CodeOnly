// 文件说明：维护 Unity 新输入系统读取和玩家输入快照。
// 所属模块：输入系统。
// 运行影响：影响状态机输入来源、移动视角输入和动作按键采样。

using System;

namespace ProjectEVE.Input
{
    /// <summary>
    /// 一个可被状态机缓存和消费的输入命令。
    /// </summary>
    [Serializable]
    public struct PlayerInputCommand
    {
        /// <summary>命令语义类型，例如轻攻击、闪避或技能。</summary>
        public InputCommandType Type;
        /// <summary>命令进入缓存或被记录时的游戏时间。</summary>
        public float Time;
        /// <summary>命令过期时间，超过后不再允许被消费。</summary>
        public float ExpireTime;
        /// <summary>命令是否已经被某个状态窗口消费。</summary>
        public bool Consumed;

        /// <summary>
        /// 创建一个带过期时间的输入命令。
        /// </summary>
        public PlayerInputCommand(InputCommandType type, float time, float expireTime)
        {
            Type = type;
            Time = time;
            ExpireTime = expireTime;
            Consumed = false;
        }

        /// <summary>
        /// 判断命令当前是否仍然可用。
        /// </summary>
        public bool IsValid(float now)
        {
            return Type != InputCommandType.None && !Consumed && now <= ExpireTime;
        }
    }
}
