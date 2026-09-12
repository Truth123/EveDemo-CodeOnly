// 文件说明：维护 Raven 护盾击破长眩晕的破盾爆发、持续失能与恢复预警三阶段视觉。
// 所属模块：Boss 战斗表现。
// 运行影响：只播放专用粒子和非武器灯光，不修改护盾、状态时长、伤害、AI、动画或武器表现。

using UnityEngine;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 护盾击破长眩晕视觉控制器。正式进入 ShieldBreakStun 时播放一次破盾爆发和持续层，
    /// 并在状态结束前指定秒数启动恢复预警。
    /// </summary>
    public sealed class BossShieldBreakStunVfxController : MonoBehaviour
    {
        /// <summary>正式进入长眩晕时播放一次的核心闪光、冲击环和外飞碎片。</summary>
        [SerializeField] private GameObject breakBurstRoot;
        /// <summary>长眩晕期间保持的低亮电弧、上升碎片、断裂半环和地面圆阵。</summary>
        [SerializeField] private GameObject stunLoopRoot;
        /// <summary>状态结束前播放的碎片收束、加速电弧和圆阵收缩。</summary>
        [SerializeField] private GameObject recoveryWarningRoot;
        /// <summary>距离长眩晕结束还有多少秒时启动恢复预警。</summary>
        [SerializeField, Min(0f)] private float recoveryWarningLeadTime = 0.7f;

        private bool isStunPlaying;
        private bool isRecoveryWarningPlaying;

        /// <summary>当前是否已经启动长眩晕视觉。</summary>
        public bool IsStunPlaying => isStunPlaying;
        /// <summary>当前是否已经进入恢复预警视觉阶段。</summary>
        public bool IsRecoveryWarningPlaying => isRecoveryWarningPlaying;
        /// <summary>恢复预警相对状态结束的提前秒数。</summary>
        public float RecoveryWarningLeadTime => recoveryWarningLeadTime;

        /// <summary>对象唤醒时清空全部阶段，避免场景加载后自动显示硬直特效。</summary>
        private void Awake()
        {
            StopStun();
        }

        /// <summary>组件禁用时清空全部粒子和灯光，避免死亡、场景切换或中断后残留。</summary>
        private void OnDisable()
        {
            StopStun();
        }

        /// <summary>
        /// 写入三阶段表现根并立即清空。供场景安装和 EditMode 测试使用。
        /// </summary>
        /// <param name="nextBreakBurstRoot">进入长眩晕时的一次性破盾爆发根。</param>
        /// <param name="nextStunLoopRoot">长眩晕期间的持续表现根。</param>
        /// <param name="nextRecoveryWarningRoot">恢复前的收束预警根。</param>
        public void BindEffectRoots(
            GameObject nextBreakBurstRoot,
            GameObject nextStunLoopRoot,
            GameObject nextRecoveryWarningRoot)
        {
            StopStun();
            breakBurstRoot = nextBreakBurstRoot;
            stunLoopRoot = nextStunLoopRoot;
            recoveryWarningRoot = nextRecoveryWarningRoot;
            StopRoot(breakBurstRoot);
            StopRoot(stunLoopRoot);
            StopRoot(recoveryWarningRoot);
        }

        /// <summary>
        /// 设置恢复预警提前量，主要供场景安装与测试校准使用。
        /// </summary>
        /// <param name="seconds">距离长眩晕结束的提前秒数。</param>
        public void SetRecoveryWarningLeadTime(float seconds)
        {
            recoveryWarningLeadTime = Mathf.Max(0f, seconds);
        }

        /// <summary>
        /// 正式进入 ShieldBreakStun 时从头播放破盾爆发和持续失能层，并保持恢复预警关闭。
        /// </summary>
        /// <returns>true 表示破盾爆发与持续根均已绑定并开始播放；false 表示关键绑定缺失。</returns>
        public bool BeginStun()
        {
            StopStun();
            if (breakBurstRoot == null || stunLoopRoot == null || recoveryWarningRoot == null)
            {
                Debug.LogError(
                    $"{nameof(BossShieldBreakStunVfxController)} requires Break Burst, Stun Loop and Recovery Warning roots.",
                    this);
                return false;
            }

            PlayRoot(breakBurstRoot);
            PlayRoot(stunLoopRoot);
            StopRoot(recoveryWarningRoot);
            isStunPlaying = true;
            return true;
        }

        /// <summary>
        /// 根据长眩晕经过时间启动一次恢复预警；重复调用不会重播或刷新预警。
        /// </summary>
        /// <param name="elapsedTime">当前 ShieldBreakStun 已经过时间，单位秒。</param>
        /// <param name="totalDuration">ShieldBreakStun 总持续时间，单位秒。</param>
        public void TickStun(float elapsedTime, float totalDuration)
        {
            if (!isStunPlaying || isRecoveryWarningPlaying || recoveryWarningRoot == null)
            {
                return;
            }

            float warningStartTime = Mathf.Max(0f, totalDuration - recoveryWarningLeadTime);
            if (elapsedTime < warningStartTime)
            {
                return;
            }

            PlayRoot(recoveryWarningRoot);
            isRecoveryWarningPlaying = true;
        }

        /// <summary>结束或中断长眩晕时立即停止并隐藏全部阶段表现。</summary>
        public void StopStun()
        {
            StopRoot(breakBurstRoot);
            StopRoot(stunLoopRoot);
            StopRoot(recoveryWarningRoot);
            isStunPlaying = false;
            isRecoveryWarningPlaying = false;
        }

        /// <summary>激活表现根，清空旧粒子后递归从头播放，并启用根下非武器灯光。</summary>
        /// <param name="root">需要从头播放的阶段根。</param>
        private static void PlayRoot(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            root.SetActive(true);
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null)
                {
                    continue;
                }

                system.gameObject.SetActive(true);
                system.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                system.Play(true);
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = true;
                }
            }
        }

        /// <summary>停止阶段根下全部粒子，关闭非武器灯光并隐藏根对象。</summary>
        /// <param name="root">需要立即清理的阶段根。</param>
        private static void StopRoot(GameObject root)
        {
            if (root == null)
            {
                return;
            }

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i]?.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            Light[] lights = root.GetComponentsInChildren<Light>(true);
            for (int i = 0; i < lights.Length; i++)
            {
                if (lights[i] != null)
                {
                    lights[i].enabled = false;
                }
            }

            root.SetActive(false);
        }
    }
}
