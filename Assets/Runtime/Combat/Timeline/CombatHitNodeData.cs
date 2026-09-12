// 文件说明：定义 Combat Timeline 解析后的唯一运行时 HitNode 战斗语义数据。
// 所属模块：Combat Timeline。
// 运行影响：影响 Player/Boss 命中、伤害、防御、反应和 HitNode 激活查询。

using ProjectEVE.Boss.AI;
using ProjectEVE.Combat;
using ProjectEVE.Player.Attacks;
using ProjectEVE.Player.Windows;
using System;

namespace ProjectEVE.Combat.Timeline
{
    /// <summary>
    /// HitNode 运行时唯一战斗语义数据。Provider Data 持有详情，RuntimeSpec 只用它判断当前是否激活。
    /// </summary>
    public readonly struct CombatHitNodeData
    {
        public readonly string HitNodeId;
        public readonly int HitIndex;
        public readonly ActionWindow Window;
        public readonly CombatAttackType AttackType;
        public readonly CombatReactionIntent ReactionIntent;
        public readonly float Damage;
        public readonly float PoiseDamage;
        public readonly float GuardDamage;
        public readonly bool CanBeGuarded;
        public readonly bool CanBePerfectGuarded;
        public readonly bool CanBePerfectEvaded;
        public readonly int MaxHitsPerTarget;
        public readonly float EffectiveRange;
        public readonly float EffectiveAngle;
        public readonly bool TriggersPerfectGuardBossStagger;
        public readonly BossAttackSourcePart SourcePart;

        public CombatHitNodeData(CombatHitNodeClip clip, int nodeIndex)
        {
            string nodeId = string.IsNullOrEmpty(clip.Name) ? $"Hit_{nodeIndex}" : clip.Name;
            HitNodeId = nodeId;
            HitIndex = clip.HitIndex > 0 ? clip.HitIndex : nodeIndex;
            Window = new ActionWindow(nodeId, clip.StartTime, clip.EndTime);
            AttackType = clip.AttackType;
            ReactionIntent = clip.ReactionIntent;
            Damage = clip.Damage;
            PoiseDamage = clip.PoiseDamage;
            GuardDamage = clip.GuardDamage;
            CanBeGuarded = clip.CanBeGuarded;
            CanBePerfectGuarded = clip.CanBePerfectGuarded;
            CanBePerfectEvaded = clip.CanBePerfectEvaded;
            MaxHitsPerTarget = Math.Max(1, clip.MaxHitsPerTarget);
            EffectiveRange = Math.Max(0f, clip.EffectiveRange);
            EffectiveAngle = Math.Max(0f, clip.EffectiveAngle);
            TriggersPerfectGuardBossStagger = clip.TriggersPerfectGuardBossStagger;
            SourcePart = clip.SourcePart;
        }

        public bool Contains(float elapsedTime)
        {
            return Window.Contains(elapsedTime);
        }
    }
}