// 文件说明：维护 Boss 攻击执行、固定 SourcePart 拖尾、Hitbox Anchor 和正式命中入口。
// 所属模块：Boss 战斗。
// 运行影响：影响 Boss HitNode 检测、玩家受击结算和 Boss 受击反馈。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Boss.Movement;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 招式执行器。按 BossAttackDefinition 时间轴处理 HitNode 检测与玩家受击结算，动作位移通过 BossMovementSystem 提交。
    /// </summary>
    public sealed class BossAttackExecutor : MonoBehaviour
    {
        /// <summary>Boss 攻击来源阵营。</summary>
        [SerializeField] private CombatTeam attackerTeam = CombatTeam.Boss;
        /// <summary>HitNode 检测层。默认全部层。</summary>
        [SerializeField] private LayerMask hitMask = ~0;
        /// <summary>是否输出 Boss 正式命中日志。</summary>
        [SerializeField] private bool logHits = true;
        /// <summary>OverlapNonAlloc 缓冲区大小。</summary>
        [SerializeField] private int overlapBufferSize = 24;
        /// <summary>Boss 正式移动系统，负责攻击期间代码推进和 HitNode 前落点修正。</summary>
        private BossMovementSystem movementSystem;
        /// <summary>Boss 顶层 Actor，用于 PerfectGuard 反制时中断当前攻击。</summary>
        [SerializeField] private BossActor bossActor;
        /// <summary>唯一 RavenMonster_Weapon 下 SlashTrailVFX 的持续采样脚本。</summary>
        [Header("Fixed Slash Trail")]
        [SerializeField] private Behaviour weaponSlashTrailSampler;
        /// <summary>Boss 的 Ab-L-Calf-Tw1/SlashTrailVFX 下持续采样脚本。</summary>
        [SerializeField] private Behaviour leftFootSlashTrailSampler;
        /// <summary>Boss 的 Ab-R-Calf-Tw1/SlashTrailVFX 下持续采样脚本。</summary>
        [SerializeField] private Behaviour rightFootSlashTrailSampler;

        private readonly Dictionary<BossAttackSourcePart, List<BossAttackHitboxAnchor>> hitboxAnchors = new Dictionary<BossAttackSourcePart, List<BossAttackHitboxAnchor>>();
        private readonly HashSet<Behaviour> desiredSlashTrailSamplers = new HashSet<Behaviour>();
        private readonly HashSet<BossAttackSourcePart> missingSlashTrailWarnings = new HashSet<BossAttackSourcePart>();
        private readonly BossCombatSystem combatSystem = new BossCombatSystem();
        private Collider[] overlapBuffer;
        private BossHitNodeParticleVfxController hitNodeParticleVfx;
        private BossAttackCueVfxController attackCueVfx;
        private BossDetachedAttackEmitter detachedAttackEmitter;
        private BossWeaponHandController weaponHandController;
        private BossAttackDefinition currentAttack;
        private Transform target;
        private int currentAttackInstanceId;
        private float attackElapsed;
        private string currentHitNodeId = string.Empty;
        private CombatHitOutcome lastOutcome;
        private int lastAttackId;
        private bool hasLastHit;
        private bool firstActiveHitEntered;
        private bool weaponConfigurationErrorIssued;

        /// <summary>当前招式经过时间。</summary>
        public float AttackElapsed => attackElapsed;
        /// <summary>当前正在执行的招式 ID。未执行招式时为空。</summary>
        public string CurrentAttackId => currentAttack != null ? currentAttack.AttackId : string.Empty;
        /// <summary>当前正在执行的攻击实例 ID。未执行招式时为 0。</summary>
        public int CurrentAttackInstanceId => currentAttack != null ? currentAttackInstanceId : 0;
        /// <summary>当前正在处理的 HitNode。</summary>
        public string CurrentHitNodeId => currentHitNodeId;
        /// <summary>最近一次 Boss 正式命中结果。</summary>
        public CombatHitOutcome LastOutcome => lastOutcome;
        /// <summary>最近一次 Boss 正式攻击实例 ID。</summary>
        public int LastAttackId => lastAttackId;
        /// <summary>是否已经产生过 Boss 正式命中。</summary>
        public bool HasLastHit => hasLastHit;
        /// <summary>当前是否正在执行招式。</summary>
        public bool IsExecuting => currentAttack != null;
        /// <summary>
        /// 在对象唤醒时绑定依赖并初始化本组件的运行时缓存。
        /// </summary>
        private void Awake()
        {
            EnsureOverlapBuffer();
            BindReferences();
            RebuildHitboxAnchors();
        }

        /// <summary>
        /// 在组件首次添加或手动重置时绑定默认引用，方便 Inspector 配置。
        /// </summary>
        private void Reset()
        {
            BindReferences();
        }

        /// <summary>
        /// 在 Inspector 数据变更时钳制参数并刷新编辑期引用，避免运行时获得非法配置。
        /// </summary>
        private void OnValidate()
        {
            overlapBufferSize = Mathf.Max(4, overlapBufferSize);
        }

        /// <summary>绑定正式移动系统，确保攻击帧位移通过统一 MovementSystem 入口提交。</summary>
        public void BindMovementSystem(BossMovementSystem nextMovementSystem)
        {
            movementSystem = nextMovementSystem;
        }

        /// <summary>开始执行一个 Boss 招式。</summary>
        public void BeginAttack(BossAttackDefinition attackDefinition, int attackInstanceId, Transform targetTransform)
        {
            currentAttack = attackDefinition;
            currentAttackInstanceId = attackInstanceId;
            target = targetTransform;
            attackElapsed = 0f;
            currentHitNodeId = string.Empty;
            combatSystem.BeginAttack(attackInstanceId);
            RebuildHitboxAnchors();
            BindReferences();
            EnsureOverlapBuffer();
            StopSlashTrailSampling();
            hitNodeParticleVfx?.ResetTriggerState();
            detachedAttackEmitter?.BeginAttack();
            attackCueVfx?.StopCue();
            weaponHandController?.ResetToDefaultHand();
            weaponConfigurationErrorIssued = false;
            missingSlashTrailWarnings.Clear();
        }

        /// <summary>
        /// 推进当前招式。返回 true 表示招式自然到达总时长；被外部反制 / 击倒 / 死亡中断时返回 false。
        /// </summary>
        public bool Tick(float deltaTime)
        {
            if (currentAttack == null)
            {
                return true;
            }

            BossAttackDefinition attackAtTickStart = currentAttack;
            float totalDuration = attackAtTickStart.TotalDuration;
            attackElapsed += deltaTime;
            movementSystem?.TickActionMotion(deltaTime, attackElapsed);
            bool weaponHandReady = TryPrepareWeaponHand(attackAtTickStart);
            hitNodeParticleVfx?.TryPlayForAttackTime(attackAtTickStart, attackElapsed);
            ProcessHitNodes(attackAtTickStart, weaponHandReady);

            if (currentAttack != attackAtTickStart)
            {
                return false;
            }

            return attackElapsed >= totalDuration;
        }

        /// <summary>结束当前招式并清理运行时数据。</summary>
        public void EndAttack()
        {
            StopSlashTrailSampling();
            hitNodeParticleVfx?.EndAttack();
            attackCueVfx?.StopCue();
            weaponHandController?.ResetToDefaultHand();
            currentAttack = null;
            target = null;
            attackElapsed = 0f;
            currentHitNodeId = string.Empty;
            combatSystem.EndAttack();
        }

        /// <summary>
        /// 处理 Hit / Nodes 数据流，并在必要时更新运行时状态或触发后续结算。
        /// </summary>
        private void ProcessHitNodes(BossAttackDefinition attack, bool weaponHandReady)
        {
            if (attack == null || attack.HitNodes == null)
            {
                StopSlashTrailSampling();
                return;
            }

            currentHitNodeId = string.Empty;
            firstActiveHitEntered = false;
            desiredSlashTrailSamplers.Clear();

            foreach (CombatHitNodeData hitNode in attack.HitNodes)
            {
                if (currentAttack != attack)
                {
                    StopSlashTrailSampling();
                    return;
                }

                if (!hitNode.Window.Contains(attackElapsed))
                {
                    continue;
                }

                currentHitNodeId = hitNode.HitNodeId;
                if (hitNode.SourcePart == BossAttackSourcePart.Weapon && !weaponHandReady)
                {
                    ReportWeaponConfigurationError();
                    continue;
                }

                if (!firstActiveHitEntered)
                {
                    firstActiveHitEntered = true;
                    bossActor?.NotifyBossAttackFirstActiveHit();
                }
                if (hitNodeParticleVfx == null ||
                    !hitNodeParticleVfx.HasBindingForHitNode(attack.AttackId, hitNode.HitNodeId))
                {
                    CollectSlashTrailSampler(hitNode.SourcePart);
                }
                hitNodeParticleVfx?.TryPlayForHitNode(attack.AttackId, hitNode.HitNodeId);
                detachedAttackEmitter?.SpawnForHitNode(
                    attack,
                    currentAttackInstanceId,
                    hitNode,
                    transform,
                    this,
                    bossActor,
                    attackerTeam,
                    combatSystem,
                    hitMask,
                    overlapBufferSize,
                    logHits);
                ProcessHitNode(attack, hitNode);
                if (currentAttack != attack)
                {
                    StopSlashTrailSampling();
                    return;
                }
            }

            CommitSlashTrailSampling();
        }

        /// <summary>
        /// 在粒子、拖尾、Detached 与正式 Hitbox 处理前更新唯一武器的持有手。
        /// </summary>
        /// <param name="attack">当前帧开始时仍在执行的 Boss 攻击。</param>
        /// <returns>武器控制器已配置并完成本帧更新时返回 true。</returns>
        private bool TryPrepareWeaponHand(BossAttackDefinition attack)
        {
            if (weaponHandController == null)
            {
                return false;
            }

            bool ready = weaponHandController.TryApplyForAttackFrame(
                attack,
                attackElapsed,
                out bool handChanged);
            if (handChanged)
            {
                StopSlashTrailSampling();
            }

            return ready;
        }

        /// <summary>每次攻击只输出一次武器控制器配置错误，避免持续 HitNode 窗口刷屏。</summary>
        private void ReportWeaponConfigurationError()
        {
            if (weaponConfigurationErrorIssued)
            {
                return;
            }

            weaponConfigurationErrorIssued = true;
            Debug.LogError(
                $"BossAttackExecutor on '{name}' skipped a Weapon HitNode because BossWeaponHandController is missing or invalid.",
                this);
        }

        /// <summary>
        /// 处理 Hit / Node 数据流，并在必要时更新运行时状态或触发后续结算。
        /// </summary>
        private void ProcessHitNode(BossAttackDefinition attack, CombatHitNodeData hitNode)
        {
            if (hitNode.SourcePart == BossAttackSourcePart.Detached)
            {
                return;
            }

            hitboxAnchors.TryGetValue(hitNode.SourcePart, out List<BossAttackHitboxAnchor> anchors);
            BossCombatProcessResult result = combatSystem.ProcessHitNode(new BossCombatExecutionRequest(
                attack,
                currentAttackInstanceId,
                hitNode,
                anchors,
                transform,
                this,
                bossActor,
                attackerTeam,
                hitMask,
                overlapBufferSize,
                logHits,
                () => currentAttack == attack));
            ApplyCombatProcessResult(result);
        }

        private void ApplyCombatProcessResult(BossCombatProcessResult result)
        {
            if (result.HasHit)
            {
                hasLastHit = true;
                lastAttackId = result.AttackInstanceId;
                lastOutcome = result.LastOutcome;
                bossActor?.NotifyBossAttackHitResult(result);
            }
        }

        /// <summary>
        /// 确保 Overlap / Buffer 可用，不满足时创建、刷新或钳制必要的运行时数据。
        /// </summary>
        private void EnsureOverlapBuffer()
        {
            if (overlapBuffer == null || overlapBuffer.Length != overlapBufferSize)
            {
                overlapBuffer = new Collider[Mathf.Max(4, overlapBufferSize)];
            }
        }

        /// <summary>
        /// 绑定 References 依赖引用，降低场景手动配置缺失导致的运行时错误。
        /// </summary>
        private void BindReferences()
        {
            if (bossActor == null)
            {
                bossActor = GetComponent<BossActor>();
            }

            if (hitNodeParticleVfx == null)
            {
                hitNodeParticleVfx = GetComponentInChildren<BossHitNodeParticleVfxController>(true);
            }

            if (hitNodeParticleVfx == null)
            {
                hitNodeParticleVfx = GetComponentInParent<BossHitNodeParticleVfxController>();
            }

            if (attackCueVfx == null)
            {
                attackCueVfx = GetComponentInChildren<BossAttackCueVfxController>(true);
            }

            if (attackCueVfx == null)
            {
                attackCueVfx = GetComponentInParent<BossAttackCueVfxController>();
            }

            if (detachedAttackEmitter == null)
            {
                detachedAttackEmitter = GetComponentInChildren<BossDetachedAttackEmitter>(true);
            }

            if (detachedAttackEmitter == null)
            {
                detachedAttackEmitter = GetComponentInParent<BossDetachedAttackEmitter>();
            }

            if (weaponHandController == null)
            {
                weaponHandController = GetComponentInChildren<BossWeaponHandController>(true);
            }

            if (weaponHandController == null)
            {
                weaponHandController = GetComponentInParent<BossWeaponHandController>();
            }
        }

        /// <summary>组件禁用时停止拖尾与攻击提示表现，并把唯一武器恢复到默认右手。</summary>
        private void OnDisable()
        {
            StopSlashTrailSampling();
            attackCueVfx?.StopCue();
            weaponHandController?.ResetToDefaultHand();
        }

        /// <summary>
        /// 按当前 HitNode 的 SourcePart 收集固定拖尾；仅 Weapon、LeftFoot、RightFoot 会产生拖尾。
        /// </summary>
        /// <param name="sourcePart">当前激活 HitNode 的检测体来源部位。</param>
        private void CollectSlashTrailSampler(BossAttackSourcePart sourcePart)
        {
            Behaviour sampler = sourcePart switch
            {
                BossAttackSourcePart.Weapon => weaponSlashTrailSampler,
                BossAttackSourcePart.LeftFoot => leftFootSlashTrailSampler,
                BossAttackSourcePart.RightFoot => rightFootSlashTrailSampler,
                _ => null
            };

            if (sampler != null)
            {
                desiredSlashTrailSamplers.Add(sampler);
                return;
            }

            if (sourcePart != BossAttackSourcePart.Weapon &&
                sourcePart != BossAttackSourcePart.LeftFoot &&
                sourcePart != BossAttackSourcePart.RightFoot)
            {
                return;
            }

            if (missingSlashTrailWarnings.Add(sourcePart))
            {
                Debug.LogError(
                    $"{nameof(BossAttackExecutor)} on '{name}' cannot play the fixed {sourcePart} slash trail because its sampler is not assigned.",
                    this);
            }
        }

        /// <summary>提交当前帧的 SourcePart 聚合结果，并关闭本帧已不再需要的固定采样器。</summary>
        private void CommitSlashTrailSampling()
        {
            SetSlashTrailSampling(weaponSlashTrailSampler);
            SetSlashTrailSampling(leftFootSlashTrailSampler);
            SetSlashTrailSampling(rightFootSlashTrailSampler);
        }

        /// <summary>
        /// 依据当前帧聚合集合切换单个固定拖尾采样器。
        /// </summary>
        /// <param name="sampler">需要提交启停状态的固定拖尾采样脚本。</param>
        private void SetSlashTrailSampling(Behaviour sampler)
        {
            if (sampler == null)
            {
                return;
            }

            bool shouldSample = desiredSlashTrailSamplers.Contains(sampler);
            if (sampler.enabled != shouldSample)
            {
                sampler.enabled = shouldSample;
            }
        }

        /// <summary>停止三个固定拖尾采样器，让已生成的轨迹按 VFX 自身生命周期自然淡出。</summary>
        private void StopSlashTrailSampling()
        {
            desiredSlashTrailSamplers.Clear();
            if (weaponSlashTrailSampler != null)
            {
                weaponSlashTrailSampler.enabled = false;
            }

            if (leftFootSlashTrailSampler != null)
            {
                leftFootSlashTrailSampler.enabled = false;
            }

            if (rightFootSlashTrailSampler != null)
            {
                rightFootSlashTrailSampler.enabled = false;
            }
        }

        /// <summary>
        /// 执行 Rebuild / Hitbox / Anchors 相关逻辑，并维护 Boss 战斗 模块的运行时一致性。
        /// </summary>
        private void RebuildHitboxAnchors()
        {
            hitboxAnchors.Clear();
            BossAttackHitboxAnchor[] anchors = GetComponentsInChildren<BossAttackHitboxAnchor>(true);
            foreach (BossAttackHitboxAnchor anchor in anchors)
            {
                if (anchor == null || !anchor.IsUsable)
                {
                    continue;
                }

                if (!hitboxAnchors.TryGetValue(anchor.SourcePart, out List<BossAttackHitboxAnchor> sourceAnchors))
                {
                    sourceAnchors = new List<BossAttackHitboxAnchor>();
                    hitboxAnchors.Add(anchor.SourcePart, sourceAnchors);
                }

                sourceAnchors.Add(anchor);
            }
        }

    }
}
