// 文件说明：维护玩家状态机、动作窗口、输入缓存、攻击定义和状态共享上下文。
// 所属模块：玩家状态机。
// 运行影响：影响玩家动作状态推进、窗口判定、输入消费和调试显示。

using ProjectEVE.Combat.Timeline;
using ProjectEVE.Input;
using ProjectEVE.Player.Attacks;
using UnityEngine;

namespace ProjectEVE.Player
{
    /// <summary>
    /// 玩家动作输入路由器。统一处理即时输入、缓存写入、缓存消费和资源检查。
    /// </summary>
    public static class PlayerActionInputRouter
    {
        /// <summary>默认输入缓存有效期，超过后窗口不能再消费该命令。</summary>
        public const float DefaultBufferLifetime = 0.30f;

        /// <summary>
        /// 根据当前帧攻击按键解析攻击类型。轻重同帧时优先轻攻击，避免误触重攻击。
        /// </summary>
        public static AttackInputType ResolveRequestedAttackInput(PlayerStateContext context)
        {
            return context.Input.HeavyAttackPressed && !context.Input.LightAttackPressed
                ? AttackInputType.Heavy
                : AttackInputType.Light;
        }

        /// <summary>
        /// 在允许接收攻击缓存的窗口内记录轻 / 重攻击输入。
        /// </summary>
        public static void CaptureAttackInput(
            PlayerStateContext context,
            bool canReceive,
            float actionEndTime,
            float lifetime = DefaultBufferLifetime)
        {
            if (canReceive)
            {
                if (context.Input.LightAttackPressed)
                {
                    SetCommand(context, InputCommandSlot.Attack, InputCommandType.LightAttack, actionEndTime, lifetime);
                }
                else if (context.Input.HeavyAttackPressed)
                {
                    SetCommand(context, InputCommandSlot.Attack, InputCommandType.HeavyAttack, actionEndTime, lifetime);
                }
            }

            RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 在允许接收闪避缓存的窗口内记录闪避输入
        /// </summary>
        public static void CaptureEvadeInput(
            PlayerStateContext context,
            bool canReceive,
            float actionEndTime,
            float lifetime = DefaultBufferLifetime)
        {
            if (canReceive && context.Input.EvadePressed)
            {
                SetCommand(context, InputCommandSlot.Evade, InputCommandType.Evade, actionEndTime, lifetime);
            }

            RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 在允许接收技能缓存的窗口内记录 Skill1 输入。
        /// </summary>
        public static void CaptureSkillInput(
            PlayerStateContext context,
            bool canReceive,
            float actionEndTime,
            float lifetime = DefaultBufferLifetime)
        {
            if (canReceive && context.Input.Skill1Pressed)
            {
                SetCommand(context, InputCommandSlot.Skill, InputCommandType.Skill1, actionEndTime, lifetime);
            }

            RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 消费闪避输入。即时输入优先，缓存输入兜底。
        /// </summary>
        public static bool TryConsumeEvade(PlayerStateContext context, bool allowImmediate)
        {
            if (allowImmediate && context.Input.EvadePressed)
            {
                context.InputBuffer.ClearSlot(InputCommandSlot.Evade);
                RefreshBufferedFlags(context);
                return true;
            }

            if (!context.InputBuffer.TryGet(InputCommandSlot.Evade, context.Input.Time, out _))
            {
                return false;
            }

            context.InputBuffer.Consume(InputCommandSlot.Evade);
            RefreshBufferedFlags(context);
            return true;
        }

        /// <summary>
        /// 消费 Skill1 输入。即时输入优先，缓存输入兜底，并统一检查 BetaEnergy。
        /// </summary>
        public static bool TryConsumeSkill(PlayerStateContext context, bool allowImmediate, PlayerSkillActionData skillData)
        {
            if (skillData == null)
            {
                return false;
            }

            if (allowImmediate && context.Input.Skill1Pressed)
            {
                context.InputBuffer.ClearSlot(InputCommandSlot.Skill);
                RefreshBufferedFlags(context);
                return context.HasEnoughSkillEnergy(skillData.SkillCost);
            }

            if (!context.InputBuffer.TryGet(InputCommandSlot.Skill, context.Input.Time, out _))
            {
                return false;
            }

            context.InputBuffer.Consume(InputCommandSlot.Skill);
            RefreshBufferedFlags(context);
            return context.HasEnoughSkillEnergy(skillData.SkillCost);
        }

        /// <summary>
        /// 解析并消费 Skill1 输入。缺 Timeline 时不消费输入，避免用默认 cost 模拟正常玩法。
        /// </summary>
        public static bool TryConsumeSkill1(PlayerStateContext context, bool allowImmediate)
        {
            if (!HasSkillInputToConsume(context, allowImmediate))
            {
                return false;
            }

            return PlayerSkillActionResolver.TryGetSkill1(out PlayerSkillActionData skillData) &&
                   TryConsumeSkill(context, allowImmediate, skillData);
        }

        /// <summary>
        /// 检查本帧是否存在可消费的 Skill1 输入，避免无输入热路径解析 Timeline。
        /// </summary>
        private static bool HasSkillInputToConsume(PlayerStateContext context, bool allowImmediate)
        {
            return (allowImmediate && context.Input.Skill1Pressed) ||
                   context.InputBuffer.TryGet(InputCommandSlot.Skill, context.Input.Time, out _);
        }

        /// <summary>
        /// 消费攻击输入并写入 RequestedAttackInput。可选择缓存优先或即时输入优先。
        /// </summary>
        public static bool TryConsumeAttack(
            PlayerStateContext context,
            bool allowImmediate,
            bool consumeBufferFirst,
            out AttackInputType inputType)
        {
            if (consumeBufferFirst && TryConsumeBufferedAttack(context, out inputType))
            {
                context.RequestAttack(inputType);
                return true;
            }

            if (allowImmediate && (context.Input.LightAttackPressed || context.Input.HeavyAttackPressed))
            {
                inputType = ResolveRequestedAttackInput(context);
                context.InputBuffer.ClearSlot(InputCommandSlot.Attack);
                context.RequestAttack(inputType);
                RefreshBufferedFlags(context);
                return true;
            }

            if (!consumeBufferFirst && TryConsumeBufferedAttack(context, out inputType))
            {
                context.RequestAttack(inputType);
                return true;
            }

            inputType = AttackInputType.None;
            return false;
        }

        /// <summary>
        /// Guard 不写入普通动作缓存；按下当帧或按住期间都允许在合法窗口即时进入 Guard。
        /// </summary>
        public static bool TryConsumeGuard(PlayerStateContext context, bool allowImmediate)
        {
            return allowImmediate && (context.Input.GuardPressed || context.Input.GuardHeld);
        }

        /// <summary>
        /// 清理攻击缓存槽，常用于连段输入已被读取但当前节点不支持派生时。
        /// </summary>
        public static void ClearAttackBuffer(PlayerStateContext context)
        {
            context.InputBuffer.ClearSlot(InputCommandSlot.Attack);
            RefreshBufferedFlags(context);
        }

        /// <summary>
        /// 根据缓存当前有效性刷新 Debug / Animator 显示字段。
        /// </summary>
        public static void RefreshBufferedFlags(PlayerStateContext context)
        {
            context.HasBufferedEvadeInput = context.InputBuffer.TryGet(InputCommandSlot.Evade, context.Input.Time, out _);
            context.HasBufferedSkillInput = context.InputBuffer.TryGet(InputCommandSlot.Skill, context.Input.Time, out _);

            context.HasBufferedAttackInput = context.InputBuffer.TryGet(InputCommandSlot.Attack, context.Input.Time, out PlayerInputCommand attackCommand);
            context.BufferedAttackInputType = context.HasBufferedAttackInput
                ? ToAttackInputType(attackCommand.Type)
                : AttackInputType.None;
        }

        /// <summary>
        /// 尝试执行 Consume / Buffered / Attack，返回是否成功，并避免在失败路径产生不必要的状态提交。
        /// </summary>
        private static bool TryConsumeBufferedAttack(PlayerStateContext context, out AttackInputType inputType)
        {
            if (!context.InputBuffer.TryGet(InputCommandSlot.Attack, context.Input.Time, out PlayerInputCommand command))
            {
                inputType = AttackInputType.None;
                return false;
            }

            inputType = ToAttackInputType(command.Type);
            context.InputBuffer.Consume(InputCommandSlot.Attack);
            RefreshBufferedFlags(context);
            return inputType != AttackInputType.None;
        }

        /// <summary>
        /// 设置 Command 数据，并同步必要的运行时缓存或调试状态。
        /// </summary>
        private static void SetCommand(
            PlayerStateContext context,
            InputCommandSlot slot,
            InputCommandType commandType,
            float actionEndTime,
            float lifetime)
        {
            float now = context.Input.Time;
            float expireTime = Mathf.Min(now + lifetime, actionEndTime);
            context.InputBuffer.Set(slot, new PlayerInputCommand(commandType, now, expireTime));
        }

        /// <summary>
        /// 将当前对象转换为 Attack / Input / Type 数据，供其他模块读取。
        /// </summary>
        private static AttackInputType ToAttackInputType(InputCommandType commandType)
        {
            return commandType == InputCommandType.HeavyAttack ? AttackInputType.Heavy :
                commandType == InputCommandType.LightAttack ? AttackInputType.Light :
                AttackInputType.None;
        }
    }
}
