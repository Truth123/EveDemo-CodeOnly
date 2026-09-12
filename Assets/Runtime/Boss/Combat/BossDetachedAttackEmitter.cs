// 文件说明：按 ActionId 与 HitNodeId 的人工绑定生成 Boss 独立战斗判定体，并可选克隆场景视觉模板。
// 所属模块：Boss 战斗。
// 运行影响：在 HitNode 首帧生成世界空间实例；场景模板运行时隐藏，自推进视觉可与移动判定体共享速度但不叠加父级位移。

using ProjectEVE.Boss.AI;
using ProjectEVE.Boss.Actor;
using ProjectEVE.Combat;
using ProjectEVE.Combat.Timeline;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>独立判定体在生成后采用的空间检测方式。</summary>
    public enum BossDetachedExecutionMode
    {
        /// <summary>判定体在生命周期内通过 Trigger 检测进入目标，适用于移动剑气。</summary>
        TriggerVolume = 0,
        /// <summary>判定体生成首帧只执行一次球形范围查询，适用于瞬时范围爆发。</summary>
        InstantPulse = 1
    }

    public enum BossDetachedDirectionMode
    {
        BossHorizontalForward = 0,
        AnchorForward = 1
    }

    [Serializable]
    public sealed class BossDetachedAttackBinding
    {
        public string ActionId;
        public string HitNodeId;
        public BossDetachedHitVolume Prefab;
        [Tooltip("可选的场景视觉模板。运行时隐藏模板本体，每次生成 Detached 时克隆并重播其粒子。")]
        public GameObject VisualTemplate;
        [Tooltip("可选的自推进粒子。指定后视觉克隆保持在世界空间，并把该粒子的 Z 轴 Start Speed 换算为本绑定的世界 Speed；适用于 Sword Slash 6 的 Pivot。")]
        public ParticleSystem VisualTravelDriver;
        public Transform SpawnAnchor;
        public Vector3 LocalPosition;
        [Tooltip("相对发射方向的额外旋转；Z 轴会在视觉模板基础角度之上滚转整个剑气与 BoxCollider，不改变直线飞行方向。")]
        public Vector3 LocalEulerAngles;
        public Vector3 LocalScale = Vector3.one;
        public BossDetachedDirectionMode DirectionMode;
        public BossDetachedExecutionMode ExecutionMode;
        public float Speed;
        public float Lifetime = 2f;
    }

    /// <summary>Boss 组合层的人工独立判定绑定；同一 HitNode 可配置多个实例。</summary>
    public sealed class BossDetachedAttackEmitter : MonoBehaviour
    {
        [SerializeField] private BossDetachedAttackBinding[] bindings = Array.Empty<BossDetachedAttackBinding>();
        private readonly HashSet<string> spawnedBindings = new HashSet<string>();

        /// <summary>进入运行时时隐藏绑定的场景视觉模板，保留其编辑状态可见性。</summary>
        private void Awake()
        {
            HideSceneVisualTemplates();
        }

        /// <summary>开始新攻击时清理“每条绑定每次攻击只生成一次”的触发状态。</summary>
        public void BeginAttack()
        {
            spawnedBindings.Clear();
        }

        /// <summary>检查场景组合层是否为指定动作节点配置了可生成的独立判定。</summary>
        /// <param name="actionId">Boss Timeline ActionId。</param>
        /// <param name="hitNodeId">Timeline HitNodeId。</param>
        /// <returns>true 表示至少一条匹配绑定同时具备 Prefab 与 SpawnAnchor。</returns>
        public bool HasValidBinding(string actionId, string hitNodeId)
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                BossDetachedAttackBinding binding = bindings[i];
                if (binding != null &&
                    binding.ActionId == actionId &&
                    binding.HitNodeId == hitNodeId &&
                    binding.Prefab != null &&
                    binding.SpawnAnchor != null)
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>生成当前攻击与 HitNode 匹配的全部独立判定体。</summary>
        /// <param name="attack">当前攻击定义。</param>
        /// <param name="attackInstanceId">本次攻击唯一实例 ID。</param>
        /// <param name="hitNode">首帧进入的 HitNode 数据快照。</param>
        /// <param name="bossTransform">Boss 水平正前方模式和攻击者语义使用的根节点。</param>
        /// <param name="source">反馈和日志使用的来源组件。</param>
        /// <param name="bossActor">接收命中结果、压力和恢复奖励的 BossActor。</param>
        /// <param name="attackerTeam">正式命中的攻击方阵营。</param>
        /// <param name="combatSystem">复用的 Boss 正式命中系统。</param>
        /// <param name="hitMask">由 BossAttackExecutor 统一配置的正式命中查询层。</param>
        /// <param name="overlapBufferSize">由 BossAttackExecutor 统一配置的 NonAlloc 查询容量。</param>
        /// <param name="logHits">是否输出正式命中日志。</param>
        public void SpawnForHitNode(
            BossAttackDefinition attack,
            int attackInstanceId,
            CombatHitNodeData hitNode,
            Transform bossTransform,
            Component source,
            BossActor bossActor,
            CombatTeam attackerTeam,
            BossCombatSystem combatSystem,
            LayerMask hitMask,
            int overlapBufferSize,
            bool logHits)
        {
            if (attack == null || bossTransform == null || hitNode.SourcePart != BossAttackSourcePart.Detached)
            {
                return;
            }

            for (int i = 0; i < bindings.Length; i++)
            {
                BossDetachedAttackBinding binding = bindings[i];
                if (binding == null || binding.ActionId != attack.AttackId || binding.HitNodeId != hitNode.HitNodeId)
                {
                    continue;
                }

                string triggerKey = $"{attackInstanceId}:{i}";
                if (!spawnedBindings.Add(triggerKey))
                {
                    continue;
                }

                if (binding.Prefab == null || binding.SpawnAnchor == null)
                {
                    Debug.LogError($"Detached hit binding invalid: action={binding.ActionId}, node={binding.HitNodeId}, index={i}.", this);
                    continue;
                }

                Vector3 direction = ResolveDirection(binding, bossTransform);
                Quaternion baseRotation = Quaternion.LookRotation(direction, Vector3.up);
                Vector3 position = binding.SpawnAnchor.TransformPoint(binding.LocalPosition);
                BossDetachedHitVolume instance = Instantiate(
                    binding.Prefab,
                    position,
                    baseRotation * Quaternion.Euler(binding.LocalEulerAngles));
                instance.transform.localScale = Vector3.Scale(instance.transform.localScale, binding.LocalScale);
                SpawnVisualTemplate(binding, instance.transform);
                instance.Initialize(
                    combatSystem,
                    new BossCombatExecutionRequest(
                        attack,
                        attackInstanceId,
                        hitNode,
                        null,
                        bossTransform,
                        instance,
                        bossActor,
                        attackerTeam,
                        hitMask,
                        overlapBufferSize,
                        logHits,
                        () => true),
                    direction * Mathf.Max(0f, binding.Speed),
                    binding.Lifetime);
                if (binding.ExecutionMode == BossDetachedExecutionMode.InstantPulse)
                {
                    instance.ExecuteInstantPulse();
                }
            }
        }

        /// <summary>隐藏所有有效的场景视觉模板，避免模板固定显示在 Boss 身上。</summary>
        private void HideSceneVisualTemplates()
        {
            for (int i = 0; i < bindings.Length; i++)
            {
                GameObject visualTemplate = bindings[i]?.VisualTemplate;
                if (visualTemplate == null || !visualTemplate.scene.IsValid())
                {
                    continue;
                }

                Transform templateTransform = visualTemplate.transform;
                if (templateTransform == transform || transform.IsChildOf(templateTransform))
                {
                    Debug.LogError($"Detached visual template cannot be the emitter or its ancestor: {visualTemplate.name}.", this);
                    continue;
                }

                visualTemplate.SetActive(false);
            }
        }

        /// <summary>克隆并重播视觉模板；配置自推进粒子时把视觉留在世界空间，避免与 Detached 根位移叠加。</summary>
        /// <param name="binding">提供视觉模板、自推进粒子、世界速度和生命周期的当前绑定。</param>
        /// <param name="instanceTransform">新生成的 Detached 判定体根节点，用于计算视觉生成时的世界 TRS。</param>
        private static void SpawnVisualTemplate(BossDetachedAttackBinding binding, Transform instanceTransform)
        {
            GameObject visualTemplate = binding.VisualTemplate;
            if (visualTemplate == null)
            {
                return;
            }

            Transform templateTransform = visualTemplate.transform;
            bool isSelfPropelled = binding.VisualTravelDriver != null;
            GameObject visualInstance = isSelfPropelled
                ? Instantiate(visualTemplate)
                : Instantiate(visualTemplate, instanceTransform, false);
            visualInstance.SetActive(false);
            Transform visualTransform = visualInstance.transform;

            if (isSelfPropelled)
            {
                visualTransform.SetPositionAndRotation(
                    instanceTransform.TransformPoint(templateTransform.localPosition),
                    instanceTransform.rotation * templateTransform.localRotation);
                visualTransform.localScale = Vector3.Scale(instanceTransform.lossyScale, templateTransform.localScale);

                if (!TryResolveClonedParticleSystem(
                        templateTransform,
                        binding.VisualTravelDriver.transform,
                        visualTransform,
                        out ParticleSystem clonedTravelDriver))
                {
                    Debug.LogError(
                        $"Detached visual travel driver must belong to its visual template: {visualTemplate.name}.",
                        visualTemplate);
                    Destroy(visualInstance);
                    return;
                }

                float worldUnitsPerLocalUnit = clonedTravelDriver.transform.TransformVector(Vector3.forward).magnitude;
                if (worldUnitsPerLocalUnit <= 0.0001f)
                {
                    Debug.LogError(
                        $"Detached visual travel driver has zero forward scale: {binding.VisualTravelDriver.name}.",
                        binding.VisualTravelDriver);
                    Destroy(visualInstance);
                    return;
                }

                ParticleSystem.MainModule travelMain = clonedTravelDriver.main;
                travelMain.startSpeed = Mathf.Max(0f, binding.Speed) / worldUnitsPerLocalUnit;
                Destroy(visualInstance, Mathf.Max(0.01f, binding.Lifetime));
            }
            else
            {
                visualTransform.localPosition = templateTransform.localPosition;
                visualTransform.localRotation = templateTransform.localRotation;
                visualTransform.localScale = templateTransform.localScale;
            }

            visualInstance.SetActive(true);

            ParticleSystem[] particleSystems = visualInstance.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i].Play(false);
            }
        }

        /// <summary>按模板层级的兄弟索引，在结构一致的克隆中定位同一个粒子节点。</summary>
        /// <param name="templateRoot">视觉模板根节点。</param>
        /// <param name="templateParticleTransform">模板中配置为自推进粒子的节点。</param>
        /// <param name="cloneRoot">由模板实例化出的视觉克隆根节点。</param>
        /// <param name="clonedParticleSystem">成功时写回克隆中对应节点的 ParticleSystem。</param>
        /// <returns>true 表示粒子属于模板层级且克隆中存在对应 ParticleSystem；否则返回 false。</returns>
        private static bool TryResolveClonedParticleSystem(
            Transform templateRoot,
            Transform templateParticleTransform,
            Transform cloneRoot,
            out ParticleSystem clonedParticleSystem)
        {
            clonedParticleSystem = null;
            if (templateParticleTransform == null ||
                (templateParticleTransform != templateRoot && !templateParticleTransform.IsChildOf(templateRoot)))
            {
                return false;
            }

            List<int> siblingIndices = new List<int>();
            Transform current = templateParticleTransform;
            while (current != templateRoot)
            {
                siblingIndices.Add(current.GetSiblingIndex());
                current = current.parent;
            }

            current = cloneRoot;
            for (int i = siblingIndices.Count - 1; i >= 0; i--)
            {
                int siblingIndex = siblingIndices[i];
                if (siblingIndex < 0 || siblingIndex >= current.childCount)
                {
                    return false;
                }

                current = current.GetChild(siblingIndex);
            }

            clonedParticleSystem = current.GetComponent<ParticleSystem>();
            return clonedParticleSystem != null;
        }

        /// <summary>在生成首帧解析一次世界空间发射方向。</summary>
        /// <param name="binding">保存方向模式与人工挂点的绑定。</param>
        /// <param name="bossTransform">Boss 水平正前方模式使用的根节点。</param>
        /// <returns>归一化世界空间方向；无有效方向时返回 Vector3.forward。</returns>
        private static Vector3 ResolveDirection(BossDetachedAttackBinding binding, Transform bossTransform)
        {
            Vector3 direction = binding.DirectionMode == BossDetachedDirectionMode.AnchorForward
                ? binding.SpawnAnchor.forward
                : bossTransform.forward;
            direction.y = binding.DirectionMode == BossDetachedDirectionMode.BossHorizontalForward ? 0f : direction.y;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }
    }
}
