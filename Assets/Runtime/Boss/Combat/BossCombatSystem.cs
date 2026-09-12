// 文件说明：维护 Boss 正式命中查询、语义过滤、去重和命中发出。
// 所属模块：Boss 战斗。
// 运行影响：影响 Boss HitNode 对玩家的正式命中结算和 PerfectGuard 反制。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using ProjectEVE.Feedback;
using ProjectEVE.Player.Combat;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 正式命中执行系统。由 BossAttackExecutor 持有并调用，不作为场景组件挂载。
    /// </summary>
    public sealed class BossCombatSystem
    {
        private readonly Dictionary<string, int> hitCounts = new Dictionary<string, int>();
        private readonly HashSet<string> missingAnchorWarnings = new HashSet<string>();
        private Collider[] overlapBuffer;

        /// <summary>开始一次正式攻击命中上下文，并清理本次攻击去重状态。</summary>
        public void BeginAttack(int attackInstanceId)
        {
            hitCounts.Clear();
        }

        /// <summary>结束当前正式攻击命中上下文。</summary>
        public void EndAttack()
        {
            hitCounts.Clear();
        }

        /// <summary>处理一个当前帧激活的 HitNode，并返回需要同步回 Executor 的运行时事实。</summary>
        public BossCombatProcessResult ProcessHitNode(BossCombatExecutionRequest request)
        {
            if (!request.IsValid)
            {
                return BossCombatProcessResult.Empty;
            }

            EnsureOverlapBuffer(request.OverlapBufferSize);

            if (request.Anchors == null || request.Anchors.Count == 0)
            {
                return WarnMissingAnchor(request);
            }

            BossCombatProcessResult aggregate = BossCombatProcessResult.Empty;
            for (int anchorIndex = 0; anchorIndex < request.Anchors.Count; anchorIndex++)
            {
                if (!request.CanContinue())
                {
                    return aggregate;
                }

                BossAttackHitboxAnchor anchor = request.Anchors[anchorIndex];
                if (anchor == null || !anchor.IsActiveForCombat)
                {
                    continue;
                }

                Vector3 center = anchor.BoxCollider.transform.TransformPoint(anchor.BoxCollider.center);
                int hitCount = QueryHitboxAnchor(anchor, center, request.HitMask);
                int boundedHitCount = Mathf.Min(hitCount, overlapBuffer.Length);
                for (int hitIndex = 0; hitIndex < boundedHitCount; hitIndex++)
                {
                    if (!request.CanContinue())
                    {
                        return aggregate;
                    }

                    Collider hitCollider = overlapBuffer[hitIndex];
                    if (hitCollider == null)
                    {
                        continue;
                    }

                    PlayerCombatReceiver receiver = hitCollider.GetComponentInParent<PlayerCombatReceiver>();
                    if (receiver == null)
                    {
                        continue;
                    }

                    BossCombatProcessResult semanticResult = EvaluateSemantic(request, receiver.transform);
                    aggregate = semanticResult;
                    if (!semanticResult.SemanticAccepted)
                    {
                        continue;
                    }

                    CombatHurtbox hurtbox = hitCollider.GetComponentInParent<CombatHurtbox>();
                    int targetInstanceId = ResolveTargetInstanceId(receiver, hurtbox);
                    string key = $"{request.AttackInstanceId}:{request.HitNode.HitNodeId}:{targetInstanceId}";
                    hitCounts.TryGetValue(key, out int targetHitCount);
                    if (targetHitCount >= request.HitNode.MaxHitsPerTarget)
                    {
                        continue;
                    }

                    hitCounts[key] = targetHitCount + 1;
                    aggregate = EmitHit(request, receiver, hitCollider, center, semanticResult);
                    if (!request.CanContinue())
                    {
                        return aggregate;
                    }
                }
            }

            return aggregate;
        }

        /// <summary>
        /// 处理独立判定体已经通过 Collider 确认的玩家命中；跳过基于 Boss 根节点的距离角度过滤，继续复用正式结算和反馈。
        /// </summary>
        /// <param name="request">生成瞬间复制的攻击实例与 HitNode 请求。</param>
        /// <param name="receiver">触发器命中的玩家接收器。</param>
        /// <param name="hitCollider">用于计算接触位置的玩家 Collider。</param>
        /// <param name="hitOrigin">投射物或驻留区域本帧的世界空间中心。</param>
        /// <param name="targetInstanceId">由独立判定体解析的 Hurtbox 或 Receiver 实例 ID。</param>
        /// <returns>实际发出命中时返回 HasHit；重复目标或无效请求返回空结果。</returns>
        public BossCombatProcessResult ProcessDetachedHit(
            BossCombatExecutionRequest request,
            PlayerCombatReceiver receiver,
            Collider hitCollider,
            Vector3 hitOrigin,
            int targetInstanceId)
        {
            if (!request.IsValid || receiver == null)
            {
                return BossCombatProcessResult.Empty;
            }

            BossCombatProcessResult semanticResult = EvaluateSemantic(request, receiver.transform);
            if (!semanticResult.SemanticAccepted)
            {
                return semanticResult;
            }

            string key = $"{request.AttackInstanceId}:{request.HitNode.HitNodeId}:{targetInstanceId}";
            hitCounts.TryGetValue(key, out int targetHitCount);
            if (targetHitCount >= request.HitNode.MaxHitsPerTarget)
            {
                return BossCombatProcessResult.Empty;
            }

            hitCounts[key] = targetHitCount + 1;
            return EmitHit(
                request,
                receiver,
                hitCollider,
                hitOrigin,
                semanticResult,
                hitOrigin);
        }

        private static int ResolveTargetInstanceId(PlayerCombatReceiver receiver, CombatHurtbox hurtbox)
        {
            return hurtbox != null ? hurtbox.GetInstanceID() : receiver.GetInstanceID();
        }

        private BossCombatProcessResult WarnMissingAnchor(BossCombatExecutionRequest request)
        {
            string warningKey = $"{request.AttackId}:{request.HitNode.HitNodeId}:{request.HitNode.SourcePart}";
            if (missingAnchorWarnings.Add(warningKey))
            {
                Debug.LogWarning(
                    $"Boss attack hitbox anchor missing: attack={request.AttackId}, node={request.HitNode.HitNodeId}, sourcePart={request.HitNode.SourcePart}. This HitNode will not hit.",
                    request.Source);
            }

            return BossCombatProcessResult.FromSemantic(
                false,
                0f,
                0f);
        }

        private int QueryHitboxAnchor(BossAttackHitboxAnchor anchor, Vector3 center, LayerMask hitMask)
        {
            BoxCollider boxCollider = anchor.BoxCollider;
            Vector3 halfExtents = Vector3.Scale(boxCollider.size, Abs(boxCollider.transform.lossyScale)) * 0.5f;
            return Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                overlapBuffer,
                boxCollider.transform.rotation,
                hitMask,
                QueryTriggerInteraction.Collide);
        }

        private BossCombatProcessResult EvaluateSemantic(BossCombatExecutionRequest request, Transform receiverTransform)
        {
            if (receiverTransform == null)
            {
                return BossCombatProcessResult.FromSemantic(false, 0f, 0f);
            }

            Vector3 toTarget = receiverTransform.position - request.AttackerTransform.position;
            toTarget.y = 0f;
            float semanticDistance = toTarget.magnitude;
            float semanticAngle = toTarget.sqrMagnitude > 0.0001f
                ? Vector3.Angle(request.AttackerTransform.forward, toTarget.normalized)
                : 0f;

            if (toTarget.sqrMagnitude <= 0.0001f)
            {
                return BossCombatProcessResult.FromSemantic(true, semanticDistance, semanticAngle);
            }

            if (request.HitNode.EffectiveRange > 0f && semanticDistance > request.HitNode.EffectiveRange)
            {
                return BossCombatProcessResult.FromSemantic(
                    false,
                    semanticDistance,
                    semanticAngle);
            }

            if (request.HitNode.EffectiveAngle > 0f && semanticAngle > request.HitNode.EffectiveAngle)
            {
                return BossCombatProcessResult.FromSemantic(
                    false,
                    semanticDistance,
                    semanticAngle);
            }

            return BossCombatProcessResult.FromSemantic(true, semanticDistance, semanticAngle);
        }

        private BossCombatProcessResult EmitHit(
            BossCombatExecutionRequest request,
            PlayerCombatReceiver receiver,
            Collider hitCollider,
            Vector3 center,
            BossCombatProcessResult semanticResult,
            Vector3? directionOrigin = null)
        {
            GameObject receiverObject = receiver.gameObject;
            Vector3 origin = directionOrigin ?? request.AttackerTransform.position;
            Vector3 direction = receiver.transform.position - origin;
            direction.y = 0f;
            float hitDistance = direction.magnitude;
            if (direction.sqrMagnitude <= 0.0001f)
            {
                direction = request.AttackerTransform.forward;
            }

            CombatHitData hit = new CombatHitData
            {
                AttackId = request.AttackInstanceId,
                AttackerTeam = request.AttackerTeam,
                AttackType = request.HitNode.AttackType,
                ReactionIntent = request.HitNode.ReactionIntent,
                Damage = request.HitNode.Damage,
                PoiseDamage = request.HitNode.PoiseDamage,
                GuardDamage = request.HitNode.GuardDamage,
                HitPosition = hitCollider != null ? hitCollider.ClosestPoint(center) : receiver.transform.position,
                HitDirection = direction.normalized,
                CanBeGuarded = request.HitNode.CanBeGuarded,
                CanBePerfectGuarded = request.HitNode.CanBePerfectGuarded,
                CanBePerfectEvaded = request.HitNode.CanBePerfectEvaded
            };

            CombatHitResult result = receiver.ReceiveHit(hit, request.Source);
            CombatFeedbackBus.Raise(CombatFeedbackEvent.FromHit(hit, result, request.Source, receiverObject));

            if (result.Outcome == CombatHitOutcome.PerfectGuard)
            {
                request.BossActor?.ResolvePerfectGuardAgainstBoss(request.HitNode);
            }

            if (request.LogHits)
            {
                Debug.Log(
                    $"Boss hit: attack={request.AttackId}, node={request.HitNode.HitNodeId}, intent={request.HitNode.ReactionIntent}, outcome={result.Outcome}",
                    request.Source);
            }

            return BossCombatProcessResult.FromHit(
                semanticResult.SemanticDistance,
                semanticResult.SemanticAngle,
                hitDistance,
                request.HitNode.HitNodeId,
                result.Outcome,
                request.AttackInstanceId);
        }

        private void EnsureOverlapBuffer(int overlapBufferSize)
        {
            int size = Mathf.Max(4, overlapBufferSize);
            if (overlapBuffer == null || overlapBuffer.Length != size)
            {
                overlapBuffer = new Collider[size];
            }
        }

        private static Vector3 Abs(Vector3 value)
        {
            return new Vector3(Mathf.Abs(value.x), Mathf.Abs(value.y), Mathf.Abs(value.z));
        }
    }

    /// <summary>
    /// BossCombatSystem 处理一个 HitNode 所需的正式运行时输入。
    /// </summary>
    public readonly struct BossCombatExecutionRequest
    {
        /// <summary>创建正式 Boss 命中执行请求。</summary>
        public BossCombatExecutionRequest(
            BossAttackDefinition attack,
            int attackInstanceId,
            CombatHitNodeData hitNode,
            IReadOnlyList<BossAttackHitboxAnchor> anchors,
            Transform attackerTransform,
            Component source,
            BossActor bossActor,
            CombatTeam attackerTeam,
            LayerMask hitMask,
            int overlapBufferSize,
            bool logHits,
            Func<bool> canContinue)
        {
            Attack = attack;
            AttackInstanceId = Mathf.Max(0, attackInstanceId);
            HitNode = hitNode;
            Anchors = anchors;
            AttackerTransform = attackerTransform;
            Source = source;
            BossActor = bossActor;
            AttackerTeam = attackerTeam;
            HitMask = hitMask;
            OverlapBufferSize = Mathf.Max(4, overlapBufferSize);
            LogHits = logHits;
            CanContinue = canContinue;
        }

        /// <summary>当前攻击定义。</summary>
        public BossAttackDefinition Attack { get; }
        /// <summary>当前攻击实例 ID。</summary>
        public int AttackInstanceId { get; }
        /// <summary>当前帧需要处理的 HitNode。</summary>
        public CombatHitNodeData HitNode { get; }
        /// <summary>当前 HitNode 来源部位对应的手动 Hitbox Anchor。</summary>
        public IReadOnlyList<BossAttackHitboxAnchor> Anchors { get; }
        /// <summary>Boss 攻击者 Transform。</summary>
        public Transform AttackerTransform { get; }
        /// <summary>命中来源组件，用于 ReceiveHit、反馈事件和日志上下文。</summary>
        public Component Source { get; }
        /// <summary>Boss 顶层 Actor，用于 PerfectGuard 反制。</summary>
        public BossActor BossActor { get; }
        /// <summary>攻击来源阵营。</summary>
        public CombatTeam AttackerTeam { get; }
        /// <summary>正式命中查询层。</summary>
        public LayerMask HitMask { get; }
        /// <summary>OverlapNonAlloc 缓冲区大小。</summary>
        public int OverlapBufferSize { get; }
        /// <summary>是否保留旧正式命中日志行为。</summary>
        public bool LogHits { get; }
        /// <summary>调用方当前攻击是否仍然有效，用于 PerfectGuard 等中断后停止继续命中。</summary>
        public Func<bool> CanContinue { get; }
        /// <summary>攻击 ID。</summary>
        public string AttackId => Attack != null ? Attack.AttackId : string.Empty;
        /// <summary>请求是否具备最低正式执行条件。</summary>
        public bool IsValid => Attack != null &&
            AttackerTransform != null &&
            Source != null &&
            AttackInstanceId > 0 &&
            CanContinue != null &&
            !string.IsNullOrEmpty(HitNode.HitNodeId);
    }

    /// <summary>
    /// BossCombatSystem 返回给 BossAttackExecutor 的既有运行时事实。
    /// </summary>
    public readonly struct BossCombatProcessResult
    {
        /// <summary>空处理结果。</summary>
        public static readonly BossCombatProcessResult Empty = new BossCombatProcessResult(
            false,
            false,
            0f,
            0f,
            false,
            0f,
            string.Empty,
            CombatHitOutcome.None,
            0,
            CombatHitOutcome.None);

        private BossCombatProcessResult(
            bool hasSemanticResult,
            bool semanticAccepted,
            float semanticDistance,
            float semanticAngle,
            bool hasHit,
            float hitDistance,
            string hitNodeId,
            CombatHitOutcome hitOutcome,
            int attackInstanceId,
            CombatHitOutcome lastOutcome)
        {
            HasSemanticResult = hasSemanticResult;
            SemanticAccepted = semanticAccepted;
            SemanticDistance = semanticDistance;
            SemanticAngle = semanticAngle;
            HasHit = hasHit;
            HitDistance = hitDistance;
            HitNodeId = hitNodeId ?? string.Empty;
            HitOutcome = hitOutcome;
            AttackInstanceId = attackInstanceId;
            LastOutcome = lastOutcome;
        }

        /// <summary>创建语义过滤处理结果。</summary>
        public static BossCombatProcessResult FromSemantic(
            bool accepted,
            float distance,
            float angle)
        {
            return new BossCombatProcessResult(
                true,
                accepted,
                distance,
                angle,
                false,
                0f,
                string.Empty,
                CombatHitOutcome.None,
                0,
                CombatHitOutcome.None);
        }

        /// <summary>创建正式命中处理结果。</summary>
        public static BossCombatProcessResult FromHit(
            float semanticDistance,
            float semanticAngle,
            float hitDistance,
            string hitNodeId,
            CombatHitOutcome hitOutcome,
            int attackInstanceId)
        {
            return new BossCombatProcessResult(
                true,
                true,
                semanticDistance,
                semanticAngle,
                true,
                hitDistance,
                hitNodeId,
                hitOutcome,
                attackInstanceId,
                hitOutcome);
        }

        /// <summary>本次处理是否产生语义过滤事实。</summary>
        public bool HasSemanticResult { get; }
        /// <summary>语义过滤是否通过。</summary>
        public bool SemanticAccepted { get; }
        /// <summary>最近语义过滤距离。</summary>
        public float SemanticDistance { get; }
        /// <summary>最近语义过滤角度。</summary>
        public float SemanticAngle { get; }
        /// <summary>本次处理是否发出正式命中。</summary>
        public bool HasHit { get; }
        /// <summary>最近正式命中距离。</summary>
        public float HitDistance { get; }
        /// <summary>最近正式命中 HitNode ID。</summary>
        public string HitNodeId { get; }
        /// <summary>最近正式命中结果。</summary>
        public CombatHitOutcome HitOutcome { get; }
        /// <summary>最近正式命中攻击实例 ID。</summary>
        public int AttackInstanceId { get; }
        /// <summary>最近正式命中总结果。</summary>
        public CombatHitOutcome LastOutcome { get; }
    }
}
