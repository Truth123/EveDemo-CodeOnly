// 文件说明：维护命中 HitStop 与 PerfectEvade 子弹时间等反馈时间缩放。
// 所属模块：战斗反馈。
// 运行影响：影响命中卡肉、完美闪避慢动作和暂停前反馈时间缩放清理。

using UnityEngine;

namespace ProjectEVE.Feedback
{
    /// <summary>
    /// 轻量反馈时间缩放控制器。使用 unscaled time 恢复 TimeScale，避免反馈持续时间受自身缩放影响。
    /// </summary>
    public sealed class CombatHitStop : MonoBehaviour
    {
        [SerializeField] private bool enableHitStop = true;
        [SerializeField] private float maxDuration = 0.12f;
        [SerializeField] private float minTimeScale = 0.03f;

        private bool hitStopActive;
        private float hitStopRestoreAtUnscaledTime;
        private float hitStopTimeScale = 1f;
        private bool slowMotionActive;
        private float slowMotionHoldUntilUnscaledTime;
        private float slowMotionRecoverUntilUnscaledTime;
        private float slowMotionTimeScale = 1f;
        private float originalTimeScale = 1f;
        private float originalFixedDeltaTime = 0.02f;

        /// <summary>
        /// 执行 Request 相关逻辑，并维护 战斗反馈 模块的运行时一致性。
        /// </summary>
        public void Request(float duration, float timeScale)
        {
            if (!enableHitStop || duration <= 0f)
            {
                return;
            }

            duration = Mathf.Clamp(duration, 0f, maxDuration);
            timeScale = Mathf.Clamp(timeScale, minTimeScale, 1f);

            CaptureOriginalTimeScaleIfNeeded();

            hitStopActive = true;
            hitStopRestoreAtUnscaledTime = Mathf.Max(hitStopRestoreAtUnscaledTime, Time.unscaledTime + duration);
            hitStopTimeScale = Mathf.Min(hitStopTimeScale, timeScale);
            ApplyEffectiveTimeScale();
        }

        /// <summary>
        /// 请求一段短慢动作。用于 PerfectEvade 等规避奖励反馈，不表示命中受击 HitStop。
        /// </summary>
        public void RequestSlowMotion(float duration, float timeScale, float recoverDuration)
        {
            if (duration <= 0f)
            {
                return;
            }

            duration = Mathf.Max(0f, duration);
            timeScale = Mathf.Clamp(timeScale, minTimeScale, 1f);
            recoverDuration = Mathf.Max(0f, recoverDuration);

            CaptureOriginalTimeScaleIfNeeded();

            slowMotionActive = true;
            slowMotionHoldUntilUnscaledTime = Mathf.Max(slowMotionHoldUntilUnscaledTime, Time.unscaledTime + duration);
            slowMotionRecoverUntilUnscaledTime = slowMotionHoldUntilUnscaledTime + recoverDuration;
            slowMotionTimeScale = Mathf.Min(slowMotionTimeScale, timeScale);
            ApplyEffectiveTimeScale();
        }

        /// <summary>
        /// 外部暂停菜单进入前取消未完成时间缩放，避免反馈恢复时覆盖暂停的 TimeScale。
        /// </summary>
        public void CancelActiveHitStop()
        {
            CancelAllTimeDilation();
        }

        /// <summary>
        /// 取消所有反馈时间缩放，恢复进入反馈前的 TimeScale / FixedDeltaTime。
        /// </summary>
        public void CancelAllTimeDilation()
        {
            RestoreOriginalTimeScale(false);
        }

        /// <summary>
        /// 按帧推进运行时逻辑，并刷新依赖的状态、输入或显示数据。
        /// </summary>
        private void Update()
        {
            if (!HasActiveTimeDilation)
            {
                return;
            }

            float now = Time.unscaledTime;
            if (hitStopActive && now >= hitStopRestoreAtUnscaledTime)
            {
                hitStopActive = false;
                hitStopRestoreAtUnscaledTime = 0f;
                hitStopTimeScale = 1f;
            }

            if (slowMotionActive && now >= slowMotionRecoverUntilUnscaledTime)
            {
                slowMotionActive = false;
                slowMotionHoldUntilUnscaledTime = 0f;
                slowMotionRecoverUntilUnscaledTime = 0f;
                slowMotionTimeScale = 1f;
            }

            if (HasActiveTimeDilation)
            {
                ApplyEffectiveTimeScale();
            }
            else
            {
                RestoreOriginalTimeScale(true);
            }
        }

        /// <summary>
        /// 在组件禁用时注销事件、清理临时状态并避免悬挂引用。
        /// </summary>
        private void OnDisable()
        {
            RestoreOriginalTimeScale(false);
        }

        /// <summary>
        /// 是否仍存在未完成的反馈时间缩放。
        /// </summary>
        private bool HasActiveTimeDilation => hitStopActive || slowMotionActive;

        /// <summary>
        /// 首次进入反馈时间缩放时记录原始 TimeScale / FixedDeltaTime。
        /// </summary>
        private void CaptureOriginalTimeScaleIfNeeded()
        {
            if (HasActiveTimeDilation)
            {
                return;
            }

            originalTimeScale = Time.timeScale;
            originalFixedDeltaTime = Time.fixedDeltaTime;
            hitStopTimeScale = 1f;
            slowMotionTimeScale = 1f;
        }

        /// <summary>
        /// 按 HitStop 优先、慢动作恢复曲线计算当前有效 TimeScale。
        /// </summary>
        private void ApplyEffectiveTimeScale()
        {
            if (originalTimeScale <= 0f)
            {
                Time.timeScale = 0f;
                Time.fixedDeltaTime = originalFixedDeltaTime;
                return;
            }

            float effectiveTimeScale = originalTimeScale;
            if (slowMotionActive)
            {
                effectiveTimeScale = EvaluateSlowMotionTimeScale(Time.unscaledTime);
            }

            if (hitStopActive)
            {
                effectiveTimeScale = Mathf.Min(effectiveTimeScale, hitStopTimeScale);
            }

            Time.timeScale = effectiveTimeScale;
            Time.fixedDeltaTime = originalFixedDeltaTime * Time.timeScale;
        }

        /// <summary>
        /// 计算子弹时间当前比例。恢复段从慢动作比例平滑回到进入反馈前的 TimeScale。
        /// </summary>
        private float EvaluateSlowMotionTimeScale(float now)
        {
            if (now <= slowMotionHoldUntilUnscaledTime ||
                slowMotionRecoverUntilUnscaledTime <= slowMotionHoldUntilUnscaledTime)
            {
                return slowMotionTimeScale;
            }

            float t = Mathf.InverseLerp(
                slowMotionHoldUntilUnscaledTime,
                slowMotionRecoverUntilUnscaledTime,
                now);
            return Mathf.Lerp(slowMotionTimeScale, originalTimeScale, t);
        }

        /// <summary>
        /// 恢复原始时间设置，并清理所有反馈时间缩放运行时状态。
        /// </summary>
        private void RestoreOriginalTimeScale(bool force)
        {
            if (!force && !HasActiveTimeDilation)
            {
                return;
            }

            Time.timeScale = originalTimeScale;
            Time.fixedDeltaTime = originalFixedDeltaTime;
            hitStopActive = false;
            hitStopRestoreAtUnscaledTime = 0f;
            hitStopTimeScale = 1f;
            slowMotionActive = false;
            slowMotionHoldUntilUnscaledTime = 0f;
            slowMotionRecoverUntilUnscaledTime = 0f;
            slowMotionTimeScale = 1f;
        }
    }
}
