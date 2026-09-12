// 文件说明：统一维护 Raven Boss 黄光不可格挡、1 秒红光与 0.53 秒红光的预警表现层绑定。
// 所属模块：Boss 战斗表现。
// 运行影响：只播放攻击提示粒子和灯光，不影响 HitNode 检测、伤害、防御解析或位移。

using System.Collections;
using UnityEngine;
using UnityEngine.Serialization;

namespace ProjectEVE.Boss.Combat
{
    /// <summary>
    /// Boss 攻击预警控制器。响应 Animation Event 播放黄光或两种时长的红光一次性提示，
    /// 并保证三种提示不会同时显示。
    /// </summary>
    public sealed class BossAttackCueVfxController : MonoBehaviour
    {
        /// <summary>不可格挡攻击使用的黄光提示根对象。</summary>
        [FormerlySerializedAs("effectRoot")]
        [SerializeField] private GameObject yellowEffectRoot;
        /// <summary>可防御连续猛攻使用的红光提示根对象。</summary>
        [SerializeField] private GameObject redEffectRoot;
        /// <summary>需要快速完成前摇时使用的 0.53 秒红光提示根对象。</summary>
        [SerializeField] private GameObject acceleratedRedEffectRoot;

        private GameObject activeRoot;
        private Coroutine stopCueRoutine;

        /// <summary>当前是否有任意一种攻击提示处于播放状态。</summary>
        public bool IsCuePlaying => activeRoot != null && activeRoot.activeSelf;
        /// <summary>当前正在显示的提示根；没有提示时为 null。</summary>
        public GameObject ActiveRoot => activeRoot;

        /// <summary>对象唤醒时清空两种提示，避免场景加载后自动播放。</summary>
        private void Awake()
        {
            StopRoot(yellowEffectRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
            StopRoot(redEffectRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
            StopRoot(acceleratedRedEffectRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>组件禁用时清理当前提示，避免攻击中断后残留粒子、Trail 或灯光。</summary>
        private void OnDisable()
        {
            StopCue();
        }

        /// <summary>
        /// 绑定黄光提示根并立即清空。主要供场景安装与 EditMode 测试使用。
        /// </summary>
        /// <param name="nextEffectRoot">不可格挡攻击使用的黄光提示根对象。</param>
        public void BindYellowEffectRoot(GameObject nextEffectRoot)
        {
            if (activeRoot == yellowEffectRoot)
            {
                StopCue();
            }

            yellowEffectRoot = nextEffectRoot;
            StopRoot(yellowEffectRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 绑定红光提示根并立即清空。主要供场景安装与 EditMode 测试使用。
        /// </summary>
        /// <param name="nextEffectRoot">连续猛攻使用的红光提示根对象。</param>
        public void BindRedEffectRoot(GameObject nextEffectRoot)
        {
            if (activeRoot == redEffectRoot)
            {
                StopCue();
            }

            redEffectRoot = nextEffectRoot;
            StopRoot(redEffectRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 绑定 0.53 秒红光提示根并立即清空。主要供场景安装与 EditMode 测试使用。
        /// </summary>
        /// <param name="nextEffectRoot">快速完成前摇时使用的红光提示根对象。</param>
        public void BindAcceleratedRedEffectRoot(GameObject nextEffectRoot)
        {
            if (activeRoot == acceleratedRedEffectRoot)
            {
                StopCue();
            }

            acceleratedRedEffectRoot = nextEffectRoot;
            StopRoot(
                acceleratedRedEffectRoot,
                ParticleSystemStopBehavior.StopEmittingAndClear);
        }

        /// <summary>
        /// 从头播放黄光不可格挡提示；播放前会停止红光或旧黄光提示。
        /// </summary>
        /// <returns>true 表示黄光根存在且已开始播放；false 表示缺少绑定。</returns>
        public bool PlayYellowCue()
        {
            return PlayCue(yellowEffectRoot, "Yellow Effect Root", nameof(PlayYellowCue));
        }

        /// <summary>
        /// 从头播放红光连续猛攻提示；播放前会停止黄光或旧红光提示。
        /// </summary>
        /// <returns>true 表示红光根存在且已开始播放；false 表示缺少绑定。</returns>
        public bool PlayRedCue()
        {
            return PlayCue(redEffectRoot, "Red Effect Root", nameof(PlayRedCue));
        }

        /// <summary>
        /// 从头播放 0.53 秒红光提示；播放前会停止黄光、1 秒红光或旧的快速红光提示。
        /// </summary>
        /// <returns>true 表示快速红光根存在且已开始播放；false 表示缺少绑定。</returns>
        public bool PlayAcceleratedRedCue()
        {
            return PlayCue(
                acceleratedRedEffectRoot,
                "Accelerated Red Effect Root",
                nameof(PlayAcceleratedRedCue));
        }

        /// <summary>停止当前提示并立即隐藏对应根对象。</summary>
        public void StopCue()
        {
            if (stopCueRoutine != null)
            {
                StopCoroutine(stopCueRoutine);
                stopCueRoutine = null;
            }

            if (activeRoot != null)
            {
                StopRoot(activeRoot, ParticleSystemStopBehavior.StopEmittingAndClear);
            }

            activeRoot = null;
        }

        /// <summary>
        /// 校验指定根，停止已有提示并启动一次性播放。
        /// </summary>
        /// <param name="root">本次需要播放的提示根对象。</param>
        /// <param name="bindingName">缺少绑定时写入错误信息的字段语义名称。</param>
        /// <param name="entryPointName">调用本次播放的公开入口名称。</param>
        /// <returns>true 表示根存在且已播放；false 表示缺少配置。</returns>
        private bool PlayCue(GameObject root, string bindingName, string entryPointName)
        {
            StopCue();
            if (root == null)
            {
                Debug.LogError(
                    $"{nameof(BossAttackCueVfxController)} requires {bindingName} for {entryPointName}.",
                    this);
                return false;
            }

            PlayRoot(root);
            activeRoot = root;

            float visibleDuration = CalculateVisibleDuration(root);
            if (Application.isPlaying && isActiveAndEnabled && visibleDuration > 0f)
            {
                stopCueRoutine = StartCoroutine(StopCueAfterDelay(visibleDuration));
            }

            return true;
        }

        /// <summary>
        /// 等待当前提示的最大起播延迟与生命周期结束，再执行统一清理。
        /// </summary>
        /// <param name="delay">提示保持可见的缩放时间，单位秒。</param>
        private IEnumerator StopCueAfterDelay(float delay)
        {
            yield return new WaitForSeconds(delay);
            stopCueRoutine = null;
            StopCue();
        }

        /// <summary>
        /// 读取根对象下全部粒子的最大起播延迟与生命周期，并换算各系统的模拟速度。
        /// </summary>
        /// <param name="root">包含一次性粒子系统的提示根对象。</param>
        /// <returns>最后一个粒子理论结束的时间，单位秒；没有粒子时返回 0。</returns>
        private static float CalculateVisibleDuration(GameObject root)
        {
            if (root == null)
            {
                return 0f;
            }

            float visibleDuration = 0f;
            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                ParticleSystem system = systems[i];
                if (system == null)
                {
                    continue;
                }

                ParticleSystem.MainModule main = system.main;
                float simulationSpeed = Mathf.Max(0.0001f, main.simulationSpeed);
                visibleDuration = Mathf.Max(
                    visibleDuration,
                    (main.startDelay.constantMax + main.startLifetime.constantMax) /
                    simulationSpeed);
            }

            return visibleDuration;
        }

        /// <summary>激活根对象，清除旧粒子后递归播放，并开启其子灯光。</summary>
        /// <param name="root">本次需要从头播放的提示根对象。</param>
        private static void PlayRoot(GameObject root)
        {
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

        /// <summary>停止根对象下的粒子，关闭子灯光并隐藏根对象。</summary>
        /// <param name="root">本次需要结束的提示根对象。</param>
        /// <param name="stopBehavior">粒子停止时保留现有粒子或立即清空的策略。</param>
        private static void StopRoot(GameObject root, ParticleSystemStopBehavior stopBehavior)
        {
            if (root == null)
            {
                return;
            }

            ParticleSystem[] systems = root.GetComponentsInChildren<ParticleSystem>(true);
            for (int i = 0; i < systems.Length; i++)
            {
                systems[i]?.Stop(true, stopBehavior);
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
