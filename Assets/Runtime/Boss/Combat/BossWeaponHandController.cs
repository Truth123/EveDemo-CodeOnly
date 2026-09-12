// 文件说明：按 Boss HitNode 边界切换唯一武器在左右手 Socket 之间的父节点。
// 所属模块：Boss 战斗。
// 运行影响：影响 Raven 武器的持有手、武器 Hitbox、拖尾与挂点世界位置。

using ProjectEVE.Boss.AI;
using ProjectEVE.Combat.Timeline;
using System;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>Boss 唯一武器当前挂载的手。</summary>
    public enum BossWeaponHand
    {
        Right = 0,
        Left = 1
    }

    /// <summary>
    /// 根据人工绑定的 Timeline 与 HitNode，把唯一武器重挂到左右手 Socket，并始终恢复作者制作的本地姿态。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class BossWeaponHandController : MonoBehaviour
    {
        [Serializable]
        private sealed class LeftHandHitNodeBinding
        {
            [SerializeField] private BossAttackTimelineAsset attackTimeline;
            [SerializeField] private string hitNodeId = string.Empty;

            public BossAttackTimelineAsset AttackTimeline => attackTimeline;
            public string HitNodeId => hitNodeId;

            /// <summary>
            /// 判断当前动作与 HitNode 是否命中这条左手绑定。
            /// </summary>
            /// <param name="attackId">当前 Boss 动作的 ActionId。</param>
            /// <param name="activeHitNodeId">当前帧激活的 HitNode ID。</param>
            /// <returns>两个 ID 都与人工绑定相同时返回 true。</returns>
            public bool Matches(string attackId, string activeHitNodeId)
            {
                return attackTimeline != null &&
                    string.Equals(attackTimeline.ActionId, attackId, StringComparison.Ordinal) &&
                    string.Equals(hitNodeId, activeHitNodeId, StringComparison.Ordinal);
            }

            /// <summary>
            /// 写入一条供场景配置或测试使用的左手 HitNode 绑定。
            /// </summary>
            /// <param name="timeline">提供 ActionId 与 HitNode 数据的 Boss Timeline。</param>
            /// <param name="nodeId">需要切到左手的 HitNode ID。</param>
            public void Configure(BossAttackTimelineAsset timeline, string nodeId)
            {
                attackTimeline = timeline;
                hitNodeId = nodeId ?? string.Empty;
            }
        }

        [SerializeField] private Transform weaponRoot;
        [SerializeField] private Transform rightHandSocket;
        [SerializeField] private Transform leftHandSocket;
        [SerializeField] private LeftHandHitNodeBinding[] leftHandHitNodes = Array.Empty<LeftHandHitNodeBinding>();

        private Vector3 authoredLocalPosition;
        private Quaternion authoredLocalRotation;
        private Vector3 authoredLocalScale = Vector3.one;
        private bool authoredPoseCaptured;
        private bool conflictWarningIssued;

        /// <summary>武器当前实际挂载的手。</summary>
        public BossWeaponHand CurrentHand { get; private set; } = BossWeaponHand.Right;
        /// <summary>唯一武器、左右 Socket 与所有左手 HitNode 绑定是否完整有效。</summary>
        public bool IsConfigured => TryGetConfigurationError(out _) == false;

        /// <summary>唤醒时记录 canonical 武器本地姿态，并恢复默认右手。</summary>
        private void Awake()
        {
            CaptureAuthoredLocalPose();
            ResetToDefaultHand();
        }

        /// <summary>组件禁用或 Boss 停止时立即把唯一武器恢复到默认右手。</summary>
        private void OnDisable()
        {
            ResetToDefaultHand();
        }

        /// <summary>
        /// 按当前帧激活 HitNode 选择持武器手；显式左手绑定与普通 Weapon 节点冲突时左手优先。
        /// </summary>
        /// <param name="attack">当前执行的 Boss 攻击定义。</param>
        /// <param name="elapsedTime">当前攻击经过时间，单位为秒。</param>
        /// <param name="handChanged">写回本帧是否发生了父节点切换。</param>
        /// <returns>配置有效并完成本帧武器位置更新时返回 true；配置失效时返回 false。</returns>
        public bool TryApplyForAttackFrame(
            BossAttackDefinition attack,
            float elapsedTime,
            out bool handChanged)
        {
            handChanged = false;
            if (TryGetConfigurationError(out _))
            {
                return false;
            }

            BossWeaponHand desiredHand = BossWeaponHand.Right;
            bool hasLeftWeaponNode = false;
            bool hasOtherWeaponNode = false;
            if (attack != null && attack.HitNodes != null)
            {
                for (int i = 0; i < attack.HitNodes.Length; i++)
                {
                    CombatHitNodeData hitNode = attack.HitNodes[i];
                    if (hitNode.SourcePart != BossAttackSourcePart.Weapon ||
                        !hitNode.Window.Contains(elapsedTime))
                    {
                        continue;
                    }

                    if (IsLeftHandHitNode(attack.AttackId, hitNode.HitNodeId))
                    {
                        hasLeftWeaponNode = true;
                        desiredHand = BossWeaponHand.Left;
                    }
                    else
                    {
                        hasOtherWeaponNode = true;
                    }
                }
            }

            if (hasLeftWeaponNode && hasOtherWeaponNode && !conflictWarningIssued)
            {
                conflictWarningIssued = true;
                Debug.LogWarning(
                    $"BossWeaponHandController on '{name}' found simultaneous left-hand and right-hand Weapon HitNodes. Left-hand binding takes priority.",
                    this);
            }

            handChanged = SetHand(desiredHand);
            return true;
        }

        /// <summary>
        /// 把唯一武器恢复到右手 Socket，并恢复初始化时记录的本地 Position、Rotation 与 Scale。
        /// </summary>
        public void ResetToDefaultHand()
        {
            if (weaponRoot == null || rightHandSocket == null)
            {
                return;
            }

            CaptureAuthoredLocalPose();
            SetHand(BossWeaponHand.Right);
            conflictWarningIssued = false;
        }

        /// <summary>
        /// 配置唯一武器和左右手 Socket，并把当前武器本地姿态记录为共享握持姿态。
        /// </summary>
        /// <param name="canonicalWeaponRoot">唯一 Raven 武器实例的根 Transform。</param>
        /// <param name="rightSocket">默认右手 Socket。</param>
        /// <param name="leftSocket">左手攻击使用的 Socket。</param>
        public void ConfigureWeapon(
            Transform canonicalWeaponRoot,
            Transform rightSocket,
            Transform leftSocket)
        {
            weaponRoot = canonicalWeaponRoot;
            rightHandSocket = rightSocket;
            leftHandSocket = leftSocket;
            authoredPoseCaptured = false;
            CaptureAuthoredLocalPose();
            ResetToDefaultHand();
        }

        /// <summary>
        /// 替换人工左手 HitNode 绑定，供编辑器迁移与自动化测试写入。
        /// </summary>
        /// <param name="timelines">每条绑定对应的 Boss Timeline。</param>
        /// <param name="hitNodeIds">与 timelines 同下标的左手 HitNode ID。</param>
        public void ConfigureLeftHandHitNodes(
            BossAttackTimelineAsset[] timelines,
            string[] hitNodeIds)
        {
            int count = timelines != null && hitNodeIds != null
                ? Mathf.Min(timelines.Length, hitNodeIds.Length)
                : 0;
            leftHandHitNodes = new LeftHandHitNodeBinding[count];
            for (int i = 0; i < count; i++)
            {
                leftHandHitNodes[i] = new LeftHandHitNodeBinding();
                leftHandHitNodes[i].Configure(timelines[i], hitNodeIds[i]);
            }
        }

        /// <summary>
        /// 在左右 Socket 间重挂武器，并明确恢复共享本地姿态。
        /// </summary>
        /// <param name="hand">本帧需要持有武器的手。</param>
        /// <returns>实际改变父节点或当前手时返回 true。</returns>
        private bool SetHand(BossWeaponHand hand)
        {
            Transform targetSocket = hand == BossWeaponHand.Left ? leftHandSocket : rightHandSocket;
            if (weaponRoot == null || targetSocket == null || !authoredPoseCaptured)
            {
                return false;
            }

            bool changed = weaponRoot.parent != targetSocket || CurrentHand != hand;
            if (!changed)
            {
                return false;
            }

            weaponRoot.SetParent(targetSocket, false);
            weaponRoot.localPosition = authoredLocalPosition;
            weaponRoot.localRotation = authoredLocalRotation;
            weaponRoot.localScale = authoredLocalScale;
            CurrentHand = hand;
            return true;
        }

        /// <summary>记录 canonical 武器当前的作者本地姿态，后续换手不读取世界姿态。</summary>
        private void CaptureAuthoredLocalPose()
        {
            if (authoredPoseCaptured || weaponRoot == null)
            {
                return;
            }

            authoredLocalPosition = weaponRoot.localPosition;
            authoredLocalRotation = weaponRoot.localRotation;
            authoredLocalScale = weaponRoot.localScale;
            authoredPoseCaptured = true;
        }

        /// <summary>
        /// 判断指定 ActionId 与 HitNodeId 是否配置为左手节点。
        /// </summary>
        /// <param name="attackId">当前动作 ActionId。</param>
        /// <param name="hitNodeId">当前帧激活的 HitNode ID。</param>
        /// <returns>存在有效人工绑定时返回 true。</returns>
        private bool IsLeftHandHitNode(string attackId, string hitNodeId)
        {
            for (int i = 0; i < leftHandHitNodes.Length; i++)
            {
                if (leftHandHitNodes[i] != null && leftHandHitNodes[i].Matches(attackId, hitNodeId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 检查武器、Socket 与所有绑定是否能安全用于正式 Weapon HitNode。
        /// </summary>
        /// <param name="error">写回第一条配置错误；有效时为空字符串。</param>
        /// <returns>存在错误时返回 true。</returns>
        private bool TryGetConfigurationError(out string error)
        {
            if (weaponRoot == null || rightHandSocket == null || leftHandSocket == null)
            {
                error = "Weapon Root、Right Hand Socket 和 Left Hand Socket 必须全部绑定。";
                return true;
            }

            if (rightHandSocket == leftHandSocket)
            {
                error = "左右手 Socket 不能引用同一个 Transform。";
                return true;
            }

            if (leftHandHitNodes == null)
            {
                error = "Left Hand Hit Nodes 数组不可为空引用。";
                return true;
            }

            for (int i = 0; i < leftHandHitNodes.Length; i++)
            {
                LeftHandHitNodeBinding binding = leftHandHitNodes[i];
                if (binding == null || binding.AttackTimeline == null || string.IsNullOrEmpty(binding.HitNodeId))
                {
                    error = $"Left Hand Hit Nodes 第 {i + 1} 条绑定不完整。";
                    return true;
                }

                CombatHitNodeClip matchedClip = null;
                foreach (CombatHitNodeClip clip in binding.AttackTimeline.EnumerateClips<CombatHitNodeClip>())
                {
                    if (clip != null && string.Equals(clip.Name, binding.HitNodeId, StringComparison.Ordinal))
                    {
                        matchedClip = clip;
                        break;
                    }
                }

                if (matchedClip == null)
                {
                    error = $"Timeline '{binding.AttackTimeline.ActionId}' 不存在 HitNode '{binding.HitNodeId}'。";
                    return true;
                }

                if (matchedClip.SourcePart != BossAttackSourcePart.Weapon)
                {
                    error = $"HitNode '{binding.HitNodeId}' 必须使用 SourcePart=Weapon。";
                    return true;
                }
            }

            error = string.Empty;
            return false;
        }
    }
}
