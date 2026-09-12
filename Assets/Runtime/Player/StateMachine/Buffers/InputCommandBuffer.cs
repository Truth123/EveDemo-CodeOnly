// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Input;

namespace ProjectEVE.Player
{
    /// <summary>
    /// 按槽位保存的轻量输入缓存。用于动作状态内部的“提前输入，窗口消费”。
    /// </summary>
    public sealed class InputCommandBuffer
    {
        /// <summary>固定槽位数组，索引必须与 InputCommandSlot 枚举保持一致。</summary>
        private readonly PlayerInputCommand[] commands = new PlayerInputCommand[4];

        /// <summary>
        /// 写入指定槽位。同槽位输入采用后输入覆盖前输入。
        /// </summary>
        public void Set(InputCommandSlot slot, PlayerInputCommand command)
        {
            commands[(int)slot] = command;
        }

        /// <summary>
        /// 尝试读取指定槽位中仍然有效的命令。
        /// </summary>
        public bool TryGet(InputCommandSlot slot, float now, out PlayerInputCommand command)
        {
            command = commands[(int)slot];
            return command.IsValid(now);
        }

        /// <summary>
        /// 标记某个槽位的命令已被消费，避免同一输入被多个窗口重复使用。
        /// </summary>
        public void Consume(InputCommandSlot slot)
        {
            PlayerInputCommand command = commands[(int)slot];
            command.Consumed = true;
            commands[(int)slot] = command;
        }

        /// <summary>
        /// 清空指定槽位。
        /// </summary>
        public void ClearSlot(InputCommandSlot slot)
        {
            commands[(int)slot] = default;
        }

        /// <summary>
        /// 清空所有槽位。进入 HitReaction、Knockdown、Dead 等强制状态时使用。
        /// </summary>
        public void Clear()
        {
            for (int i = 0; i < commands.Length; i++)
            {
                commands[i] = default;
            }
        }
    }
}
