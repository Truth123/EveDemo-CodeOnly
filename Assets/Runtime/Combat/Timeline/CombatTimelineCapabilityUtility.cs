// 文件说明：集中维护 Combat Timeline authoring clip 到运行时 CapabilityId 的语义映射。
// 所属模块：Combat Timeline。
// 运行影响：为 RuntimeSpec、Validation 和 Editor 显示提供统一窗口语义来源。

namespace ProjectEVE.Combat.Timeline
{
    public readonly struct CombatTimelineCapabilityDescriptor
    {
        public readonly CombatTimelineCapabilityId Id;
        public readonly CombatTimelineCapabilityKind Kind;

        public CombatTimelineCapabilityDescriptor(
            CombatTimelineCapabilityId id,
            CombatTimelineCapabilityKind kind)
        {
            Id = id;
            Kind = kind;
        }
    }

    public static class CombatTimelineCapabilityUtility
    {
        public static bool TryDescribeClip(CombatTimelineClip clip, out CombatTimelineCapabilityDescriptor descriptor)
        {
            if (clip == null)
            {
                descriptor = default;
                return false;
            }

            if (clip.CapabilityId != CombatTimelineCapabilityId.None && TryDescribeCapability(clip.CapabilityId, out descriptor))
            {
                descriptor = ReconcileClipFamily(clip, descriptor);
                return true;
            }

            descriptor = default;
            return false;
        }

        public static string GetCapabilityLabel(CombatTimelineClip clip)
        {
            if (clip is BossReactionGateWindowClip)
            {
                return "Boss Reaction Gate";
            }

            return TryDescribeClip(clip, out CombatTimelineCapabilityDescriptor descriptor)
                ? $"{descriptor.Id} ({descriptor.Kind})"
                : "Unmapped";
        }

        public static bool IsCapabilityCompatibleWithClip(CombatTimelineClip clip)
        {
            if (clip == null || clip.CapabilityId == CombatTimelineCapabilityId.None)
            {
                return true;
            }

            if (!TryDescribeCapability(clip.CapabilityId, out CombatTimelineCapabilityDescriptor descriptor))
            {
                return false;
            }

            CombatTimelineCapabilityKind familyKind = GetClipFamilyKind(clip);
            return familyKind == CombatTimelineCapabilityKind.None || familyKind == descriptor.Kind;
        }

        public static bool TryDescribeCapability(
            CombatTimelineCapabilityId capabilityId,
            out CombatTimelineCapabilityDescriptor descriptor)
        {
            switch (capabilityId)
            {
                case CombatTimelineCapabilityId.Cancel_Commit:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Hit_Attack:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Hit);
                    return true;
                case CombatTimelineCapabilityId.Input_AttackBuffer:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.InputBuffer);
                    return true;
                case CombatTimelineCapabilityId.Cancel_AttackCombo:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Input_EvadeBuffer:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.InputBuffer);
                    return true;
                case CombatTimelineCapabilityId.Cancel_ToEvade:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Input_SkillBuffer:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.InputBuffer);
                    return true;
                case CombatTimelineCapabilityId.Cancel_ToSkill:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Cancel_ToGuard:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Cancel_AttackReset:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Cancel_MovementReturn:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Cancel_ToGuardRelease:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Cancel);
                    return true;
                case CombatTimelineCapabilityId.Defense_Invincible:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Defense);
                    return true;
                case CombatTimelineCapabilityId.Defense_PerfectEvade:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Defense);
                    return true;
                case CombatTimelineCapabilityId.Defense_PerfectGuard:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Defense);
                    return true;
                case CombatTimelineCapabilityId.Defense_GuardBlock:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Defense);
                    return true;
                case CombatTimelineCapabilityId.Armor_SuperArmor:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Armor);
                    return true;
                case CombatTimelineCapabilityId.Armor_Uninterruptible:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Armor);
                    return true;
                case CombatTimelineCapabilityId.Input_PerfectGuardChain:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.InputBuffer);
                    return true;
                case CombatTimelineCapabilityId.Reaction_Stun:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Reaction);
                    return true;
                case CombatTimelineCapabilityId.Reaction_Recovery:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Reaction);
                    return true;
                case CombatTimelineCapabilityId.Reaction_CanReturn:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Reaction);
                    return true;
                case CombatTimelineCapabilityId.Phase_Loop:
                    descriptor = new CombatTimelineCapabilityDescriptor(capabilityId, CombatTimelineCapabilityKind.Phase);
                    return true;
                default:
                    descriptor = default;
                    return false;
            }
        }

        private static CombatTimelineCapabilityDescriptor ReconcileClipFamily(
            CombatTimelineClip clip,
            CombatTimelineCapabilityDescriptor descriptor)
        {
            if (clip is CombatHitNodeClip)
            {
                return new CombatTimelineCapabilityDescriptor(CombatTimelineCapabilityId.Hit_Attack, CombatTimelineCapabilityKind.Hit);
            }

            return descriptor;
        }


        private static CombatTimelineCapabilityKind GetClipFamilyKind(CombatTimelineClip clip)
        {
            return clip switch
            {
                CombatHitNodeClip => CombatTimelineCapabilityKind.Hit,
                CombatAttackWindowClip => CombatTimelineCapabilityKind.InputBuffer,
                CombatCancelWindowClip => CombatTimelineCapabilityKind.Cancel,
                CombatDefenseWindowClip => CombatTimelineCapabilityKind.Defense,
                CombatArmorWindowClip => CombatTimelineCapabilityKind.Armor,
                CombatMotionReferenceClip => CombatTimelineCapabilityKind.Motion,
                CombatReactionWindowClip => CombatTimelineCapabilityKind.Reaction,
                BossReactionGateWindowClip => CombatTimelineCapabilityKind.Interrupt,
                CombatMarkerClip => CombatTimelineCapabilityKind.Marker,
                _ => CombatTimelineCapabilityKind.None
            };
        }
    }
}
