// 文件说明：维护玩家普通移动、动作位移、Root Motion 和 Boss 软互斥站位。
// 所属模块：玩家移动。
// 运行影响：影响玩家 CharacterController 位移、动作位移裁剪和动画位移反馈。

namespace ProjectEVE.Player.Movement
{
    /// <summary>
    /// 玩家 in-place 动作对应的代码驱动位移 ID。
    /// </summary>
    public enum PlayerActionMotionId
    {
        None = 0,

        EvadeForward = 10,
        EvadeBackward = 11,
        EvadeLeft = 12,
        EvadeRight = 13,

        PerfectEvadeForward = 20,
        PerfectEvadeBackward = 21,
        PerfectEvadeLeft = 22,
        PerfectEvadeRight = 23,

        HitReactionLight = 30,
        HitReactionMedium = 31,

        KnockdownBackward = 40,

        Skill1Forward = 50
    }
}
