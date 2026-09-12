// 文件说明：维护脱离 Boss 骨骼后的投射物或瞬时区域战斗判定。
// 所属模块：Boss 战斗。
// 运行影响：按 Trigger 生命周期或单次球形查询定位玩家，并复用 BossCombatSystem 发出正式命中。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using ProjectEVE.Player.Combat;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>独立于 Boss 骨骼的触发器判定体；视觉子节点可选，命中逻辑不读取粒子组件。</summary>
    [RequireComponent(typeof(Collider))]
    public sealed class BossDetachedHitVolume : MonoBehaviour
    {
        [SerializeField] private Collider hitCollider;
        private readonly HashSet<int> hitTargets = new HashSet<int>();
        private BossCombatSystem combatSystem;
        private BossCombatExecutionRequest request;
        private Vector3 velocity;
        private float remainingLifetime;
        private bool initialized;
        private Collider[] overlapBuffer;

        private void Awake()
        {
            if (hitCollider == null)
            {
                hitCollider = GetComponent<Collider>();
            }

            if (hitCollider != null)
            {
                hitCollider.isTrigger = true;
            }
        }

        /// <summary>写入生成瞬间复制的战斗数据和世界空间运动参数。</summary>
        /// <param name="ownerCombatSystem">负责构造正式 CombatHitData 和反馈事件的 Boss 战斗系统。</param>
        /// <param name="executionRequest">包含攻击实例、HitNode、阵营和 BossActor 的不可变命中请求。</param>
        /// <param name="worldVelocity">判定体每秒的世界空间位移。</param>
        /// <param name="lifetime">判定体从生成起可存在的秒数。</param>
        public void Initialize(
            BossCombatSystem ownerCombatSystem,
            BossCombatExecutionRequest executionRequest,
            Vector3 worldVelocity,
            float lifetime)
        {
            combatSystem = ownerCombatSystem;
            request = executionRequest;
            velocity = worldVelocity;
            remainingLifetime = Mathf.Max(0.01f, lifetime);
            hitTargets.Clear();
            initialized = combatSystem != null && request.IsValid && hitCollider != null;
            if (!initialized)
            {
                Destroy(gameObject);
            }
        }

        /// <summary>
        /// 在生成首帧按 SphereCollider 的世界中心与半径查询一次玩家，随后关闭判定并销毁实例。
        /// </summary>
        /// <returns>本次脉冲产生正式命中的目标数量；配置无效或范围内无玩家时返回 0。</returns>
        public int ExecuteInstantPulse()
        {
            if (!initialized || !(hitCollider is SphereCollider sphereCollider))
            {
                Debug.LogError($"{nameof(BossDetachedHitVolume)} InstantPulse requires an initialized SphereCollider.", this);
                FinishInstantPulse();
                return 0;
            }

            int bufferSize = Mathf.Max(4, request.OverlapBufferSize);
            if (overlapBuffer == null || overlapBuffer.Length != bufferSize)
            {
                overlapBuffer = new Collider[bufferSize];
            }

            Vector3 center = sphereCollider.transform.TransformPoint(sphereCollider.center);
            Vector3 scale = sphereCollider.transform.lossyScale;
            float radiusScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            float radius = sphereCollider.radius * radiusScale;
            int overlapCount = Physics.OverlapSphereNonAlloc(
                center,
                radius,
                overlapBuffer,
                request.HitMask,
                QueryTriggerInteraction.Collide);
            int hitCount = 0;
            for (int i = 0; i < overlapCount; i++)
            {
                if (TryProcessCollider(overlapBuffer[i], center))
                {
                    hitCount++;
                }
            }

            FinishInstantPulse();
            return hitCount;
        }

        private void Update()
        {
            if (!initialized)
            {
                return;
            }

            transform.position += velocity * Time.deltaTime;
            remainingLifetime -= Time.deltaTime;
            if (remainingLifetime <= 0f)
            {
                Destroy(gameObject);
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            TryProcessCollider(other, transform.position);
        }

        /// <summary>
        /// 把一个空间查询结果映射为唯一玩家目标，并通过 BossCombatSystem 提交正式命中。
        /// </summary>
        /// <param name="other">范围查询或 Trigger 捕获到的玩家 Collider。</param>
        /// <param name="hitOrigin">命中方向与接触反馈使用的世界空间范围中心。</param>
        /// <returns>true 表示本 Collider 产生了正式命中；false 表示目标无效、重复或被战斗规则拒绝。</returns>
        private bool TryProcessCollider(Collider other, Vector3 hitOrigin)
        {
            if (!initialized || other == null)
            {
                return false;
            }

            PlayerCombatReceiver receiver = other.GetComponentInParent<PlayerCombatReceiver>();
            if (receiver == null)
            {
                return false;
            }

            CombatHurtbox hurtbox = other.GetComponentInParent<CombatHurtbox>();
            int targetId = hurtbox != null ? hurtbox.GetInstanceID() : receiver.GetInstanceID();
            if (!hitTargets.Add(targetId))
            {
                return false;
            }

            BossCombatProcessResult result = combatSystem.ProcessDetachedHit(
                request,
                receiver,
                other,
                hitOrigin,
                targetId);
            if (result.HasHit)
            {
                request.BossActor?.NotifyBossAttackHitResult(result);
            }

            return result.HasHit;
        }

        /// <summary>关闭瞬时判定体并在当前帧结束时销毁，避免后续物理步再次触发伤害。</summary>
        private void FinishInstantPulse()
        {
            initialized = false;
            if (hitCollider != null)
            {
                hitCollider.enabled = false;
            }

            Destroy(gameObject);
        }
    }
}
