// 文件说明：维护 Boss HitNode 到可提前生成的一次性世界空间粒子特效人工绑定。
// 所属模块：Boss 战斗表现。
// 运行影响：按 HitNode 起点与可选提前量生成独立粒子实例，不影响 HitNode 检测、伤害、受击反应或位移。

using ProjectEVE.Boss.AI;
using ProjectEVE.Combat.Timeline;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// 一次性粒子实例在 HitNode 首帧采用的方向基准。
    /// </summary>
    public enum BossHitNodeParticleDirectionMode
    {
        /// <summary>使用 Boss 根对象在 HitNode 首帧投影到水平面的正前方。</summary>
        BossHorizontalForward = 0,
        /// <summary>使用武器、手部或腿部挂点在 HitNode 首帧的完整世界旋转。</summary>
        AnchorForward = 1
    }

    /// <summary>
    /// 按人工配置的 BossAttack Timeline 与 HitNode，在首个激活帧生成一个或多个不跟随骨骼的粒子实例。
    /// </summary>
    public sealed class BossHitNodeParticleVfxController : MonoBehaviour
    {
        [Serializable]
        private sealed class HitNodeParticleBinding
        {
            /// <summary>提供 AttackId 与可选 HitNode 列表的 BossAttack Timeline。</summary>
            [SerializeField] private BossAttackTimelineAsset attackTimeline;
            /// <summary>触发当前一次性粒子的 HitNode ID。</summary>
            [SerializeField] private string hitNodeId = string.Empty;
            /// <summary>生成时实例化的纯粒子 Prefab。</summary>
            [SerializeField] private GameObject effectPrefab;
            /// <summary>在 HitNode 首帧提供生成位置和可选方向的骨骼或 Socket。</summary>
            [SerializeField] private Transform spawnAnchor;
            /// <summary>实例旋转采用 Boss 水平正前方还是挂点首帧朝向。</summary>
            [SerializeField] private BossHitNodeParticleDirectionMode directionMode;
            /// <summary>以挂点本地坐标解释的生成位置偏移。</summary>
            [SerializeField] private Vector3 localPositionOffset;
            /// <summary>叠加到方向基准后的欧拉角偏移，用于适配 Prefab 发射轴。</summary>
            [SerializeField] private Vector3 localEulerOffset;
            /// <summary>乘到 Prefab 根缩放上的额外缩放。</summary>
            [SerializeField] private Vector3 scaleMultiplier = Vector3.one;
            /// <summary>实例存在的最长秒数；到时销毁，避免循环子粒子永久残留。</summary>
            [SerializeField, Min(0.01f)] private float lifetimeSeconds = 2.2f;
            /// <summary>相对 HitNode 起点提前生成特效的秒数；0 表示仍在 HitNode 首帧生成。</summary>
            [SerializeField, Min(0f)] private float leadTimeSeconds;

            /// <summary>当前绑定引用的 BossAttack Timeline。</summary>
            public BossAttackTimelineAsset AttackTimeline => attackTimeline;
            /// <summary>当前绑定引用的 HitNode ID。</summary>
            public string HitNodeId => hitNodeId;
            /// <summary>当前绑定实例化的特效 Prefab。</summary>
            public GameObject EffectPrefab => effectPrefab;
            /// <summary>当前绑定采样生成位置与方向的挂点。</summary>
            public Transform SpawnAnchor => spawnAnchor;
            /// <summary>当前绑定采用的方向基准。</summary>
            public BossHitNodeParticleDirectionMode DirectionMode => directionMode;
            /// <summary>挂点本地坐标下的生成位置偏移。</summary>
            public Vector3 LocalPositionOffset => localPositionOffset;
            /// <summary>叠加到方向基准后的欧拉角偏移。</summary>
            public Vector3 LocalEulerOffset => localEulerOffset;
            /// <summary>乘到 Prefab 根缩放上的额外缩放。</summary>
            public Vector3 ScaleMultiplier => scaleMultiplier;
            /// <summary>实例销毁前允许存在的秒数。</summary>
            public float LifetimeSeconds => lifetimeSeconds;
            /// <summary>相对 HitNode 起点提前生成的秒数。</summary>
            public float LeadTimeSeconds => leadTimeSeconds;

            /// <summary>
            /// 判断当前绑定是否应响应指定攻击与 HitNode。
            /// </summary>
            /// <param name="attackId">当前正在执行的 Boss 攻击 ID。</param>
            /// <param name="activeHitNodeId">当前帧激活的 HitNode ID。</param>
            /// <returns>true 表示 Timeline 的 ActionId 与 HitNodeId 都匹配；false 表示当前绑定不响应。</returns>
            public bool Matches(string attackId, string activeHitNodeId)
            {
                return attackTimeline != null &&
                    !string.IsNullOrEmpty(attackTimeline.ActionId) &&
                    !string.IsNullOrEmpty(hitNodeId) &&
                    string.Equals(attackTimeline.ActionId, attackId, StringComparison.Ordinal) &&
                    string.Equals(hitNodeId, activeHitNodeId, StringComparison.Ordinal);
            }

            /// <summary>
            /// 写入一条运行时或测试用的一次性粒子绑定。
            /// </summary>
            /// <param name="timeline">提供攻击 ID 与 HitNode 列表的 BossAttack Timeline。</param>
            /// <param name="nextHitNodeId">需要触发粒子的 HitNode ID。</param>
            /// <param name="prefab">触发时实例化的粒子 Prefab。</param>
            /// <param name="anchor">提供首帧生成位置与可选方向的挂点。</param>
            /// <param name="nextDirectionMode">实例采用的方向基准。</param>
            /// <param name="positionOffset">挂点本地坐标下的生成位置偏移。</param>
            /// <param name="eulerOffset">叠加到方向基准后的欧拉角偏移。</param>
            /// <param name="nextScaleMultiplier">乘到 Prefab 根缩放上的额外缩放。</param>
            /// <param name="nextLifetimeSeconds">实例销毁前允许存在的秒数。</param>
            public void Configure(
                BossAttackTimelineAsset timeline,
                string nextHitNodeId,
                GameObject prefab,
                Transform anchor,
                BossHitNodeParticleDirectionMode nextDirectionMode,
                Vector3 positionOffset,
                Vector3 eulerOffset,
                Vector3 nextScaleMultiplier,
                float nextLifetimeSeconds,
                float nextLeadTimeSeconds)
            {
                attackTimeline = timeline;
                hitNodeId = nextHitNodeId ?? string.Empty;
                effectPrefab = prefab;
                spawnAnchor = anchor;
                directionMode = nextDirectionMode;
                localPositionOffset = positionOffset;
                localEulerOffset = eulerOffset;
                scaleMultiplier = nextScaleMultiplier;
                lifetimeSeconds = nextLifetimeSeconds;
                leadTimeSeconds = Mathf.Max(0f, nextLeadTimeSeconds);
            }

            /// <summary>
            /// 判断当前攻击经过时间是否已到达绑定 HitNode 的特效生成时刻。
            /// </summary>
            /// <param name="attack">提供唯一运行时 HitNode 数据的当前 Boss 攻击。</param>
            /// <param name="elapsedTime">当前攻击从开始计算的经过秒数。</param>
            /// <returns>true 表示 ActionId 匹配且经过时间已跨过 HitNode 起点减提前量；false 表示尚未到时或绑定失效。</returns>
            public bool ShouldPlayForAttackTime(BossAttackDefinition attack, float elapsedTime)
            {
                if (attack == null ||
                    attackTimeline == null ||
                    !string.Equals(attackTimeline.ActionId, attack.AttackId, StringComparison.Ordinal) ||
                    attack.HitNodes == null)
                {
                    return false;
                }

                for (int i = 0; i < attack.HitNodes.Length; i++)
                {
                    CombatHitNodeData hitNode = attack.HitNodes[i];
                    if (string.Equals(hitNode.HitNodeId, hitNodeId, StringComparison.Ordinal))
                    {
                        float spawnTime = Mathf.Max(0f, hitNode.Window.StartTime - leadTimeSeconds);
                        return elapsedTime >= spawnTime;
                    }
                }

                return false;
            }
        }

        /// <summary>由开发者在 Inspector 中维护的 HitNode 到一次性粒子 Prefab 绑定。</summary>
        [SerializeField] private HitNodeParticleBinding[] particleBindings = Array.Empty<HitNodeParticleBinding>();

        private readonly HashSet<int> playedBindingIndices = new HashSet<int>();

        /// <summary>唤醒时校验人工绑定，缺失引用或失效 HitNode 会明确报告而不是静默忽略。</summary>
        private void Awake()
        {
            ValidateBindings();
        }

        /// <summary>组件禁用时清除本轮攻击的绑定播放记录，下一次启用后允许重新触发。</summary>
        private void OnDisable()
        {
            ResetTriggerState();
        }

        /// <summary>
        /// 如果当前 HitNode 命中人工绑定，则播放本轮攻击尚未触发的全部匹配粒子。
        /// </summary>
        /// <param name="attackId">当前正在执行的 Boss 攻击 ID。</param>
        /// <param name="hitNodeId">当前帧激活的 HitNode ID。</param>
        /// <returns>true 表示本次至少生成了一个粒子实例；false 表示未匹配、已播放或配置无效。</returns>
        public bool TryPlayForHitNode(string attackId, string hitNodeId)
        {
            if (string.IsNullOrEmpty(attackId) || string.IsNullOrEmpty(hitNodeId) || particleBindings == null)
            {
                return false;
            }

            bool spawnedAny = false;
            for (int i = 0; i < particleBindings.Length; i++)
            {
                HitNodeParticleBinding binding = particleBindings[i];
                if (binding == null ||
                    playedBindingIndices.Contains(i) ||
                    !binding.Matches(attackId, hitNodeId) ||
                    binding.EffectPrefab == null ||
                    binding.SpawnAnchor == null ||
                    binding.LifetimeSeconds <= 0f)
                {
                    continue;
                }

                GameObject instance = SpawnEffect(binding);
                if (instance == null)
                {
                    continue;
                }

                playedBindingIndices.Add(i);
                spawnedAny = true;
            }

            return spawnedAny;
        }

        /// <summary>
        /// 按当前攻击时间播放已经到达“HitNode 起点减提前量”的全部绑定，低帧率跨过触发点时仍只补播一次。
        /// </summary>
        /// <param name="attack">提供 ActionId 与唯一运行时 HitNode 数据的当前 Boss 攻击。</param>
        /// <param name="elapsedTime">当前攻击从开始计算的经过秒数。</param>
        /// <returns>true 表示本次至少生成一个特效实例；false 表示尚未到时、已经播放或配置无效。</returns>
        public bool TryPlayForAttackTime(BossAttackDefinition attack, float elapsedTime)
        {
            if (attack == null || particleBindings == null)
            {
                return false;
            }

            bool spawnedAny = false;
            for (int i = 0; i < particleBindings.Length; i++)
            {
                HitNodeParticleBinding binding = particleBindings[i];
                if (binding == null ||
                    playedBindingIndices.Contains(i) ||
                    !binding.ShouldPlayForAttackTime(attack, elapsedTime) ||
                    binding.EffectPrefab == null ||
                    binding.SpawnAnchor == null ||
                    binding.LifetimeSeconds <= 0f)
                {
                    continue;
                }

                GameObject instance = SpawnEffect(binding);
                if (instance == null)
                {
                    continue;
                }

                playedBindingIndices.Add(i);
                spawnedAny = true;
            }

            return spawnedAny;
        }

        /// <summary>
        /// 重置当前攻击的逐绑定播放记录，让下一次攻击可以重新触发全部配置效果。
        /// </summary>
        public void ResetTriggerState()
        {
            playedBindingIndices.Clear();
        }

        /// <summary>
        /// 结束攻击时只重置触发记录；已经生成的世界空间实例继续播放到各自生命周期结束。
        /// </summary>
        public void EndAttack()
        {
            ResetTriggerState();
        }

        /// <summary>
        /// 判断指定攻击节点是否已被当前控制器选作一次性粒子节点，供持续拖尾路由执行互斥。
        /// </summary>
        /// <param name="attackId">当前正在执行的 Boss 攻击 ID。</param>
        /// <param name="hitNodeId">需要查询的 HitNode ID。</param>
        /// <returns>true 表示至少存在一条 Timeline 与 HitNode 均匹配的人工绑定；false 表示该节点未配置一次性粒子。</returns>
        /// <remarks>即使粒子 Prefab 或挂点配置失效也返回 true；失效绑定仍代表作者选择了粒子表现，并由现有校验负责报告错误。</remarks>
        public bool HasBindingForHitNode(string attackId, string hitNodeId)
        {
            if (string.IsNullOrEmpty(attackId) || string.IsNullOrEmpty(hitNodeId) || particleBindings == null)
            {
                return false;
            }

            for (int i = 0; i < particleBindings.Length; i++)
            {
                HitNodeParticleBinding binding = particleBindings[i];
                if (binding != null && binding.Matches(attackId, hitNodeId))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 为测试或运行时组装追加一条 Timeline + HitNode + Prefab + 挂点绑定。
        /// </summary>
        /// <param name="timeline">提供攻击 ID 与 HitNode 列表的 BossAttack Timeline。</param>
        /// <param name="hitNodeId">需要触发粒子的 HitNode ID。</param>
        /// <param name="effectPrefab">触发时实例化的粒子 Prefab。</param>
        /// <param name="spawnAnchor">提供首帧生成位置与可选方向的挂点。</param>
        /// <param name="directionMode">实例采用的方向基准。</param>
        /// <param name="localPositionOffset">挂点本地坐标下的生成位置偏移。</param>
        /// <param name="localEulerOffset">叠加到方向基准后的欧拉角偏移。</param>
        /// <param name="scaleMultiplier">乘到 Prefab 根缩放上的额外缩放。</param>
        /// <param name="lifetimeSeconds">实例销毁前允许存在的秒数。</param>
        /// <param name="leadTimeSeconds">相对 HitNode 起点提前生成特效的秒数；默认 0 表示不提前。</param>
        public void BindEffectForHitNode(
            BossAttackTimelineAsset timeline,
            string hitNodeId,
            GameObject effectPrefab,
            Transform spawnAnchor,
            BossHitNodeParticleDirectionMode directionMode,
            Vector3 localPositionOffset,
            Vector3 localEulerOffset,
            Vector3 scaleMultiplier,
            float lifetimeSeconds,
            float leadTimeSeconds = 0f)
        {
            int oldLength = particleBindings != null ? particleBindings.Length : 0;
            Array.Resize(ref particleBindings, oldLength + 1);
            HitNodeParticleBinding binding = new HitNodeParticleBinding();
            binding.Configure(
                timeline,
                hitNodeId,
                effectPrefab,
                spawnAnchor,
                directionMode,
                localPositionOffset,
                localEulerOffset,
                scaleMultiplier,
                lifetimeSeconds,
                leadTimeSeconds);
            particleBindings[oldLength] = binding;
        }

        /// <summary>
        /// 捕获挂点首帧位置与选定方向，生成无父级粒子实例并安排生命周期结束销毁。
        /// </summary>
        /// <param name="binding">已经通过完整配置校验的一次性粒子绑定。</param>
        /// <returns>成功生成且包含粒子系统时返回实例；无法播放时返回 null。</returns>
        private GameObject SpawnEffect(HitNodeParticleBinding binding)
        {
            Vector3 position = binding.SpawnAnchor.TransformPoint(binding.LocalPositionOffset);
            Quaternion rotation = ResolveSpawnRotation(binding) * Quaternion.Euler(binding.LocalEulerOffset);
            GameObject instance = Instantiate(binding.EffectPrefab, position, rotation);
            instance.name = binding.EffectPrefab.name + "_Runtime";
            instance.transform.SetParent(null, true);
            instance.transform.localScale = Vector3.Scale(binding.EffectPrefab.transform.localScale, binding.ScaleMultiplier);

            ParticleSystem[] particleSystems = instance.GetComponentsInChildren<ParticleSystem>(true);
            if (particleSystems == null || particleSystems.Length == 0)
            {
                DestroySpawnedInstance(instance);
                return null;
            }

            if (!instance.activeSelf)
            {
                instance.SetActive(true);
            }

            for (int i = 0; i < particleSystems.Length; i++)
            {
                ParticleSystem particleSystem = particleSystems[i];
                if (particleSystem == null)
                {
                    continue;
                }

                if (!particleSystem.gameObject.activeSelf)
                {
                    particleSystem.gameObject.SetActive(true);
                }

                particleSystem.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particleSystem.Play(true);
            }

            if (Application.isPlaying)
            {
                Destroy(instance, binding.LifetimeSeconds);
            }

            return instance;
        }

        /// <summary>
        /// 根据绑定方向模式计算 HitNode 首帧世界旋转，不保留后续骨骼跟随关系。
        /// </summary>
        /// <param name="binding">提供挂点和方向模式的一次性粒子绑定。</param>
        /// <returns>尚未叠加 Prefab 发射轴欧拉偏移的世界旋转。</returns>
        private Quaternion ResolveSpawnRotation(HitNodeParticleBinding binding)
        {
            if (binding.DirectionMode == BossHitNodeParticleDirectionMode.AnchorForward)
            {
                return binding.SpawnAnchor.rotation;
            }

            Vector3 horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (horizontalForward.sqrMagnitude <= 0.0001f)
            {
                horizontalForward = Vector3.forward;
            }

            return Quaternion.LookRotation(horizontalForward.normalized, Vector3.up);
        }

        /// <summary>
        /// 销毁无法播放的临时实例；Play Mode 延迟到帧末，EditMode 测试立即清理。
        /// </summary>
        /// <param name="instance">需要清理的临时粒子实例。</param>
        private static void DestroySpawnedInstance(GameObject instance)
        {
            if (instance == null)
            {
                return;
            }

            if (Application.isPlaying)
            {
                Destroy(instance);
            }
            else
            {
                DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// 在 Play Mode 启动时报告空引用、空 ID、失效 HitNode 或非法生命周期。
        /// </summary>
        private void ValidateBindings()
        {
            if (particleBindings == null)
            {
                return;
            }

            for (int i = 0; i < particleBindings.Length; i++)
            {
                string error = GetBindingError(particleBindings[i]);
                if (!string.IsNullOrEmpty(error))
                {
                    Debug.LogError($"{nameof(BossHitNodeParticleVfxController)} 绑定 {i} 无效：{error}", this);
                }
            }
        }

        /// <summary>
        /// 检查单条一次性粒子绑定是否包含运行时生成所需的全部数据。
        /// </summary>
        /// <param name="binding">需要检查的一次性粒子绑定。</param>
        /// <returns>空字符串表示绑定有效；否则返回可直接显示给开发者的错误原因。</returns>
        private static string GetBindingError(HitNodeParticleBinding binding)
        {
            if (binding == null)
            {
                return "绑定对象为空。";
            }

            if (binding.AttackTimeline == null)
            {
                return "未选择 BossAttack Timeline。";
            }

            if (string.IsNullOrEmpty(binding.AttackTimeline.ActionId))
            {
                return "Timeline 的 ActionId 为空。";
            }

            if (string.IsNullOrEmpty(binding.HitNodeId))
            {
                return "未选择 HitNode。";
            }

            if (!ContainsHitNode(binding.AttackTimeline, binding.HitNodeId))
            {
                return $"Timeline 中不存在 HitNode '{binding.HitNodeId}'。";
            }

            if (binding.EffectPrefab == null)
            {
                return "未绑定粒子 Prefab。";
            }

            if (binding.SpawnAnchor == null)
            {
                return "未绑定生成挂点。";
            }

            if (binding.LifetimeSeconds <= 0f)
            {
                return "生命周期必须大于 0 秒。";
            }

            if (binding.LeadTimeSeconds < 0f)
            {
                return "提前量不能小于 0 秒。";
            }

            if (binding.EffectPrefab.GetComponentInChildren<ParticleSystem>(true) == null)
            {
                return "绑定的 Prefab 不包含 ParticleSystem。";
            }

            return string.Empty;
        }

        /// <summary>
        /// 判断指定 BossAttack Timeline 是否仍包含目标 HitNode。
        /// </summary>
        /// <param name="timeline">需要检查的 BossAttack Timeline。</param>
        /// <param name="hitNodeId">期望存在的 HitNode ID。</param>
        /// <returns>true 表示存在同名 CombatHitNodeClip；false 表示引用已失效。</returns>
        private static bool ContainsHitNode(BossAttackTimelineAsset timeline, string hitNodeId)
        {
            if (timeline == null || string.IsNullOrEmpty(hitNodeId))
            {
                return false;
            }

            foreach (CombatHitNodeClip clip in timeline.EnumerateClips<CombatHitNodeClip>())
            {
                if (clip != null && string.Equals(clip.Name, hitNodeId, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
