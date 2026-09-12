// 文件说明：管理玩家 Skill1 三段出手粒子和每段命中特效的显式重播与清理。
// 所属模块：玩家战斗表现。
// 运行影响：只影响 SkillEffect 粒子生命周期，不参与 Timeline、命中解析、伤害或状态切换。

using System;
using System.Collections.Generic;
using UnityEngine;

namespace ProjectEVE.Player.Combat
{
    /// <summary>
    /// 管理场景内 SkillEffect 的主粒子和 hitEffect 子粒子，供 Skill 状态与武器 Hitbox 共享调用。
    /// </summary>
    public sealed class PlayerSkillEffectController : MonoBehaviour
    {
        /// <summary>SkillEffect 根对象；保持启用，由控制器显式重播其主粒子。</summary>
        [SerializeField] private GameObject effectRoot;
        /// <summary>每段真实命中后临时启用的 hitEffect 子对象。</summary>
        [SerializeField] private GameObject hitEffect;

        private ParticleSystem[] mainParticleSystems = Array.Empty<ParticleSystem>();
        private ParticleSystem[] hitParticleSystems = Array.Empty<ParticleSystem>();
        private bool hitEffectPlayedThisSegment;

        /// <summary>唤醒时绑定并缓存粒子，同时清除 Play On Awake 或编辑器预览留下的粒子。</summary>
        private void Awake()
        {
            BindReferences();
            CacheParticleSystems();
            StopAll();

            if (effectRoot == null || hitEffect == null)
            {
                Debug.LogError(
                    $"{nameof(PlayerSkillEffectController)} 缺少 effectRoot 或 hitEffect 绑定。",
                    this);
            }
        }

        /// <summary>首次添加组件时按当前 SkillEffect 层级填充默认引用并缓存粒子。</summary>
        private void Reset()
        {
            BindReferences();
            CacheParticleSystems();
        }

        /// <summary>Inspector 绑定变化时刷新粒子缓存，确保主粒子不会包含 hitEffect 子树。</summary>
        private void OnValidate()
        {
            BindReferences();
            CacheParticleSystems();
        }

        /// <summary>组件禁用时清除全部技能粒子，避免状态切换或场景退出后残留。</summary>
        private void OnDisable()
        {
            StopAll();
        }

        /// <summary>
        /// 在每段吸附窗口首次激活时清除上一段残留并从头播放主粒子。
        /// </summary>
        /// <returns>至少存在一个主 ParticleSystem 并已提交播放时返回 true；缺少配置时返回 false。</returns>
        public bool PlaySegment()
        {
            if (effectRoot == null || mainParticleSystems.Length == 0)
            {
                return false;
            }

            if (!effectRoot.activeSelf)
            {
                effectRoot.SetActive(true);
            }

            EndHitWindow();
            RestartParticleSystems(mainParticleSystems);
            return true;
        }

        /// <summary>
        /// 在当前 Skill HitNode 首次产生有效命中时启用并从头播放命中特效。
        /// </summary>
        /// <returns>本次调用实际启动了 hitEffect 时返回 true；本段已播放或缺少配置时返回 false。</returns>
        public bool PlayHitEffect()
        {
            if (hitEffectPlayedThisSegment || hitEffect == null || hitParticleSystems.Length == 0)
            {
                return false;
            }

            hitEffect.SetActive(true);
            RestartParticleSystems(hitParticleSystems);
            hitEffectPlayedThisSegment = true;
            return true;
        }

        /// <summary>在当前 Skill HitNode 结束时清空并禁用 hitEffect，准备下一段命中。</summary>
        public void EndHitWindow()
        {
            StopParticleSystems(hitParticleSystems);
            if (hitEffect != null && hitEffect.activeSelf)
            {
                hitEffect.SetActive(false);
            }

            hitEffectPlayedThisSegment = false;
        }

        /// <summary>在 Skill 退出、中断或组件禁用时清空主粒子与命中粒子。</summary>
        public void StopAll()
        {
            StopParticleSystems(mainParticleSystems);
            EndHitWindow();
        }

        /// <summary>按当前层级绑定 SkillEffect 根和直属 hitEffect 子对象。</summary>
        private void BindReferences()
        {
            if (effectRoot == null)
            {
                effectRoot = gameObject;
            }

            if (hitEffect == null && effectRoot != null)
            {
                Transform hitTransform = effectRoot.transform.Find("hitEffect");
                if (hitTransform != null)
                {
                    hitEffect = hitTransform.gameObject;
                }
            }
        }

        /// <summary>缓存主粒子与命中粒子；主粒子显式排除 hitEffect 整个子树。</summary>
        private void CacheParticleSystems()
        {
            if (effectRoot == null)
            {
                mainParticleSystems = Array.Empty<ParticleSystem>();
                hitParticleSystems = Array.Empty<ParticleSystem>();
                return;
            }

            hitParticleSystems = hitEffect != null
                ? hitEffect.GetComponentsInChildren<ParticleSystem>(true)
                : Array.Empty<ParticleSystem>();

            ParticleSystem[] allSystems = effectRoot.GetComponentsInChildren<ParticleSystem>(true);
            List<ParticleSystem> mainSystems = new List<ParticleSystem>(allSystems.Length);
            for (int i = 0; i < allSystems.Length; i++)
            {
                ParticleSystem particleSystem = allSystems[i];
                if (particleSystem == null ||
                    (hitEffect != null && particleSystem.transform.IsChildOf(hitEffect.transform)))
                {
                    continue;
                }

                mainSystems.Add(particleSystem);
            }

            mainParticleSystems = mainSystems.ToArray();
        }

        /// <summary>
        /// 清空指定粒子数组并逐个使用非递归 Play，从而避免父子 ParticleSystem 被重复启动。
        /// </summary>
        /// <param name="particleSystems">本次需要从头播放的粒子数组。</param>
        private static void RestartParticleSystems(ParticleSystem[] particleSystems)
        {
            StopParticleSystems(particleSystems);
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i]?.Play(false);
            }
        }

        /// <summary>停止并清空指定粒子数组，不递归影响数组之外的粒子层。</summary>
        /// <param name="particleSystems">本次需要停止并清空的粒子数组。</param>
        private static void StopParticleSystems(ParticleSystem[] particleSystems)
        {
            for (int i = 0; i < particleSystems.Length; i++)
            {
                particleSystems[i]?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
        }
    }
}
