// 文件说明：维护 Boss 代码位移、Root Motion、MotionWarp、碰撞裁剪和调试快照。
// 所属模块：Boss 位移。
// 运行影响：影响 Boss 空间位置、攻击落点、穿模保护和位移调试。

using UnityEngine;

namespace ProjectEVE.Boss.Movement
{
    /// <summary>
    /// Optional visual hooks for fast phase/orbit movement. Gameplay movement never depends on these effects.
    /// </summary>
    public sealed class BossPhaseMoveVfx : MonoBehaviour
    {
        /// <summary>
        /// 执行 Play / Begin 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        [SerializeField] private ParticleSystem startEffect;
        [SerializeField] private ParticleSystem endEffect;
        [SerializeField] private ParticleSystem trailEffect;
        [SerializeField] private Transform trailAnchor;

        public void PlayBegin(Vector3 startPosition, Vector3 endPosition)
        {
            PlayAt(startEffect, startPosition);

            if (trailAnchor != null)
            {
                trailAnchor.position = startPosition;
            }

            if (trailEffect != null)
            {
                trailEffect.transform.position = startPosition;
                trailEffect.Play(true);
            }
        }

        /// <summary>
        /// 推进 Tick 时间线或状态逻辑，并返回或写入本帧产生的运行时结果。
        /// </summary>
        public void Tick(Vector3 position)
        {
            if (trailAnchor != null)
            {
                trailAnchor.position = position;
            }
            else if (trailEffect != null)
            {
                trailEffect.transform.position = position;
            }
        }

        /// <summary>
        /// 执行 Play / End 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        public void PlayEnd(Vector3 position)
        {
            PlayAt(endEffect, position);
            Stop();
        }

        /// <summary>
        /// 停止 Stop 流程，并清理当前动作、位移或显示状态。
        /// </summary>
        public void Stop()
        {
            if (trailEffect != null)
            {
                trailEffect.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
        }

        /// <summary>
        /// 执行 Play / At 相关逻辑，并维护 Boss 位移 模块的运行时一致性。
        /// </summary>
        private static void PlayAt(ParticleSystem effect, Vector3 position)
        {
            if (effect == null)
            {
                return;
            }

            effect.transform.position = position;
            effect.Play(true);
        }
    }
}
