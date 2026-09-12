// 文件说明：定义 Combat Timeline 运行时资产、Clip 类型、查询转换和校验逻辑。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 动作窗口、HitNode、Reaction 和后续能力快照解析。

using ProjectEVE.Boss.AI;
using ProjectEVE.Combat;
using ProjectEVE.Player;
using ProjectEVE.Player.Movement;
using ProjectEVE.Player.Windows;
using System;
using UnityEngine;

namespace ProjectEVE.Combat.Timeline
{
    public enum CombatTimelineOwner
    {
        Player = 0,
        Boss = 1
    }

    public enum CombatTimelineActionKind
    {
        PlayerAttack = 0,
        PlayerSkill = 1,
        PlayerEvade = 2,
        PlayerGuard = 3,
        BossAttack = 4,
        PlayerReaction = 5,
        BossReaction = 6,
        /// <summary>不创建攻击实例，仅驱动 Boss 动画与 MotionProfile 的重定位动作。</summary>
        BossReposition = 7
    }

    public enum CombatTimelineReactionType
    {
        None = 0,
        HitReaction = 1,
        GuardHit = 2,
        Knockdown = 3,
        Dead = 4
    }

    public enum CombatTimelineReactionDirection
    {
        Any = 0,
        Front = 1,
        Back = 2,
        Left = 3,
        Right = 4
    }

    public enum CombatTimelineTrackKind
    {
        Phase = 0,
        HitNode = 1,
        InputBuffer = 2,
        Cancel = 3,
        Defense = 4,
        Armor = 5,
        PlayerMotion = 6,
        BossMotionWarp = 7,
        BossCodeMove = 8,
        RootMotionSuppress = 9,
        Marker = 10,
        Reaction = 11,
        AnimationPreview = 12,
        Interrupt = 13
    }

    public enum CombatTimelineClipKind
    {
        Phase = 0,
        AttackWindow = 1,
        HitNode = 2,
        CancelWindow = 3,
        DefenseWindow = 4,
        ArmorWindow = 5,
        PlayerMotion = 6,
        BossMotionWarp = 7,
        BossCodeMove = 8,
        RootMotionSuppress = 9,
        Marker = 10,
        Reaction = 11,
        AnimationPreview = 12,
        BossReactionGateWindow = 14
    }

    public enum BossReactionGatePolicy
    {
        AllowInterrupt = 0,
        BlockInterrupt = 1
    }

    public enum BossReactionGateBlockedOutcome
    {
        Any = 0,
        Knockdown = 1
    }

    public enum CombatTimelineCapabilityId
    {
        None = 0,
        Cancel_Commit = 1,
        Hit_Attack = 2,
        Input_AttackBuffer = 3,
        Cancel_AttackCombo = 4,
        Input_EvadeBuffer = 5,
        Cancel_ToEvade = 6,
        Input_SkillBuffer = 7,
        Cancel_ToSkill = 8,
        Cancel_ToGuard = 9,
        Cancel_AttackReset = 10,
        Cancel_MovementReturn = 11,
        Defense_Invincible = 12,
        Defense_PerfectEvade = 13,
        Defense_PerfectGuard = 14,
        Defense_GuardBlock = 15,
        Armor_SuperArmor = 16,
        Input_PerfectGuardChain = 19,
        Reaction_Stun = 21,
        Reaction_Recovery = 22,
        Reaction_CanReturn = 23,
        Phase_Loop = 24,
        Armor_Uninterruptible = 25,
        Cancel_ToGuardRelease = 27
    }

    [Serializable]
    public sealed class CombatTimelineTrack
    {
        public string Name;
        public CombatTimelineTrackKind TrackKind;
        public bool Locked;
        public bool Muted;

        [SerializeReference]
        public CombatTimelineClip[] Clips = Array.Empty<CombatTimelineClip>();

        /// <summary>
        /// 创建 CombatTimelineTrack 实例，并准备 Combat Timeline 模块需要的初始状态。
        /// </summary>
        public CombatTimelineTrack()
        {
        }

        /// <summary>
        /// 创建 CombatTimelineTrack 实例，并准备 Combat Timeline 模块需要的初始状态。
        /// </summary>
        public CombatTimelineTrack(string name, CombatTimelineTrackKind trackKind)
        {
            Name = name;
            TrackKind = trackKind;
        }
    }

    [Serializable]
    public abstract class CombatTimelineClip
    {
        public string Name;
        public CombatTimelineCapabilityId CapabilityId;
        public float StartTime;
        public float EndTime;

        public abstract CombatTimelineClipKind ClipKind { get; }

        public float Duration => Mathf.Max(0f, EndTime - StartTime);

        /// <summary>
        /// 将当前对象转换为 Action / Window 数据，供其他模块读取。
        /// </summary>
        public ActionWindow ToActionWindow()
        {
            return new ActionWindow(string.IsNullOrEmpty(Name) ? CapabilityId.ToString() : Name, StartTime, EndTime);
        }
    }

    [Serializable]
    public sealed class CombatPhaseClip : CombatTimelineClip
    {
        public PlayerStatePhase Phase = PlayerStatePhase.Start;

        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.Phase;
    }

    [Serializable]
    public sealed class CombatAttackWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.AttackWindow;
    }

    [Serializable]
    public sealed class CombatCancelWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.CancelWindow;
    }

    [Serializable]
    public sealed class CombatDefenseWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.DefenseWindow;
    }

    [Serializable]
    public sealed class CombatArmorWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.ArmorWindow;
    }

    [Serializable]
    public sealed class CombatHitNodeClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.HitNode;

        public CombatAttackType AttackType = CombatAttackType.LightAttack;
        public CombatReactionIntent ReactionIntent = CombatReactionIntent.HitReaction;
        public float Damage;
        public float PoiseDamage;
        public float GuardDamage;

        public bool CanBeGuarded = true;
        public bool CanBePerfectGuarded = true;
        public bool CanBePerfectEvaded = true;
        public int MaxHitsPerTarget = 1;
        public float EffectiveRange;
        public float EffectiveAngle;
        public bool TriggersPerfectGuardBossStagger;

        public int HitIndex;
        public BossAttackSourcePart SourcePart = BossAttackSourcePart.Weapon;
    }

    [Serializable]
    public sealed class CombatReactionWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.Reaction;

        public CombatTimelineReactionType ReactionType = CombatTimelineReactionType.None;
        public CombatTimelineReactionDirection Direction = CombatTimelineReactionDirection.Any;
        public float StunDuration;
        public float RecoveryStartTime;
        public float CanReturnTime;
        public PlayerActionMotionId ReactionMotionId = PlayerActionMotionId.None;
    }

    [Serializable]
    public sealed class BossReactionGateWindowClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.BossReactionGateWindow;

        public CombatAttackType[] AttackTypes = Array.Empty<CombatAttackType>();
        public BossReactionGatePolicy Policy = BossReactionGatePolicy.AllowInterrupt;
        public BossReactionGateBlockedOutcome BlockedOutcome = BossReactionGateBlockedOutcome.Any;
    }

    [Serializable]
    public sealed class CombatMotionReferenceClip : CombatTimelineClip
    {
        public CombatTimelineClipKind MotionKind = CombatTimelineClipKind.PlayerMotion;
        public override CombatTimelineClipKind ClipKind => MotionKind;

        public PlayerActionMotionId PlayerMotionId = PlayerActionMotionId.None;
        public string BossAttackId;
        public string ProfileWindowName;
    }

    [Serializable]
    public sealed class CombatAnimationClipWindow : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.AnimationPreview;

        public AnimationClip PreviewAnimationClip;
        public string AnimatorStateName;
        public float ClipStartOffset;
        public float ClipEndOffset;
        public float ClipSpeed = 1f;
        public bool LoopPreview;
    }

    [Serializable]
    public sealed class CombatMarkerClip : CombatTimelineClip
    {
        public override CombatTimelineClipKind ClipKind => CombatTimelineClipKind.Marker;
        public string MarkerId;
    }
}
